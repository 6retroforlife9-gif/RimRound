using RimRound.Simulation;
using RimRound.Utilities;
using Verse;

namespace RimRound.Comps
{
    public partial class ThingComp_PawnAttitude
    {
        private readonly BodySocialTasteState socialBodyTaste = new BodySocialTasteState();

        public bool HasSocialBodyTaste => socialBodyTaste.IsUsable;
        public int SocialBodyTasteLevel => socialBodyTaste.UsableLevel;
        public int? SocialBodyTasteSchema => socialBodyTaste.SchemaVersion;
        public bool SocialBodyTasteSchemaPresent => socialBodyTaste.SchemaPresent;
        public bool SocialBodyTasteLevelPresent => socialBodyTaste.LevelPresent;
        public int? SocialBodyTasteRawLevel => socialBodyTaste.Level;
        public string SocialBodyTasteMalformedSchemaText => socialBodyTaste.MalformedSchemaText;
        public string SocialBodyTasteMalformedLevelText => socialBodyTaste.MalformedLevelText;

        private void ExposeSocialBodyTaste()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // A nullable Scribe read alone loses the absent/present-null
                // distinction. Parse both typed fields before any initialization.
                socialBodyTaste.LoadRawFields(
                    Scribe.loader.curXmlParent?[BodySocialTasteState.SchemaKey],
                    Scribe.loader.curXmlParent?[BodySocialTasteState.LevelKey]);
            }
            else if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Never emit an absent node; force each present value, including
                // zero and null. Malformed text becomes a present-null marker.
                if (socialBodyTaste.SchemaPresent)
                {
                    int? schema = socialBodyTaste.SchemaVersion;
                    Scribe_Values.Look(ref schema, BodySocialTasteState.SchemaKey, null, true);
                }
                if (socialBodyTaste.LevelPresent)
                {
                    int? level = socialBodyTaste.Level;
                    Scribe_Values.Look(ref level, BodySocialTasteState.LevelKey, null, true);
                }
            }
            else if (Scribe.mode == LoadSaveMode.PostLoadInit) InitializeSocialBodyTaste();
        }

        public void InitializeSocialBodyTaste()
        {
            if (socialBodyTaste.SchemaPresent || socialBodyTaste.LevelPresent) return;
            Pawn pawn = parent as Pawn;
            if (!PawnHelperEligibility.HasSocialIdentity(pawn)) return;
            // Native load can initialize comps before assigning fallback Ideo.
            // Wait for that expected identity; spawn/body updates retry without
            // ever rewriting an already present taste.
            if (ModsConfig.IdeologyActive && pawn.ShouldHaveIdeo && pawn.Ideo == null) return;
            socialBodyTaste.TryInitialize(pawn?.thingIDNumber ?? 0,
                true, AI.ThoughtWorker_SocialBodyOpinion.HasEmbodiedAbundance(pawn));
        }
    }
}
