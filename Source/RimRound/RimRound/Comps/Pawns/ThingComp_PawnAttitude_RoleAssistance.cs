using RimRound.Ideology;
using RimRound.Simulation;
using RimRound.Utilities;
using Verse;

namespace RimRound.Comps
{
    public partial class ThingComp_PawnAttitude
    {
        private bool followerMealsEnabled;
        private int nextFollowerMealTick;
        public bool FollowerMealsEnabled => followerMealsEnabled;
        public int NextFollowerMealTick => nextFollowerMealTick;
        private void ExposeRoleAssistance()
        {
            Scribe_Values.Look(ref followerMealsEnabled, "rrFollowerMealsEnabled", false);
            Scribe_Values.Look(ref nextFollowerMealTick, "rrNextFollowerMealTick", 0);
        }
        public void SetFollowerMealsEnabled(bool enabled) => followerMealsEnabled = enabled;
        public void NotifyFollowerMealStarted(int tick)
        {
            if (tick >= 0 && tick <= int.MaxValue - 2500)
                nextFollowerMealTick = tick + 2500;
        }
        public bool WillAcceptFollowerMeal
        {
            get
            {
                Pawn pawn = parent as Pawn;
                if (!followerMealsEnabled || !HasPreference || !BodyModelEligibility.CanFollowBodyGoal(pawn) ||
                    !pawn.Spawned || !pawn.IsColonist || pawn.IsPrisoner || pawn.IsSlave ||
                    BodyRoleExpectations.Instance?.For(pawn) == null) return false;
                var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
                float lower, upper;
                return body?.UsesBodyGoals == true && body.Goal == BodyGoal.Gain &&
                    TryGetPreferredRangeKg(out lower, out upper) &&
                    body.ProjectedBodyMassKg < lower && body.HasGainAppetite &&
                    BodyFeedingPolicy.CanUseExtraGainMeal(pawn);
            }
        }

        public string FollowerMealStatus
        {
            get
            {
                Pawn pawn = parent as Pawn;
                if (!followerMealsEnabled) return "RR_FollowerMealOff".Translate();
                if (!HasPreference || !BodyModelEligibility.CanFollowBodyGoal(pawn) || !pawn.Spawned ||
                    !pawn.IsColonist || pawn.IsPrisoner || pawn.IsSlave || BodyRoleExpectations.Instance?.For(pawn) == null)
                    return "RR_FollowerMealIneligible".Translate();
                var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
                if (!TryGetPreferredRangeKg(out float lower, out float upper) || body?.UsesBodyGoals != true)
                    return "RR_FollowerMealIneligible".Translate();
                if (body.ProjectedBodyMassKg >= lower) return "RR_FollowerMealPersonalTarget".Translate();
                if (body.Goal != BodyGoal.Gain || !body.HasGainAppetite)
                    return "RR_FollowerMealGoal".Translate();
                if (!BodyFeedingPolicy.CanUseExtraGainMeal(pawn)) return "RR_FollowerMealNotReady".Translate();
                return "RR_FollowerMealWilling".Translate();
            }
        }
    }
}
