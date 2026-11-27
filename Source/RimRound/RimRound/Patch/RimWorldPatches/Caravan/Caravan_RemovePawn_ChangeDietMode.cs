using HarmonyLib;
using RimRound.Utilities;
using RimWorld.Planet;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.RemovePawn))]
    public class Caravan_RemovePawn_ChangeDietMode
    {
        public static void Prefix(Caravan __instance, Pawn __0)
        {
            if (__0 != null && __instance.ContainsPawn(__0))
                CaravanPatchUtility.RestoreDietModeIncludingPassengers(__0);
        }
    }
}
