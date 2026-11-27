using HarmonyLib;
using RimWorld;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Preserve vanilla recreation variety while giving its explicitly
    /// energetic roaming activities a very small priority reduction.
    /// Reading, chess, prayer, and every other ordinary recreation giver keep
    /// their original RimWorld chance.
    /// </summary>
    [HarmonyPatch(typeof(JoyGiver), nameof(JoyGiver.GetChance))]
    public static class EnergeticJoyPriorityPatch
    {
        private const float EnergeticChanceMultiplier = 0.95f;

        [HarmonyPostfix]
        public static void Postfix(JoyGiver __instance, ref float __result)
        {
            if (__result > 0f &&
                (__instance is JoyGiver_GoForWalk || __instance is JoyGiver_GoSwimming))
            {
                __result *= EnergeticChanceMultiplier;
            }
        }
    }
}
