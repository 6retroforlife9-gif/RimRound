using System;

namespace RimRound.Simulation
{
    /// <summary>Bounded conditioning from useful food and actual active time.</summary>
    public sealed class BodyAdaptationState
    {
        public const int CurrentVersion = 1;
        public int SchemaVersion = CurrentVersion;
        public float EatingConditioning;
        public float SizeConditioning;
        public int WindowTicks;
        public long ActiveTicks;
        public double UsefulNutrition;
        public double ExpenditureNutrition;
        public double UsefulCapacityUnits;

        public bool IsUsable => SchemaVersion == CurrentVersion;
        public float CapacityBonus => IsUsable ? .5f * Unit(EatingConditioning) + .5f * Unit(SizeConditioning) : 0f;
        public float FullnessPainMitigation => IsUsable ? .15f * Unit(EatingConditioning) : 0f;

        public float ConditionedCapacity(float baseCapacity, float volumeMultiplier)
        {
            if (!IsUsable || !Finite(volumeMultiplier) || volumeMultiplier < 0f) return baseCapacity;
            // Full conditioning fits a 3.6-volume Feast at 90% comfortable
            // fullness, including the recipient's effective volume modifier.
            float trainedFloor = 4f * volumeMultiplier * Math.Min(Unit(EatingConditioning), Unit(SizeConditioning));
            return Math.Max(baseCapacity, trainedFloor);
        }

        public void RecordUsefulVolume(float effectiveVolume, float comfortableCapacity)
        {
            if (!IsUsable || !Finite(effectiveVolume) || effectiveVolume <= 0f ||
                !Finite(comfortableCapacity) || comfortableCapacity <= 0f) return;
            double next = UsefulCapacityUnits + (double)effectiveVolume / comfortableCapacity;
            if (!double.IsNaN(next) && !double.IsInfinity(next)) UsefulCapacityUnits = next;
        }

        public void RecordUsefulIntake(float nutrition)
        {
            if (!IsUsable || !Finite(nutrition) || nutrition <= 0f) return;
            double next = UsefulNutrition + nutrition;
            if (!double.IsNaN(next) && !double.IsInfinity(next)) UsefulNutrition = next;
        }

        public void RecordResourceExpenditure(float nutrition)
        {
            if (!IsUsable || !Finite(nutrition) || nutrition <= 0f ||
                double.IsNaN(ExpenditureNutrition) || ExpenditureNutrition < 0d ||
                double.IsInfinity(ExpenditureNutrition + nutrition)) return;
            ExpenditureNutrition += nutrition;
        }

        public void Advance(int ticks, float expenditure, float biologicalMassRatio)
        {
            // Native Food intervals are short. An elapsed/off-map bulk interval
            // is not evidence of an active routine and earns no catch-up.
            if (!IsUsable || ticks <= 0 || ticks > 2500 || !Finite(expenditure) || expenditure < 0f ||
                !Finite(biologicalMassRatio) || biologicalMassRatio <= 0f) return;
            ActiveTicks = Math.Min(long.MaxValue - ticks, Math.Max(0L, ActiveTicks)) + ticks;
            WindowTicks = Math.Max(0, Math.Min(60000, WindowTicks)) + ticks;
            ExpenditureNutrition = Math.Max(0d, ExpenditureNutrition) + expenditure;
            if (WindowTicks < 60000) return;
            double days = WindowTicks / 60000d;
            // Two comfortable stomach volumes per active day support full
            // training. Calories alone cannot substitute for physical meals.
            float intakeTarget = Unit((float)(Math.Max(0d, UsefulCapacityUnits) / (2d * days)));
            float sizeTarget = Unit((float)Math.Log(Math.Max(1f, biologicalMassRatio), 2d));
            EatingConditioning = Approach(EatingConditioning, intakeTarget, days);
            SizeConditioning = Approach(SizeConditioning, sizeTarget, days);
            WindowTicks = 0;
            UsefulNutrition = 0d;
            ExpenditureNutrition = 0d;
            UsefulCapacityUnits = 0d;
        }

        private static float Approach(float current, float target, double days)
        {
            current = Unit(current);
            if (target > current) return Math.Min(target, current + (float)(.1d * days));
            // A routine at or below normal expenditure gives a ten-active-day
            // half-life; it never instantly erases conditioning or stomach food.
            return target + (current - target) * (float)Math.Exp(-Math.Log(2d) * days / 10d);
        }
        private static float Unit(float x) => Finite(x) ? Math.Max(0f, Math.Min(1f, x)) : 0f;
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }
}
