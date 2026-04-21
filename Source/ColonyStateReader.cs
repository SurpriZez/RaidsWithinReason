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

        public static int GetStockedAmount(Map map, ThingDef def)
        {
            if (def == null) return 0;
            return map.resourceCounter.GetCount(def);
        }

        public static List<ThingDef> GetDynamicLootCandidates(Map map)
        {
            var candidates = new List<ThingDef>();
            foreach (var def in map.resourceCounter.AllCountedAmounts.Keys)
            {
                // Only consider stackable resources with actual value
                if (def.stackLimit > 1 && def.BaseMarketValue > 0.1f && !def.IsApparel && !def.IsWeapon)
                {
                    int count = map.resourceCounter.GetCount(def);
                    if (count > 0 && (count * def.BaseMarketValue) > 50f)
                    {
                        candidates.Add(def);
                    }
                }
            }
            return candidates;
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

        public static Pawn GetRandomNotableColonist(Map map)
        {
            List<Pawn> colonists = map.mapPawns.FreeColonists.ToList();
            if (!colonists.Any()) return null;

            List<Pawn> notableColonists = new List<Pawn>();

            foreach (Pawn p in colonists)
            {
                if (p.skills == null) continue;

                bool isNotable = false;
                foreach (SkillRecord skill in p.skills.skills)
                {
                    bool strictlyHighest = true;
                    foreach (Pawn other in colonists)
                    {
                        if (other == p || other.skills == null) continue;
                        if (other.skills.GetSkill(skill.def).Level >= skill.Level)
                        {
                            strictlyHighest = false;
                            break;
                        }
                    }

                    if (strictlyHighest && skill.Level > 0)
                    {
                        isNotable = true;
                        break;
                    }
                }

                if (isNotable)
                {
                    notableColonists.Add(p);
                }
            }

            if (notableColonists.Any())
                return notableColonists.RandomElement();

            return colonists.RandomElement();
        }

        public static bool HasAnyRooms(Map map)
        {
            foreach (IntVec3 cell in map.AllCells)
            {
                Room room = cell.GetRoom(map);
                if (room != null && !room.PsychologicallyOutdoors)
                    return true;
            }
            return false;
        }

        public static Room GetRandomRoomByPurpose(Map map)
        {
            var roomsByRole = new Dictionary<RoomRoleDef, List<Room>>();
            foreach (IntVec3 cell in map.AllCells)
            {
                Room room = cell.GetRoom(map);
                if (room != null && !room.PsychologicallyOutdoors && room.Role != null)
                {
                    if (!roomsByRole.ContainsKey(room.Role))
                        roomsByRole[room.Role] = new List<Room>();

                    if (!roomsByRole[room.Role].Contains(room))
                        roomsByRole[room.Role].Add(room);
                }
            }

            if (roomsByRole.Count == 0) return null;

            var randomRole = roomsByRole.Keys.ToList().RandomElement();
            return roomsByRole[randomRole].RandomElement();
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
