using System.Collections.Generic;
using RimRound.FeedingTube;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Caregiver-only one-serving Fatten job. The prisoner retains the permanent
    /// bed-lock job (or native downed job) while the warden performs every toil.
    /// </summary>
    public class JobDriver_FattenPrisonerDirectFeed : JobDriver_FoodFeedPatient
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (TargetThingA is Building_FoodFaucet)
            {
                return pawn.Reserve(
                    Deliveree,
                    job,
                    1,
                    -1,
                    null,
                    errorOnFailed);
            }

            return base.TryMakePreToilReservations(errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() =>
                !PrisonerFatteningFoodPatch.CanContinueFattenJob(Deliveree));

            if (pawn.inventory != null && pawn.inventory.Contains(TargetThingA))
            {
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(
                    pawn,
                    TargetIndex.A);
            }
            else if (TargetThingA is Building_NutrientPasteDispenser)
            {
                yield return Toils_Goto.GotoThing(
                        TargetIndex.A,
                        PathEndMode.InteractionCell)
                    .FailOnForbidden(TargetIndex.A);
                yield return Toils_Ingest.TakeMealFromDispenser(
                    TargetIndex.A,
                    pawn);
            }
            else if (TargetThingA is Building_FoodFaucet)
            {
                yield return Toils_Goto.GotoThing(
                        TargetIndex.A,
                        PathEndMode.InteractionCell)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.A);

                Toil takeNetworkMeal = ToilMaker.MakeToil(
                    "TakeFattenMealFromFoodNetwork");
                takeNetworkMeal.initAction = delegate
                {
                    Pawn actor = takeNetworkMeal.actor;
                    Building_FoodFaucet faucet = actor?.CurJob
                        ?.GetTarget(TargetIndex.A).Thing as
                            Building_FoodFaucet;
                    if (actor == null || faucet == null ||
                        actor.carryTracker == null ||
                        actor.carryTracker.CarriedThing != null)
                    {
                        actor?.jobs?.curDriver?.EndJobWith(
                            JobCondition.Incompletable);
                        return;
                    }

                    Thing serving =
                        FoodNetworkV2ServingUtility.TryDispenseOneMeal(
                            faucet);
                    if (serving == null ||
                        !actor.carryTracker.TryStartCarry(serving))
                    {
                        if (serving != null && !serving.Destroyed)
                        {
                            FoodNetworkV2ServingUtility.TryReturnToNetwork(
                                faucet,
                                serving);
                            serving.Destroy(DestroyMode.Vanish);
                        }
                        actor.jobs.curDriver.EndJobWith(
                            JobCondition.Incompletable);
                        return;
                    }

                    actor.CurJob.SetTarget(
                        TargetIndex.A,
                        actor.carryTracker.CarriedThing);
                    actor.CurJob.count = 1;
                };
                takeNetworkMeal.defaultCompleteMode =
                    ToilCompleteMode.Delay;
                takeNetworkMeal.defaultDuration =
                    Building_NutrientPasteDispenser.CollectDuration;
                yield return takeNetworkMeal;
            }
            else
            {
                yield return Toils_Goto.GotoThing(
                        TargetIndex.A,
                        PathEndMode.ClosestTouch)
                    .FailOnForbidden(TargetIndex.A);
                yield return Toils_Ingest.PickupIngestible(
                    TargetIndex.A,
                    Deliveree);
            }

            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

            yield return FeedOtherUtility.ChewIngestibleWithEatingSpeed(
                    Deliveree,
                    FeedOtherUtility.AssistedEatingDurationFactor,
                    TargetIndex.A,
                    TargetIndex.None)
                .FailOnCannotTouch(TargetIndex.B, PathEndMode.Touch);

            Toil finalize = Toils_Ingest.FinalizeIngest(
                Deliveree,
                TargetIndex.A);
            finalize.AddFinishAction(delegate
            {
                PrisonerFatteningFoodPatch.CompleteFattenServing(Deliveree);
            });
            yield return finalize;
        }
    }
}
