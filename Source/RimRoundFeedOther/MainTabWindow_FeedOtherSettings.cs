using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimRound.FeedOther
{
    public static class FeedOtherSettingsUI
    {
        private const float FooterHeight = 44f;
        private static readonly Vector2[] ScrollPositions = new Vector2[7];
        private static readonly List<TabRecord> Tabs = new List<TabRecord>();
        private static FeedOtherSettingsPage currentPage = FeedOtherSettingsPage.Feeding;
        private static bool showAdvancedPersuasion;

        public static bool Draw(Rect inRect)
        {
            FeedOtherSettings settings = FeedOtherMod.Settings;
            bool changed = settings.Validate();
            EnsureTabs();

            Text.Font = GameFont.Medium;
            Widgets.Label(
                new Rect(inRect.x + 8f, inRect.y + 2f, inRect.width - 16f, 36f),
                "RR_FeedOtherSettingsTitle".Translate());

            Rect tabRect = new Rect(
                inRect.x + 6f,
                inRect.y + 50f,
                inRect.width - 12f,
                inRect.height - 50f);
            TabDrawer.DrawTabs<TabRecord>(tabRect, Tabs);

            Rect panelRect = new Rect(
                inRect.x + 2f,
                inRect.y + 78f,
                inRect.width - 4f,
                inRect.height - 78f - FooterHeight);
            Widgets.DrawMenuSection(panelRect);

            Rect scrollRect = panelRect.ContractedBy(10f);
            float contentHeight = ContentHeight(currentPage);
            Rect viewRect = new Rect(0f, 0f, scrollRect.width - 18f, contentHeight);
            int pageIndex = (int)currentPage;
            Widgets.BeginScrollView(
                scrollRect,
                ref ScrollPositions[pageIndex],
                viewRect,
                true);

            SettingsDrawer drawer = new SettingsDrawer(viewRect.width);
            switch (currentPage)
            {
                case FeedOtherSettingsPage.Feeding:
                    DrawFeeding(drawer, settings);
                    break;
                case FeedOtherSettingsPage.Cooldowns:
                    DrawCooldowns(drawer, settings);
                    break;
                case FeedOtherSettingsPage.Social:
                    DrawSocial(drawer, settings);
                    break;
                case FeedOtherSettingsPage.PawnBehaviour:
                    DrawPawnBehaviour(drawer, settings);
                    break;
                case FeedOtherSettingsPage.Prisoners:
                    DrawPrisoners(drawer, settings);
                    break;
                case FeedOtherSettingsPage.WorldGeneration:
                    DrawWorldGeneration(drawer, settings);
                    break;
                case FeedOtherSettingsPage.FoodNetwork:
                    DrawFoodNetwork(drawer, settings);
                    break;
            }

            changed |= drawer.Changed;
            Widgets.EndScrollView();

            Rect resetPageRect = new Rect(
                inRect.x + 12f,
                inRect.yMax - 38f,
                170f,
                32f);
            if (Widgets.ButtonText(resetPageRect, "RR_FeedOtherSettingsResetPage".Translate()))
            {
                settings.ResetPage(currentPage);
                changed = true;
            }

            Rect resetAllRect = new Rect(
                resetPageRect.xMax + 10f,
                resetPageRect.y,
                150f,
                32f);
            if (Widgets.ButtonText(resetAllRect, "RR_FeedOtherSettingsResetAll".Translate()))
            {
                settings.ResetAll();
                changed = true;
            }

            settings.Validate();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            GUI.enabled = true;
            return changed;
        }

        private static void EnsureTabs()
        {
            if (Tabs.Count > 0)
            {
                return;
            }

            AddTab(FeedOtherSettingsPage.Feeding, "RR_FeedOtherSettingsTabFeeding");
            AddTab(FeedOtherSettingsPage.Cooldowns, "RR_FeedOtherSettingsTabCooldowns");
            AddTab(FeedOtherSettingsPage.Social, "RR_FeedOtherSettingsTabSocial");
            AddTab(FeedOtherSettingsPage.PawnBehaviour, "RR_FeedOtherSettingsTabPawnBehaviour");
            AddTab(FeedOtherSettingsPage.Prisoners, "RR_FeedOtherSettingsTabPrisoners");
            AddTab(FeedOtherSettingsPage.WorldGeneration, "RR_FeedOtherSettingsTabWorld");
            AddTab(FeedOtherSettingsPage.FoodNetwork, "RR_FeedOtherSettingsTabFoodNetwork");
        }

        private static void AddTab(FeedOtherSettingsPage page, string labelKey)
        {
            FeedOtherSettingsPage capturedPage = page;
            Tabs.Add(new TabRecord(
                labelKey.Translate(),
                delegate { currentPage = capturedPage; },
                () => currentPage == capturedPage));
        }

        private static float ContentHeight(FeedOtherSettingsPage page)
        {
            switch (page)
            {
                case FeedOtherSettingsPage.Feeding:
                    return 720f;
                case FeedOtherSettingsPage.Cooldowns:
                    return 560f;
                case FeedOtherSettingsPage.Social:
                    return 680f;
                case FeedOtherSettingsPage.PawnBehaviour:
                    return showAdvancedPersuasion ? 1160f : 720f;
                case FeedOtherSettingsPage.Prisoners:
                    return 590f;
                case FeedOtherSettingsPage.WorldGeneration:
                    return 720f;
                case FeedOtherSettingsPage.FoodNetwork:
                    return 980f;
                default:
                    return 700f;
            }
        }

        private static void DrawFeeding(SettingsDrawer drawer, FeedOtherSettings settings)
        {
            drawer.Header("RR_FeedOtherSettingsFeedingHeader");
            drawer.Checkbox(ref settings.autonomousSharedMealsEnabled,
                "RR_FeedOtherSettingsAutonomousShared", "RR_FeedOtherSettingsAutonomousSharedTip", true, "Live");
            drawer.Checkbox(ref settings.autonomousOneWayFeedingEnabled,
                "RR_FeedOtherSettingsAutonomousOneWay", "RR_FeedOtherSettingsAutonomousOneWayTip", true, "Live");
            drawer.Checkbox(ref settings.manualPartnerFeedingEnabled,
                "RR_FeedOtherSettingsManualFeeding", "RR_FeedOtherSettingsManualFeedingTip", true, "Live");
            drawer.Checkbox(ref settings.bedsideFeedingEnabled,
                "RR_FeedOtherSettingsBedsideFeeding", "RR_FeedOtherSettingsBedsideFeedingTip", true, "Live");

            drawer.Slider(ref settings.startingFullnessPercent,
                FeedOtherSettings.MinimumStartingFullnessPercent,
                Mathf.Min(FeedOtherSettings.MaximumStartingFullnessPercent, settings.feedingTargetPercent - 1f),
                1f, "RR_FeedOtherSettingsStartingCutoff", "RR_FeedOtherSettingsStartingCutoffTip", "F0", "%", true, "Live");
            drawer.Slider(ref settings.feedingTargetPercent,
                FeedOtherSettings.MinimumFeedingTargetPercent,
                FeedOtherSettings.MaximumFeedingTargetPercent,
                1f, "RR_FeedOtherSettingsFeedingTarget", "RR_FeedOtherSettingsFeedingTargetTooltip", "F0", "%", true, "Live");
            drawer.Note(FeedingStageDescription(settings.FeedingTargetPercentRounded));
            if (settings.FeedingTargetPercentRounded >= 80)
            {
                drawer.Warning("RR_FeedOtherSettingsHighTargetWarning");
            }

            drawer.Slider(ref settings.maximumSessionHours,
                0.5f, 12f, 0.5f, "RR_FeedOtherSettingsSessionTimeout",
                "RR_FeedOtherSettingsSessionTimeoutTip", "F1", " h", true, "Live");
            bool autonomousEnabled = settings.autonomousSharedMealsEnabled ||
                settings.autonomousOneWayFeedingEnabled;
            drawer.Slider(ref settings.autonomousFrequencyMultiplier,
                0.25f, 3f, 0.25f, "RR_FeedOtherSettingsAutonomousFrequency",
                "RR_FeedOtherSettingsAutonomousFrequencyTip", "F2", "×", autonomousEnabled, "Live");
            drawer.Note("RR_FeedOtherSettingsMoodNote".Translate());
        }

        private static void DrawCooldowns(SettingsDrawer drawer, FeedOtherSettings settings)
        {
            drawer.Header("RR_FeedOtherSettingsCooldownHeader");
            drawer.Slider(ref settings.participantCooldownHours,
                0f, 24f, 0.5f, "RR_FeedOtherSettingsParticipantCooldown",
                "RR_FeedOtherSettingsParticipantCooldownTip", "F1", " h", true, "Live");
            drawer.Slider(ref settings.oneWayFeederCooldownHours,
                0f, 24f, 0.5f, "RR_FeedOtherSettingsOneWayCooldown",
                "RR_FeedOtherSettingsOneWayCooldownTip", "F1", " h", true, "Live");
            drawer.Slider(ref settings.pairCooldownHours,
                0f, 24f, 0.5f, "RR_FeedOtherSettingsPairCooldown",
                "RR_FeedOtherSettingsPairCooldownTip", "F1", " h", true, "Live");
            drawer.Checkbox(ref settings.likeOrHigherCooldownBypass,
                "RR_FeedOtherSettingsLikeBypass", "RR_FeedOtherSettingsLikeBypassTip", true, "Live");
            drawer.Checkbox(ref settings.manualOrdersIgnoreCooldowns,
                "RR_FeedOtherSettingsManualIgnoresCooldown", "RR_FeedOtherSettingsManualIgnoresCooldownTip", true, "Live");
            drawer.Note("RR_FeedOtherSettingsCooldownZeroNote".Translate());
        }

        private static void DrawSocial(SettingsDrawer drawer, FeedOtherSettings settings)
        {
            drawer.Header("RR_FeedOtherSettingsSocialHeader");
            drawer.Checkbox(ref settings.feedingDialogueEnabled,
                "RR_FeedOtherSettingsDialogue", "RR_FeedOtherSettingsDialogueTip", true, "Live");
            drawer.Checkbox(ref settings.postMealSocialEnabled,
                "RR_FeedOtherSettingsPostMealSocial", "RR_FeedOtherSettingsPostMealSocialTip", true, "Live");
            drawer.Slider(ref settings.postMealRecreationTargetPercent,
                50f, 100f, 1f, "RR_FeedOtherSettingsRecreationTarget",
                "RR_FeedOtherSettingsRecreationTargetTip", "F0", "%", settings.postMealSocialEnabled, "Live");
            drawer.Slider(ref settings.maximumPostMealChatHours,
                0.25f, 4f, 0.25f, "RR_FeedOtherSettingsMaxChat",
                "RR_FeedOtherSettingsMaxChatTip", "F2", " h", settings.postMealSocialEnabled, "Live");
            drawer.Checkbox(ref settings.socialOpinionMemoryEnabled,
                "RR_FeedOtherSettingsSocialMemory", "RR_FeedOtherSettingsSocialMemoryTip", true, "Live");
            drawer.Checkbox(ref settings.positiveVeryFullMoodEnabled,
                "RR_FeedOtherSettingsPositiveMood", "RR_FeedOtherSettingsPositiveMoodTip", true, "Live");
            drawer.Checkbox(ref settings.negativeVeryFullMoodEnabled,
                "RR_FeedOtherSettingsNegativeMood", "RR_FeedOtherSettingsNegativeMoodTip", true, "Live");
            drawer.Checkbox(ref settings.afterMealMoodEnabled,
                "RR_FeedOtherSettingsAfterMealMood", "RR_FeedOtherSettingsAfterMealMoodTip",
                settings.postMealSocialEnabled, "Live");
        }

        private static void DrawPawnBehaviour(SettingsDrawer drawer, FeedOtherSettings settings)
        {
            drawer.Header("RR_FeedOtherSettingsPawnHeader");
            drawer.Checkbox(ref settings.idleUnderweightEatingEnabled,
                "RR_FeedOtherSettingsIdleEating", "RR_FeedOtherSettingsIdleEatingTip", true, "Live");
            drawer.Slider(ref settings.idleEatingTriggerPercent,
                5f, 60f, 1f, "RR_FeedOtherSettingsIdleTrigger",
                "RR_FeedOtherSettingsIdleTriggerTip", "F0", "%", settings.idleUnderweightEatingEnabled, "Live");
            drawer.Slider(ref settings.idleMinimumDelayMinutes,
                1f, 120f, 0.1f, "RR_FeedOtherSettingsIdleMinDelay",
                "RR_FeedOtherSettingsIdleMinDelayTip", "F1", " min", settings.idleUnderweightEatingEnabled, "Live");
            drawer.Slider(ref settings.idleMaximumDelayMinutes,
                settings.idleMinimumDelayMinutes, 120f, 0.1f, "RR_FeedOtherSettingsIdleMaxDelay",
                "RR_FeedOtherSettingsIdleMaxDelayTip", "F1", " min", settings.idleUnderweightEatingEnabled, "Live");

            bool biotechActive = ModsConfig.BiotechActive;
            drawer.Checkbox(ref settings.automaticMilkExpressionEnabled,
                "RR_FeedOtherSettingsMilkExpression", "RR_FeedOtherSettingsMilkExpressionTip",
                biotechActive, biotechActive ? "Live" : "Biotech required");

            drawer.Checkbox(ref settings.weightOpinionPersuasionEnabled,
                "RR_FeedOtherSettingsPersuasion", "RR_FeedOtherSettingsPersuasionTip", true, "Live");
            drawer.Slider(ref settings.persuasionCooldownDays,
                0.25f, 10f, 0.25f, "RR_FeedOtherSettingsPersuasionCooldown",
                "RR_FeedOtherSettingsPersuasionCooldownTip", "F2", " d", settings.weightOpinionPersuasionEnabled, "Live");

            Rect advancedRect = drawer.TakeRow(34f);
            if (Widgets.ButtonText(advancedRect,
                (showAdvancedPersuasion
                    ? "RR_FeedOtherSettingsHideAdvanced"
                    : "RR_FeedOtherSettingsShowAdvanced").Translate()))
            {
                showAdvancedPersuasion = !showAdvancedPersuasion;
            }

            if (!showAdvancedPersuasion)
            {
                return;
            }

            drawer.Subheader("RR_FeedOtherSettingsPersuasionAdvancedHeader");
            bool persuasionEnabled = settings.weightOpinionPersuasionEnabled;
            drawer.Slider(ref settings.persuasionBaseChancePercent,
                0f, 100f, 1f, "RR_FeedOtherSettingsPersuasionBase",
                "RR_FeedOtherSettingsPersuasionBaseTip", "F0", "%", persuasionEnabled, "Live");
            drawer.Slider(ref settings.persuasionPerSocialLevelPercent,
                0f, 20f, 0.5f, "RR_FeedOtherSettingsPersuasionPerLevel",
                "RR_FeedOtherSettingsPersuasionPerLevelTip", "F1", "%", persuasionEnabled, "Live");
            drawer.Slider(ref settings.persuasionOpinionInfluencePercent,
                0f, 50f, 1f, "RR_FeedOtherSettingsPersuasionOpinion",
                "RR_FeedOtherSettingsPersuasionOpinionTip", "F0", "%", persuasionEnabled, "Live");
            drawer.Slider(ref settings.persuasionRomanticBonusPercent,
                0f, 50f, 1f, "RR_FeedOtherSettingsPersuasionRomantic",
                "RR_FeedOtherSettingsPersuasionRomanticTip", "F0", "%", persuasionEnabled, "Live");
            drawer.Slider(ref settings.persuasionFamilyBonusPercent,
                0f, 50f, 1f, "RR_FeedOtherSettingsPersuasionFamily",
                "RR_FeedOtherSettingsPersuasionFamilyTip", "F0", "%", persuasionEnabled, "Live");
            drawer.Slider(ref settings.persuasionMinimumChancePercent,
                0f, 99f, 1f, "RR_FeedOtherSettingsPersuasionMinimum",
                "RR_FeedOtherSettingsPersuasionMinimumTip", "F0", "%", persuasionEnabled, "Live");
            drawer.Slider(ref settings.persuasionMaximumChancePercent,
                settings.persuasionMinimumChancePercent, 100f, 1f, "RR_FeedOtherSettingsPersuasionMaximum",
                "RR_FeedOtherSettingsPersuasionMaximumTip", "F0", "%", persuasionEnabled, "Live");
            drawer.Note("RR_FeedOtherSettingsPersuasionSocialCapNote".Translate());
        }

        private static void DrawPrisoners(SettingsDrawer drawer, FeedOtherSettings settings)
        {
            drawer.Header("RR_FeedOtherSettingsPrisonerHeader");
            drawer.Checkbox(ref settings.prisonerFeedingOverhaulEnabled,
                "RR_FeedOtherSettingsPrisonerOverhaul", "RR_FeedOtherSettingsPrisonerOverhaulTip", true, "Live");
            drawer.Slider(ref settings.prisonerMealDeliveryCooldownHours,
                0f, 12f, 0.5f, "RR_FeedOtherSettingsPrisonerDeliveryCooldown",
                "RR_FeedOtherSettingsPrisonerDeliveryCooldownTip", "F1", " h",
                settings.prisonerFeedingOverhaulEnabled, "Live");
            drawer.Checkbox(ref settings.prisonerFattenEnabled,
                "RR_FeedOtherSettingsFatten", "RR_FeedOtherSettingsFattenTip",
                settings.prisonerFeedingOverhaulEnabled, "Live");
            bool fattenEnabled = settings.prisonerFeedingOverhaulEnabled && settings.prisonerFattenEnabled;
            drawer.Slider(ref settings.prisonerFattenTargetPercent,
                60f, 90f, 1f, "RR_FeedOtherSettingsFattenTarget",
                "RR_FeedOtherSettingsFattenTargetTip", "F0", "%", fattenEnabled, "Live");
            drawer.Slider(ref settings.prisonerFattenResumePercent,
                0f, Mathf.Min(50f, settings.prisonerFattenTargetPercent - 1f), 1f,
                "RR_FeedOtherSettingsFattenResume", "RR_FeedOtherSettingsFattenResumeTip",
                "F0", "%", fattenEnabled, "Live");
            if (settings.prisonerFattenTargetPercent >= 80f)
            {
                drawer.Warning("RR_FeedOtherSettingsFattenWarning");
            }
        }

        private static void DrawWorldGeneration(SettingsDrawer drawer, FeedOtherSettings settings)
        {
            drawer.Header("RR_FeedOtherSettingsWorldHeader");
            drawer.Checkbox(ref settings.balancedWeightOpinionDistributionEnabled,
                "RR_FeedOtherSettingsBalancedOpinions", "RR_FeedOtherSettingsBalancedOpinionsTip", true, "New pawns only");
            drawer.Checkbox(ref settings.guaranteedEatingStyleTraitsEnabled,
                "RR_FeedOtherSettingsEatingTraits", "RR_FeedOtherSettingsEatingTraitsTip", true, "New pawns only");
            drawer.Checkbox(ref settings.guaranteedElasticityTraitsEnabled,
                "RR_FeedOtherSettingsElasticityTraits", "RR_FeedOtherSettingsElasticityTraitsTip", true, "New pawns only");
            drawer.Checkbox(ref settings.repairExistingTraitCategoriesEnabled,
                "RR_FeedOtherSettingsRepairTraits", "RR_FeedOtherSettingsRepairTraitsTip", true, "On load/save");
            drawer.Checkbox(ref settings.gluttoniumVeinsEnabled,
                "RR_FeedOtherSettingsGluttonium", "RR_FeedOtherSettingsGluttoniumTip", true, "New maps only");
            drawer.Slider(ref settings.gluttoniumVeinsPer10kCells,
                0f, 5f, 0.1f, "RR_FeedOtherSettingsGluttoniumFrequency",
                "RR_FeedOtherSettingsGluttoniumFrequencyTip", "F1", " /10k",
                settings.gluttoniumVeinsEnabled, "New maps only");

            bool odysseyActive = ModsConfig.OdysseyActive;
            drawer.Checkbox(ref settings.orbitalMovementReliefEnabled,
                "RR_FeedOtherSettingsOrbitalRelief", "RR_FeedOtherSettingsOrbitalReliefTip",
                odysseyActive, odysseyActive ? "Live" : "Odyssey required");
            drawer.Slider(ref settings.orbitalMovementReliefPercent,
                0f, 100f, 1f, "RR_FeedOtherSettingsOrbitalReliefAmount",
                "RR_FeedOtherSettingsOrbitalReliefAmountTip", "F0", "%",
                odysseyActive && settings.orbitalMovementReliefEnabled,
                odysseyActive ? "Live" : "Odyssey required");
            drawer.Note("RR_FeedOtherSettingsAlwaysOnFixes".Translate());
        }

        private static void DrawFoodNetwork(SettingsDrawer drawer, FeedOtherSettings settings)
        {
            drawer.Header("RR_FoodNetworkSettingsHeader");
            drawer.Checkbox(ref settings.foodNetworkV2Enabled,
                "RR_FoodNetworkSettingsEnabled", "RR_FoodNetworkSettingsEnabledTip",
                true, "Live");
            bool enabled = settings.foodNetworkV2Enabled;
            drawer.Checkbox(ref settings.processPreparedMealsInFoodProcessor,
                "RR_FoodNetworkSettingsPreparedMeals", "RR_FoodNetworkSettingsPreparedMealsTip",
                enabled, "Live");
            drawer.Slider(ref settings.foodProcessorNutritionPerCycle,
                0.05f, 2f, 0.05f, "RR_FoodNetworkSettingsProcessorRate",
                "RR_FoodNetworkSettingsProcessorRateTip", "F2", " nutrition/cycle",
                enabled, "Live");
            drawer.Slider(ref settings.distillerNutritionPerCycle,
                0.05f, 2f, 0.05f, "RR_FoodNetworkSettingsDistillerRate",
                "RR_FoodNetworkSettingsDistillerRateTip", "F2", " nutrition/cycle",
                enabled, "Live");

            drawer.Subheader("RR_FoodNetworkSettingsFeederHeader");
            drawer.Slider(ref settings.foodDispenserServingNutrition,
                0.05f, 1f, 0.05f, "RR_FoodNetworkSettingsServingSize",
                "RR_FoodNetworkSettingsServingSizeTip", "F2", " nutrition",
                enabled, "Live");
            drawer.Slider(ref settings.autoFeederGainFullnessPercentPerPulse,
                1f, 50f, 1f, "RR_FoodNetworkSettingsFeederGainFlow",
                "RR_FoodNetworkSettingsFeederGainFlowTip", "F0", "% capacity/pulse",
                enabled, "Live");
            drawer.Slider(ref settings.autoFeederCheckSeconds,
                0.5f, 30f, 0.5f, "RR_FoodNetworkSettingsFeederInterval",
                "RR_FoodNetworkSettingsFeederIntervalTip", "F1", " sec",
                enabled, "Live");
            drawer.Slider(ref settings.autoFeederMaximumTargetPercent,
                Mathf.Max(70f, settings.feedingTargetPercent), 95f, 1f,
                "RR_FoodNetworkSettingsMaximumTarget",
                "RR_FoodNetworkSettingsMaximumTargetTip", "F0", "%",
                enabled, "Live");
            drawer.Slider(ref settings.advancedAutoFeederRange,
                2f, 20f, 1f, "RR_FoodNetworkSettingsAdvancedRange",
                "RR_FoodNetworkSettingsAdvancedRangeTip", "F0", " cells",
                enabled, "Live");
            drawer.Slider(ref settings.advancedAutoFeederBedLimit,
                1f, 12f, 1f, "RR_FoodNetworkSettingsAdvancedBeds",
                "RR_FoodNetworkSettingsAdvancedBedsTip", "F0", " beds",
                enabled, "Live");

            drawer.Subheader("RR_FoodNetworkSettingsOverlayHeader");
            drawer.Checkbox(ref settings.showFoodPipeOverlayWhenSelected,
                "RR_FoodNetworkSettingsSelectedOverlay", "RR_FoodNetworkSettingsSelectedOverlayTip",
                enabled, "Live");
            drawer.Checkbox(ref settings.alwaysShowFoodPipeOverlay,
                "RR_FoodNetworkSettingsAlwaysOverlay", "RR_FoodNetworkSettingsAlwaysOverlayTip",
                enabled, "Live");
            drawer.Note("RR_FoodNetworkSettingsMigrationNote".Translate());
            drawer.Note("RR_FoodNetworkSettingsPreservationNote".Translate());
        }

        private static TaggedString FeedingStageDescription(int targetPercent)
        {
            if (targetPercent >= 90)
            {
                return "RR_FeedOtherSettingsStageAboutToBurst".Translate();
            }

            if (targetPercent >= 80)
            {
                return "RR_FeedOtherSettingsStagePainfullyFull".Translate();
            }

            if (targetPercent >= 70)
            {
                return "RR_FeedOtherSettingsStageVeryFull".Translate();
            }

            return "RR_FeedOtherSettingsStageFull".Translate();
        }

        private sealed class SettingsDrawer
        {
            private readonly float width;
            private float y;

            public bool Changed { get; private set; }

            public SettingsDrawer(float width)
            {
                this.width = width;
                y = 4f;
            }

            public void Header(string key)
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(TakeRow(36f), key.Translate());
                Text.Font = GameFont.Small;
                y += 4f;
            }

            public void Subheader(string key)
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(TakeRow(32f), key.Translate());
                Text.Font = GameFont.Small;
            }

            public void Checkbox(
                ref bool value,
                string labelKey,
                string tooltipKey,
                bool enabled,
                string status)
            {
                Rect row = TakeRow(32f);
                string label = labelKey.Translate();
                if (!string.IsNullOrEmpty(status))
                {
                    label += "  [" + status + "]";
                }

                bool oldValue = value;
                bool previousEnabled = GUI.enabled;
                GUI.enabled = enabled;
                Widgets.CheckboxLabeled(row, label, ref value, !enabled);
                GUI.enabled = previousEnabled;
                TooltipHandler.TipRegion(row, tooltipKey.Translate());
                if (value != oldValue)
                {
                    Changed = true;
                }
            }

            public void Slider(
                ref float value,
                float minimum,
                float maximum,
                float step,
                string labelKey,
                string tooltipKey,
                string numberFormat,
                string suffix,
                bool enabled,
                string status)
            {
                Rect whole = TakeRow(64f);
                Rect labelRect = new Rect(whole.x, whole.y, whole.width, 26f);
                string label = labelKey.Translate();
                if (!string.IsNullOrEmpty(status))
                {
                    label += "  [" + status + "]";
                }

                Color oldColor = GUI.color;
                if (!enabled)
                {
                    GUI.color = Color.gray;
                }
                Widgets.Label(labelRect, label);
                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(labelRect, value.ToString(numberFormat) + suffix);
                Text.Anchor = oldAnchor;
                GUI.color = oldColor;

                Rect sliderRect = new Rect(whole.x, whole.y + 26f, whole.width, 30f);
                bool previousEnabled = GUI.enabled;
                GUI.enabled = enabled;
                float newValue = Widgets.HorizontalSlider(
                    sliderRect,
                    value,
                    minimum,
                    maximum,
                    false,
                    null,
                    minimum.ToString(numberFormat) + suffix,
                    maximum.ToString(numberFormat) + suffix,
                    step);
                GUI.enabled = previousEnabled;
                if (enabled && !Mathf.Approximately(newValue, value))
                {
                    value = newValue;
                    Changed = true;
                }

                TooltipHandler.TipRegion(whole, tooltipKey.Translate());
            }

            public void Note(TaggedString text)
            {
                Rect rect = TakeRow(56f);
                Color oldColor = GUI.color;
                GUI.color = Color.gray;
                Widgets.Label(rect, text);
                GUI.color = oldColor;
            }

            public void Warning(string key)
            {
                Rect rect = TakeRow(58f);
                Color oldColor = GUI.color;
                GUI.color = Color.yellow;
                Widgets.Label(rect, key.Translate());
                GUI.color = oldColor;
            }

            public Rect TakeRow(float height)
            {
                Rect row = new Rect(4f, y, width - 8f, height);
                y += height + 4f;
                return row;
            }
        }
    }

    public sealed class MainTabWindow_FeedOtherSettings : MainTabWindow
    {
        private bool settingsChanged;

        public override Vector2 RequestedTabSize
        {
            get { return new Vector2(1060f, 760f); }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            FeedOtherMod.Settings.Validate();
            settingsChanged = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (FeedOtherSettingsUI.Draw(inRect))
            {
                settingsChanged = true;
                FeedOtherMod.ApplyRuntimeSettings(false);
            }
        }

        public override void PostClose()
        {
            if (settingsChanged)
            {
                FeedOtherMod.SaveSettings();
            }

            base.PostClose();
        }
    }
}
