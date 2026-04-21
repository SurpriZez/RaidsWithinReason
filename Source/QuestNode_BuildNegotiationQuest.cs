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

            var request = slate.Get<NegotiationRequest>("request");
            var faction = slate.Get<Faction>("faction");
            var map     = slate.Get<Map>("map");

            string successSignal = Find.UniqueIDsManager.GetNextSignalTagID().ToString();
            int    expiryTick    = Find.TickManager.TicksGame +
                                   Mathf.RoundToInt(request.template.timeLimitDays * GenDate.TicksPerDay);

            // Most valuable prisoner on the map for pawn demands
            Pawn prisoner = request.template.demandType == NegotiationDemandType.Pawn
                ? map.mapPawns.PrisonersOfColony.MaxByWithFallback(p => p.MarketValue)
                : null;

            // Delivery is handled via right-clicking the negotiator on the map.
            AddPart(new QuestPart_RequireDelivery
            {
                request          = request,
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
                goalDef    = DefDatabase<RaidGoalDef>.GetNamedSilentFail($"RaidGoal_{request.template.linkedGoalType}"),
            });

            AddPart(new QuestPart_GrantGoodwill
            {
                inSignal      = successSignal,
                faction       = faction,
                goodwillChange = 20f,
            });

            string requireLine = BuildRequireLine(request, prisoner);

            string nameText = $"Negotiation: {faction?.Name}";
            string descText =
                $"{faction?.Name} demands: {request.template.targetDescription}\n\n" +
                $"Required: {requireLine}\n" +
                $"Deadline: {request.template.timeLimitDays} days\n\n" +
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

        private static string BuildRequireLine(NegotiationRequest request, Pawn prisoner)
        {
            if (request.template.demandType == NegotiationDemandType.Pawn)
                return $"Right-click the negotiator to release prisoner {prisoner?.LabelShort ?? "as demanded"}";

            return $"Right-click the negotiator to pay {request.TargetDescription}";
        }
    }
}
