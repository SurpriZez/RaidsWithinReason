using RimWorld;
using Verse;

namespace RaidsWithinReason
{
    public enum NegotiationDemandType
    {
        Goods,
        Pawn,
        Goodwill,
        Silver,
    }

    public class NegotiationDemandDef : Def
    {
        public NegotiationDemandType demandType;
        public ThingDef              thingDef;
        public int                   baseAmount;
        public float                 timeLimitDays;
        public RaidGoalType          linkedGoalType;
        public string                targetDescription = string.Empty;
    }
}
