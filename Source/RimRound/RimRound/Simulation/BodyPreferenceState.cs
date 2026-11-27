using System;

namespace RimRound.Simulation
{
    public enum BodyPreferenceDescriptionFamily
    {
        General,
        Lean,
        Fat
    }

    /// <summary>
    /// Saved personal wishes, independent of mass, assignment, art and religion.
    /// Ratios use the fixed biological race baseline, never mutable BodySize.
    /// The Pawn adapter owns lifecycle and Scribe; reads have no side effects.
    /// </summary>
    public sealed class BodyPreferenceState
    {
        public const int CurrentVersion = 1;
        public const float MinimumRatio = 0.8f;
        public const float MaximumRatio = 10000f;
        public int SchemaVersion;
        public int SeedOpinion;
        public float LowerRatio;
        public float UpperRatio;
        public float Conviction;

        // Ordered compatibility seeds, corresponding to WeightOpinion 1..9.
        // These are biological ratios, not the legacy thought/art stage indices.
        private static readonly float[] LowerSeeds = { .8f, .9f, 1f, 1.5f, 2f, 4f, 8f, 16f, 32f };
        private static readonly float[] UpperSeeds = { 1f, 1.3f, 1.5f, 2.25f, 4f, 8f, 16f, 32f, 64f };

        public bool IsUsable => SchemaVersion == CurrentVersion && ValidRange(LowerRatio, UpperRatio) &&
            Finite(Conviction) && Conviction >= 0f && Conviction <= 1f;

        public bool TryInitialize(int seedOpinion, float conviction)
        {
            // A future schema or unknown old opinion must not be rewritten.
            if (SchemaVersion != 0 || seedOpinion < 1 || seedOpinion > LowerSeeds.Length ||
                !Finite(conviction) || conviction < 0f || conviction > 1f) return false;
            SeedOpinion = seedOpinion;
            LowerRatio = LowerSeeds[seedOpinion - 1];
            UpperRatio = UpperSeeds[seedOpinion - 1];
            Conviction = conviction;
            SchemaVersion = CurrentVersion;
            return true;
        }

        public bool TrySetRange(float lower, float upper, float conviction)
        {
            if (!IsUsable || !ValidRange(lower, upper) || !Finite(conviction) ||
                conviction < 0f || conviction > 1f) return false;
            LowerRatio = lower;
            UpperRatio = upper;
            Conviction = conviction;
            return true;
        }

        public bool TrySetOpinion(int opinion)
        {
            if (!IsUsable || opinion < 1 || opinion > LowerSeeds.Length) return false;
            return TrySetRange(LowerSeeds[opinion - 1], UpperSeeds[opinion - 1], Conviction);
        }

        public int ProjectedOpinion
        {
            get
            {
                if (!IsUsable) return SeedOpinion;
                // Log distance treats equal proportional changes equally at
                // ordinary and very large sizes. Stable tie: lower opinion.
                double center = Math.Log((double)LowerRatio * UpperRatio) * .5d;
                double nearest = double.MaxValue;
                int result = 1;
                for (int i = 0; i < LowerSeeds.Length; i++)
                {
                    double distance = Math.Abs(center - Math.Log((double)LowerSeeds[i] * UpperSeeds[i]) * .5d);
                    if (distance < nearest) { nearest = distance; result = i + 1; }
                }
                return result;
            }
        }

        public bool TryGetRangeKg(float baselineKg, out float lower, out float upper)
        {
            lower = upper = 0f;
            if (!IsUsable || !Finite(baselineKg) || baselineKg <= 0f) return false;
            float a = LowerRatio * baselineKg, b = UpperRatio * baselineKg;
            if (!Finite(a) || !Finite(b) || a <= 0f) return false;
            lower = a;
            upper = b;
            return true;
        }

        // Stage order matches RR_BodyPreference. Conviction measures resistance
        // to changing a preference; it does not amplify a pawn's mood.
        public static int MoodStageIndex(float massKg, float lowerKg, float upperKg)
        {
            if (!Finite(massKg) || !Finite(lowerKg) || !Finite(upperKg) ||
                massKg <= 0f || lowerKg <= 0f || upperKg < lowerKg) return -1;
            if (massKg >= lowerKg && massKg <= upperKg) return 3;

            bool below = massKg < lowerKg;
            double nearestEdge = below ? lowerKg : upperKg;
            double distance = Math.Abs(massKg - nearestEdge) / nearestEdge;
            int severity = distance >= .30d ? 3 : distance >= .10d ? 2 : 1;
            return below ? 3 - severity : 3 + severity;
        }

        public static BodyPreferenceDescriptionFamily DescriptionFamilyForRange(
            float lowerKg, float upperKg, float firstNonLeanKg, float firstFatKg)
        {
            if (!Finite(lowerKg) || !Finite(upperKg) || !Finite(firstNonLeanKg) || !Finite(firstFatKg) ||
                lowerKg <= 0f || upperKg < lowerKg || firstNonLeanKg <= 0f || firstFatKg <= firstNonLeanKg)
                return BodyPreferenceDescriptionFamily.General;
            if (upperKg < firstNonLeanKg) return BodyPreferenceDescriptionFamily.Lean;
            if (lowerKg >= firstFatKg) return BodyPreferenceDescriptionFamily.Fat;
            return BodyPreferenceDescriptionFamily.General;
        }

        public static bool ValidRange(float lower, float upper) => Finite(lower) && Finite(upper) &&
            lower >= MinimumRatio && upper >= lower && upper <= MaximumRatio;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
