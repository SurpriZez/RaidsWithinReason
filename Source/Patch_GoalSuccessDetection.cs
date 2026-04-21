using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    // ── Loot + Capture ───────────────────────────────────────────────────────────
    // State is captured in Prefix (before ExitMap removes the pawn from the map/lord),
    // success is evaluated in Postfix (after the pawn has fully left, so the retreat
    // memo doesn't interrupt the carrier's JobDriver_Kidnap mid-exit).

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class Patch_Pawn_ExitMap_GoalDetect
    {
        public class ExitState
        {
            public Lord         lord;
            public Map          map;
            public RaidGoalDef  goal;
            public Pawn         carriedColonist; // Capture: pawn being kidnapped
            public float        inventoryValue;  // Loot: value carried off
            public bool         preMarked;       // true if MarkSuccessSilent was called in Prefix
        }

        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, out ExitState __state)
        {
            Lord lord = __instance.GetLord();
            Map  map  = __instance.Map;

            __state = new ExitState { lord = lord, map = map };

            if (lord == null || map == null) return;

            var tracker = map.GetComponent<RaidGoalTracker>();
            RaidGoalDef goal = tracker?.GetGoal(lord);
            if (goal == null || tracker.IsSucceeded(lord)) return;

            __state.goal = goal;

            if (goal.goalType == RaidGoalType.Capture &&
                __instance.carryTracker?.CarriedThing is Pawn carried &&
                carried.IsColonist)
            {
                __state.carriedColonist = carried;
                // Pre-mark NOW so Lord.Cleanup (which may fire inside ExitMap) sees success.
                tracker.MarkSuccessSilent(lord);
                __state.preMarked = true;
            }

            if (goal.goalType == RaidGoalType.Loot &&
                __instance.inventory?.innerContainer != null)
            {
                float v = 0f;
                foreach (Thing t in __instance.inventory.innerContainer)
                    v += t.MarketValue * t.stackCount;
                __state.inventoryValue = v;

                if (v > 0f)
                {
                    tracker.AccumulateLoot(lord, v);
                    if (tracker.GetLootValue(lord) >= 500f)
                    {
                        tracker.MarkSuccessSilent(lord);
                        __state.preMarked = true;
                    }
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ExitState __state)
        {
            if (__state?.lord == null || __state.map == null || !__state.preMarked) return;

            // The carrier has now fully exited — safe to send the memo and letter.
            __state.lord.ReceiveMemo("RWR_GoalSucceeded");

            if (__state.goal?.goalType == RaidGoalType.Capture)
                GoalSuccessLetters.TrySend(__state.lord, __state.goal, __state.map,
                    __state.carriedColonist?.LabelShort);
            else if (__state.goal?.goalType == RaidGoalType.Loot)
            {
                var tracker = __state.map.GetComponent<RaidGoalTracker>();
                GoalSuccessLetters.TrySend(__state.lord, __state.goal, __state.map,
                    $"your colonists' valuables ({(int)tracker.GetLootValue(__state.lord)} silver worth)");
            }
        }
    }

    // ── Destroy ──────────────────────────────────────────────────────────────────
    // Fires only when a building is actually destroyed — not per-tick.
    // Prefix captures the map before the building despawns; Postfix checks the result.

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_Thing_TakeDamage_GoalDetect
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance, out Map __state)
        {
            __state = __instance.Map; // capture before possible despawn
        }

        [HarmonyPostfix]
        public static void Postfix(Thing __instance, DamageInfo dinfo, Map __state)
        {
            // Fast exits — runs on every damage event, so be cheap
            if (__state == null || !__instance.Destroyed) return;
            if (!(__instance is Building)) return;
            if (__instance.def.passability == Traversability.Impassable) return; // wall

            if (!(dinfo.Instigator is Pawn attacker)) return;

            Lord lord = attacker.GetLord();
            if (lord == null) return;

            var tracker = __state.GetComponent<RaidGoalTracker>();
            RaidGoalDef goal = tracker?.GetGoal(lord);
            if (goal?.goalType != RaidGoalType.Destroy || tracker.IsSucceeded(lord)) return;

            Building target = tracker.GetTargetBuilding(lord);
            if (target == null || __instance != target) return;

            tracker.MarkSuccess(lord);
            GoalSuccessLetters.TrySend(lord, goal, __state, __instance.def.label);
        }
    }

    // ── Shared letter helper ──────────────────────────────────────────────────────

    internal static class GoalSuccessLetters
    {
        internal static void TrySend(Lord lord, RaidGoalDef goal, Map map, string detail = null)
        {
            if (goal == null || map == null) return;
            if (!goal.retreatOnSuccess || !RWR_Mod.Settings.enableRetreatOnSuccess) return;

            string faction = lord?.faction?.Name ?? "The raiders";
            string body = goal.goalType switch
            {
                RaidGoalType.Loot    => $"{faction} has seized {detail ?? "your valuables"} and is withdrawing from your colony.",
                RaidGoalType.Capture => $"{faction} has abducted {detail ?? "one of your colonists"} and is withdrawing.",
                RaidGoalType.Destroy  => $"{faction} has destroyed your {detail ?? "prized furnishings"} and is withdrawing.",
                RaidGoalType.Revenge  => $"{faction} has made an example of {detail ?? "your strongest fighter"} and is withdrawing.",
                _                   => $"{faction} has achieved their objective and is withdrawing.",
            };

            Find.LetterStack.ReceiveLetter(
                $"{faction} got what they came for",
                body,
                LetterDefOf.NegativeEvent,
                new LookTargets(map.Parent));
        }
    }
}
