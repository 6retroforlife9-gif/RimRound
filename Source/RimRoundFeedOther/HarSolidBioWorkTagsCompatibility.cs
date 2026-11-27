using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    // HAR replaces the native solid-bio selection. Older versions omit the
    // vanilla required-work-tag check, so a nonviolent chief can exhaust every
    // generation retry. Reject only that incompatible choice; native shuffled
    // generation remains responsible for finding a usable replacement.
    [HarmonyPatch]
    internal static class HarSolidBioWorkTagsCompatibility
    {
        private static readonly Type HarType = AccessTools.TypeByName("AlienRace.HarmonyPatches");
        private static readonly MethodInfo Selector = HarType == null ? null : AccessTools.Method(HarType,
            "TryGetRandomUnusedSolidBioForPostfix", new[] { typeof(List<BackstoryCategoryFilter>),
                typeof(bool).MakeByRefType(), typeof(PawnBio).MakeByRefType(), typeof(PawnKindDef),
                typeof(Gender), typeof(string) });
        private static readonly FieldInfo BioReference = HarType == null ? null : AccessTools.Field(HarType, "bioReference");

        private static bool Prepare() => Selector != null && Selector.ReturnType == typeof(void) &&
            BioReference != null && !BioReference.FieldType.IsValueType;
        private static MethodBase TargetMethod() => Selector;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix([HarmonyArgument(1)] ref bool selected,
            [HarmonyArgument(2)] ref PawnBio bio, PawnKindDef kind)
        {
            if (!selected || bio == null || kind == null || kind.requiredWorkTags == WorkTags.None) return;
            WorkTags disabled = (bio.childhood?.workDisables ?? WorkTags.None) |
                (bio.adulthood?.workDisables ?? WorkTags.None);
            if ((disabled & kind.requiredWorkTags) == WorkTags.None) return;
            selected = false;
            bio = null;
            // The rejected biography must not contribute HAR forced items or
            // hediffs to the replacement pawn.
            BioReference.SetValue(null, null);
        }
    }
}
