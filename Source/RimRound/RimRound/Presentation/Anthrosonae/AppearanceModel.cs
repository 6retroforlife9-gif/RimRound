using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RimRound.Presentation.Anthrosonae
{
    public readonly struct AppearanceColor : IEquatable<AppearanceColor>
    {
        public readonly float R, G, B, A;
        public static readonly AppearanceColor White = new AppearanceColor(1, 1, 1, 1);
        public AppearanceColor(float r, float g, float b, float a)
        {
            if (new[] { r, g, b, a }.Any(x => float.IsNaN(x) || float.IsInfinity(x)))
                throw new ArgumentException("Palette components must be finite.");
            R = r; G = g; B = b; A = a;
        }
        public AppearanceColor Multiply(AppearanceColor other) => new AppearanceColor(R * other.R, G * other.G, B * other.B, A * other.A);
        public bool Equals(AppearanceColor other) => R == other.R && G == other.G && B == other.B && A == other.A;
        public override bool Equals(object obj) => obj is AppearanceColor other && Equals(other);
        public override int GetHashCode() { unchecked { return (((R.GetHashCode() * 397) ^ G.GetHashCode()) * 397 ^ B.GetHashCode()) * 397 ^ A.GetHashCode(); } }
    }

    public readonly struct AppearancePalette : IEquatable<AppearancePalette>
    {
        public readonly AppearanceColor Primary;
        public readonly AppearanceColor? Secondary;
        public AppearancePalette(AppearanceColor primary, AppearanceColor? secondary) { Primary = primary; Secondary = secondary; }
        public bool Equals(AppearancePalette other) => Primary.Equals(other.Primary) && Nullable.Equals(Secondary, other.Secondary);
        public override bool Equals(object obj) => obj is AppearancePalette other && Equals(other);
        public override int GetHashCode() => Primary.GetHashCode() * 397 ^ Secondary.GetHashCode();
    }

    public readonly struct AppearanceNodeKey : IEquatable<AppearanceNodeKey>
    {
        public readonly string ProviderPackageId, GeneDefName;
        public readonly int NodeOrdinal;
        public AppearanceNodeKey(string provider, string gene, int ordinal)
        {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(gene) || ordinal < 0)
                throw new ArgumentException("An exact provider, gene and node ordinal are required.");
            ProviderPackageId = provider.ToLowerInvariant(); GeneDefName = gene; NodeOrdinal = ordinal;
        }
        public bool Equals(AppearanceNodeKey other) => ProviderPackageId == other.ProviderPackageId && GeneDefName == other.GeneDefName && NodeOrdinal == other.NodeOrdinal;
        public override bool Equals(object obj) => obj is AppearanceNodeKey other && Equals(other);
        public override int GetHashCode() => ((ProviderPackageId.GetHashCode() * 397) ^ GeneDefName.GetHashCode()) * 397 ^ NodeOrdinal;
        public override string ToString() => ProviderPackageId + "/" + GeneDefName + "/" + NodeOrdinal;
    }

    public readonly struct AppearanceCoatKey : IEquatable<AppearanceCoatKey>
    {
        public readonly string ProviderPackageId, GeneDefName, FurDefName;
        public AppearanceCoatKey(string provider, string gene, string fur)
        {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(gene) || string.IsNullOrWhiteSpace(fur))
                throw new ArgumentException("An exact coat provider, gene and FurDef are required.");
            ProviderPackageId = provider.ToLowerInvariant(); GeneDefName = gene; FurDefName = fur;
        }
        public bool Equals(AppearanceCoatKey other) => ProviderPackageId == other.ProviderPackageId && GeneDefName == other.GeneDefName && FurDefName == other.FurDefName;
        public override bool Equals(object obj) => obj is AppearanceCoatKey other && Equals(other);
        public override int GetHashCode() => ((ProviderPackageId.GetHashCode() * 397) ^ GeneDefName.GetHashCode()) * 397 ^ FurDefName.GetHashCode();
        public override string ToString() => ProviderPackageId + "/" + GeneDefName + "/" + FurDefName;
    }

    // These fields are canonical values from fully resolved runtime properties,
    // not raw XML text or a hash of only a few familiar properties.
    public sealed class AppearanceNodeStructure : IEquatable<AppearanceNodeStructure>
    {
        public readonly IReadOnlyDictionary<string, string> Fields;
        public AppearanceNodeStructure(IEnumerable<KeyValuePair<string, string>> fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Key) || field.Value == null) throw new ArgumentException("Structural fields must be explicit.");
                copy.Add(field.Key.Trim(), field.Value.Trim());
            }
            if (copy.Count == 0) throw new ArgumentException("Empty structure cannot establish support.");
            Fields = new ReadOnlyDictionary<string, string>(copy);
        }
        public bool Equals(AppearanceNodeStructure other) => other != null && Fields.Count == other.Fields.Count &&
            Fields.All(x => other.Fields.TryGetValue(x.Key, out var value) && value == x.Value);
        public override bool Equals(object obj) => Equals(obj as AppearanceNodeStructure);
        public override int GetHashCode() { unchecked { int result = 17; foreach (var field in Fields) result = (result * 397 ^ field.Key.GetHashCode()) * 397 ^ field.Value.GetHashCode(); return result; } }
    }

    public enum AppearanceLayerLocation { Head, Body, Unknown }
    public enum AppearanceApiStatus { Absent, Available, Incompatible }
    public sealed class AppearanceLayer : IEquatable<AppearanceLayer>
    {
        public readonly AppearanceNodeKey Key;
        public readonly AppearanceNodeStructure Structure;
        public readonly AppearanceLayerLocation Location;
        public AppearanceLayer(AppearanceNodeKey key, AppearanceNodeStructure structure, AppearanceLayerLocation location)
        { Key = key; Structure = structure ?? throw new ArgumentNullException(nameof(structure)); Location = location; }
        public bool Equals(AppearanceLayer other) => other != null && Key.Equals(other.Key) && Location == other.Location && Structure.Equals(other.Structure);
        public override bool Equals(object obj) => Equals(obj as AppearanceLayer);
        public override int GetHashCode() => (Key.GetHashCode() * 397 ^ Structure.GetHashCode()) * 397 ^ (int)Location;
    }

    public sealed class AppearanceDescriptor : IEquatable<AppearanceDescriptor>
    {
        public readonly AppearanceApiStatus ApiStatus;
        public readonly string AdapterId, ApiVersion, NativeFurDefName;
        public readonly AppearanceCoatKey? ActiveCoat;
        public readonly AppearancePalette Palette;
        public readonly IReadOnlyList<AppearanceLayer> Layers;
        public AppearanceDescriptor(AppearanceApiStatus apiStatus, string adapterId, string apiVersion,
            string nativeFurDefName, AppearanceCoatKey? activeCoat, AppearancePalette palette, IEnumerable<AppearanceLayer> layers)
        {
            ApiStatus = apiStatus; AdapterId = adapterId; ApiVersion = apiVersion; NativeFurDefName = nativeFurDefName;
            ActiveCoat = activeCoat; Palette = palette;
            var copy = (layers ?? new AppearanceLayer[0]).ToArray();
            if (copy.Any(x => x == null)) throw new ArgumentException("Null layers are not a complete capture.");
            Layers = Array.AsReadOnly(copy.OrderBy(x => x.Key.ToString(), StringComparer.Ordinal).ToArray());
        }
        public bool Equals(AppearanceDescriptor other) => other != null && ApiStatus == other.ApiStatus && AdapterId == other.AdapterId &&
            ApiVersion == other.ApiVersion && NativeFurDefName == other.NativeFurDefName && Nullable.Equals(ActiveCoat, other.ActiveCoat) &&
            Palette.Equals(other.Palette) && Layers.SequenceEqual(other.Layers);
        public override bool Equals(object obj) => Equals(obj as AppearanceDescriptor);
        public override int GetHashCode() => (ActiveCoat.GetHashCode() * 397 ^ Palette.GetHashCode()) * 397 ^ (int)ApiStatus;
    }

    public readonly struct AppearanceSilhouetteKey : IEquatable<AppearanceSilhouetteKey>
    {
        public readonly string SourceProfileId, SourceShapeId, SourceStageKey;
        public AppearanceSilhouetteKey(string profile, string shape, string stage)
        {
            if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(shape) || string.IsNullOrWhiteSpace(stage))
                throw new ArgumentException("Resolved source silhouette identity is required.");
            SourceProfileId = profile; SourceShapeId = shape; SourceStageKey = stage;
        }
        public bool Equals(AppearanceSilhouetteKey other) => SourceProfileId == other.SourceProfileId && SourceShapeId == other.SourceShapeId && SourceStageKey == other.SourceStageKey;
        public override bool Equals(object obj) => obj is AppearanceSilhouetteKey other && Equals(other);
        public override int GetHashCode() => ((SourceProfileId.GetHashCode() * 397) ^ SourceShapeId.GetHashCode()) * 397 ^ SourceStageKey.GetHashCode();
    }

    public enum RegisteredAppearanceRole { CoatSurface, BodyAttachment }
    public sealed class AppearanceLayerRegistration
    {
        public readonly AppearanceNodeKey Key;
        public readonly AppearanceNodeStructure Structure;
        public readonly RegisteredAppearanceRole Role;
        public readonly string AttachmentFamily;
        public AppearanceLayerRegistration(AppearanceNodeKey key, AppearanceNodeStructure structure, RegisteredAppearanceRole role, string family = null)
        {
            Key = key; Structure = structure ?? throw new ArgumentNullException(nameof(structure)); Role = role;
            if (role == RegisteredAppearanceRole.BodyAttachment && string.IsNullOrWhiteSpace(family)) throw new ArgumentException("Attachment family is required.");
            AttachmentFamily = family;
        }
    }

    // Centred UV coordinates in the body and the fixed-size attachment meshes.
    // The native render tree applies their common ancestor transforms afterwards.
    public sealed class AppearanceSideJoint
    {
        public readonly float BodyX, BodyZ, AttachmentX, AttachmentZ;
        public AppearanceSideJoint(float bodyX, float bodyZ, float attachmentX, float attachmentZ)
        {
            if (new[] { bodyX, bodyZ, attachmentX, attachmentZ }.Any(x => float.IsNaN(x) || float.IsInfinity(x) || x < -.5f || x > .5f))
                throw new ArgumentException("Joint coordinates must lie within their texture quads.");
            BodyX = bodyX; BodyZ = bodyZ; AttachmentX = attachmentX; AttachmentZ = attachmentZ;
        }
        public float DeltaX(float bodyWidth, float attachmentWidth, float nativeOffset) => bodyWidth * BodyX - attachmentWidth * AttachmentX - nativeOffset;
        public float DeltaZ(float bodyHeight, float attachmentHeight, float nativeOffset) => bodyHeight * BodyZ - attachmentHeight * AttachmentZ - nativeOffset;
    }

    // Fit values are declared in final image axes. Actual facing/pose conversion
    // is a separate verified native worker boundary; no shared Def is changed.
    public enum AppearanceRearPosePolicy { Native, Hidden, NorthTurned }

    // The South artwork may cover the rear completely, or expose a raised
    // back/butt. The joint is expressed AFTER turning the North attachment.
    public sealed class AppearanceRearFit
    {
        public readonly AppearanceRearPosePolicy Policy;
        public readonly AppearanceSideJoint SouthJoint;
        public AppearanceRearFit(AppearanceRearPosePolicy policy, AppearanceSideJoint southJoint = null)
        {
            if (policy != AppearanceRearPosePolicy.Hidden && policy != AppearanceRearPosePolicy.NorthTurned)
                throw new ArgumentException("Explicit rear pose required.");
            if ((policy == AppearanceRearPosePolicy.NorthTurned) != (southJoint != null))
                throw new ArgumentException("Only an exposed rear pose has a South joint.");
            Policy = policy; SouthJoint = southJoint;
        }
    }

    public sealed class AppearanceAttachmentFit
    {
        public readonly string Family, PlacementPolicyId;
        public readonly bool Validated;
        public readonly AppearanceSideJoint SideJoint, NorthJoint;
        public readonly AppearanceRearFit RearFit;
        public AppearanceAttachmentFit(string family, string placementPolicyId, bool validated, AppearanceSideJoint sideJoint = null,
            AppearanceRearFit rearFit = null, AppearanceSideJoint northJoint = null)
        {
            if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(placementPolicyId)) throw new ArgumentException("Exact attachment placement policy is required.");
            Family = family; PlacementPolicyId = placementPolicyId; Validated = validated;
            SideJoint = sideJoint; RearFit = rearFit; NorthJoint = northJoint;
        }
    }

    public sealed class AppearanceCoatMapping
    {
        public readonly AppearanceCoatKey Coat;
        public readonly AppearanceSilhouetteKey Silhouette;
        public readonly string BodyTextureRoot, MaskTextureRoot, DirectionPolicy, MaterialPolicyId;
        public readonly string NativeReferenceBody;
        public readonly bool NeutralizeBodyValue;
        public readonly bool AssetsAvailable, DiagnosticOnly, EqualPaletteOnly;
        public readonly IReadOnlyDictionary<string, AppearanceAttachmentFit> AttachmentFits;
        public AppearanceCoatMapping(AppearanceCoatKey coat, AppearanceSilhouetteKey silhouette, string body, string mask,
            string direction, string materialPolicy, bool assetsAvailable, bool diagnosticOnly, bool equalPaletteOnly,
            IEnumerable<AppearanceAttachmentFit> fits, string nativeReferenceBody = "Female", bool neutralizeBodyValue = false)
        {
            if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(materialPolicy) ||
                (direction != "mirrorWest" && direction != "fourDirections")) throw new ArgumentException("Complete material/art mapping is required.");
            Coat = coat; Silhouette = silhouette; BodyTextureRoot = body; MaskTextureRoot = mask;
            DirectionPolicy = direction; MaterialPolicyId = materialPolicy; AssetsAvailable = assetsAvailable;
            DiagnosticOnly = diagnosticOnly; EqualPaletteOnly = equalPaletteOnly;
            NativeReferenceBody = nativeReferenceBody; NeutralizeBodyValue = neutralizeBodyValue;
            AttachmentFits = new ReadOnlyDictionary<string, AppearanceAttachmentFit>((fits ?? new AppearanceAttachmentFit[0]).ToDictionary(x => x.Family, StringComparer.Ordinal));
        }
    }

    public sealed class AppearanceSupportCatalog
    {
        public readonly long Revision;
        public readonly IReadOnlyList<AppearanceCoatKey> Coats;
        public readonly IReadOnlyDictionary<AppearanceNodeKey, AppearanceLayerRegistration> Layers;
        public readonly IReadOnlyList<AppearanceCoatMapping> Mappings;
        private readonly Dictionary<AppearanceCoatKey, Dictionary<AppearanceSilhouetteKey, AppearanceCoatMapping>> mappingIndex;
        public AppearanceCoatMapping FindMapping(AppearanceCoatKey coat, AppearanceSilhouetteKey silhouette) =>
            mappingIndex.TryGetValue(coat, out var rows) && rows.TryGetValue(silhouette, out var mapping) ? mapping : null;
        public AppearanceSupportCatalog(long revision, IEnumerable<AppearanceCoatKey> coats,
            IEnumerable<AppearanceLayerRegistration> layers, IEnumerable<AppearanceCoatMapping> mappings)
        {
            Revision = revision;
            var coatCopy = (coats ?? new AppearanceCoatKey[0]).ToArray();
            var mappingCopy = (mappings ?? new AppearanceCoatMapping[0]).ToArray();
            if (coatCopy.Distinct().Count() != coatCopy.Length || mappingCopy.Any(x => x == null) ||
                mappingCopy.GroupBy(x => new { x.Coat, x.Silhouette }).Any(x => x.Count() > 1))
                throw new ArgumentException("Ambiguous support registrations must be rejected before publication.");
            Coats = Array.AsReadOnly(coatCopy);
            Layers = new ReadOnlyDictionary<AppearanceNodeKey, AppearanceLayerRegistration>((layers ?? new AppearanceLayerRegistration[0]).ToDictionary(x => x.Key));
            Mappings = Array.AsReadOnly(mappingCopy);
            mappingIndex = mappingCopy.GroupBy(x => x.Coat).ToDictionary(group => group.Key,
                group => group.ToDictionary(x => x.Silhouette));
        }
    }
}
