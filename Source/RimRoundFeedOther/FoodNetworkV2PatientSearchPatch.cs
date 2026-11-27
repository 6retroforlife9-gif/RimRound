using HarmonyLib;
using RimRound.FeedingTube;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    /// <summary>
    /// WorkGiver_FeedPatient normally asks FoodUtility for a real ingestible
    /// Thing. Food Network faucets are buildings, so they are offered only as
    /// a guarded prepared-meal option before accepting raw fallback food. The custom
    /// patient driver then converts the faucet target into the selected real
    /// portion before carrying it to the patient.
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

            if ((!__result || !FeedOtherUtility.IsPreparedMeal(foodSource)) &&
                TryFindFaucetFood(worker, patient, out Thing prepared, out ThingDef preparedDef))
            { foodSource = prepared; foodDef = preparedDef; __result = true; }
        }

        internal static bool TryFindFaucetFood(Pawn worker, Pawn patient,
            out Thing foodSource, out ThingDef foodDef)
        {
            foodSource = null;
            foodDef = null;
            if (JobDefOf.FeedPatient == null ||
                JobDefOf.FeedPatient.driverClass !=
                    typeof(JobDriver_FoodFeedPatientEatingSpeed) ||
                worker == null || patient == null ||
                worker.Map == null || patient.Map != worker.Map)
            {
                return false;
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
                    return false;
                }

                FaucetMealAcquisition.Selection selected;
                if (!FaucetMealAcquisition.TrySelect(faucet, worker, patient, out selected)) return false;
                foodSource = faucet;
                foodDef = selected.Portion.FoodDef;
                return true;
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[RimRound Feed Other] Food Network patient-food " +
                    "fallback failed safely: " +
                    exception.GetType().Name + ": " +
                    exception.Message,
                    173648932);
                return false;
            }
        }
    }

    // Wardens call FoodUtility directly, bypassing the doctor's private search.
    // Keep native prepared food first and reuse the protected prisoner eligibility
    // gate before offering recipient-aware liquid meals ahead of raw fallback food.
    [HarmonyPatch(typeof(WorkGiver_Warden_Feed), nameof(WorkGiver_Warden_Feed.JobOnThing))]
    internal static class FoodNetworkWardenPatientSearchPatch
    {
        private static readonly Func<WorkGiver_Warden, Pawn, Thing, bool, bool>
            ShouldTakeCareOfPrisoner = AccessTools.MethodDelegate<
                Func<WorkGiver_Warden, Pawn, Thing, bool, bool>>(
                    AccessTools.Method(typeof(WorkGiver_Warden),
                        "ShouldTakeCareOfPrisoner"));

        [HarmonyPostfix]
        private static void Postfix(WorkGiver_Warden_Feed __instance,
            Pawn pawn, Thing t, ref Job __result)
        {
            Pawn patient = t as Pawn;
            if ((__result != null && (__result.def != JobDefOf.FeedPatient ||
                    FeedOtherUtility.IsPreparedMeal(__result.targetA.Thing))) ||
                pawn == null || patient?.needs?.food == null ||
                PrisonerFatteningFoodPatch.ShouldBlockFattenSelfEating(patient) ||
                !ShouldTakeCareOfPrisoner(__instance, pawn, patient, false) ||
                !WardenFeedUtility.ShouldBeFed(patient) ||
                patient.needs.food.CurLevelPercentage >=
                    patient.needs.food.PercentageThreshHungry + 0.02f ||
                patient.foodRestriction?.GetCurrentRespectedRestriction(pawn)
                    ?.filter.AllowedDefCount == 0)
                return;

            Thing food;
            ThingDef foodDef;
            if (!FoodNetworkV2FeedPatientSearchPatch.TryFindFaucetFood(
                    pawn, patient, out food, out foodDef)) return;
            __result = JobMaker.MakeJob(JobDefOf.FeedPatient, food, patient);
            __result.count = 1;
        }
    }
}
