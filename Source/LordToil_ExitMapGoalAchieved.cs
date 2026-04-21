using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    // Raiders who achieved their goal jog off the map immediately.
    // All movement logic is inherited from LordToil_ExitMap; this subclass
    // exists so the graph can name the toil and transitions can target it
    // without ambiguity with vanilla ExitMap toils in the same graph.
    public class LordToil_ExitMapGoalAchieved : LordToil_ExitMap
    {
        public LordToil_ExitMapGoalAchieved()
            : base(LocomotionUrgency.Jog, interruptCurrentJob: true) { }
    }
}
