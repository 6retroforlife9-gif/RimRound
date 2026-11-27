using RimRound.Simulation;
using RimRound.Utilities;
using UnityEngine;
using Verse;

namespace RimRound.Comps
{
    public partial class FullnessAndDietStats_ThingComp
    {
        private BodyGoal previousRangeAction = BodyGoal.Maintain;
        public BodyGoal AssignedGoal => bodyGoal;
        public bool ContinuousGain => bodyGoal == BodyGoal.Gain || bodyGoal == BodyGoal.ForceGain;
        public bool HasGainAppetite => ContinuousGain || AdditionalGoalNutrition > .0001f;
        public float LimitGainNutrition(float requested) => ContinuousGain ? requested : Mathf.Min(requested, AdditionalGoalNutrition);
        public float ProjectedBodyMassKg => BodyGoalRegulation.ProjectedMass(
            ((Pawn)parent).Weight() + massProjectionRemainderKg, ProjectedEnergyNutrition,
            WeightGainMultiplier, WeightLossMultiplier);

        private void ExposePreferenceGoal()
        {
            Scribe_Values.Look(ref previousRangeAction, "rrPreviousRangeAction", BodyGoal.Maintain);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && previousRangeAction != BodyGoal.Gain &&
                previousRangeAction != BodyGoal.Lose && previousRangeAction != BodyGoal.Maintain)
                previousRangeAction = BodyGoal.Maintain;
        }

        public bool TryGetGoalRange(out float lower, out float upper)
        {
            lower = upper = targetMassKg;
            if (ContinuousGain) return false;
            if (bodyGoal == BodyGoal.Auto)
            {
                var attitude = parent.TryGetComp<ThingComp_PawnAttitude>();
                if (attitude == null || !attitude.TryGetPreferredRangeKg(out lower, out upper)) return false;
                Hediff mass = ((Pawn)parent).WeightHediff();
                float minimum = Mathf.Max(GlobalSettings.minWeight.threshold, mass == null ? 26f :
                    Utilities.HediffUtility.SeverityToKilosWithBaseWeight(mass.def.minSeverity));
                float maximum = Mathf.Max(minimum, Mathf.Min(GlobalSettings.maxWeight.threshold, mass == null ? GlobalSettings.maxWeight.threshold :
                    Utilities.HediffUtility.SeverityToKilosWithBaseWeight(mass.def.maxSeverity)));
                lower = Mathf.Clamp(lower, minimum, maximum);
                upper = Mathf.Clamp(upper, lower, maximum);
                return true;
            }
            if (targetMassKg <= 0f || !BodyEnergyState.IsFinite(targetMassKg)) return false;
            if (bodyGoal == BodyGoal.Maintain)
            {
                float tolerance = baselineMassKg * BodyGoalRegulation.MaintainToleranceBaselineFraction;
                lower = Mathf.Max(1f, targetMassKg - tolerance);
                upper = targetMassKg + tolerance;
            }
            return true;
        }

        private BodyGoal ResolveEffectiveBodyGoal()
        {
            if (ContinuousGain) return BodyGoal.Gain;
            if (!UsesBodyGoals || !UsesBodyModel) return bodyGoal == BodyGoal.Auto ? BodyGoal.Maintain : bodyGoal;
            float projected = ProjectedBodyMassKg;
            float lower, upper;
            if (!TryGetGoalRange(out lower, out upper)) return BodyGoal.Maintain;
            if (bodyGoal == BodyGoal.Auto || bodyGoal == BodyGoal.Maintain)
                return BodyGoalRegulation.SelectRangeAction(projected, lower, upper, baselineMassKg, previousRangeAction);
            if (bodyGoal == BodyGoal.Recovery && projected >= targetMassKg)
                return BodyGoal.Maintain;
            if (bodyGoal == BodyGoal.Lose && projected <= targetMassKg) return BodyGoal.Maintain;
            return bodyGoal;
        }

        private float LiveGoalTarget
        {
            get
            {
                float lower, upper;
                return bodyGoal == BodyGoal.Auto && TryGetGoalRange(out lower, out upper)
                    ? (lower + upper) * .5f : targetMassKg;
            }
        }

        public float AdditionalGoalNutrition => BodyGoalRegulation.NutritionUntilTarget(
            ((Pawn)parent).Weight() + massProjectionRemainderKg, LiveGoalTarget,
            ProjectedEnergyNutrition, WeightGainMultiplier, WeightLossMultiplier);

        public string GoalRegulationDescription
        {
            get
            {
                if (!UsesBodyGoals) return null;
                float scale = GlobalSettings.usePoundsWherePossible ? 2.20462f : 1f;
                string unit = GlobalSettings.usePoundsWherePossible ? "lb" : "kg";
                string text = "RR_BodyGoalLiveAction".Translate(("RR_BodyGoal_" + Goal).Translate(),
                    (ProjectedBodyMassKg * scale).ToString("F1"), unit).ToString();
                if (bodyGoal == BodyGoal.Auto && TryGetGoalRange(out float lower, out float upper))
                {
                    text += "\n" + "RR_BodyGoalLiveRange".Translate((lower * scale).ToString("F1"),
                        (upper * scale).ToString("F1"), unit);
                    var attitude = parent.TryGetComp<ThingComp_PawnAttitude>();
                    if (attitude != null && attitude.TryGetPreferredRangeKg(out float desiredLower, out float desiredUpper) &&
                        (Mathf.Abs(lower - desiredLower) > .01f || Mathf.Abs(upper - desiredUpper) > .01f))
                        text += "\n" + "RR_BodyGoalRangeLimited".Translate();
                }
                if (ProjectedEnergyNutrition > .01d)
                    text += "\n" + "RR_BodyGoalStoredEnergy".Translate();
                if (Goal == BodyGoal.Lose) text += "\n" + "RR_BodyGoalWholeMeals".Translate();
                return text;
            }
        }
    }
}
