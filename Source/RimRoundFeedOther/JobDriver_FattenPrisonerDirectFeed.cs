using System.Collections.Generic;
using RimRound.FeedingTube;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Caregiver-only one-serving Fatten job. The prisoner retains native rest
    /// ownership while the warden performs every toil.
    /// </summary>
    public class JobDriver_FattenPrisonerDirectFeed : JobDriver_FoodFeedPatient
    {
        private bool usingFoodNetworkFaucet;

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            usingFoodNetworkFaucet = TargetThingA is Building_FoodFaucet;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref usingFoodNetworkFaucet, "usingFoodNetworkFaucet", false);
        }

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
                pawn.Drafted || pawn.InMentalState ||
                !PrisonerFatteningFoodPatch.CanContinueFattenJob(Deliveree));

            if (MealSittingToils.CanCollect(pawn, Deliveree))
            {
                foreach (Toil toil in MealSittingToils.Care(this, Deliveree, FaucetMealPurpose.Fatten, true)) yield return toil;
                yield break;
            }

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
            else if (usingFoodNetworkFaucet || TargetThingA is Building_FoodFaucet)
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
                    if (FeedOtherUtility.ResumeCarriedNetworkMeal(actor)) return;
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
                        FaucetMealAcquisition.CollectForPurpose(faucet, actor, Deliveree,
                            FaucetMealPurpose.Fatten);
                    if (serving == null ||
                        !actor.carryTracker.TryStartCarry(serving))
                    {
                        if (serving != null && !serving.Destroyed)
                        {
                            FoodNetworkServingRecovery.TryReturnOrPlace(
                                faucet, serving, actor);
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

            Toil chew = FeedOtherUtility.ChewIngestibleWithEatingSpeed(
                    Deliveree,
                    FeedOtherUtility.AssistedEatingDurationFactor,
                    TargetIndex.A,
                    TargetIndex.None)
                .FailOnCannotTouch(TargetIndex.B, PathEndMode.Touch);
            chew.AddFailCondition(() =>
            {
                Thing food = pawn.carryTracker?.CarriedThing;
                return food == null ||
                    !FoodUtility.WillEat(Deliveree, food, pawn, true, false);
            });
            yield return chew;

            Toil finalize = Toils_Ingest.FinalizeIngest(
                Deliveree,
                TargetIndex.A);
            System.Action nativeFinalize = finalize.initAction;
            bool consumed = false;
            finalize.initAction = delegate
            {
                Thing food = pawn.CurJob?.GetTarget(TargetIndex.A).Thing;
                if (!FeedOtherUtility.CanFinalizeCareIngestion(
                        Deliveree,
                        pawn,
                        food,
                        job,
                        true))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                nativeFinalize();
                consumed = true;
            };
            finalize.AddFinishAction(delegate
            {
                if (consumed)
                {
                    PrisonerFatteningFoodPatch.CompleteFattenServing(Deliveree);
                }
            });
            yield return finalize;
        }
    }
}
