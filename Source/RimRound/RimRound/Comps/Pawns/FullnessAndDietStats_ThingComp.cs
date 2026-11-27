using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

using RimRound.Utilities;
using RimRound.Defs;
using RimWorld;
using Verse.AI;
using RimRound.UI;
using UnityEngine;
using System.Reflection;
using Resources = RimRound.Utilities.Resources;
using Verse.Sound;
using RimWorld.Planet;
using RimRound.AI;

namespace RimRound.Comps
{
    public partial class FullnessAndDietStats_ThingComp : ThingComp
    {
        public bool Disabled {
            get {
                // Eligibility changes on adulthood, death and need replacement.
                return !BodyModelEligibility.IsEligible(parent as Pawn);
            }
        }

        public FullnessAndDietStats_ThingComp()
        {

        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            base.PostDeSpawn(map, mode);
            DespawnSustainers();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            DespawnSustainers();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Pair<float, float> ranges = GetRanges();
                cachedSliderVal1 = ranges.First;
                cachedSliderVal2 = ranges.Second;
            }

            Scribe_Values.Look<bool>(ref defaultBodyTypeForced, "defaultBodyTypeForced", false);
            Scribe_Values.Look<bool>(ref _isConnectedToFeedingMachine, "isConnectedToFeedingMachine", false);

            Scribe_Values.Look<float>(ref cachedSliderVal1, "cachedSliderPos1", -1);
            Scribe_Values.Look<float>(ref cachedSliderVal2, "cachedSliderPos2", -1);

            Scribe_Values.Look<DietMode>(ref dietMode, "dietMode", DietMode.Disabled);
            Scribe_Values.Look<DietMode>(ref preCaravanDietMode, "preCaravanDietMode", DietMode.Disabled);
            Scribe_Values.Look<float>(ref currentFullness, "currentFullness", 0);
            Scribe_Values.Look<float>(ref softLimitPersonal, "softLimit", defaultSoftLimit);
            Scribe_Values.Look<float>(ref currentFullnessToNutritionRatio, "currentFullnessToNutritionRatio", defaultFullnessToNutritionRatio);
            Scribe_Values.Look<float>(ref consumedNutrition, "consumedNutrition", 0f);
            Scribe_Values.Look<float>(ref legacyPerkNutrition, "rrLegacyPerkNutrition", -1f);
            Scribe_Values.Look<float>(ref cumulativeSeverityGained, "suddenWGCumSeverity");

            Scribe_Values.Look<float>(ref debugSoftLimitDelta, "debugSoftLimitDelta");

            ExposeStatBonuses();
            ExposeProgressionLedger();
            ExposePerkLevels();
            ExposeBodyState();
            ExposeBodyStageSavedState();
            ExposePendingWeightChanges();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                InitValuesOnLoad();
        }

        bool _loadedDietBars = false;
        private List<SavedWeightChange> savedWeightGains;
        private List<SavedWeightChange> savedWeightLosses;

        private void ExposePendingWeightChanges()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedWeightGains = activeWeightGainRequests.Select(r => new SavedWeightChange(r)).ToList();
                savedWeightLosses = activeWeightLossRequests.Select(r => new SavedWeightChange(r)).ToList();
            }
            Scribe_Collections.Look(ref savedWeightGains, "rrPendingWeightGains", LookMode.Deep);
            Scribe_Collections.Look(ref savedWeightLosses, "rrPendingWeightLosses", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Queue enumeration and reconstruction preserve the original
                // FIFO order and absolute native ticks, including reversals.
                activeWeightGainRequests = new Queue<WeightGainRequest>((savedWeightGains ?? new List<SavedWeightChange>()).Where(r => r != null).Select(r => r.request));
                activeWeightLossRequests = new Queue<WeightGainRequest>((savedWeightLosses ?? new List<SavedWeightChange>()).Where(r => r != null).Select(r => r.request));
                savedWeightGains = savedWeightLosses = null;
            }
        }

        public sealed class SavedWeightChange : IExposable
        {
            public WeightGainRequest request;
            public SavedWeightChange() { }
            public SavedWeightChange(WeightGainRequest request) { this.request = request; }
            public void ExposeData()
            {
                Scribe_Values.Look(ref request.amountToGain, "amountKg");
                Scribe_Values.Look(ref request.tickToApplyOn, "applyTick");
                Scribe_Values.Look(ref request.duration, "durationTicks");
                Scribe_Values.Look(ref request.triggerMessages, "triggerMessages");
                Scribe_Values.Look(ref request.useMultipliers, "useMultipliers", true);
            }
        }

        private void InitValuesOnLoad()
        {
            // Old saves can contain invalid values after a removed food or race mod.
            // Keep the pawn's other saved state, but restore finite stomach units.
            CurrentFullnessToNutritionRatio = currentFullnessToNutritionRatio;
            CurrentFullness = currentFullness;
            if (float.IsNaN(softLimitPersonal) || float.IsInfinity(softLimitPersonal) || softLimitPersonal <= 0f)
                softLimitPersonal = defaultSoftLimit;
            InitBarsIfNull();
            UpdateDietBars();
            InitializePerksIfNull();
            InitializeBodyState();
            if (legacyPerkNutrition < 0f) legacyPerkNutrition = consumedNutrition;
        }

        private bool progressionLedgerInitialized = true;
        private RimRoundStatBonuses legacyRawSnapshot;
        private RimRoundStatBonuses ownedSinceMarker;
        private List<string> progressionStatNames;
        private List<float> legacyRawSnapshotValues;
        private List<float> ownedSinceMarkerValues;
        private float legacyPerkNutrition = 0f;

        private void ExposeProgressionLedger()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (progressionStatNames == null) progressionStatNames = ProgressionSources.FieldNames();
                foreach (string name in ProgressionSources.FieldNames())
                    if (!progressionStatNames.Contains(name)) progressionStatNames.Add(name);
                legacyRawSnapshotValues = ProgressionSources.ToList(legacyRawSnapshot, progressionStatNames, legacyRawSnapshotValues);
                ownedSinceMarkerValues = ProgressionSources.ToList(ownedSinceMarker, progressionStatNames, ownedSinceMarkerValues);
            }
            Scribe_Values.Look(ref progressionLedgerInitialized, "rrProgressionLedgerInitialized", false);
            Scribe_Collections.Look(ref progressionStatNames, "rrProgressionStatNames", LookMode.Value);
            Scribe_Collections.Look(ref legacyRawSnapshotValues, "rrLegacyRawSnapshot", LookMode.Value);
            Scribe_Collections.Look(ref ownedSinceMarkerValues, "rrOwnedSinceMarker", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                legacyRawSnapshot = ProgressionSources.FromList(progressionStatNames, legacyRawSnapshotValues);
                ownedSinceMarker = ProgressionSources.FromList(progressionStatNames, ownedSinceMarkerValues);
                if (!progressionLedgerInitialized)
                {
                    // Historical callbacks did not record provenance. Archive the
                    // whole raw baseline once; Legacy still reads it unchanged.
                    legacyRawSnapshot = statBonuses;
                    ownedSinceMarker = new RimRoundStatBonuses();
                    progressionLedgerInitialized = true;
                }
            }
        }

        public void RecordOwnedBonus(RimRoundStatBonuses bonus)
        {
            ownedSinceMarker = ProgressionSources.Add(ownedSinceMarker, bonus);
        }

        public RimRoundStatBonuses EffectiveStatBonuses
        {
            get
            {
                if (ColonyProgression.LegacyPerksActive)
                    return ProgressionSources.Add(statBonuses, EquipmentQuality.LegacyQualityDelta(parent as Pawn));
                var external = ProgressionSources.Add(statBonuses, ProgressionSources.Negate(legacyRawSnapshot));
                external = ProgressionSources.Add(external, ProgressionSources.Negate(ownedSinceMarker));
                return ProgressionSources.Add(external, ProgressionSources.Live(parent as Pawn));
            }
        }

        private void InitBarsIfNull()
        {
            if (this.parent.AsPawn().Dead || this.parent.AsPawn().needs?.food == null)
                return;

            if (nutritionbar == null)
                this.nutritionbar = new WeightGizmo_NutritionBar(((Pawn)parent));

            if (fullnessbar == null)
                this.fullnessbar = new WeightGizmo_FullnessBar(((Pawn)parent));

        }

        public void ExposeStatBonuses()
        {

            Scribe_Values.Look<float>(ref statBonuses.weightGainMultiplier, "weightGainMultiplier");
            Scribe_Values.Look<float>(ref statBonuses.weightLossMultiplier, "weightLossMultiplier");
            Scribe_Values.Look<float>(ref statBonuses.digestionRateMultiplier, "digestionRateMultiplier");
            Scribe_Values.Look<float>(ref statBonuses.softLimitMultiplier, "softLimitMultiplier");
            Scribe_Values.Look<float>(ref statBonuses.stomachElasticityMultiplier, "stomachElasticityMultiplier");
            Scribe_Values.Look<float>(ref statBonuses.hardLimitAdditionalPercentageMultiplier, "hardLimitAdditionalPercentageMultiplier");

            Scribe_Values.Look<float>(ref statBonuses.weightGainMultBonus, "weightGainBonus");
            Scribe_Values.Look<float>(ref statBonuses.weightLossMultBonus, "weightLossBonus");


            Scribe_Values.Look<float>(ref statBonuses.digestionRateFlatBonus, "digestionRateFlatBonus");
            Scribe_Values.Look<float>(ref statBonuses.softLimitFlatBonus, "softLimitFlatBonus");
            Scribe_Values.Look<float>(ref statBonuses.stomachElasticityFlatBonus, "stomachElasticityFlatBonus");

            Scribe_Values.Look<float>(ref statBonuses.hardLimitAdditionalPercentageMultBonus, "hardLimitAdditionalPercentageBonus");
            Scribe_Values.Look<float>(ref statBonuses.fullnessGainedMultBonus, "fullnessGainedMultiplierBonus");
            Scribe_Values.Look<float>(ref statBonuses.movementFlatBonus, "flatMoveBonus");
            Scribe_Values.Look<float>(ref statBonuses.manipulationFlatBonus, "flatManipulationBonus");
            Scribe_Values.Look<float>(ref statBonuses.eatingFlatBonus, "flatEatingSpeedBonus");
            Scribe_Values.Look<float>(ref statBonuses.movementPenaltyMitigationMultBonus_Weight, "movementPenaltyMitigationMultBonus_Weight");
            Scribe_Values.Look<float>(ref statBonuses.movementPenaltyMitigationMultBonus_Fullness, "movementPenaltyMitigationMultBonus_Fullness");
            Scribe_Values.Look<float>(ref statBonuses.eatingSpeedReductionMitigationMultBonus_Fullness, "eatingSpeedReductionMitigationMultBonus_Fullness");
            Scribe_Values.Look<float>(ref statBonuses.manipulationPenaltyMitigationMultBonus_Weight, "manipulationPenaltyMitigationMultBonus_Weight");
            Scribe_Values.Look<float>(ref statBonuses.painMitigationMultBonus_Fullness, "painMitigationMultBonus_Fullness");


            return;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Disabled) yield break;

            if (GlobalSettings.showPawnDietManagementGizmo && ShouldShowWeightGizmo())
            {
                // A selected outsider still has readable body information.
                // Only an explicitly manual, editable pawn gets the sliders.
                yield return MakeBodyGoalCommand();
                if (!HasAssignedBodyGoal || UsesBodyGoals ||
                    !BodyModelEligibility.CanManage(parent as Pawn)) yield break;
                // Map generation can spawn pawns on a worker thread. Build the
                // Unity-backed gizmo only when the UI actually asks for it.
                if (weightGizmo == null)
                    weightGizmo = new WeightGizmo(this);
                yield return weightGizmo;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            InitializePerksIfNull();
            if (Disabled) { return; }


            if (((Pawn)parent)?.RaceProps.Humanlike ?? false)
            {
                // The shared initializer also handles becoming eligible without
                // spawning; do not create a different starting mass here.
                InitValuesOnLoad();

                // If this is called before InitValuesOnLoad(), sliders will be set to incorrect postions.
                if (!_loadedDietBars)
                {
                    SetRangesByValue(cachedSliderVal1, cachedSliderVal2);
                    _loadedDietBars = true;
                }

                HistoryAutoRecorderGroupWeight.Instance().AddHistoryRecorders(this.parent.AsPawn());
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (Disabled) { return; }

            if (!parent.Spawned && !parent.AsPawn().IsCaravanMember())
                return;

            if (parent.AsPawn().WeightHediff() == null)
                InitializeBodyState();

            ProcessWeightGainRequests(GlobalSettings.ticksBetweenWeightGainRequestProcess.threshold);
            ProcessWeightLossRequests(GlobalSettings.ticksBetweenWeightGainRequestProcess.threshold);

            // Physiology follows Need_Food.NeedInterval, including caravans.

            if (parent.IsHashIntervalTick(60))
                CumulativeSeverityKilosGained -= immunitySeverityDecay;


            if (parent.Spawned) 
            {
                // Muted pawn audio needs no live voices or sound-selection work.
                // End existing loops so unmuting starts with current pawn state.
                if (Prefs.VolumeMaster <= 0f || Prefs.VolumeGame <= 0f)
                {
                    DespawnSustainers();
                    return;
                }
                DoFootstepSounds();
                // These ambient one-shots already wait several seconds between
                // plays. Stagger their eligibility checks once per game second;
                // movement sounds and active sustainers retain their tick timing.
                if (parent.IsHashIntervalTick(60))
                {
                    DoEmptyStomachSounds();
                    DoGurgleSounds();
                    if (!IsConnectedToFeedingMachine)
                        DoBurpSounds();
                    DoStomachStretchSounds();
                }

                DoSloshSounds();

                if (!IsConnectedToFeedingMachine && parent.IsHashIntervalTick(TICK_CHECK_INTERVAL_FOR_BREATHING))
                {
                    DoBreathingSounds();
                }
            }
        }

        private void DespawnSustainers() 
        {
            sloshSound?.End();
            breathSound?.End();

            sloshSound = null;
            breathSound = null;
        }

        private void DoStomachStretchSounds()
        {
            const float SECONDS_BETWEEN_STRETCH_SOUNDS = 4;
            SoundDef soundDef = SoundUtility.GetStomachStretchingSoundByFullness(this);
            SoundUtility.PlayOneShotForPawnIfNotWaiting(parent.AsPawn(), soundDef, SECONDS_BETWEEN_STRETCH_SOUNDS);
        }

        private bool _isConnectedToFeedingMachine = false;
        public bool IsConnectedToFeedingMachine 
        {
            get 
            {
                return _isConnectedToFeedingMachine;
            }
            set 
            {
                _isConnectedToFeedingMachine = value;
            }
        }

        const int TICK_CHECK_INTERVAL_FOR_BREATHING = 60 * 3;
        private void DoBreathingSounds()
        {
            BodyTypeDef thresholdForBreathingAllTheTime = Defs.BodyTypeDefOf.F_050_MorbidlyObese;
            float fullnessPercentForBreathing = 1f; // As percent of soft limit

            if (!IsConnectedToFeedingMachine && ((parent?.AsPawn()?.pather?.Moving ?? false) ||
                (fullnessbar?.CurrentFullnessAsPercentOfSoftLimit ?? 0) >= fullnessPercentForBreathing ||
                BodyTypeUtility.PawnIsOverWeightThreshold(parent.AsPawn(), thresholdForBreathingAllTheTime)))
            {
                if (breathSound == null || breathSound.Ended)
                {
                    var soundDef = SoundUtility.GetBreathingSoundByWeightOpinionAndGender(parent.AsPawn());
                    breathSound = soundDef.TrySpawnSustainer(SoundInfo.InMap(parent.AsPawn()));
                }
            }
            else
            {
                breathSound?.End();
                breathSound = null;
            }
        }

        private void DoSloshSounds() 
        {
            if (!parent.Spawned)
            { 
                return; 
            }

            const int TICKS_PER_SECOND = 60;
            if (SloshDurationSeconds <= 0) 
            {
                return;
            }

            if (SloshStartTick + (SloshDurationSeconds * TICKS_PER_SECOND) <= Find.TickManager.TicksAbs) 
            {
                SloshStartTick = 0;
                SloshDurationSeconds = 0;
                sloshSound?.End();
                sloshSound = null;
                return;
            }

            if (sloshSound == null)
            {
                SoundDef sound = SoundUtility.GetStomachSloshByWeight(this);
                if (sound != null && SoundUtility.PawnShouldPlaySound(parent.AsPawn()))
                {
                    sloshSound = sound.TrySpawnSustainer(SoundInfo.InMap(parent.AsPawn()));
                }
            }
        }


        private void DoGurgleSounds()
        {
            const float SECONDS_BETWEEN_GURGLE_SOUNDS = 10;
            SoundDef soundDef = SoundUtility.GetStomachGurgleSoundsByWeight(this);
            SoundUtility.PlayOneShotForPawnIfNotWaiting(parent.AsPawn(), soundDef, SECONDS_BETWEEN_GURGLE_SOUNDS);
        }

        private void DoBurpSounds() 
        {
            const float PERCENT_VARIATION = 0.4f;
            float randomDelay = (1 + ((float)Values.random.NextDouble() - 0.5f) * PERCENT_VARIATION) * GlobalSettings.soundBurpDelaySeconds.threshold;
            SoundDef soundDef = SoundUtility.GetBurpSoundsByWeight(this);
            SoundUtility.PlayOneShotForPawnIfNotWaiting(parent.AsPawn(), soundDef, randomDelay);
        }

        private void DoEmptyStomachSounds()
        {
            const float SECONDS_BETWEEN_EMPTY_SOUND = 10;
            SoundDef soundDef = SoundUtility.GetEmptyStomachSoundsByWeight(this);
            SoundUtility.PlayOneShotForPawnIfNotWaiting(parent.AsPawn(), soundDef, SECONDS_BETWEEN_EMPTY_SOUND);
        }

        private void DoFootstepSounds()
        {
            SoundDef footstepSound = SoundUtility.GetFootStepSoundsByWeightAndMovement(this);
            if (footstepSound == null) { return; }
            footstepSound.PlayOneShot(SoundInfo.InMap(new TargetInfo(this.parent)));
        }

        private List<string> _perkNamesForSaving = new List<string>();
        private List<int> _perkLevelValuesForSaving = new List<int>();

        private void ExposePerkLevels()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (perkLevels?.PerkToLevels == null)
                    return;

                _perkNamesForSaving = new List<string>();
                _perkLevelValuesForSaving = new List<int>();

                foreach (var x in perkLevels.PerkToLevels)
                {
                    _perkNamesForSaving.Add(x.Key);
                    _perkLevelValuesForSaving.Add(x.Value);
                }
                _perkLevelsToSpendForSaving = perkLevels.availablePoints;
                _currentLevelForSaving = perkLevels.currentLevel;

                Scribe_Values.Look<int>(ref _currentLevelForSaving, "currentLevelForSaving", 0);
                Scribe_Values.Look<int>(ref _perkLevelsToSpendForSaving, "perkLevelsToSpend", 0);
                Scribe_Collections.Look<string>(ref _perkNamesForSaving, "perkNamesForSaving", LookMode.Value);
                Scribe_Collections.Look<int>(ref _perkLevelValuesForSaving, "perkValuesForSaving", LookMode.Value);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (_perkLevelValuesForSaving is null)
                    _perkLevelValuesForSaving = new List<int>();
                if (_perkNamesForSaving is null)
                    _perkNamesForSaving = new List<string>();

                Scribe_Values.Look<int>(ref _currentLevelForSaving, "currentLevelForSaving", 0);
                Scribe_Values.Look<int>(ref _perkLevelsToSpendForSaving, "perkLevelsToSpend", 0);
                Scribe_Collections.Look<string>(ref _perkNamesForSaving, "perkNamesForSaving", LookMode.Value);
                Scribe_Collections.Look<int>(ref _perkLevelValuesForSaving, "perkValuesForSaving", LookMode.Value);
                // Initialize the perk levels dictionary somewhere else (like PostLoadInit())
            }

        }

        private void InitializePerksIfNull()
        {
            if (perkLevels is null)
                perkLevels = new PerkLevels();

            if (!(perkLevels.PerkToLevels is null))
                return;

            perkLevels.PerkToLevels = new Dictionary<string, int>() { };

            for (int i = 0; i < Perks.basicPerks.Count; ++i)
            {
                perkLevels.PerkToLevels.Add(Perks.basicPerks[i].perkName, 0);
            }
            for (int i = 0; i < Perks.advancedPerks.Count; ++i)
            {
                perkLevels.PerkToLevels.Add(Perks.advancedPerks[i].perkName, 0);
            }
            for (int i = 0; i < Perks.elitePerks.Count; ++i)
            {
                perkLevels.PerkToLevels.Add(Perks.elitePerks[i].perkName, 0);
            }
            for (int i = 0; i < Perks.ultimatePerks.Count; ++i)
            {
                perkLevels.PerkToLevels.Add(Perks.ultimatePerks[i].perkName, 0);
            }

            // Set values from save
            perkLevels.availablePoints = _perkLevelsToSpendForSaving;
            perkLevels.currentLevel = Mathf.Max(1, _currentLevelForSaving);
            if (_perkNamesForSaving is null || _perkNamesForSaving.Count == 0)
                return;

            for (int i = 0; i < _perkNamesForSaving.Count(); ++i)
            {
                // Keep retired/unknown purchases serialized for the later perk
                // migration. A missing parallel value is an unpurchased level.
                if (!string.IsNullOrEmpty(_perkNamesForSaving[i]))
                    perkLevels.PerkToLevels[_perkNamesForSaving[i]] = _perkLevelValuesForSaving != null && i < _perkLevelValuesForSaving.Count
                        ? _perkLevelValuesForSaving[i] : 0;
            }
        }

        public void ProcessWeightLossRequests(int ticksBetweenChecks)
        {
            if (!parent.IsHashIntervalTick(ticksBetweenChecks))
                return;

            // Keep the existing budget of one reversal per check, even after
            // loading a backlog. Arrival order need not be deadline order.
            WeightGainRequest gainRequest;
            if (Simulation.WeightChangeScheduler.TryDequeueDue(this.activeWeightLossRequests,
                Find.TickManager.TicksGame, out gainRequest))
                ChangeWeightAndUpdateSprite(gainRequest);
        }

        public void ProcessWeightGainRequests(int ticksBetweenChecks)
        {
            if (!parent.IsHashIntervalTick(ticksBetweenChecks))
                return;

            int currentTick = Find.TickManager.TicksGame;
            WeightGainRequest gainRequest;
            // Apply at most one pulse per check; a later-enqueued pulse may
            // become due before the queue head.
            if (Simulation.WeightChangeScheduler.TryDequeueDue(this.activeWeightGainRequests,
                currentTick, out gainRequest))
            {
                float gainedSeverity = ChangeWeightAndUpdateSprite(gainRequest);
                CreateWLRequestIfNonZeroDuration(currentTick, gainRequest, gainedSeverity);
            }
        }


        /// <summary>
        /// Actually applies weight gain request to pawn and updates sprite.
        /// </summary>
        /// <param name="gainRequest">Weight gain request to process</param>
        /// <returns>Change in severity</returns>
        private float ChangeWeightAndUpdateSprite(WeightGainRequest gainRequest)
        {
            float actualGainedSeverity = Utilities.HediffUtility.AddHediffSeverity(
                 Defs.HediffDefOf.RimRound_Weight,
                 this.parent.AsPawn(),
                 Utilities.HediffUtility.KilosToSeverityWithoutBaseWeight(gainRequest.amountToGain),
                 false,
                 false,
                 gainRequest.useMultipliers);

            var pbtThingComp = parent.TryGetComp<PawnBodyType_ThingComp>();
            if (pbtThingComp != null) 
            {
                var bodyUpdated = BodyTypeUtility.UpdatePawnSprite(parent.AsPawn(), pbtThingComp.PersonallyExempt, pbtThingComp.CategoricallyExempt);

                if (bodyUpdated)
                {
                    SoundDef bwomfSound = Utilities.SoundUtility.GetBwomfSoundByWeight(parent.AsPawn());
                    if (SoundUtility.PawnShouldPlaySound(parent.AsPawn())) 
                    {
                        bwomfSound.PlayOneShot(SoundInfo.InMap(new TargetInfo(this.parent)));
                    }
                }
            }

            return actualGainedSeverity;
        }


        private void CreateWLRequestIfNonZeroDuration(int currentTick, WeightGainRequest gainRequest, float severityChangeFromPriorGain)
        {
            if (gainRequest.duration > 0)
            {
                this.activeWeightLossRequests.Enqueue(
                    new WeightGainRequest(
                        -1*Utilities.HediffUtility.SeverityToKilosWithoutBaseWeight(severityChangeFromPriorGain), 
                        currentTick + gainRequest.duration, 
                        0, 
                        gainRequest.triggerMessages,
                        false));
            }
        }

        public void PassiveWeightLossTick()
        {
            // Retained for binary compatibility. Native Food need intervals now
            // own expenditure, so callers must not charge it a second time.
        }

        public void ActiveWeightGainTick(float nutrition)
        {
            if (UsesBodyModel)
                bodyEnergy.RecordIntake(nutrition);
        }

        public void FullnessCheckTick()
        {

            float severity = (CurrentFullness > 0 ? CurrentFullness / Mathf.Max(HardLimit, 0.01f) : 0.01f);
            Utilities.HediffUtility.SetHediffSeverity(Defs.HediffDefOf.RimRound_Fullness, (Pawn)parent, severity);

            return;
        }

        public void RuptureStomachCheckTick()
        {
            float severity = (CurrentFullness > 0 ? CurrentFullness / Mathf.Max(HardLimit, 0.01f) : 0.01f);

            if (severity > Defs.HediffDefOf.RimRound_Fullness.stages.Last().minSeverity)
            {
                float vomitChance = Values.RandomFloat(0, 1);
                if (vomitChance >= 0.50)
                    ((Pawn)parent).jobs.StartJob(
                        JobMaker.MakeJob(RimWorld.JobDefOf.Vomit),
                        JobCondition.InterruptForced,
                        null, true, true, null, null, false, false);

                RuptureStomach();

                CurrentFullness = SoftLimit * (1 - Values.RandomFloat(0.1f, 0.4f));
            }

            return;
        }

        public void StomachGrowthTick()
        {
            if (CurrentFullness > SoftLimit)
                softLimitPersonal += StomachElasticity * GlobalSettings.ticksPerHungerCheck.threshold;

            return;
        }

        public float DigestionTick()
        {
            float amountToSubtract = DigestionRate * GlobalSettings.ticksPerHungerCheck.threshold;

            if (CurrentFullness > amountToSubtract)
            {
                CurrentFullness -= amountToSubtract;
                return amountToSubtract;
            }
            else
            {
                float tmp = CurrentFullness;
                CurrentFullness = 0;
                return tmp;
            }
        }

        public void RuptureStomach()
        {
            BodyPartRecord pawnStomach = ((Pawn)parent).RaceProps.body
                .GetPartsWithDef(DefDatabase<BodyPartDef>.GetNamed("Stomach", true))
                .FirstOrDefault();
            if (pawnStomach == null)
                return;
            float currentStomachHealth = ((Pawn)parent).health.hediffSet.GetPartHealth(pawnStomach);
            float afterRuptureStomachHealth = 2;

            if (currentStomachHealth <= afterRuptureStomachHealth)
                return;

            int numberOfWounds = Values.RandomInt(1, 5);
            float damageVariationPercent = 0.50f;
            float remainingDamage = (currentStomachHealth - afterRuptureStomachHealth);
            float damagePerWound = remainingDamage / numberOfWounds;

            while (remainingDamage > 0)
            {
                float thisDmg = Values.RandomFloat(1 - damageVariationPercent, 1 + damageVariationPercent) * damagePerWound;

                if (thisDmg > remainingDamage)
                    thisDmg = remainingDamage;

                DamageInfo stomachRuptureDamageInfo = new DamageInfo(
                    Defs.DamageDefOf.RR_StomachBurst,
                    thisDmg, 0, -1, null, pawnStomach);

                remainingDamage -= thisDmg;

                ((Pawn)parent).TakeDamage(stomachRuptureDamageInfo);
            }
        }

        private float currentFullnessToNutritionRatio = defaultFullnessToNutritionRatio;

        public float CurrentFullnessToNutritionRatio
        {
            get
            {
                return currentFullnessToNutritionRatio;
            }
            set
            {
                currentFullnessToNutritionRatio = float.IsNaN(value) || float.IsInfinity(value) || value <= 0f
                    ? defaultFullnessToNutritionRatio : value;
            }
        }

        public void UpdateRatio(float incomingNutrition, float incomingRatio = defaultFullnessToNutritionRatio)
        {
            if (float.IsNaN(incomingNutrition) || float.IsInfinity(incomingNutrition) || incomingNutrition <= 0f)
                return;
            if (float.IsNaN(incomingRatio) || float.IsInfinity(incomingRatio) || incomingRatio <= 0f)
                incomingRatio = defaultFullnessToNutritionRatio;
            //Weighted average of current values and incoming values  
            CurrentFullnessToNutritionRatio =
                (CurrentFullness + incomingRatio * incomingNutrition) /
                ((CurrentFullness / CurrentFullnessToNutritionRatio) + incomingNutrition);
        }


        public DietMode DietMode
        {
            get
            {
                return dietMode;
            }
            set
            {
                dietMode = value;
                this.UpdateDietBars();
            }

        }

        private DietMode dietMode = DietMode.Nutrition;

        public DietMode preCaravanDietMode = DietMode.Nutrition;

        public bool defaultBodyTypeForced = false;

        private BodyTypeDef defaultBodyType;

        public BodyTypeDef DefaultBodyType
        {
            get
            {
                string savedNative = parent?.TryGetComp<PawnBodyType_ThingComp>()?.NativeBodyDefName;
                BodyTypeDef preserved = savedNative == null ? null : DefDatabase<BodyTypeDef>.GetNamedSilentFail(savedNative);
                if (preserved != null && !BodyTypeUtility.IsRRBody(preserved))
                    return preserved;
                if (defaultBodyType is null || BodyTypeUtility.IsRRBody(defaultBodyType))
                {

                    defaultBodyType = RimWorld.BodyTypeDefOf.Thin;
                }
                return defaultBodyType;
            }
            set
            {
                defaultBodyType = value;
                var pawn = parent as Pawn;
                var presentation = pawn?.TryGetComp<PawnBodyType_ThingComp>();
                if (value != null && pawn?.story?.bodyType == value &&
                    presentation != null && presentation.NativeBodyDefName != value.defName)
                    presentation.ObserveExternalNativeBody(value);
            }
        }

        //How much (in liters) the stomach grows when over the softlimit per tick.
        public float StomachElasticity
        {
            get
            {
                float baseStomachElasticity = 0.00001f;
                float endlessIndulgenceMultBonus = perkLevels.EffectLevel("RR_Endless_Indulgence_Title") ?? 0;
                float elasticityValue = baseStomachElasticity *
                        (GlobalSettings.stomachElasticityMultiplier.threshold *
                        (1 + endlessIndulgenceMultBonus) *
                        (1 + EffectiveStatBonuses.stomachElasticityMultiplier)) +
                        EffectiveStatBonuses.stomachElasticityFlatBonus;

                return Mathf.Clamp(elasticityValue, 0, float.MaxValue);
            }
        }

        public float DigestionRate
        {
            get
            {
                float digestionBeyondQuestionMult = (perkLevels.EffectLevel("RR_Digestion_Beyond_Question_Title") ?? 0) * 0.2f;
                float gigaGurglingMult = (perkLevels.EffectLevel("RR_GigaGurgling_Title") ?? 0) * 0.5f;
                float titaniumStomachMultBonus = (perkLevels.EffectLevel("RR_TitaniumStomach_Title") ?? 0) * 1f;

                float baseDigestionRate = 3.0f;

                return Mathf.Clamp(
                        (GlobalSettings.digestionRateMultiplier.threshold *
                        baseDigestionRate *
                        (float)(this.parent.AsPawn().needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed, true)) *
                        (1 + EffectiveStatBonuses.digestionRateMultiplier + gigaGurglingMult + digestionBeyondQuestionMult + titaniumStomachMultBonus) *
                        HungerDroneUtility.GetCurrentHungerMultiplierFromDrone(this.parent.AsPawn())) +
                        EffectiveStatBonuses.digestionRateFlatBonus,
                    0,
                    float.MaxValue);
            }
        }

        public float WeightGainMultiplier
        {
            get
            {
                int apexAbsorbtionLevel = perkLevels.EffectLevel("RR_Apex_Absorption_Title") ?? 0;
                int wg4000Level = perkLevels.EffectLevel("RR_WeightGain4000_Title") ?? 0;
                int makesAllTheRulesLevel = perkLevels.EffectLevel("RR_MakesAllTheRules_Title") ?? 0;
                int heavyRevianLevel = perkLevels.EffectLevel("RR_HeavyRevian_Title") ?? 0;
                int thatLevel = perkLevels.EffectLevel("RR_That_Title") ?? 0;
                int inescapableInfinityLevel = perkLevels.EffectLevel("RR_InescapableInfinity_Title") ?? 0;
                int peakEvolutionLevel = perkLevels.EffectLevel("RR_PeakEvolution_Title") ?? 0;

                if (makesAllTheRulesLevel > 0)
                {
                    Map currentMap = this.parent.Map;
                    if (currentMap != null)
                    {
                        Vector2 vector = Find.WorldGrid.LongLatOf(currentMap.Tile);

                        if (!(GenDate.HourFloat(Find.TickManager.TicksAbs, vector.x) is float time &&
                            (time >= 18.0 ||
                            time <= 6.0)))
                        {
                            makesAllTheRulesLevel = 0;
                        }
                    }
                }

                return Mathf.Clamp((1 + EffectiveStatBonuses.weightGainMultiplier) *
                        GlobalSettings.weightGainMultiplier.threshold *
                        (this.parent.AsPawn().gender == Gender.Female ?
                            GlobalSettings.weightGainMultiplierFemale.threshold :
                            GlobalSettings.weightGainMultiplierMale.threshold) +
                        (EffectiveStatBonuses.weightGainMultBonus + EquipmentQuality.RestingBonus(parent as Pawn) +
                        0.1f * apexAbsorbtionLevel +
                        0.2f * wg4000Level +
                        0.5f * heavyRevianLevel +
                        1.0f * peakEvolutionLevel +
                        4.0f * makesAllTheRulesLevel +
                        2.0f * thatLevel +
                        5.0f * inescapableInfinityLevel),
                    0, float.MaxValue);
            }
        }


        public float WeightLossMultiplier
        {
            get
            {
                int dietPlanLevel = perkLevels.EffectLevel("RR_Diet_Plan_Title") ?? 0;
                return Mathf.Clamp((1 + EffectiveStatBonuses.weightLossMultiplier) *
                        GlobalSettings.weightLossMultiplier.threshold *
                        (this.parent.AsPawn().gender == Gender.Female ?
                            GlobalSettings.weightLossMultiplierFemale.threshold :
                            GlobalSettings.weightLossMultiplierMale.threshold) +
                        (EffectiveStatBonuses.weightLossMultBonus +
                        0.2f * dietPlanLevel),
                    0, float.MaxValue);
            }
        }




        // Passenger bulk belongs to the current holder. Never store it in the food
        // ledger: meals keep their own volume, digestion rate and rupture thresholds.
        public float PassengerVolume => ExternalNutrition.PassengerVolume(this);

        public float OccupiedVolume => (float)Math.Min(float.MaxValue, (double)CurrentFullness + PassengerVolume);

        public float RemainingFullnessUntil(float limit)
        {
            return limit - OccupiedVolume;
        }

        internal string PassengerBreakdown(float passengers)
        {
            return "RR_PassengerOccupancy".Translate(CurrentFullness.ToString("F2"), passengers.ToString("F2"),
                Mathf.Max(0f, SoftLimit - CurrentFullness - passengers).ToString("F2")).ToString();
        }


        //Current actual fullness. Displayed as the bar
        private float currentFullness = 0.0f;

        public float CurrentFullness
        {
            get
            {
                return currentFullness;
            }
            set
            {
                currentFullness = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
            }
        }


        //-------------------
        public float debugSoftLimitDelta = 0;
        //In liters. represents threshold of stoach capacity. Initial value at pawn spawn.
        private float softLimitPersonal = defaultSoftLimit + Values.RandomFloat(softLimitVariation.x, softLimitVariation.y);
        public float SoftLimit
        {
            get
            {
                float blackHolePerkBonus = 50f * (perkLevels.EffectLevel("RR_BlackHole_Title") ?? 0);
                float oneMoreBitePerkBonus = 1f * (perkLevels.EffectLevel("RR_OneMoreBite_Title") ?? 0);
                float debugIncrease = debugSoftLimitDelta;

                float capacity = Mathf.Clamp(softLimitPersonal *
                    (GlobalSettings.softLimitMuliplier.threshold *
                    (1 + EffectiveStatBonuses.softLimitMultiplier)) +
                    EffectiveStatBonuses.softLimitFlatBonus + blackHolePerkBonus + oneMoreBitePerkBonus + debugIncrease, 0, float.MaxValue);
                if (float.IsNaN(capacity) || float.IsInfinity(capacity) || capacity <= 0f) capacity = defaultSoftLimit;
                return ColonyProgression.LegacyPerksActive ? capacity :
                    adaptation.ConditionedCapacity(capacity, FullnessGainedMultiplier);
            }
        }

        public bool SetAboveHardLimit
        {
            get
            {
                return fullnessbar?.peaceForeverHeld ?? false;
            }
        }

        //How much more than the soft limit the Hard Limit is.
        private float hardLimitAdditionalPercentage = 0.3f;
        public float HardLimit
        {
            get
            {
                float limitBreakMultBonus = (perkLevels.EffectLevel("RR_LimitBreak_Title") ?? 0) * 0.1f;

                float capacity = SoftLimit *
                    (1f +
                    Mathf.Clamp(
                        hardLimitAdditionalPercentage *
                        ((1 + limitBreakMultBonus) *
                        (1 + EffectiveStatBonuses.hardLimitAdditionalPercentageMultiplier) *
                        GlobalSettings.hardLimitMuliplier.threshold +
                        EffectiveStatBonuses.hardLimitAdditionalPercentageMultBonus), 0.3f, 10)
                    );
                return float.IsNaN(capacity) || float.IsInfinity(capacity) || capacity <= 0f
                    ? Mathf.Min(SoftLimit, float.MaxValue / 1.3f) * 1.3f : capacity;
            }
        }



        bool ShouldShowWeightGizmo()
        {
            List<object> selectedPawns = new List<object>();
            foreach (object o in Find.Selector.SelectedObjects)
            {
                if (o.AsPawn() is null)
                {
                    continue;
                }
                else
                {
                    selectedPawns.Add(o);
                }
            }

            if (selectedPawns.Count > 1)
                return false;
            else
                return true;
        }

        public Pair<float, float> GetRanges()
        {
            if (nutritionbar is null || fullnessbar is null)
                return new Pair<float, float>(-1, -1);

            switch (this.DietMode)
            {
                case DietMode.Nutrition:
                    return nutritionbar.GetRanges();
                case DietMode.Hybrid:
                    return new Pair<float, float>(
                        nutritionbar.GetRanges().First,
                        fullnessbar.GetRanges().First);
                case DietMode.Fullness:
                    return fullnessbar.GetRanges();
                case DietMode.Disabled:
                    return new Pair<float, float>(-1, -1);
                default:
                    Log.Error("WeightGizmo_ThingComp GetRanges() ran default case!");
                    return new Pair<float, float>(-1, -1);
            }
        }


        public void SetRangesByValue(float first, float second)
        {
            float maxNutrition = nutritionbar.needFood.MaxLevel;
            float maxDisplayFullness = fullnessbar.DisplayLimit;
            switch (this.DietMode)
            {
                case DietMode.Nutrition:
                    nutritionbar.SetRanges(first / maxNutrition, second / maxNutrition);
                    return;
                case DietMode.Hybrid:
                    nutritionbar.SetRanges(first / maxNutrition, 0);
                    fullnessbar.SetRanges(second / maxDisplayFullness, 0);
                    return;
                case DietMode.Fullness:
                    fullnessbar.SetRanges(first / maxDisplayFullness, second / maxDisplayFullness);
                    return;
                case DietMode.Disabled:
                    return;
                default:
                    Log.Error("WeightGizmo_ThingComp SetRanges() ran default case!");
                    return;
            }

        }
        public void SetRangesByPercent(float first, float second)
        {
            switch (this.DietMode)
            {
                case DietMode.Nutrition:
                    if (nutritionbar != null)
                        nutritionbar.SetRanges(first, second);
                    return;
                case DietMode.Hybrid:
                    if (nutritionbar != null)
                        nutritionbar.SetRanges(first, 0);
                    if (fullnessbar != null)
                        fullnessbar.SetRanges(second, 0);
                    return;
                case DietMode.Fullness:
                    if (fullnessbar != null)
                        fullnessbar.SetRanges(first, second);
                    return;
                case DietMode.Disabled:
                    return;
                default:
                    Log.Error("WeightGizmo_ThingComp SetRanges() ran default case!");
                    return;
            }
        }


        //100% for fullness is hardlimit
        public void SetRangesPercent(float first, float second)
        {
            if (first > 1 || second > 1)
            {
                Log.Error("Inputs for SetRangesPercent must be less than or equal to 1!");
                return;
            }

            float maxNutrition = parent.AsPawn().needs.food.MaxLevel;
            float maxFullness = this.HardLimit;

            switch (this.DietMode)
            {
                case DietMode.Nutrition:
                    nutritionbar.SetRanges(first * maxNutrition, second * maxNutrition);
                    return;
                case DietMode.Hybrid:
                    nutritionbar.SetRanges(first * maxNutrition, 0);
                    fullnessbar.SetRanges(second * maxFullness, 0);
                    return;
                case DietMode.Fullness:
                    fullnessbar.SetRanges(first * maxFullness, second * maxFullness);
                    return;
                case DietMode.Disabled:
                    return;
                default:
                    Log.Error("WeightGizmo_ThingComp SetRanges() ran default case!");
                    return;
            }

        }

        public void UpdateDietBars()
        {
            if (nutritionbar != null)
                nutritionbar.UpdateBar(this.DietMode);
            if (fullnessbar != null)
                fullnessbar.UpdateBar(this.DietMode);
        }

        public float FullnessGainedMultiplier
        {
            get
            {
                return Mathf.Clamp(
                    GlobalSettings.fullnessMultiplier.threshold + EffectiveStatBonuses.fullnessGainedMultBonus, 0, 10);
            }
        }

        public float ConsumedNutrition
        {
            get => consumedNutrition;
            set
            {
                float positiveIntake = Mathf.Max(0f, value - consumedNutrition);
                consumedNutrition = value;
                if (!ColonyProgression.LegacyPerksActive) return;
                legacyPerkNutrition += positiveIntake;
                if (legacyPerkNutrition >= GlobalSettings.nutritionPerPerkLevel.threshold * perkLevels.currentLevel)
                {
                    int currentLevel = Mathf.FloorToInt(legacyPerkNutrition / GlobalSettings.nutritionPerPerkLevel.threshold) + 1;
                    int levelsGained = currentLevel - perkLevels.currentLevel;

                    if (levelsGained < 0)
                        Log.Error("Error: Levels gained was negative.");

                    perkLevels.currentLevel = currentLevel;
                    perkLevels.availablePoints += levelsGained * GlobalSettings.levelsGainedPerLevel.threshold;
                }
            }
        }


        public float SloshDurationSeconds { get; set; }
        public float SloshStartTick { get; set; }


        private float consumedNutrition = 0;
        private int _perkLevelsToSpendForSaving = 0;
        private int _currentLevelForSaving = 0;

        public RimRoundStatBonuses statBonuses = new RimRoundStatBonuses();

        public Queue<WeightGainRequest> activeWeightGainRequests = new Queue<WeightGainRequest>();
        public Queue<WeightGainRequest> activeWeightLossRequests = new Queue<WeightGainRequest>();

        public PerkLevels perkLevels = new PerkLevels();

        public WeightGizmo weightGizmo;
        public WeightGizmo_FullnessBar fullnessbar;
        public WeightGizmo_NutritionBar nutritionbar;

        float cachedSliderVal1 = 0.30f;
        float cachedSliderVal2 = 0.90f;

        public const float defaultSoftLimit = 0.9f;
        public static Vector2 softLimitVariation = new Vector2(-0.10f, 0.50f);

        public const float defaultFullnessToNutritionRatio = 1f; //i.e. 0.5 Fullness for 1 nutrition is 0.5f

        private float cumulativeSeverityGained = 0;
        public float CumulativeSeverityKilosGained { get => cumulativeSeverityGained; set => cumulativeSeverityGained = value; }

        public const float severityUntilImmunity = 400;
        public const float immunitySeverityDecay = 0.5f;

        private Sustainer sloshSound;
        private Sustainer breathSound;
    }

    public class PerkLevels
    {
        public int currentLevel = 1;
        public int availablePoints = 0;
        public Dictionary<string, int> PerkToLevels;

        // Saved purchases remain intact, including unknown keys. Only this view
        // is consumed by gameplay; the raw dictionary is retained for migration.
        public int? EffectLevel(string key)
        {
            int level;
            return ColonyProgression.LegacyPerksActive && PerkToLevels != null &&
                PerkToLevels.TryGetValue(key, out level) ? level : 0;
        }
    }

    public struct WeightGainRequest
    {
        /// <param name="amountToGain">Amount of weight to gain in kilograms.</param>
        /// <param name="tickToApplyOn">Tick after which the request should be executed.</param>
        /// <param name="duration">Number of ticks for the weight to stay applied. If set to 0, weight gained is permanent.</param>
        /// <param name="triggerMessages">Whether weight gained from this should trigger the notifications at the top of the screen.</param>
        public WeightGainRequest(float amountToGain, int tickToApplyOn, int duration = 0, bool triggerMessages = false, bool useMultipliers = true)
        {
            this.amountToGain = amountToGain;
            this.tickToApplyOn = tickToApplyOn;
            this.duration = duration;
            this.triggerMessages = triggerMessages;
            this.useMultipliers = useMultipliers;
        }
        public float amountToGain;
        public int tickToApplyOn;
        public int duration;
        public bool triggerMessages;
        public bool useMultipliers;
    }


    public enum DietMode
    {
        Nutrition,
        Hybrid,
        Fullness,
        Disabled
    }
}
