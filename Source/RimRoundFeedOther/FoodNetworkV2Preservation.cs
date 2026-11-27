using HarmonyLib;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    // Covers old collected network meals whose native poison comp is already
    // saved. Ordinary meals, including uninitialized vanilla paste, are unchanged.
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.AddFoodPoisoningHediff))]
    internal static class FoodNetworkNoPoisoning
    {
        private static bool Prefix(Thing ingestible)
        {
            return ingestible?.TryGetComp<CompFoodNetworkServing>()?.IsInitialized != true;
        }
    }
}
