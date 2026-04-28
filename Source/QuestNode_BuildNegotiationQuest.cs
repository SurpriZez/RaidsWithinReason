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

            string nameText = "RWR_NegotiationQuestName".Translate(faction?.Name ?? (string)"RWR_UnknownFaction".Translate());
            string descText = "RWR_NegotiationQuestDesc".Translate(
                faction?.Name ?? (string)"RWR_UnknownFaction".Translate(),
                request.template.targetDescription,
                requireLine,
                request.template.timeLimitDays);

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
                return "RWR_NegotiationQuestRequirePawn".Translate(prisoner?.LabelShort ?? (string)"RWR_DemandedPrisoner".Translate());

            return "RWR_NegotiationQuestRequireItems".Translate(request.TargetDescription);
        }
    }
}
