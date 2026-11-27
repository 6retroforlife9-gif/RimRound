using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using System;
using Verse;

namespace RimRound.Comps
{
    public partial class ThingComp_PawnAttitude
    {
        private readonly BodyPreferenceState preference = new BodyPreferenceState();
        public bool HasPreference => preference.IsUsable;
        public float PreferredLowerRatio => preference.LowerRatio;
        public float PreferredUpperRatio => preference.UpperRatio;
        public float PreferenceConviction => preference.Conviction;
        public int PreferenceSchema => preference.SchemaVersion;

        private void ExposePreference()
        {
            Scribe_Values.Look(ref preference.SchemaVersion, "rrPreferenceSchema", 0);
            Scribe_Values.Look(ref preference.SeedOpinion, "rrPreferenceSeedOpinion", 0);
            Scribe_Values.Look(ref preference.LowerRatio, "rrPreferenceLowerRatio", 0f);
            Scribe_Values.Look(ref preference.UpperRatio, "rrPreferenceUpperRatio", 0f);
            Scribe_Values.Look(ref preference.Conviction, "rrPreferenceConviction", 0f);
            Scribe_Values.Look(ref _weightOpinionFloatInt, "rrLegacyOpinionScore", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) InitializePreference();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            InitializePreference();
            InitializeSocialBodyTaste();
        }

        public void InitializePreference()
        {
            Pawn pawn = parent as Pawn;
            if (!BodyModelEligibility.HasAdultBiology(pawn) || pawn.story?.traits == null) return;
            if (preference.IsUsable) { ProjectPreference(); return; }
            if (preference.SchemaVersion != 0) return;
            WeightOpinion seed = weightOpinion;
            if (seed == WeightOpinion.None)
            {
                // Stable precedence for conflicting old traits, independent
                // of their list order. Unknown saved opinions remain raw.
                for (int i = 1; i <= 8; i++)
                {
                    TraitDef trait = WeightOpinionUtility.GetTraitByWeightOpinion((WeightOpinion)i);
                    if (trait != null && pawn.story.traits.HasTrait(trait)) { seed = (WeightOpinion)i; break; }
                }
                if (seed == WeightOpinion.None)
                    seed = WeightOpinionUtility.TraitDefToWeightOpinion(WeightOpinionUtility.AssignInitialWeightOpinionTraits(pawn));
            }
            // Conviction is independent of mass, religion, guest resistance
            // and player assignments. This runs once, at an explicit lifecycle.
            if (preference.TryInitialize((int)seed, .6f)) ProjectPreference();
        }

        private void ProjectPreference()
        {
            if (!preference.IsUsable) return;
            weightOpinion = (WeightOpinion)preference.ProjectedOpinion;
            Pawn pawn = parent as Pawn;
            TraitDef projected = WeightOpinionUtility.GetTraitByWeightOpinion(weightOpinion);
            if (pawn?.story?.traits == null || projected == null) return;
            int count = 0;
            foreach (Trait trait in pawn.story.traits.allTraits)
                if (WeightOpinionUtility.TraitDefToWeightOpinion(trait.def) != WeightOpinion.None) count++;
            if (count == 1 && pawn.story.traits.HasTrait(projected)) return;
            WeightOpinionUtility.RemoveWeightOpinionTraits(pawn);
            pawn.story.traits.GainTrait(new Trait(projected, 0, true));
        }

        public bool TrySetPreference(float lowerRatio, float upperRatio, float conviction)
        {
            if (!BodyModelEligibility.HasAdultBiology(parent as Pawn) ||
                !preference.TrySetRange(lowerRatio, upperRatio, conviction)) return false;
            ProjectPreference();
            return true;
        }

        public bool TryGetPreferredRangeKg(out float lower, out float upper)
        {
            Pawn pawn = parent as Pawn;
            float declared = pawn?.def.GetModExtension<BodyMetabolismExtension>()?.baselineMassKg ?? 0f;
            float baseline = declared > 0f && BodyEnergyState.IsFinite(declared)
                ? declared : 70f * (pawn?.def.race?.baseBodySize ?? 1f);
            baseline = Math.Max(1f, Math.Min(10000f, baseline));
            return preference.TryGetRangeKg(baseline, out lower, out upper);
        }

        public string PreferenceDescription
        {
            get
            {
                float lower, upper;
                if (!TryGetPreferredRangeKg(out lower, out upper)) return "RR_PreferenceUnavailable".Translate();
                float scale = GlobalSettings.usePoundsWherePossible ? 2.20462f : 1f;
                string unit = GlobalSettings.usePoundsWherePossible ? "lb" : "kg";
                return "RR_PreferenceSummary".Translate((lower * scale).ToString("F1"),
                    (upper * scale).ToString("F1"), unit, preference.Conviction.ToString("P0"));
            }
        }
    }
}
