using HarmonyLib;
using RimRound.Comps;
using Verse;
using RimWorld;
using RimRound.Utilities;
using RimRound.Things;
using UnityEngine;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(PawnRenderer))]
    [HarmonyPatch("GetBodyPos")]
    public class PawnRenderer_GetBodyPos_HideBlankets
    {
        private delegate Vector3 BodyPosition(PawnRenderer renderer,Vector3 drawLoc,PawnPosture posture,out bool showBody);
        private static readonly BodyPosition BodyPositionFor=AccessTools.MethodDelegate<BodyPosition>(
            AccessTools.Method(typeof(PawnRenderer),"GetBodyPos"));
        // Kept for the existing save-load hook. The render path no longer
        // keeps a shared pawn cache that needs invalidating.
        public static void InvalidateCaches() { }

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnRenderer __instance, ref bool __2, Pawn ___pawn,ref Vector3 __result,PawnPosture posture)
        {
            Pawn pawn = ___pawn;

            if (pawn is null || !(pawn?.RaceProps?.Humanlike is bool b && b))
                return;

            HideCovers_ThingComp comp = pawn.TryGetComp<HideCovers_ThingComp>();

            if (comp is null)
                return;


            if (pawn.CurrentBed() is Building_Bed bed) 
            {
                if(posture!=PawnPosture.Standing&&bed is Building_BlobBed blob&&blob.IsOperational&&blob.HostBed is Building_Bed support&&
                    !(support is Building_BlobBed)&&blob.originatingPawn is Pawn host)
                {
                    // The physical, reservable guest slot stays beside the bed;
                    // only the guest's rendered body rests over the host.
                    bool hostVisible;
                    __result=BodyPositionFor(host.Drawer.renderer,host.DrawPos,host.GetPosture(),out hostVisible);
                    __result.y+=0.08f;
                    __2=true;
                    return;
                }
                if(posture!=PawnPosture.Standing&&pawn.TryGetComp<MakeBlobIntoBed_ThingComp>()?.blobBed is Building_BlobBed hosted&&hosted.IsOperational)
                {
                    __2=true;
                    return;
                }
                if (bed.IsBlobBed())
                {
                    __2 = true;
                    return;
                }
                __2 = comp.HideCovers;
            }
        }
    }
}
