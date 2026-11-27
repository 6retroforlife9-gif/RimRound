using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.FeedingTube;

namespace RimRound.FeedOther
{
    /// <summary>Pure FIFO serving feasibility and conserved route-capacity bands.</summary>
    public static class LiquidReserveAllocation
    {
        public static double PrefixCapacity(IReadOnlyList<FoodAmount> ordered, FoodAmount serving)
        {
            if (ordered == null || !serving.Valid || serving.Nutrition <= 0d || serving.Volume <= 0d ||
                ordered.Any(a => !a.Valid)) return 0d;
            var remaining = ordered.ToArray();
            double capacity = 0d, total = remaining.Sum(a => a.Nutrition);
            if (!FoodServingMath.Finite(total)) return 0d;
            int cursor = 0;
            while (cursor < remaining.Length)
            {
                while (cursor < remaining.Length && remaining[cursor].Nutrition <= 0d) cursor++;
                if (cursor >= remaining.Length) break;
                var first = remaining[cursor];
                double count = Math.Floor(first.Nutrition / serving.Nutrition);
                if (count >= 1d)
                {
                    // Skip a uniform run in O(1), not one loop per stored meal.
                    double perVolume = first.Volume * (serving.Nutrition / first.Nutrition);
                    if (!FoodServingMath.CanDilute(perVolume, serving.Volume)) break;
                    double used = Math.Min(first.Nutrition, count * serving.Nutrition);
                    double volume = used == first.Nutrition ? first.Volume : first.Volume * (used / first.Nutrition);
                    remaining[cursor] = new FoodAmount(first.Nutrition - used, first.Volume - volume);
                    capacity += used;
                    continue;
                }
                FoodServingDrawPlan plan;
                // A serving straddling batches must pass the same no-concentration
                // calculation as real collection; an unusable front batch cannot be skipped.
                if (!FoodServingMath.TryPlan(remaining, new[] { serving }, false, out plan)) break;
                remaining = plan.Remainders;
                capacity += serving.Nutrition;
            }
            return Math.Min(total, capacity);
        }

        public sealed class Band
        {
            public double Nutrition { get; internal set; }
            public double UpperBound { get; internal set; }
        }

        public static IReadOnlyList<Band> Bands(double nutrition, IEnumerable<double> routeCapacities)
        {
            if (!FoodServingMath.Positive(nutrition)) return new Band[0];
            var bounds = (routeCapacities ?? Enumerable.Empty<double>())
                .Where(FoodServingMath.Positive).Select(n => Math.Min(nutrition, n))
                .Concat(new[] { nutrition }).Distinct().OrderBy(n => n);
            double previous = 0d;
            var result = new List<Band>();
            foreach (double upper in bounds)
            {
                result.Add(new Band { Nutrition = upper - previous, UpperBound = upper });
                previous = upper;
            }
            return result.AsReadOnly();
        }
    }
}
