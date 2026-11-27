using System;
using System.Collections.Generic;
using RimRound.FeedingTube;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Vanilla-compatible patient/prisoner feeding driver whose chew duration
    /// uses the recipient's live Eating Speed and the assisted-feeding bonus.
    /// </summary>
    public class JobDriver_FoodFeedPatientEatingSpeed : JobDriver_FoodFeedPatient
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
            this.FailOn(() => pawn.Drafted || pawn.InMentalState ||
                !FoodUtility.ShouldBeFedBySomeone(Deliveree));

            if (MealSittingToils.CanCollect(pawn, Deliveree))
            {
                foreach (Toil toil in MealSittingToils.Care(this, Deliveree, FaucetMealPurpose.Routine, false)) yield return toil;
                yield break;
            }

            Toil carryFoodFromInventory =
                Toils_Misc.TakeItemFromInventoryToCarrier(pawn, TargetIndex.A);
            Toil goToNutrientDispenser = Toils_Goto.GotoThing(
                    TargetIndex.A,
                    PathEndMode.InteractionCell)
                .FailOnForbidden(TargetIndex.A);
            Toil goToFoodNetworkFaucet = Toils_Goto.GotoThing(
                    TargetIndex.A,
                    PathEndMode.InteractionCell)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);
            Toil goToFoodHolder = Toils_Goto.GotoThing(
                    TargetIndex.C,
                    PathEndMode.Touch)
                .FailOn(() =>
                    FoodHolder != FoodHolderInventory?.pawn ||
                    FoodHolder.IsForbidden(pawn));
            Toil carryFoodToPatient =
                Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

            yield return Toils_Jump.JumpIf(
                carryFoodFromInventory,
                () => pawn.inventory != null &&
                    pawn.inventory.Contains(TargetThingA));
            yield return Toils_Haul.CheckItemCarriedByOtherPawn(
                Food,
                TargetIndex.C,
                goToFoodHolder);
            yield return Toils_Jump.JumpIf(
                goToNutrientDispenser,
                () => TargetThingA is Building_NutrientPasteDispenser);
            yield return Toils_Jump.JumpIf(
                goToFoodNetworkFaucet,
                () => TargetThingA is Building_FoodFaucet);
            yield return Toils_Goto.GotoThing(
                    TargetIndex.A,
                    PathEndMode.ClosestTouch)
                .FailOnForbidden(TargetIndex.A);
            yield return Toils_Ingest.PickupIngestible(
                TargetIndex.A,
                Deliveree);
            yield return Toils_Jump.Jump(carryFoodToPatient);
            yield return goToFoodHolder;
            yield return Toils_General.Wait(25)
                .WithProgressBarToilDelay(TargetIndex.C);
            yield return Toils_Haul.TakeFromOtherInventory(
                Food,
                pawn.inventory.innerContainer,
                FoodHolderInventory?.innerContainer,
                job.count,
                TargetIndex.A);
            yield return carryFoodFromInventory;
            yield return Toils_Jump.Jump(carryFoodToPatient);
            yield return goToNutrientDispenser;
            yield return Toils_Ingest.TakeMealFromDispenser(
                TargetIndex.A,
                pawn);
            yield return Toils_Jump.Jump(carryFoodToPatient);
            yield return goToFoodNetworkFaucet;

            Toil takeFoodNetworkMeal = ToilMaker.MakeToil(
                "TakePatientMealFromFoodNetwork");
            takeFoodNetworkMeal.initAction = delegate
            {
                Pawn actor = takeFoodNetworkMeal.actor;
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

                actor.rotationTracker.FaceTarget(faucet);
                Thing serving =
                    FaucetMealAcquisition.Collect(
                        faucet,
                        actor,
                        Deliveree);
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
                actor.CurJob.count =
                    actor.carryTracker.CarriedThing.stackCount;
            };
            takeFoodNetworkMeal.defaultCompleteMode =
                ToilCompleteMode.Delay;
            takeFoodNetworkMeal.defaultDuration =
                Building_NutrientPasteDispenser.CollectDuration;
            yield return takeFoodNetworkMeal;
            yield return carryFoodToPatient;

            Toil chew = FeedOtherUtility.ChewIngestibleWithEatingSpeed(
                    Deliveree,
                    FeedOtherUtility.AssistedEatingDurationFactor,
                    TargetIndex.A,
                    TargetIndex.None,
                    1.5f)
                .FailOnCannotTouch(
                    TargetIndex.B,
                    PathEndMode.Touch);
            chew.AddFailCondition(() =>
            {
                Thing food = pawn.carryTracker?.CarriedThing;
                return Deliveree?.TryGetComp<RimRound.Comps.FullnessAndDietStats_ThingComp>()
                        ?.UsesBodyModel == true &&
                    (food == null ||
                     !FoodUtility.WillEat(Deliveree, food, pawn, true, false));
            });
            yield return chew;

            Toil finalize = Toils_Ingest.FinalizeIngest(
                Deliveree,
                TargetIndex.A);
            Action nativeFinalize = finalize.initAction;
            bool consumed = false;
            finalize.initAction = delegate
            {
                Thing food = pawn.CurJob?.GetTarget(TargetIndex.A).Thing;
                if (!FeedOtherUtility.CanFinalizeCareIngestion(
                        Deliveree,
                        pawn,
                        food,
                        job,
                        false))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                nativeFinalize();
                consumed = true;
            };
            finalize.finishActions = new List<Action>
            {
                delegate
                {
                    if (consumed && ModsConfig.AnomalyActive &&
                        Rand.Chance(0.3f) &&
                        MetalhorrorUtility.IsInfected(pawn))
                    {
                        MetalhorrorUtility.Infect(
                            Deliveree,
                            pawn,
                            "FeedingImplant");
                    }
                }
            };
            yield return finalize;
        }
    }
}
