using System;
using System.Collections.Generic;

namespace RimRound.FeedingTube
{
    // No game state or float projections: shared by both storage engines.
    public static class FoodServingMath
    {
        public const double StandardVolume = 0.9d;
        public const double LargeVolume = 1.8d;
        public const double FeastVolume = 3.6d;
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool Positive(double value) => value > 0d && Finite(value);
        public static bool GradeValid(int grade) => grade >= 1 && grade <= 5;
        public static int ClampGrade(int grade) => Math.Max(1, Math.Min(5, grade));
        public static int AvailableGrade(double nutrition, double volume)
        {
            if (!Positive(nutrition) || !Positive(volume)) return 0;
            return Math.Max(0, Math.Min(5, (int)Math.Floor(Math.Min(5d, nutrition / volume))));
        }
        public static bool Same(double a, double b) =>
            Finite(a) && Finite(b) && Math.Abs(a - b) <= Math.Max(Math.Abs(a), Math.Abs(b)) * 3.552713678800501e-15d;
        public static bool CanDilute(double sourceVolume, double servingVolume) =>
            Positive(sourceVolume) && Positive(servingVolume) &&
            (sourceVolume <= servingVolume || Same(sourceVolume, servingVolume));

        public static bool FitsBatchWithFinalWaste(double room, IReadOnlyList<double> servingVolumes,
            double minimumUsefulFraction = 0.75d)
        {
            if (!Finite(room) || room < 0d || servingVolumes == null || servingVolumes.Count == 0 ||
                !Positive(minimumUsefulFraction) || minimumUsefulFraction > 1d) return false;
            double previous = 0d;
            for (int i = 0; i < servingVolumes.Count; i++)
            {
                if (!Positive(servingVolumes[i])) return false;
                if (i + 1 < servingVolumes.Count) previous += servingVolumes[i];
            }
            double last = servingVolumes[servingVolumes.Count - 1];
            return previous <= room && (room - previous >= last * minimumUsefulFraction ||
                Same(room - previous, last * minimumUsefulFraction));
        }

        // Every request is planned against the remainder of the previous one.
        // Source order is chosen by its storage engine (v2 uses oldest first).
        // No stock changes occur until all requests and physical outputs pass.
        public static bool TryPlan(IReadOnlyList<FoodAmount> sources,
            IReadOnlyList<FoodAmount> servings, bool allowConcentration,
            out FoodServingDrawPlan plan)
        {
            plan = null;
            if (sources == null || servings == null || servings.Count == 0) return false;
            var n = new double[sources.Count];
            var v = new double[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                if (!sources[i].Valid) return false;
                n[i] = sources[i].Nutrition; v[i] = sources[i].Volume;
            }
            var result = new FoodServingDrawPlan(sources.Count, servings.Count);
            for (int s = 0; s < servings.Count; s++)
            {
                FoodAmount wanted = servings[s];
                if (!wanted.Valid || wanted.Nutrition == 0d || wanted.Volume == 0d) return false;
                double remaining = wanted.Nutrition, drawnN = 0d, drawnV = 0d;
                for (int i = 0; i < n.Length && remaining > 0d; i++)
                {
                    if (n[i] <= 0d || v[i] <= 0d) continue;
                    double takeN = Math.Min(remaining, n[i]);
                    double takeV = takeN == n[i] ? v[i] : v[i] * (takeN / n[i]);
                    if (!Positive(takeN) || !Positive(takeV)) return false;
                    result.Slices[s].Add(new FoodDrawSlice(i, takeN, takeV));
                    n[i] -= takeN; v[i] -= takeV;
                    drawnN += takeN; drawnV += takeV;
                    remaining = Math.Max(0d, remaining - takeN);
                }
                // The tolerance covers double summation only; it cannot turn a
                // float-short old vat into a nominally complete serving.
                if (remaining > 0d || !Same(drawnN, wanted.Nutrition) ||
                    (!allowConcentration && !CanDilute(drawnV, wanted.Volume))) return false;
                result.SourceAmounts[s] = new FoodAmount(drawnN, drawnV);
            }
            for (int i = 0; i < n.Length; i++) result.Remainders[i] = new FoodAmount(n[i], v[i]);
            plan = result;
            return true;
        }
    }

    public struct FoodAmount
    {
        public readonly double Nutrition;
        public readonly double Volume;
        public bool Valid => FoodServingMath.Finite(Nutrition) && FoodServingMath.Finite(Volume) &&
            Nutrition >= 0d && Volume >= 0d && ((Nutrition == 0d) == (Volume == 0d));
        public FoodAmount(double nutrition, double volume) { Nutrition = nutrition; Volume = volume; }
    }
    public struct FoodDrawSlice
    {
        public readonly int SourceIndex;
        public readonly double Nutrition;
        public readonly double Volume;
        public FoodDrawSlice(int sourceIndex, double nutrition, double volume)
        { SourceIndex = sourceIndex; Nutrition = nutrition; Volume = volume; }
    }
    public sealed class FoodServingDrawPlan
    {
        public FoodAmount[] Remainders { get; }
        public FoodAmount[] SourceAmounts { get; }
        public List<FoodDrawSlice>[] Slices { get; }
        internal FoodServingDrawPlan(int sources, int servings)
        {
            Remainders = new FoodAmount[sources]; SourceAmounts = new FoodAmount[servings];
            Slices = new List<FoodDrawSlice>[servings];
            for (int i = 0; i < servings; i++) Slices[i] = new List<FoodDrawSlice>();
        }
    }
}
