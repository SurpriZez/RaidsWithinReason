using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    public class Alert_NegotiatorPresent : Alert
    {
        public Alert_NegotiatorPresent()
        {
            this.defaultLabel = "Negotiator present";
            this.defaultPriority = AlertPriority.Critical;
        }



        public override TaggedString GetExplanation()
        {
            return "One or more negotiators from another faction are waiting at your borders. You should talk to them or consider their demands before they lose patience.";
        }


        public override AlertReport GetReport()
        {
            Map map = Find.CurrentMap;
            if (map == null) return false;
            var negotiators = GetNegotiators(map);
            var alert = new AlertReport()
            {
                culpritsPawns = negotiators,
                active = negotiators.Any()
            };
            return alert;
        }

        protected override void OnClick()
        {
            var letter = Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_NegotiatorArrival>().FirstOrDefault();
            if (letter != null)
            {
                letter.OpenLetter();
            }
            else
            {
                base.OnClick();
            }
        }

        private List<Pawn> GetNegotiators(Map map)
        {
            var result = new List<Pawn>();
            var lords = map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                if (lords[i].LordJob is LordJob_NegotiatorVisit visit)
                {
                    // Add all pawns in this lord, but specifically highlight the ones with our hediff if we want to be precise.
                    // For an alert, showing all of them (including guards) is usually fine.
                    result.AddRange(lords[i].ownedPawns);
                }
            }
            return result;
        }
    }
}
