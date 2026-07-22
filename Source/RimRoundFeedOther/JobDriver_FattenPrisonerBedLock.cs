using RimWorld;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Permanent bed-rest driver used only while RR_Fatten is selected. It never
    /// searches for another job, so normal prisoner wandering, eating and prison
    /// break job overrides cannot pull the prisoner out of bed.
    /// </summary>
    public class JobDriver_FattenPrisonerBedLock : JobDriver_LayDown
    {
        public override bool LookForOtherJobs => false;
    }
}
