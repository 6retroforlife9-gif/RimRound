using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;

namespace RimRound.FeedOther
{
    /// <summary>
    /// The independent Gluttonium gen step remains XML-defined so it keeps the
    /// same ordering as v1.0.66. This prefix supplies the current user frequency
    /// immediately before a new map is generated, or skips only that step when
    /// Gluttonium generation is disabled.
    /// </summary>
    [HarmonyPatch(typeof(GenStep_ScatterLumpsMineable),
        nameof(GenStep_ScatterLumpsMineable.Generate))]
    public static class GluttoniumGenerationSettingsPatch
    {
        private static readonly FieldInfo ForcedDefField = AccessTools.Field(
            typeof(GenStep_ScatterLumpsMineable),
            "forcedDefToScatter");
        private static readonly FieldInfo CountRangeField = AccessTools.Field(
            typeof(GenStep_ScatterLumpsMineable),
            "countPer10kCellsRange");

        [HarmonyPrefix]
        public static bool Prefix(GenStep_ScatterLumpsMineable __instance)
        {
            ThingDef forcedDef = ForcedDefField?.GetValue(__instance) as ThingDef;
            if (forcedDef?.defName != "RR_GluttoniumOre")
            {
                return true;
            }

            if (!FeedOtherMod.Settings.gluttoniumVeinsEnabled)
            {
                return false;
            }

            if (CountRangeField != null)
            {
                float frequency = FeedOtherMod.Settings.gluttoniumVeinsPer10kCells;
                CountRangeField.SetValue(__instance, new FloatRange(frequency, frequency));
            }

            return true;
        }
    }
}
