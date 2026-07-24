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
            ref float __0,
            out float __state)
        {
            __state = __instance?.CurrentFullness ?? 0f;
            Pawn pawn = __instance?.parent as Pawn;
            if (pawn != null && FeedOtherUtility.IsActiveFeedOtherEater(pawn))
            {
                // Apply the cap before RimRound observes the new value. The final
                // whole small-food item is still consumed, but any excess fullness
                // is discarded and can never trigger Painfully Full or collapse.
                float target = FeedOtherUtility.FeedingTarget(pawn);
                __0 = UnityEngine.Mathf.Min(__0, target);

                // Snap a rising value that lands within floating-point epsilon to
                // the exact threshold so the session latch and mood thought cannot
                // be missed by a 69.99999% result.
                if (__0 > __state && __0 + FeedOtherUtility.FoodFitEpsilon >= target)
                {
                    __0 = target;
                }
            }

            // A pawn using the Food Network faucet in Fullness or Hybrid mode
            // collects whole 0.90 meals. Consume the final whole meal normally,
            // but discard only the part that would exceed the active bar target.
            float selfFeedingTarget;
            if (pawn != null && __0 > __state &&
                FoodNetworkV2ServingUtility.TryGetActiveSelfFeedingTarget(
                    pawn,
                    __instance,
                    out selfFeedingTarget))
            {
                __0 = __state >= selfFeedingTarget
                    ? __state
                    : UnityEngine.Mathf.Min(__0, selfFeedingTarget);
            }

            // While a warden is actively fattening a prisoner, clamp the final
            // whole serving to exact Painfully Full (80% hard capacity). Once
            // reached, the saved Fatten latch blocks more feeding until below 10%.
            if (pawn != null && __0 > __state &&
                PrisonerFatteningFoodPatch.IsActiveFattenFeedRecipient(pawn))
            {
                float fattenTarget =
                    PrisonerFatteningFoodPatch.FattenFullnessTarget(pawn, __instance);
                __0 = __state >= fattenTarget
                    ? __state
                    : UnityEngine.Mathf.Min(__0, fattenTarget);
            }

            // Apply the same target to any other eating route while Fatten is
            // selected. This prevents any alternate eating route from pushing
            // the prisoner beyond the bounded 80% session target. Only increases are
            // clamped, so an already-overfull pawn is not forcibly reduced.
            if (pawn != null && __0 > __state &&
                PrisonerFatteningFoodPatch.IsFattenPrisoner(pawn))
            {
                float fattenTarget =
                    PrisonerFatteningFoodPatch.FattenFullnessTarget(pawn, __instance);
                __0 = __state >= fattenTarget
                    ? __state
                    : UnityEngine.Mathf.Min(__0, fattenTarget);
            }
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
