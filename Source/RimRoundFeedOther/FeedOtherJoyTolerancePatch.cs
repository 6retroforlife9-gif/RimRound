using HarmonyLib;
using RimWorld;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Feed/share eligibility is governed by fullness, attitude, exemptions,
    /// and partner availability. It should not silently disappear because a
    /// pawn recently gained Gluttonous recreation from ordinary food.
    /// </summary>
    [HarmonyPatch(typeof(JoyToleranceSet), nameof(JoyToleranceSet.Notify_JoyGained))]
    public static class FeedOtherJoyTolerancePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(JoyKindDef __1)
        {
            // Joy itself is still gained by Need_Joy.GainJoy. Skipping only
            // this notification keeps the dedicated selection category fresh,
            // so vanilla's boredom gate and tolerance weighting cannot hide it.
            return __1 != FeedOtherDefOf.RR_FeedOtherJoy;
        }
    }
}
