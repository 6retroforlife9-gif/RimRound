using HarmonyLib;
using RimRound.Utilities;
using RimWorld.Planet;
using System;
using System.Reflection;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.RemoveAllPawns))]
    public class Caravan_RemoveAllPawns_ChangeDietMode
    {
        public static void Prefix(Caravan __instance)
        {
            CaravanPatchUtility.RestoreCaravanDietMode(__instance);
        }

        public static MethodInfo AllPawnsAboardMI() => CaravanPatchUtility.AllPawnsAboardMI();
        public static Type GetVehiclePawnType() => CaravanPatchUtility.GetVehiclePawnType();
    }
}
