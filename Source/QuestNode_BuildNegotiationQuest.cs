using System.Collections.Generic;
using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace RaidsWithinReason
{
    // Root QuestNode for RWR_NegotiatorDemand. Called by QuestGen.Generate().
    // Required slate keys: "demand", "scaledAmount", "faction", "map"
    public class QuestNode_BuildNegotiationQuest : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            Quest quest = QuestGen.quest;
            Slate slate = QuestGen.slate;

            var demand       = slate.Get<NegotiationDemandDef>("demand");
            int scaledAmount = slate.Get<int>("scaledAmount");
            var faction      = slate.Get<Faction>("faction");
            var map          = slate.Get<Map>("map");

            string successSignal = Find.UniqueIDsManager.GetNextSignalTagID().ToString();
            int    expiryTick    = Find.TickManager.TicksGame +
                                   Mathf.RoundToInt(demand.timeLimitDays * GenDate.TicksPerDay);

            // Most valuable prisoner on the map for pawn demands
            Pawn prisoner = demand.demandType == NegotiationDemandType.Pawn
                ? map.mapPawns.PrisonersOfColony.MaxByWithFallback(p => p.MarketValue)
                : null;

            // Delivery is handled via right-clicking the negotiator on the map.
            // No world-map delivery point is needed.
            AddPart(new QuestPart_RequireDelivery
            {
                demand           = demand,
                requiredAmount   = scaledAmount,
                deliveryPoint    = null,
                requiredPrisoner = prisoner,
                outSignalSuccess = successSignal,
                map              = map,
            });

            AddPart(new QuestPart_TimerExpiry
            {
                expiryTick = expiryTick,
                faction    = faction,
                map        = map,
                goalDef    = DefDatabase<RaidGoalDef>.GetNamedSilentFail($"RaidGoal_{demand.linkedGoalType}"),
            });

            AddPart(new QuestPart_GrantGoodwill
            {
                inSignal      = successSignal,
                faction       = faction,
                goodwillChange = 20f, // compliance bonus — larger than a standard raid-success goodwill tick
            });

            string requireLine = BuildRequireLine(demand, scaledAmount, prisoner);

            string nameText = $"Negotiation: {faction?.Name}";
            string descText =
                $"{faction?.Name} demands: {demand.targetDescription}\n\n" +
                $"Required: {requireLine}\n" +
                $"Deadline: {demand.timeLimitDays} days\n\n" +
                "Fulfilling the demand will improve relations. Failure triggers an immediate raid.";

            QuestGen.AddQuestNameRules(new List<Rule>
            {
                new Rule_String("questName", nameText),
            });
            QuestGen.AddQuestDescriptionRules(new List<Rule>
            {
                new Rule_String("questDescription", descText),
            });
        }

        private static void AddPart(QuestPart part)
        {
            part.quest = QuestGen.quest;
            QuestGen.quest.PartsListForReading.Add(part);
        }

        private static string BuildRequireLine(NegotiationDemandDef demand, int scaledAmount, Pawn prisoner)
        {
            if (demand.demandType == NegotiationDemandType.Pawn)
                return $"Right-click the negotiator to release prisoner {prisoner?.LabelShort ?? "as demanded"}";

            string thing = demand.thingDef?.label ?? "silver";
            return $"Right-click the negotiator to pay {scaledAmount}× {thing}";
        }
    }
}
