using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    public class LordJob_GoalRaid : LordJob_AssaultColony
    {
        private RaidGoalDef goalDef;

        public LordJob_GoalRaid() { }

        public LordJob_GoalRaid(RaidGoalDef goalDef, Faction faction,
                                bool canKidnap = true, bool canTimeoutOrFlee = true,
                                bool sappers = false, bool useAvoidGridSmart = false,
                                bool canSteal = true)
            : base(faction, canKidnap, canTimeoutOrFlee, sappers, useAvoidGridSmart, canSteal)
        {
            this.goalDef = goalDef;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph baseGraph = base.CreateGraph();

            if (goalDef == null)
                return baseGraph;

            // Capture the original first toil (assault entry) before we shift the list
            LordToil originalFirst = baseGraph.lordToils[0];

            var pursueToil = new LordToil_PursueGoal(goalDef);
            baseGraph.lordToils.Insert(0, pursueToil);

            // On success: retreat off-map if retreatOnSuccess (and the global setting allows it)
            bool effectiveRetreat = goalDef.retreatOnSuccess && RWR_Mod.Settings.enableRetreatOnSuccess;
            LordToil successDest;
            if (effectiveRetreat)
            {
                var exitToil = new LordToil_ExitMapGoalAchieved();
                baseGraph.lordToils.Add(exitToil);
                successDest = exitToil;
            }
            else
            {
                successDest = originalFirst;
            }

            var successTrans = new Transition(pursueToil, successDest);
            successTrans.triggers.Add(new Trigger_Memo("RWR_GoalSucceeded"));
            baseGraph.transitions.Add(successTrans);

            // Heavy casualties → hand off to the last toil in the base graph (flee/exit)
            LordToil fleeDest = baseGraph.lordToils[baseGraph.lordToils.Count - 1];
            var casualtyTrans = new Transition(pursueToil, fleeDest);
            casualtyTrans.triggers.Add(new Trigger_FractionPawnsLost(0.3f));
            baseGraph.transitions.Add(casualtyTrans);

            return baseGraph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref goalDef, "goalDef");
        }
    }
}
