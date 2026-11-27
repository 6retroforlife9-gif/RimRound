using RimRound.Comps;
using RimRound.Things;
using RimWorld;
using Verse;

namespace RimRound.Utilities
{
    public static class BlobBedComfort
    {
        public static int MoodStage(Pawn host)
        {
            if(!BodyModelEligibility.IsEligible(host)||!host.Spawned||
                !host.health.capacities.CanBeAwake||
                !(host.TryGetComp<MakeBlobIntoBed_ThingComp>()?.blobBed is Building_BlobBed bed)||
                !bed.IsOperational||bed.originatingPawn!=host)return -1;
            foreach(Pawn guest in bed.CurOccupants)
            {
                if(guest==host||!BodyModelEligibility.IsEligible(guest)||guest.CurrentBed()!=bed)continue;
                int stage=guest.WeightHediff()?.CurStageIndex??-1;
                // The same biological hediff stages used by stage health perks;
                // artwork overrides do not alter this benefit. Gelatinous I is
                // the first full blob stage in the current stage definition.
                if(stage>=19)return 2;
                if(stage>=StageHealthPerks.FoodImmunityStageIndex)return 1;
                if(stage>=StageHealthPerks.HealingStageIndex)return 0;
            }
            return -1;
        }
    }

    public sealed class ThoughtWorker_BlobBedHeavyCompany : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            int stage=BlobBedComfort.MoodStage(p);
            return stage<0?ThoughtState.Inactive:ThoughtState.ActiveAtStage(stage);
        }
    }
}
