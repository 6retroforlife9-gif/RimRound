using HarmonyLib;
using System.Reflection;
using System;
using RimRound.FeedingTube;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    [StaticConstructorOnStartup]
    public static class FeedOtherBootstrap
    {
        private static bool? appliedFoodNetworkV2Mode;

        static FeedOtherBootstrap()
        {
            Harmony harmony = new Harmony("RimRound.FeedOther");

            // Set the shared gate before companion patches run. The later
            // handoff synchronizes live maps after startup is complete.
            FoodNetworkPatchPolicy.LegacyPatchesEnabled =
                !FeedOtherMod.Settings.foodNetworkV2Enabled;
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                ApplyFoodNetworkPatchMode(
                    FeedOtherMod.Settings.foodNetworkV2Enabled);
            });

            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // Install beta generation probabilities before any new pawn can
            // be generated. The category finalizer itself is Harmony-patched
            // after vanilla and RimRound trait generation.
            RimRoundTraitGenerationBeta.InitializeBetaRules();

            // Vanilla uses a fixed 1.5x chew duration for every patient. Replace
            // only the untouched vanilla driver so ordinary patient/prisoner
            // feeding respects the recipient's Eating Speed and our modest
            // assisted-feeding speed bonus without clobbering another mod's
            // custom FeedPatient driver.
            if (JobDefOf.FeedPatient != null &&
                JobDefOf.FeedPatient.driverClass ==
                    typeof(JobDriver_FoodFeedPatient))
            {
                JobDefOf.FeedPatient.driverClass =
                    typeof(JobDriver_FoodFeedPatientEatingSpeed);
            }
        }

        internal static void ApplyFoodNetworkPatchMode(bool enabled)
        {
            if (appliedFoodNetworkV2Mode.HasValue &&
                appliedFoodNetworkV2Mode.Value == enabled)
            {
                return;
            }

            bool switchedFromLegacy =
                appliedFoodNetworkV2Mode.HasValue &&
                !appliedFoodNetworkV2Mode.Value && enabled;
            bool switchedToLegacy =
                (!appliedFoodNetworkV2Mode.HasValue ||
                 appliedFoodNetworkV2Mode.Value) && !enabled;

            if (enabled)
            {
                if (switchedFromLegacy)
                {
                    // The classic network may have changed tank volume while
                    // v2 was disabled. Import that live state once before the
                    // old network objects are detached.
                    FoodNetworkV2MapComponent.ImportLegacyStorageFromAllMaps();
                    FoodNetworkV2AutoFeederUtility.ImportLegacyLinksFromAllMaps();
                }

                FoodNetworkPatchPolicy.LegacyPatchesEnabled = false;
                FoodNetworkV2FaucetListerUtility.SyncAllMaps(false);
                ResetLegacyManagers();
                FoodNetworkV2MapComponent.MarkAllMapsDirty();
            }
            else
            {
                if (switchedToLegacy)
                {
                    FoodNetworkV2MapComponent.ExportSavedStorageToLegacyAllMaps();
                    FoodNetworkV2AutoFeederUtility.PrepareForLegacyModeAllMaps();
                }

                FoodNetworkPatchPolicy.LegacyPatchesEnabled = true;
                FoodNetworkV2FaucetListerUtility.SyncAllMaps(true);
                FoodNetworkV2MapComponent.MarkAllMapsDirty();
                ResetLegacyManagers();
            }

            appliedFoodNetworkV2Mode = enabled;
        }

        private static void ResetLegacyManagers()
        {
            if (Find.Maps == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                FoodTransmitter_NetManager manager =
                    FoodTransmitter_NetManager.For(map);
                if (manager != null)
                {
                    manager.ResetNetManager();
                }
            }
        }

    }
}
