using AlienRace;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using UnityEngine;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(AlienRenderTreePatches))]
    [HarmonyPatch(nameof(AlienRenderTreePatches.TrySetupGraphIfNeededPrefix))]
    public class HarmonyPatches_TrySetupGraphIfNeededPrefix_UpdatePawnBodySize
    {
        // HAR restores the life-stage draw sizes in this method. Apply the
        // body-stage size afterwards, before the render tree builds its meshes.
        public static void Postfix(PawnRenderTree __0)
        {
            Pawn pawn = __0?.pawn;
            if (pawn?.RaceProps?.Humanlike != true)
                return;

            PawnBodyType_ThingComp bodyComp = pawn.TryGetComp<PawnBodyType_ThingComp>();
            if (bodyComp == null || bodyComp.usingCustomBodyMeshSize)
                return;

            AlienPartGenerator.AlienComp alienComp = pawn.TryGetComp<AlienPartGenerator.AlienComp>();
            if (alienComp == null)
                return;

            BodyTypeInfo? bodyTypeInfo = RacialBodyTypeInfoUtility.GetRacialBodyTypeInfo(pawn);
            if (bodyTypeInfo == null)
                return;

            float meshSize = bodyTypeInfo.Value.meshSize;
            if (float.IsNaN(meshSize) || float.IsInfinity(meshSize) || meshSize <= 0f)
                return;

            Vector2 stageSize = new Vector2(meshSize, meshSize);
            alienComp.customDrawSize = stageSize;
            float portraitSize = bodyComp.PreparedPresentation?.Stage?.PortraitScale ?? meshSize;
            // Keep the silhouette readable beside a native-sized head. World
            // meshes retain their physical stage size.
            portraitSize = Mathf.Min(portraitSize, 2.5f);
            alienComp.customPortraitDrawSize = new Vector2(portraitSize, portraitSize);
        }
    }
}
