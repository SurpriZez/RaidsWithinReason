using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    // Monitors the map/world for the player completing the demanded action.
    // Sends outSignalSuccess when the condition is met.
    public class QuestPart_RequireDelivery : QuestPart
    {
        public NegotiationRequest request;
        public RWR_DeliveryPoint    deliveryPoint;
        public Pawn                 requiredPrisoner;
        public string               outSignalSuccess;
        public Map                  map;
        public bool                 completed;

        public void DoTick()
        {
            if (completed || quest.State != QuestState.Ongoing) return;
            if (Find.TickManager.TicksGame % 300 != 0) return;

            if (request?.template?.demandType == NegotiationDemandType.Pawn)
                CheckPrisonerRelease();
            else
                CheckCaravanDelivery();
        }

        private void CheckPrisonerRelease()
        {
            if (requiredPrisoner == null || requiredPrisoner.Dead || requiredPrisoner.Destroyed)
                return;
            if (!requiredPrisoner.IsPrisonerOfColony)
                CompleteDelivery();
        }

        private void CheckCaravanDelivery()
        {
            if (deliveryPoint == null || deliveryPoint.Destroyed) return;

            foreach (Caravan caravan in Find.WorldObjects.Caravans
                     .Where(c => c.Faction == Faction.OfPlayer && c.Tile == deliveryPoint.Tile))
            {
                if (TryConsumeFromCaravan(caravan))
                {
                    CompleteDelivery();
                    return;
                }
            }
        }

        private bool TryConsumeFromCaravan(Caravan caravan)
        {
            ThingDef target = request.thingDef;
            int total = caravan.PawnsListForReading
                .SelectMany(p => p.inventory.innerContainer)
                .Where(t => t.def == target)
                .Sum(t => t.stackCount);

            if (total < request.amount) return false;

            int remaining = request.amount;
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                foreach (Thing thing in pawn.inventory.innerContainer
                         .Where(t => t.def == target).ToList())
                {
                    if (thing.stackCount <= remaining)
                    {
                        remaining -= thing.stackCount;
                        pawn.inventory.innerContainer.Remove(thing);
                        thing.Destroy();
                    }
                    else
                    {
                        thing.stackCount -= remaining;
                        remaining = 0;
                    }
                    if (remaining <= 0) break;
                }
                if (remaining <= 0) break;
            }
            return true;
        }

        public void ForceComplete() => CompleteDelivery();

        private void CompleteDelivery()
        {
            completed = true;
            if (deliveryPoint != null && !deliveryPoint.Destroyed)
                deliveryPoint.Destroy();
            Find.SignalManager.SendSignal(new Signal(outSignalSuccess));
        }

        public override string DescriptionPart
        {
            get
            {
                if (completed) return null;
                if (request?.template?.demandType == NegotiationDemandType.Pawn)
                    return $"Release prisoner: {requiredPrisoner?.LabelShort ?? "demanded prisoner"}";
                return $"Deliver {request?.TargetDescription} to the negotiator";
            }
        }

        public override void Cleanup()
        {
            base.Cleanup();
            if (deliveryPoint != null && !deliveryPoint.Destroyed)
                deliveryPoint.Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref request,             "request");
            Scribe_References.Look(ref deliveryPoint, "deliveryPoint");
            Scribe_References.Look(ref requiredPrisoner, "requiredPrisoner");
            Scribe_Values.Look(ref outSignalSuccess,  "outSignalSuccess");
            Scribe_References.Look(ref map,           "map");
            Scribe_Values.Look(ref completed,         "completed");
        }
    }

    // Counts down and fires a raid when time expires.
    public class QuestPart_TimerExpiry : QuestPart
    {
        public int         expiryTick;
        public Faction     faction;
        public Map         map;
        public RaidGoalDef goalDef;
        public bool        triggered;
        
        public void DoTick()
        {
            if (triggered || quest.State != QuestState.Ongoing) return;
            if (Find.TickManager.TicksGame < expiryTick) return;

            triggered = true;
            FireRaid();
        }

        private void FireRaid()
        {
            // Dismiss the negotiator if still on the map.
            foreach (Lord lord in map.lordManager.lords)
            {
                if (lord.faction == faction && lord.LordJob is LordJob_NegotiatorVisit)
                    lord.ReceiveMemo("NegotiatorDismissed");
            }

            var priorLords = map.lordManager.lords.ToHashSet();

            IncidentDef   raidDef = IncidentDefOf.RaidEnemy;
            IncidentParms parms   = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction         = faction;

            Patch_IncidentWorker_Raid_TryExecute._skipInterception = true;
            Patch_IncidentWorker_Raid_TryExecute._pendingGoal      = goalDef;
            Patch_IncidentWorker_Raid_TryExecute._pendingFaction   = faction;
            try
            {
                raidDef.Worker.TryExecute(parms);
            }
            finally
            {
                Patch_IncidentWorker_Raid_TryExecute._skipInterception = false;
                Patch_IncidentWorker_Raid_TryExecute._pendingGoal      = null;
                Patch_IncidentWorker_Raid_TryExecute._pendingFaction   = null;
            }

            // Assign the forced goal to any lords that spawned from this raid
            if (goalDef != null)
            {
                var tracker = map.GetComponent<RaidGoalTracker>();
                foreach (Lord newLord in map.lordManager.lords
                         .Where(l => !priorLords.Contains(l) && l.faction == faction))
                    tracker?.SetGoal(newLord, goalDef);
            }

            Find.LetterStack.ReceiveLetter(
                "Demand Expired",
                $"Your failure to comply with the demands of {faction?.Name} has provoked a raid.",
                LetterDefOf.ThreatBig,
                new LookTargets(map.Parent));

            quest.End(QuestEndOutcome.Fail, sendLetter: false);
        }

        public override string DescriptionPart
        {
            get
            {
                if (triggered || quest?.State != QuestState.Ongoing) return null;
                int ticks = expiryTick - Find.TickManager.TicksGame;
                return ticks > 0 ? $"Time remaining: {ticks.ToStringTicksToPeriod()}" : "Expired";
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref expiryTick,    "expiryTick");
            Scribe_References.Look(ref faction,   "faction");
            Scribe_References.Look(ref map,       "map");
            Scribe_Defs.Look(ref goalDef,         "goalDef");
            Scribe_Values.Look(ref triggered,     "triggered");
        }
    }

    // Fires when delivery succeeds: applies goodwill bonus and ends the quest.
    public class QuestPart_GrantGoodwill : QuestPart
    {
        public string  inSignal;
        public Faction faction;
        public float   goodwillChange;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            if (signal.tag != inSignal) return;

            faction?.TryAffectGoodwillWith(
                Faction.OfPlayer,
                Mathf.RoundToInt(goodwillChange),
                canSendMessage: false,
                canSendHostilityLetter: false);

            Find.LetterStack.ReceiveLetter(
                "Demand Fulfilled",
                $"You have fulfilled the demands of {faction?.Name}. " +
                $"Your relationship with them has improved by {Mathf.RoundToInt(goodwillChange)} goodwill.",
                LetterDefOf.PositiveEvent);

            quest.End(QuestEndOutcome.Success, sendLetter: false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal,       "inSignal");
            Scribe_References.Look(ref faction,    "faction");
            Scribe_Values.Look(ref goodwillChange, "goodwillChange");
        }
    }
}
