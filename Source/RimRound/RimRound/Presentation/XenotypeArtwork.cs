using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimRound.Presentation
{
    // Captured on the game thread. The render path compares exact node identities;
    // it never enumerates genes, changes Defs, or infers a surface from its label.
    internal sealed class XenotypeArtworkCapture : IEquatable<XenotypeArtworkCapture>
    {
        private readonly struct Layer : IEquatable<Layer>
        {
            internal readonly Gene Owner;
            internal readonly PawnRenderNodeProperties Properties;
            internal readonly bool Replace;
            internal Layer(Gene owner, PawnRenderNodeProperties properties, bool replace)
            { Owner = owner; Properties = properties; Replace = replace; }
            public bool Equals(Layer other) => ReferenceEquals(Owner, other.Owner) &&
                ReferenceEquals(Properties, other.Properties) && Replace == other.Replace;
        }

        private readonly Layer[] layers;
        internal readonly PawnRenderNodeProperties BaseBodyProperties;
        internal readonly string FailureReason;

        private XenotypeArtworkCapture(List<Layer> layers, PawnRenderNodeProperties baseBody, string reason)
        { this.layers = layers.ToArray(); BaseBodyProperties = baseBody; FailureReason = reason; }

        internal static XenotypeArtworkCapture Capture(Pawn pawn)
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Xenotype artwork capture requires the game thread.");
            var layers = new List<Layer>();
            PawnRenderNodeProperties baseBody = null;
            string reason = null;
            FindBaseBody(pawn.RaceProps.renderTree?.root, ref baseBody, ref reason, 0);
            if (baseBody == null) reason = "xenotype-base-renderer-unsupported";
            bool foundFur = false;
            var genes = pawn.genes?.GenesListForReading;
            if (genes != null)
                for (int i = 0; i < genes.Count; i++)
                {
                    var gene = genes[i];
                    if (!gene.Active || gene.def.renderNodeProperties == null) continue;
                    foreach (var properties in gene.def.renderNodeProperties)
                        CaptureLayer(gene, properties, false, layers, ref foundFur, ref reason, 0);
                }
            if (pawn.story.furDef != null && !foundFur && reason == null)
                reason = "xenotype-fur-renderer-unsupported";
            return new XenotypeArtworkCapture(layers, baseBody, reason);
        }

        private static void FindBaseBody(PawnRenderNodeProperties properties, ref PawnRenderNodeProperties body,
            ref string reason, int depth)
        {
            if (properties == null || depth > 32) { reason = "xenotype-base-renderer-unsupported"; return; }
            if (properties.tagDef == PawnRenderNodeTagDefOf.Body)
            {
                if (body != null || properties.nodeClass != typeof(PawnRenderNode_Body) ||
                    properties.workerClass != typeof(PawnRenderNodeWorker_Body))
                    reason = "xenotype-base-renderer-unsupported";
                body = properties;
            }
            if (properties.children != null)
                foreach (var child in properties.children) FindBaseBody(child, ref body, ref reason, depth + 1);
        }

        private static void CaptureLayer(Gene owner, PawnRenderNodeProperties properties, bool head,
            List<Layer> layers, ref bool foundFur, ref string reason, int depth)
        {
            if (properties == null || depth > 32) { reason = "xenotype-layer-unsupported"; return; }
            // Head-only genes already use native ownership, including custom eye
            // workers and nested head features. Do not reinterpret their artwork.
            head |= properties.parentTagDef == PawnRenderNodeTagDefOf.Head || properties.tagDef == PawnRenderNodeTagDefOf.Head;
            // Class contracts, not package IDs: derived fur/body nodes retain
            // their surface role; AttachmentBody derives from Body but is a
            // separate feature and must be classified first.
            bool attachment = IsA(properties.workerClass, typeof(PawnRenderNodeWorker_AttachmentBody));
            bool fur = !head && !attachment && IsA(properties.nodeClass, typeof(PawnRenderNode_Fur)) &&
                IsA(properties.workerClass, typeof(PawnRenderNodeWorker_Fur));
            bool body = !head && !attachment && IsA(properties.nodeClass, typeof(PawnRenderNode_Body)) &&
                IsA(properties.workerClass, typeof(PawnRenderNodeWorker_Body));
            bool container = !properties.useGraphic && properties.nodeClass == typeof(PawnRenderNode) &&
                (properties.workerClass == typeof(PawnRenderNodeWorker) || properties.workerClass == typeof(PawnRenderNodeWorker_Body));
            if (!head && !fur && !body && !attachment && !container) reason = "xenotype-layer-unsupported";
            // A gene replacing the graph's main Body tag can also take ownership
            // of unrelated children. That is a custom base renderer, not merely
            // a surface that can be removed after traversal.
            if (!head && properties.tagDef == PawnRenderNodeTagDefOf.Body) reason = "xenotype-base-renderer-unsupported";
            foundFur |= fur;
            layers.Add(new Layer(owner, properties, fur || body));
            if (head) return;
            if (properties.children != null)
                foreach (var child in properties.children)
                    CaptureLayer(owner, child, false, layers, ref foundFur, ref reason, depth + 1);
        }

        private static bool IsA(Type candidate, Type contract) => candidate != null && contract.IsAssignableFrom(candidate);

        internal bool Replaces(PawnRenderNode node)
        {
            if (FailureReason != null || node == null) return false;
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (!layer.Replace || !ReferenceEquals(layer.Properties, node.Props)) continue;
                // Native gene setup assigns the root's gene after constructing
                // its children. Nested children therefore need ancestor ownership.
                var ancestor = node;
                while (ancestor != null && ancestor.gene == null) ancestor = ancestor.parent;
                if (ReferenceEquals(ancestor?.gene, layer.Owner)) return true;
            }
            return false;
        }

        public bool Equals(XenotypeArtworkCapture other)
        {
            if (other == null || FailureReason != other.FailureReason ||
                !ReferenceEquals(BaseBodyProperties, other.BaseBodyProperties) || layers.Length != other.layers.Length) return false;
            for (int i = 0; i < layers.Length; i++) if (!layers[i].Equals(other.layers[i])) return false;
            return true;
        }
        public override bool Equals(object obj) => Equals(obj as XenotypeArtworkCapture);
        public override int GetHashCode() => (BaseBodyProperties?.GetHashCode() ?? 0) ^ layers.Length;
    }

    [HarmonyPatch(typeof(PawnRenderNode_Body), nameof(PawnRenderNode_Body.GraphicFor))]
    public static class XenotypeArtworkBodyGraphicPatch
    {
        // Optional gene frameworks can overwrite HAR's result with their own
        // body texture or an empty graphic. The explicit override owns this result.
        [HarmonyAfter("OskarPotocki.VEF", "AnthrosonaeMod")]
        [HarmonyPriority(int.MinValue)]
        public static void Postfix(PawnRenderNode_Body __instance, Pawn pawn, ref Graphic __result)
        {
            var prepared = PawnPresentationOwner.For(pawn);
            if (prepared?.UsesXenotypeArtworkOverride != true || !PawnPresentationOwner.IsCustom(pawn) ||
                !ReferenceEquals(__instance.Props, prepared.XenotypeArtwork.BaseBodyProperties)) return;
            __result = pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Rotting ? prepared.RottenGraphic : prepared.BodyGraphic;
        }
    }

    [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.ParallelPreDraw))]
    public static class XenotypeArtworkDrawRequestsPatch
    {
        public static void Postfix(PawnRenderTree __instance, List<PawnGraphicDrawRequest> ___drawRequests)
        {
            var prepared = PawnPresentationOwner.For(__instance.pawn);
            if (prepared?.UsesXenotypeArtworkOverride != true || !PawnPresentationOwner.IsCustom(__instance.pawn)) return;
            // Filter after native traversal, not in CanDrawNow/GraphicFor: a
            // hidden parent would prevent its independent children from drawing.
            for (int i = ___drawRequests.Count - 1; i >= 0; i--)
                if (prepared.XenotypeArtwork.Replaces(___drawRequests[i].node)) ___drawRequests.RemoveAt(i);
        }
    }
}
