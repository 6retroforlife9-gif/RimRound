using RimRound.Comps;
using RimRound.Utilities;
using Verse;
using UnityEngine;

namespace RimRound.FeedOther
{
    [StaticConstructorOnStartup]
    internal static class OrdinaryIntakeSessionBudget
    {
        static OrdinaryIntakeSessionBudget()
        {
            OrdinaryFoodIntake.ActiveBudgetProvider = ActiveBudget;
            OrdinaryFoodIntake.UsefulIntakeCommitted = (eater, food, nutrition, volume) =>
                MealSittingComponent.Current?.Committed(eater, food, nutrition, volume);
            BodyFeedingPolicy.SocialMealJobProvider = SocialFeedingPolicy.PreferSocialMeal;
            BodyFeedingPolicy.SocialMealActive = pawn => FeedOtherUtility.IsActiveFeedOtherEater(pawn) ||
                pawn?.CurJobDef == FeedOtherDefOf.RR_RequestGainMeal;
            ConnectedFeederIntake.MaximumTargetFraction = () => FeedOtherMod.Settings.autoFeederMaximumTargetPercent / 100f;
        }

        private static OrdinaryIntakeBudget? ActiveBudget(Pawn pawn)
        {
            var sitting = MealSittingComponent.Current?.BudgetFor(pawn);
            if (sitting.HasValue) return sitting;
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body?.UsesBodyModel != true) return null;
            float target;
            Thing food;
            if (PrisonerFatteningFoodPatch.IsActiveFattenFeedRecipient(pawn))
            {
                return PrisonerFatteningFoodPatch.BudgetForActiveFattenFeed(pawn);
            }
            if (!FeedOtherUtility.IsActiveFeedOtherEater(pawn) ||
                !FeedOtherUtility.TryGetActiveSessionFood(pawn, out food)) return null;
            if (FeedOtherUtility.IsAutonomousFeedOtherEater(pawn)) return SocialFeedingPolicy.Budget(pawn, food);
            target = FeedOtherUtility.FeedingTarget(pawn);
            float ratio = OrdinaryFoodIntake.Density(food) * body.FullnessGainedMultiplier;
            float nutrition = ratio > 0f ? Mathf.Max(0f, target - body.OccupiedVolume) / ratio :
                Mathf.Max(0f, pawn.needs.food.MaxLevel - pawn.needs.food.CurLevel);
            return new OrdinaryIntakeBudget(nutrition, target);
        }
    }
}
