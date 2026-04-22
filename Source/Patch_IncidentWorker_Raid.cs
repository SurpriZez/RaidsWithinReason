using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    public static class Patch_IncidentWorker_Raid_TryExecute
    {
        // Written by Prefix, read by Patch_LordMaker_MakeNewLord, cleared by Postfix.
        internal static RaidGoalDef _pendingGoal;
        internal static Faction     _pendingFaction;
        // Set by debug actions to skip interception and evaluator for a single raid.
        internal static bool        _debugForceGoal;
        // Set by timers to ensure they don't get intercepted by another negotiation attempt.
        internal static bool        _skipInterception;
        // The specific pawn to target (e.g. for arrest rescue)
        internal static Pawn        _pendingTarget;

        [HarmonyPrefix]
        public static bool Prefix(IncidentWorker __instance, IncidentParms parms, ref bool __result)
        {
            if (!(__instance is IncidentWorker_Raid)) return true;

            // Goal already set or explicitly requested to skip mod interception.
            if (_debugForceGoal || _skipInterception || _pendingGoal != null) return true;

            Map     map     = parms.target as Map;
            Faction faction = parms.faction;

            // Redirect qualifying raids to a negotiation attempt.
            if (ShouldIntercept(faction, map))
            {
                IncidentDef negotiatorDef = DefDatabase<IncidentDef>.GetNamedSilentFail("RWR_NegotiatorArrival");
                if (negotiatorDef != null)
                {
                    bool success = (bool)Traverse.Create(negotiatorDef.Worker).Method("TryExecuteWorker", parms).GetValue();
                    if (success)
                    {
                        Current.Game.GetComponent<NegotiatorCooldownComponent>()
                               ?.RecordNegotiatorSent(faction);
                        __result = true;
                        return false; // cancel the raid incident COMPLETELY (bypasses SendStandardLetter)
                    }
                }
                // Negotiator failed to spawn — fall through to normal raid.
            }

            if (map == null || faction == null) return true;

            // Chaotic raid: skip goal assignment entirely and let vanilla behaviour handle it.
            if (Rand.Chance(RWR_Mod.Settings.chaoticRaidChance)) return true;

            _pendingGoal    = RaidGoalEvaluator.SelectGoal(parms, faction, map);
            _pendingFaction = faction;
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(bool __result, IncidentParms parms)
        {
            RaidGoalDef goal = _pendingGoal;
            _pendingGoal    = null;
            _pendingFaction = null;

            if (!__result || goal == null) return;

            Map     map     = parms.target as Map;
            Faction faction = parms.faction;
            if (map == null || faction == null) return;

            Lord raidLord = map.lordManager?.lords.LastOrDefault(l => l?.faction == faction);
            if (raidLord != null && map.GetComponent<RaidGoalTracker>()?.GetGoal(raidLord) != null)
            {
                Pawn targetPawn = null;
                if (goal.goalType == RaidGoalType.Revenge)
                {
                    targetPawn = ColonyStateReader.GetRandomNotableColonist(map);
                    if (targetPawn != null)
                        map.GetComponent<RaidGoalTracker>().SetTargetPawn(raidLord, targetPawn);
                }
                else if (goal.goalType == RaidGoalType.Capture)
                {
                    targetPawn = ColonyStateReader.GetRandomNotableColonist(map);
                    if (targetPawn != null)
                        map.GetComponent<RaidGoalTracker>().SetTargetPawn(raidLord, targetPawn);
                }

                if (RWR_Mod.Settings.enableGoalLetters)
                {
                    var letter = new ChoiceLetter_RaidGoalAnnouncement
                    {
                        def            = DefDatabase<LetterDef>.GetNamed("RWR_RaidGoalAnnouncement"),
                        Label          = $"{faction.Name} raids with purpose",
                        Text           = BuildLetterText(faction, goal, targetPawn),
                        lookTargets    = targetPawn != null ? new LookTargets(targetPawn) : new LookTargets(map.Parent),
                        relatedFaction = faction,
                    };
                    Find.LetterStack.ReceiveLetter(letter);
                }
            }
        }

        // Returns true if the raid should be redirected to a negotiation attempt.
        private static bool ShouldIntercept(Faction faction, Map map)
        {
            if (faction == null || map == null) return false;
            if (!faction.def.humanlikeFaction) return false; // mechanoids and insects don't negotiate
            if (!Rand.Chance(RWR_Mod.Settings.negotiationChance)) return false;

            var cooldown = Current.Game.GetComponent<NegotiatorCooldownComponent>();
            return cooldown == null || !cooldown.IsOnCooldown(faction);
        }

        private static string BuildLetterText(Faction faction, RaidGoalDef goal, Pawn target = null)
        {
            bool effectiveRetreat = goal.retreatOnSuccess && RWR_Mod.Settings.enableRetreatOnSuccess;
            string retreatLine = effectiveRetreat
                ? "If the raiders achieve their objective they will retreat — stopping them before that will send them fleeing."
                : "The raiders will not retreat even if they achieve their goal. Expect a sustained assault.";

            string desc = goal.targetDescription;
            if (goal.goalType == RaidGoalType.Revenge && target != null)
            {
                desc = $"kill {target.NameShortColored.Resolve()} in retaliation";
            }
            else if (goal.goalType == RaidGoalType.Capture && target != null)
            {
                desc = $"capture {target.NameShortColored.Resolve()}";
            }

            return $"Raiders from {faction.Name} have arrived with a specific objective:\n\n" +
                   $"\"{desc}\"\n\n" +
                   retreatLine;
        }
    }

    // Intercepts LordMaker.MakeNewLord to register the goal on allowed raid types.
    [HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord))]
    public static class Patch_LordMaker_MakeNewLord
    {
        [HarmonyPostfix]
        public static void Postfix(Lord __result, Map map, Faction faction)
        {
            if (__result?.LordJob == null || map == null) return;

            RaidGoalDef pending = Patch_IncidentWorker_Raid_TryExecute._pendingGoal;
            
            // If spawned via certain debug actions or custom mods, the overarching
            // IncidentWorker may not have cached a pending goal. We generate a fallback here.
            if (pending == null && faction != null && faction.HostileTo(Faction.OfPlayer))
            {
                if (!Rand.Chance(RWR_Mod.Settings.chaoticRaidChance) && faction.def.humanlikeFaction)
                {
                    pending = RaidGoalEvaluator.SelectGoal(null, faction, map);
                }
            }

            if (pending == null) return;

            string jobName = __result.LordJob.GetType().Name;
            if (jobName == "LordJob_AssaultColony" || 
                jobName == "LordJob_StageThenAttack" || 
                jobName == "LordJob_Siege")
            {
                map.GetComponent<RaidGoalTracker>()?.SetGoal(__result, pending);
            }
        }
    }

    // Persists the assigned goal within the LordJob itself cleanly so that the objective survives save/load
    // before cross-references resolve.
    [HarmonyPatch]
    public static class Patch_LordJob_ExposeData
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LordJob_AssaultColony), nameof(LordJob_AssaultColony.ExposeData));
            yield return AccessTools.Method(typeof(LordJob_StageThenAttack), nameof(LordJob_StageThenAttack.ExposeData));
            yield return AccessTools.Method(typeof(LordJob_Siege), nameof(LordJob_Siege.ExposeData));
        }

        public static Dictionary<LordJob, RaidGoalDef> runtimeGoals = new Dictionary<LordJob, RaidGoalDef>();

        public static void Postfix(LordJob __instance)
        {
            string jobName = __instance.GetType().Name;
            if (jobName != "LordJob_AssaultColony" && 
                jobName != "LordJob_StageThenAttack" && 
                jobName != "LordJob_Siege") return;

            RaidGoalDef goal = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (__instance.lord != null)
                    goal = __instance.lord.Map?.GetComponent<RaidGoalTracker>()?.GetGoal(__instance.lord);
            }

            Scribe_Defs.Look(ref goal, "rwr_dynamicGoal");

            if (Scribe.mode == LoadSaveMode.LoadingVars && goal != null)
            {
                runtimeGoals[__instance] = goal;
            }
        }
    }

    // Dynamically rewrites the StateGraph of humanoid raids to inject the goal pursuit toil
    // just before their standard assault phase.
    [HarmonyPatch]
    public static class Patch_LordJob_CreateGraph
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LordJob_AssaultColony), nameof(LordJob_AssaultColony.CreateGraph));
            yield return AccessTools.Method(typeof(LordJob_StageThenAttack), nameof(LordJob_StageThenAttack.CreateGraph));
            yield return AccessTools.Method(typeof(LordJob_Siege), nameof(LordJob_Siege.CreateGraph));
        }

        public static void Postfix(LordJob __instance, ref StateGraph __result)
        {
            string jobName = __instance.GetType().Name;
            if (jobName != "LordJob_AssaultColony" && 
                jobName != "LordJob_StageThenAttack" && 
                jobName != "LordJob_Siege") return;

            RaidGoalDef goal = null;

            // Fresh raid execution: pull from the pending goal cache
            if (Patch_IncidentWorker_Raid_TryExecute._pendingGoal != null)
                goal = Patch_IncidentWorker_Raid_TryExecute._pendingGoal;
            // Existing raid being loaded: pull from the ExposeData cache
            else
                Patch_LordJob_ExposeData.runtimeGoals.TryGetValue(__instance, out goal);

            if (goal == null) return;

            // Find an appropriate assault toil in the graph
            LordToil assaultToil = __result.lordToils.FirstOrDefault(t => 
                t.GetType().Name.Contains("AssaultColony") || 
                t.GetType().Name == "LordToil_AssaultColonyBreaching");
            
            if (assaultToil == null && __result.lordToils.Count > 0)
                assaultToil = __result.lordToils[0]; // fallback to the starting toil
            
            if (assaultToil == null) return;

            var pursueToil = new LordToil_PursueGoal(goal);
            int index = __result.lordToils.IndexOf(assaultToil);
            __result.lordToils.Insert(index, pursueToil);

            // Redirect transitions aiming at the assault toil to point to the pursue toil
            foreach (var trans in __result.transitions)
            {
                if (trans.target == assaultToil)
                    trans.target = pursueToil;
            }

            // Create transitions from PursueToil back to original or Exit
            bool effectiveRetreat = goal.retreatOnSuccess && RWR_Mod.Settings.enableRetreatOnSuccess;
            LordToil successDest = effectiveRetreat ? new LordToil_ExitMapGoalAchieved() : assaultToil;

            if (effectiveRetreat)
                __result.lordToils.Add(successDest);

            var successTrans = new Transition(pursueToil, successDest);
            successTrans.triggers.Add(new Trigger_Memo("RWR_GoalSucceeded"));
            __result.transitions.Add(successTrans);

            // Casualties fall back to standard flee
            if (__result.lordToils.Count > 0)
            {
                LordToil fleeDest = __result.lordToils.Last();
                if (fleeDest != pursueToil && fleeDest != successDest)
                {
                    var casualtyTrans = new Transition(pursueToil, fleeDest);
                    casualtyTrans.triggers.Add(new Trigger_FractionPawnsLost(0.3f));
                    __result.transitions.Add(casualtyTrans);
                }
            }
        }
    }

    // Single-button choice letter for one-way raid goal announcements.
    public class ChoiceLetter_RaidGoalAnnouncement : ChoiceLetter
    {
        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                var opt = new DiaOption("Understood");
                opt.resolveTree = true;
                opt.action      = () => Find.LetterStack.RemoveLetter(this);
                yield return opt;
            }
        }
    }
}
