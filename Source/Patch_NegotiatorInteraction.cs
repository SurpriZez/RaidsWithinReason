using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    // Prevents colonist AI from auto-targeting the negotiator pawn.
    // The player can still manually order an attack, with consequences.
    [HarmonyPatch(typeof(Pawn), "ThreatDisabled")]
    public static class Patch_Pawn_ThreatDisabled_Negotiator
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            if (NegotiatorUtil.IsNegotiator(__instance))
                __result = true;
        }
    }

    // Trigger immediate retaliation if the negotiator is murdered.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_Negotiator
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance)
        {
            if (NegotiatorUtil.IsNegotiator(__instance))
            {
                Faction faction = __instance.Faction;
                Map     map     = __instance.Map;
                if (faction != null && map != null)
                {
                    Messages.Message($"Negotiator murdered! {faction.Name} is launching an immediate retaliatory strike!", MessageTypeDefOf.ThreatBig);
                    
                    RaidGoalDef goal = DefDatabase<RaidGoalDef>.GetNamedSilentFail("RaidGoal_Revenge");
                    NegotiatorUtil.TriggerImmediateRaid(faction, map, goal);
                }
            }
        }
    }

    // Trigger immediate rescue raid if the negotiator is arrested.
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class Patch_Pawn_GuestTracker_SetGuestStatus
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn_GuestTracker __instance, Faction newHost, GuestStatus guestStatus)
        {
            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn != null && NegotiatorUtil.IsNegotiator(pawn) && guestStatus == GuestStatus.Prisoner)
            {
                Faction faction = pawn.Faction;
                Map     map     = pawn.Map;
                Log.Message($"[RWR] Arrest detected on negotiator {pawn.LabelShort}. Faction: {faction?.Name}, Hostile: {faction?.HostileTo(Faction.OfPlayer)}");
                if (faction != null && map != null && faction.HostileTo(Faction.OfPlayer))
                {
                    Messages.Message($"Negotiator arrested! {faction.Name} is launching a rescue operation!", MessageTypeDefOf.ThreatBig);
                    
                    RaidGoalDef goal = DefDatabase<RaidGoalDef>.GetNamedSilentFail("RaidGoal_ReleasePrisoner");
                    NegotiatorUtil.TriggerImmediateRaid(faction, map, goal, 1.2f, pawn);
                }
            }
        }
    }

    // Injects right-click options onto the negotiator pawn itself via Thing.GetFloatMenuOptions,
    // which FloatMenuMakerMap calls for each thing at the clicked cell.
    [HarmonyPatch(typeof(Thing), nameof(Thing.GetFloatMenuOptions))]
    public static class Patch_FloatMenu_Negotiator
    {
        [HarmonyPostfix]
        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> __result,
                                                           Thing __instance, Pawn selPawn)
        {
            foreach (FloatMenuOption opt in __result)
                yield return opt;

            if (!selPawn.IsColonistPlayerControlled) yield break;
            if (!(__instance is Pawn target) || !NegotiatorUtil.IsNegotiator(target)) yield break;

            var visitJob = target.GetLord()?.LordJob as LordJob_NegotiatorVisit;
            if (visitJob?.request == null) yield break;

            QuestPart_RequireDelivery part = FindDeliveryPart(visitJob.request);
            if (part == null || part.completed) yield break;

            FloatMenuOption payOpt = BuildOption(target, visitJob.request, part, target.Map);
            if (payOpt != null) yield return payOpt;
        }

        private static FloatMenuOption BuildOption(Pawn negotiator, NegotiationRequest request,
                                                   QuestPart_RequireDelivery part, Map map)
        {
            if (request.template.demandType == NegotiationDemandType.Pawn)
                return BuildPrisonerOption(negotiator, part, map);

            ThingDef thingDef = request.thingDef;
            int available = map.resourceCounter.GetCount(thingDef);
            int required  = request.amount;

            string label = $"Pay {request.TargetDescription} to negotiator ({available} available)";

            if (available < required)
                return new FloatMenuOption(label + " — not enough", null);

            return new FloatMenuOption(label, () =>
            {
                ConsumeFromMap(map, thingDef, required);
                part.ForceComplete();
                negotiator.GetLord()?.ReceiveMemo("NegotiatorDismissed");
                Messages.Message(
                    "Payment delivered. The negotiator is satisfied.",
                    MessageTypeDefOf.PositiveEvent);
            });
        }

        private static FloatMenuOption BuildPrisonerOption(Pawn negotiator,
                                                           QuestPart_RequireDelivery part, Map map)
        {
            Pawn prisoner = part.requiredPrisoner;
            if (prisoner == null || prisoner.Dead || prisoner.Destroyed)
                return new FloatMenuOption("Required prisoner is no longer available", null);
            if (!prisoner.IsPrisonerOfColony)
                return new FloatMenuOption("Prisoner already released", null);

            return new FloatMenuOption($"Hand over {prisoner.LabelShort} to negotiator", () =>
            {
                if (NegotiatorUtil.HandoverPrisoner(prisoner, negotiator, map))
                {
                    part.ForceComplete();
                    negotiator.GetLord()?.ReceiveMemo("NegotiatorDismissed");
                }
            });
        }

        private static QuestPart_RequireDelivery FindDeliveryPart(NegotiationRequest request)
        {
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.State != QuestState.Ongoing) continue;
                foreach (QuestPart p in quest.PartsListForReading)
                {
                    if (p is QuestPart_RequireDelivery d && d.request != null && 
                        d.request.template == request.template && 
                        d.request.thingDef == request.thingDef && !d.completed)
                        return d;
                }
            }
            return null;
        }

        private static void ConsumeFromMap(Map map, ThingDef def, int amount)
        {
            int remaining = amount;
            foreach (Thing thing in map.listerThings.ThingsOfDef(def).ToList())
            {
                if (remaining <= 0) break;
                int take = Mathf.Min(remaining, thing.stackCount);
                thing.stackCount -= take;
                remaining -= take;
                if (thing.stackCount <= 0)
                    thing.Destroy(DestroyMode.Vanish);
            }
        }
    }

    internal static class NegotiatorUtil
    {
        private static HediffDef _hediffDef;

        internal static bool IsNegotiator(Pawn pawn)
        {
            if (_hediffDef == null)
                _hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("RWR_Negotiator");
            return _hediffDef != null && (pawn.health?.hediffSet?.HasHediff(_hediffDef) ?? false);
        }

        internal static void ScheduleRetaliation(Faction faction, Map map, RaidGoalDef goal = null)
        {
            if (faction == null || map == null) return;

            // Avoid duplicate retaliation quests for the same faction
            foreach (var existing in Find.QuestManager.QuestsListForReading)
            {
                if (existing.State == QuestState.Ongoing)
                {
                    // Identify retaliation quest: has a timer for THIS faction but no delivery requirement
                    bool isRetaliation = existing.PartsListForReading.Any(p => p is QuestPart_TimerExpiry timer && timer.faction == faction) && 
                                         !existing.PartsListForReading.Any(p => p is QuestPart_RequireDelivery);
                    if (isRetaliation) return;
                }
            }

            int delayTicks = Rand.Range(GenDate.TicksPerDay, 4 * GenDate.TicksPerDay);

            RimWorld.QuestGen.Slate slate = new RimWorld.QuestGen.Slate();
            slate.Set("faction",    faction);
            slate.Set("map",        map);
            slate.Set("delayTicks", delayTicks);
            slate.Set("goal",       goal);

            Quest quest = RimWorld.QuestGen.QuestGen.Generate(
                DefDatabase<QuestScriptDef>.GetNamed("RWR_RetaliationTimer"), slate);
            Find.QuestManager.Add(quest);
            quest.Accept(null);
        }

        internal static bool HandoverPrisoner(Pawn prisoner, Pawn negotiator, Map map)
        {
            if (prisoner == null || negotiator == null || map == null) return false;

            // Use the native release system
            GenGuest.PrisonerRelease(prisoner);
            
            // Add them to the negotiator group
            negotiator.GetLord()?.AddPawn(prisoner);

            GenPlace.TryPlaceThing(prisoner, negotiator.Position, map, ThingPlaceMode.Near);
            
            // Ensure they leave peacefully (clearing combat targets and setting duty)
            prisoner.mindState.enemyTarget = null;
            prisoner.mindState.duty = new Verse.AI.PawnDuty(DutyDefOf.ExitMapBest);

            Messages.Message($"{prisoner.LabelShort} has been handed over and released.", MessageTypeDefOf.PositiveEvent);
            return true;
        }

        internal static void TriggerImmediateRaid(Faction faction, Map map, RaidGoalDef goal, float pointsMultiplier = 1f, Pawn target = null)
        {
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = faction;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            parms.points *= pointsMultiplier;
            
            // This global state is picked up by the Raid Incident Patch to assign the goal
            Patch_IncidentWorker_Raid_TryExecuteWorker._pendingGoal    = goal;
            Patch_IncidentWorker_Raid_TryExecuteWorker._pendingFaction = faction;
            Patch_IncidentWorker_Raid_TryExecuteWorker._pendingTarget  = target;
            
            try
            {
                bool success = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
                Log.Message($"[RWR] TriggerImmediateRaid execution result: {success} for goal: {goal?.defName}");
            }
            finally
            {
                Patch_IncidentWorker_Raid_TryExecuteWorker._pendingGoal    = null;
                Patch_IncidentWorker_Raid_TryExecuteWorker._pendingFaction = null;
                Patch_IncidentWorker_Raid_TryExecuteWorker._pendingTarget  = null;
            }
        }
        internal static int ConsumeResources(Map map, ThingDef def, int amount)
        {
            if (def == null || amount <= 0) return 0;

            int remaining = amount;
            // Get all items of this type on the map, prioritizing those in stockpiles/storage
            var candidates = map.listerThings.ThingsOfDef(def)
                .Where(t => !t.IsForbidden(Faction.OfPlayer))
                .OrderByDescending(t => t.Position.GetSlotGroup(map) != null || t.ParentHolder is Building_Storage)
                .ToList();

            foreach (var thing in candidates)
            {
                if (remaining <= 0) break;

                int toTake = Mathf.Min(thing.stackCount, remaining);
                if (toTake >= thing.stackCount)
                {
                    remaining -= thing.stackCount;
                    thing.Destroy();
                }
                else
                {
                    thing.stackCount -= toTake;
                    remaining -= toTake;
                }
            }

            return amount - remaining; // return total consumed
        }
    }
}
