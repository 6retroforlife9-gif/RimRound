using System;
using System.Collections.Generic;
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
