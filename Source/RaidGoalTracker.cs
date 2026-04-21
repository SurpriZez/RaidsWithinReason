using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    public class RaidGoalTracker : MapComponent
    {
        // --- persisted ---
        private Dictionary<Lord, RaidGoalDef> goals     = new Dictionary<Lord, RaidGoalDef>();
        private HashSet<Lord>                 succeeded = new HashSet<Lord>();

        // --- runtime only (not saved) ---
        private Dictionary<Lord, Pawn>     targetPawns     = new Dictionary<Lord, Pawn>();
        private Dictionary<Lord, Building> targetBuildings = new Dictionary<Lord, Building>();
        private Dictionary<Lord, float>    lootValues      = new Dictionary<Lord, float>();

        public RaidGoalTracker(Map map) : base(map) { }

        // ── goal assignment ──────────────────────────────────────────────

        public void SetGoal(Lord lord, RaidGoalDef goal)  => goals[lord] = goal;
        public RaidGoalDef GetGoal(Lord lord)
        {
            goals.TryGetValue(lord, out var g);
            return g;
        }

        // ── runtime target registration (set each time the toil refreshes) ──

        public void SetTargetPawn(Lord lord, Pawn pawn)
        {
            if (pawn != null) targetPawns[lord] = pawn;
            else              targetPawns.Remove(lord);
        }

        public Pawn GetTargetPawn(Lord lord)
        {
            targetPawns.TryGetValue(lord, out var p);
            return p;
        }

        public void SetTargetBuilding(Lord lord, Building building)
        {
            if (building != null) targetBuildings[lord] = building;
            else                  targetBuildings.Remove(lord);
        }

        public Building GetTargetBuilding(Lord lord)
        {
            targetBuildings.TryGetValue(lord, out var b);
            return b;
        }

        // ── loot value accumulation ──────────────────────────────────────

        public void AccumulateLoot(Lord lord, float value)
        {
            lootValues.TryGetValue(lord, out float current);
            lootValues[lord] = current + value;
        }

        public float GetLootValue(Lord lord)
        {
            lootValues.TryGetValue(lord, out float v);
            return v;
        }

        // ── success tracking ─────────────────────────────────────────────

        // Marks succeeded and sends the retreat memo. Use when the lord is still active.
        public void MarkSuccess(Lord lord)
        {
            succeeded.Add(lord);
            lord?.ReceiveMemo("RWR_GoalSucceeded");
        }

        // Marks succeeded WITHOUT sending the memo — use in a Harmony Prefix when the
        // memo must be deferred to the Postfix to avoid interrupting the exiting pawn's job.
        public void MarkSuccessSilent(Lord lord) => succeeded.Add(lord);

        public bool IsSucceeded(Lord lord) => succeeded.Contains(lord);

        // ── cleanup ──────────────────────────────────────────────────────

        public void Cleanup(Lord lord)
        {
            goals.Remove(lord);
            succeeded.Remove(lord);
            targetPawns.Remove(lord);
            targetBuildings.Remove(lord);
            lootValues.Remove(lord);
        }

        // ── save / load ──────────────────────────────────────────────────

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref goals,     "goals",     LookMode.Reference, LookMode.Def);
            Scribe_Collections.Look(ref succeeded, "succeeded", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                goals     ??= new Dictionary<Lord, RaidGoalDef>();
                succeeded ??= new HashSet<Lord>();

                // Remove entries where the Lord or the RaidGoalDef no longer exist
                foreach (Lord stale in goals.Keys
                    .Where(k => k == null || goals[k] == null).ToList())
                    goals.Remove(stale);
                succeeded.RemoveWhere(l => l == null);

                // Runtime dictionaries are always re-initialized on load
                targetPawns     = new Dictionary<Lord, Pawn>();
                targetBuildings = new Dictionary<Lord, Building>();
                lootValues      = new Dictionary<Lord, float>();
            }
        }
    }
}
