using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RimRound.State;
using RimRound.Presentation.Anthrosonae;

namespace RimRound.Presentation
{
    public readonly struct PresentationSelection : IEquatable<PresentationSelection>
    {
        public readonly string Mode, ProfileId, ShapeId;
        public PresentationSelection(string mode, string profileId, string shapeId)
        { Mode = mode; ProfileId = profileId; ShapeId = shapeId; }
        public bool Equals(PresentationSelection other) => Mode == other.Mode &&
            ProfileId == other.ProfileId && ShapeId == other.ShapeId;
        public override bool Equals(object obj) => obj is PresentationSelection other && Equals(other);
        public override int GetHashCode() => ((Mode?.GetHashCode() ?? 0) * 397 ^
            (ProfileId?.GetHashCode() ?? 0)) * 397 ^ (ShapeId?.GetHashCode() ?? 0);
    }

    // Definition metadata only. Capture once on the main thread for a registry
    // revision; share it across pawn contexts. No asset/material readiness cache.
    public readonly struct PresentationAnchorFact
    {
        public readonly bool Exists, IsRimRoundBody;
        public PresentationAnchorFact(bool exists, bool isRimRoundBody)
        { Exists = exists; IsRimRoundBody = isRimRoundBody; }
    }

    public sealed class PresentationDefinitionFacts
    {
        public readonly long RegistryRevision;
        private readonly IReadOnlyDictionary<string, PresentationAnchorFact> anchors;
        public PresentationDefinitionFacts(long revision, IDictionary<string, PresentationAnchorFact> source)
        {
            RegistryRevision = revision;
            anchors = new ReadOnlyDictionary<string, PresentationAnchorFact>(
                new Dictionary<string, PresentationAnchorFact>(source, StringComparer.Ordinal));
        }
        public bool AnchorExists(ResolvedStageEntry entry) => entry.AnchorPolicy != "legacyBodyType" ||
            (entry.LegacyBodyTypeDefName != null && anchors.TryGetValue(entry.LegacyBodyTypeDefName, out var fact) && fact.Exists);
        public bool UsesNativeSmallAnchor(ResolvedStageEntry entry) => entry.AnchorPolicy == "legacyBodyType" &&
            (entry.LegacyBodyTypeDefName == null || !anchors.TryGetValue(entry.LegacyBodyTypeDefName, out var fact) || !fact.IsRimRoundBody);
    }

    // Published on PreparedBodyPresentation beside the actual chosen result.
    // All fields are copied values or immutable registry/descriptor references;
    // there are no Pawn, Gene, mutable Properties, Texture, Graphic or delegates.
    public sealed class PresentationCandidateContext
    {
        public readonly PresentationRegistry Registry;
        public readonly PresentationDefinitionFacts Definitions;
        public readonly AnthrosonaePolicySnapshot Appearance;
        public readonly bool Eligible, Exempt, NativeFurLayer, GeneBodyLayer, TattooLayer, Mutant, Dead, Statue, Dessicated;
        public readonly bool StandardOnly;
        public readonly bool OverrideXenotypeArtwork;
        public readonly string XenotypeArtworkFailure;
        private readonly string racePackage, raceDef, gender, presentationGender, stage, cap, defaultProfile;
        private readonly bool nativeDefault;
        private readonly long stateRevision;
        private readonly IReadOnlyCollection<string> capabilities;
        private readonly IReadOnlyList<string> variants;

        public PresentationCandidateContext(PresentationRegistry registry, PresentationRequest request,
            PresentationDefinitionFacts definitions, AnthrosonaePolicySnapshot appearance, bool standardOnly,
            bool eligible, bool exempt, bool nativeFurLayer, bool geneBodyLayer, bool tattooLayer,
            bool mutant, bool dead, bool statue, bool dessicated,
            bool overrideXenotypeArtwork = false, string xenotypeArtworkFailure = null)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (request == null) throw new ArgumentNullException(nameof(request));
            Definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            if (definitions.RegistryRevision != registry.Revision) throw new ArgumentException("Stale definition facts.");
            racePackage = request.RacePackageId; raceDef = request.RaceDefName;
            gender = request.Gender; presentationGender = request.RequestedPresentationGender;
            stage = request.StageKey; cap = request.VisualCapStageKey; defaultProfile = request.DefaultProfileIdOverride;
            nativeDefault = request.NativeDefault; stateRevision = request.StateRevision;
            capabilities = Array.AsReadOnly((request.AdapterCapabilityIds ?? new string[0]).ToArray());
            variants = Array.AsReadOnly((request.VariantIds ?? new string[0]).ToArray());
            Appearance = appearance; StandardOnly = standardOnly;
            Eligible = eligible; Exempt = exempt; NativeFurLayer = nativeFurLayer; GeneBodyLayer = geneBodyLayer;
            TattooLayer = tattooLayer; Mutant = mutant; Dead = dead; Statue = statue; Dessicated = dessicated;
            OverrideXenotypeArtwork = overrideXenotypeArtwork; XenotypeArtworkFailure = xenotypeArtworkFailure;
        }

        internal PresentationRequest RequestFor(PresentationSelection choice) => new PresentationRequest {
            RacePackageId = racePackage, RaceDefName = raceDef, Gender = gender,
            RequestedPresentationGender = presentationGender, AdapterCapabilityIds = capabilities,
            StageKey = stage, VisualCapStageKey = cap, VariantIds = variants, StateRevision = stateRevision,
            DefaultProfileIdOverride = defaultProfile, NativeDefault = nativeDefault,
            SelectionMode = choice.Mode, RequestedProfileId = choice.ProfileId,
            RequestedShapeId = PresentationSavedState.EffectiveShapeId(choice.ShapeId, StandardOnly)
        };

        public bool CanChooseProfile(string profileId) => PresentationResolver.CanChooseProfile(Registry,
            RequestFor(new PresentationSelection("override", profileId, null)), profileId);
    }

    public sealed class PresentationCandidate
    {
        public readonly PresentationSelection Choice;
        public readonly ResolvedBodyPresentation Resolution;
        public readonly AppearanceSupportDecision CoatDecision;
        public readonly string NativeReason;
        public bool IsNativeNow => NativeReason != null;
        // Predicts capture/registry/policy eligibility only. This is deliberately
        // not named Supported or Ready; actual attachment/material/apparel
        // preparation can still produce a whole-native fallback.
        public bool IsCustomCandidate => NativeReason == null && Resolution.SourceEntry != null;
        public bool DiagnosticOnly => CoatDecision?.Mapping?.DiagnosticOnly == true;
        public string ExplanationCode => NativeReason ?? Resolution.FallbackReason;
        internal PresentationCandidate(PresentationSelection choice, ResolvedBodyPresentation resolution,
            AppearanceSupportDecision coatDecision, string reason)
        { Choice = choice; Resolution = resolution; CoatDecision = coatDecision; NativeReason = reason; }
    }

    public static class PresentationCandidateEvaluator
    {
        // Shared by preparation and menu predictions. Resource/apparel failures
        // are still reported by the actual chosen PreparedBodyPresentation.
        public static PresentationCandidate Evaluate(PresentationCandidateContext context, PresentationSelection choice)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var resolution = PresentationResolver.Resolve(context.Registry, context.RequestFor(choice));
            string reason = resolution.IsNativeFallback ? resolution.FallbackReason : null;
            if (!context.Eligible) reason = "ineligible-body";
            if (context.Exempt) reason = "body-exempt";
            var entry = resolution.SourceEntry;
            AppearanceSupportDecision coatDecision = null;
            if (context.Appearance != null)
            {
                coatDecision = context.Appearance.Resolve(resolution, context.Registry, reason, context.Statue,
                    entry != null && context.Definitions.UsesNativeSmallAnchor(entry));
                if (coatDecision.Kind == AppearanceDecisionKind.NativeComposite) reason = coatDecision.FallbackReason;
            }
            else if (context.OverrideXenotypeArtwork)
            {
                // The opt-in removes only the gene/fur guard. Keep selection,
                // race, provider, biology and condition failures authoritative.
                if (reason == null) reason = context.XenotypeArtworkFailure;
            }
            else
            {
                if (context.NativeFurLayer) reason = "native-fur-layer";
                if (context.GeneBodyLayer) reason = "native-gene-body-layer";
            }
            if (context.TattooLayer) reason = "native-tattoo-layer";
            if (context.Mutant) reason = "native-mutant-body";
            if (context.Dessicated)
            {
                if (entry?.DessicationPolicy != "body") reason = "native-desiccation";
            }
            else if (context.Dead && entry?.CorpsePolicy == "native") reason = "native-corpse";
            if (reason == null && entry != null && !context.Definitions.AnchorExists(entry)) reason = "missing-anchor-definition";
            return new PresentationCandidate(choice, resolution, coatDecision, reason);
        }

        // Save eligibility is independent of what can be drawn at the current
        // stage. This allows a known future-stage choice and preserves raw IDs.
        // Unknown *existing* IDs are retained by the save/UI; this method only
        // controls newly issued selections from the installed metadata.
        public static bool IsDeclaredSelection(PresentationCandidateContext context, PresentationSelection choice)
        {
            if (PresentationSavedState.NormalMode(choice.Mode) == null) return false;
            if (choice.Mode != "override") return true;
            return context != null && context.CanChooseProfile(choice.ProfileId) &&
                context.Registry.Profiles.TryGetValue(choice.ProfileId ?? "", out var profile) &&
                (choice.ShapeId == null || profile.ShapeIds.Contains(choice.ShapeId));
        }

        public static PresentationProfile ShapeProfile(PresentationCandidateContext context,
            PresentationRegistry registry, PresentationSelection saved)
        {
            if (saved.Mode != "default" && saved.Mode != "override") return null;
            if (context == null) return null;
            string id = saved.ProfileId;
            if (saved.Mode == "default")
                id = Evaluate(context, saved).Resolution.SelectedBaseProfileId;
            else if (!context.CanChooseProfile(id)) return null;
            return id != null && registry.Profiles.TryGetValue(id, out var profile) ? profile : null;
        }
    }
}
