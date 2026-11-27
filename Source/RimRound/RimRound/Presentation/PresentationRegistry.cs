using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RimRound.Presentation
{
    public sealed class ResolvedStageEntry
    {
        public readonly string ProfileId, ShapeId, StageKey, SourceProfileId, SourceShapeId, SourceStageKey;
        public readonly string BodyTextureRoot, DirectionPolicy, ShaderPolicy, ColorPolicy, MaskTextureRoot, MaskPolicy;
        public readonly float MapScale, PortraitScale, PortraitZoom, PortraitPanX, PortraitPanY, PortraitPanStyleY;
        public readonly bool ForceFittedPortrait;
        public readonly string DownedPosePolicy;
        public readonly string AnchorPolicy, LegacyBodyTypeDefName;
        public readonly float HeadOffsetX, HeadOffsetY, NorthOffsetX, NorthOffsetY, BedOffsetX, BedOffsetY;
        public readonly float WoundOffsetX, WoundOffsetY, EyeOffsetX, EyeOffsetY;
        public readonly string ApparelSuffix, ApparelFallback, CorpsePolicy, DessicationPolicy;
        public readonly float WiggleSpeed;

        internal ResolvedStageEntry(string profile, string shape, string stage, ResolvedStageEntry source)
        {
            ProfileId = profile; ShapeId = shape; StageKey = stage;
            SourceProfileId = source.SourceProfileId; SourceShapeId = source.SourceShapeId; SourceStageKey = source.SourceStageKey;
            BodyTextureRoot = source.BodyTextureRoot; DirectionPolicy = source.DirectionPolicy;
            ShaderPolicy = source.ShaderPolicy; ColorPolicy = source.ColorPolicy; MaskTextureRoot = source.MaskTextureRoot; MaskPolicy = source.MaskPolicy;
            MapScale = source.MapScale; PortraitScale = source.PortraitScale; PortraitZoom = source.PortraitZoom;
            PortraitPanX = source.PortraitPanX; PortraitPanY = source.PortraitPanY;
            PortraitPanStyleY = source.PortraitPanStyleY; ForceFittedPortrait = source.ForceFittedPortrait;
            DownedPosePolicy = source.DownedPosePolicy;
            AnchorPolicy = source.AnchorPolicy; LegacyBodyTypeDefName = source.LegacyBodyTypeDefName;
            HeadOffsetX = source.HeadOffsetX; HeadOffsetY = source.HeadOffsetY;
            NorthOffsetX = source.NorthOffsetX; NorthOffsetY = source.NorthOffsetY;
            BedOffsetX = source.BedOffsetX; BedOffsetY = source.BedOffsetY;
            WoundOffsetX = source.WoundOffsetX; WoundOffsetY = source.WoundOffsetY;
            EyeOffsetX = source.EyeOffsetX; EyeOffsetY = source.EyeOffsetY;
            ApparelSuffix = source.ApparelSuffix; ApparelFallback = source.ApparelFallback;
            CorpsePolicy = source.CorpsePolicy; DessicationPolicy = source.DessicationPolicy;
            WiggleSpeed = source.WiggleSpeed;
        }

        internal ResolvedStageEntry(string profile, string shape, string stage, PresentationStageData input)
        {
            ProfileId = SourceProfileId = profile; ShapeId = SourceShapeId = shape; StageKey = SourceStageKey = stage;
            BodyTextureRoot = input.BodyTextureRoot; DirectionPolicy = input.DirectionPolicy;
            ShaderPolicy = input.ShaderPolicy; ColorPolicy = input.ColorPolicy; MaskTextureRoot = input.MaskTextureRoot; MaskPolicy = input.MaskPolicy;
            MapScale = input.MapScale; PortraitScale = input.PortraitScale; PortraitZoom = input.PortraitZoom;
            PortraitPanX = input.PortraitPanX; PortraitPanY = input.PortraitPanY;
            PortraitPanStyleY = input.PortraitPanStyleY; ForceFittedPortrait = input.ForceFittedPortrait;
            DownedPosePolicy = input.DownedPosePolicy;
            AnchorPolicy = input.AnchorPolicy; LegacyBodyTypeDefName = input.LegacyBodyTypeDefName;
            HeadOffsetX = input.HeadOffsetX; HeadOffsetY = input.HeadOffsetY;
            NorthOffsetX = input.NorthOffsetX; NorthOffsetY = input.NorthOffsetY;
            BedOffsetX = input.BedOffsetX; BedOffsetY = input.BedOffsetY;
            WoundOffsetX = input.WoundOffsetX; WoundOffsetY = input.WoundOffsetY;
            EyeOffsetX = input.EyeOffsetX; EyeOffsetY = input.EyeOffsetY;
            ApparelSuffix = input.ApparelSuffix; ApparelFallback = input.ApparelFallback;
            CorpsePolicy = input.CorpsePolicy; DessicationPolicy = input.DessicationPolicy;
            WiggleSpeed = input.WiggleSpeed;
        }
    }

    public sealed class PresentationProfile
    {
        public readonly string ProviderPackageId, DefName, ProfileId, ArtSetId, Label, Description, DefaultShapeId, FallbackProfileId;
        public readonly IReadOnlyList<string> ShapeIds, SupportedAdapterIds, PresentationGenders, RequiredPackageIds, Credits;
        public readonly IReadOnlyDictionary<string, ResolvedStageEntry> Entries;
        public readonly bool Selectable;
        public readonly IReadOnlyDictionary<string, string> Variants;

        internal PresentationProfile(PresentationProfileData data, IDictionary<string, ResolvedStageEntry> entries)
        {
            ProviderPackageId = data.ProviderPackageId; DefName = data.DefName; ProfileId = data.ProfileId;
            ArtSetId = data.ArtSetId; Label = data.Label; Description = data.Description;
            DefaultShapeId = data.DefaultShapeId; FallbackProfileId = data.FallbackProfileId;
            Selectable = data.Selectable;
            Variants = new ReadOnlyDictionary<string, string>(data.Variants.ToDictionary(v => v.Id, v => v.ProfileId, StringComparer.Ordinal));
            ShapeIds = Frozen(data.ShapeIds); SupportedAdapterIds = Frozen(data.SupportedAdapterIds);
            PresentationGenders = Frozen(data.PresentationGenders); RequiredPackageIds = Frozen(data.RequiredPackageIds);
            Credits = Frozen(data.Credits);
            Entries = new ReadOnlyDictionary<string, ResolvedStageEntry>(new Dictionary<string, ResolvedStageEntry>(entries, StringComparer.Ordinal));
        }

        internal static IReadOnlyList<string> Frozen(IEnumerable<string> values) { return Array.AsReadOnly(values.ToArray()); }
        public bool TryGetEntry(string shape, string stage, out ResolvedStageEntry entry)
        {
            return Entries.TryGetValue(shape + "/" + stage, out entry);
        }
    }

    public sealed class RacePresentation
    {
        public readonly string ProviderPackageId, RacePackageId, RaceDefName, AdapterId;
        public readonly int Priority;
        public readonly string DefaultMaleProfileId, DefaultFemaleProfileId, DefaultNoneProfileId, FallbackProfileId;
        public readonly IReadOnlyList<string> RequiredCapabilityIds;
        internal RacePresentation(RacePresentationData d)
        {
            ProviderPackageId = d.ProviderPackageId; RacePackageId = d.RacePackageId; RaceDefName = d.RaceDefName;
            AdapterId = d.AdapterId; Priority = d.Priority;
            DefaultMaleProfileId = d.DefaultMaleProfileId; DefaultFemaleProfileId = d.DefaultFemaleProfileId;
            DefaultNoneProfileId = d.DefaultNoneProfileId; FallbackProfileId = d.FallbackProfileId;
            RequiredCapabilityIds = PresentationProfile.Frozen(d.RequiredCapabilityIds);
        }
    }

    public sealed class PresentationRegistry
    {
        private static readonly string[] StageNames = MakeStages();
        private readonly IReadOnlyDictionary<string, PresentationProfile> profiles;
        private readonly IReadOnlyDictionary<string, RacePresentation> races;
        public readonly long Revision;
        public readonly IReadOnlyList<PresentationDiagnostic> Diagnostics;
        public IReadOnlyDictionary<string, PresentationProfile> Profiles { get { return profiles; } }
        public IReadOnlyDictionary<string, RacePresentation> Races { get { return races; } }
        public static IReadOnlyList<string> CanonicalStageKeys { get; } = Array.AsReadOnly(StageNames);

        private PresentationRegistry(long revision, IDictionary<string, PresentationProfile> profiles, IDictionary<string, RacePresentation> races, List<PresentationDiagnostic> diagnostics)
        {
            Revision = revision;
            this.profiles = new ReadOnlyDictionary<string, PresentationProfile>(new Dictionary<string, PresentationProfile>(profiles, StringComparer.Ordinal));
            this.races = new ReadOnlyDictionary<string, RacePresentation>(new Dictionary<string, RacePresentation>(races, StringComparer.Ordinal));
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        }

        public static PresentationRegistry Build(IEnumerable<PresentationProfileData> profileInputs,
            IEnumerable<RacePresentationData> raceInputs, IEnumerable<string> activePackages,
            Func<string, bool> assetExists, long revision)
        {
            if (assetExists == null) throw new ArgumentNullException(nameof(assetExists));
            var active = new HashSet<string>(activePackages ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var diags = new List<PresentationDiagnostic>();
            var raw = new Dictionary<string, PresentationProfileData>(StringComparer.Ordinal);
            var supplied = (profileInputs ?? new PresentationProfileData[0]).Where(p => p != null).ToList();
            foreach (var absent in supplied.Where(p => !active.Contains(p.ProviderPackageId ?? "")))
                diags.Add(D("missing-dependency", absent.ProfileId, absent.ProviderPackageId));
            var candidates = supplied.Where(p => active.Contains(p.ProviderPackageId ?? "")).ToList();
            foreach (var group in candidates.GroupBy(p => p.ProfileId ?? "", StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var ordered = group.OrderBy(candidate => candidate.ProviderPackageId ?? "", StringComparer.Ordinal).ThenBy(candidate => candidate.DefName ?? "", StringComparer.Ordinal).ToList();
                if (ordered.Count > 1) { diags.Add(new PresentationDiagnostic("duplicate-profile", group.Key, "Conflicting profile disabled", ordered.Select(candidate => candidate.ProviderPackageId ?? ""))); continue; }
                var p = Copy(ordered[0]);
                if (p.Schema != 1 || !ValidId(p.ProfileId) || Blank(p.ProviderPackageId) || Blank(p.DefName) || Blank(p.ArtSetId) || Blank(p.DefaultShapeId))
                { diags.Add(D("malformed-profile", group.Key, p.ProviderPackageId)); continue; }
                if (!active.Contains(p.ProviderPackageId) || p.RequiredPackageIds.Any(x => !active.Contains(x)))
                { diags.Add(D("missing-dependency", p.ProfileId, p.ProviderPackageId)); continue; }
                if (p.ShapeIds.Count == 0 || p.ShapeIds.Any(Blank) || p.ShapeIds.Distinct(StringComparer.Ordinal).Count() != p.ShapeIds.Count || !p.ShapeIds.Contains(p.DefaultShapeId) ||
                    p.SupportedAdapterIds.Count == 0 || p.SupportedAdapterIds.Any(Blank) || p.RequiredPackageIds.Any(Blank) ||
                    p.PresentationGenders.Count == 0 || p.PresentationGenders.Any(g => g != "male" && g != "female" && g != "none") ||
                    p.Stages.Any(s => s == null) || p.Variants.Any(v => v == null || Blank(v.Id) || Blank(v.ProfileId)) ||
                    p.Variants.Select(v => v.Id).Distinct(StringComparer.Ordinal).Count() != p.Variants.Count)
                { diags.Add(D("malformed-profile", p.ProfileId, p.ProviderPackageId)); continue; }
                raw[p.ProfileId] = p;
            }

            var resolved = new Dictionary<string, ResolvedStageEntry>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var invalid = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in raw.Values.OrderBy(p => p.ProfileId, StringComparer.Ordinal))
            {
                if (p.Stages.Any(s => !p.ShapeIds.Contains(s.ShapeId) || !StageNames.Contains(s.StageKey)))
                { invalid.Add(p.ProfileId); diags.Add(D("undeclared-stage", p.ProfileId, p.ProviderPackageId)); }
            }
            foreach (var p in raw.Values.OrderBy(p => p.ProfileId, StringComparer.Ordinal))
            foreach (var shape in p.ShapeIds)
            foreach (var stage in StageNames)
                ResolveEntry(p.ProfileId, shape, stage, raw, resolved, visiting, invalid, assetExists, diags);

            // A fully resolved entry may still point to a profile subsequently quarantined.
            bool invalidChanged;
            do { invalidChanged = false; foreach (var p in raw.Values)
                if (p.Stages.Any(s => !Blank(s.ReuseProfileId) && invalid.Contains(s.ReuseProfileId)) && invalid.Add(p.ProfileId)) invalidChanged = true;
            } while (invalidChanged);

            var profiles = new Dictionary<string, PresentationProfile>(StringComparer.Ordinal);
            foreach (var p in raw.Values)
            {
                if (invalid.Contains(p.ProfileId)) continue;
                if (!Blank(p.FallbackProfileId) && !raw.ContainsKey(p.FallbackProfileId))
                { diags.Add(D("missing-fallback", p.ProfileId, p.ProviderPackageId)); continue; }
                var entries = new Dictionary<string, ResolvedStageEntry>(StringComparer.Ordinal);
                foreach (var shape in p.ShapeIds)
                foreach (var stage in StageNames)
                    entries[shape + "/" + stage] = resolved[p.ProfileId + "/" + shape + "/" + stage];
                profiles[p.ProfileId] = new PresentationProfile(p, entries);
            }
            // A fallback to an invalid profile also invalidates its owner.
            bool changed;
            do { changed = false; foreach (var p in profiles.Values.ToArray())
                if (!Blank(p.FallbackProfileId) && !profiles.ContainsKey(p.FallbackProfileId))
                { profiles.Remove(p.ProfileId); diags.Add(D("missing-fallback", p.ProfileId, p.ProviderPackageId)); changed = true; }
            } while (changed);

            // Fallback links are selection links too; reject cycles before publication.
            foreach (var p in profiles.Values.ToArray())
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var current = p;
                while (current != null && !Blank(current.FallbackProfileId))
                {
                    if (!seen.Add(current.ProfileId)) { foreach (var id in seen) profiles.Remove(id); diags.Add(D("fallback-cycle", p.ProfileId, p.ProviderPackageId)); break; }
                    profiles.TryGetValue(current.FallbackProfileId, out current);
                }
            }
            // Publish only profiles whose entire declared fallback and reuse graph survived.
            do
            {
                changed = false;
                foreach (var p in profiles.Values.ToArray())
                {
                    PresentationProfileData source = raw[p.ProfileId];
                    bool brokenFallback = !Blank(p.FallbackProfileId) && !profiles.ContainsKey(p.FallbackProfileId);
                    bool brokenReuse = source.Stages.Any(s => !Blank(s.ReuseProfileId) && !profiles.ContainsKey(s.ReuseProfileId));
                    if (!brokenFallback && !brokenReuse) continue;
                    profiles.Remove(p.ProfileId);
                    diags.Add(D(brokenFallback ? "missing-fallback" : "invalid-reuse-profile", p.ProfileId, p.ProviderPackageId));
                    changed = true;
                }
            } while (changed);
            var races = new Dictionary<string, RacePresentation>(StringComparer.Ordinal);
            foreach (var group in (raceInputs ?? new RacePresentationData[0]).Where(r => r != null && active.Contains(r.ProviderPackageId ?? ""))
                .GroupBy(r => RaceKey(r.RacePackageId, r.RaceDefName), StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var ordered = group.OrderByDescending(r => r.Priority).ThenBy(r => r.ProviderPackageId ?? "", StringComparer.Ordinal).ToList();
                if (ordered.Count > 1) diags.Add(new PresentationDiagnostic("duplicate-race", group.Key, "Unique highest priority wins; tied highest disabled", ordered.Select(r => r.ProviderPackageId ?? "")));
                if (ordered.Count > 1 && ordered[0].Priority == ordered[1].Priority) continue;
                var d = Copy(ordered[0]);
                if (Blank(d.ProviderPackageId) || Blank(d.RacePackageId) || Blank(d.RaceDefName) || Blank(d.AdapterId) || !active.Contains(d.ProviderPackageId) || !active.Contains(d.RacePackageId) || d.RequiredCapabilityIds.Any(Blank))
                { diags.Add(D("malformed-race", group.Key, d.ProviderPackageId)); continue; }
                races[group.Key] = new RacePresentation(d);
            }
            return new PresentationRegistry(revision, profiles, races, diags.Take(256).ToList());
        }

        private static ResolvedStageEntry ResolveEntry(string profile, string shape, string stage,
            IDictionary<string, PresentationProfileData> raw, IDictionary<string, ResolvedStageEntry> done,
            ISet<string> visiting, ISet<string> invalid, Func<string, bool> assetExists, IList<PresentationDiagnostic> diags)
        {
            string key = profile + "/" + shape + "/" + stage;
            ResolvedStageEntry answer;
            if (done.TryGetValue(key, out answer)) return answer;
            if (visiting.Count >= 128 || !visiting.Add(key)) { invalid.Add(profile); diags.Add(D("reuse-cycle-or-depth", key, profile)); return null; }
            PresentationProfileData p;
            if (!raw.TryGetValue(profile, out p) || !p.ShapeIds.Contains(shape) || !StageNames.Contains(stage))
            { invalid.Add(profile); diags.Add(D("missing-reuse", key, profile)); visiting.Remove(key); return null; }
            var matching = p.Stages.Where(candidate => candidate.ShapeId == shape && candidate.StageKey == stage).ToList();
            if (matching.Count != 1)
            { invalid.Add(profile); diags.Add(D(matching.Count == 0 ? "missing-stage" : "duplicate-stage", key, p.ProviderPackageId)); visiting.Remove(key); return null; }
            var s = matching[0];
            if (!Blank(s.ReuseProfileId) || !Blank(s.ReuseShapeId) || !Blank(s.ReuseStageKey))
            {
                if (Blank(s.ReuseProfileId) || Blank(s.ReuseShapeId) || Blank(s.ReuseStageKey) || !Blank(s.BodyTextureRoot))
                { invalid.Add(profile); diags.Add(D("malformed-reuse", key, p.ProviderPackageId)); }
                else
                {
                    var source = ResolveEntry(s.ReuseProfileId, s.ReuseShapeId, s.ReuseStageKey, raw, done, visiting, invalid, assetExists, diags);
                    if (source != null) answer = new ResolvedStageEntry(profile, shape, stage, source);
                    else invalid.Add(profile);
                }
            }
            else if (ValidStage(s, assetExists)) answer = new ResolvedStageEntry(profile, shape, stage, s);
            else { invalid.Add(profile); diags.Add(D("malformed-stage-or-asset", key, p.ProviderPackageId)); }
            visiting.Remove(key);
            if (answer != null) done[key] = answer;
            return answer;
        }

        private static bool ValidStage(PresentationStageData s, Func<string, bool> assetExists)
        {
            if (!ValidTexturePath(s.BodyTextureRoot) || (s.DirectionPolicy != "mirrorWest" && s.DirectionPolicy != "fourDirections") ||
                (s.DownedPosePolicy != "native" && s.DownedPosePolicy != "frontStatic") ||
                (s.ShaderPolicy != "cutout" && s.ShaderPolicy != "masked") ||
                (s.ColorPolicy != "skin" && s.ColorPolicy != "white") ||
                (s.MaskPolicy != "none" && s.MaskPolicy != "bodySuffix" && s.MaskPolicy != "separate") ||
                (s.ShaderPolicy == "masked" && s.MaskPolicy == "none") ||
                (s.ShaderPolicy != "masked" && s.MaskPolicy != "none") ||
                (s.MaskPolicy == "separate" && !ValidTexturePath(s.MaskTextureRoot)) ||
                (s.MaskPolicy != "separate" && !Blank(s.MaskTextureRoot)) ||
                (s.AnchorPolicy != "native" && s.AnchorPolicy != "legacyBodyType" && s.AnchorPolicy != "explicit") ||
                (s.AnchorPolicy == "legacyBodyType" && Blank(s.LegacyBodyTypeDefName)) ||
                (s.ApparelFallback != "requested" && s.ApparelFallback != "default" && s.ApparelFallback != "native") ||
                (s.CorpsePolicy != "native" && s.CorpsePolicy != "body") ||
                (s.DessicationPolicy != "native" && s.DessicationPolicy != "body")) return false;
            var values = new[] { s.MapScale, s.PortraitScale, s.PortraitZoom, s.PortraitPanX, s.PortraitPanY, s.PortraitPanStyleY,
                s.HeadOffsetX, s.HeadOffsetY, s.NorthOffsetX, s.NorthOffsetY, s.BedOffsetX, s.BedOffsetY,
                s.WoundOffsetX, s.WoundOffsetY, s.EyeOffsetX, s.EyeOffsetY, s.WiggleSpeed };
            if (values.Any(v => float.IsNaN(v) || float.IsInfinity(v))) return false;
            if (s.MapScale <= 0 || s.MapScale > 256 || s.PortraitScale <= 0 || s.PortraitScale > 256 || s.PortraitZoom <= 0 || s.PortraitZoom > 16 || s.WiggleSpeed < 0 || s.WiggleSpeed > 256) return false;
            if (values.Skip(3).Take(13).Any(v => Math.Abs(v) > 256)) return false;
            foreach (var direction in s.DirectionPolicy == "fourDirections" ? new[] { "south", "north", "east", "west" } : new[] { "south", "north", "east" })
            {
                if (!assetExists(s.BodyTextureRoot + "_" + direction)) return false;
                if (s.MaskPolicy == "bodySuffix" && !assetExists(s.BodyTextureRoot + "_" + direction + "m")) return false;
                if (s.MaskPolicy == "separate" && !assetExists(s.MaskTextureRoot + "_" + direction)) return false;
            }
            return true;
        }

        private static PresentationProfileData Copy(PresentationProfileData p) { return new PresentationProfileData {
            Schema=p.Schema, ProviderPackageId=p.ProviderPackageId, DefName=p.DefName, ProfileId=p.ProfileId, ArtSetId=p.ArtSetId,
            Label=p.Label, Description=p.Description, DefaultShapeId=p.DefaultShapeId, FallbackProfileId=p.FallbackProfileId,
            Selectable=p.Selectable, Variants=(p.Variants ?? new List<PresentationVariantData>()).Select(v => v == null ? null : v.Copy()).ToList(),
            ShapeIds=new List<string>(p.ShapeIds ?? new List<string>()), SupportedAdapterIds=new List<string>(p.SupportedAdapterIds ?? new List<string>()),
            PresentationGenders=new List<string>(p.PresentationGenders ?? new List<string>()), RequiredPackageIds=new List<string>(p.RequiredPackageIds ?? new List<string>()),
            Credits=new List<string>(p.Credits ?? new List<string>()), Stages=(p.Stages ?? new List<PresentationStageData>()).Select(Copy).ToList() }; }
        private static PresentationStageData Copy(PresentationStageData s) { return s == null ? null : s.Copy(); }
        private static RacePresentationData Copy(RacePresentationData d) { return new RacePresentationData {
            ProviderPackageId=d.ProviderPackageId, RacePackageId=d.RacePackageId, RaceDefName=d.RaceDefName, AdapterId=d.AdapterId, Priority=d.Priority,
            DefaultMaleProfileId=d.DefaultMaleProfileId, DefaultFemaleProfileId=d.DefaultFemaleProfileId, DefaultNoneProfileId=d.DefaultNoneProfileId,
            FallbackProfileId=d.FallbackProfileId, RequiredCapabilityIds=new List<string>(d.RequiredCapabilityIds ?? new List<string>()) }; }
        private static bool Blank(string s) { return string.IsNullOrWhiteSpace(s); }
        private static bool ValidId(string s) { return !Blank(s) && s.Contains(".") && s.Split('.').All(part => part.Length > 0 && part.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9')); }
        private static bool ValidTexturePath(string path)
        {
            if (Blank(path) || path.StartsWith("/") || path.StartsWith("\\") || path.Contains(":") || path.Contains("\\")) return false;
            return path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
        }
        private static PresentationDiagnostic D(string code, string subject, string provider) { return new PresentationDiagnostic(code, subject, "", new[] { provider ?? "" }); }
        internal static string RaceKey(string package, string def) { return (package ?? "").ToLowerInvariant() + "/" + (def ?? ""); }
        private static string[] MakeStages() { var a = new List<string> { "thin", "average", "thick", "chonky", "chubby", "corpulent", "fat", "obese", "morbid", "lardy", "enormous", "gigantic", "titanic" }; for (int i=1;i<=20;i++) a.Add("gelatinous" + i.ToString("00")); return a.ToArray(); }
    }
}
