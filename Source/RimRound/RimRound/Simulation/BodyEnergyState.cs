using System;

namespace RimRound.Simulation
{
    /// <summary>
    /// Nutrition and body-energy accounting without game or Unity dependencies.
    /// The pawn component persists the public primitive fields with Scribe.
    /// Intake totals count ingestion; only absorbed nutrition enters the balance.
    /// </summary>
    public sealed class BodyEnergyState
    {
        public const int CurrentVersion = 1;
        public const int TicksPerDay = 60000;
        public const float DefaultMaximumBaselineFractionPerDay = 0.01f;

        public int SchemaVersion = CurrentVersion;
        public float LegacyNutritionRemaining;
        public float LegacyVolumeRemaining;
        public double PendingEnergyNutritionExact;
        public double TotalIntakeNutritionExact;
        public double TotalExpenditureNutritionExact;
        // Subset of the positive absorbed balance, never additional energy.
        public double MealGainEligibleNutrition;
        // Compatibility projections for existing consumers. Scribe persists the
        // exact owners, so repeated small releases at a body limit retain energy.
        public float PendingEnergyNutrition { get => (float)PendingEnergyNutritionExact; set => PendingEnergyNutritionExact = value; }
        public float TotalIntakeNutrition { get => (float)TotalIntakeNutritionExact; set => TotalIntakeNutritionExact = value; }
        public float TotalExpenditureNutrition { get => (float)TotalExpenditureNutritionExact; set => TotalExpenditureNutritionExact = value; }

        /// <summary>
        /// Call once for a pre-v1 save. New pawns begin at CurrentVersion.
        /// This records old stomach nutrition without crediting it yet; old food
        /// is credited only as its volume digests. A second call is a no-op.
        /// </summary>
        public bool SnapshotLegacy(float fullnessVolume, float volumePerNutrition)
        {
            if (SchemaVersion != 0)
                return false;

            LegacyVolumeRemaining = IsPositiveFinite(fullnessVolume)
                ? fullnessVolume : 0f;
            float safeRatio = IsPositiveFinite(volumePerNutrition)
                ? volumePerNutrition : 1f;
            float legacyNutrition = LegacyVolumeRemaining / safeRatio;
            LegacyNutritionRemaining = IsPositiveFinite(legacyNutrition)
                ? legacyNutrition : 0f;
            SchemaVersion = CurrentVersion;
            return true;
        }

        public bool RecordIntake(float nutrition)
        {
            if (!IsPositiveFinite(nutrition)) return false;
            double pending = PendingEnergyNutritionExact + nutrition;
            double total = TotalIntakeNutritionExact + nutrition;
            if (!IsFinite(pending) || !IsFinite(total))
                return false;
            PendingEnergyNutritionExact = pending;
            TotalIntakeNutritionExact = total;
            return true;
        }

        public bool RecordConsumedIntake(double nutrition)
        {
            if (!IsFinite(nutrition) || nutrition <= 0d || !IsFinite(TotalIntakeNutritionExact + nutrition)) return false;
            TotalIntakeNutritionExact += nutrition;
            return true;
        }

        public bool CreditAbsorbedEnergy(double nutrition, double mealGainEligible = 0d)
        {
            if (!IsFinite(nutrition) || nutrition <= 0d || !IsFinite(PendingEnergyNutritionExact + nutrition)) return false;
            double positiveAfter = Math.Max(0d, PendingEnergyNutritionExact + nutrition);
            double eligible = IsFinite(mealGainEligible) ? Math.Max(0d, Math.Min(nutrition, mealGainEligible)) : 0d;
            MealGainEligibleNutrition = Math.Min(positiveAfter, Math.Max(0d, MealGainEligibleNutrition) +
                eligible * Math.Min(1d, positiveAfter / nutrition));
            PendingEnergyNutritionExact += nutrition;
            return true;
        }

        public bool RecordExpenditure(float nutrition)
        {
            if (!IsPositiveFinite(nutrition))
                return false;
            double pending = PendingEnergyNutritionExact - nutrition;
            double total = TotalExpenditureNutritionExact + nutrition;
            if (!IsFinite(pending) || !IsFinite(total))
                return false;
            if (PendingEnergyNutritionExact > 0d)
                MealGainEligibleNutrition *= Math.Max(0d, pending) / PendingEnergyNutritionExact;
            else MealGainEligibleNutrition = 0d;
            PendingEnergyNutritionExact = pending;
            TotalExpenditureNutritionExact = total;
            return true;
        }

        /// <summary>
        /// Drain the old food's proportional share of mixed stomach volume.
        /// The returned nutrition is already recorded as intake in this ledger;
        /// the caller only adds it to the vanilla Food need, once. New meal
        /// volume has no pending nutrition and is never credited here. If an
        /// external legacy setter reduced displayed fullness, the caller can
        /// continue draining virtual old volume with totalVolumeBefore at
        /// least LegacyVolumeRemaining. This preserves the saved old reserve.
        /// </summary>
        public float DigestLegacy(float digestedVolume, float totalVolumeBefore, bool recordAsBodyIntake = true)
        {
            if (!IsPositiveFinite(digestedVolume) ||
                !IsPositiveFinite(totalVolumeBefore) ||
                !IsPositiveFinite(LegacyVolumeRemaining) ||
                !IsPositiveFinite(LegacyNutritionRemaining))
                return 0f;

            float volumeBefore = LegacyVolumeRemaining;
            float legacyShare = Math.Min(1f, volumeBefore / totalVolumeBefore);
            float legacyVolumeDigested = Math.Min(
                volumeBefore, digestedVolume * legacyShare);
            if (!IsPositiveFinite(legacyVolumeDigested))
                return 0f;

            float credited = legacyVolumeDigested >= volumeBefore ||
                digestedVolume >= totalVolumeBefore
                ? LegacyNutritionRemaining
                : LegacyNutritionRemaining * (legacyVolumeDigested / volumeBefore);
            if (!IsPositiveFinite(credited) || (recordAsBodyIntake && !RecordIntake(credited)))
                return 0f;

            LegacyVolumeRemaining = Math.Max(0f, volumeBefore - legacyVolumeDigested);
            LegacyNutritionRemaining = Math.Max(0f, LegacyNutritionRemaining - credited);
            if (LegacyVolumeRemaining <= 0f)
                LegacyNutritionRemaining = 0f;
            return credited;
        }

        /// <summary>
        /// Absorbed gain follows digestion or the dose clock without another
        /// daily cap. Loss retains its baseline rate safeguard. Preserve energy
        /// that cannot be converted at a mass limit. The baseline must be
        /// independent of RimRound's current weight or modified BodySize.
        /// </summary>
        public float ApplyEnergyToMass(
            float currentMassKg,
            float baselineMassKg,
            float nutritionPerKg,
            float gainMultiplier,
            float lossMultiplier,
            float minMassKg,
            float maxMassKg,
            int intervalTicks,
            float maximumBaselineFractionPerDay =
                DefaultMaximumBaselineFractionPerDay,
            float mealGainBonus = 0f)
        {
            if (intervalTicks <= 0 || !IsFinite(PendingEnergyNutritionExact) ||
                PendingEnergyNutritionExact == 0d || !IsFinite(currentMassKg) ||
                !IsPositiveFinite(baselineMassKg) ||
                !IsPositiveFinite(nutritionPerKg) ||
                !IsFinite(minMassKg) || !IsFinite(maxMassKg) ||
                minMassKg > maxMassKg ||
                !IsPositiveFinite(maximumBaselineFractionPerDay))
                return 0f;

            bool gaining = PendingEnergyNutritionExact > 0d;
            float multiplier = gaining ? gainMultiplier : lossMultiplier;
            double eligibleFraction = gaining && IsFinite(MealGainEligibleNutrition)
                ? Math.Min(1d, Math.Max(0d, MealGainEligibleNutrition / PendingEnergyNutritionExact)) : 0d;
            if (gaining && IsFinite(mealGainBonus) && mealGainBonus > 0f)
                multiplier *= (float)(1d + eligibleFraction * mealGainBonus);
            if (!IsPositiveFinite(multiplier))
                return 0f;

            double desiredKg = PendingEnergyNutritionExact /
                nutritionPerKg * multiplier;
            double rateLimitKg = (double)baselineMassKg *
                maximumBaselineFractionPerDay * intervalTicks / TicksPerDay;
            if (rateLimitKg <= 0d || double.IsInfinity(rateLimitKg))
                return 0f;

            double boundedKg = gaining ? desiredKg : Math.Max(-rateLimitKg, desiredKg);
            // A pre-existing out-of-range mass must not force an oversized
            // correction that consumes more energy than this interval has.
            if (gaining)
                boundedKg = Math.Min(boundedKg,
                    Math.Max(0d, (double)maxMassKg - currentMassKg));
            else
                boundedKg = Math.Max(boundedKg,
                    -Math.Max(0d, (double)currentMassKg - minMassKg));
            if ((gaining && boundedKg <= 0d) ||
                (!gaining && boundedKg >= 0d))
                return 0f;

            float massDeltaKg = (float)boundedKg;
            if (massDeltaKg == 0f || !IsFinite(massDeltaKg))
                return 0f;

            // Debit only the representable delta returned to the projection.
            // Rounding must not consume more than the actual energy reserve.
            if (Math.Abs((double)massDeltaKg) > Math.Abs(boundedKg))
                massDeltaKg = (float)(boundedKg * (1d - 1.2e-7d));
            double energyConverted = (double)massDeltaKg * nutritionPerKg / multiplier;
            double remaining = PendingEnergyNutritionExact - energyConverted;
            if (!IsFinite(remaining))
                return 0f;
            // Floating-point rounding must not turn a positive reserve negative
            // (or a deficit positive) after a complete conversion.
            if ((gaining && remaining < 0f) || (!gaining && remaining > 0f))
                remaining = 0f;
            MealGainEligibleNutrition = gaining ? Math.Max(0d, remaining) * eligibleFraction : 0d;
            PendingEnergyNutritionExact = remaining;
            return massDeltaKg;
        }

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsPositiveFinite(float value)
        {
            return IsFinite(value) && value > 0f;
        }
    }

    public enum BodyGoal
    {
        Legacy = 0,
        Maintain = 1,
        Gain = 2,
        Lose = 3,
        Recovery = 4,
        Auto = 5,
        ForceGain = 6
    }
}
