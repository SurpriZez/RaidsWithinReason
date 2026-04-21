using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    public class LordJob_NegotiatorVisit : LordJob
    {
        private IntVec3             waitSpot;
        private int                 waitTicks;
        public  NegotiationDemandDef demand;

        public LordJob_NegotiatorVisit() { }

        public LordJob_NegotiatorVisit(IntVec3 waitSpot, int waitTicks, NegotiationDemandDef demand = null)
        {
            this.waitSpot  = waitSpot;
            this.waitTicks = waitTicks;
            this.demand    = demand;
        }

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();

            // Initial: negotiator waits near entry while the letter is open.
            var wait = new LordToil_DefendPoint(waitSpot);
            graph.StartingToil = wait;

            // After acceptance: negotiator keeps waiting on-map until the player pays.
            var waitForPayment = new LordToil_DefendPoint(waitSpot);
            graph.AddToil(waitForPayment);

            var exit = new LordToil_ExitMap();
            graph.AddToil(exit);

            // Letter ignored for too long → leave hostile.
            var timeout = new Transition(wait, exit);
            timeout.triggers.Add(new Trigger_TicksPassed(waitTicks));
            graph.AddTransition(timeout);

            // Player refused or ignored → dismissed immediately.
            var dismissed = new Transition(wait, exit);
            dismissed.triggers.Add(new Trigger_Memo("NegotiatorDismissed"));
            graph.AddTransition(dismissed);

            // Player accepted → switch to waiting-for-payment state.
            var accepted = new Transition(wait, waitForPayment);
            accepted.triggers.Add(new Trigger_Memo("NegotiatorAccepted"));
            graph.AddTransition(accepted);

            // Payment made or quest timer expired → leave.
            var paid = new Transition(waitForPayment, exit);
            paid.triggers.Add(new Trigger_Memo("NegotiatorDismissed"));
            graph.AddTransition(paid);

            return graph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref waitSpot,  "waitSpot");
            Scribe_Values.Look(ref waitTicks, "waitTicks");
            Scribe_Defs.Look(ref demand,      "demand");
        }
    }
}
