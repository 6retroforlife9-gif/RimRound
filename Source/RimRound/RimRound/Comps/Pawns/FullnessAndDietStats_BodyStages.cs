using System;
using RimRound.Simulation;
using RimRound.State;
using Verse;

namespace RimRound.Comps
{
    public partial class FullnessAndDietStats_ThingComp
    {
        private int rrBodyStageSchema;
        private string rrBodyStagePolicyId;
        private float rrBodyStageMultiplier = 1f;
        private string rrBodyPresentationStageKey;

        public bool BodyStageStateInitialized => rrBodyStageSchema >= 1;
        public string BodyStagePolicyId => rrBodyStagePolicyId;
        public float BodyStageMultiplier => BodyStageSavedState.FiniteMultiplier(rrBodyStageMultiplier);
        public string BodyPresentationStageKey => rrBodyPresentationStageKey;

        private void ExposeBodyStageSavedState()
        {
            Scribe_Values.Look(ref rrBodyStageSchema, "rrBodyStageSchema", 0);
            Scribe_Values.Look(ref rrBodyStagePolicyId, "rrBodyStagePolicyId", null);
            Scribe_Values.Look(ref rrBodyStageMultiplier, "rrBodyStageMultiplier", 1f);
            Scribe_Values.Look(ref rrBodyPresentationStageKey, "rrBodyPresentationStageKey", null);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                rrBodyStageMultiplier = BodyStageSavedState.FiniteMultiplier(rrBodyStageMultiplier);
        }

        /// <summary>Call once at legacy conversion or generation, before any art selection.</summary>
        public void InitializeBodyStageState(string policyId, float initialMultiplier)
        {
            BodyStageSavedState.Initialize(ref rrBodyStageSchema, ref rrBodyStagePolicyId,
                ref rrBodyStageMultiplier, policyId, initialMultiplier);
        }

        /// <summary>Advance only at the existing biological update cadence.</summary>
        public BodyStageTransition AdvanceBodyStageState(BodyStagePolicy policy, float severity,
            bool varyingThresholds)
        {
            if (!BodyStageStateInitialized)
                throw new InvalidOperationException("Body stage state has not been initialized.");
            var transition = BodyStageSavedState.Advance(policy, severity, varyingThresholds,
                ref rrBodyStageMultiplier);
            rrBodyPresentationStageKey = transition.StageKey;
            return transition;
        }
    }
}
