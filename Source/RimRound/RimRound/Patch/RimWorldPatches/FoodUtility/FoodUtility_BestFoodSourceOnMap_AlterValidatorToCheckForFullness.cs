using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(FoodUtility))]
    [HarmonyPatch(nameof(FoodUtility.BestFoodSourceOnMap))]
    public class FoodUtility_BestFoodSourceOnMap_AlterValidatorToCheckForFullness
    {
        private static void Prefix(Pawn eater, out FoodSearchBudgetScope __state)
        { __state = new FoodSearchBudgetScope(eater); }

        private static Exception Finalizer(Exception __exception, FoodSearchBudgetScope __state)
        { __state?.Dispose(); return __exception; }

        // Native optimality is a distance/mood score, not a strict meal tier.
        // Food-is-food perks and small-portion waste can therefore make nearby
        // raw ingredients beat a permitted meal. Reuse the native validator and
        // path/reservation search with a meal floor before accepting that fallback.
        public static void Postfix(Pawn getter, Pawn eater, bool desperate,
            ref ThingDef foodDef, FoodPreferability maxPref, bool allowDrug,
            bool allowDispenserFull, bool allowDispenserEmpty, bool allowForbidden,
            bool allowSociallyImproper, bool forceScanWholeMap, bool ignoreReservations,
            bool calculateWantedStackCount, FoodPreferability minPrefOverride,
            float? minNutrition, bool allowVenerated, ref Thing __result)
        {
            if (__result == null || foodDef?.ingestible == null ||
                foodDef.ingestible.preferability >= FoodPreferability.MealAwful ||
                minPrefOverride >= FoodPreferability.MealAwful || maxPref < FoodPreferability.MealAwful ||
                eater?.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel != true)
                return;
            ThingDef preparedDef;
            Thing prepared = FoodUtility.BestFoodSourceOnMap(getter, eater, desperate,
                out preparedDef, maxPref, allowPlant: false, allowDrug: allowDrug,
                allowCorpse: false, allowDispenserFull: allowDispenserFull,
                allowDispenserEmpty: allowDispenserEmpty, allowForbidden: allowForbidden,
                allowSociallyImproper: allowSociallyImproper, allowHarvest: false,
                forceScanWholeMap: forceScanWholeMap, ignoreReservations: ignoreReservations,
                calculateWantedStackCount: calculateWantedStackCount,
                minPrefOverride: FoodPreferability.MealAwful, minNutrition: minNutrition,
                allowVenerated: allowVenerated);
            if (prepared == null || preparedDef?.ingestible == null ||
                preparedDef.ingestible.preferability < FoodPreferability.MealAwful) return;
            __result = prepared;
            foodDef = preparedDef;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) 
        {
            List<CodeInstruction> codeInstructions = new List<CodeInstruction>(instructions);
            List<CodeInstruction> newInstructions = new List<CodeInstruction>();
            bool triedToApply = false;
            int startJndex = -1;

            MethodInfo zigmaMI = typeof(FoodUtility_BestFoodSourceOnMap_AlterValidatorToCheckForFullness).GetMethod(nameof(Zigma), BindingFlags.Public | BindingFlags.Static);


            if (zigmaMI is null)
                Log.Error($"Error getting method info for {nameof(Zigma)}!");

            for (int jndex = 0; jndex < codeInstructions.Count; ++jndex) 
            {
                if (codeInstructions[jndex].opcode == OpCodes.Ldftn && codeInstructions[jndex + 1].opcode == OpCodes.Newobj) 
                {
                    startJndex = jndex + 2;
                    break;
                }
            }

            newInstructions.Add(new CodeInstruction(OpCodes.Ldarg_1));
            newInstructions.Add(new CodeInstruction(OpCodes.Call, zigmaMI));



            if (startJndex != -1)
            {
                triedToApply = true;
                codeInstructions.InsertRange(startJndex, newInstructions);
            }

            if (!triedToApply)
                Log.Error($"Failed to apply transpiler for {nameof(FoodUtility_BestFoodSourceOnMap_AlterValidatorToCheckForFullness.Transpiler)}");

            return codeInstructions.AsEnumerable();
        }
        
        static bool FeedingTubeModInstalled() 
        {
            try
            {
                Activator.CreateInstance("RimRound Feeding Tube", "Building_FoodFaucet");
                return true;
            }
            catch (Exception) 
            {
                return false;
            }
        }

        public static Predicate<Thing> Zigma(Predicate<Thing> ogPredicate, Pawn eater)//, Pawn eater, ThingDef foodDef)
        {
            Predicate<Thing> dogma = delegate (Thing t)
            {
                if (t == null || !ogPredicate(t))
                    return false;
                if (eater == null || eater.RaceProps == null)
                    return true;
                if (!eater.RaceProps.Humanlike)
                {
                    return !t.def.defName.Contains("FeedingTube") &&
                        !t.def.defName.Contains("FoodFaucet");
                }
                if (t.def.ingestible == null)
                    return true;
                if (eater.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel == true &&
                    OrdinaryFoodIntake.IsAtomicDef(t.def)) return false;
                float nutrition = FoodUtility.NutritionForEater(eater, t);
                if (nutrition <= 0f)
                    return true;
                // A positive useful portion can fit even when its whole item
                // is larger than the live permitted target; commit records waste.
                return BodyFeedingPolicy.CanFitFood(eater, t, nutrition);
            };

            return dogma;
        }

    }
}
