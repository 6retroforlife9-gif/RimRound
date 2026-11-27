using HarmonyLib;
using RimRound.Comps;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
        new[] { typeof(PawnGenerationRequest) })]
    public static class PawnGenerator_GeneratePawn_InitializeSocialTaste
    {
        public static void Postfix(Pawn __result)
        {
            // GenerateTraits precedes genes, and companion mods can replace
            // the preference utility. Initialize from the completed pawn.
            __result?.TryGetComp<ThingComp_PawnAttitude>()?.InitializeSocialBodyTaste();
        }
    }
}
