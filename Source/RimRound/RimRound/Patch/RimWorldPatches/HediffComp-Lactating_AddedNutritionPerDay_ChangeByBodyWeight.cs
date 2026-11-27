using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimRound.Utilities;
using RimRound.Comps;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(HediffComp_Lactating))]
    [HarmonyPatch(nameof(HediffComp_Lactating.TryCharge))]
    public class HediffComp_Lactating_AddedNutritionPerDay_ChangeByBodyWeight
    {
        public static bool Prefix(ref float desiredChargeAmount, HediffComp_Lactating __instance, out float __state)
        {
            __state = __instance.parent.pawn.needs?.food?.CurLevel ?? 0f;
            float mult = BodyResourceUtility.GetMilkMultiplierByWeight(__instance.parent.pawn);
            desiredChargeAmount *= mult;
            return true;
        }

        public static void Postfix(HediffComp_Lactating __instance, float __state)
        {
            Pawn pawn = __instance.parent.pawn;
            if (pawn.needs?.food == null) return;
            // Observe the actual native transaction, including its caps and
            // resource growth factor. Do not multiply or subtract Food again.
            pawn.TryGetComp<FullnessAndDietStats_ThingComp>()?.RecordNativeResourceExpenditure(
                __state - pawn.needs.food.CurLevel);
        }


    }
}
