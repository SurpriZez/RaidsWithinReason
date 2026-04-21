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
            if (__result) return;
            if (NegotiatorUtil.IsNegotiator(__instance))
                __result = true;
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
            if (visitJob?.demand == null) yield break;

            QuestPart_RequireDelivery part = FindDeliveryPart(visitJob.demand);
            if (part == null || part.completed) yield break;

            FloatMenuOption payOpt = BuildOption(target, visitJob.demand, part, target.Map);
            if (payOpt != null) yield return payOpt;
        }

        private static FloatMenuOption BuildOption(Pawn negotiator, NegotiationDemandDef demand,
                                                   QuestPart_RequireDelivery part, Map map)
        {
            if (demand.demandType == NegotiationDemandType.Pawn)
                return BuildPrisonerOption(negotiator, part, map);

            ThingDef thingDef = demand.thingDef ?? ThingDefOf.Silver;
            int available = map.resourceCounter.GetCount(thingDef);
            int required  = part.requiredAmount;

            string label = $"Pay {required}× {thingDef.label} to negotiator ({available} available)";

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
                prisoner.guest.SetGuestStatus(null);
                GenPlace.TryPlaceThing(prisoner, negotiator.Position, map, ThingPlaceMode.Near);
                negotiator.GetLord()?.AddPawn(prisoner);
                part.ForceComplete();
                negotiator.GetLord()?.ReceiveMemo("NegotiatorDismissed");
                Messages.Message(
                    $"{prisoner.LabelShort} has been handed over.",
                    MessageTypeDefOf.PositiveEvent);
            });
        }

        private static QuestPart_RequireDelivery FindDeliveryPart(NegotiationDemandDef demand)
        {
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.State != QuestState.Ongoing) continue;
                foreach (QuestPart p in quest.PartsListForReading)
                {
                    if (p is QuestPart_RequireDelivery d && d.demand == demand && !d.completed)
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
    }
}
