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
            this.FailOn(() => !FoodUtility.ShouldBeFedBySomeone(Deliveree));

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
                    FoodNetworkV2ServingUtility.TryDispenseMeals(
                        faucet,
                        Deliveree,
                        1);
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
                actor.CurJob.count =
                    actor.carryTracker.CarriedThing.stackCount;
            };
            takeFoodNetworkMeal.defaultCompleteMode =
                ToilCompleteMode.Delay;
            takeFoodNetworkMeal.defaultDuration =
                Building_NutrientPasteDispenser.CollectDuration;
            yield return takeFoodNetworkMeal;
            yield return carryFoodToPatient;

            yield return FeedOtherUtility.ChewIngestibleWithEatingSpeed(
                    Deliveree,
                    FeedOtherUtility.AssistedEatingDurationFactor,
                    TargetIndex.A,
                    TargetIndex.None)
                .FailOnCannotTouch(
                    TargetIndex.B,
                    PathEndMode.Touch);

            Toil finalize = Toils_Ingest.FinalizeIngest(
                Deliveree,
                TargetIndex.A);
            finalize.finishActions = new List<Action>
            {
                delegate
                {
                    if (ModsConfig.AnomalyActive &&
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
