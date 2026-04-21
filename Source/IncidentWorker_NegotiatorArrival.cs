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
            parms.sendLetter = false; // Suppress the vanilla letter since we send our own ChoiceLetter.
            Map     map     = (Map)parms.target;
            Faction faction = parms.faction ?? Find.FactionManager.RandomEnemyFaction(allowHidden: false, allowDefeated: false, allowNonHumanlike: false);
            if (faction == null) { Log.Warning("[RWR] No valid humanlike enemy faction found."); return false; }

            NegotiationRequest request = SelectRequest(faction, map);
            if (request == null) { Log.Warning($"[RWR] SelectRequest returned null."); return false; }

            // Calculate amount based on Market Value and Wealth
            if (request.template.demandType == NegotiationDemandType.Goods)
            {
                float targetValue = 500f + (map.wealthWatcher.WealthTotal * 0.01f); // 1% of wealth + 500 base
                request.amount = Mathf.Max(1, Mathf.RoundToInt(targetValue / request.thingDef.BaseMarketValue));
            }
            else
            {
                request.amount = request.template.baseAmount;
            }

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
                new LordJob_NegotiatorVisit(spawnCell, GenDate.TicksPerDay, request), map);
            foreach (Pawn p in allPawns) lord.AddPawn(p);

            var letter = (ChoiceLetter_NegotiatorArrival)LetterMaker.MakeLetter("Negotiator Arrives", BuildLetterText(faction, request), DefDatabase<LetterDef>.GetNamed("RWR_Letter_NegotiatorArrival"));
            letter.lookTargets    = new LookTargets(negotiator);
            letter.relatedFaction = faction;
            letter.negotiator     = negotiator;
            letter.request        = request;
            letter.negotiatorLord = lord;
            letter.incidentMap    = map;

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

        private static NegotiationRequest SelectRequest(Faction faction, Map map)
        {
            float wealthScore        = ColonyStateReader.GetWealthScore(map);
            bool  hasPrisoner        = ColonyStateReader.HasValuablePrisoner(map);
            bool  hasRooms           = ColonyStateReader.HasAnyRooms(map);
            bool  recentlyAttacked   = ColonyStateReader.RecentlyAttackedFaction(faction, map);

            // 1. Pick a template
            var template = DefDatabase<NegotiationDemandDef>.AllDefsListForReading
                .InRandomOrder()
                .Where(t => 
                    (t.demandType != NegotiationDemandType.Pawn || hasPrisoner) &&
                    (t.linkedGoalType != RaidGoalType.Destroy || hasRooms)
                )
                .MaxByWithFallback(t => 
                {
                    float score = t.linkedGoalType switch
                    {
                        RaidGoalType.Loot    => wealthScore,
                        RaidGoalType.Capture => hasPrisoner ? 0.8f : 0.0f,
                        RaidGoalType.Destroy => hasRooms ? 0.6f : 0.0f,
                        RaidGoalType.Revenge => recentlyAttacked ? 0.9f : 0.05f,
                        _                    => 0.01f,
                    };
                    return score + Rand.Value * 0.1f;
                });

            if (template == null) return null;

            // 2. Select specific ThingDef or Pawn if needed
            ThingDef targetDef  = template.thingDef;
            Pawn     targetPawn = null;

            if (template.demandType == NegotiationDemandType.Goods)
            {
                var candidates = ColonyStateReader.GetDynamicLootCandidates(map);
                if (candidates.Any())
                {
                    targetDef = candidates.RandomElementByWeight(c => ColonyStateReader.GetStockedAmount(map, c) * c.BaseMarketValue);
                }
            }
            else if (template.demandType == NegotiationDemandType.Pawn)
            {
                targetPawn = map.mapPawns.PrisonersOfColony.RandomElementWithFallback();
            }

            var request = new NegotiationRequest(template, targetDef, template.baseAmount);
            request.targetPawn = targetPawn;
            return request;
        }

        private static string BuildLetterText(Faction faction, NegotiationRequest request)
        {
            return $"A negotiator from {faction.Name} has arrived at your colony's border.\n\n" +
                   $"Their demand: {request.template.targetDescription} ({request.TargetDescription}).\n\n" +
                   $"You have {request.template.timeLimitDays} days to comply. Refusing or ignoring them " +
                   $"will provoke a retaliatory raid within 1\u20134 days.";
        }
    }

    public class ChoiceLetter_NegotiatorArrival : ChoiceLetter
    {
        public Pawn               negotiator;
        public NegotiationRequest request;
        public Lord               negotiatorLord;
        public Map                incidentMap;

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
                string thingLabel = request.thingDef?.label ?? "silver";
                
                if (request.template.demandType == NegotiationDemandType.Goods)
                {
                    int available = ColonyStateReader.GetStockedAmount(incidentMap, request.thingDef);
                    if (available < request.amount)
                    {
                        string failText = $"You no longer have enough {thingLabel} to fulfill this demand. (Required: {request.amount}, Available: {available})";
                        Find.WindowStack.Add(new Dialog_MessageBox(failText));
                        return;
                    }

                    string confirmText = $"Instantly deliver {request.amount} {thingLabel} from your stockpiles to {relatedFaction?.Name}?";
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmText, () =>
                    {
                        int taken = NegotiatorUtil.ConsumeResources(incidentMap, request.thingDef, request.amount);
                        if (taken >= request.amount)
                        {
                            Messages.Message($"Successfully delivered {request.amount} {thingLabel} to the negotiator. They are departing peacefully.", MessageTypeDefOf.PositiveEvent);
                            negotiatorLord?.ReceiveMemo("NegotiatorDismissed");
                            Find.LetterStack.RemoveLetter(this);
                        }
                        else
                        {
                            Messages.Message($"Internal error: Could only find {taken} {thingLabel} during consumption.", MessageTypeDefOf.RejectInput);
                        }
                    }));
                }
                else if (request.template.demandType == NegotiationDemandType.Pawn)
                {
                    Pawn prisoner = request.targetPawn;
                    if (prisoner == null || prisoner.Dead || prisoner.Destroyed || !prisoner.IsPrisonerOfColony)
                    {
                        Find.WindowStack.Add(new Dialog_MessageBox("The required prisoner is no longer available. You must refuse or ignore the demand."));
                        return;
                    }

                    string confirmText = $"Instantly hand over {prisoner.LabelShort} and release them to {relatedFaction?.Name}?";
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmText, () =>
                    {
                        if (NegotiatorUtil.HandoverPrisoner(prisoner, negotiator, incidentMap))
                        {
                            negotiatorLord?.ReceiveMemo("NegotiatorDismissed");
                            Find.LetterStack.RemoveLetter(this);
                        }
                    }));
                }
                else
                {
                    string confirmText =
                        $"Deliver {request.TargetDescription} to {relatedFaction?.Name}?\n\n" +
                        "A compliance timer will begin. Failure to deliver will still trigger a raid.";

                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmText, () =>
                    {
                        Slate slate = new Slate();
                        slate.Set("request",      request);
                        slate.Set("faction",      relatedFaction);
                        slate.Set("map",          incidentMap);

                        Quest quest = QuestGen.Generate(DefDatabase<QuestScriptDef>.GetNamed("RWR_NegotiatorDemand"), slate);
                        Find.QuestManager.Add(quest);
                        quest.Accept(null);

                        negotiatorLord?.ReceiveMemo("NegotiatorAccepted");
                        Find.LetterStack.RemoveLetter(this);
                    }));
                }
            };
            return opt;
        }

        private DiaOption OptionRefuse()
        {
            var opt = new DiaOption("Refuse");
            opt.resolveTree = true;
            opt.action = () =>
            {
                RaidGoalDef goal = DefDatabase<RaidGoalDef>.GetNamedSilentFail($"RaidGoal_{request.template.linkedGoalType}");
                NegotiatorUtil.ScheduleRetaliation(relatedFaction, incidentMap, goal);
                negotiatorLord?.ReceiveMemo("NegotiatorDismissed");
                Find.LetterStack.RemoveLetter(this);
                Find.LetterStack.ReceiveLetter(
                    $"You refused the demand",
                    $"The negotiator from {relatedFaction?.Name} has left, but they will return.",
                    LetterDefOf.NegativeEvent);
            };
            return opt;
        }

        private DiaOption OptionIgnore()
        {
            var opt = new DiaOption("Ignore for now");
            opt.resolveTree = true;
            opt.action = () =>
            {
                Find.LetterStack.RemoveLetter(this);
            };
            return opt;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref negotiator,     "negotiator");
            Scribe_Deep.Look(ref request,               "request");
            Scribe_References.Look(ref negotiatorLord, "negotiatorLord");
            Scribe_References.Look(ref incidentMap,    "incidentMap");
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Ensure request container exists even if scribe fails
                request ??= new NegotiationRequest();
            }
        }
    }
}
