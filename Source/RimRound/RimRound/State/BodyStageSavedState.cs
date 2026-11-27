using System;
using RimRound.Simulation;

namespace RimRound.State
{
    /// <summary>Pure operations for the three independently saved biological stage fields.</summary>
    public static class BodyStageSavedState
    {
        public static void Initialize(ref int schema, ref string policyId, ref float multiplier,
            string legacyPolicyId, float legacyMultiplier)
        {
            if (schema >= 1) return;
            // Keep an unknown raw ID. It is not replaced when the table is unavailable.
            if (policyId == null) policyId = legacyPolicyId;
            multiplier = FiniteMultiplier(legacyMultiplier);
            schema = 1;
        }

        public static float FiniteMultiplier(float multiplier)
        {
            return multiplier > 0f && !float.IsNaN(multiplier) && !float.IsInfinity(multiplier)
                ? multiplier : 1f;
        }

        public static BodyStageTransition Advance(BodyStagePolicy policy, float severity,
            bool varyingThresholds, ref float multiplier)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            var transition = policy.Evaluate(severity, FiniteMultiplier(multiplier), varyingThresholds);
            multiplier = FiniteMultiplier(transition.NextMultiplier);
            return transition;
        }
    }
}
