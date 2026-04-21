using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RaidsWithinReason
{
    public class LordToil_PursueGoal : LordToil
    {
        private readonly RaidGoalDef goal;

        private Pawn    targetPawn;
        private IntVec3 targetCell;
        private Pawn    designatedKidnapper; // only this raider gets DutyDefOf.Kidnap
        private bool    hasBeenResisted; // tracks if the raid was attacked during loot

        public LordToil_PursueGoal(RaidGoalDef goal)
        {
            this.goal = goal;
        }

        public override bool AllowSelfTend => false;

        public override void Init()
        {
            base.Init();
            RefreshTargets();
        }

        public override void LordToilTick()
        {
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                if (goal.goalType == RaidGoalType.Loot)
                {
                    if (!hasBeenResisted && CheckIfResisted())
                    {
                        hasBeenResisted = true;
                    }
                    UpdateAllDuties();
                }
                else if (goal.goalType == RaidGoalType.Capture || goal.goalType == RaidGoalType.Revenge || goal.goalType == RaidGoalType.ReleasePrisoner)
                {
                    if (goal.goalType == RaidGoalType.ReleasePrisoner && Map != null)
                        CheckForRescue(Map);

                    UpdateAllDuties();
                }
            }
        }

        public override void UpdateAllDuties()
        {
            RefreshTargets();

            foreach (Pawn pawn in lord.ownedPawns)
            {
                PawnDuty newDuty = BuildDuty(pawn);
                if (pawn.mindState.duty == null || pawn.mindState.duty.def != newDuty.def || pawn.mindState.duty.focus != newDuty.focus)
                {
                    pawn.mindState.duty = newDuty;
                }

                if (goal.goalType == RaidGoalType.Revenge || goal.goalType == RaidGoalType.Capture)
                    ForcePursuitAttack(pawn);
            }
        }

        private void ForcePursuitAttack(Pawn raider)
        {
            if (targetPawn == null || targetPawn.Dead || targetPawn.Destroyed) return;
            if (raider.Dead || raider.Downed || !raider.Spawned) return;

            // Bug #3: let self-defense take priority when an enemy is actively meleeing this raider.
            if (IsUnderMeleeAttack(raider)) return;

            bool isRanged = raider.equipment?.Primary?.def?.IsRangedWeapon ?? false;

            if (targetPawn.Downed)
            {
                if (goal.goalType == RaidGoalType.Revenge)
                {
                    if (raider.CurJob?.targetA.Thing != targetPawn)
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, targetPawn);
                        job.killIncappedTarget = true;
                        raider.jobs.StartJob(
                            job,
                            JobCondition.InterruptForced,
                            canReturnCurJobToPool: false);
                    }
                }
            }
            else if (!isRanged)
            {
                // Melee raider vs alive target: force approach and attack.
                if (raider.CurJob?.targetA.Thing != targetPawn && raider.CurJob?.def != JobDefOf.Kidnap)
                    raider.jobs.StartJob(
                        JobMaker.MakeJob(JobDefOf.AttackMelee, targetPawn),
                        JobCondition.InterruptForced,
                        canReturnCurJobToPool: false);
            }
            else
            {
                // Ranged raiders: bias the AI's target selection without forcing melee approach.
                // The AssaultColony duty already moves them toward targetPawn.Position.
                raider.mindState.enemyTarget = targetPawn;
            }
        }

        private static bool IsUnderMeleeAttack(Pawn raider)
        {
            foreach (IntVec3 offset in GenAdj.AdjacentCells)
            {
                IntVec3 cell = raider.Position + offset;
                if (!cell.InBounds(raider.Map)) continue;
                foreach (Thing t in cell.GetThingList(raider.Map))
                {
                    if (t is Pawn attacker && attacker.HostileTo(raider.Faction)
                        && !attacker.Downed && attacker.CurJob?.targetA.Thing == raider)
                        return true;
                }
            }
            return false;
        }

        private void RefreshTargets()
        {
            Map map = Map;
            if (map == null) return;

            var tracker = map.GetComponent<RaidGoalTracker>();

            switch (goal.goalType)
            {
                case RaidGoalType.Loot:
                    // No central target for Loot; DutyDefOf.Steal handles individual item selection per pawn.
                    break;

                case RaidGoalType.Capture:
                {
                    Pawn current = tracker?.GetTargetPawn(lord);
                    // Lock onto the initial target; never switch to a new colonist.
                    targetPawn = (current != null && !current.Dead && !current.Destroyed)
                        ? current
                        : ColonyStateReader.GetRandomNotableColonist(map);
                    targetCell = targetPawn?.Position ?? IntVec3.Invalid;
                    tracker?.SetTargetPawn(lord, targetPawn);

                    // Designate exactly one raider as the kidnapper when the target is downed.
                    // Only this pawn gets DutyDefOf.Kidnap; reservation contention causes all
                    // others to fall through to JobGiver_ExitMap and abandon the raid.
                    if (targetPawn != null && targetPawn.Downed && targetPawn.CarriedBy == null)
                        designatedKidnapper = lord.ownedPawns
                            .Where(p => !p.Dead && !p.Downed)
                            .OrderBy(p => p.Position.DistanceTo(targetPawn.Position))
                            .FirstOrDefault();
                    else if (targetPawn == null || targetPawn.CarriedBy != null)
                        designatedKidnapper = null;
                    break;
                }

                case RaidGoalType.Destroy:
                {
                    Room room  = ColonyStateReader.GetRandomRoomByPurpose(map);
                    Building b = room != null ? FindPrimaryBuilding(room, map) : null;
                    targetCell = b?.Position ?? (room != null ? room.Cells.RandomElement() : IntVec3.Invalid);
                    tracker?.SetTargetBuilding(lord, b);
                    break;
                }

                case RaidGoalType.Revenge:
                {
                    Pawn current = tracker?.GetTargetPawn(lord);
                    if (current != null && current.Dead)
                    {
                        // Target is dead — mission accomplished, fall back.
                        tracker.MarkSuccess(lord);
                        return;
                    }
                    // Lock onto the initial target; never switch to a new colonist.
                    targetPawn = (current != null && !current.Destroyed)
                        ? current
                        : ColonyStateReader.GetRandomNotableColonist(map);
                    targetCell = targetPawn?.Position ?? IntVec3.Invalid;
                    tracker?.SetTargetPawn(lord, targetPawn);
                    break;
                }

                case RaidGoalType.ReleasePrisoner:
                {
                    Pawn current = tracker?.GetTargetPawn(lord);
                    // Use the pending target if we just started, otherwise stay locked on.
                    targetPawn = (current != null && current.IsPrisonerOfColony) 
                        ? current 
                        : Patch_IncidentWorker_Raid_TryExecuteWorker._pendingTarget;

                    if (targetPawn == null || !targetPawn.IsPrisonerOfColony)
                    {
                        // Mission accomplished or target lost
                        tracker?.MarkSuccess(lord);
                        return;
                    }
                    targetCell = targetPawn.Position;
                    tracker?.SetTargetPawn(lord, targetPawn);
                    break;
                }
            }
        }

        private PawnDuty BuildDuty(Pawn pawn)
        {
            switch (goal.goalType)
            {
                case RaidGoalType.Loot:
                    if (hasBeenResisted)
                        return new PawnDuty(DutyDefOf.AssaultColony);

                    Thing item = FindLootFor(pawn);
                    if (item != null)
                    {
                        // Native Steal AI handles picking unique items and leaving. 
                        // But if triggered from >120 cells away, it returns null and abandons the map instantly!
                        // We MUST bring them close first.
                        if (pawn.CanReserveAndReach(item, PathEndMode.ClosestTouch, Danger.Some))
                        {
                            if (pawn.Position.DistanceToSquared(item.Position) > 900) // ~30 cells
                                return new PawnDuty(DutyDefOf.Defend, item.Position, 8f);

                            return new PawnDuty(DutyDefOf.Steal); 
                        }
                        
                        // Blocked by doors or heavily guarded? Bash our way in!
                        return new PawnDuty(DutyDefOf.AssaultColony, item.Position);
                    }
                    
                    return new PawnDuty(DutyDefOf.AssaultColony);

                case RaidGoalType.Capture:
                    if (targetPawn == null || targetPawn.Dead || targetPawn.Destroyed)
                        return new PawnDuty(DutyDefOf.AssaultColony);

                    // This raider is already carrying the target — keep going.
                    if (pawn.carryTracker?.CarriedThing == targetPawn)
                        return new PawnDuty(DutyDefOf.Kidnap, targetPawn);

                    // Someone else is already carrying the target — escort/assault so we
                    // don't interfere and strip the target from the carrier's arms.
                    if (targetPawn.CarriedBy != null)
                        return new PawnDuty(DutyDefOf.AssaultColony);

                    // Target is downed — only the designated kidnapper picks them up.
                    if (targetPawn.Downed)
                    {
                        if (pawn == designatedKidnapper)
                            return new PawnDuty(DutyDefOf.Kidnap, targetPawn);
                        return new PawnDuty(DutyDefOf.AssaultColony);
                    }

                    // Target is still up — converge and attack them.
                    return new PawnDuty(DutyDefOf.AssaultColony, targetPawn);

                case RaidGoalType.Destroy:
                    if (targetCell.IsValid)
                        return new PawnDuty(DutyDefOf.AssaultColony, targetCell);
                    return new PawnDuty(DutyDefOf.AssaultColony);

                case RaidGoalType.Revenge:
                    if (targetPawn == null || targetPawn.Dead || targetPawn.Destroyed)
                        return new PawnDuty(DutyDefOf.AssaultColony);
                    // Focus all raiders on the target directly, including when downed.
                    return new PawnDuty(DutyDefOf.AssaultColony, targetPawn);

                case RaidGoalType.ReleasePrisoner:
                    if (targetPawn == null || !targetPawn.IsPrisonerOfColony)
                        return new PawnDuty(DutyDefOf.AssaultColony);
                    return new PawnDuty(DutyDefOf.AssaultColony, targetPawn);

                default:
                    return new PawnDuty(DutyDefOf.AssaultColony);
            }
        }

        // Most valuable non-wall building in the room — raiders will target this specifically.
        internal static Building FindPrimaryBuilding(Room room, Map map)
        {
            Building best      = null;
            float    bestValue = 0f;
            foreach (IntVec3 cell in room.Cells)
            {
                foreach (Thing t in cell.GetThingList(map))
                {
                    if (!(t is Building b)) continue;
                    if (b.def.passability == Traversability.Impassable) continue; // skip walls
                    if (b.def.IsDoor) continue;
                    float v = b.MarketValue;
                    if (v > bestValue) { bestValue = v; best = b; }
                }
            }
            return best;
        }

        private Thing FindLootFor(Pawn p)
        {
            Thing best = null;
            float bestValue = 0f;

            foreach (Zone zone in p.Map.zoneManager.AllZones)
            {
                if (zone is Zone_Stockpile stockpile)
                {
                    foreach (Thing t in stockpile.AllContainedThings)
                    {
                        float v = t.MarketValue * t.stackCount;
                        if (v > bestValue) { bestValue = v; best = t; }
                    }
                }
            }

            foreach (Building b in p.Map.listerBuildings.allBuildingsColonist)
            {
                if (b is Building_Storage storage && storage.slotGroup != null)
                {
                    foreach (Thing t in storage.slotGroup.HeldThings)
                    {
                        float v = t.MarketValue * t.stackCount;
                        if (v > bestValue) { bestValue = v; best = t; }
                    }
                }
            }

            return best;
        }

        private bool CheckIfResisted()
        {
            foreach (Pawn raider in lord.ownedPawns)
            {
                if (raider.Dead || raider.Downed) return true;
                
                // If they are bleeding, they likely just got shot or cut
                if (raider.health.hediffSet.BleedRateTotal > 0.001f) return true;

                if (IsUnderMeleeAttack(raider)) return true;
                
                // If they have acquired an enemy target, they are in combat
                if (raider.mindState.enemyTarget != null) return true;
            }
            return false;
        }
        private void CheckForRescue(Map map)
        {
            if (targetPawn == null || !targetPawn.IsPrisonerOfColony) return;

            foreach (Pawn raider in lord.ownedPawns)
            {
                if (!raider.Dead && !raider.Downed && raider.Position.AdjacentTo8WayOrInside(targetPawn))
                {
                    // Free them using the native system!
                    GenGuest.PrisonerRelease(targetPawn);
                    targetPawn.SetFaction(raider.Faction);
                    
                    // Rescued pawn should leave
                    Lord rescueLord = LordMaker.MakeNewLord(raider.Faction, new LordJob_ExitMapNear(), map);
                    rescueLord.AddPawn(targetPawn);

                    Messages.Message($"{targetPawn.LabelShort} has been freed by {raider.Faction.Name} raiders!", targetPawn, MessageTypeDefOf.NegativeEvent);
                    
                    map.GetComponent<RaidGoalTracker>()?.MarkSuccess(lord);
                    break;
                }
            }
        }
    }
}
