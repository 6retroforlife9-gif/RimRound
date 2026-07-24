using HarmonyLib;
using RimRound.FeedingTube;
using RimWorld;
using System;
using Verse;

namespace RimRound.FeedOther
{
    /// <summary>
    /// WorkGiver_FeedPatient normally asks FoodUtility for a real ingestible
    /// Thing. Food Network faucets are buildings, so they are offered only as
    /// a guarded fallback when the ordinary search found no food. The custom
    /// patient driver then converts the faucet target into real 0.90 paste
    /// meals before carrying them to the patient.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_FeedPatient), "TryFindBestFoodSourceFor")]
    internal static class FoodNetworkV2FeedPatientSearchPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            [HarmonyArgument(0)] Pawn worker,
            [HarmonyArgument(1)] Pawn patient,
            [HarmonyArgument(2)] ref Thing foodSource,
            [HarmonyArgument(3)] ref ThingDef foodDef,
            ref bool __result)
        {
            if (__result && foodSource != null &&
                !(foodSource is Building_NutrientPasteDispenser) &&
                !(foodSource is Building_FoodFaucet) &&
                !FeedOtherUtility.IsAutomaticFoodSelectionFitAcceptable(
                    patient,
                    foodSource,
                    1))
            {
                __result = false;
                foodSource = null;
                foodDef = null;
            }

            if (__result ||
                JobDefOf.FeedPatient == null ||
                JobDefOf.FeedPatient.driverClass !=
                    typeof(JobDriver_FoodFeedPatientEatingSpeed) ||
                !FeedOtherMod.Settings.foodNetworkV2Enabled ||
                worker == null || patient == null ||
                worker.Map == null || patient.Map != worker.Map)
            {
                return;
            }

            try
            {
                bool desperate = patient.needs?.food != null &&
                    patient.needs.food.CurCategory ==
                        HungerCategory.Starving;
                Building_FoodFaucet faucet;
                float score;
                if (!FoodNetworkV2FaucetSearchUtility.TryFindBestFaucet(
                    worker,
                    patient,
                    desperate,
                    FoodPreferability.MealLavish,
                    false,
                    false,
                    FoodPreferability.Undefined,
                    false,
                    out faucet,
                    out score))
                {
                    return;
                }

                foodSource = faucet;
                foodDef = ThingDefOf.MealNutrientPaste;
                __result = true;
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[RimRound Feed Other] Food Network patient-food " +
                    "fallback failed safely: " +
                    exception.GetType().Name + ": " +
                    exception.Message,
                    173648932);
            }
        }
    }
}
