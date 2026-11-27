using RimRound.Utilities;
using RimWorld.Planet;
using System;
using System.Reflection;

namespace RimRound.Patch
{
    public class VehicleFramework_CaravanHelper_MakeVehicleCaravan_ChangeDietMode
    {
        public static void Postfix(Caravan __result)
        {
            // Use successful native membership, not the caller's enumerable.
            // The pawn-owned guard preserves the original mode when AddPawn
            // has already handled a member or a boarded passenger.
            CaravanPatchUtility.SetCaravanDietModeToDisabled(__result);
        }

        public static PatchCollection GetPatchCollection()
        {
            return new PatchCollection
            {
                postfix = typeof(VehicleFramework_CaravanHelper_MakeVehicleCaravan_ChangeDietMode).GetMethod(
                    nameof(Postfix), ModCompatibilityUtility.majorFlags)
            };
        }

        public static MethodInfo AllPawnsAboardMI() => CaravanPatchUtility.AllPawnsAboardMI();
        public static Type GetVehiclePawnType() => CaravanPatchUtility.GetVehiclePawnType();
    }
}
