using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimRound.Presentation
{
    public sealed class BodyPresentationVariantData
    {
        public string id;
        public string profileId;
    }

    // Verse's XML loader populates public fields. The pure registry consumes a copied data object.
    public sealed class BodyPresentationProfileDef : Def
    {
        public int schema = 1;
        public string profileId;
        public string artSetId;
        public string defaultShapeId = "standard";
        public List<string> shapeIds = new List<string>();
        public List<string> supportedAdapterIds = new List<string>();
        public List<string> presentationGenders = new List<string>();
        public List<string> requiredPackageIds = new List<string>();
        public List<string> credits = new List<string>();
        public string fallbackProfileId;
        public bool selectable = true;
        public List<BodyPresentationVariantData> variants = new List<BodyPresentationVariantData>();
        public List<PresentationStageData> stages = new List<PresentationStageData>();

        public PresentationProfileData ToData(string providerPackageId)
        {
            return new PresentationProfileData {
                Schema = schema, ProviderPackageId = providerPackageId, DefName = defName, ProfileId = profileId,
                ArtSetId = artSetId, Label = label, Description = description,
                DefaultShapeId = defaultShapeId, ShapeIds = shapeIds, SupportedAdapterIds = supportedAdapterIds,
                PresentationGenders = presentationGenders, RequiredPackageIds = requiredPackageIds,
                Credits = credits, FallbackProfileId = fallbackProfileId, Stages = stages, Selectable = selectable,
                Variants = variants == null ? null : variants.Select(v => v == null ? null :
                    new PresentationVariantData { Id = v.id, ProfileId = v.profileId }).ToList()
            };
        }
    }
}
