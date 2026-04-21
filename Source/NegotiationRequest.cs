using Verse;

namespace RaidsWithinReason
{
    // A non-Def container for a specific, dynamically-generated negotiation attempt.
    public class NegotiationRequest : IExposable
    {
        public NegotiationDemandDef template;
        public ThingDef              thingDef;
        public int                   amount;
        public Pawn                  targetPawn;

        public NegotiationRequest() { }

        public NegotiationRequest(NegotiationDemandDef template, ThingDef thingDef, int amount)
        {
            this.template = template;
            this.thingDef = thingDef;
            this.amount   = amount;
        }

        public string TargetDescription
        {
            get
            {
                if (template == null) return "a demand";
                if (template.demandType == NegotiationDemandType.Goods)
                {
                    string label = thingDef?.label ?? "silver";
                    return $"{amount} {label}";
                }
                if (template.demandType == NegotiationDemandType.Pawn && targetPawn != null)
                {
                    return $"release {targetPawn.NameShortColored.Resolve()}";
                }
                return template.targetDescription;
            }
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref template,  "template");
            Scribe_Defs.Look(ref thingDef,  "thingDef");
            Scribe_Values.Look(ref amount,   "amount");
            Scribe_References.Look(ref targetPawn, "targetPawn");
        }
    }
}
