using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RaidsWithinReason
{
    public static class ColonyStateReader
    {
        private const float WealthCap = 500_000f;

        public static float GetWealthScore(Map map)
        {
            float total = map.wealthWatcher.WealthTotal;
            return total >= WealthCap ? 1f : total / WealthCap;
        }

        public static bool HasValuablePrisoner(Map map)
        {
            return map.mapPawns.PrisonersOfColony.Any();
        }

        public static Pawn GetStrongestColonist(Map map)
        {
            return map.mapPawns.FreeColonists
                .MaxByWithFallback(p =>
                    (p.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0) +
                    (p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0));
        }

        public static Room GetMostBeautifulRoom(Map map)
        {
            var rooms = new HashSet<Room>();
            foreach (IntVec3 cell in map.AllCells)
            {
                Room room = cell.GetRoom(map);
                if (room != null && !room.PsychologicallyOutdoors)
                    rooms.Add(room);
            }
            return rooms.MaxByWithFallback(r => r.GetStat(RoomStatDefOf.Impressiveness));
        }

        public static bool RecentlyAttackedFaction(Faction faction, Map map)
        {
            int cutoff = Find.TickManager.TicksGame - 15 * GenDate.TicksPerDay;
            foreach (LogEntry entry in Find.PlayLog.AllEntries)
            {
                if (entry.Timestamp < cutoff) break;
                if (!(entry is BattleLogEntry_MeleeCombat || entry is BattleLogEntry_RangedFire)) continue;

                var pawns = entry.GetConcerns().OfType<Pawn>().ToList();
                if (pawns.Any(p => p?.Faction == Faction.OfPlayer) &&
                    pawns.Any(p => p?.Faction == faction))
                    return true;
            }
            return false;
        }
    }
}
