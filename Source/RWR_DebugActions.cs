using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    public static class RWR_DebugActions
    {
        // ── Negotiator ───────────────────────────────────────────────────────

        [DebugAction("RaidsWithinReason", "Force negotiator arrival", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceNegotiatorArrival()
        {
            Map map = Find.CurrentMap;
            IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail("RWR_NegotiatorArrival");
            if (def == null) { Log.Error("[RWR] RWR_NegotiatorArrival IncidentDef not found."); return; }

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.Misc, map);
            parms.faction = Find.FactionManager.RandomEnemyFaction(allowHidden: false, allowDefeated: false, allowNonHumanlike: false);
            if (!def.Worker.TryExecute(parms))
                Log.Warning("[RWR] Negotiator arrival failed to execute (no entry cell or no demand?).");
        }

        [DebugAction("RaidsWithinReason", "Finish retaliation timer instantly", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FinishRetaliationTimer()
        {
            var quests = Find.QuestManager.QuestsListForReading;
            bool any = false;
            foreach (var quest in quests)
            {
                if (quest.State != QuestState.Ongoing) continue;
                
                // Identify retaliation quest: has a timer but no delivery requirement
                bool isRetaliation = quest.PartsListForReading.Any(p => p is QuestPart_TimerExpiry) && 
                                     !quest.PartsListForReading.Any(p => p is QuestPart_RequireDelivery);

                if (isRetaliation)
                {
                    foreach (var part in quest.PartsListForReading)
                    {
                        if (part is QuestPart_TimerExpiry timer)
                        {
                            timer.expiryTick = Find.TickManager.TicksGame;
                            any = true;
                        }
                    }
                }
            }
            if (!any) Log.Warning("[RWR] No active retaliation timer quest found.");
        }

        // ── Raids with specific goals ────────────────────────────────────────

        [DebugAction("RaidsWithinReason", "Force raid: Loot", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceRaidLoot() => ForceRaidWithGoal("RaidGoal_Loot");

        [DebugAction("RaidsWithinReason", "Force raid: Capture", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceRaidCapture() => ForceRaidWithGoal("RaidGoal_Capture");

        [DebugAction("RaidsWithinReason", "Force raid: Destroy", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceRaidDestroy() => ForceRaidWithGoal("RaidGoal_Destroy");

        [DebugAction("RaidsWithinReason", "Force raid: Revenge", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceRaidRevenge() => ForceRaidWithGoal("RaidGoal_Revenge");

        private static void ForceRaidWithGoal(string goalDefName)
        {
            RaidGoalDef goal = DefDatabase<RaidGoalDef>.GetNamedSilentFail(goalDefName);
            if (goal == null) { Log.Error($"[RWR] RaidGoalDef '{goalDefName}' not found."); return; }

            Map map = Find.CurrentMap;
            Faction faction = Find.FactionManager.RandomEnemyFaction();
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = faction;

            Patch_IncidentWorker_Raid_TryExecuteWorker._pendingGoal    = goal;
            Patch_IncidentWorker_Raid_TryExecuteWorker._pendingFaction = faction;
            Patch_IncidentWorker_Raid_TryExecuteWorker._debugForceGoal = true;
            try
            {
                IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            finally
            {
                Patch_IncidentWorker_Raid_TryExecuteWorker._pendingGoal    = null;
                Patch_IncidentWorker_Raid_TryExecuteWorker._pendingFaction = null;
                Patch_IncidentWorker_Raid_TryExecuteWorker._debugForceGoal = false;
            }

            Log.Message($"[RWR] Forced raid with goal: {goal.defName} from {faction?.Name ?? "unknown faction"}");
        }

        // ── Retreat test ─────────────────────────────────────────────────────

        [DebugAction("RaidsWithinReason", "Force-succeed active raid goal", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceSucceedActiveRaid()
        {
            Map map = Find.CurrentMap;
            var tracker = map.GetComponent<RaidGoalTracker>();
            if (tracker == null) { Log.Warning("[RWR] No RaidGoalTracker on current map."); return; }

            bool any = false;
            foreach (Lord lord in map.lordManager.lords)
            {
                if (tracker.GetGoal(lord) == null || tracker.IsSucceeded(lord)) continue;
                tracker.MarkSuccess(lord);
                any = true;
            }
            if (!any) Log.Warning("[RWR] No active goal raid lords found. Spawn one first with 'Force raid: X'.");
        }

        // ── State inspector ──────────────────────────────────────────────────

        [DebugAction("RaidsWithinReason", "Log active raid goals", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogActiveRaidGoals()
        {
            Map map = Find.CurrentMap;
            var tracker = map.GetComponent<RaidGoalTracker>();
            if (tracker == null) { Log.Warning("[RWR] No RaidGoalTracker on current map."); return; }

            var lords = map.lordManager?.lords;
            if (lords == null || lords.Count == 0) { Log.Message("[RWR] No active lords on map."); return; }

            foreach (Lord lord in lords)
            {
                RaidGoalDef goal = tracker.GetGoal(lord);
                if (goal == null) continue;
                bool succeeded   = tracker.IsSucceeded(lord);
                Pawn target      = tracker.GetTargetPawn(lord);
                Building bldg    = tracker.GetTargetBuilding(lord);
                float loot       = tracker.GetLootValue(lord);
                Log.Message($"[RWR] Lord ({lord.faction?.Name}) → goal={goal.defName} succeeded={succeeded} targetPawn={target?.LabelShort ?? "-"} targetBuilding={bldg?.LabelShort ?? "-"} lootValue={loot:F0}");
            }
        }

        [DebugAction("RaidsWithinReason", "Log colony state scores", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogColonyState()
        {
            Map map = Find.CurrentMap;
            float wealth        = ColonyStateReader.GetWealthScore(map);
            bool  hasPrisoner   = ColonyStateReader.HasValuablePrisoner(map);
            bool  hasRooms      = ColonyStateReader.HasAnyRooms(map);
            Log.Message($"[RWR] Colony state — wealthScore={wealth:F2}  hasPrisoner={hasPrisoner}  hasRooms={hasRooms}");
        }

        [DebugAction("RaidsWithinReason", "Log negotiator cooldowns", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogNegotiatorCooldowns()
        {
            var cooldown = Current.Game.GetComponent<NegotiatorCooldownComponent>();
            if (cooldown == null) { Log.Warning("[RWR] NegotiatorCooldownComponent not found."); return; }

            var factions = Find.FactionManager.AllFactionsVisible
                .Where(f => f.HostileTo(Faction.OfPlayer));
            foreach (Faction f in factions)
                Log.Message($"[RWR] {f.Name}: onCooldown={cooldown.IsOnCooldown(f)}");
        }
    }
}
