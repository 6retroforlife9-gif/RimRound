using System;
using System.Collections.Generic;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Comps
{
    public partial class FullnessAndDietStats_ThingComp
    {
        private readonly BodyEnergyState bodyEnergy = new BodyEnergyState();
        private readonly FoodEnergyReleaseState foodEnergyRelease = new FoodEnergyReleaseState();
        private BodyGoal bodyGoal = BodyGoal.Maintain;
        private bool bodyGoalAssigned;
        private int bodyGoalAssignmentVersion = 1;
        private float baselineMassKg;
        private float targetMassKg;
        private float dailyIntakeNutrition;
        private float dailyMealBudgetNutrition;
        private float carriedMealAllowance;
        private bool mealBudgetInitialized;
        private int intakeDay = -1;
        private int lastBodyMealTick = -60000;
        private string lastMealOutcome;
        private int lastMealOutcomeTick = -60000;
        public void RecordMealOutcome(string outcome)
        {
            lastMealOutcome = outcome;
            lastMealOutcomeTick = Find.TickManager?.TicksGame ?? 0;
        }
        public string RecentMealOutcome => lastMealOutcome != null &&
            (Find.TickManager?.TicksGame ?? 0) - lastMealOutcomeTick < 60000 ? lastMealOutcome : null;
        public readonly GainMealCycle GainMealState = new GainMealCycle();
        public bool GainMealInProgress => GainMealSession.IsActive(parent as Pawn);
        internal Verse.AI.Job GainMealJob;
        internal int GainMealJobProgress;
        private float lastMassDeltaKg;
        private float observedExpenditure;
        private int observedIntervalTicks;
        private float reserveNutritionUsed;
        private float massProjectionRemainderKg;
        private float dailyMassChangeKg;
        private double externalDigestionNutrition;
        // Social meals compare this counter across the session, including saves.
        public float MealIntakeNutrition => (float)Math.Max(0d, bodyEnergy.TotalIntakeNutritionExact - externalDigestionNutrition);
        private double ordinaryUsedNutrition;
        private double ordinaryUsedVolume;
        private double ordinaryWastedNutrition;
        private double ordinaryWastedVolume;
        private int ordinaryIntakeTransactions;

        public double OrdinaryUsedNutrition => ordinaryUsedNutrition;
        public double OrdinaryUsedVolume => ordinaryUsedVolume;
        public double OrdinaryWastedNutrition => ordinaryWastedNutrition;
        public double OrdinaryWastedVolume => ordinaryWastedVolume;
        public int OrdinaryIntakeTransactions => ordinaryIntakeTransactions;

        public bool UsesBodyModel => BodyModelEligibility.IsEligible(parent as Pawn);
        // Saved goal mode is independent of current command/AI permissions.
        // Unrelated visitors also have a default Maintain value, but no assignment.
        public bool HasAssignedBodyGoal => bodyGoalAssigned;
        public BodyGoal RawAssignedBodyGoal => bodyGoal;
        public bool UsesBodyGoals => bodyGoalAssigned && bodyGoal != BodyGoal.Legacy;
        public BodyGoal Goal => ResolveEffectiveBodyGoal();
        public float PendingLegacyNutrition => bodyEnergy.LegacyNutritionRemaining;
        public int LastBodyMealTick => lastBodyMealTick;
        public float TargetBodyMassKg => LiveGoalTarget;
        public float BaselineBodyMassKg => baselineMassKg;
        public BodyEnergyState EnergyState => bodyEnergy;
        public FoodEnergyReleaseState FoodReleaseState => foodEnergyRelease;
        public double ProjectedEnergyNutrition => bodyEnergy.PendingEnergyNutritionExact + foodEnergyRelease.UnabsorbedNutrition;
        public float DailyIntakeNutrition
        {
            get { ResetIntakeDay(); return dailyIntakeNutrition; }
        }
        public float CarriedMealAllowance
        {
            get { ResetIntakeDay(); return carriedMealAllowance; }
        }
        public float EstimatedDailyExpenditureNutrition => UsesBodyModel
            ? Mathf.Max(0f, ((Pawn)parent).needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed, true) * 60000f)
            : 0f;

        private void ExposeBodyState()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                InitializeBodyGoalAssignment();
                GainMealSession.IsActive(parent as Pawn);
            }
            Scribe_Values.Look(ref bodyEnergy.SchemaVersion, "rrBodySchema", 0);
            Scribe_Values.Look(ref bodyEnergy.LegacyNutritionRemaining, "rrLegacyNutrition", 0f);
            Scribe_Values.Look(ref bodyEnergy.LegacyVolumeRemaining, "rrLegacyVolume", 0f);
            Scribe_Values.Look(ref bodyEnergy.PendingEnergyNutritionExact, "rrEnergyBalance", 0d);
            Scribe_Values.Look(ref bodyEnergy.TotalIntakeNutritionExact, "rrEnergyIntake", 0d);
            Scribe_Values.Look(ref bodyEnergy.TotalExpenditureNutritionExact, "rrEnergyExpenditure", 0d);
            Scribe_Values.Look(ref bodyEnergy.MealGainEligibleNutrition, "rrMealGainEnergy", 0d);
            Scribe_Values.Look(ref foodEnergyRelease.SchemaVersion, "rrFoodReleaseSchema", 0);
            Scribe_Values.Look(ref foodEnergyRelease.OrdinaryNutrition, "rrUndigestedNutrition", 0d);
            Scribe_Values.Look(ref foodEnergyRelease.OrdinaryVolume, "rrUndigestedVolume", 0d);
            Scribe_Values.Look(ref foodEnergyRelease.OrdinaryMealGainEligible, "rrUndigestedMealGain", 0d);
            Scribe_Values.Look(ref foodEnergyRelease.VolumeFreeMealGainEligible, "rrVolumeFreeMealGain", 0d);
            Scribe_Values.Look(ref foodEnergyRelease.VolumeFreeNutrition, "rrVolumeFreeNutrition", 0d);
            Scribe_Values.Look(ref foodEnergyRelease.TransitionNutrition, "rrTransitionNutrition", 0d);
            Scribe_Values.Look(ref foodEnergyRelease.TransitionTicksRemaining, "rrTransitionTicks", 0);
            Scribe_Collections.Look(ref foodEnergyRelease.DoseNutrition, "rrTimedDoseNutrition", LookMode.Value);
            Scribe_Collections.Look(ref foodEnergyRelease.DoseTicksRemaining, "rrTimedDoseTicks", LookMode.Value);
            Scribe_Values.Look(ref bodyGoal, "rrBodyGoal", BodyGoal.Legacy);
            Scribe_Values.Look(ref GainMealState.Active, "rrGainMealInProgress", false);
            Scribe_Values.Look(ref GainMealState.Owner, "rrGainMealOwner", GainMealOwner.None);
            Scribe_Values.Look(ref GainMealState.StartedTick, "rrGainMealStartedTick", 0);
            Scribe_Values.Look(ref GainMealState.ActivityTick, "rrGainMealActivityTick", 0);
            Scribe_Values.Look(ref GainMealState.RetryAfterTick, "rrGainMealRetryTick", 0);
            Scribe_Values.Look(ref GainMealState.SocialRetryAfterTick, "rrSocialMealRetryTick", 0);
            Scribe_Values.Look(ref GainMealState.RearmRequired, "rrGainMealRearm", false);
            // Job references and a live reserve lease cannot safely survive reload.
            // Keep the wait latch; loading must not manufacture another meal.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                GainMealState.EndAfterReload(Find.TickManager?.TicksGame ?? 0);
                GainMealJob = null;
            }
            Scribe_Values.Look(ref bodyGoalAssigned, "rrBodyGoalAssigned", false);
            Scribe_Values.Look(ref bodyGoalAssignmentVersion, "rrBodyGoalAssignmentVersion", 0);
            Scribe_Values.Look(ref baselineMassKg, "rrBodyBaselineKg", 0f);
            Scribe_Values.Look(ref targetMassKg, "rrBodyTargetKg", 0f);
            Scribe_Values.Look(ref dailyIntakeNutrition, "rrDailyIntake", 0f);
            Scribe_Values.Look(ref dailyMealBudgetNutrition, "rrDailyMealBudget", 0f);
            Scribe_Values.Look(ref carriedMealAllowance, "rrMealAllowanceCarry", 0f);
            Scribe_Values.Look(ref mealBudgetInitialized, "rrMealBudgetInitialized", false);
            Scribe_Values.Look(ref intakeDay, "rrIntakeDay", -1);
            Scribe_Values.Look(ref lastBodyMealTick, "rrLastMealTick", -60000);
            Scribe_Values.Look(ref lastMealOutcome, "rrLastMealOutcome");
            Scribe_Values.Look(ref lastMealOutcomeTick, "rrLastMealOutcomeTick", -60000);
            Scribe_Values.Look(ref lastMassDeltaKg, "rrLastMassDeltaKg", 0f);
            Scribe_Values.Look(ref reserveNutritionUsed, "rrReserveNutritionUsed", 0f);
            Scribe_Values.Look(ref massProjectionRemainderKg, "rrMassProjectionRemainderKg", 0f);
            Scribe_Values.Look(ref dailyMassChangeKg, "rrDailyMassChangeKg", 0f);
            Scribe_Values.Look(ref externalDigestionNutrition, "rrExternalDigestionNutrition", 0d);
            Scribe_Values.Look(ref ordinaryUsedNutrition, "rrOrdinaryUsedNutrition", 0d);
            Scribe_Values.Look(ref ordinaryUsedVolume, "rrOrdinaryUsedVolume", 0d);
            Scribe_Values.Look(ref ordinaryWastedNutrition, "rrOrdinaryWastedNutrition", 0d);
            Scribe_Values.Look(ref ordinaryWastedVolume, "rrOrdinaryWastedVolume", 0d);
            Scribe_Values.Look(ref ordinaryIntakeTransactions, "rrOrdinaryIntakeTransactions", 0);
            ExposePreferenceGoal();
            ExposeAdaptation();
            ExposeBodyPerks();
        }

        private void InitializeBodyState()
        {
            bodyEnergy.SnapshotLegacy(currentFullness, currentFullnessToNutritionRatio);
            if (!BodyEnergyState.IsFinite(bodyEnergy.PendingEnergyNutritionExact)) bodyEnergy.PendingEnergyNutritionExact = 0d;
            bodyEnergy.LegacyNutritionRemaining = FiniteNonnegative(bodyEnergy.LegacyNutritionRemaining);
            bodyEnergy.LegacyVolumeRemaining = FiniteNonnegative(bodyEnergy.LegacyVolumeRemaining);
            bodyEnergy.TotalIntakeNutritionExact = FiniteOrdinaryTotal(bodyEnergy.TotalIntakeNutritionExact);
            bodyEnergy.TotalExpenditureNutritionExact = FiniteOrdinaryTotal(bodyEnergy.TotalExpenditureNutritionExact);
            externalDigestionNutrition = Math.Min(bodyEnergy.TotalIntakeNutritionExact, FiniteOrdinaryTotal(externalDigestionNutrition));
            foodEnergyRelease.Sanitize();
            foodEnergyRelease.MigrateExistingBalance(bodyEnergy);
            dailyIntakeNutrition = FiniteNonnegative(dailyIntakeNutrition);
            ordinaryUsedNutrition = FiniteOrdinaryTotal(ordinaryUsedNutrition);
            ordinaryUsedVolume = FiniteOrdinaryTotal(ordinaryUsedVolume);
            ordinaryWastedNutrition = FiniteOrdinaryTotal(ordinaryWastedNutrition);
            ordinaryWastedVolume = FiniteOrdinaryTotal(ordinaryWastedVolume);
            ordinaryIntakeTransactions = Math.Max(0, ordinaryIntakeTransactions);
            dailyMealBudgetNutrition = FiniteNonnegative(dailyMealBudgetNutrition);
            carriedMealAllowance = BodyEnergyState.IsFinite(carriedMealAllowance)
                ? Mathf.Clamp(carriedMealAllowance, -BodyMealBudget.MaximumCarryNutrition,
                    BodyMealBudget.MaximumCarryNutrition) : 0f;
            if (!BodyEnergyState.IsFinite(massProjectionRemainderKg)) massProjectionRemainderKg = 0f;
            if (!Enum.IsDefined(typeof(BodyGoal), bodyGoal)) bodyGoal = BodyGoal.Legacy;
            InitializeBodyGoalAssignment();
            Pawn pawn = parent as Pawn;
            bool createdMass = false;
            if (UsesBodyModel)
            {
                // A child or a pawn without a Food need may become eligible
                // without respawning. Create projections at that transition.
                if (pawn.WeightHediff() == null)
                {
                    Utilities.HediffUtility.AddHediffOfDefTo(Defs.HediffDefOf.RimRound_Weight, pawn);
                    createdMass = true;
                }
                if (Utilities.HediffUtility.GetHediffOfDefFrom(Defs.HediffDefOf.RimRound_Fullness, pawn) == null)
                    Utilities.HediffUtility.AddHediffOfDefTo(Defs.HediffDefOf.RimRound_Fullness, pawn);
                InitBarsIfNull();
            }
            // Inactive biology keeps its saved baseline and target, including
            // zero/uninitialized values on synthetic or not-yet-adult pawns.
            // The existing native interval initializes them when eligible.
            if (!UsesBodyModel) return;
            var attitude = pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (attitude?.PreferenceSchema == 0) attitude.InitializePreference();
            attitude?.InitializeSocialBodyTaste();
            if (!BodyEnergyState.IsFinite(baselineMassKg) || baselineMassKg <= 0f)
            {
                float declared = pawn?.def.GetModExtension<BodyMetabolismExtension>()?.baselineMassKg ?? 0f;
                baselineMassKg = declared > 0f && BodyEnergyState.IsFinite(declared)
                    ? declared : 70f * (pawn?.def.race?.baseBodySize ?? 1f);
                baselineMassKg = Mathf.Clamp(baselineMassKg, 1f, 10000f);
            }
            if (createdMass)
            {
                Hediff created = pawn.WeightHediff();
                float minimum = Mathf.Max(GlobalSettings.minWeight.threshold, Utilities.HediffUtility.SeverityToKilosWithBaseWeight(created.def.minSeverity));
                float maximum = Mathf.Max(minimum, Mathf.Min(GlobalSettings.maxWeight.threshold, Utilities.HediffUtility.SeverityToKilosWithBaseWeight(created.def.maxSeverity)));
                Utilities.HediffUtility.SetHediffSeverity(Defs.HediffDefOf.RimRound_Weight, pawn,
                    Utilities.HediffUtility.KilosToSeverityWithBaseWeight(Mathf.Clamp(baselineMassKg, minimum, maximum)));
                targetMassKg = pawn.Weight();
            }
            if (!BodyEnergyState.IsFinite(targetMassKg) || targetMassKg <= 0f)
                targetMassKg = Mathf.Max(GlobalSettings.minWeight.threshold, pawn?.Weight() ?? baselineMassKg);
            Hediff weight = pawn?.WeightHediff();
            if (weight != null)
            {
                float min = Mathf.Max(GlobalSettings.minWeight.threshold, Utilities.HediffUtility.SeverityToKilosWithBaseWeight(weight.def.minSeverity));
                float max = Mathf.Max(min, Mathf.Min(GlobalSettings.maxWeight.threshold, Utilities.HediffUtility.SeverityToKilosWithBaseWeight(weight.def.maxSeverity)));
                targetMassKg = Mathf.Clamp(targetMassKg, min, max);
            }
        }

        private static float FiniteNonnegative(float value) => BodyEnergyState.IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        private static double FiniteOrdinaryTotal(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? 0d : Math.Max(0d, value);

        private void InitializeBodyGoalAssignment()
        {
            bool colonyScope = BodyModelEligibility.HasColonyManagementScope(parent as Pawn);
            if (bodyGoalAssignmentVersion == 0)
            {
                // Old outsider Maintain values can be generation defaults.
                // Gain/Lose/Recovery were explicitly selected, so preserve them
                // even if their former colony pawn is currently away/released.
                bodyGoalAssigned = colonyScope || ContinuousGain ||
                    bodyGoal == BodyGoal.Lose || bodyGoal == BodyGoal.Recovery;
                bodyGoalAssignmentVersion = 1;
            }
            // Joining the colony enables its saved/default choice. Leaving it
            // suspends automation without erasing the assignment or target.
            if (colonyScope) bodyGoalAssigned = true;
        }

        private void ResetIntakeDay()
        {
            if (!mealBudgetInitialized)
            {
                // Older saves have today's intake but no earned-budget field.
                // Begin at zero rounding balance, without inventing debt for
                // meals eaten before this accounting existed.
                dailyMealBudgetNutrition = dailyIntakeNutrition;
                carriedMealAllowance = 0f;
                mealBudgetInitialized = true;
            }
            int day = (Find.TickManager?.TicksGame ?? 0) / 60000;
            if (intakeDay == day) return;
            // Only actual native expenditure earns tomorrow's rounding credit.
            // No catch-up across frozen/off-map days, and no banking more than
            // one ordinary meal. Overshoot also carries, so it is not free food.
            carriedMealAllowance = intakeDay == day - 1 && UsesBodyGoals &&
                (Goal == BodyGoal.Gain || Goal == BodyGoal.Recovery)
                ? BodyMealBudget.NextCarry(carriedMealAllowance,
                    dailyMealBudgetNutrition, dailyIntakeNutrition) : 0f;
            intakeDay = day;
            dailyIntakeNutrition = 0f;
            dailyMealBudgetNutrition = 0f;
            dailyMassChangeKg = 0f;
        }

        // Called exactly once by each ingestion transaction. The vanilla caller
        // (or the custom feeder) remains responsible for applying Food need.
        public void RecordIngestion(float nutrition, float effectiveFullnessPerNutrition)
            => RecordIngestion(nutrition, effectiveFullnessPerNutrition, null);

        public void RecordIngestion(float nutrition, float effectiveFullnessPerNutrition, ThingDef sourceDef)
            => RecordIngestion(nutrition, effectiveFullnessPerNutrition, FoodEnergyReleasePolicy.DurationTicks(sourceDef));

        public void RecordIngestion(float nutrition, float effectiveFullnessPerNutrition, int releaseDurationTicks)
        {
            if (!BodyEnergyState.IsFinite(effectiveFullnessPerNutrition) ||
                effectiveFullnessPerNutrition < 0f) return;
            RecordExactIngestion(nutrition, (double)nutrition * effectiveFullnessPerNutrition, releaseDurationTicks);
        }

        private bool RecordExactIngestion(double nutrition, double physicalVolume, int releaseDurationTicks)
        {
            if (!UsesBodyModel || !BodyEnergyState.IsFinite(nutrition) || nutrition <= 0d ||
                nutrition > float.MaxValue || !BodyEnergyState.IsFinite(physicalVolume) || physicalVolume < 0d ||
                physicalVolume > float.MaxValue || !BodyEnergyState.IsFinite((float)(CurrentFullness + physicalVolume))) return false;
            InitializeBodyState();
            // Nutrition and represented volume stay double throughout the authoritative
            // release ledger. Native fullness, perks and display counters are projections.
            if (!foodEnergyRelease.Record(nutrition, physicalVolume, releaseDurationTicks, bodyEnergy,
                releaseDurationTicks == 0 && Ideology.AbundanceRoles.GainsFromMeals(parent as Pawn))) return false;
            float projectedNutrition = (float)nutrition, volume = (float)physicalVolume;
            RecordAdaptationIntake(projectedNutrition, volume);
            RecordPerkIntake(projectedNutrition);
            parent.TryGetComp<ThingComp_PawnAttitude>()?.RecordPreferenceIntake(projectedNutrition);
            InitializePerksIfNull();
            if (physicalVolume > 0d)
            {
                UpdateRatio(projectedNutrition, (float)(physicalVolume / nutrition));
                CurrentFullness += volume;
            }
            ConsumedNutrition += projectedNutrition;
            ResetIntakeDay();
            dailyIntakeNutrition += projectedNutrition;
            lastBodyMealTick = Find.TickManager?.TicksGame ?? 0;
            if (!OrdinaryFoodIntake.IsSupplementalCommit(parent as Pawn))
                GainMealSession.RecordProgress(parent as Pawn);
            return true;
        }

        internal bool TryCreditExternalDigestion(float amount)
        {
            if (!UsesBodyModel || !BodyEnergyState.IsFinite(amount) || amount <= 0f) return false;
            InitializeBodyState();
            ResetIntakeDay();
            var food = ((Pawn)parent).needs.food;
            float updatedDaily = dailyIntakeNutrition + amount;
            float beforeFood = food.CurLevel;
            float maximumFood = food.MaxLevel;
            if (!BodyEnergyState.IsFinite(updatedDaily) || !BodyEnergyState.IsFinite(beforeFood) ||
                !BodyEnergyState.IsFinite(maximumFood) || maximumFood <= 0f) return false;
            float updatedFood = (float)Math.Min(maximumFood, (double)beforeFood + amount);
            // Already digested nutrients have no physical volume or ordinary meal
            // effects. The saved energy is absorbed only by a real Food interval.
            double previousIntake = bodyEnergy.TotalIntakeNutritionExact;
            double previousUnabsorbed = foodEnergyRelease.VolumeFreeNutrition;
            if (!foodEnergyRelease.Record(amount, 0d, 0, bodyEnergy, mealGainEligible: false)) return false;
            if (updatedFood != beforeFood)
            {
                try { food.CurLevel = updatedFood; }
                catch (Exception error)
                {
                    Log.ErrorOnce("RimRound external digestion could not finish setting Food: " + error, 76519642);
                    // A patched native setter may fail either before or after
                    // applying the change. Only the former is safe to retry.
                    if (food.CurLevel == beforeFood)
                    {
                        bodyEnergy.TotalIntakeNutritionExact = previousIntake;
                        foodEnergyRelease.VolumeFreeNutrition = previousUnabsorbed;
                        return false;
                    }
                }
            }
            externalDigestionNutrition += amount;
            dailyIntakeNutrition = updatedDaily;
            return true;
        }

        // Represented volume counters precede the recipient's volume modifier.
        // Waste is observable/saved but never enters energy or progression.
        public void RecordOrdinaryIntake(MealPortionPlan plan, float multiplier)
            => RecordOrdinaryIntake(plan, multiplier, null);

        public void ClearMealGainEligibility()
        {
            bodyEnergy.MealGainEligibleNutrition = 0d;
            foodEnergyRelease.OrdinaryMealGainEligible = 0d;
            foodEnergyRelease.VolumeFreeMealGainEligible = 0d;
        }

        public void RecordOrdinaryIntake(MealPortionPlan plan, float multiplier, ThingDef sourceDef)
            => RecordOrdinaryIntake(plan, multiplier, FoodEnergyReleasePolicy.DurationTicks(sourceDef));

        public void RecordOrdinaryIntake(MealPortionPlan plan, float multiplier, int releaseDurationTicks)
            => TryRecordOrdinaryIntake(plan, multiplier, releaseDurationTicks);

        internal bool TryRecordOrdinaryIntake(MealPortionPlan plan, float multiplier, int releaseDurationTicks)
        {
            if (!RecordExactIngestion(plan.UsefulNutrition, plan.UsefulVolume * multiplier, releaseDurationTicks)) return false;
            ordinaryUsedNutrition += plan.UsefulNutrition;
            ordinaryUsedVolume += plan.UsefulVolume;
            ordinaryWastedNutrition += plan.WastedNutrition;
            ordinaryWastedVolume += plan.WastedVolume;
            ordinaryIntakeTransactions++;
            return true;
        }

        // A native resource can spend Food directly, outside NeedInterval.
        // Record that observed debit once without replaying hunger/digestion or
        // granting another interval of mass change. The next real interval
        // projects the saved energy balance through the usual bounded model.
        public void RecordNativeResourceExpenditure(float nutrition)
        {
            if (!UsesBodyModel || !BodyEnergyState.IsFinite(nutrition) || nutrition <= 0f) return;
            InitializeBodyState();
            ResetIntakeDay();
            bodyEnergy.RecordExpenditure(nutrition);
            if (CanTrainAdaptation) adaptation.RecordResourceExpenditure(nutrition);
            bodyPerks.RecordResourceExpenditure(nutrition, CanEarnBodyPerks);
            parent.TryGetComp<ThingComp_PawnAttitude>()?.RecordPreferenceResourceExpenditure(nutrition);
            if (UsesBodyGoals && (Goal == BodyGoal.Gain || Goal == BodyGoal.Recovery))
                dailyMealBudgetNutrition += nutrition * BodyFeedingPolicy.DailyIntakeFactor(this);
        }

        // Called only by the actual native FoodFallPerTick read inside
        // NeedInterval. Frozen needs never reach it. No elapsed-time catch-up.
        public void ObserveNativeFoodInterval(float nutrition, int ticks)
        {
            if (ticks <= 0 || (!UsesBodyModel && PendingLegacyNutrition <= 0f)) return;
            observedExpenditure = FiniteNonnegative(nutrition);
            observedIntervalTicks = ticks;
        }

        public void CompleteNativeFoodInterval()
        {
            int ticks = observedIntervalTicks;
            float expenditure = observedExpenditure;
            observedIntervalTicks = 0;
            observedExpenditure = 0f;
            Pawn pawn = parent as Pawn;
            if (ticks <= 0 || pawn?.needs?.food == null || pawn.Dead) return;
            InitializePerksIfNull();
            InitializeBodyState();

            float volumeBefore = (float)Math.Max(CurrentFullness,
                bodyEnergy.LegacyVolumeRemaining + foodEnergyRelease.OrdinaryVolume);
            float clearedVolume = Mathf.Min(volumeBefore, FiniteNonnegative(DigestionRate) * ticks * Ideology.AbundanceRituals.DigestionMultiplier(pawn));
            CurrentFullness = Mathf.Max(0f, CurrentFullness - clearedVolume);
            float legacyCredit = bodyEnergy.DigestLegacy(clearedVolume, volumeBefore, UsesBodyModel);
            if (legacyCredit > 0f)
            {
                pawn.needs.food.CurLevel += legacyCredit;
                if (UsesBodyModel) ConsumedNutrition += legacyCredit;
            }
            if (!UsesBodyModel) return;
            bool feedee = Ideology.AbundanceRoles.GainsFromMeals(pawn);
            if (!feedee) ClearMealGainEligibility();
            foodEnergyRelease.Advance(clearedVolume, volumeBefore, ticks, bodyEnergy);
            ResetIntakeDay();
            if (UsesBodyGoals && (Goal == BodyGoal.Gain || Goal == BodyGoal.Recovery))
                dailyMealBudgetNutrition += expenditure * BodyFeedingPolicy.DailyIntakeFactor(this);
            bodyEnergy.RecordExpenditure(expenditure);
            AdvanceAdaptation(ticks, expenditure);
            AdvanceBodyPerks(ticks, expenditure);
            parent.TryGetComp<ThingComp_PawnAttitude>()?.ObservePreferenceExperience(ticks, expenditure);

            // Lose uses a small, explicit contribution from body reserves.
            // It is NOT recorded as food intake: the full native expenditure
            // above debits the energy ledger once. No malnutrition discount.
            float currentMass = pawn.Weight();
            previousRangeAction = Goal;
            float safeTarget = Mathf.Max(TargetBodyMassKg, baselineMassKg * 0.9f, GlobalSettings.minWeight.threshold);
            if (UsesBodyGoals && BodyModelEligibility.CanFollowBodyGoal(pawn) &&
                Goal == BodyGoal.Lose && currentMass > safeTarget &&
                pawn.needs.food.CurCategory != HungerCategory.Starving &&
                pawn.health.hediffSet.GetFirstHediffOfDef(RimWorld.HediffDefOf.Malnutrition) == null)
            {
                float credit = Mathf.Min(expenditure * 0.1f, Mathf.Max(0f, pawn.needs.food.MaxLevel - pawn.needs.food.CurLevel));
                pawn.needs.food.CurLevel += credit;
                reserveNutritionUsed += credit;
            }

            Hediff massHediff = pawn.WeightHediff();
            float effectiveMinimum = Mathf.Max(GlobalSettings.minWeight.threshold,
                massHediff == null ? 26f : Utilities.HediffUtility.SeverityToKilosWithBaseWeight(massHediff.def.minSeverity));
            float effectiveMaximum = Mathf.Min(GlobalSettings.maxWeight.threshold,
                massHediff == null ? GlobalSettings.maxWeight.threshold : Utilities.HediffUtility.SeverityToKilosWithBaseWeight(massHediff.def.maxSeverity));
            float delta = bodyEnergy.ApplyEnergyToMass(currentMass + massProjectionRemainderKg, baselineMassKg, 4f,
                FiniteNonnegative(WeightGainMultiplier), Mathf.Clamp(WeightLossMultiplier, 0f, 10f),
                effectiveMinimum, effectiveMaximum, ticks, 0.01f, feedee ? 0.2f : 0f);
            lastMassDeltaKg = delta;
            ResetIntakeDay();
            dailyMassChangeKg += delta;
            if (delta != 0f)
            {
                // Do not run weight multipliers twice. The hediff remains the
                // compatibility mass owner while other legacy writers migrate.
                massProjectionRemainderKg += delta;
                if (massHediff != null)
                {
                    float previousSeverity = massHediff.Severity;
                    float projectedSeverity = previousSeverity + massProjectionRemainderKg * Values.severityPerKilo;
                    // Do not snap an old pawn to newly configured mass bounds.
                    // ApplyEnergyToMass already bounds each direction; the
                    // native hediff setter enforces its own Def's limits.
                    massHediff.Severity = projectedSeverity;
                    // Float hediffs cannot express every small interval change.
                    // Keep the remainder in the save instead of deleting energy.
                    double appliedKg = ((double)massHediff.Severity - previousSeverity) / Values.severityPerKilo;
                    massProjectionRemainderKg -= (float)appliedKg;
                }
            }
            if (UsesBodyGoals && ((bodyGoal == BodyGoal.Lose && pawn.Weight() <= safeTarget) ||
                (bodyGoal == BodyGoal.Recovery && pawn.Weight() >= targetMassKg)))
                ApplyBodyGoal(BodyGoal.Maintain);
            FullnessCheckTick();
            // Growth was formerly tied to the configurable comp tick interval.
            if (ColonyProgression.LegacyPerksActive && CurrentFullness > SoftLimit)
                softLimitPersonal += StomachElasticity * ticks;
            if (GlobalSettings.burstingEnabled) RuptureStomachCheckTick();
        }

        public void SetBodyGoal(BodyGoal goal)
        {
            if (!BodyModelEligibility.CanManage(parent as Pawn) || !Enum.IsDefined(typeof(BodyGoal), goal)) return;
            ApplyBodyGoal(goal);
        }

        private void ApplyBodyGoal(BodyGoal goal)
        {
            GainMealSession.End(parent as Pawn);
            InitializeBodyState();
            ResetIntakeDay();
            bodyGoalAssigned = true;
            if (bodyGoal != goal)
            {
                carriedMealAllowance = 0f;
                // Earlier meals still count against today's target, but may
                // not become tomorrow's debt against post-change expenditure.
                dailyMealBudgetNutrition = dailyIntakeNutrition;
            }
            bodyGoal = goal;
            previousRangeAction = BodyGoal.Maintain;
            float mass = ((Pawn)parent).Weight();
            targetMassKg = goal == BodyGoal.Lose ? Mathf.Max(baselineMassKg * 0.9f, mass - baselineMassKg * 0.1f) :
                goal == BodyGoal.Recovery ? Mathf.Max(mass, baselineMassKg) : mass;
            Hediff weight = ((Pawn)parent).WeightHediff();
            float min = Mathf.Max(GlobalSettings.minWeight.threshold, weight == null ? 26f :
                Utilities.HediffUtility.SeverityToKilosWithBaseWeight(weight.def.minSeverity));
            float max = Mathf.Max(min, Mathf.Min(GlobalSettings.maxWeight.threshold, weight == null ? GlobalSettings.maxWeight.threshold :
                Utilities.HediffUtility.SeverityToKilosWithBaseWeight(weight.def.maxSeverity)));
            targetMassKg = Mathf.Clamp(targetMassKg, min, max);
            if ((goal == BodyGoal.Recovery && mass >= targetMassKg) ||
                (goal == BodyGoal.Lose && mass <= targetMassKg)) bodyGoal = BodyGoal.Maintain;
            if (goal != BodyGoal.Legacy) DietMode = DietMode.Nutrition;
        }

        public string BodyGoalLabel => !HasAssignedBodyGoal ? "RR_BodyGoal_Unmanaged".Translate().ToString() :
            bodyGoal == BodyGoal.Auto ? "RR_BodyGoalAutoAction".Translate(("RR_BodyGoal_" + Goal).Translate()).ToString() :
            ("RR_BodyGoal_" + bodyGoal).Translate().ToString();
        public string BodyGoalReadOnlyReason
        {
            get
            {
                Pawn pawn = parent as Pawn;
                if (BodyModelEligibility.CanManage(pawn)) return null;
                if (pawn?.InMentalState == true) return "RR_BodyGoalReadOnlyMentalState".Translate();
                if (pawn != null && pawn.IsQuestLodger()) return "RR_BodyGoalReadOnlyGuest".Translate();
                return "RR_BodyGoalReadOnlyControl".Translate();
            }
        }
        public string BodyStatusDescription
        {
            get
            {
                Pawn pawn = parent as Pawn;
                float scale = GlobalSettings.usePoundsWherePossible ? 2.20462f : 1f;
                string unit = GlobalSettings.usePoundsWherePossible ? "lb" : "kg";
                string status = HasAssignedBodyGoal
                    ? (ContinuousGain ? "RR_BodyStatusContinuous".Translate(BodyGoalLabel,
                        (pawn.Weight() * scale).ToString("F1"), unit, CurrentFullness.ToString("F2"),
                        (SoftLimit * .9f).ToString("F2")).ToString() :
                        "RR_BodyStatus".Translate(BodyGoalLabel, (pawn.Weight() * scale).ToString("F1"),
                        (TargetBodyMassKg * scale).ToString("F1"), unit, CurrentFullness.ToString("F2"), SoftLimit.ToString("F2")).ToString()) +
                        "\n" + ("RR_BodyGoalDesc_" + bodyGoal).Translate().ToString()
                    : "RR_BodyStatusUnmanaged".Translate((pawn.Weight() * scale).ToString("F1"),
                        unit, CurrentFullness.ToString("F2"), SoftLimit.ToString("F2")).ToString();
                float passengers = PassengerVolume;
                if (passengers > 0f) status += "\n" + PassengerBreakdown(passengers);
                return status +
                    "\n" + "RR_FdrEnergyStatus".Translate(
                        (foodEnergyRelease.OrdinaryNutrition + foodEnergyRelease.VolumeFreeNutrition).ToString("F2"),
                        foodEnergyRelease.TimedNutrition.ToString("F2"), bodyEnergy.PendingEnergyNutritionExact.ToString("F2"),
                        FiniteNonnegative(WeightGainMultiplier).ToString("0.##")).ToString() +
                    (foodEnergyRelease.TransitionNutrition > 0d ? "\n" + "RR_FdrTransitionStatus".Translate(
                        foodEnergyRelease.TransitionNutrition.ToString("F2"),
                        (foodEnergyRelease.TransitionTicksRemaining / 2500d).ToString("F1")).ToString() : "") +
                    (GoalRegulationDescription != null ? "\n" + GoalRegulationDescription : "") +
                    "\n" + (pawn.TryGetComp<ThingComp_PawnAttitude>()?.PreferenceDescription ?? "") +
                    "\n" + "RR_BodyTrendToday".Translate((dailyMassChangeKg * scale).ToString("+0.00;-0.00;0.00"), unit).ToString() +
                    "\n" + "RR_BodyFoodPolicy".Translate(pawn.foodRestriction?.CurrentFoodPolicy?.label ?? "-").ToString() +
                    (BodyGoalReadOnlyReason != null ? "\n" + BodyGoalReadOnlyReason : "") +
                    (RecentMealOutcome != null ? "\nLast meal: " + RecentMealOutcome : "") +
                    (PendingLegacyNutrition > 0f ? "\n" + "RR_LegacyNutritionPending".Translate(PendingLegacyNutrition.ToString("F2")).ToString() : "");
            }
        }

        public void OpenBodyGoalMenu()
        {
            if (!BodyModelEligibility.CanManage(parent as Pawn)) return;
            var options = new List<FloatMenuOption>();
            foreach (BodyGoal option in Enum.GetValues(typeof(BodyGoal)))
            {
                BodyGoal captured = option;
                options.Add(new FloatMenuOption(("RR_BodyGoal_" + option).Translate(), () => SetBodyGoal(captured)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private Command_Action MakeBodyGoalCommand()
        {
            var command = new Command_Action
            {
                defaultLabel = "RR_BodyGoalCommand".Translate(BodyGoalLabel),
                defaultDesc = BodyStatusDescription + (GainMealSession.Reason(parent as Pawn) is string mealReason ? "\n" + mealReason : ""),
                icon = ContentFinder<Texture2D>.Get("UI/WeightGizmo/WeightGizmo_ModeButtons/WeightGizmo_Mode_Nutrition"),
                action = OpenBodyGoalMenu
            };
            string reason = BodyGoalReadOnlyReason;
            if (reason != null) command.Disable(reason);
            return command;
        }
    }
}
