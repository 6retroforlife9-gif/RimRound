using System;

namespace RimRound.Simulation
{
    /// <summary>Bounded rounding between a nutrition target and indivisible meals.</summary>
    public static class BodyMealBudget
    {
        public const float MaximumCarryNutrition = 0.9f;

        public static float NextCarry(float previous, float earned, float eaten)
        {
            if (!BodyEnergyState.IsFinite(previous) ||
                !BodyEnergyState.IsFinite(earned) || earned < 0f ||
                !BodyEnergyState.IsFinite(eaten) || eaten < 0f) return 0f;
            return Math.Max(-MaximumCarryNutrition,
                Math.Min(MaximumCarryNutrition, previous + earned - eaten));
        }
    }
}
