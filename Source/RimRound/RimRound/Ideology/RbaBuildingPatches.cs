using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimRound.Ideology
{
    // Native reform copies every precept into the live ideology, including the
    // ones referenced by active ritual Lords. Refuse that transaction before
    // the confirmation callback can close the editor, preserving its draft.
    [HarmonyPatch(typeof(IdeoDevelopmentUtility), nameof(IdeoDevelopmentUtility.ConfirmChangesToIdeo))]
    internal static class RbaBuildingReformConfirmation
    {
        private static bool Prefix(Ideo ideo, Ideo newIdeo)
        {
            if (!RbaBuildings.DeferReform(ideo, newIdeo)) return true;
            Messages.Message("RR_RbaReformRitualActive".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }

    // Also protect direct native callers that do not use the confirmation UI.
    [HarmonyPatch(typeof(IdeoDevelopmentUtility), nameof(IdeoDevelopmentUtility.ApplyChangesToIdeo))]
    internal static class RbaBuildingReformApplication
    {
        private static bool Prefix(Ideo ideo, Ideo newIdeo) => !RbaBuildings.DeferReform(ideo, newIdeo);
    }

    [HarmonyPatch(typeof(IdeoFoundation), nameof(IdeoFoundation.InitPrecepts))]
    internal static class RbaBuildingGeneration
    {
        private static void Postfix(IdeoFoundation __instance) => RbaBuildings.Reconcile(__instance.ideo);
    }

    [HarmonyPatch(typeof(IdeoUIUtility), nameof(IdeoUIUtility.DoPrecepts))]
    internal static class RbaBuildingBeforeCards
    {
        private static void Prefix(Ideo ideo) => RbaBuildings.Reconcile(ideo);
    }

    [HarmonyPatch(typeof(IdeoUIUtility), "DoPreceptsInt")]
    internal static class RbaBuildingCardCategory
    {
        private static void Prefix(Ideo ideo, ref Func<PreceptDef, bool> filter)
        {
            // 1.6 uses exact class equality for the Buildings category. Identify
            // that category by its native building Def, then extend only it.
            // No compiler-generated method names or global class checks change.
            if (filter == null) return;
            Func<PreceptDef, bool> original = filter;
            bool buildings = original(PreceptDefOf.IdeoBuilding);
            filter = def => def?.defName == RbaBuildings.PreceptDefName
                ? buildings && AbundanceRoles.Enabled(ideo) : original(def);
        }
    }

    [HarmonyPatch(typeof(Room), nameof(Room.GetRoomRoleLabel))]
    internal static class RbaBuildingRoomLabel
    {
        internal static bool HasRitualBed(Room room) => ModsConfig.IdeologyActive && room != null &&
            !room.PsychologicallyOutdoors && room.ContainedAndAdjacentThings.OfType<Building_Bed>().Any(b =>
                b.GetRoom() == room && AbundanceBeds.Valid(b, AbundanceBeds.Focus(b)?.Ideology));

        private static void Postfix(Room __instance, ref string __result)
        {
            if (!HasRitualBed(__instance)) return;
            if (__instance.Role != RoomRoleDefOf.Bedroom)
            {
                __result = "RR_RbaRitualRoomAnnotation".Translate(__result);
                return;
            }
            Pawn[] owners = __instance.Owners.Take(2).ToArray();
            string roomLabel = "RR_RbaRitualBedroom".Translate();
            __result = owners.Length == 0 ? roomLabel : owners.Length == 1
                ? "SomeonesRoom".Translate(owners[0].LabelShort, roomLabel, owners[0].Named("PAWN")).ToString()
                : "CouplesRoom".Translate(owners[0].LabelShort, owners[1].LabelShort, roomLabel,
                    owners[0].Named("PAWN1"), owners[1].Named("PAWN2")).ToString();
        }
    }

    // The selected building's stats gizmo uses this separate native label
    // path; its tooltip uses Room.GetRoomRoleLabel. Keep both displays aligned
    // without changing the shared RoomRoleDef or any room scoring.
    [HarmonyPatch(typeof(RoomRoleDef), nameof(RoomRoleDef.PostProcessedLabelCap))]
    internal static class RbaBuildingRoomStatsLabel
    {
        private static void Postfix(RoomRoleDef __instance, Room room, ref string __result)
        {
            if (room?.Role != __instance || !RbaBuildingRoomLabel.HasRitualBed(room)) return;
            __result = (room.Role == RoomRoleDefOf.Bedroom
                ? "RR_RbaRitualBedroom".Translate().ToString()
                : "RR_RbaRitualRoomAnnotation".Translate(__result).ToString()).CapitalizeFirst();
        }
    }
}
