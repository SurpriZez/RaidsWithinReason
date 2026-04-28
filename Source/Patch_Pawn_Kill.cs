using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill
    {
        public class KillState
        {
            public Lord lord;
            public bool isNegotiator;
            public Map  map;
        }

        // Capture state before Kill() fires — GetLord() returns null after pawn is removed.
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, out KillState __state)
        {
            HediffDef negotiatorDef = DefDatabase<HediffDef>.GetNamedSilentFail("RWR_Negotiator");
            __state = new KillState
            {
                lord         = __instance.GetLord(),
                isNegotiator = negotiatorDef != null &&
                               (__instance.health?.hediffSet?.HasHediff(negotiatorDef) ?? false),
                map          = __instance.MapHeld,
            };
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, DamageInfo? dinfo, KillState __state)
        {
            // Negotiator / guard handling
            if (__state?.lord?.LordJob is LordJob_NegotiatorVisit visitJob && __state.map != null)
            {
                Faction faction = __instance.Faction;
                if (faction != null && faction != Faction.OfPlayer)
                {
                    if (__state.isNegotiator)
                        HandleNegotiatorKilled(__instance, __state.lord, visitJob, faction, __state.map);
                    else
                        HandleGuardKilled(__instance, __state.lord, faction);
                }
            }

            // Revenge goal: colonist killed by a raider
            CheckRevengeKillGoal(__instance, dinfo, __state?.map);
        }

        private static void CheckRevengeKillGoal(Pawn victim, DamageInfo? dinfo, Map map)
        {
            if (map == null || victim?.Faction != Faction.OfPlayer) return;
            if (dinfo == null) return;
            if (!(dinfo.Value.Instigator is Pawn attacker)) return;

            Lord attackerLord = attacker.GetLord();
            if (attackerLord == null) return;

            var tracker = map.GetComponent<RaidGoalTracker>();
            RaidGoalDef goal = tracker?.GetGoal(attackerLord);
            if (goal?.goalType != RaidGoalType.Revenge || tracker.IsSucceeded(attackerLord)) return;
            if (tracker.GetTargetPawn(attackerLord) != victim) return;

            tracker.MarkSuccess(attackerLord);
            GoalSuccessLetters.TrySend(attackerLord, goal, map, victim.LabelShort);
        }

        private static void HandleNegotiatorKilled(Pawn pawn, Lord lord, LordJob_NegotiatorVisit visitJob,
                                                   Faction faction, Map map)
        {
            CancelActiveQuest(faction);

            int penalty = -(Rand.Range(40, 61));
            faction.TryAffectGoodwillWith(Faction.OfPlayer, penalty,
                canSendMessage: false, canSendHostilityLetter: false);

            RaidGoalDef forcedGoal = DefDatabase<RaidGoalDef>.GetNamedSilentFail("RaidGoal_Revenge");

            int delayTicks = Rand.Range(0, 2 * GenDate.TicksPerDay);
            Current.Game.GetComponent<PendingRaidComponent>()
                   .Enqueue(faction, map, delayTicks, forcedGoal);

            Find.LetterStack.ReceiveLetter(
                "RWR_NegotiatorKilledTitle".Translate(faction.Name),
                "RWR_NegotiatorKilledText".Translate(pawn.LabelShort, faction.Name, penalty),
                LetterDefOf.ThreatBig,
                new LookTargets(pawn));

            lord.ReceiveMemo("NegotiatorDismissed");
        }

        private static void HandleGuardKilled(Pawn pawn, Lord lord, Faction faction)
        {
            faction.TryAffectGoodwillWith(Faction.OfPlayer, -15,
                canSendMessage: false, canSendHostilityLetter: false);

            // Negotiator departs — demand still active but time limit is halved
            lord.ReceiveMemo("NegotiatorDismissed");
            HalveQuestTimeLimit(faction);

            Messages.Message(
                "RWR_MessageGuardKilled".Translate(faction.Name),
                new LookTargets(pawn),
                MessageTypeDefOf.NegativeEvent);
        }

        private static void CancelActiveQuest(Faction faction)
        {
            foreach (Quest quest in Find.QuestManager.QuestsListForReading.ToList()
                     .Where(q => q.State == QuestState.Ongoing))
            {
                if (quest.PartsListForReading.OfType<QuestPart_TimerExpiry>().Any(p => p.faction == faction))
                {
                    quest.End(QuestEndOutcome.Fail, sendLetter: false);
                    break;
                }
            }
        }

        private static void HalveQuestTimeLimit(Faction faction)
        {
            foreach (Quest quest in Find.QuestManager.QuestsListForReading
                     .Where(q => q.State == QuestState.Ongoing))
            {
                QuestPart_TimerExpiry timer = quest.PartsListForReading.OfType<QuestPart_TimerExpiry>()
                    .FirstOrDefault(p => p.faction == faction);
                if (timer == null || timer.triggered) continue;

                int now       = Find.TickManager.TicksGame;
                int remaining = timer.expiryTick - now;
                if (remaining > 0)
                    timer.expiryTick = now + remaining / 2;
                break;
            }
        }
    }
}
