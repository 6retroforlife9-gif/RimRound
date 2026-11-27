using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(FoodUtility))]
    [HarmonyPatch(nameof(FoodUtility.FoodOptimality))]
    public class FoodUtility_FoodOptimality_AdjustOptimality
    {
        public static void Postfix(ref float __result, Pawn __0, Thing __1, ThingDef __2) 
        {
            if (__0 == null || __1 == null || __1.def?.ingestible == null || BodyFeedingPolicy.IsPackingFood(__0))
                return;
            float nutrition = FoodUtility.NutritionForEater(__0, __1);
            if (!BodyFeedingPolicy.CanFitFood(__0, __1, nutrition))
                __result -= 9999999f;
        }
    }
}
