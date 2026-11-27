using System;

namespace RimRound.Simulation
{
    /// <summary>A fixed useful-intake allowance, independent of digestion during a sitting.</summary>
    public sealed class MealSittingBudget
    {
        public double RemainingVolume, RemainingNutrition;
        public double InitialVolume, InitialNutrition;
        public double CollectedVolume, CollectedNutrition;
        public double Tolerance;

        public void Initialize(double volume, double nutrition, double tolerance)
        {
            CollectedNutrition = CollectedVolume = 0d;
            InitialVolume = RemainingVolume = Valid(volume) ? volume : 0d;
            InitialNutrition = RemainingNutrition = Valid(nutrition) ? nutrition : 0d;
            Tolerance = Math.Min(InitialVolume, Valid(tolerance) ? tolerance : 0d);
        }

        public bool Complete => RemainingVolume <= Tolerance + 1e-7d || RemainingNutrition <= 1e-7d;
        public bool CollectedEnough => InitialVolume - CollectedVolume <= Tolerance + 1e-7d ||
            InitialNutrition - CollectedNutrition <= 1e-7d;

        // The final whole item may waste up to one quarter. A smaller remainder
        // is left for a better fitting food, rather than destroying most of a meal.
        public int CountToCollect(double nutrition, double volume, int available)
        {
            if (!Valid(nutrition) || nutrition <= 0d || !Valid(volume) || available <= 0 || CollectedEnough) return 0;
            double volumeRoom = Math.Max(0d, InitialVolume - CollectedVolume);
            double energyRoom = Math.Max(0d, InitialNutrition - CollectedNutrition);
            double units = Math.Min(energyRoom / nutrition, volume > 0d ? volumeRoom / volume : double.MaxValue);
            if (!Valid(units)) return 0;
            int whole = (int)Math.Min(available, Math.Floor(units + 1e-7d));
            if (whole < available && units - whole >= .75d - 1e-7d &&
                (volume <= 0d || volumeRoom - whole * volume > Tolerance + 1e-7d)) whole++;
            return Math.Max(0, whole);
        }

        public void Collected(double nutrition, double volume)
        {
            if (!Valid(nutrition) || !Valid(volume)) return;
            double fraction = Math.Min(1d, nutrition > 0d ? Math.Max(0d, InitialNutrition - CollectedNutrition) / nutrition : 1d);
            if (volume > 0d) fraction = Math.Min(fraction, Math.Max(0d, InitialVolume - CollectedVolume) / volume);
            CollectedNutrition += nutrition * fraction;
            CollectedVolume += volume * fraction;
        }

        public void Consumed(double nutrition, double volume)
        {
            if (!Valid(nutrition) || !Valid(volume)) return;
            RemainingNutrition = Math.Max(0d, RemainingNutrition - nutrition);
            RemainingVolume = Math.Max(0d, RemainingVolume - volume);
        }

        public static bool Valid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
    }
}
