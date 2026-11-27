using System;

namespace RimRound.Simulation
{
    /// <summary>Food-decision estimates only. Never writes mass or energy.</summary>
    public static class BodyGoalRegulation
    {
        public const float MaintainToleranceBaselineFraction = .01f;
        public const float AutoHysteresisBaselineFraction = .02f;

        public static float ProjectedMass(float mass, float pendingNutrition, float gainMultiplier, float lossMultiplier)
            => ProjectedMass(mass, (double)pendingNutrition, gainMultiplier, lossMultiplier);

        public static float ProjectedMass(float mass, double pendingNutrition, float gainMultiplier, float lossMultiplier)
        {
            if (!Finite(mass) || mass <= 0f) return 0f;
            if (!Finite(pendingNutrition)) return mass;
            float multiplier = pendingNutrition >= 0f ? gainMultiplier : lossMultiplier;
            if (!Finite(multiplier) || multiplier <= 0f) return mass;
            double projected = mass + pendingNutrition * (pendingNutrition >= 0d ? multiplier : Math.Min(10f, multiplier)) / 4d;
            return (float)Math.Max(0d, Math.Min(float.MaxValue, projected));
        }

        public static BodyGoal SelectRangeAction(float projectedMass, float lower, float upper,
            float baseline, BodyGoal previous)
        {
            if (!Finite(projectedMass) || !Finite(lower) || !Finite(upper) ||
                !Finite(baseline) || baseline <= 0f || lower <= 0f || upper < lower)
                return BodyGoal.Maintain;
            // Do not let a buffer swallow a narrow range or force oscillation.
            float buffer = Math.Min(baseline * AutoHysteresisBaselineFraction, (upper - lower) * .25f);
            if (previous == BodyGoal.Gain && projectedMass < lower + buffer) return BodyGoal.Gain;
            if (previous == BodyGoal.Lose && projectedMass > upper - buffer) return BodyGoal.Lose;
            if (projectedMass < lower - buffer) return BodyGoal.Gain;
            if (projectedMass > upper + buffer) return BodyGoal.Lose;
            return BodyGoal.Maintain;
        }

        public static float NutritionUntilTarget(float mass, float target, float pendingNutrition,
            float gainMultiplier, float lossMultiplier)
            => NutritionUntilTarget(mass, target, (double)pendingNutrition, gainMultiplier, lossMultiplier);

        public static float NutritionUntilTarget(float mass, float target, double pendingNutrition,
            float gainMultiplier, float lossMultiplier)
        {
            if (!Finite(mass) || !Finite(target) || mass <= 0f || target <= 0f ||
                !Finite(pendingNutrition)) return 0f;
            float multiplier = target >= mass ? gainMultiplier : lossMultiplier;
            if (!Finite(multiplier) || multiplier <= 0f) return 0f;
            double requiredBalance = ((double)target - mass) * 4d / (target >= mass ? multiplier : Math.Min(10f, multiplier));
            return (float)Math.Max(0d, Math.Min(float.MaxValue, requiredBalance - pendingNutrition));
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
