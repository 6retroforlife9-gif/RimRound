using HarmonyLib;
using RimRound.Utilities;
using RimWorld.Planet;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.AddPawn))]
    public class Caravan_AddPawn_ChangeDietMode
    {
        public static void Prefix(Caravan __instance, Pawn __0, out bool __state)
        {
            __state = __0 != null && !__instance.ContainsPawn(__0);
        }

        public static void Postfix(Caravan __instance, Pawn __0, bool __state)
        {
            // AddPawn is also used for carried pawns and later additions.
            // Failed and repeated additions must not overwrite the saved mode.
            if (__state && __instance.ContainsPawn(__0))
                CaravanPatchUtility.SetDietModeToDisabledIncludingPassengers(__0);
        }
    }
}
