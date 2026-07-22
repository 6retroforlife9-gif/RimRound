using System.Collections.Generic;
using RimWorld;
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
        private bool usingNutrientPasteDispenser;
        private bool eatingFromInventory;

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
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            usingNutrientPasteDispenser =
                TargetThingA is Building_NutrientPasteDispenser;
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
            this.FailOnDespawnedOrNull(TargetIndex.B);

            if (eatingFromInventory)
            {
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(
                    pawn,
                    TargetIndex.A);
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
                Pawn actor = carryToCell.actor;
                actor.pather.StartPath(actor.CurJob.targetC, PathEndMode.OnCell);
            };
            carryToCell.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            carryToCell.FailOnDestroyedNullOrForbidden(TargetIndex.B);
            carryToCell.AddFailCondition(delegate
            {
                if (!pawn.IsCarryingThing(job.targetA.Thing))
                {
                    return true;
                }

                Pawn prisoner = job.targetB.Pawn;
                return prisoner == null || !prisoner.IsPrisonerOfColony ||
                    prisoner.guest == null ||
                    !prisoner.guest.CanBeBroughtFood;
            });
            yield return carryToCell;

            Toil dropMeal = ToilMaker.MakeToil("DropPrisonerMeal");
            dropMeal.initAction = delegate
            {
                Thing dropped;
                if (pawn.carryTracker.TryDropCarriedThing(
                        pawn.CurJob.targetC.Cell,
                        ThingPlaceMode.Direct,
                        out dropped))
                {
                    FeedOtherCooldownComponent.NotifyPrisonerMealDelivered(
                        Deliveree);
                }
            };
            dropMeal.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dropMeal;
        }
    }
}
