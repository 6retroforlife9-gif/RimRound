using System;
using System.Collections.Generic;
using System.Linq;

namespace RimRound.Presentation.Anthrosonae
{
    public enum AppearanceDecisionKind { ExistingPresentation, NativeComposite, PreparedBaseBody }
    public sealed class AppearanceSupportDecision
    {
        public readonly AppearanceDecisionKind Kind;
        public readonly string FallbackReason;
        public readonly AppearanceNodeKey? UnsupportedNode;
        public readonly AppearanceCoatMapping Mapping;
        public readonly IReadOnlyList<AppearanceNodeKey> SuppressedFurNodes;
        public readonly IReadOnlyList<AppearanceAttachmentFit> AttachmentFits;
        public bool BypassLegacyBlankFurPath => Kind == AppearanceDecisionKind.PreparedBaseBody;
        public bool IsCompleteSupport => Kind == AppearanceDecisionKind.PreparedBaseBody && !Mapping.DiagnosticOnly;
        internal AppearanceSupportDecision(AppearanceDecisionKind kind, string reason = null, AppearanceNodeKey? unsupported = null,
            AppearanceCoatMapping mapping = null, IEnumerable<AppearanceNodeKey> suppressed = null, IEnumerable<AppearanceAttachmentFit> fits = null)
        {
            Kind = kind; FallbackReason = reason; UnsupportedNode = unsupported; Mapping = mapping;
            SuppressedFurNodes = Array.AsReadOnly((suppressed ?? new AppearanceNodeKey[0]).ToArray());
            AttachmentFits = Array.AsReadOnly((fits ?? new AppearanceAttachmentFit[0]).ToArray());
        }
        public bool SuppressesFurNode(AppearanceNodeKey key) => SuppressedFurNodes.Contains(key);
    }

    public static class AppearancePolicy
    {
        private static AppearanceSupportDecision Native(string reason, AppearanceNodeKey? node = null) =>
            new AppearanceSupportDecision(AppearanceDecisionKind.NativeComposite, reason, node);

        // Only captured immutable values. No reflection, Pawn/Gene references,
        // content loads, graphics, biological state or saved-choice mutation.
        public static AppearanceSupportDecision Evaluate(AppearanceDescriptor appearance, AppearanceSilhouetteKey silhouette,
            bool nativeSmallStage, string existingNativeReason, AppearanceSupportCatalog catalog, bool allowDiagnosticAssets)
        {
            if (appearance == null || catalog == null) throw new ArgumentNullException();
            if (existingNativeReason != null) return Native(existingNativeReason);
            if (!appearance.ActiveCoat.HasValue)
            {
                if (appearance.NativeFurDefName != null) return Native("native-fur-layer");
                if (appearance.Layers.Any(x => x.Location != AppearanceLayerLocation.Head)) return Native("native-gene-body-layer");
                return new AppearanceSupportDecision(AppearanceDecisionKind.ExistingPresentation);
            }
            // Intentionally native at small rows; request IDs live outside this
            // derived decision and remain saved for return to an RR anchor.
            if (nativeSmallStage) return Native("native-small-stage");
            if (appearance.ApiStatus != AppearanceApiStatus.Available) return Native("unsupported-appearance-api");
            var coat = appearance.ActiveCoat.Value;
            if (coat.FurDefName != appearance.NativeFurDefName) return Native("appearance-coat-mismatch");
            if (!catalog.Coats.Contains(coat)) return Native("unsupported-coat");
            if (appearance.Layers.GroupBy(x => x.Key).Any(x => x.Count() > 1)) return Native("ambiguous-appearance-layer");

            var furNodes = new List<AppearanceNodeKey>();
            var attachmentFamilies = new List<string>();
            foreach (var layer in appearance.Layers)
            {
                if (layer.Location == AppearanceLayerLocation.Head) continue;
                if (layer.Location != AppearanceLayerLocation.Body) return Native("unsupported-body-layer", layer.Key);
                if (!catalog.Layers.TryGetValue(layer.Key, out var registration)) return Native("unsupported-body-layer", layer.Key);
                if (!registration.Structure.Equals(layer.Structure)) return Native("modified-body-layer", layer.Key);
                if (registration.Role == RegisteredAppearanceRole.CoatSurface)
                {
                    if (layer.Key.ProviderPackageId != coat.ProviderPackageId || layer.Key.GeneDefName != coat.GeneDefName)
                        return Native("ambiguous-coat-layer", layer.Key);
                    furNodes.Add(layer.Key);
                }
                else attachmentFamilies.Add(registration.AttachmentFamily);
            }
            if (furNodes.Count != 1) return Native("incomplete-coat-layer");
            var mapping = catalog.FindMapping(coat, silhouette);
            if (mapping == null || !mapping.AssetsAvailable) return Native("missing-coat-art");
            if (mapping.DiagnosticOnly && !allowDiagnosticAssets) return Native("diagnostic-art-unavailable");
            if (mapping.EqualPaletteOnly && (!appearance.Palette.Secondary.HasValue ||
                !appearance.Palette.Primary.Equals(appearance.Palette.Secondary.Value))) return Native("diagnostic-equal-palette-required");
            var fits = new List<AppearanceAttachmentFit>();
            foreach (var family in attachmentFamilies)
            {
                if (!mapping.AttachmentFits.TryGetValue(family, out var fit) ||
                    (!fit.Validated && !(mapping.DiagnosticOnly && allowDiagnosticAssets))) return Native("missing-attachment-fit");
                fits.Add(fit);
            }
            return new AppearanceSupportDecision(AppearanceDecisionKind.PreparedBaseBody, mapping: mapping, suppressed: furNodes, fits: fits);
        }
    }

    public readonly struct AppearanceMaterialPalette
    {
        public readonly AppearanceColor Primary, Secondary;
        public readonly bool UseComplexShader;
        public AppearanceMaterialPalette(AppearancePalette raw, AppearanceColor nodePostFactor, bool rotting,
            Func<AppearanceColor, AppearanceColor> rottenColor, Func<AppearanceColor, AppearanceColor, bool> indistinguishable)
        {
            if (rottenColor == null || indistinguishable == null) throw new ArgumentNullException();
            Primary = raw.Primary.Multiply(nodePostFactor);
            Secondary = raw.Secondary.HasValue ? raw.Secondary.Value.Multiply(nodePostFactor) : AppearanceColor.White;
            if (rotting)
            {
                Primary = rottenColor(Primary);
                // Exact audited null-secondary branch returns white directly.
                if (raw.Secondary.HasValue) Secondary = rottenColor(Secondary);
            }
            UseComplexShader = raw.Secondary.HasValue && !indistinguishable(Primary, Secondary);
        }
    }
}
