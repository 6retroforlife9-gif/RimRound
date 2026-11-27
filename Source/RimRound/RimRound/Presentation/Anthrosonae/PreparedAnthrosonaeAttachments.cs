using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimRound.Presentation.Anthrosonae
{
    public sealed class PreparedAnthrosonaeAttachmentDelta
    {
        public readonly CapturedAnthrosonaeAttachment Capture;
        public readonly bool FitValidated;
        public readonly string PlacementPolicyId;
        public readonly AppearanceRearPosePolicy SouthRearPolicy;
        public readonly float SouthRearLayerDelta;
        private readonly Vector3 northMap, eastMap, southMap, westMap, northPortrait, eastPortrait, southPortrait, westPortrait;
        internal PreparedAnthrosonaeAttachmentDelta(CapturedAnthrosonaeAttachment capture, AppearanceAttachmentFit fit,
            float mapScale, float portraitScale, float nativeBodyScale, float nativeBodyWidth)
        {
            Capture = capture; PlacementPolicyId = fit.PlacementPolicyId; FitValidated = fit.Validated;
            SouthRearPolicy = fit.RearFit?.Policy ?? AppearanceRearPosePolicy.Native;
            var family = capture.Family;
            float nativeOffsetScale = family.ScaleOffsetByBodySize ? nativeBodyScale : 1f;
            float map = (mapScale - 1f) * nativeOffsetScale;
            float portrait = (Mathf.Min(portraitScale, 2.5f) - 1f) * nativeOffsetScale;
            // Resolve the audited raw offsets once; never read live DrawData.
            Vector3 north = family.NorthData.offset ?? family.DefaultData.offset ?? Vector3.zero;
            Vector3 south = family.SouthData.offset ?? family.DefaultData.offset ?? Vector3.zero;
            Vector3 east = family.EastData.offset ?? (family.WestData.offset.HasValue ?
                Mirror(family.WestData.offset.Value) : family.DefaultData.offset ?? Vector3.zero);
            Vector3 west = family.WestData.offset ?? (family.EastData.offset.HasValue ?
                Mirror(family.EastData.offset.Value) : family.DefaultData.offset ?? Vector3.zero);
            northMap = Delta(north, map); eastMap = Delta(east, map); southMap = Delta(south, map); westMap = Delta(west, map);
            northPortrait = Delta(north, portrait); eastPortrait = Delta(east, portrait);
            southPortrait = Delta(south, portrait); westPortrait = Delta(west, portrait);
            if (fit.NorthJoint != null)
            {
                // Giant North artwork exposes its rear. Attach the native-size
                // painted origin to the authored butt/back joint, not the scaled
                // native offset. Other facings and ordinary roots are unchanged.
                var joint = fit.NorthJoint;
                float width = family.MeshSize.x * nativeBodyScale, height = family.MeshSize.y * nativeBodyScale;
                northMap = new Vector3(joint.DeltaX(nativeBodyWidth * mapScale, width, north.x * nativeOffsetScale), 0f,
                    joint.DeltaZ(nativeBodyWidth * mapScale, height, north.z * nativeOffsetScale));
                northPortrait = new Vector3(joint.DeltaX(nativeBodyWidth * Mathf.Min(portraitScale, 2.5f), width, north.x * nativeOffsetScale), 0f,
                    joint.DeltaZ(nativeBodyWidth * Mathf.Min(portraitScale, 2.5f), height, north.z * nativeOffsetScale));
            }
            if (fit.SideJoint != null)
            {
                // Explicit attachment meshes bypass HAR's enlarged body mesh.
                // Align the painted tail/wing origin with this body's authored
                // joint, independently of the native mesh-centre offset.
                // Keep native mesh size, altitude and ancestor transforms.
                float attachmentWidth = family.MeshSize.x * nativeBodyScale;
                float attachmentHeight = family.MeshSize.y * nativeBodyScale;
                var joint = fit.SideJoint;
                eastMap = new Vector3(joint.DeltaX(nativeBodyWidth * mapScale, attachmentWidth, east.x * nativeOffsetScale), 0f,
                    joint.DeltaZ(nativeBodyWidth * mapScale, attachmentHeight, east.z * nativeOffsetScale));
                eastPortrait = new Vector3(joint.DeltaX(nativeBodyWidth * Mathf.Min(portraitScale, 2.5f), attachmentWidth, east.x * nativeOffsetScale), 0f,
                    joint.DeltaZ(nativeBodyWidth * Mathf.Min(portraitScale, 2.5f), attachmentHeight, east.z * nativeOffsetScale));
                westMap = Mirror(eastMap); westPortrait = Mirror(eastPortrait);
            }
            if (SouthRearPolicy == AppearanceRearPosePolicy.NorthTurned)
            {
                var joint = fit.RearFit.SouthJoint;
                float width = family.MeshSize.x * nativeBodyScale, height = family.MeshSize.y * nativeBodyScale;
                southMap = new Vector3(joint.DeltaX(nativeBodyWidth * mapScale, width, south.x * nativeOffsetScale), 0f,
                    joint.DeltaZ(nativeBodyWidth * mapScale, height, south.z * nativeOffsetScale));
                southPortrait = new Vector3(joint.DeltaX(nativeBodyWidth * Mathf.Min(portraitScale, 2.5f), width, south.x * nativeOffsetScale), 0f,
                    joint.DeltaZ(nativeBodyWidth * Mathf.Min(portraitScale, 2.5f), height, south.z * nativeOffsetScale));
                // Exposed rear parts need the native North foreground layer.
                // Keep native debug/animation offsets and common bed transforms.
                SouthRearLayerDelta = (family.NorthData.layer ?? family.DefaultData.layer ?? 0f) -
                    (family.SouthData.layer ?? family.DefaultData.layer ?? 0f);
            }
        }
        private static Vector3 Mirror(Vector3 value) => new Vector3(-value.x, value.y, value.z);
        // Never alter altitude/depth. All sixteen audited source offsets have Y=0.
        private static Vector3 Delta(Vector3 value, float factor) => new Vector3(value.x * factor, 0f, value.z * factor);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal bool HasFiniteDeltas => Finite(northMap) && Finite(eastMap) && Finite(southMap) && Finite(westMap) &&
            Finite(northPortrait) && Finite(eastPortrait) && Finite(southPortrait) && Finite(westPortrait);
        internal bool Matches(PawnRenderNode node) => node != null && ReferenceEquals(node.gene, Capture.Gene) &&
            ReferenceEquals(node.Props, Capture.Properties);
        internal Vector3 DeltaFor(int facing, bool portrait)
        {
            switch (facing)
            {
                case 0: return portrait ? northPortrait : northMap;
                case 1: return portrait ? eastPortrait : eastMap;
                case 2: return portrait ? southPortrait : southMap;
                case 3: return portrait ? westPortrait : westMap;
                default: return Vector3.zero;
            }
        }
    }

    // Complete immutable placement result; publish only after every active
    // attachment has an explicit mapping-specific fit.
    public sealed class PreparedAnthrosonaePilot
    {
        public readonly AnthrosonaeCapture Capture;
        public readonly AppearanceSupportDecision Decision;
        public readonly CapturedAnthrosonaeAttachmentSet AttachmentCapture;
        public readonly IReadOnlyList<PreparedAnthrosonaeAttachmentDelta> Attachments;
        public readonly bool AttachmentFitValidated;
        private PreparedAnthrosonaePilot(AnthrosonaeCapture capture, AppearanceSupportDecision decision,
            CapturedAnthrosonaeAttachmentSet attachmentCapture, PreparedAnthrosonaeAttachmentDelta[] attachments)
        {
            Capture = capture; Decision = decision; AttachmentCapture = attachmentCapture;
            Attachments = Array.AsReadOnly((PreparedAnthrosonaeAttachmentDelta[])attachments.Clone());
            AttachmentFitValidated = attachments.All(x => x.FitValidated);
        }
        private static bool PositiveFinite(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool TryCreate(AnthrosonaeCapture capture, CapturedAnthrosonaeAttachmentSet attachmentCapture,
            AppearanceSupportDecision decision, ResolvedStageEntry stage,
            out PreparedAnthrosonaePilot prepared, out string reason)
        {
            prepared = null; reason = "incomplete-attachment-capture";
            if (!UnityData.IsInMainThread) { reason = "appearance-capture-off-thread"; return false; }
            if (capture?.Descriptor == null || attachmentCapture == null) return false;
            if (capture.FailureReason != null) { reason = capture.FailureReason; return false; }
            if (decision?.Kind != AppearanceDecisionKind.PreparedBaseBody || decision.Mapping == null)
            { reason = decision?.FallbackReason ?? "native-appearance-required"; return false; }
            if (stage == null || !PositiveFinite(stage.MapScale) || !PositiveFinite(stage.PortraitScale))
            { reason = "invalid-attachment-stage-scale"; return false; }
            if (!PositiveFinite(capture.NativeBodyScale)) { reason = "unsupported-native-body-scale"; return false; }
            if (!PositiveFinite(capture.NativeBodyWidth)) { reason = "unsupported-native-body-scale"; return false; }
            if (!AnthrosonaeBuiltInFamilies.ExpectedNodesInitialized) { reason = "unsupported-attachment-api"; return false; }
            int expectedAttachments = 0;
            foreach (var layer in capture.Descriptor.Layers)
            {
                if (layer.Location == AppearanceLayerLocation.Head) continue;
                if (layer.Location != AppearanceLayerLocation.Body ||
                    !AnthrosonaeBuiltInFamilies.ExpectedNodes.TryGetValue(layer.Key, out var expected))
                { reason = "unsupported-body-layer"; return false; }
                if (!expected.Structure.Equals(layer.Structure)) { reason = "modified-body-layer"; return false; }
                if (expected.Role != RegisteredAppearanceRole.BodyAttachment) continue;
                expectedAttachments++;
                if (!attachmentCapture.Entries.Any(x => x.Key.Equals(layer.Key) && x.Structure.Equals(layer.Structure))) return false;
            }
            if (expectedAttachments != attachmentCapture.Count) return false;
            if (decision.AttachmentFits.Count != attachmentCapture.Count) return false;
            var placements = new List<PreparedAnthrosonaeAttachmentDelta>(attachmentCapture.Count);
            foreach (var attachment in attachmentCapture.Entries)
            {
                var fits = decision.AttachmentFits.Where(x => x.Family == attachment.Family.FamilyId).ToArray();
                if (fits.Length != 1) { reason = "missing-attachment-fit"; return false; }
                if (!fits[0].Validated && !decision.Mapping.DiagnosticOnly)
                { reason = "missing-attachment-fit"; return false; }
                var placement = new PreparedAnthrosonaeAttachmentDelta(attachment, fits[0],
                    stage.MapScale, stage.PortraitScale, capture.NativeBodyScale, capture.NativeBodyWidth);
                if (!placement.HasFiniteDeltas) { reason = "invalid-attachment-offset"; return false; }
                placements.Add(placement);
            }
            prepared = new PreparedAnthrosonaePilot(capture, decision, attachmentCapture, placements.ToArray());
            reason = null; return true;
        }
        public bool MatchesFur(PawnRenderNode node) => node != null && Capture.CoatGene != null &&
            ReferenceEquals(node.gene, Capture.CoatGene) && ReferenceEquals(node.Props, Capture.CoatProperties);
        internal static PreparedAnthrosonaePilot ForAttachment(PawnRenderNode node, PawnDrawParms parms)
        {
            // These base-worker hooks run for every node. Reject unrelated
            // bodies, apparel and head decorations before looking up a pawn comp.
            if (node?.gene == null || node.Worker.GetType() != typeof(PawnRenderNodeWorker_AttachmentBody)) return null;
            var prepared = PawnPresentationOwner.For(parms.pawn);
            if (prepared?.AnthrosonaePilot == null || !prepared.IsCustom) return null;
            if (parms.pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Dessicated &&
                prepared.Stage.DessicationPolicy != "body") return null;
            return prepared.AnthrosonaePilot;
        }
        public bool TryGetSouthRear(PawnRenderNode node, PawnDrawParms parms, out PreparedAnthrosonaeAttachmentDelta placement)
        {
            placement = null;
            // Only these audited, unanchored, central native attachment nodes
            // may borrow North visibility/materials. Never bypass a body-part
            // facing gate or an unknown derived worker.
            if (parms.facing != Rot4.South || node == null || node.bodyPart != null ||
                node.Worker.GetType() != typeof(PawnRenderNodeWorker_AttachmentBody)) return false;
            for (int i = 0; i < Attachments.Count; i++)
                if (Attachments[i].SouthRearPolicy != AppearanceRearPosePolicy.Native && Attachments[i].Matches(node))
                { placement = Attachments[i]; return true; }
            return false;
        }
        public bool TryGetAttachmentDelta(PawnRenderNode node, PawnDrawParms parms, out Vector3 delta)
        {
            delta = Vector3.zero;
            int facing = parms.facing.AsInt;
            if (facing < 0 || facing > 3 || node == null) return false;
            bool portrait = (parms.flags & PawnRenderFlags.Portrait) != 0;
            // At most sixteen immutable identity entries. No gene enumeration,
            // live placement reads, reflection, allocation or cache mutation here.
            for (int i = 0; i < Attachments.Count; i++)
                if (Attachments[i].Matches(node)) { delta = Attachments[i].DeltaFor(facing, portrait); return true; }
            return false;
        }
    }
}
