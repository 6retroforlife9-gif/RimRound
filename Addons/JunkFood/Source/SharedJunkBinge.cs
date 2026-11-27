using RimRound.Comps;
using RimWorld;
using Verse;
using Verse.AI;

namespace Oinky.JunkFood
{
    public sealed class MentalBreakWorker_JunkFoodBinge : MentalBreakWorker
    {
        public override float CommonalityFor(Pawn pawn, bool moodCaused = false)
        {
            int degree = JunkSharedRules.TraitDegree(pawn);
            return base.CommonalityFor(pawn, moodCaused) * (degree == 1 ? 2f : degree >= 2 ? 5f : 1f);
        }
    }

    public sealed class MentalStateWorker_JunkFoodBinge : MentalStateWorker
    {
        public override bool StateCanOccur(Pawn pawn)
        {
            if (!base.StateCanOccur(pawn) || !JunkSharedRules.HabitsEnabled || pawn.InMentalState ||
                !JunkSnackUtility.Ready(pawn, true)) return false;
            var state = JunkFoodPolicy.Colony.SharedRecord(pawn, false)?.State;
            var any = pawn.needs.TryGetNeed<Need_Chemical_Any>();
            bool craving = state?.Habit == true && state.NeedLevel <= .1d;
            bool interested = JunkSharedRules.TraitDegree(pawn) > 0 && any != null &&
                any.MoodBuffForCurrentLevel <= Need_Chemical_Any.MoodBuff.Neutral;
            return (craving || interested) &&
                JunkSnackUtility.FindFood(pawn, JunkSnackPurpose.Binge, prospective: true) != null;
        }
    }

    public sealed class MentalState_JunkFoodBinge : MentalState_Binging
    {
        public int NextSnackTick, NextStockCheckTick, EmptyChecks;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref NextSnackTick, "nextJunkSnackTick");
            Scribe_Values.Look(ref NextStockCheckTick, "nextJunkStockCheckTick");
            Scribe_Values.Look(ref EmptyChecks, "emptyJunkStockChecks");
        }
        public override void PostStart(string reason)
        {
            base.PostStart(reason);
            NextSnackTick = JunkSharedRules.Now;
            NextStockCheckTick = JunkSharedRules.Now + 600;
        }
        public void NotifyUsefulSnack()
        {
            NextSnackTick = JunkSharedRules.Now + 600;
            NextStockCheckTick = NextSnackTick;
            EmptyChecks = 0;
        }
        public override void MentalStateTick(int delta)
        {
            if (!JunkSnackUtility.Wants(pawn, JunkSnackPurpose.Binge) || !pawn.Spawned)
            { RecoverFromState(); return; }
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null || body.CurrentFullness >= body.HardLimit - .000001f)
            { RecoverFromState(); return; }
            base.MentalStateTick(delta);
            if (pawn.MentalState != this || JunkSharedRules.Now < NextStockCheckTick) return;
            NextStockCheckTick = JunkSharedRules.Now + 600;
            // A walking/chewing owned job already holds a valid source. A real
            // loss is counted twice, 600 ticks apart, rather than every AI scan.
            if (pawn.CurJob?.def?.defName == JunkSnackUtility.JobName) return;
            if (JunkSnackUtility.FindFood(pawn, JunkSnackPurpose.Binge) == null)
            { if (++EmptyChecks >= 2) RecoverFromState(); }
            else EmptyChecks = 0;
        }
    }

    public sealed class JobGiver_JunkFoodBinge : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var state = pawn.MentalState as MentalState_JunkFoodBinge;
            return state == null || JunkSharedRules.Now < state.NextSnackTick ? null :
                JunkSnackUtility.TryJob(pawn, JunkSnackPurpose.Binge);
        }
    }
}
