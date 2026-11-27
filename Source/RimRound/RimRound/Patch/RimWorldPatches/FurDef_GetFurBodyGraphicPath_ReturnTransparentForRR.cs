using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(FurDef))]
    [HarmonyPatch(nameof(FurDef.GetFurBodyGraphicPath))]
    public class FurDef_GetFurBodyGraphicPath_ReturnTransparentForRR
    {
        public static void Postfix(FurDef __instance, ref string __result, Pawn pawn)
        {
            if (RimRound.Presentation.PawnPresentationOwner.IsCustom(pawn) &&
                (RimRound.Presentation.PawnPresentationOwner.For(pawn)?.AnthrosonaePilot != null ||
                 RimRound.Presentation.PawnPresentationOwner.For(pawn)?.UsesXenotypeArtworkOverride == true) && __instance == pawn.story.furDef) return;
            if (BodyTypeUtility.HasCustomBody(pawn))
                __result = "BlankTexture";

            return;
        }
    }
}
