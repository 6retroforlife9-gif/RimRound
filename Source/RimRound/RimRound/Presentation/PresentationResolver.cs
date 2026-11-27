using System;
using System.Collections.Generic;
using System.Linq;

namespace RimRound.Presentation
{
    public sealed class ResolvedBodyPresentation
    {
        public readonly string RequestedProfileId, RequestedShapeId, EffectiveProfileId, EffectiveShapeId;
        public readonly string SelectedBaseProfileId;
        public readonly string RequestedStageKey, EffectiveStageKey, EffectivePresentationGender;
        public readonly ResolvedStageEntry SourceEntry;
        public readonly bool IsNativeFallback;
        public readonly string FallbackReason;
        public readonly long RegistryRevision, StateRevision;

        internal ResolvedBodyPresentation(PresentationRequest request, PresentationRegistry registry,
            string stage, string gender, PresentationProfile profile, string shape, ResolvedStageEntry entry, string reason,
            string selectedBaseProfileId = null)
        {
            RequestedProfileId = request.RequestedProfileId; RequestedShapeId = request.RequestedShapeId;
            RequestedStageKey = request.StageKey; EffectiveStageKey = stage; EffectivePresentationGender = gender;
            EffectiveProfileId = profile == null ? null : profile.ProfileId;
            SelectedBaseProfileId = selectedBaseProfileId ?? profile?.ProfileId;
            EffectiveShapeId = shape; SourceEntry = entry;
            IsNativeFallback = profile == null; FallbackReason = reason;
            RegistryRevision = registry.Revision; StateRevision = request.StateRevision;
        }
    }

    public static class PresentationResolver
    {
        public static ResolvedBodyPresentation Resolve(PresentationRegistry registry, PresentationRequest request)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (request == null) throw new ArgumentNullException(nameof(request));
            string stage = request.StageKey;
            int stageIndex = Index(stage);
            if (stageIndex < 0) return new ResolvedBodyPresentation(request, registry, stage, null, null, null, null, "unknown-stage");
            if (!string.IsNullOrEmpty(request.VisualCapStageKey))
            {
                int cap = Index(request.VisualCapStageKey);
                if (cap < 0) return new ResolvedBodyPresentation(request, registry, stage, null, null, null, null, "unknown-visual-cap");
                if (stageIndex > cap) stage = request.VisualCapStageKey;
            }
            RacePresentation race;
            if (!registry.Races.TryGetValue(PresentationRegistry.RaceKey(request.RacePackageId, request.RaceDefName), out race))
                return new ResolvedBodyPresentation(request, registry, stage, NormalGender(request.Gender), null, null, null, "unknown-race");
            if (!HasRaceCapabilities(race, request))
                return new ResolvedBodyPresentation(request, registry, stage, NormalGender(request.Gender), null, null, null, "unsupported-adapter");
            if (request.SelectionMode == "native")
                return new ResolvedBodyPresentation(request, registry, stage, NormalGender(request.Gender), null, null, null, "selected-native");
            if (request.SelectionMode != "default" && request.SelectionMode != "override")
                return new ResolvedBodyPresentation(request, registry, stage, NormalGender(request.Gender), null, null, null, "unknown-mode");

            string gender = NormalGender(request.RequestedPresentationGender ?? request.Gender);
            if (gender == null) return new ResolvedBodyPresentation(request, registry, stage, null, null, null, null, "unknown-gender");
            PresentationProfile profile;
            string shape;
            ResolvedStageEntry entry;
            if (request.SelectionMode == "override" && TrySelect(registry, race, request.RequestedProfileId, request.RequestedShapeId, gender, stage, out profile, out shape, out entry))
                return ResolveSelected(request, registry, race, stage, gender, profile, shape, entry, null);
            if (request.NativeDefault)
                return new ResolvedBodyPresentation(request, registry, stage, gender, null, null, null, "native-global-default");
            bool unavailableGlobalDefault = request.DefaultProfileIdOverride != null;
            if (unavailableGlobalDefault)
            {
                if (request.SelectionMode == "default" && request.RequestedShapeId != null &&
                    TrySelect(registry, race, request.DefaultProfileIdOverride, request.RequestedShapeId, gender, stage,
                        out profile, out shape, out entry))
                    return ResolveSelected(request, registry, race, stage, gender, profile, shape, entry, null);
                if (TrySelect(registry, race, request.DefaultProfileIdOverride, null, gender, stage, out profile, out shape, out entry))
                    return ResolveSelected(request, registry, race, stage, gender, profile, shape, entry,
                        request.SelectionMode == "override" ? "requested-unavailable" :
                        request.RequestedShapeId != null ? "requested-shape-unavailable" : null);
            }
            string defaultId = gender == "male" ? race.DefaultMaleProfileId : gender == "female" ? race.DefaultFemaleProfileId : race.DefaultNoneProfileId;
            if (request.SelectionMode == "default" && request.RequestedShapeId != null &&
                TrySelect(registry, race, defaultId, request.RequestedShapeId, gender, stage, out profile, out shape, out entry))
                return ResolveSelected(request, registry, race, stage, gender, profile, shape, entry,
                    unavailableGlobalDefault ? "global-default-unavailable" : null);
            if (TrySelect(registry, race, defaultId, null, gender, stage, out profile, out shape, out entry))
                return ResolveSelected(request, registry, race, stage, gender, profile, shape, entry, unavailableGlobalDefault ? "global-default-unavailable" : request.SelectionMode == "override" ? "requested-unavailable" :
                    request.RequestedShapeId != null ? "requested-shape-unavailable" : null);
            if (TrySelect(registry, race, race.FallbackProfileId, null, gender, stage, out profile, out shape, out entry))
                return ResolveSelected(request, registry, race, stage, gender, profile, shape, entry, unavailableGlobalDefault ? "global-default-unavailable" : request.SelectionMode == "override" ? "requested-unavailable" : "default-unavailable");
            if (TryFallbackChain(registry, race, race.FallbackProfileId, gender, stage, out profile, out shape, out entry))
                return ResolveSelected(request, registry, race, stage, gender, profile, shape, entry, unavailableGlobalDefault ? "global-default-unavailable" : "race-profile-fallback");
            if (request.SelectionMode == "override" && TryFallbackChain(registry, race, request.RequestedProfileId, gender, stage, out profile, out shape, out entry))
                return ResolveSelected(request, registry, race, stage, gender, profile, shape, entry, unavailableGlobalDefault ? "global-default-unavailable" : "requested-profile-fallback");
            if (TryFallbackChain(registry, race, defaultId, gender, stage, out profile, out shape, out entry))
                return ResolveSelected(request, registry, race, stage, gender, profile, shape, entry, unavailableGlobalDefault ? "global-default-unavailable" : "default-profile-fallback");
            return new ResolvedBodyPresentation(request, registry, stage, gender, null, null, null,
                unavailableGlobalDefault ? "global-default-unavailable" : request.SelectionMode == "override" ? "requested-unavailable" : "no-compatible-profile");
        }

        private static ResolvedBodyPresentation ResolveSelected(PresentationRequest request, PresentationRegistry registry,
            RacePresentation race, string stage, string gender, PresentationProfile baseProfile, string shape,
            ResolvedStageEntry baseEntry, string reason)
        {
            // Variant declarations belong only to the compatible base. A variant target
            // cannot change the base's selected shape or initiate another selection chain.
            foreach (string variantId in request.VariantIds ?? new string[0])
            {
                string targetId;
                if (variantId == null || !baseProfile.Variants.TryGetValue(variantId, out targetId)) continue;
                PresentationProfile target;
                string targetShape;
                ResolvedStageEntry targetEntry;
                if (TrySelect(registry, race, targetId, shape, gender, stage, out target, out targetShape, out targetEntry))
                    return new ResolvedBodyPresentation(request, registry, stage, gender, target, targetShape, targetEntry, reason,
                        baseProfile.ProfileId);
            }
            return new ResolvedBodyPresentation(request, registry, stage, gender, baseProfile, shape, baseEntry, reason);
        }

        private static bool TryFallbackChain(PresentationRegistry registry, RacePresentation race, string start,
            string gender, string stage, out PresentationProfile profile, out string shape, out ResolvedStageEntry entry)
        {
            profile = null; shape = null; entry = null;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = start;
            while (!string.IsNullOrEmpty(current) && visited.Add(current))
            {
                PresentationProfile from;
                if (!registry.Profiles.TryGetValue(current, out from)) return false;
                current = from.FallbackProfileId;
                if (TrySelect(registry, race, current, null, gender, stage, out profile, out shape, out entry)) return true;
            }
            return false;
        }

        private static bool TrySelect(PresentationRegistry registry, RacePresentation race, string id, string requestedShape,
            string gender, string stage, out PresentationProfile profile, out string shape, out ResolvedStageEntry entry)
        {
            profile = null; shape = null; entry = null;
            if (string.IsNullOrEmpty(id) || !registry.Profiles.TryGetValue(id, out profile)) return false;
            if (!IsCompatibleProfile(race, profile, gender)) return false;
            shape = requestedShape ?? profile.DefaultShapeId;
            if (!profile.ShapeIds.Contains(shape)) return false;
            return profile.TryGetEntry(shape, stage, out entry);
        }

        // Declaration compatibility does not depend on current stage, shape,
        // coat, palette or visual cap: a future-stage choice remains selectable.
        public static bool CanChooseProfile(PresentationRegistry registry, PresentationRequest request, string profileId)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!registry.Races.TryGetValue(PresentationRegistry.RaceKey(request.RacePackageId, request.RaceDefName), out var race) ||
                !HasRaceCapabilities(race, request) || string.IsNullOrEmpty(profileId) ||
                !registry.Profiles.TryGetValue(profileId, out var profile) || !profile.Selectable) return false;
            string gender = NormalGender(request.RequestedPresentationGender ?? request.Gender);
            return gender != null && IsCompatibleProfile(race, profile, gender);
        }

        private static bool HasRaceCapabilities(RacePresentation race, PresentationRequest request)
        {
            var caps = new HashSet<string>(request.AdapterCapabilityIds ?? new string[0], StringComparer.Ordinal);
            return caps.Contains(race.AdapterId) && race.RequiredCapabilityIds.All(c => caps.Contains(c));
        }

        private static bool IsCompatibleProfile(RacePresentation race, PresentationProfile profile, string gender)
        {
            if (!profile.SupportedAdapterIds.Contains(race.AdapterId) || !profile.PresentationGenders.Contains(gender)) return false;
            string family;
            switch (profile.ProfileId)
            {
                case "rimround.bamboo.ratkin": case "rimround.bamboo.ratkin.lardyalt": family = "ratkinset"; break;
                case "rimround.bamboo.anty": case "rimround.bamboo.anty.lardyalt": family = "antyset"; break;
                default: return true;
            }
            // These shipped race-only sets declare the general human adapter.
            // Their existing race default family supplies the missing scope.
            string raceDefault = gender == "male" ? race.DefaultMaleProfileId :
                gender == "female" ? race.DefaultFemaleProfileId : race.DefaultNoneProfileId;
            return raceDefault == "rimround.compat." + family + "." + gender;
        }

        private static int Index(string key)
        {
            for (int i=0; i<PresentationRegistry.CanonicalStageKeys.Count; i++)
                if (PresentationRegistry.CanonicalStageKeys[i] == key) return i;
            return -1;
        }
        private static string NormalGender(string gender)
        {
            if (string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase)) return "male";
            if (string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase)) return "female";
            if (string.Equals(gender, "None", StringComparison.OrdinalIgnoreCase)) return "none";
            return null;
        }
    }
}
