using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    public class IncidentWorker_NegotiatorArrival : IncidentWorker
    {
        private const int EarliestDay = 10;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms)) return false;
            if (Find.TickManager.TicksGame < EarliestDay * GenDate.TicksPerDay) return false;
            var map = parms.target as Map;
            return map != null && map.mapPawns.FreeColonistsCount > 0;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map     map     = (Map)parms.target;
            Faction faction = parms.faction ?? Find.FactionManager.RandomEnemyFaction(allowHidden: false, allowDefeated: false, allowNonHumanlike: false);
            if (faction == null) { Log.Warning("[RWR] No valid humanlike enemy faction found."); return false; }

            NegotiationDemandDef demand = SelectDemand(faction, map);
            if (demand == null) { Log.Warning($"[RWR] SelectDemand returned null. NegotiationDemandDef count: {DefDatabase<NegotiationDemandDef>.DefCount}"); return false; }

            float wealthScore   = ColonyStateReader.GetWealthScore(map);
            float scale         = wealthScore < 0.33f ? 0.5f : wealthScore < 0.67f ? 1f : 1.5f;
            int   scaledAmount  = Mathf.RoundToInt(demand.baseAmount * scale);

            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 spawnCell, map, 0.5f))
            { Log.Warning("[RWR] TryFindRandomPawnEntryCell failed."); return false; }

            Pawn negotiator = SpawnPawn(faction, map, spawnCell);
            if (negotiator == null) return false;
            negotiator.health.AddHediff(DefDatabase<HediffDef>.GetNamed("RWR_Negotiator"));

            var allPawns = new List<Pawn> { negotiator };
            if (Rand.Chance(0.4f))
            {
                int guardCount = Rand.Range(1, 4);
                for (int i = 0; i < guardCount; i++)
                    allPawns.Add(SpawnPawn(faction, map,
                        CellFinder.RandomClosewalkCellNear(spawnCell, map, 3)));
            }

            Lord lord = LordMaker.MakeNewLord(faction,
                new LordJob_NegotiatorVisit(spawnCell, GenDate.TicksPerDay, demand), map);
            foreach (Pawn p in allPawns) lord.AddPawn(p);

            var letter = new ChoiceLetter_NegotiatorArrival
            {
                def            = DefDatabase<LetterDef>.GetNamed("RWR_NegotiatorArrival"),
                Label          = "Negotiator Arrives",
                Text           = BuildLetterText(faction, demand, scaledAmount),
                lookTargets    = new LookTargets(negotiator),
                relatedFaction = faction,
                negotiator     = negotiator,
                demand         = demand,
                scaledAmount   = scaledAmount,
                negotiatorLord = lord,
                incidentMap    = map,
            };
            Find.LetterStack.ReceiveLetter(letter);
            return true;
        }

        private static Pawn SpawnPawn(Faction faction, Map map, IntVec3 cell)
        {
            PawnKindDef kind = ResolveNegotiatorKind(faction);
            if (kind == null)
            {
                Log.Error($"[RWR] No usable humanlike PawnKindDef found for faction '{faction.Name}'.");
                return null;
            }
            try
            {
                var request = new PawnGenerationRequest(
                    kind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    tile: map.Tile,
                    mustBeCapableOfViolence: true,
                    developmentalStages: DevelopmentalStage.Adult);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                GenSpawn.Spawn(pawn, cell, map, Rot4.Random);
                pawn.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Guest);
                return pawn;
            }
            catch (System.Exception e)
            {
                Log.Error($"[RWR] PawnGenerator failed for kind '{kind.defName}' faction '{faction.Name}': {e.Message}");
                return null;
            }
        }

        private static PawnKindDef ResolveNegotiatorKind(Faction faction)
        {
            // Prefer basicMemberKind if it has valid humanlike life stages.
            PawnKindDef basic = faction.def.basicMemberKind;
            if (IsValidHumanlikeKind(basic)) return basic;

            // Fall back to any valid kind from the faction's pawn groups.
            if (faction.def.pawnGroupMakers != null)
                foreach (var group in faction.def.pawnGroupMakers)
                    foreach (var option in group.options)
                        if (IsValidHumanlikeKind(option.kind)) return option.kind;

            return null;
        }

        private static bool IsValidHumanlikeKind(PawnKindDef kind) =>
            kind != null &&
            kind.RaceProps?.Humanlike == true &&
            !kind.RaceProps.lifeStageAges.NullOrEmpty();

        private static NegotiationDemandDef SelectDemand(Faction faction, Map map)
        {
            float wealthScore        = ColonyStateReader.GetWealthScore(map);
            bool  hasPrisoner        = ColonyStateReader.HasValuablePrisoner(map);
            float roomImpressiveness = ColonyStateReader.GetMostBeautifulRoom(map)
                                           ?.GetStat(RoomStatDefOf.Impressiveness) ?? 0f;
            bool  recentlyAttacked   = ColonyStateReader.RecentlyAttackedFaction(faction, map);

            return DefDatabase<NegotiationDemandDef>.AllDefsListForReading
                .Where(d => d.demandType != NegotiationDemandType.Pawn || hasPrisoner)
                .MaxByWithFallback(d => d.linkedGoalType switch
                {
                    RaidGoalType.Loot    => wealthScore,
                    RaidGoalType.Capture => hasPrisoner ? 0.8f : 0.2f,
                    RaidGoalType.Destroy => roomImpressiveness > 10f ? 0.6f : 0.1f,
                    RaidGoalType.Revenge => recentlyAttacked ? 0.9f : 0.05f,
                    _                    => 0f,
                });
        }

        private static string BuildLetterText(Faction faction, NegotiationDemandDef demand, int scaledAmount)
        {
            string thing = demand.thingDef?.label ?? "silver";
            return $"A negotiator from {faction.Name} has arrived at your colony's border.\n\n" +
                   $"Their demand: {demand.targetDescription} ({scaledAmount} {thing}).\n\n" +
                   $"You have {demand.timeLimitDays} days to comply. Refusing or ignoring them " +
                   $"will provoke a retaliatory raid within 1\u20134 days.";
        }
    }

    public class ChoiceLetter_NegotiatorArrival : ChoiceLetter
    {
        public Pawn                 negotiator;
        public NegotiationDemandDef demand;
        public int                  scaledAmount;
        public Lord                 negotiatorLord;
        public Map                  incidentMap;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                yield return OptionAccept();
                yield return OptionRefuse();
                yield return OptionIgnore();
            }
        }

        private DiaOption OptionAccept()
        {
            var opt = new DiaOption("Accept");
            opt.resolveTree = true;
            opt.action = () =>
            {
                string thingLabel = demand?.thingDef?.label ?? "silver";
                string confirmText =
                    $"Deliver {scaledAmount} {thingLabel} to {relatedFaction?.Name}?\n\n" +
                    "A compliance timer will begin. Failure to deliver will still trigger a raid.";

                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmText, () =>
                {
                    Slate slate = new Slate();
                    slate.Set("demand",       demand);
                    slate.Set("scaledAmount", scaledAmount);
                    slate.Set("faction",      relatedFaction);
                    slate.Set("map",          incidentMap);

                    Quest quest = QuestGen.Generate(
                        DefDatabase<QuestScriptDef>.GetNamed("RWR_NegotiatorDemand"), slate);
                    Find.QuestManager.Add(quest);
                    quest.Accept(null);

                    negotiatorLord?.ReceiveMemo("NegotiatorAccepted");
                    Find.LetterStack.RemoveLetter(this);
                }));
            };
            return opt;
        }

        private DiaOption OptionRefuse()
        {
            var opt = new DiaOption("Refuse");
            opt.resolveTree = true;
            opt.action = () =>
            {
                ScheduleRaid();
                negotiatorLord?.ReceiveMemo("NegotiatorDismissed");
                Find.LetterStack.RemoveLetter(this);
            };
            return opt;
        }

        private DiaOption OptionIgnore()
        {
            var opt = new DiaOption("Ignore for now");
            opt.resolveTree = true;
            opt.action = () =>
            {
                ScheduleRaid();
                Find.LetterStack.RemoveLetter(this);
                // Negotiator departs after 24 h via LordJob_NegotiatorVisit timeout
            };
            return opt;
        }

        private void ScheduleRaid()
        {
            IncidentDef   raidDef   = IncidentDefOf.RaidEnemy;
            IncidentParms raidParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, incidentMap);
            raidParms.faction       = relatedFaction;
            int delayTicks          = Rand.Range(GenDate.TicksPerDay, 4 * GenDate.TicksPerDay);
            Find.Storyteller.incidentQueue.Add(
                raidDef,
                Find.TickManager.TicksGame + delayTicks,
                raidParms);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref negotiator,     "negotiator");
            Scribe_Defs.Look(ref demand,               "demand");
            Scribe_Values.Look(ref scaledAmount,       "scaledAmount");
            Scribe_References.Look(ref negotiatorLord, "negotiatorLord");
            Scribe_References.Look(ref incidentMap,    "incidentMap");
        }
    }
}
