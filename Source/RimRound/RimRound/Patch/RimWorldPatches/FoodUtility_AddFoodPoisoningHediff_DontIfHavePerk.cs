using HarmonyLib;
using Verse;
using RimWorld;
using RimRound.Comps;
using RimRound.Utilities;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(FoodUtility))]
    [HarmonyPatch(nameof(FoodUtility.AddFoodPoisoningHediff))]
    public class FoodUtility_AddFoodPoisoningHediff_DontIfHavePerk 
    {
        public static bool Prefix(Pawn pawn) 
        {
            if (StageHealthPerks.IsFoodPoisoningImmune(pawn)) return false;

            if (pawn is null || !(pawn.TryGetComp<FullnessAndDietStats_ThingComp>() is FullnessAndDietStats_ThingComp comp))
                return true;

            if ((comp.perkLevels?.EffectLevel("RR_TitaniumStomach_Title") ?? 0) > 0)
                return false;

            return true;
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.GetFoodPoisonChanceFactor))]
    internal static class FoodUtility_GetFoodPoisonChanceFactor_StageImmunity
    {
        public static void Postfix(Pawn ingester, ref float __result)
        {
            if (StageHealthPerks.IsFoodPoisoningImmune(ingester)) __result = 0f;
        }
    }
}
