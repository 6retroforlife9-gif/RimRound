using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using Verse;

namespace RimRound.Patch
{
    // Vanilla's cached pawn frame covers only a two-cell square. Enlarged
    // RimRound meshes can consume its remaining margin and get cropped.
    [HarmonyPatch(typeof(PawnRenderer), "ParallelGetPreRenderResults")]
    public static class PawnRenderer_ParallelGetPreRenderResults_DisableOversizedAtlas
    {
        public static void Prefix(Pawn ___pawn, ref bool disableCache)
        {
            if (disableCache || ___pawn?.RaceProps?.Humanlike != true)
                return;

            PawnBodyType_ThingComp bodyComp = ___pawn.TryGetComp<PawnBodyType_ThingComp>();
            if (bodyComp == null || bodyComp.usingCustomBodyMeshSize)
                return;

            // A normal adult body mesh is 1.5 world units wide in a two-unit
            // atlas frame. Any stage enlargement reduces the small margin
            // left for the head, apparel and posture offsets.
            if (RacialBodyTypeInfoUtility.HasEnlargedBodyMesh(___pawn, bodyComp))
                disableCache = true;
        }
    }
}
