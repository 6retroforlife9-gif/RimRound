using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.FeedingTube.Patches
{
    [HarmonyPatch(typeof(JobDriver_Ingest), "MakeNewToils")]
    public class JobDriver_Ingest_MakeNewToils_AddExceptionForFaucet
    {
        private static readonly FieldInfo ChewingField =
            AccessTools.Field(typeof(JobDriver_Ingest), "chewing");

        public static bool Prefix(JobDriver_Ingest __instance,
            ref IEnumerable<Toil> __result)
        {
            bool collectedClassic = HasCollectedServing(__instance);
            if (!FoodNetworkPatchPolicy.LegacyPatchesEnabled &&
                !collectedClassic) return true;

            Building_FoodFaucet faucet =
                __instance?.job?.GetTarget(TargetIndex.A).Thing as Building_FoodFaucet;
            Thing alreadyCollected = __instance?.job?
                .GetTarget(TargetIndex.B).Thing;
            if (faucet == null && !collectedClassic) return true;

            // One portion per job makes the next search recheck hunger,
            // fullness, network stock and restrictions.
            if (alreadyCollected == null &&
                GetNumberOfTimesToEatFromSystem(__instance.pawn, faucet) <= 0)
            {
                __result = new Toil[0];
                return false;
            }

            var toils = new List<Toil>();
            if (faucet != null && !faucet.Destroyed)
                toils.AddRange(JobDriver_Ingest_PrepareToIngestToils_AddFaucetSupport
                    .MakeFaucetToils(__instance));
            else
            {
                // Preserve native toil indexes when a source is destroyed
                // after its serving was collected, then the job is loaded.
                Toil sourceGone = ToilMaker.MakeToil(
                    "CollectedClassicSourceGone");
                sourceGone.defaultCompleteMode = ToilCompleteMode.Instant;
                toils.Add(sourceGone);
                Toil mealReady = ToilMaker.MakeToil(
                    "CollectedClassicMealReady");
                mealReady.defaultCompleteMode = ToilCompleteMode.Instant;
                toils.Add(mealReady);
            }
            toils.Add(Toils_Ingest.CarryIngestibleToChewSpot(
                __instance.pawn, TargetIndex.B)
                .FailOnDestroyedNullOrForbidden(TargetIndex.B));
            toils.Add(Toils_Ingest.FindAdjacentEatSurface(
                TargetIndex.C, TargetIndex.B));
            Toil chew = Toils_Ingest.ChewIngestible(
                __instance.pawn, 0.1f, TargetIndex.B, TargetIndex.C)
                .FailOn(toil => !CanConsume(__instance));
            if (ChewingField != null) ChewingField.SetValue(__instance, chew);
            toils.Add(chew);
            toils.Add(Toils_Ingest.FinalizeIngest(
                __instance.pawn, TargetIndex.B)
                .FailOn(toil => !CanConsume(__instance)));
            __result = toils;
            return false;
        }

        // A saved classic job keeps its Target B serving and toil indexes even
        // after the player switches to v2. New faucet jobs still obey the gate.
        public static bool HasCollectedServing(JobDriver_Ingest driver)
        {
            Thing serving = driver?.job?.GetTarget(TargetIndex.B).Thing;
            return serving?.def == Defs.ThingDefOf.RR_FeedingTubeFluid &&
                (serving as ThingWithComps)?.AllComps
                    .OfType<INetworkFoodServing>().Any() == true;
        }

        private static bool CanConsume(JobDriver_Ingest driver)
        {
            Pawn pawn = driver?.pawn;
            Thing serving = driver?.job?.GetTarget(TargetIndex.B).Thing;
            if (pawn == null || pawn.Dead || pawn.Downed ||
                serving == null || serving.Destroyed ||
                pawn.needs?.food == null ||
                !FoodUtility.WillEat(pawn, serving, null, true, false))
                return false;
            bool vanillaDecision =
                pawn.needs.food.CurCategory >= HungerCategory.Hungry;
            if (!BodyFeedingPolicy.ShouldSeekFood(pawn, vanillaDecision) ||
                BodyFeedingPolicy.GetRequestedNutrition(pawn,
                    Mathf.Max(0f, pawn.needs.food.MaxLevel -
                        pawn.needs.food.CurLevel)) <= 0f)
                return false;
            return BodyFeedingPolicy.CanFitFood(pawn, serving,
                FoodUtility.NutritionForEater(pawn, serving));
        }

        private static int GetNumberOfTimesToEatFromSystem(Pawn pawn,
            Building_FoodFaucet foodFaucet)
        {
            if (pawn?.needs?.food == null ||
                foodFaucet?.foodNetTrader?.FoodNet == null)
                return 0;
            float nutritionPerDispense = foodFaucet.nutritionPerDispense;
            float ratio = foodFaucet.foodNetTrader.FoodNet.FullnessToNutritionRatio;
            if (nutritionPerDispense <= 0f || ratio <= 0f ||
                float.IsNaN(ratio) || float.IsInfinity(ratio))
                return 0;
            bool vanillaDecision =
                pawn.needs.food.CurCategory >= HungerCategory.Hungry;
            if (!BodyFeedingPolicy.ShouldSeekFood(pawn, vanillaDecision))
                return 0;
            float wanted = BodyFeedingPolicy.GetRequestedNutrition(pawn,
                Mathf.Max(0f, pawn.needs.food.MaxLevel -
                    pawn.needs.food.CurLevel));
            if (wanted <= 0f) return 0;
            FullnessAndDietStats_ThingComp fullness =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness == null || fullness.Disabled) return 1;
            if (!BodyFeedingPolicy.CanFitPortion(pawn, nutritionPerDispense, ratio)) return 0;
            return 1;
        }
    }
}
