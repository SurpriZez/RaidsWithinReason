using RimWorld;
using Verse;

namespace RaidsWithinReason
{
    public static class RaidGoalEvaluator
    {
        public static RaidGoalDef SelectGoal(IncidentParms parms, Faction faction, Map map)
        {
            var candidates = DefDatabase<RaidGoalDef>.AllDefsListForReading;
            if (candidates.Count == 0) return null;

            float wealthScore        = ColonyStateReader.GetWealthScore(map);
            bool  hasPrisoner        = ColonyStateReader.HasValuablePrisoner(map);
            float roomImpressiveness = ColonyStateReader.GetMostBeautifulRoom(map)
                                           ?.GetStat(RoomStatDefOf.Impressiveness) ?? 0f;
            bool  recentlyAttacked   = ColonyStateReader.RecentlyAttackedFaction(faction, map);

            return candidates.MaxByWithFallback(def => def.goalType switch
            {
                RaidGoalType.Loot    => wealthScore,
                RaidGoalType.Capture => hasPrisoner ? 0.8f : 0f,
                RaidGoalType.Destroy => roomImpressiveness > 10f ? 0.6f : 0.1f,
                _                    => 0f, // Revenge is reactive only — never randomly assigned
            });
        }
    }
}
