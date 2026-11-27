using System.Collections.Generic;
using RimWorld;
using RimRound.FeedingTube;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Delivers one controlled food serving to a prisoner and starts the saved
    /// two-hour delivery cooldown only after the food is successfully dropped.
    /// </summary>
    public class JobDriver_PrisonerMealDelivery : JobDriver
    {
        private const int MaximumDestinationChanges = 2;
        private const int DestinationCheckInterval = 30;
        private const float MaximumDeliveryDistance = 6f;

        private bool usingNutrientPasteDispenser;
        private bool usingFoodNetworkFaucet;
        private bool eatingFromInventory;
        private bool delivered;
        private int destinationChanges;

        private Pawn Deliveree => job.targetB.Pawn;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref usingNutrientPasteDispenser,
                "usingNutrientPasteDispenser",
                false);
            Scribe_Values.Look(
                ref eatingFromInventory,
                "eatingFromInventory",
                false);
            Scribe_Values.Look(ref delivered, "delivered", false);
            Scribe_Values.Look(ref usingFoodNetworkFaucet, "usingFoodNetworkFaucet", false);
            Scribe_Values.Look(ref destinationChanges,
                "destinationChanges", 0);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            usingNutrientPasteDispenser =
                TargetThingA is Building_NutrientPasteDispenser;
            usingFoodNetworkFaucet = TargetThingA is Building_FoodFaucet;
            eatingFromInventory = pawn.inventory != null &&
                pawn.inventory.Contains(TargetThingA);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(
                Deliveree,
                job,
                1,
                -1,
                null,
                errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(delegate(JobCondition condition)
            {
                if (delivered || pawn?.Spawned != true ||
                    pawn.carryTracker?.CarriedThing == null ||
                    !pawn.IsCarryingThing(job.targetA.Thing)) return;

                Thing placed;
                // Interrupted jobs must leave the actual serving accounted
                // for, even when policy or prison security changes in transit.
                if (!pawn.carryTracker.TryDropCarriedThing(
                        pawn.Position, ThingPlaceMode.Near, out placed))
                    Log.Warning("RimRound could not place interrupted prisoner meal; " +
                        "the warden still carries it.");
            });
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.AddFailCondition(() =>
                !FeedOtherMod.Settings.prisonerFeedingOverhaulEnabled ||
                pawn.Drafted || pawn.InMentalState || Deliveree == null ||
                !Deliveree.IsPrisonerOfColony || Deliveree.InMentalState ||
                PrisonerFatteningFoodPatch.IsFattenPrisoner(Deliveree) &&
                    Deliveree.InBed());

            if (MealSittingToils.CanCollect(pawn, Deliveree))
            {
                foreach (Toil toil in MealSittingToils.Collect(pawn, FaucetMealPurpose.Routine, Deliveree)) yield return toil;
            }
            else if (eatingFromInventory)
            {
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(
                    pawn,
                    TargetIndex.A);
            }
            else if (usingFoodNetworkFaucet)
            {
                foreach (Toil toil in FaucetMealAcquisition.CollectionToils(pawn, Deliveree))
                    yield return toil;
            }
            else if (usingNutrientPasteDispenser)
            {
                yield return Toils_Goto.GotoThing(
                        TargetIndex.A,
                        PathEndMode.InteractionCell)
                    .FailOnForbidden(TargetIndex.A);
                yield return Toils_Ingest.TakeMealFromDispenser(
                    TargetIndex.A,
                    pawn);
            }
            else
            {
                yield return Toils_Ingest.ReserveFoodFromStackForIngesting(
                    TargetIndex.A,
                    Deliveree);
                yield return Toils_Goto.GotoThing(
                        TargetIndex.A,
                        PathEndMode.ClosestTouch)
                    .FailOnForbidden(TargetIndex.A);
                yield return Toils_Ingest.PickupIngestible(
                    TargetIndex.A,
                    Deliveree);
            }

            Toil carryToCell = ToilMaker.MakeToil("CarryPrisonerMealToCell");
            carryToCell.initAction = delegate
            {
                Thing carried = pawn.carryTracker?.CarriedThing;
                if (carried == null || carried.stackCount <= 0 ||
                    carried.def?.ingestible == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Pickup may split a stack or create a paste meal. From here
                // target A must identify the serving actually in the hands.
                job.targetA = carried;
                if (!CanDeliverCarriedMeal() || !StartDeliveryPath())
                    EndJobWith(JobCondition.Incompletable);
            };
            carryToCell.tickAction = delegate
            {
                if (pawn.Position == job.targetC.Cell)
                {
                    if (DeliveryCellValid(job.targetC.Cell))
                        ReadyForNextToil();
                    else if (!StartDeliveryPath())
                        EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (Find.TickManager.TicksGame % DestinationCheckInterval != 0)
                    return;
                if (!DeliveryCellValid(job.targetC.Cell))
                {
                    if (!StartDeliveryPath())
                        EndJobWith(JobCondition.Incompletable);
                }
                else if (!pawn.pather.Moving)
                {
                    EndJobWith(JobCondition.ErroredPather);
                }
            };
            carryToCell.defaultCompleteMode = ToilCompleteMode.Never;
            carryToCell.FailOnDestroyedNullOrForbidden(TargetIndex.B);
            carryToCell.AddFailCondition(delegate
            {
                return !CanDeliverCarriedMeal();
            });
            yield return carryToCell;

            Toil dropMeal = ToilMaker.MakeToil("DropPrisonerMeal");
            dropMeal.initAction = delegate
            {
                // CarryToCell checked this on its previous tick. Policy and
                // prisoner state may change before this instant toil runs.
                if (!CanDeliverCarriedMeal())
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // A prisoner can cross a room boundary on the arrival tick.
                if (pawn.Position != job.targetC.Cell ||
                    !DeliveryCellValid(job.targetC.Cell))
                {
                    if (destinationChanges < MaximumDestinationChanges)
                        JumpToToil(carryToCell);
                    else
                        EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Thing dropped;
                if (pawn.carryTracker.TryDropCarriedThing(
                        pawn.CurJob.targetC.Cell,
                        ThingPlaceMode.Direct,
                        out dropped) && dropped != null && dropped.Spawned &&
                    dropped.Position == pawn.CurJob.targetC.Cell)
                {
                    delivered = MealSittingComponent.Current?.DropDelivery(pawn, Deliveree, pawn.CurJob.targetC.Cell) ?? true;
                    if (!delivered) { EndJobWith(JobCondition.Incompletable); return; }
                    FeedOtherCooldownComponent.NotifyPrisonerMealDelivered(
                        Deliveree);
                }
                else
                    EndJobWith(JobCondition.Incompletable);
            };
            dropMeal.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dropMeal;
        }

        private bool StartDeliveryPath()
        {
            if (job.targetC.IsValid &&
                ++destinationChanges > MaximumDestinationChanges)
                return false;
            IntVec3 cell;
            if (!TryChooseDeliveryCell(out cell)) return false;
            job.targetC = cell;
            if (pawn.Position != cell)
                pawn.pather.StartPath(cell, PathEndMode.OnCell);
            return true;
        }

        private bool TryChooseDeliveryCell(out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            Pawn prisoner = Deliveree;
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (prisoner == null || carried == null ||
                !prisoner.Spawned || prisoner.Map != pawn.Map ||
                !prisoner.Position.IsInPrisonCell(prisoner.Map)) return false;

            IntVec3 preferred = RCellFinder.SpotToChewStandingNear(
                prisoner, carried, DeliveryCellValid);
            if (DeliveryCellValid(preferred))
            {
                cell = preferred;
                return true;
            }

            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(
                prisoner.Position, MaximumDeliveryDistance, true))
            {
                if (!DeliveryCellValid(candidate)) continue;
                cell = candidate;
                return true;
            }
            return false;
        }

        private bool DeliveryCellValid(IntVec3 cell)
        {
            Pawn prisoner = Deliveree;
            Map map = pawn.Map;
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (prisoner == null || map == null || carried == null ||
                !prisoner.Spawned || prisoner.Map != map ||
                !prisoner.Position.IsInPrisonCell(map) ||
                !cell.IsValid || !cell.InBounds(map) ||
                !cell.InHorDistOf(prisoner.Position, MaximumDeliveryDistance) ||
                !cell.Standable(map) || cell.IsForbidden(prisoner) ||
                GenPlace.HaulPlaceBlockerIn(carried, cell, map, false) != null)
                return false;

            Room prisonerRoom = prisoner.GetRoom();
            Room cellRoom = cell.GetRoom(map);
            if (prisonerRoom == null || cellRoom == null ||
                !prisonerRoom.IsPrisonCell || cellRoom != prisonerRoom)
                return false;

            return pawn.CanReach(cell, PathEndMode.OnCell,
                    pawn.NormalMaxDanger()) &&
                prisoner.CanReach(cell, PathEndMode.OnCell, Danger.None);
        }

        private bool CanDeliverCarriedMeal()
        {
            Pawn prisoner = Deliveree;
            Thing carried = pawn.carryTracker?.CarriedThing;
            return FeedOtherMod.Settings.prisonerFeedingOverhaulEnabled &&
                !pawn.Drafted && !pawn.InMentalState &&
                prisoner != null && prisoner.IsPrisonerOfColony &&
                !prisoner.Dead && prisoner.Spawned &&
                prisoner.Map == pawn.Map && !prisoner.InMentalState &&
                prisoner.guest != null &&
                prisoner.guest.PrisonerIsSecure &&
                prisoner.guest.CanBeBroughtFood &&
                !(PrisonerFatteningFoodPatch.IsFattenPrisoner(prisoner) &&
                    prisoner.InBed()) &&
                carried != null && pawn.IsCarryingThing(job.targetA.Thing) &&
                FoodUtility.WillEat(prisoner, carried, pawn, true, false);
        }
    }
}
