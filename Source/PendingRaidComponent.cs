using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    public class PendingRaid : IExposable
    {
        public Faction     faction;
        public Map         map;
        public int         triggerTick;
        public RaidGoalDef forcedGoal;

        public void ExposeData()
        {
            Scribe_References.Look(ref faction,  "faction");
            Scribe_References.Look(ref map,      "map");
            Scribe_Values.Look(ref triggerTick,  "triggerTick");
            Scribe_Defs.Look(ref forcedGoal,     "forcedGoal");
        }
    }

    // GameComponent auto-discovered by RimWorld on game init/load.
    // Holds raids that must fire after a fixed delay (e.g. negotiator killing).
    public class PendingRaidComponent : GameComponent
    {
        private List<PendingRaid> pending = new List<PendingRaid>();

        public PendingRaidComponent(Game game) : base() { }

        public void Enqueue(Faction faction, Map map, int delayTicks, RaidGoalDef forcedGoal)
        {
            pending.Add(new PendingRaid
            {
                faction     = faction,
                map         = map,
                triggerTick = Find.TickManager.TicksGame + delayTicks,
                forcedGoal  = forcedGoal,
            });
        }

        public override void GameComponentTick()
        {
            if (pending.Count > 0)
            {
                int now = Find.TickManager.TicksGame;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    if (now >= pending[i].triggerTick)
                    {
                        FireRaid(pending[i]);
                        pending.RemoveAt(i);
                    }
                }
            }

            // QuestPart ticking — QuestPartTick() is not virtual on QuestPart in 1.6
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.State != QuestState.Ongoing) continue;
                foreach (QuestPart part in quest.PartsListForReading)
                {
                    if (part is QuestPart_RequireDelivery delivery) delivery.DoTick();
                    else if (part is QuestPart_TimerExpiry timer) timer.DoTick();
                }
            }
        }

        private static void FireRaid(PendingRaid raid)
        {
            if (raid.map == null || raid.faction == null) return;

            var priorLords = raid.map.lordManager.lords.ToHashSet();

            IncidentDef   raidDef = IncidentDefOf.RaidEnemy;
            IncidentParms parms   = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, raid.map);
            parms.faction         = raid.faction;
            raidDef.Worker.TryExecute(parms);

            if (raid.forcedGoal != null)
            {
                var tracker = raid.map.GetComponent<RaidGoalTracker>();
                foreach (Lord newLord in raid.map.lordManager.lords
                         .Where(l => !priorLords.Contains(l) && l.faction == raid.faction))
                    tracker?.SetGoal(newLord, raid.forcedGoal);
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pending, "pending", LookMode.Deep);
            pending ??= new List<PendingRaid>();
        }
    }
}
