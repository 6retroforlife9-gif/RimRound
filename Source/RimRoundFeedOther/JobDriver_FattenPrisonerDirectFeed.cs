using System.Collections.Generic;
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
