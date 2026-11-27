using System;
using System.Collections.Generic;

namespace RimRound.Presentation
{
    // These are XML-facing inputs. Build copies them; callers may continue to edit their inputs.
    public sealed class PresentationStageData
    {
        public string ShapeId;
        public string StageKey;
        public string ReuseProfileId;
        public string ReuseShapeId;
        public string ReuseStageKey;
        public string BodyTextureRoot;
        public string DirectionPolicy = "mirrorWest";
        public string ShaderPolicy = "cutout";
        public string ColorPolicy = "skin";
        public string MaskTextureRoot;
        public string MaskPolicy = "none";
        public float MapScale = 1f;
        public float PortraitScale = 1f;
        public float PortraitZoom = 1f;
        public float PortraitPanX;
        public float PortraitPanY;
        public float PortraitPanStyleY;
        public bool ForceFittedPortrait;
        public string DownedPosePolicy = "native";
        public string AnchorPolicy = "native";
        public string LegacyBodyTypeDefName;
        public float HeadOffsetX;
        public float HeadOffsetY;
        public float NorthOffsetX;
        public float NorthOffsetY;
        public float BedOffsetX;
        public float BedOffsetY;
        public float WoundOffsetX;
        public float WoundOffsetY;
        public float EyeOffsetX;
        public float EyeOffsetY;
        public string ApparelSuffix;
        public string ApparelFallback = "native";
        public string CorpsePolicy = "native";
        public string DessicationPolicy = "native";
        public float WiggleSpeed = 1f;
        internal PresentationStageData Copy() { return (PresentationStageData)MemberwiseClone(); }
    }

    public sealed class PresentationVariantData
    {
        public string Id;
        public string ProfileId;
        internal PresentationVariantData Copy() { return (PresentationVariantData)MemberwiseClone(); }
    }

    public sealed class PresentationProfileData
    {
        public int Schema = 1;
        public string ProviderPackageId;
        public string DefName;
        public string ProfileId;
        public string ArtSetId;
        public string Label;
        public string Description;
        public string DefaultShapeId = "standard";
        public List<string> ShapeIds = new List<string>();
        public List<string> SupportedAdapterIds = new List<string>();
        public List<string> PresentationGenders = new List<string>();
        public List<string> RequiredPackageIds = new List<string>();
        public List<string> Credits = new List<string>();
        public string FallbackProfileId;
        public bool Selectable = true;
        public List<PresentationVariantData> Variants = new List<PresentationVariantData>();
        public List<PresentationStageData> Stages = new List<PresentationStageData>();
    }

    public sealed class RacePresentationData
    {
        public string ProviderPackageId;
        public string RacePackageId;
        public string RaceDefName;
        public string AdapterId;
        public int Priority;
        public string DefaultMaleProfileId;
        public string DefaultFemaleProfileId;
        public string DefaultNoneProfileId;
        public string FallbackProfileId;
        public List<string> RequiredCapabilityIds = new List<string>();
    }

    public sealed class PresentationRequest
    {
        public string RacePackageId;
        public string RaceDefName;
        public IReadOnlyCollection<string> AdapterCapabilityIds;
        public string Gender;
        public string RequestedPresentationGender;
        public string RequestedProfileId;
        public string RequestedShapeId;
        public string SelectionMode = "default";
        public string StageKey;
        public string VisualCapStageKey;
        public long StateRevision;
        public IReadOnlyList<string> VariantIds;
        public string DefaultProfileIdOverride;
        public bool NativeDefault;
    }

    public sealed class PresentationDiagnostic
    {
        public readonly string Code;
        public readonly string Subject;
        public readonly string Detail;
        public readonly IReadOnlyList<string> Providers;

        internal PresentationDiagnostic(string code, string subject, string detail, IEnumerable<string> providers)
        {
            Code = code;
            Subject = subject;
            Detail = detail;
            Providers = Array.AsReadOnly(new List<string>(providers ?? new string[0]).ToArray());
        }
    }
}
