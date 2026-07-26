using HarmonyLib;
using RimRound.Comps;
using RimRound.FeedingTube;
using RimRound.FeedingTube.Comps;
using RimRound.Patch;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using FeedingTubeThingDefOf = RimRound.FeedingTube.Defs.ThingDefOf;

namespace RimRound.FeedOther
{
    public sealed class CompProperties_FoodNetworkServing : CompProperties
    {
        public CompProperties_FoodNetworkServing()
        {
            compClass = typeof(CompFoodNetworkServing);
        }
    }

    /// <summary>
    /// The original feeding-fluid density component stores density on the
    /// shared ThingDef. This per-thing component keeps one dispensed serving's
    /// density independent so two different networks can be eaten safely at
    /// the same time.
    /// </summary>
    public sealed class CompFoodNetworkServing : ThingComp
    {
        private bool initialized;
        private float fullnessToNutritionRatio = 1f;
        private float representedNutrition = 0.1f;

        public bool IsInitialized
        {
            get { return initialized; }
        }

        public float FullnessToNutritionRatio
        {
            get { return Mathf.Max(FoodNetworkV2Constants.Epsilon, fullnessToNutritionRatio); }
        }

        public float RepresentedNutrition
        {
            get { return Mathf.Max(0f, representedNutrition); }
        }

        public void Initialize(FoodBatchV2 batch)
        {
            Initialize(batch, 1);
        }

        public void Initialize(FoodBatchV2 batch, int servingCount)
        {
            if (batch == null || batch.Empty || servingCount <= 0)
            {
                initialized = false;
                fullnessToNutritionRatio = 1f;
                representedNutrition = 0f;
                return;
            }

            initialized = true;
            fullnessToNutritionRatio = batch.FullnessToNutritionRatio;
            representedNutrition = batch.nutrition / servingCount;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(
                ref initialized,
                "rrFoodNetworkServingInitialized",
                false);
            Scribe_Values.Look(
                ref fullnessToNutritionRatio,
                "rrFoodNetworkFullnessToNutritionRatio",
                1f);
            Scribe_Values.Look(
                ref representedNutrition,
                "rrFoodNetworkRepresentedNutrition",
                0.1f);
            fullnessToNutritionRatio = Mathf.Max(
                FoodNetworkV2Constants.Epsilon,
                fullnessToNutritionRatio);
            representedNutrition = Mathf.Max(0f, representedNutrition);
        }

        public override string CompInspectStringExtra()
        {
            if (!initialized)
            {
                return null;
            }
            return "RR_FoodNetworkServingInspect".Translate(
                representedNutrition.ToString("F2"),
                (1f / FullnessToNutritionRatio).ToString("F2"),
                (representedNutrition * FullnessToNutritionRatio)
                    .ToString("F4"));
        }
    }

    /// <summary>
    /// RimRound's ratio is a fullness-to-nutrition ratio, while the inspector
    /// historically displayed its reciprocal under the vague label "density".
    /// Show the reciprocal as concentration and also show the actual base
    /// fullness volume of one item so high-nutrition foods are unambiguous.
    /// </summary>
    [HarmonyPatch(
        typeof(ThingComp_FoodItems_NutritionDensity),
        nameof(ThingComp_FoodItems_NutritionDensity.CompInspectStringExtra))]
    internal static class NutritionConcentrationInspectPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ThingComp_FoodItems_NutritionDensity __instance,
            ref string __result)
        {
            float ratio = __instance?.Props == null
                ? 0f
                : __instance.Props.fullnessToNutritionRatio;
            Thing food = __instance?.parent;
            if (ratio <= FoodNetworkV2Constants.Epsilon || food == null)
            {
                return;
            }

            float nutrition;
            try
            {
                nutrition = food.GetStatValue(StatDefOf.Nutrition);
            }
            catch (Exception)
            {
                return;
            }

            __result = "RR_NutritionConcentrationInspect".Translate(
                (1f / ratio).ToString("F2"),
                (nutrition * ratio).ToString("F4"));
        }
    }

    [HarmonyPatch(
        typeof(FoodNetStorage_ThingComp),
        nameof(FoodNetStorage_ThingComp.CompInspectStringExtra))]
    internal static class ClassicFoodNetworkConcentrationInspectPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result))
            {
                return;
            }

            __result = __result
                .Replace(
                    "Nutrition_Density".Translate().ToString(),
                    "RR_NutritionConcentration".Translate().ToString())
                .Replace(
                    "RR_NutritionDensity".Translate().ToString(),
                    "RR_NutritionConcentration".Translate().ToString());
        }
    }

    [HarmonyPatch(
        typeof(Building_NutrientDistillery),
        nameof(Building_NutrientDistillery.GetInspectString))]
    internal static class ClassicDistilleryConcentrationInspectPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            if (!string.IsNullOrEmpty(__result))
            {
                __result = __result.Replace(
                    "Target nutrition density",
                    "Target nutrition concentration");
            }
        }
    }

    [HarmonyPatch(
        typeof(Building_NutrientDistillery),
        nameof(Building_NutrientDistillery.GetGizmos))]
    internal static class ClassicDistilleryConcentrationGizmoPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref IEnumerable<Gizmo> __result)
        {
            __result = Relabel(__result);
        }

        private static IEnumerable<Gizmo> Relabel(IEnumerable<Gizmo> gizmos)
        {
            if (gizmos == null)
            {
                yield break;
            }

            foreach (Gizmo gizmo in gizmos)
            {
                Command command = gizmo as Command;
                if (command != null)
                {
                    if (command.defaultLabel ==
                        "Increase target nutrition density")
                    {
                        command.defaultLabel =
                            "Increase target nutrition concentration";
                    }
                    else if (command.defaultLabel ==
                        "Decrease target nutrition density")
                    {
                        command.defaultLabel =
                            "Decrease target nutrition concentration";
                    }
                }

                yield return gizmo;
            }
        }
    }

    internal static class FoodNetworkV2ServingUtility
    {
        public static Thing MakeServing(FoodBatchV2 batch)
        {
            return MakeServing(
                batch,
                FeedingTubeThingDefOf.RR_FeedingTubeFluid,
                "RR_FeedingTubeFluid");
        }

        private static Thing MakePasteMeal(FoodBatchV2 batch)
        {
            return MakePasteMealStack(batch, 1);
        }

        private static Thing MakePasteMealStack(
            FoodBatchV2 batch,
            int servingCount)
        {
            return MakeServing(
                batch,
                ThingDefOf.MealNutrientPaste,
                "MealNutrientPaste",
                servingCount);
        }

        private static Thing MakeServing(
            FoodBatchV2 batch,
            ThingDef servingDef,
            string servingDefName,
            int servingCount = 1)
        {
            if (batch == null || batch.Empty || servingDef == null)
            {
                return null;
            }

            Thing serving = ThingMaker.MakeThing(servingDef);
            CompFoodNetworkServing servingComp =
                serving.TryGetComp<CompFoodNetworkServing>();
            if (servingComp == null)
            {
                Log.ErrorOnce(
                    "[RimRound Feed Other] " + servingDefName + " is missing its " +
                    "Food Network v2 serving component.",
                    198624871);
                serving.Destroy(DestroyMode.Vanish);
                return null;
            }

            int clampedServingCount = Mathf.Clamp(
                servingCount,
                1,
                Mathf.Max(1, servingDef.stackLimit));
            serving.stackCount = clampedServingCount;
            servingComp.Initialize(batch, clampedServingCount);
            CompIngredients compIngredients = serving.TryGetComp<CompIngredients>();
            if (compIngredients != null)
            {
                foreach (ThingDef ingredient in batch.ingredients)
                {
                    if (ingredient != null)
                    {
                        compIngredients.RegisterIngredient(ingredient);
                    }
                }
            }

            return serving;
        }

        public static Thing TryDispenseForPawn(
            Building_FoodFaucet faucet,
            Pawn eater)
        {
            int requestedMeals = RequestedMealCountForPawn(faucet, eater);
            return TryDispenseMeals(faucet, eater, requestedMeals);
        }

        public static Thing TryDispenseIdleMealsForPawn(
            Building_FoodFaucet faucet,
            Pawn eater)
        {
            int requestedMeals = RequestedIdleMealCountForPawn(
                faucet,
                eater);
            return TryDispenseMealsInternal(
                faucet,
                eater,
                requestedMeals,
                int.MaxValue);
        }

        public static Thing TryDispenseMeals(
            Building_FoodFaucet faucet,
            Pawn eater,
            int requestedMeals)
        {
            return TryDispenseMealsInternal(
                faucet,
                eater,
                requestedMeals,
                FoodNetworkV2Constants.MaximumDispenserMealsPerTrip);
        }

        private static Thing TryDispenseMealsInternal(
            Building_FoodFaucet faucet,
            Pawn eater,
            int requestedMeals,
            int maximumMealsPerTrip)
        {
            if (faucet == null || eater == null || requestedMeals <= 0 ||
                !faucet.Spawned ||
                !FoodNetworkV2MachineUtility.IsOperational(faucet))
            {
                return null;
            }

            FoodNetworkV2 network = FoodNetworkV2MachineUtility.NetworkFor(faucet);
            if (network == null)
            {
                return null;
            }

            int completeMeals = Mathf.FloorToInt(
                (network.StoredNutrition + FoodNetworkV2Constants.Epsilon) /
                FoodNetworkV2Constants.DispenserMealNutrition);
            int carrySpace = eater.carryTracker == null
                ? 0
                : eater.carryTracker.AvailableStackSpace(
                    ThingDefOf.MealNutrientPaste);
            int mealCount = Mathf.Min(
                requestedMeals,
                completeMeals,
                Mathf.Max(1, maximumMealsPerTrip),
                Mathf.Max(1, ThingDefOf.MealNutrientPaste.stackLimit),
                Mathf.Max(0, carrySpace));
            if (mealCount <= 0)
            {
                return null;
            }

            FoodBatchV2 batch;
            float nutrition =
                FoodNetworkV2Constants.DispenserMealNutrition * mealCount;
            if (!network.TryDrawExactNutrition(nutrition, out batch))
            {
                return null;
            }

            return FinishDispense(
                faucet,
                network,
                batch,
                true,
                mealCount);
        }

        public static Thing TryDispense(
            Building_FoodFaucet faucet,
            float ignoredNutrition)
        {
            return TryDispenseOneMeal(faucet);
        }

        public static Thing TryDispenseOneMeal(Building_FoodFaucet faucet)
        {
            if (faucet == null || !faucet.Spawned ||
                !FoodNetworkV2MachineUtility.IsOperational(faucet))
            {
                return null;
            }

            FoodNetworkV2 network = FoodNetworkV2MachineUtility.NetworkFor(faucet);
            FoodBatchV2 batch;
            if (network == null ||
                !network.TryDrawExactNutrition(
                    FoodNetworkV2Constants.DispenserMealNutrition,
                    out batch))
            {
                return null;
            }

            return FinishDispense(faucet, network, batch, true, 1);
        }

        public static bool CanDispenseForPawn(
            Building_FoodFaucet faucet,
            Pawn eater)
        {
            FoodNetworkV2 network = FoodNetworkV2MachineUtility.NetworkFor(faucet);
            return faucet != null && eater != null && !eater.Dead &&
                eater.needs != null && eater.needs.food != null &&
                FoodNetworkV2MachineUtility.IsOperational(faucet) &&
                network != null &&
                network.CanDrawNutrition(
                    FoodNetworkV2Constants.DispenserMealNutrition);
        }

        public static bool TryPreviewMeal(
            Building_FoodFaucet faucet,
            out FoodBatchV2 batch)
        {
            batch = null;
            FoodNetworkV2 network = FoodNetworkV2MachineUtility.NetworkFor(faucet);
            return faucet != null &&
                FoodNetworkV2MachineUtility.IsOperational(faucet) &&
                network != null &&
                network.TryPreviewExactNutrition(
                    FoodNetworkV2Constants.DispenserMealNutrition,
                    out batch);
        }

        public static int RequestedMealCountForPawn(
            Building_FoodFaucet faucet,
            Pawn eater)
        {
            return RequestedMealCountForPawn(
                faucet,
                eater,
                FoodNetworkV2Constants.MaximumDispenserMealsPerTrip);
        }

        public static int RequestedIdleMealCountForPawn(
            Building_FoodFaucet faucet,
            Pawn eater)
        {
            return RequestedMealCountForPawn(
                faucet,
                eater,
                int.MaxValue);
        }

        private static int RequestedMealCountForPawn(
            Building_FoodFaucet faucet,
            Pawn eater,
            int maximumRequestedMeals)
        {
            if (!CanDispenseForPawn(faucet, eater))
            {
                return 0;
            }

            FullnessAndDietStats_ThingComp fullness =
                eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness == null || fullness.Disabled ||
                fullness.DietMode == DietMode.Disabled ||
                fullness.DietMode == DietMode.Nutrition)
            {
                return 1;
            }

            if (fullness.DietMode != DietMode.Fullness &&
                fullness.DietMode != DietMode.Hybrid)
            {
                return 1;
            }

            FoodBatchV2 preview;
            if (!TryPreviewMeal(faucet, out preview))
            {
                return 0;
            }

            Pair<float, float> ranges;
            try
            {
                ranges = fullness.GetRanges();
            }
            catch (Exception)
            {
                return 1;
            }

            float target = ranges.Second;
            if (!fullness.SetAboveHardLimit)
            {
                target = Mathf.Min(target, fullness.HardLimit);
            }

            float remaining = Mathf.Max(0f, target - fullness.CurrentFullness);
            float gainPerMeal =
                FoodNetworkV2Constants.DispenserMealNutrition *
                preview.FullnessToNutritionRatio *
                fullness.FullnessGainedMultiplier;
            if (remaining <= FoodNetworkV2Constants.Epsilon ||
                gainPerMeal <= FoodNetworkV2Constants.Epsilon)
            {
                return 0;
            }

            int requested = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (remaining - FoodNetworkV2Constants.Epsilon) /
                    gainPerMeal));
            return Mathf.Min(
                requested,
                Mathf.Max(1, maximumRequestedMeals));
        }

        internal static bool TryGetActiveSelfFeedingTarget(
            Pawn pawn,
            FullnessAndDietStats_ThingComp fullness,
            out float target)
        {
            target = 0f;
            if (pawn == null || fullness == null || pawn.CurJob == null ||
                fullness.Disabled ||
                (fullness.DietMode != DietMode.Fullness &&
                 fullness.DietMode != DietMode.Hybrid))
            {
                return false;
            }

            Thing food = pawn.CurJob.GetTarget(TargetIndex.A).Thing;
            CompFoodNetworkServing serving = food == null
                ? null
                : food.TryGetComp<CompFoodNetworkServing>();
            bool networkJob =
                (serving != null && serving.IsInitialized) ||
                pawn.CurJob.GetTarget(TargetIndex.C).Thing is
                    Building_FoodFaucet;
            bool idleEatToFullness =
                IdleUnderweightEatingPatch.IsIdleEatToFullnessJob(
                    pawn.CurJob);
            if (!networkJob && !idleEatToFullness)
            {
                return false;
            }

            Pair<float, float> ranges;
            try
            {
                ranges = fullness.GetRanges();
            }
            catch (Exception)
            {
                return false;
            }

            target = ranges.Second;
            if (!fullness.SetAboveHardLimit)
            {
                target = Mathf.Min(target, fullness.HardLimit);
            }
            return target > FoodNetworkV2Constants.Epsilon;
        }

        private static Thing FinishDispense(
            Building_FoodFaucet faucet,
            FoodNetworkV2 network,
            FoodBatchV2 batch,
            bool pasteMeal,
            int servingCount = 1)
        {
            Thing serving = pasteMeal
                ? MakePasteMealStack(batch, servingCount)
                : MakeServing(
                    batch,
                    FeedingTubeThingDefOf.RR_FeedingTubeFluid,
                    "RR_FeedingTubeFluid",
                    servingCount);
            if (serving == null)
            {
                network.TryStore(batch);
                return null;
            }

            if (faucet.def.building != null &&
                faucet.def.building.soundDispense != null)
            {
                faucet.def.building.soundDispense.PlayOneShot(
                    new TargetInfo(faucet.Position, faucet.Map, false));
            }
            return serving;
        }

        public static bool TryReturnToNetwork(Thing networkNode, Thing serving)
        {
            if (networkNode == null || serving == null)
            {
                return false;
            }

            CompFoodNetworkServing servingComp =
                serving.TryGetComp<CompFoodNetworkServing>();
            if (servingComp == null || !servingComp.IsInitialized ||
                servingComp.RepresentedNutrition <=
                    FoodNetworkV2Constants.Epsilon)
            {
                return false;
            }

            CompIngredients ingredientComp =
                serving.TryGetComp<CompIngredients>();
            IEnumerable<ThingDef> ingredients = ingredientComp == null ||
                ingredientComp.ingredients.NullOrEmpty()
                    ? (IEnumerable<ThingDef>)new ThingDef[0]
                    : (IEnumerable<ThingDef>)ingredientComp.ingredients;
            float returnedNutrition = servingComp.RepresentedNutrition *
                Mathf.Max(1, serving.stackCount);
            FoodBatchV2 batch = new FoodBatchV2(
                returnedNutrition,
                returnedNutrition * servingComp.FullnessToNutritionRatio,
                ingredients,
                Find.TickManager == null ? 0 : Find.TickManager.TicksGame);
            FoodNetworkV2 network =
                FoodNetworkV2MachineUtility.NetworkFor(networkNode);
            return network != null && network.TryStore(batch);
        }
    }

    /// <summary>
    /// A serving can represent any withdrawn nutrition amount regardless of
    /// the output ThingDef's ordinary fixed stat. Override the per-eater
    /// nutrition lookup only for an initialized v2 serving so both paste-meal
    /// jobs and direct bed feeding consume exactly what the network withdrew.
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.NutritionForEater))]
    internal static class FoodNetworkV2ServingNutritionPatch
    {
        private static void Postfix(Thing food, ref float __result)
        {
            if (food == null)
            {
                return;
            }

            CompFoodNetworkServing serving =
                food.TryGetComp<CompFoodNetworkServing>();
            if (serving != null && serving.IsInitialized)
            {
                __result = serving.RepresentedNutrition;
            }
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.GetFinalIngestibleDef))]
    internal static class FoodNetworkV2FaucetIngestibleDefPatch
    {
        private static bool Prefix(Thing foodSource, ref ThingDef __result)
        {
            if (FeedOtherMod.Settings.foodNetworkV2Enabled &&
                foodSource is Building_FoodFaucet)
            {
                __result = ThingDefOf.MealNutrientPaste;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Building_FoodFaucet), nameof(Building_FoodFaucet.TryDispenseFood))]
    internal static class FoodNetworkV2FaucetDispensePatch
    {
        private static bool Prefix(Building_FoodFaucet __instance, ref Thing __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }

            __result = FoodNetworkV2ServingUtility.TryDispenseOneMeal(
                __instance);
            return false;
        }
    }

    /// <summary>
    /// Replace only RimRound's fullness handling for an actual network serving.
    /// Every other ingestible continues through RimRound's original postfix.
    /// </summary>
    [HarmonyPatch(
        typeof(Thing_Ingested_HarmonyPatch),
        nameof(Thing_Ingested_HarmonyPatch.Postfix))]
    internal static class FoodNetworkV2ServingIngestedPatch
    {
        private static bool Prefix(
            [HarmonyArgument(0)] Thing ingestedThing,
            [HarmonyArgument(1)] Pawn ingester,
            [HarmonyArgument(2)] ref float nutritionResult)
        {
            if (ingestedThing == null || ingester == null)
            {
                return true;
            }

            CompFoodNetworkServing serving =
                ingestedThing.TryGetComp<CompFoodNetworkServing>();
            if (serving == null || !serving.IsInitialized)
            {
                return true;
            }

            FullnessAndDietStats_ThingComp fullness =
                ingester.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness == null)
            {
                return false;
            }

            if ((ingester.Spawned || ingester.IsCaravanMember()) &&
                ingester.RaceProps.Humanlike && nutritionResult > 0f &&
                !fullness.Disabled &&
                fullness.DietMode != DietMode.Disabled)
            {
                float ratio = serving.FullnessToNutritionRatio;
                fullness.UpdateRatio(nutritionResult, ratio);
                fullness.CurrentFullness +=
                    nutritionResult * ratio * fullness.FullnessGainedMultiplier;
                nutritionResult = 0f;
            }

            Thing_Ingested_StomachBurstCheck.Postfix(fullness);
            return false;
        }
    }

    /// <summary>
    /// Finds and scores a ready Food Network v2 dispenser without inserting
    /// the non-ingestible building into FoodUtility's global map-food result.
    /// Vanilla callers other than JobGiver_GetFood commonly assume that every
    /// returned Thing is a real edible stack, so the dispenser is considered
    /// only after the self-feeding job giver has completed normally.
    /// </summary>
    internal static class FoodNetworkV2FaucetSearchUtility
    {
        internal static bool TryFindBestFaucet(
            Pawn getter,
            Pawn eater,
            bool desperate,
            FoodPreferability maxPref,
            bool allowForbidden,
            bool allowSociallyImproper,
            FoodPreferability minPrefOverride,
            bool allowVenerated,
            out Building_FoodFaucet bestFaucet,
            out float bestFaucetScore)
        {
            bestFaucet = null;
            bestFaucetScore = float.MinValue;
            if (getter == null || eater == null || getter.Map == null ||
                eater.Map != getter.Map || getter.RaceProps == null ||
                !getter.RaceProps.ToolUser || getter.health == null ||
                getter.health.capacities == null ||
                !getter.health.capacities.CapableOf(
                    PawnCapacityDefOf.Manipulation))
            {
                return false;
            }

            ThingDef pasteDef = ThingDefOf.MealNutrientPaste;
            if (pasteDef == null || pasteDef.ingestible == null)
            {
                return false;
            }

            FoodPreferability minimum = ResolveMinimumPreference(
                eater,
                desperate,
                minPrefOverride);
            if ((int)pasteDef.ingestible.preferability < (int)minimum ||
                (int)pasteDef.ingestible.preferability > (int)maxPref)
            {
                return false;
            }

            try
            {
                if (!eater.WillEat(
                    pasteDef,
                    getter,
                    true,
                    allowVenerated))
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

            IEnumerable<Building_FoodFaucet> faucets = getter.Map.listerBuildings
                .allBuildingsColonist
                .OfType<Building_FoodFaucet>();
            foreach (Building_FoodFaucet faucet in faucets)
            {
                if (!ValidFaucet(
                    faucet,
                    getter,
                    eater,
                    allowForbidden,
                    allowSociallyImproper))
                {
                    continue;
                }

                float distance = (getter.Position - faucet.InteractionCell)
                    .LengthManhattan;
                float score = FaucetOptimality(
                    eater,
                    pasteDef,
                    distance);
                if (score > bestFaucetScore)
                {
                    bestFaucet = faucet;
                    bestFaucetScore = score;
                }
            }

            return bestFaucet != null;
        }

        private static bool ValidFaucet(
            Building_FoodFaucet faucet,
            Pawn getter,
            Pawn eater,
            bool allowForbidden,
            bool allowSociallyImproper)
        {
            if (faucet == null || !faucet.Spawned ||
                (faucet.Faction != getter.Faction &&
                 faucet.Faction != getter.HostFaction) ||
                (!allowForbidden && faucet.IsForbidden(getter)) ||
                !FoodNetworkV2ServingUtility.CanDispenseForPawn(
                    faucet,
                    eater) ||
                !faucet.InteractionCell.Standable(faucet.Map) ||
                !IsSociallyProper(faucet, getter, eater, allowSociallyImproper))
            {
                return false;
            }

            return getter.Map.reachability.CanReachNonLocal(
                getter.Position,
                new TargetInfo(faucet.InteractionCell, getter.Map),
                PathEndMode.OnCell,
                TraverseParms.For(getter, Danger.Some));
        }

        private static bool IsSociallyProper(
            Thing foodSource,
            Pawn getter,
            Pawn eater,
            bool allowSociallyImproper)
        {
            if (allowSociallyImproper)
            {
                return true;
            }

            bool animalsCare = !getter.IsAnimal;
            return foodSource.IsSociallyProper(getter) ||
                foodSource.IsSociallyProper(
                    eater,
                    eater.IsPrisonerOfColony,
                    animalsCare);
        }

        private static FoodPreferability ResolveMinimumPreference(
            Pawn eater,
            bool desperate,
            FoodPreferability overrideValue)
        {
            if (overrideValue != FoodPreferability.Undefined)
            {
                return overrideValue;
            }
            if (eater.NonHumanlikeOrWildMan())
            {
                return FoodPreferability.NeverForNutrition;
            }
            if (desperate)
            {
                return FoodPreferability.DesperateOnly;
            }
            Need_Food foodNeed = eater.needs == null
                ? null
                : eater.needs.food;
            return foodNeed != null && (int)foodNeed.CurCategory >= 2
                ? FoodPreferability.RawBad
                : FoodPreferability.MealAwful;
        }

        internal static float FaucetOptimality(
            Pawn eater,
            ThingDef foodDef,
            float distance)
        {
            // Do not pass the dispenser building into FoodOptimality. Several
            // vanilla/RimRound thought and stat hooks correctly expect the
            // supplied Thing itself to be ingestible. The output definition is
            // still used for the same broad preference offsets as a simple
            // meal, while distance decides between multiple dispensers.
            float score = 300f - distance;
            switch (foodDef.ingestible.preferability)
            {
                case FoodPreferability.NeverForNutrition:
                    return -9999999f;
                case FoodPreferability.DesperateOnly:
                    score -= 150f;
                    break;
                case FoodPreferability.DesperateOnlyForHumanlikes:
                    if (eater.RaceProps.Humanlike)
                    {
                        score -= 150f;
                    }
                    break;
            }

            if (eater.RaceProps.Humanlike)
            {
                score += foodDef.ingestible.optimalityOffsetHumanlikes;
            }
            else if (eater.IsAnimal)
            {
                score += foodDef.ingestible.optimalityOffsetFeedingAnimals;
            }
            return score;
        }
    }

    /// <summary>
    /// RimRound's original faucet patch advertises Building_FoodFaucet as a
    /// vanilla FoodSource. That classification is unsafe for Food Network v2:
    /// vanilla scores every listed source before its validator runs and assumes
    /// the source's own ThingDef is ingestible. A faucet is a Building with no
    /// ingestible properties, so leaving it in either food-source list can throw
    /// before our dedicated self-feeding job is considered.
    ///
    /// Force the v2 classification after RRHarmony and repair the already-built
    /// map/region lists when a save loads or the classic-mode toggle changes.
    /// </summary>
    [HarmonyPatch(typeof(ThingListGroupHelper), nameof(ThingListGroupHelper.Includes))]
    internal static class FoodNetworkV2FaucetGroupIsolationPatch
    {
        [HarmonyPostfix]
        [HarmonyAfter("RRHarmony")]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            ThingRequestGroup group,
            ThingDef def,
            ref bool __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled || def == null ||
                def.defName != FoodNetworkV2Constants.FaucetDefName)
            {
                return;
            }

            if (group == ThingRequestGroup.FoodSource ||
                group == ThingRequestGroup.FoodSourceNotPlantOrTree)
            {
                __result = false;
            }
        }
    }

    internal static class FoodNetworkV2FaucetListerUtility
    {
        private static readonly FieldInfo ListsByGroupField =
            AccessTools.Field(typeof(ListerThings), "listsByGroup");
        private static readonly FieldInfo StateHashByGroupField =
            AccessTools.Field(typeof(ListerThings), "stateHashByGroup");
        private static readonly List<Region> TouchableRegions =
            new List<Region>();
        private static readonly ThingRequestGroup[] FoodSourceGroups =
        {
            ThingRequestGroup.FoodSource,
            ThingRequestGroup.FoodSourceNotPlantOrTree
        };

        internal static void SyncAllMaps(bool includeAsVanillaFoodSource)
        {
            if (Find.Maps == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                SyncMap(map, includeAsVanillaFoodSource);
            }
        }

        internal static void SyncMap(
            Map map,
            bool includeAsVanillaFoodSource)
        {
            if (map == null || map.listerThings == null)
            {
                return;
            }

            ThingDef faucetDef = DefDatabase<ThingDef>.GetNamedSilentFail(
                FoodNetworkV2Constants.FaucetDefName);
            if (faucetDef == null)
            {
                return;
            }

            List<Thing> faucets = map.listerThings.ThingsOfDef(faucetDef)
                .Where(delegate(Thing thing)
                {
                    return thing != null && thing.Spawned && thing.Map == map;
                })
                .ToList();

            foreach (Thing faucet in faucets)
            {
                SetFoodSourceMembership(
                    map.listerThings,
                    faucet,
                    includeAsVanillaFoodSource);

                TouchableRegions.Clear();
                RegionListersUpdater.GetTouchableRegions(
                    faucet,
                    map,
                    TouchableRegions);
                foreach (Region region in TouchableRegions)
                {
                    if (region != null)
                    {
                        SetFoodSourceMembership(
                            region.ListerThings,
                            faucet,
                            includeAsVanillaFoodSource);
                    }
                }
            }
            TouchableRegions.Clear();
        }

        private static void SetFoodSourceMembership(
            ListerThings lister,
            Thing faucet,
            bool include)
        {
            if (lister == null || faucet == null ||
                ListsByGroupField == null || StateHashByGroupField == null)
            {
                return;
            }

            List<Thing>[] lists =
                ListsByGroupField.GetValue(lister) as List<Thing>[];
            int[] stateHashes =
                StateHashByGroupField.GetValue(lister) as int[];
            if (lists == null || stateHashes == null)
            {
                return;
            }

            foreach (ThingRequestGroup group in FoodSourceGroups)
            {
                int index = (int)group;
                if (index < 0 || index >= lists.Length ||
                    index >= stateHashes.Length)
                {
                    continue;
                }

                List<Thing> groupList = lists[index];
                bool changed = false;
                if (include)
                {
                    if (groupList == null)
                    {
                        groupList = new List<Thing>();
                        lists[index] = groupList;
                    }
                    if (!groupList.Contains(faucet))
                    {
                        groupList.Add(faucet);
                        changed = true;
                    }
                }
                else if (groupList != null)
                {
                    while (groupList.Remove(faucet))
                    {
                        changed = true;
                    }
                }

                if (changed)
                {
                    stateHashes[index]++;
                }
            }
        }
    }

    /// <summary>
    /// Reject complete food portions that would discard more than 1.0 nutrition
    /// at the eater's active target. The conversion from excess fullness back to
    /// nutrition uses each food's real concentration and the eater's live
    /// fullness multiplier.
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality))]
    internal static class MaximumNutritionWasteFoodOptimalityPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            [HarmonyArgument(0)] Pawn eater,
            [HarmonyArgument(1)] Thing foodSource,
            ref float __result)
        {
            if (eater == null || foodSource == null ||
                foodSource is Building_NutrientPasteDispenser ||
                foodSource is Building_FoodFaucet ||
                foodSource.def?.ingestible == null)
            {
                return;
            }

            if (!FeedOtherUtility.IsAutomaticFoodSelectionFitAcceptable(
                eater,
                foodSource,
                1))
            {
                __result = float.MinValue;
            }
        }
    }

    /// <summary>
    /// Let vanilla finish its complete food search first, then compare its
    /// ordinary ingest job with an operational Food Network v2 dispenser. This
    /// confines the non-ingestible building target to the one job driver that
    /// explicitly supports it; wardening, patient feeding, inventory transfer,
    /// animal feeding, and other FoodUtility callers continue to receive only
    /// real edible Things.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    internal static class FoodNetworkV2GetFoodJobPatch
    {
        private const int FoodJobErrorKey = 184736220;

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(Pawn pawn, ref Job __result)
        {
            if (pawn == null ||
                (__result != null && __result.def != JobDefOf.Ingest))
            {
                return;
            }

            if (__result != null)
            {
                Thing selectedFood = __result.GetTarget(TargetIndex.A).Thing;
                if (selectedFood != null &&
                    !(selectedFood is Building_NutrientPasteDispenser) &&
                    !(selectedFood is Building_FoodFaucet) &&
                    !FeedOtherUtility.IsAutomaticFoodSelectionFitAcceptable(
                        pawn,
                        selectedFood,
                        1))
                {
                    __result = null;
                }
            }

            if (!FeedOtherMod.Settings.foodNetworkV2Enabled ||
                pawn.needs == null || pawn.needs.food == null ||
                pawn.RaceProps == null || pawn.Map == null ||
                FoodUtility.ShouldBeFedBySomeone(pawn))
            {
                return;
            }

            bool desperate =
                pawn.needs.food.CurCategory == HungerCategory.Starving;
            try
            {
                Building_FoodFaucet faucet;
                float faucetScore;
                if (!FoodNetworkV2FaucetSearchUtility.TryFindBestFaucet(
                    pawn,
                    pawn,
                    desperate,
                    FoodPreferability.MealLavish,
                    false,
                    false,
                    FoodPreferability.Undefined,
                    false,
                    out faucet,
                    out faucetScore))
                {
                    return;
                }

                if (__result != null)
                {
                    Thing currentFood =
                        __result.GetTarget(TargetIndex.A).Thing;
                    if (currentFood == null ||
                        currentFood is Building_FoodFaucet)
                    {
                        return;
                    }

                    ThingDef currentFoodDef =
                        FoodUtility.GetFinalIngestibleDef(currentFood);
                    if (currentFoodDef == null ||
                        currentFoodDef.ingestible == null)
                    {
                        // Vanilla produced a job we cannot compare safely.
                        // Preserve it rather than risking the pawn's think tree.
                        return;
                    }

                    float currentDistance =
                        (pawn.Position - currentFood.PositionHeld)
                            .LengthManhattan;
                    float currentScore = FoodUtility.FoodOptimality(
                        pawn,
                        currentFood,
                        currentFoodDef,
                        currentDistance);
                    if (currentFood.ParentHolder is Pawn_InventoryTracker)
                    {
                        // Match TryFindBestFoodSourceFor's preference penalty
                        // when it compares carried food with a map source.
                        currentScore -= 32f;
                    }

                    if (faucetScore <= currentScore)
                    {
                        return;
                    }
                }

                Job job = JobMaker.MakeJob(JobDefOf.Ingest, faucet);
                job.count = 1;
                __result = job;
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(
                    "[RimRound Feed Other] Food Network v2 dispenser eating " +
                    "job selection failed safely: " +
                    exception.GetType().Name + ": " +
                    exception.Message,
                    FoodJobErrorKey);
            }
        }
    }

    [HarmonyPatch(
        typeof(JobDriver_Ingest),
        nameof(JobDriver_Ingest.TryMakePreToilReservations))]
    internal static class FoodNetworkV2IngestReservationPatch
    {
        private static bool Prefix(
            JobDriver_Ingest __instance,
            bool errorOnFailed,
            ref bool __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled ||
                !(__instance.job.GetTarget(TargetIndex.A).Thing is Building_FoodFaucet) &&
                !(__instance.job.GetTarget(TargetIndex.C).Thing is Building_FoodFaucet))
            {
                return true;
            }

            // Vanilla does not reserve a nutrient-paste dispenser. Several
            // pawns may walk to it together; the transactional draw at the
            // collection toil decides who receives the remaining food.
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(JobDriver_Ingest),
        nameof(JobDriver_Ingest.Notify_Starting))]
    internal static class FoodNetworkV2IngestStartingPatch
    {
        private static void Postfix(
            JobDriver_Ingest __instance,
            ref bool ___usingNutrientPasteDispenser)
        {
            if (FeedOtherMod.Settings.foodNetworkV2Enabled &&
                __instance.job.GetTarget(TargetIndex.A).Thing is
                    Building_FoodFaucet)
            {
                __instance.job.SetTarget(
                    TargetIndex.C,
                    __instance.job.GetTarget(TargetIndex.A));
                // Reuse vanilla's nutrient-paste job report while our custom
                // toils bridge the non-vanilla dispenser building class.
                ___usingNutrientPasteDispenser = true;
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver_Ingest), "MakeNewToils")]
    internal static class FoodNetworkV2IngestToilsPatch
    {
        private static readonly System.Reflection.FieldInfo ChewingField =
            AccessTools.Field(typeof(JobDriver_Ingest), "chewing");

        private static bool Prefix(
            JobDriver_Ingest __instance,
            ref IEnumerable<Toil> __result)
        {
            if (IdleUnderweightEatingPatch.IsIdleEatToFullnessJob(
                __instance?.job))
            {
                __result =
                    IdleUnderweightEatingPatch
                        .MakeStandingIdleEatingToils(__instance);
                return false;
            }

            Building_FoodFaucet faucet =
                __instance.job.GetTarget(TargetIndex.A).Thing as Building_FoodFaucet;
            if (faucet == null)
            {
                faucet = __instance.job.GetTarget(TargetIndex.C).Thing as
                    Building_FoodFaucet;
            }
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled || faucet == null)
            {
                return true;
            }

            List<Toil> toils = new List<Toil>();
            toils.Add(
                Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.InteractionCell)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.C));

            Building_FoodFaucet sourceFaucet = faucet;
            Toil dispense = ToilMaker.MakeToil("TakePasteMealFromFoodNetwork");
            dispense.initAction = delegate
            {
                Pawn actor = dispense.actor;
                Thing existingServing =
                    actor.CurJob.GetTarget(TargetIndex.A).Thing;
                CompFoodNetworkServing existingComp = existingServing == null
                    ? null
                    : existingServing.TryGetComp<CompFoodNetworkServing>();
                if (existingComp != null && existingComp.IsInitialized &&
                    actor.carryTracker.CarriedThing == existingServing)
                {
                    // Save/load may reconstruct this delayed collection toil
                    // after the paste was already withdrawn. Keep the existing
                    // carried meal instead of drawing a duplicate batch.
                    return;
                }

                actor.rotationTracker.FaceTarget(sourceFaucet);
                Thing serving =
                    FoodNetworkV2ServingUtility.TryDispenseForPawn(
                        sourceFaucet,
                        actor);
                if (serving == null ||
                    !actor.carryTracker.TryStartCarry(serving))
                {
                    if (serving != null && !serving.Destroyed)
                    {
                        FoodNetworkV2ServingUtility.TryReturnToNetwork(
                            sourceFaucet,
                            serving);
                        serving.Destroy(DestroyMode.Vanish);
                    }
                    actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                    return;
                }

                actor.CurJob.SetTarget(
                    TargetIndex.A,
                    actor.carryTracker.CarriedThing);
                actor.CurJob.count =
                    actor.carryTracker.CarriedThing.stackCount;
            };
            dispense.defaultCompleteMode = ToilCompleteMode.Delay;
            dispense.defaultDuration = Building_NutrientPasteDispenser.CollectDuration;
            toils.Add(dispense);

            toils.Add(
                Toils_Ingest.CarryIngestibleToChewSpot(
                    __instance.pawn,
                    TargetIndex.A)
                    .FailOnDestroyedNullOrForbidden(TargetIndex.A));
            toils.Add(Toils_Ingest.FindAdjacentEatSurface(
                TargetIndex.B,
                TargetIndex.A));

            Toil chew = FeedOtherUtility.ChewIngestibleWithEatingSpeed(
                __instance.pawn,
                1f,
                TargetIndex.A,
                TargetIndex.B);
            if (ChewingField != null)
            {
                ChewingField.SetValue(__instance, chew);
            }
            toils.Add(chew);
            toils.Add(Toils_Ingest.FinalizeIngest(
                __instance.pawn,
                TargetIndex.A));

            __result = toils;
            return false;
        }
    }
}
