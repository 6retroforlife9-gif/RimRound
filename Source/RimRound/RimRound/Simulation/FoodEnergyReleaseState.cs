using System;
using System.Collections.Generic;

namespace RimRound.Simulation
{
    /// <summary>
    /// Unabsorbed energy. Ordinary food follows its share of physical digestion;
    /// each special dose follows its own clock. Advance only on an observed
    /// native food interval, never elapsed save/load or wall-clock time.
    /// </summary>
    public sealed class FoodEnergyReleaseState
    {
        public const int CurrentVersion = 1;
        public const int FantasyDoseTicks = 60000;
        public const int BalancedDoseTicks = 180000;
        public const int TransitionTicks = 180000;

        public int SchemaVersion = CurrentVersion;
        public double OrdinaryNutrition;
        public double OrdinaryVolume;
        public double OrdinaryMealGainEligible;
        public double VolumeFreeNutrition;
        public double VolumeFreeMealGainEligible;
        public double TransitionNutrition;
        public int TransitionTicksRemaining;
        public List<double> DoseNutrition = new List<double>();
        public List<int> DoseTicksRemaining = new List<int>();
        public double TimedNutrition
        {
            get { double total = 0d; if (DoseNutrition != null) foreach (double dose in DoseNutrition) total += dose; return total; }
        }

        public double UnabsorbedNutrition
        {
            get
            {
                double total = OrdinaryNutrition + VolumeFreeNutrition + TransitionNutrition;
                total += TimedNutrition;
                return total;
            }
        }

        // Existing positive rrEnergyBalance was already counted as intake.
        // Move it, do not count it again or infer another reserve from fullness.
        public bool MigrateExistingBalance(BodyEnergyState energy)
        {
            if (SchemaVersion != 0 || energy == null) return false;
            double pending = energy.PendingEnergyNutritionExact;
            if (Positive(pending))
            {
                TransitionNutrition = pending;
                TransitionTicksRemaining = TransitionTicks;
                energy.PendingEnergyNutritionExact = 0d;
            }
            SchemaVersion = CurrentVersion;
            return true;
        }

        public bool Record(double nutrition, double effectiveVolume, int durationTicks, BodyEnergyState energy, bool mealGainEligible = false)
        {
            if (energy == null || !Positive(nutrition) || !Finite(effectiveVolume) ||
                effectiveVolume < 0d || durationTicks < 0 || !Finite(UnabsorbedNutrition + nutrition) ||
                !Finite(OrdinaryVolume + effectiveVolume)) return false;
            if (!energy.RecordConsumedIntake(nutrition)) return false;
            if (durationTicks > 0)
            {
                // Equal remaining durations can share a bucket without changing
                // either dose's release rate. Older doses retain their clocks.
                int index = DoseTicksRemaining.IndexOf(durationTicks);
                if (index >= 0) DoseNutrition[index] += nutrition;
                else { DoseNutrition.Add(nutrition); DoseTicksRemaining.Add(durationTicks); }
            }
            else if (effectiveVolume > 0d)
            {
                OrdinaryNutrition += nutrition;
                OrdinaryVolume += effectiveVolume;
                if (mealGainEligible) OrdinaryMealGainEligible += nutrition;
            }
            else
            {
                // Explicit zero-volume modifiers still defer conversion until
                // the next real digestion interval. No invisible stomach bulk.
                VolumeFreeNutrition += nutrition;
                if (mealGainEligible) VolumeFreeMealGainEligible += nutrition;
            }
            return true;
        }

        public double Advance(double digestedVolume, double totalVolumeBefore, int ticks, BodyEnergyState energy)
        {
            if (ticks <= 0 || energy == null) return 0d;
            double released = 0d;
            if (Positive(digestedVolume) && Positive(totalVolumeBefore) && Positive(OrdinaryVolume))
            {
                double fraction = Math.Min(1d, digestedVolume / Math.Max(totalVolumeBefore, OrdinaryVolume));
                double credit = OrdinaryNutrition * fraction;
                double eligibleCredit = Math.Min(OrdinaryNutrition, Math.Max(0d, OrdinaryMealGainEligible)) * fraction;
                if (energy.CreditAbsorbedEnergy(credit, eligibleCredit))
                {
                    OrdinaryNutrition -= credit;
                    OrdinaryMealGainEligible = Math.Max(0d, OrdinaryMealGainEligible - eligibleCredit);
                    OrdinaryVolume *= 1d - fraction;
                    released += credit;
                }
            }
            if (energy.CreditAbsorbedEnergy(VolumeFreeNutrition, VolumeFreeMealGainEligible))
            { released += VolumeFreeNutrition; VolumeFreeNutrition = 0d; VolumeFreeMealGainEligible = 0d; }

            if (Positive(TransitionNutrition) && TransitionTicksRemaining > 0)
            {
                int elapsed = Math.Min(ticks, TransitionTicksRemaining);
                double credit = TransitionNutrition * ((double)elapsed / TransitionTicksRemaining);
                if (energy.CreditAbsorbedEnergy(credit))
                {
                    TransitionNutrition -= credit;
                    TransitionTicksRemaining -= elapsed;
                    released += credit;
                }
            }
            for (int i = DoseNutrition.Count - 1; i >= 0; i--)
            {
                int elapsed = Math.Min(ticks, DoseTicksRemaining[i]);
                double credit = DoseNutrition[i] * ((double)elapsed / DoseTicksRemaining[i]);
                if (!energy.CreditAbsorbedEnergy(credit)) continue;
                DoseNutrition[i] -= credit;
                DoseTicksRemaining[i] -= elapsed;
                released += credit;
                if (DoseTicksRemaining[i] == 0)
                { DoseTicksRemaining.RemoveAt(i); DoseNutrition.RemoveAt(i); }
            }
            return released;
        }

        public void Sanitize()
        {
            OrdinaryNutrition = Nonnegative(OrdinaryNutrition);
            OrdinaryVolume = Nonnegative(OrdinaryVolume);
            VolumeFreeNutrition = Nonnegative(VolumeFreeNutrition);
            OrdinaryMealGainEligible = Math.Min(OrdinaryNutrition, Nonnegative(OrdinaryMealGainEligible));
            VolumeFreeMealGainEligible = Math.Min(VolumeFreeNutrition, Nonnegative(VolumeFreeMealGainEligible));
            TransitionNutrition = Nonnegative(TransitionNutrition);
            if (OrdinaryNutrition > 0d && OrdinaryVolume == 0d)
            { VolumeFreeNutrition += OrdinaryNutrition; VolumeFreeMealGainEligible += OrdinaryMealGainEligible; OrdinaryNutrition = 0d; OrdinaryMealGainEligible = 0d; }
            if (OrdinaryNutrition == 0d) OrdinaryVolume = 0d;
            if (TransitionNutrition == 0d) TransitionTicksRemaining = 0;
            else if (TransitionTicksRemaining <= 0) TransitionTicksRemaining = TransitionTicks;
            if (DoseNutrition == null) DoseNutrition = new List<double>();
            if (DoseTicksRemaining == null) DoseTicksRemaining = new List<int>();
            // Missing duration is retained with a conservative 72-hour clock;
            // malformed energy cannot enter a mass or native Food calculation.
            while (DoseTicksRemaining.Count < DoseNutrition.Count) DoseTicksRemaining.Add(BalancedDoseTicks);
            while (DoseTicksRemaining.Count > DoseNutrition.Count) DoseTicksRemaining.RemoveAt(DoseTicksRemaining.Count - 1);
            for (int i = DoseNutrition.Count - 1; i >= 0; i--)
            {
                if (!Positive(DoseNutrition[i])) { DoseNutrition.RemoveAt(i); DoseTicksRemaining.RemoveAt(i); }
                else if (DoseTicksRemaining[i] <= 0) DoseTicksRemaining[i] = BalancedDoseTicks;
            }
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Positive(double value) => Finite(value) && value > 0d;
        private static double Nonnegative(double value) => Finite(value) ? Math.Max(0d, value) : 0d;
    }
}
