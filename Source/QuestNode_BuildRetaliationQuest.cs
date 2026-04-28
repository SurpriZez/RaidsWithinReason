using System.Collections.Generic;
using RimWorld;
using RimWorld.QuestGen;
using Verse;
using Verse.Grammar;

namespace RaidsWithinReason
{
    // Root QuestNode for RWR_RetaliationTimer. Called by QuestGen.Generate().
    // Required slate keys: "faction", "map", "delayTicks"
    public class QuestNode_BuildRetaliationQuest : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            Quest quest = QuestGen.quest;
            Slate slate = QuestGen.slate;

            var faction    = slate.Get<Faction>("faction");
            var map        = slate.Get<Map>("map");
            var goal       = slate.Get<RaidGoalDef>("goal");
            int delayTicks = slate.Get<int>("delayTicks");

            int expiryTick = Find.TickManager.TicksGame + delayTicks;

            // Use the existing QuestPart_TimerExpiry to fire the raid.
            var timerPart = new QuestPart_TimerExpiry
            {
                expiryTick = expiryTick,
                faction    = faction,
                map        = map,
                goalDef    = goal
            };
            timerPart.quest = quest;
            quest.PartsListForReading.Add(timerPart);

            string nameText = "RWR_RetaliationQuestName".Translate(faction?.Name ?? "RWR_UnknownFaction".Translate());
            string descText = "RWR_RetaliationQuestDesc".Translate(faction?.Name ?? "RWR_UnknownFaction".Translate(), delayTicks.ToStringTicksToPeriod());

            QuestGen.AddQuestNameRules(new List<Rule>
            {
                new Rule_String("questName", nameText),
            });
            QuestGen.AddQuestDescriptionRules(new List<Rule>
            {
                new Rule_String("questDescription", descText),
            });
        }
    }
}
