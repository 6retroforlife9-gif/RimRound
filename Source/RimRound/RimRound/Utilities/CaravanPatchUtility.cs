using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimRound.Utilities
{
    public static class CaravanPatchUtility
    {
        public static void SetDietModeToDisabled(Pawn p)
        {
            if (p?.TryGetComp<Comps.FullnessAndDietStats_ThingComp>() is Comps.FullnessAndDietStats_ThingComp comp &&
                !comp.UsesBodyModel && !comp.CaravanDietModeSuspended)
            {
                comp.preCaravanDietMode = comp.DietMode;
                comp.DietMode = Comps.DietMode.Disabled;
                comp.CaravanDietModeSuspended = true;
            }
        }

        public static void RestoreDietMode(Pawn p)
        {
            if (p?.TryGetComp<Comps.FullnessAndDietStats_ThingComp>() is Comps.FullnessAndDietStats_ThingComp comp)
            {
                // The saved pawn field remains authoritative after a reload,
                // when the transient duplicate-entry guard has not been set.
                if (!comp.UsesBodyModel)
                    comp.DietMode = comp.preCaravanDietMode;
                comp.CaravanDietModeSuspended = false;
            }
        }

        public static void SetDietModeToDisabledIncludingPassengers(Pawn pawn)
        {
            foreach (Pawn member in PawnsIncludingPassengers(pawn))
                SetDietModeToDisabled(member);
        }

        public static void RestoreDietModeIncludingPassengers(Pawn pawn)
        {
            foreach (Pawn member in PawnsIncludingPassengers(pawn))
                RestoreDietMode(member);
        }

        public static void SetCaravanDietModeToDisabled(Caravan caravan)
        {
            if (caravan == null) return;
            foreach (Pawn pawn in caravan.PawnsListForReading)
                SetDietModeToDisabledIncludingPassengers(pawn);
        }

        public static void RestoreCaravanDietMode(Caravan caravan)
        {
            if (caravan == null) return;
            // Native RemoveAllPawns clears the owner directly. Its live members
            // are available here both for new caravans and loaded saves.
            foreach (Pawn pawn in caravan.PawnsListForReading)
                RestoreDietModeIncludingPassengers(pawn);
        }

        private static IEnumerable<Pawn> PawnsIncludingPassengers(Pawn pawn)
        {
            if (pawn == null) yield break;
            yield return pawn;
            Type vehicleType = GetVehiclePawnType();
            if (vehicleType == null || !vehicleType.IsInstanceOfType(pawn)) yield break;
            MethodInfo getter = AllPawnsAboardMI();
            if (getter == null) yield break;
            IEnumerable<Pawn> passengers = getter.Invoke(pawn, null) as IEnumerable<Pawn>;
            if (passengers == null) yield break;
            foreach (Pawn passenger in passengers)
                if (passenger != null && passenger != pawn)
                    yield return passenger;
        }

        // These caches contain mod metadata only, never pawns, caravans or worlds.
        private static bool vehicleTypeChecked;
        private static Type vehiclePawnType;
        private static MethodInfo allPawnsAboard;

        public static Type GetVehiclePawnType()
        {
            if (!vehicleTypeChecked)
            {
                vehiclePawnType = ModCompatibilityUtility.GetTypeFromMod("Vehicle Framework", "VehiclePawn");
                vehicleTypeChecked = true;
            }
            return vehiclePawnType;
        }

        public static MethodInfo AllPawnsAboardMI()
        {
            if (allPawnsAboard == null)
                allPawnsAboard = GetVehiclePawnType()?.GetProperty("AllPawnsAboard",
                    BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod(true);
            return allPawnsAboard;
        }

        public static int GetUniqueID(Caravan caravan) => caravan.ID;
    }
}
