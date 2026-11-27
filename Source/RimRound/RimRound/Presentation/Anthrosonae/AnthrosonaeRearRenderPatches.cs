using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimRound.Presentation.Anthrosonae
{
    internal static class PreparedAnthrosonaeRearPose
    {
        internal static readonly Quaternion NorthTurn = new Quaternion(0f, 1f, 0f, 0f);
        internal static bool TryGet(PawnRenderNode node, PawnDrawParms parms, out PreparedAnthrosonaeAttachmentDelta placement)
        {
            placement = null;
            if (parms.facing != Rot4.South) return false;
            var pilot = PreparedAnthrosonaePilot.ForAttachment(node, parms);
            return pilot != null && pilot.TryGetSouthRear(node, parms, out placement);
        }
        internal static bool Exposed(PawnRenderNode node, PawnDrawParms parms) =>
            TryGet(node, parms, out var placement) && placement.SouthRearPolicy == AppearanceRearPosePolicy.NorthTurned;
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.CanDrawNow))]
    public static class PreparedAnthrosonaeRearVisibilityPatch
    {
        public static bool Prefix(PawnRenderNode node, ref PawnDrawParms parms, ref bool __result)
        {
            if (node?.gene == null || parms.facing != Rot4.South) return true;
            if (!PreparedAnthrosonaeRearPose.TryGet(node, parms, out var placement)) return true;
            if (placement.SouthRearPolicy == AppearanceRearPosePolicy.Hidden)
            { __result = false; return false; }
            // This base method receives its own value copy. Only the audited
            // attachment's facing gate sees North; its caller still sees South.
            // Preserve rot mode, skip flags, health and DebugEnabled checks.
            // The derived Body worker subsequently keeps NoBody/bed/duty gates.
            parms.facing = Rot4.North;
            return true;
        }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), "GetMaterial")]
    public static class PreparedAnthrosonaeRearMaterialPatch
    {
        public static void Prefix(PawnRenderNode node, ref PawnDrawParms parms)
        {
            if (node?.gene == null || parms.facing != Rot4.South) return;
            // Native graphic/palette and invisibility handling remain intact.
            if (PreparedAnthrosonaeRearPose.Exposed(node, parms)) parms.facing = Rot4.North;
        }
    }

    [HarmonyPatch(typeof(PawnRenderNode), nameof(PawnRenderNode.GetMesh))]
    public static class PreparedAnthrosonaeRearMeshPatch
    {
        public static void Prefix(PawnRenderNode __instance, ref PawnDrawParms parms)
        {
            if (__instance?.gene == null || parms.facing != Rot4.South) return;
            if (PreparedAnthrosonaeRearPose.Exposed(__instance, parms)) parms.facing = Rot4.North;
        }
    }

}
