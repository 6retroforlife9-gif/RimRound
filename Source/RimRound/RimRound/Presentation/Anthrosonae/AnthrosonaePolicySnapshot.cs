using System;

namespace RimRound.Presentation.Anthrosonae
{
    public sealed class AnthrosonaePolicySnapshot
    {
        public readonly AppearanceDescriptor Descriptor;
        public readonly string FailureReason;
        public readonly AppearanceSupportCatalog Catalog;
        public AnthrosonaePolicySnapshot(AppearanceDescriptor descriptor, string failureReason, AppearanceSupportCatalog catalog)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            FailureReason = failureReason; Catalog = catalog;
        }
        private static AppearanceSupportDecision Native(string reason) =>
            new AppearanceSupportDecision(AppearanceDecisionKind.NativeComposite, reason);

        // No optional assembly lookup, Bridge.Initialize, live Gene/Props,
        // definition database, content, textures or Unity calls.
        public AppearanceSupportDecision Resolve(ResolvedBodyPresentation resolution, PresentationRegistry registry,
            string existingReason, bool statue, bool nativeSmallStage)
        {
            var entry = resolution?.SourceEntry;
            if (existingReason != null) return Native(existingReason);
            if (entry == null) return Native("missing-coat-art");
            if (nativeSmallStage) return Native("native-small-stage");
            if (FailureReason != null) return Native(FailureReason);
            if (statue) return Native("anthrosonae-pilot-statue-unavailable");
            if (Catalog == null) return Native("unsupported-appearance-api");
            if (!AnthrosonaeSilhouetteRegistry.TryMatch(resolution, registry, out var token, out var failure))
                return Native("modified-appearance-silhouette");
            string root = AnthrosonaeSilhouetteRegistry.ResolveRoot(token);
            return AppearancePolicy.Evaluate(Descriptor,
                new AppearanceSilhouetteKey("rimround.physical-root", root, "native-coat-v1"), false, null, Catalog, false);
        }
    }
}
