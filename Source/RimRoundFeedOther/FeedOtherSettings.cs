using UnityEngine;
using Verse;

namespace RimRound.FeedOther
{
    public enum FeedOtherSettingsPage
    {
        Feeding,
        Cooldowns,
        Social,
        PawnBehaviour,
        Prisoners,
        WorldGeneration,
        FoodNetwork
    }

    public sealed class FeedOtherSettings : ModSettings
    {
        public const float DefaultFeedingTargetPercent = 70f;
        public const float MinimumFeedingTargetPercent = 60f;
        public const float MaximumFeedingTargetPercent = 90f;

        public const float DefaultStartingFullnessPercent = 50f;
        public const float MinimumStartingFullnessPercent = 5f;
        public const float MaximumStartingFullnessPercent = 89f;

        public bool autonomousSharedMealsEnabled = true;
        public bool autonomousOneWayFeedingEnabled = true;
        public bool manualPartnerFeedingEnabled = true;
        public bool bedsideFeedingEnabled = true;
        public float startingFullnessPercent = DefaultStartingFullnessPercent;
        public float feedingTargetPercent = DefaultFeedingTargetPercent;
        public float maximumSessionHours = 3f;
        public float autonomousFrequencyMultiplier = 1f;

        public float participantCooldownHours = 6f;
        public float oneWayFeederCooldownHours = 3f;
        public float pairCooldownHours = 6f;
        public bool likeOrHigherCooldownBypass = true;
        public bool manualOrdersIgnoreCooldowns = true;

        public bool feedingDialogueEnabled = true;
        public bool postMealSocialEnabled = true;
        public float postMealRecreationTargetPercent = 95f;
        public float maximumPostMealChatHours = 1f;
        public bool socialOpinionMemoryEnabled = true;
        public bool positiveVeryFullMoodEnabled = true;
        public bool negativeVeryFullMoodEnabled = true;
        public bool afterMealMoodEnabled = true;

        public bool idleUnderweightEatingEnabled = true;
        public float idleEatingTriggerPercent = 25f;
        public float idleMinimumDelayMinutes = 7.2f;
        public float idleMaximumDelayMinutes = 21.6f;
        public bool automaticMilkExpressionEnabled = true;
        public bool weightOpinionPersuasionEnabled = true;
        public float persuasionCooldownDays = 1f;
        public float persuasionBaseChancePercent = 20f;
        public float persuasionPerSocialLevelPercent = 6.5f;
        public float persuasionOpinionInfluencePercent = 15f;
        public float persuasionRomanticBonusPercent = 15f;
        public float persuasionFamilyBonusPercent = 8f;
        public float persuasionMinimumChancePercent = 5f;
        public float persuasionMaximumChancePercent = 95f;

        public bool prisonerFeedingOverhaulEnabled = true;
        public float prisonerMealDeliveryCooldownHours = 2f;
        public bool prisonerFattenEnabled = true;
        public float prisonerFattenTargetPercent = 80f;
        public float prisonerFattenResumePercent = 10f;

        public bool balancedWeightOpinionDistributionEnabled = true;
        public bool guaranteedEatingStyleTraitsEnabled = true;
        public bool guaranteedElasticityTraitsEnabled = true;
        public bool repairExistingTraitCategoriesEnabled = true;
        public bool gluttoniumVeinsEnabled = true;
        public float gluttoniumVeinsPer10kCells = 1.2f;
        public bool orbitalMovementReliefEnabled = true;
        public float orbitalMovementReliefPercent = 75f;

        public bool foodNetworkV2Enabled = true;
        public bool processPreparedMealsInFoodProcessor = true;
        public float foodProcessorNutritionPerCycle = 0.1f;
        public float foodDispenserServingNutrition = 0.1f;
        public float distillerNutritionPerCycle = 0.1f;
        public float autoFeederCheckSeconds = 3.33f;
        public float autoFeederGainFullnessPercentPerPulse = 10f;
        public float autoFeederMaximumTargetPercent = 90f;
        public float advancedAutoFeederRange = 9f;
        public float advancedAutoFeederBedLimit = 4f;
        public bool showFoodPipeOverlayWhenSelected = true;
        public bool alwaysShowFoodPipeOverlay = false;

        public float FeedingTargetFraction
        {
            get { return feedingTargetPercent / 100f; }
        }

        public int FeedingTargetPercentRounded
        {
            get { return Mathf.RoundToInt(feedingTargetPercent); }
        }

        public float StartingFullnessFraction
        {
            get { return startingFullnessPercent / 100f; }
        }

        public int MaximumSessionTicks
        {
            get { return HoursToTicks(maximumSessionHours); }
        }

        public int ParticipantCooldownTicks
        {
            get { return HoursToTicks(participantCooldownHours); }
        }

        public int OneWayFeederCooldownTicks
        {
            get { return HoursToTicks(oneWayFeederCooldownHours); }
        }

        public int PairCooldownTicks
        {
            get { return HoursToTicks(pairCooldownHours); }
        }

        public int PrisonerMealDeliveryCooldownTicks
        {
            get { return HoursToTicks(prisonerMealDeliveryCooldownHours); }
        }

        public float PostMealRecreationTargetFraction
        {
            get { return postMealRecreationTargetPercent / 100f; }
        }

        public int MaximumPostMealChatTicks
        {
            get { return HoursToTicks(maximumPostMealChatHours); }
        }

        public float IdleEatingTriggerFraction
        {
            get { return idleEatingTriggerPercent / 100f; }
        }

        public int IdleMinimumDelayTicks
        {
            get { return MinutesToTicks(idleMinimumDelayMinutes); }
        }

        public int IdleMaximumDelayTicks
        {
            get { return MinutesToTicks(idleMaximumDelayMinutes); }
        }

        public int PersuasionCooldownTicks
        {
            get { return Mathf.Max(1, Mathf.RoundToInt(persuasionCooldownDays * 60000f)); }
        }

        public float PersuasionBaseChance
        {
            get { return persuasionBaseChancePercent / 100f; }
        }

        public float PersuasionPerSocialLevelChance
        {
            get { return persuasionPerSocialLevelPercent / 100f; }
        }

        public float PersuasionOpinionChancePerPoint
        {
            get { return persuasionOpinionInfluencePercent / 10000f; }
        }

        public float PersuasionRomanticBonus
        {
            get { return persuasionRomanticBonusPercent / 100f; }
        }

        public float PersuasionFamilyBonus
        {
            get { return persuasionFamilyBonusPercent / 100f; }
        }

        public float PersuasionMinimumChance
        {
            get { return persuasionMinimumChancePercent / 100f; }
        }

        public float PersuasionMaximumChance
        {
            get { return persuasionMaximumChancePercent / 100f; }
        }

        public float PrisonerFattenTargetFraction
        {
            get { return prisonerFattenTargetPercent / 100f; }
        }

        public float PrisonerFattenResumeFraction
        {
            get { return prisonerFattenResumePercent / 100f; }
        }

        public float OrbitalMovementPenaltyFactor
        {
            get
            {
                return orbitalMovementReliefEnabled
                    ? 1f - orbitalMovementReliefPercent / 100f
                    : 1f;
            }
        }

        public int AutoFeederCheckTicks
        {
            get
            {
                return Mathf.Max(30, Mathf.RoundToInt(autoFeederCheckSeconds * 60f));
            }
        }

        public int AdvancedAutoFeederBedLimitRounded
        {
            get { return Mathf.RoundToInt(advancedAutoFeederBedLimit); }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref autonomousSharedMealsEnabled, "autonomousSharedMealsEnabled", true);
            Scribe_Values.Look(ref autonomousOneWayFeedingEnabled, "autonomousOneWayFeedingEnabled", true);
            Scribe_Values.Look(ref manualPartnerFeedingEnabled, "manualPartnerFeedingEnabled", true);
            Scribe_Values.Look(ref bedsideFeedingEnabled, "bedsideFeedingEnabled", true);
            Scribe_Values.Look(ref startingFullnessPercent, "startingFullnessPercent", DefaultStartingFullnessPercent);
            Scribe_Values.Look(ref feedingTargetPercent, "feedingTargetPercent", DefaultFeedingTargetPercent);
            Scribe_Values.Look(ref maximumSessionHours, "maximumSessionHours", 3f);
            Scribe_Values.Look(ref autonomousFrequencyMultiplier, "autonomousFrequencyMultiplier", 1f);

            Scribe_Values.Look(ref participantCooldownHours, "participantCooldownHours", 6f);
            Scribe_Values.Look(ref oneWayFeederCooldownHours, "oneWayFeederCooldownHours", 3f);
            Scribe_Values.Look(ref pairCooldownHours, "pairCooldownHours", 6f);
            Scribe_Values.Look(ref likeOrHigherCooldownBypass, "likeOrHigherCooldownBypass", true);
            Scribe_Values.Look(ref manualOrdersIgnoreCooldowns, "manualOrdersIgnoreCooldowns", true);

            Scribe_Values.Look(ref feedingDialogueEnabled, "feedingDialogueEnabled", true);
            Scribe_Values.Look(ref postMealSocialEnabled, "postMealSocialEnabled", true);
            Scribe_Values.Look(ref postMealRecreationTargetPercent, "postMealRecreationTargetPercent", 95f);
            Scribe_Values.Look(ref maximumPostMealChatHours, "maximumPostMealChatHours", 1f);
            Scribe_Values.Look(ref socialOpinionMemoryEnabled, "socialOpinionMemoryEnabled", true);
            Scribe_Values.Look(ref positiveVeryFullMoodEnabled, "positiveVeryFullMoodEnabled", true);
            Scribe_Values.Look(ref negativeVeryFullMoodEnabled, "negativeVeryFullMoodEnabled", true);
            Scribe_Values.Look(ref afterMealMoodEnabled, "afterMealMoodEnabled", true);

            Scribe_Values.Look(ref idleUnderweightEatingEnabled, "idleUnderweightEatingEnabled", true);
            Scribe_Values.Look(ref idleEatingTriggerPercent, "idleEatingTriggerPercent", 25f);
            Scribe_Values.Look(ref idleMinimumDelayMinutes, "idleMinimumDelayMinutes", 7.2f);
            Scribe_Values.Look(ref idleMaximumDelayMinutes, "idleMaximumDelayMinutes", 21.6f);
            Scribe_Values.Look(ref automaticMilkExpressionEnabled, "automaticMilkExpressionEnabled", true);
            Scribe_Values.Look(ref weightOpinionPersuasionEnabled, "weightOpinionPersuasionEnabled", true);
            Scribe_Values.Look(ref persuasionCooldownDays, "persuasionCooldownDays", 1f);
            Scribe_Values.Look(ref persuasionBaseChancePercent, "persuasionBaseChancePercent", 20f);
            Scribe_Values.Look(ref persuasionPerSocialLevelPercent, "persuasionPerSocialLevelPercent", 6.5f);
            Scribe_Values.Look(ref persuasionOpinionInfluencePercent, "persuasionOpinionInfluencePercent", 15f);
            Scribe_Values.Look(ref persuasionRomanticBonusPercent, "persuasionRomanticBonusPercent", 15f);
            Scribe_Values.Look(ref persuasionFamilyBonusPercent, "persuasionFamilyBonusPercent", 8f);
            Scribe_Values.Look(ref persuasionMinimumChancePercent, "persuasionMinimumChancePercent", 5f);
            Scribe_Values.Look(ref persuasionMaximumChancePercent, "persuasionMaximumChancePercent", 95f);

            Scribe_Values.Look(ref prisonerFeedingOverhaulEnabled, "prisonerFeedingOverhaulEnabled", true);
            Scribe_Values.Look(ref prisonerMealDeliveryCooldownHours, "prisonerMealDeliveryCooldownHours", 2f);
            Scribe_Values.Look(ref prisonerFattenEnabled, "prisonerFattenEnabled", true);
            Scribe_Values.Look(ref prisonerFattenTargetPercent, "prisonerFattenTargetPercent", 80f);
            Scribe_Values.Look(ref prisonerFattenResumePercent, "prisonerFattenResumePercent", 10f);

            Scribe_Values.Look(ref balancedWeightOpinionDistributionEnabled, "balancedWeightOpinionDistributionEnabled", true);
            Scribe_Values.Look(ref guaranteedEatingStyleTraitsEnabled, "guaranteedEatingStyleTraitsEnabled", true);
            Scribe_Values.Look(ref guaranteedElasticityTraitsEnabled, "guaranteedElasticityTraitsEnabled", true);
            Scribe_Values.Look(ref repairExistingTraitCategoriesEnabled, "repairExistingTraitCategoriesEnabled", true);
            Scribe_Values.Look(ref gluttoniumVeinsEnabled, "gluttoniumVeinsEnabled", true);
            Scribe_Values.Look(ref gluttoniumVeinsPer10kCells, "gluttoniumVeinsPer10kCells", 1.2f);
            Scribe_Values.Look(ref orbitalMovementReliefEnabled, "orbitalMovementReliefEnabled", true);
            Scribe_Values.Look(ref orbitalMovementReliefPercent, "orbitalMovementReliefPercent", 75f);

            Scribe_Values.Look(ref foodNetworkV2Enabled, "foodNetworkV2Enabled", true);
            Scribe_Values.Look(ref processPreparedMealsInFoodProcessor, "processPreparedMealsInFoodProcessor", true);
            Scribe_Values.Look(ref foodProcessorNutritionPerCycle, "foodProcessorNutritionPerCycle", 0.1f);
            Scribe_Values.Look(ref foodDispenserServingNutrition, "foodDispenserServingNutrition", 0.1f);
            Scribe_Values.Look(ref distillerNutritionPerCycle, "distillerNutritionPerCycle", 0.1f);
            Scribe_Values.Look(ref autoFeederCheckSeconds, "autoFeederCheckSeconds", 3.33f);
            Scribe_Values.Look(ref autoFeederGainFullnessPercentPerPulse,
                "autoFeederGainFullnessPercentPerPulse", 10f);
            Scribe_Values.Look(ref autoFeederMaximumTargetPercent, "autoFeederMaximumTargetPercent", 90f);
            Scribe_Values.Look(ref advancedAutoFeederRange, "advancedAutoFeederRange", 9f);
            Scribe_Values.Look(ref advancedAutoFeederBedLimit, "advancedAutoFeederBedLimit", 4f);
            Scribe_Values.Look(ref showFoodPipeOverlayWhenSelected, "showFoodPipeOverlayWhenSelected", true);
            Scribe_Values.Look(ref alwaysShowFoodPipeOverlay, "alwaysShowFoodPipeOverlay", false);

            base.ExposeData();
            Validate();
        }

        public bool Validate()
        {
            bool changed = false;
            changed |= ClampRounded(ref feedingTargetPercent, MinimumFeedingTargetPercent, MaximumFeedingTargetPercent, 1f);
            float maximumStart = Mathf.Min(MaximumStartingFullnessPercent, feedingTargetPercent - 1f);
            changed |= ClampRounded(ref startingFullnessPercent, MinimumStartingFullnessPercent, maximumStart, 1f);
            changed |= ClampRounded(ref maximumSessionHours, 0.5f, 12f, 0.5f);
            changed |= ClampRounded(ref autonomousFrequencyMultiplier, 0.25f, 3f, 0.25f);

            changed |= ClampRounded(ref participantCooldownHours, 0f, 24f, 0.5f);
            changed |= ClampRounded(ref oneWayFeederCooldownHours, 0f, 24f, 0.5f);
            changed |= ClampRounded(ref pairCooldownHours, 0f, 24f, 0.5f);

            changed |= ClampRounded(ref postMealRecreationTargetPercent, 50f, 100f, 1f);
            changed |= ClampRounded(ref maximumPostMealChatHours, 0.25f, 4f, 0.25f);

            changed |= ClampRounded(ref idleEatingTriggerPercent, 5f, 60f, 1f);
            changed |= ClampRounded(ref idleMinimumDelayMinutes, 1f, 120f, 0.1f);
            changed |= ClampRounded(ref idleMaximumDelayMinutes, idleMinimumDelayMinutes, 120f, 0.1f);
            changed |= ClampRounded(ref persuasionCooldownDays, 0.25f, 10f, 0.25f);
            changed |= ClampRounded(ref persuasionBaseChancePercent, 0f, 100f, 1f);
            changed |= ClampRounded(ref persuasionPerSocialLevelPercent, 0f, 20f, 0.5f);
            changed |= ClampRounded(ref persuasionOpinionInfluencePercent, 0f, 50f, 1f);
            changed |= ClampRounded(ref persuasionRomanticBonusPercent, 0f, 50f, 1f);
            changed |= ClampRounded(ref persuasionFamilyBonusPercent, 0f, 50f, 1f);
            changed |= ClampRounded(ref persuasionMinimumChancePercent, 0f, 99f, 1f);
            changed |= ClampRounded(ref persuasionMaximumChancePercent, persuasionMinimumChancePercent, 100f, 1f);

            changed |= ClampRounded(ref prisonerMealDeliveryCooldownHours, 0f, 12f, 0.5f);
            changed |= ClampRounded(ref prisonerFattenTargetPercent, 60f, 90f, 1f);
            changed |= ClampRounded(ref prisonerFattenResumePercent, 0f,
                Mathf.Min(50f, prisonerFattenTargetPercent - 1f), 1f);

            changed |= ClampRounded(ref gluttoniumVeinsPer10kCells, 0f, 5f, 0.1f);
            changed |= ClampRounded(ref orbitalMovementReliefPercent, 0f, 100f, 1f);

            changed |= ClampRounded(ref foodProcessorNutritionPerCycle, 0.05f, 2f, 0.05f);
            changed |= ClampRounded(ref foodDispenserServingNutrition, 0.05f, 1f, 0.05f);
            changed |= ClampRounded(ref distillerNutritionPerCycle, 0.05f, 2f, 0.05f);
            changed |= ClampRounded(
                ref autoFeederCheckSeconds,
                0.5f,
                30f,
                1f / 60f);
            changed |= ClampRounded(
                ref autoFeederGainFullnessPercentPerPulse,
                1f,
                50f,
                1f);
            changed |= ClampRounded(
                ref autoFeederMaximumTargetPercent,
                Mathf.Max(70f, feedingTargetPercent),
                95f,
                1f);
            changed |= ClampRounded(ref advancedAutoFeederRange, 2f, 20f, 1f);
            changed |= ClampRounded(ref advancedAutoFeederBedLimit, 1f, 12f, 1f);
            return changed;
        }

        public void ResetPage(FeedOtherSettingsPage page)
        {
            switch (page)
            {
                case FeedOtherSettingsPage.Feeding:
                    ResetFeeding();
                    break;
                case FeedOtherSettingsPage.Cooldowns:
                    ResetCooldowns();
                    break;
                case FeedOtherSettingsPage.Social:
                    ResetSocial();
                    break;
                case FeedOtherSettingsPage.PawnBehaviour:
                    ResetPawnBehaviour();
                    break;
                case FeedOtherSettingsPage.Prisoners:
                    ResetPrisoners();
                    break;
                case FeedOtherSettingsPage.WorldGeneration:
                    ResetWorldGeneration();
                    break;
                case FeedOtherSettingsPage.FoodNetwork:
                    ResetFoodNetwork();
                    break;
            }

            Validate();
        }

        public void ResetAll()
        {
            ResetFeeding();
            ResetCooldowns();
            ResetSocial();
            ResetPawnBehaviour();
            ResetPrisoners();
            ResetWorldGeneration();
            ResetFoodNetwork();
            Validate();
        }

        private void ResetFeeding()
        {
            autonomousSharedMealsEnabled = true;
            autonomousOneWayFeedingEnabled = true;
            manualPartnerFeedingEnabled = true;
            bedsideFeedingEnabled = true;
            startingFullnessPercent = DefaultStartingFullnessPercent;
            feedingTargetPercent = DefaultFeedingTargetPercent;
            maximumSessionHours = 3f;
            autonomousFrequencyMultiplier = 1f;
        }

        private void ResetCooldowns()
        {
            participantCooldownHours = 6f;
            oneWayFeederCooldownHours = 3f;
            pairCooldownHours = 6f;
            likeOrHigherCooldownBypass = true;
            manualOrdersIgnoreCooldowns = true;
        }

        private void ResetSocial()
        {
            feedingDialogueEnabled = true;
            postMealSocialEnabled = true;
            postMealRecreationTargetPercent = 95f;
            maximumPostMealChatHours = 1f;
            socialOpinionMemoryEnabled = true;
            positiveVeryFullMoodEnabled = true;
            negativeVeryFullMoodEnabled = true;
            afterMealMoodEnabled = true;
        }

        private void ResetPawnBehaviour()
        {
            idleUnderweightEatingEnabled = true;
            idleEatingTriggerPercent = 25f;
            idleMinimumDelayMinutes = 7.2f;
            idleMaximumDelayMinutes = 21.6f;
            automaticMilkExpressionEnabled = true;
            weightOpinionPersuasionEnabled = true;
            persuasionCooldownDays = 1f;
            persuasionBaseChancePercent = 20f;
            persuasionPerSocialLevelPercent = 6.5f;
            persuasionOpinionInfluencePercent = 15f;
            persuasionRomanticBonusPercent = 15f;
            persuasionFamilyBonusPercent = 8f;
            persuasionMinimumChancePercent = 5f;
            persuasionMaximumChancePercent = 95f;
        }

        private void ResetPrisoners()
        {
            prisonerFeedingOverhaulEnabled = true;
            prisonerMealDeliveryCooldownHours = 2f;
            prisonerFattenEnabled = true;
            prisonerFattenTargetPercent = 80f;
            prisonerFattenResumePercent = 10f;
        }

        private void ResetWorldGeneration()
        {
            balancedWeightOpinionDistributionEnabled = true;
            guaranteedEatingStyleTraitsEnabled = true;
            guaranteedElasticityTraitsEnabled = true;
            repairExistingTraitCategoriesEnabled = true;
            gluttoniumVeinsEnabled = true;
            gluttoniumVeinsPer10kCells = 1.2f;
            orbitalMovementReliefEnabled = true;
            orbitalMovementReliefPercent = 75f;
        }

        private void ResetFoodNetwork()
        {
            foodNetworkV2Enabled = true;
            processPreparedMealsInFoodProcessor = true;
            foodProcessorNutritionPerCycle = 0.1f;
            foodDispenserServingNutrition = 0.1f;
            distillerNutritionPerCycle = 0.1f;
            autoFeederCheckSeconds = 3.33f;
            autoFeederGainFullnessPercentPerPulse = 10f;
            autoFeederMaximumTargetPercent = 90f;
            advancedAutoFeederRange = 9f;
            advancedAutoFeederBedLimit = 4f;
            showFoodPipeOverlayWhenSelected = true;
            alwaysShowFoodPipeOverlay = false;
        }

        private static int HoursToTicks(float hours)
        {
            return Mathf.Max(0, Mathf.RoundToInt(hours * 2500f));
        }

        private static int MinutesToTicks(float minutes)
        {
            return Mathf.Max(1, Mathf.RoundToInt(minutes * (2500f / 60f)));
        }

        private static bool ClampRounded(ref float value, float minimum, float maximum, float step)
        {
            float oldValue = value;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                value = minimum;
            }
            else
            {
                value = Mathf.Clamp(value, minimum, maximum);
                if (step > 0f)
                {
                    value = Mathf.Round(value / step) * step;
                    value = Mathf.Clamp(value, minimum, maximum);
                }
            }

            return !Mathf.Approximately(oldValue, value);
        }
    }

    public sealed class FeedOtherMod : Mod
    {
        private static FeedOtherMod instance;
        private static FeedOtherSettings settings = new FeedOtherSettings();

        public static FeedOtherSettings Settings
        {
            get { return settings; }
        }

        public FeedOtherMod(ModContentPack content) : base(content)
        {
            instance = this;
            FeedOtherSettings loadedSettings = GetSettings<FeedOtherSettings>();
            settings = loadedSettings ?? new FeedOtherSettings();
            settings.Validate();
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                ApplyRuntimeSettings(false);
            });
        }

        public override string SettingsCategory()
        {
            return "RimRound Patch";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (FeedOtherSettingsUI.Draw(inRect))
            {
                SaveSettings();
            }
        }

        public static void ApplyRuntimeSettings(bool repairExistingPawns)
        {
            settings.Validate();
            FeedOtherBootstrap.ApplyFoodNetworkPatchMode(
                settings.foodNetworkV2Enabled);
            RimRoundTraitGenerationBeta.ApplyConfiguredOpinionDistribution();
            WeightOpinionShiftSettingsUtility.ApplyAbilityCooldown();
            if (Current.Game != null)
            {
                OrbitalMovementPenaltyUtility.NotifySettingsChanged();
                PrisonerFatteningFoodPatch.EnforceAllFattenBedLocks();
                FoodNetworkV2MapComponent.MarkAllMapsDirty();

                if (repairExistingPawns && settings.repairExistingTraitCategoriesEnabled)
                {
                    RimRoundTraitGenerationBeta.EnsureAllExistingPawns();
                }
            }
        }

        public static void SaveSettings()
        {
            ApplyRuntimeSettings(true);
            if (instance != null)
            {
                instance.WriteSettings();
            }
        }
    }
}
