using HarmonyLib;
using RimRound.Comps;
using Verse;

namespace RimRound.FeedOther
{
    [HarmonyPatch(typeof(FullnessAndDietStats_ThingComp), "CurrentFullness", MethodType.Setter)]
    public static class CurrentFullness_FeedOtherClampPatch
    {
        [HarmonyPrefix]
        public static void Prefix(
            FullnessAndDietStats_ThingComp __instance,
            out float __state)
        {
            // Capture the previous volume for notifications only. Once an item
            // has been consumed, reducing its recorded volume would disconnect
            // the stomach ledger from the nutrition actually granted.
            __state = __instance?.CurrentFullness ?? 0f;
        }

        [HarmonyPostfix]
        public static void Postfix(FullnessAndDietStats_ThingComp __instance, float __state)
        {
            Pawn pawn = __instance?.parent as Pawn;
            if (pawn == null)
            {
                return;
            }

            if (PrisonerFatteningFoodPatch.IsFattenPrisoner(pawn))
            {
                PrisonerFatteningFoodPatch.UpdateFatteningContinuation(pawn);
            }

            float veryFullThreshold =
                __instance.HardLimit * FeedOtherUtility.VeryFullFractionOfHardLimit;
            if (__state + FeedOtherUtility.FoodFitEpsilon < veryFullThreshold &&
                __instance.CurrentFullness + FeedOtherUtility.FoodFitEpsilon >= veryFullThreshold)
            {
                FeedOtherUtility.NotifyVeryFullReached(pawn);
            }
        }
    }
}
