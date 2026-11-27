using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RimRound.Simulation
{
    /// <summary>Biological compatibility data; no artwork or live Pawn state.</summary>
    public sealed class BodyStageRow
    {
        public float MaximumSeverity { get; }
        public string StageKey { get; }
        public float NextMultiplier { get; }

        public BodyStageRow(float maximumSeverity, string stageKey, float nextMultiplier)
        {
            if (!BodyStagePolicy.PositiveFinite(maximumSeverity))
                throw new ArgumentOutOfRangeException(nameof(maximumSeverity));
            if (string.IsNullOrWhiteSpace(stageKey)) throw new ArgumentException("Stage key is required.", nameof(stageKey));
            if (!BodyStagePolicy.PositiveFinite(nextMultiplier))
                throw new ArgumentOutOfRangeException(nameof(nextMultiplier));
            MaximumSeverity = maximumSeverity;
            StageKey = stageKey;
            NextMultiplier = nextMultiplier;
        }
    }

    public readonly struct BodyStageTransition
    {
        public int RowIndex { get; }
        public string StageKey { get; }
        public float NextMultiplier { get; }

        internal BodyStageTransition(int rowIndex, BodyStageRow row)
        {
            RowIndex = rowIndex;
            StageKey = row.StageKey;
            NextMultiplier = row.NextMultiplier;
        }
    }

    /// <summary>
    /// Immutable legacy transition table. The pawn component owns the saved
    /// multiplier and applies a returned transition only at an explicit update.
    /// A table is chosen at conversion/generation, never by a later art choice.
    /// </summary>
    public sealed class BodyStagePolicy
    {
        public string Id { get; }
        public ReadOnlyCollection<BodyStageRow> Rows { get; }

        public BodyStagePolicy(string id, IEnumerable<BodyStageRow> rows)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Policy ID is required.", nameof(id));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var copied = new List<BodyStageRow>();
            float previous = 0f;
            foreach (BodyStageRow row in rows)
            {
                if (row == null || row.MaximumSeverity <= previous)
                    throw new ArgumentException("Positive stage thresholds must strictly increase.", nameof(rows));
                copied.Add(row);
                previous = row.MaximumSeverity;
            }
            if (copied.Count == 0) throw new ArgumentException("Policy has no stages.", nameof(rows));
            Id = id;
            Rows = copied.AsReadOnly();
        }

        public BodyStageTransition Evaluate(float severity, float savedMultiplier, bool varyingThresholds)
        {
            if (float.IsNaN(severity) || float.IsInfinity(severity) || severity < 0f)
                throw new ArgumentOutOfRangeException(nameof(severity));
            float multiplier = EffectiveMultiplier(savedMultiplier, varyingThresholds);
            int index = 0;
            // Mono preserves extra precision for the original float product in
            // comparisons. An explicit double product also preserves its exact
            // boundary behavior in the pure .NET tests (A01 native baseline).
            while (index < Rows.Count - 1 && severity > (double)Rows[index].MaximumSeverity * multiplier)
                index++;
            return new BodyStageTransition(index, Rows[index]);
        }

        public static float EffectiveMultiplier(float savedMultiplier, bool varyingThresholds)
        {
            return varyingThresholds && PositiveFinite(savedMultiplier) ? savedMultiplier : 1f;
        }

        public static bool IsAbove(float severity, float threshold, float savedMultiplier, bool varyingThresholds)
        {
            return severity > (double)threshold * EffectiveMultiplier(savedMultiplier, varyingThresholds);
        }

        public static int WeightStageIndex(float severity, IList<float> minimumSeverities,
            float savedMultiplier, bool varyingThresholds)
        {
            if (minimumSeverities == null || minimumSeverities.Count == 0) return 0;
            float multiplier = EffectiveMultiplier(savedMultiplier, varyingThresholds);
            for (int i = minimumSeverities.Count - 1; i >= 0; i--)
                if (severity >= (double)minimumSeverities[i] * multiplier) return i;
            return 0;
        }

        internal static bool PositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
