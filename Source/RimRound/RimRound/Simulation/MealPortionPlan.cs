using System;

namespace RimRound.Simulation
{
    /// <summary>
    /// Partition one represented, uniform food serving. This is a calculation,
    /// not an ingestion transaction: callers must revalidate before removing food.
    /// Volume is the serving's represented volume, before recipient modifiers.
    /// Drugs and other atomic effects must use their separate dose authorization.
    /// </summary>
    public struct MealPortionPlan
    {
        public double UsefulNutrition { get; private set; }
        public double UsefulVolume { get; private set; }
        public double WastedNutrition { get; private set; }
        public double WastedVolume { get; private set; }
        public double UsefulFraction { get; private set; }

        // Select only the whole units needed to supply the useful request.
        // This avoids discarding a large raw stack merely because hunger wants
        // more energy than the recipient's permitted physical room can hold.
        public static bool TryCreateForItems(double nutritionPerItem, double volumePerItem,
            double nutritionBudget, double volumeBudget, int available, int maximum,
            out int count, out MealPortionPlan plan)
        {
            count = 0;
            plan = default(MealPortionPlan);
            if (available <= 0) return false;
            int ceiling = maximum > 0 ? Math.Min(available, maximum) : available;
            MealPortionPlan preview;
            if (!TryCreate(nutritionPerItem * ceiling, volumePerItem * ceiling,
                nutritionBudget, volumeBudget, out preview)) return false;
            count = (int)Math.Min(ceiling, Math.Max(1d,
                Math.Ceiling(preview.UsefulNutrition / nutritionPerItem - 1e-9d)));
            if (!TryCreate(nutritionPerItem * count, volumePerItem * count,
                nutritionBudget, volumeBudget, out plan)) { count = 0; return false; }
            return true;
        }

        /// <summary>
        /// A positive request selects a serving; physical room then limits how
        /// much of that serving is eaten. Calories beyond hunger are retained.
        /// Metered feeds use TryCreate instead, keeping their nutrition budget.
        /// </summary>
        public static bool TryCreateServing(double nutrition, double volume,
            double requestedNutrition, double volumeBudget, out MealPortionPlan plan)
        {
            plan = default(MealPortionPlan);
            return Finite(requestedNutrition) && requestedNutrition > 0d &&
                TryCreate(nutrition, volume, nutrition, volumeBudget, out plan);
        }

        /// <summary>
        /// A false result means consume nothing, not discard an entire meal.
        /// Useful nutrition may exceed the Food bar deficit when a goal permits it.
        /// The caller supplies a finite live budget, not the clamped Food bar.
        /// </summary>
        public static bool TryCreate(double nutrition, double volume,
            double nutritionBudget, double volumeBudget, out MealPortionPlan plan)
        {
            plan = default(MealPortionPlan);
            if (!Finite(nutrition) || nutrition <= 0d ||
                !Finite(volume) || volume < 0d ||
                !Finite(nutritionBudget) || nutritionBudget <= 0d ||
                !Finite(volumeBudget) || volumeBudget < 0d)
                return false;

            double fraction = Math.Min(1d, nutritionBudget / nutrition);
            if (volume > 0d) fraction = Math.Min(fraction, volumeBudget / volume);
            if (fraction <= 0d) return false;

            double usefulNutrition = nutrition * fraction;
            double usefulVolume = volume * fraction;
            if (usefulNutrition <= 0d) return false;
            plan = new MealPortionPlan {
                UsefulFraction = fraction,
                UsefulNutrition = usefulNutrition,
                UsefulVolume = usefulVolume,
                WastedNutrition = nutrition - usefulNutrition,
                WastedVolume = volume - usefulVolume
            };
            return true;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
