using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    [HarmonyPatch(typeof(IncidentWorker_Raid), "TryExecuteWorker")]
    public static class Patch_IncidentWorker_Raid_TryExecuteWorker
    {
        // Written by Prefix, read by Patch_LordMaker_MakeNewLord, cleared by Postfix.
        internal static RaidGoalDef _pendingGoal;
        internal static Faction     _pendingFaction;
        // Set by debug actions to skip interception and evaluator for a single raid.
        internal static bool        _debugForceGoal;

        [HarmonyPrefix]
        public static bool Prefix(IncidentParms parms)
        {
            // Debug path: goal already set by caller, skip all normal logic.
            if (_debugForceGoal) return true;

            Map     map     = parms.target as Map;
            Faction faction = parms.faction;

            // Redirect qualifying raids to a negotiation attempt.
            if (ShouldIntercept(faction, map))
            {
                IncidentDef negotiatorDef =
                    DefDatabase<IncidentDef>.GetNamedSilentFail("RWR_NegotiatorArrival");

                if (negotiatorDef?.Worker.TryExecute(parms) == true)
                {
                    Current.Game.GetComponent<NegotiatorCooldownComponent>()
                           ?.RecordNegotiatorSent(faction);
                    return false; // cancel the raid
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

            if (!RWR_Mod.Settings.enableGoalLetters) return;

            Lord raidLord = map.lordManager?.lords.LastOrDefault(l => l?.faction == faction);
            if (raidLord?.LordJob is LordJob_GoalRaid)
            {
                var letter = new ChoiceLetter_RaidGoalAnnouncement
                {
                    def            = DefDatabase<LetterDef>.GetNamed("RWR_RaidGoalAnnouncement"),
                    Label          = $"{faction.Name} raids with purpose",
                    Text           = BuildLetterText(faction, goal),
                    lookTargets    = new LookTargets(map.Parent),
                    relatedFaction = faction,
                };
                Find.LetterStack.ReceiveLetter(letter);
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

        private static string BuildLetterText(Faction faction, RaidGoalDef goal)
        {
            bool effectiveRetreat = goal.retreatOnSuccess && RWR_Mod.Settings.enableRetreatOnSuccess;
            string retreatLine = effectiveRetreat
                ? "If the raiders achieve their objective they will retreat — stopping them before that will send them fleeing."
                : "The raiders will not retreat even if they achieve their goal. Expect a sustained assault.";

            return $"Raiders from {faction.Name} have arrived with a specific objective:\n\n" +
                   $"\"{goal.targetDescription}\"\n\n" +
                   retreatLine;
        }
    }

    // Intercepts LordMaker.MakeNewLord to substitute LordJob_GoalRaid when a pending goal exists.
    [HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord))]
    public static class Patch_LordMaker_MakeNewLord
    {
        [HarmonyPrefix]
        public static void Prefix(ref LordJob lordJob)
        {
            RaidGoalDef pending = Patch_IncidentWorker_Raid_TryExecuteWorker._pendingGoal;
            Faction     faction = Patch_IncidentWorker_Raid_TryExecuteWorker._pendingFaction;
            if (pending == null || faction == null) return;
            if (lordJob == null || lordJob.GetType() != typeof(LordJob_AssaultColony)) return;

            var t = Traverse.Create(lordJob);
            bool canKidnap         = t.Field("canKidnap").GetValue<bool>();
            bool canTimeoutOrFlee  = t.Field("canTimeoutOrFlee").GetValue<bool>();
            bool sappers           = t.Field("sappers").GetValue<bool>();
            bool useAvoidGridSmart = t.Field("useAvoidGridSmart").GetValue<bool>();
            bool canSteal          = t.Field("canSteal").GetValue<bool>();

            lordJob = new LordJob_GoalRaid(pending, faction,
                canKidnap && pending.goalType != RaidGoalType.Revenge,
                canTimeoutOrFlee, sappers, useAvoidGridSmart, canSteal);
        }

        [HarmonyPostfix]
        public static void Postfix(Lord __result, Map map)
        {
            RaidGoalDef pending = Patch_IncidentWorker_Raid_TryExecuteWorker._pendingGoal;
            if (__result == null || pending == null || map == null) return;
            if (__result.LordJob is LordJob_GoalRaid)
                map.GetComponent<RaidGoalTracker>()?.SetGoal(__result, pending);
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
