using HarmonyLib;
using RimRound.Comps;
using RimWorld;
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
            if (___pawn == null)
            {
                return;
            }

            if (PrisonerFatteningFoodPatch.IsActiveFattenFeedRecipient(___pawn))
            {
                __result = PrisonerFatteningFoodPatch.NutritionWantedForActiveFattenFeed(___pawn);
                return;
            }

            if (FeedOtherUtility.IsActiveFeedOtherEater(___pawn) &&
                ___pawn.TryGetComp<FullnessAndDietStats_ThingComp>() != null)
            {
                __result = FeedOtherUtility.NutritionWantedForSession(___pawn);
            }
        }
    }
}
