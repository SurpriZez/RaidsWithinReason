using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    // Fires when a Lord is dissolved (all pawns dead, fled, or exited the map).
    // At this point the Lord is still fully valid — map and faction are accessible.
    [HarmonyPatch(typeof(Lord), nameof(Lord.Cleanup))]
    public static class Patch_Lord_Cleanup
    {
        [HarmonyPrefix]
        public static void Prefix(Lord __instance)
        {
            Map map = __instance.Map;
            if (map == null) return;

            TryTriggerRevenge(__instance, map);

            var tracker = map.GetComponent<RaidGoalTracker>();
            if (tracker == null) return;

            RaidGoalDef goal = tracker.GetGoal(__instance);
            if (goal == null) return; // not a raid we're tracking

            bool    succeeded = tracker.IsSucceeded(__instance);
            float   delta     = succeeded ? goal.successGoodwillDelta : goal.failureGoodwillDelta;
            Faction faction   = __instance.faction;

            if (delta != 0f && faction != null && faction != Faction.OfPlayer)
            {
                faction.TryAffectGoodwillWith(
                    Faction.OfPlayer,
                    Mathf.RoundToInt(delta),
                    canSendMessage:        false,
                    canSendHostilityLetter: false);
            }

            SendConsequenceLetter(__instance, goal, succeeded, delta, faction, map);

            tracker.Cleanup(__instance);
        }

        // When the player wipes enemy forces on a non-player map, schedule a Revenge raid.
        private static void TryTriggerRevenge(Lord lord, Map map)
        {
            if (map.IsPlayerHome) return;
            if (!map.mapPawns.AnyColonistSpawned) return;

            Faction faction = lord.faction;
            if (faction == null || faction == Faction.OfPlayer) return;
            if (!faction.HostileTo(Faction.OfPlayer)) return;
            if (faction.defeated) return;

            bool anyLeft = map.mapPawns.AllPawnsSpawned
                .Any(p => p.Faction == faction && !p.Dead && !p.Downed);
            if (anyLeft) return;

            RaidGoalDef revenge = DefDatabase<RaidGoalDef>.GetNamedSilentFail("RaidGoal_Revenge");
            if (revenge == null) return;

            Map homeMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
            if (homeMap == null) return;

            int delay = Rand.Range(GenDate.TicksPerDay, 3 * GenDate.TicksPerDay);
            Current.Game.GetComponent<PendingRaidComponent>()?.Enqueue(faction, homeMap, delay, revenge);

            Messages.Message(
                "RWR_MessageRoutedRetaliation".Translate(faction.Name),
                MessageTypeDefOf.ThreatSmall);
        }

        private static void SendConsequenceLetter(Lord lord, RaidGoalDef goal,
                                                  bool succeeded, float delta,
                                                  Faction faction, Map map)
        {
            string factionName = faction?.Name ?? (string)"RWR_UnknownRaiders".Translate();
            int    deltaRounded = Mathf.RoundToInt(delta);

            string title = succeeded
                ? (string)"RWR_PostRaidTitleSuccess".Translate(factionName)
                : (string)"RWR_PostRaidTitleFailure".Translate(factionName);

            string outcome = succeeded
                ? (string)"RWR_PostRaidOutcomeSuccess".Translate(goal.targetDescription)
                : (string)"RWR_PostRaidOutcomeFailure".Translate(factionName);

            string goodwillLine = deltaRounded > 0
                ? (string)"RWR_PostRaidGoodwillPositive".Translate(factionName, deltaRounded)
                : deltaRounded < 0
                    ? (string)"RWR_PostRaidGoodwillNegative".Translate(factionName, deltaRounded)
                    : null;

            string body = goodwillLine != null
                ? $"{outcome}\n\n{goodwillLine}"
                : outcome;

            LetterDef letterDef = succeeded
                ? LetterDefOf.NegativeEvent
                : LetterDefOf.PositiveEvent;

            Find.LetterStack.ReceiveLetter(title, body, letterDef,
                new LookTargets(map.Parent));
        }
    }
}
