using HarmonyLib;
using RimRound.Utilities;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth))]
    class BodyPartDef_GetMaxHealth_AdjustMaxHPByWeight
    {
        public static void Postfix(ref float __result, BodyPartDef __instance, Pawn __0)
        {
            __result *= StageHealthPerks.PartHealthMultiplier(__0, __instance);
        }
    }
}
