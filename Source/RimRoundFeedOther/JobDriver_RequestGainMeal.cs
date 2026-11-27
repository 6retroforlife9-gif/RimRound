using System.Collections.Generic;
using RimRound.Comps;
using RimRound.Simulation;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    // The eater's selected job is the commit point for recruiting a helper.
    // The helper's existing driver owns reservations and the linked eating job.
    public sealed class JobDriver_RequestGainMeal : JobDriver
    {
        private bool dispatched;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref dispatched, "rrGainMealDispatched", false);
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil request = ToilMaker.MakeToil("RequestGainMeal");
            request.defaultCompleteMode = ToilCompleteMode.Delay;
            request.defaultDuration = 60;
            request.initAction = () =>
            {
                var state = pawn.TryGetComp<FullnessAndDietStats_ThingComp>()?.GainMealState;
                Job invitation;
                Pawn helper = job.targetA.Pawn;
                if (dispatched || !SocialFeedingPolicy.TryCreatePairJob(helper, pawn, job.count == 1, out invitation))
                {
                    // A rejected request falls back to solo food. Accepted
                    // invitations mark the attempt in the helper's driver,
                    // before starting either linked participant.
                    if (state != null) state.SocialRetryAfterTick = Find.TickManager.TicksGame + GainMealCycle.RetryDelayTicks;
                    EndJobWith(JobCondition.Incompletable); return;
                }
                dispatched = true;
                helper.jobs.StartJob(invitation, JobCondition.InterruptOptional, resumeCurJobAfterwards: true);
                // The linked recipient driver replaces this request. A helper
                // that cannot start leaves only this bounded wait, then solo food.
            };
            yield return request;
        }
    }
}
