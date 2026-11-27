using HarmonyLib;
using RimRound.Comps;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.FeedOther
{
    [HarmonyPatch(typeof(Need_Food), "NutritionWanted", MethodType.Getter)]
    public static class NeedFood_NutritionWanted_FeedOtherPatch
    {
        [HarmonyPostfix]
        [HarmonyAfter("RRHarmony")]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn ___pawn, ref float __result)
        {
            FullnessAndDietStats_ThingComp body =
                ___pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body?.UsesBodyModel != true)
            {
                return;
            }

            var sitting = MealSittingComponent.Current?.BudgetFor(___pawn);
            if (sitting.HasValue)
            {
                // A sitting budgets the whole visit, not a single mouthful.
                // Social feeding keeps its stack pickup allocation but consumes
                // prepared meals one at a time, rechecking the live budget after
                // each serving. Other sitting drivers retain their own portions.
                __result = FeedOtherUtility.IsActiveFeedOtherEater(___pawn)
                    ? Mathf.Min(sitting.Value.Nutrition,
                        FeedOtherUtility.NutritionWantedForSession(___pawn))
                    : sitting.Value.Nutrition;
                return;
            }

            if (PrisonerFatteningFoodPatch.IsActiveFattenFeedRecipient(___pawn))
            {
                __result = PrisonerFatteningFoodPatch.NutritionWantedForActiveFattenFeed(___pawn);
                return;
            }

            if (FeedOtherUtility.IsActiveFeedOtherEater(___pawn))
            {
                if (FeedOtherUtility.IsAutonomousFeedOtherEater(___pawn))
                {
                    Thing serving;
                    if (!FeedOtherUtility.TryGetActiveSessionFood(___pawn, out serving) ||
                        !FeedOtherUtility.CanAutonomousSessionEat(___pawn, serving))
                    {
                        __result = 0f;
                        return;
                    }
                }
                __result = FeedOtherUtility.NutritionWantedForSession(___pawn);
            }
        }
    }
}
