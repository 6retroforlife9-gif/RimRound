using RimRound.Comps;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace RimRound.AI
{
    public sealed class ThoughtWorker_BodyPreference : ThoughtWorker
    {
        public override string PostProcessDescription(Pawn pawn, string description)
        {
            return base.PostProcessDescription(pawn, SelectDescription(pawn, description));
        }

        private int MatchingDescriptionStage(string description)
        {
            // RimWorld passes the cached thought's loaded stage description, which can
            // differ from a fresh calculation after the pawn changes weight.
            var stages = def?.stages;
            if (string.IsNullOrEmpty(description) || stages?.Count != BodyPreferenceWording.StageCount) return -1;
            int match = -1;
            for (int i = 0; i < stages.Count; i++)
            {
                string stageText = stages[i]?.description;
                if ((stageText ?? def.description) != description) continue;
                if (stageText == null || match >= 0) return -1;
                match = i;
            }
            return match;
        }

        private string SelectDescription(Pawn pawn, string cachedGeneral)
        {
            int cachedStage = MatchingDescriptionStage(cachedGeneral);
            if (cachedStage < 0 || Current.Game == null || !GlobalSettings.moodletsForWeightOpinions ||
                !BodyModelEligibility.IsEligible(pawn)) return cachedGeneral;
            var presentation = pawn.TryGetComp<PawnBodyType_ThingComp>();
            if (presentation?.PersonallyExempt == true || presentation?.CategoricallyExempt == true)
                return cachedGeneral;
            var attitude = pawn.TryGetComp<ThingComp_PawnAttitude>();
            var weight = pawn.WeightHediff();
            var stages = weight?.def?.stages;
            if (stages == null || attitude == null || !attitude.TryGetPreferredRangeKg(out float lower, out float upper))
                return cachedGeneral;
            var thick = UniqueWeightStage(stages, "Thick");
            var fat = UniqueWeightStage(stages, "Fat");
            if (thick == null || fat == null) return cachedGeneral;
            float multiplier = RacialBodyTypeInfoUtility.GetBodyTypeWeightRequirementMultiplier(pawn);
            float firstNonLean = Utilities.HediffUtility.SeverityToKilosWithBaseWeight(thick.minSeverity * multiplier);
            float firstFat = Utilities.HediffUtility.SeverityToKilosWithBaseWeight(fat.minSeverity * multiplier);
            if (!(firstNonLean > 0f && firstFat > firstNonLean) || !BodyEnergyState.IsFinite(firstFat))
                return cachedGeneral;
            var family = BodyPreferenceState.DescriptionFamilyForRange(lower, upper, firstNonLean, firstFat);
            float mass = Utilities.HediffUtility.SeverityToKilosWithBaseWeight(weight.Severity);
            int liveStage = BodyPreferenceState.MoodStageIndex(mass, lower, upper);
            if (!BodyPreferenceWording.TryCreate(pawn.thingIDNumber, Find.TickManager.TicksGame, cachedStage,
                liveStage, family, out var wording)) return cachedGeneral;
            var regions = wording.CanUseRegionWish ? BodyPreferenceWordingContext.PresentWishRegions(pawn) : BodyPreferenceWishRegions.None;
            bool weightLimitsWalking = wording.CanUseWalkingDifficulty &&
                BodyPreferenceWordingContext.HasWeightCausedWalkingDifficulty(pawn);
            return wording.Resolve(cachedGeneral, attitude.PreferredLowerRatio, regions, weightLimitsWalking, ReadTranslation);
        }

        private static string ReadTranslation(string key)
        {
            var language = LanguageDatabase.activeLanguage;
            return language != null && language.TryGetTextFromKey(key, out TaggedString text) &&
                !string.IsNullOrWhiteSpace(text) ? text.RawText : null;
        }

        private static HediffStage UniqueWeightStage(List<HediffStage> stages, string originalLabel)
        {
            // HediffStage.PostLoad retains the source label before localization.
            HediffStage match = null;
            foreach (var stage in stages)
            {
                if (stage?.untranslatedLabel != originalLabel) continue;
                if (match != null) return null;
                match = stage;
            }
            return match;
        }

        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            if (!GlobalSettings.moodletsForWeightOpinions || !BodyModelEligibility.IsEligible(pawn) ||
                pawn.WeightHediff() == null) return ThoughtState.Inactive;
            var presentation = pawn.TryGetComp<PawnBodyType_ThingComp>();
            if (presentation?.PersonallyExempt == true || presentation?.CategoricallyExempt == true)
                return ThoughtState.Inactive;
            var attitude = pawn.TryGetComp<ThingComp_PawnAttitude>();
            float lower, upper;
            if (attitude == null || !attitude.TryGetPreferredRangeKg(out lower, out upper))
                return ThoughtState.Inactive;
            int stage = BodyPreferenceState.MoodStageIndex(pawn.Weight(), lower, upper);
            if (stage < 0) return ThoughtState.Inactive;
            return ThoughtState.ActiveAtStage(stage);
        }
    }
}
