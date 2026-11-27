using System;
using RimRound.Comps;
using RimRound.Simulation;
using Verse;

namespace RimRound.Utilities
{
    /// <summary>The adapter owns its encounters, including save continuity and duplicate settlement.</summary>
    public static class ExternalNutrition
    {
        public const int ApiVersion = 1;
        private static Func<Pawn, float> passengerVolumeProvider;

        // Register an accessor, never an individual game's tracker. Passing null removes it.
        public static void SetPassengerVolumeProvider(Func<Pawn, float> provider)
            => passengerVolumeProvider = provider;

        internal static bool HasPassengerVolumeProvider => passengerVolumeProvider != null;

        public static bool IsEligible(Pawn pawn)
            => pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel == true;

        public static float SoftCapacity(Pawn pawn)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body?.UsesBodyModel == true ? body.SoftLimit : 0f;
        }

        internal static float PassengerVolume(FullnessAndDietStats_ThingComp body)
        {
            var provider = passengerVolumeProvider;
            if (provider == null || !body.UsesBodyModel) return 0f;
            float volume = provider((Pawn)body.parent);
            return BodyEnergyState.IsFinite(volume) && volume > 0f ? volume : 0f;
        }

        // A true result commits both Food and body energy even when Food is already full.
        // The caller must retire that amount from its own saved entitlement exactly once.
        public static bool TryCreditDigestion(Pawn pawn, float amount)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body != null && body.TryCreditExternalDigestion(amount);
        }

        // The caller already debited Food and supplies the observed change, after clamping.
        public static void RecordFoodExpense(Pawn pawn, float amount)
            => pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.RecordNativeResourceExpenditure(amount);
    }
}
