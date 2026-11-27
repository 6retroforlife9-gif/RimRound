using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Oinky.JunkFood
{
    public sealed class SharedJunkPawnRecord : IExposable
    {
        public Pawn Pawn;
        public SharedHabitState State = new SharedHabitState();
        public int RetryAfterTick;
        public int CarryRetryAfterTick;
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            if (State == null) State = new SharedHabitState();
            Scribe_Values.Look(ref State.Habit, "habit");
            Scribe_Values.Look(ref State.Exposure, "exposure");
            Scribe_Values.Look(ref State.EligibleFraction, "eligibleFraction");
            Scribe_Values.Look(ref State.NeedLevel, "needLevel");
            Scribe_Values.Look(ref State.DeprivedTicks, "deprivedActiveTicks");
            Scribe_Values.Look(ref State.AbstinentTicks, "abstinentActiveTicks");
            Scribe_Values.Look(ref State.ActiveTicks, "activeTicks");
            Scribe_Values.Look(ref State.ScheduleFraction, "scheduleFraction");
            Scribe_Values.Look(ref State.LastScheduleUse, "lastScheduleUse", -1d);
            Scribe_Values.Look(ref State.LastWallTick, "lastWallTick", -1);
            Scribe_Values.Look(ref RetryAfterTick, "retryAfterTick");
            Scribe_Values.Look(ref CarryRetryAfterTick, "carryRetryAfterTick");
        }
    }

    public static class JunkSharedRules
    {
        public const string HabitName = "Oinky_JunkFoodHabit";
        public const string NeedName = "Oinky_JunkFoodNeed";
        public const string ExposureName = "Oinky_JunkFoodExposure";
        public static HediffDef HabitDef => DefDatabase<HediffDef>.GetNamedSilentFail(HabitName);
        public static HediffDef ExposureDef => DefDatabase<HediffDef>.GetNamedSilentFail(ExposureName);
        public static NeedDef NeedDef => DefDatabase<NeedDef>.GetNamedSilentFail(NeedName);
        public static bool Mode => JunkFoodPolicy.Colony?.SharedMode == true;
        public static bool Enabled => Mode && JunkFoodPolicy.Enabled;
        public static bool HabitsEnabled => Enabled && JunkFoodPolicy.Colony.CravingsEnabled;
        public static int Now => Find.TickManager?.TicksGame ?? 0;
        public static bool AdultKind(Pawn pawn) => pawn?.RaceProps?.Humanlike == true &&
            pawn.RaceProps.IsFlesh && !pawn.IsSubhuman && pawn.ageTracker != null &&
            pawn.DevelopmentalStage == DevelopmentalStage.Adult && pawn.ageTracker.AgeBiologicalYears >= 18 &&
            pawn.def.GetModExtension<BodyMetabolismExtension>()?.usesFoodMetabolism != false;
        public static bool Active(Pawn pawn)
        {
            if (!AdultKind(pawn) || pawn.Dead || pawn.Suspended || pawn.needs?.food == null ||
                !BodyModelEligibility.UsesFoodMetabolism(pawn) ||
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel != true) return false;
            NeedDef need = NeedDef;
            if (need != null && (pawn.health.hediffSet.DisablesNeed(need) ||
                pawn.genes?.DisablesNeed(need) == true || pawn.story?.traits?.DisablesNeed(need) == true ||
                pawn.Ideo?.DisablesNeed(need) == true || (pawn.IsMutant && pawn.mutant.Def.disableNeeds &&
                (pawn.mutant.Def.needWhitelist == null || !pawn.mutant.Def.needWhitelist.Contains(need))))) return false;
            var dormant = pawn.TryGetComp<CompCanBeDormant>();
            if (dormant != null && !dormant.Awake && dormant.Props.freezeNeeds.Contains(NeedDef)) return false;
            return pawn.SpawnedOrAnyParentSpawned || pawn.IsCaravanMember() ||
                RimWorld.PawnUtility.IsTravelingInTransportPodWorldObject(pawn);
        }
        public static int TraitDegree(Pawn pawn) => pawn?.story?.traits?.DegreeOfTrait(TraitDefOf.DrugDesire) ?? 0;
        public static bool HasNativeHabit(Pawn pawn) => HabitDef != null &&
            pawn?.health?.hediffSet?.HasHediff(HabitDef) == true;
        public static bool HasRealAddiction(Pawn pawn) => pawn?.health?.hediffSet?.hediffs
            .Any(h => h.def.IsAddiction && h.def.defName != HabitName) == true;
    }

    public sealed partial class JunkFoodGameComponent
    {
        private bool sharedMode;
        private List<SharedJunkPawnRecord> sharedRecords = new List<SharedJunkPawnRecord>();
        private Dictionary<Pawn, SharedJunkPawnRecord> sharedIndex = new Dictionary<Pawn, SharedJunkPawnRecord>();
        private List<JunkPolicyRecord> sharedPolicies = new List<JunkPolicyRecord>();
        private List<JunkCaravanReserveRecord> caravanReserves = new List<JunkCaravanReserveRecord>();
        private readonly HashSet<Pawn> reconciling = new HashSet<Pawn>();
        internal int InternalCleanupDepth;
        public bool SharedMode => sharedMode;

        private void ExposeSharedData()
        {
            // The absent key MUST remain false; it is not a migration signal.
            Scribe_Values.Look(ref sharedMode, "Oinky_UseSharedJunkFoodPolicy", false);
            Scribe_Collections.Look(ref sharedRecords, "Oinky_SharedJunkStateV1", LookMode.Deep);
            Scribe_Collections.Look(ref sharedPolicies, "Oinky_SharedJunkPolicyV1", LookMode.Deep);
            Scribe_Collections.Look(ref caravanReserves, "Oinky_SharedJunkCaravanReservesV1", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sharedRecords = sharedRecords ?? new List<SharedJunkPawnRecord>();
                sharedPolicies = sharedPolicies ?? new List<JunkPolicyRecord>();
                caravanReserves = caravanReserves ?? new List<JunkCaravanReserveRecord>();
                sharedIndex.Clear();
                for (int i = sharedRecords.Count - 1; i >= 0; i--)
                {
                    var row = sharedRecords[i];
                    if (row?.Pawn == null || sharedIndex.ContainsKey(row.Pawn)) { sharedRecords.RemoveAt(i); continue; }
                    row.State = row.State ?? new SharedHabitState();
                    row.State.Normalize(); row.State.LastWallTick = Now;
                    sharedIndex.Add(row.Pawn, row);
                }
            }
        }

        private void InitializeSharedMode()
        {
            // Do not clear legacy history just because this is an old/default-off save.
            if (sharedMode) ClearHistories();
            PrunePolicies();
            ReconcileSharedSettings();
        }

        public void SetSharedMode(bool value)
        {
            if (sharedMode == value) return;
            sharedMode = value;
            ClearHistories(); // Explicit transition: never convert either history.
            foreach (var row in sharedRecords) { row.State.ClearEffects(); row.State.LastWallTick = Now; }
            InternalCleanupDepth++;
            try { foreach (Pawn pawn in AllKnownPawns()) RemoveSharedEffects(pawn, true); }
            finally { InternalCleanupDepth--; }
            ReconcileSharedSettings();
        }

        private IEnumerable<Pawn> AllKnownPawns() => PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead
            .Concat(sharedRecords.Where(r => r?.Pawn != null).Select(r => r.Pawn)).Distinct().ToArray();

        private void ReconcileSharedSettings()
        {
            foreach (Pawn pawn in AllKnownPawns())
            {
                if (!sharedMode || !contentEnabled) StopSharedJobs(pawn, true);
                else if (!cravingsEnabled) StopSharedJobs(pawn, false);
                ReconcileSharedPawn(pawn);
            }
        }

        public SharedJunkPawnRecord SharedRecord(Pawn pawn, bool create = true)
        {
            if (pawn == null) return null;
            SharedJunkPawnRecord row;
            if (!sharedIndex.TryGetValue(pawn, out row) && create)
            {
                row = new SharedJunkPawnRecord { Pawn = pawn };
                row.State.LastWallTick = Now;
                sharedRecords.Add(row); sharedIndex.Add(pawn, row);
            }
            return row;
        }

        public void AdvanceShared(Pawn pawn)
        {
            var row = SharedRecord(pawn, false);
            if (row == null) return;
            int delta = row.State.LastWallTick < 0 ? 0 : Math.Max(0, Now - row.State.LastWallTick);
            row.State.LastWallTick = Now;
            if (JunkSharedRules.Active(pawn) && row.State.Advance(delta, sharedMode && contentEnabled && cravingsEnabled))
            {
                // Distinguish our recovery from an externally forced addiction.
                // The generation flags do not block explicit forcedAddictions.
                var native = pawn.health.hediffSet.GetFirstHediffOfDef(JunkSharedRules.HabitDef);
                if (native != null)
                {
                    InternalCleanupDepth++;
                    try { pawn.health.RemoveHediff(native); }
                    finally { InternalCleanupDepth--; }
                }
            }
            ReconcileSharedPawn(pawn);
        }

        private void TickShared()
        {
            if (Now % 150 != 0) return;
            foreach (var row in sharedRecords.ToArray()) AdvanceShared(row.Pawn);
            if (Now % 2500 == 0) PrunePolicies();
        }

        public void RecordShared(OrdinaryIntakeCommit commit)
        {
            if (!sharedMode || !contentEnabled || commit == null || !JunkFoodPolicy.IsJunk(commit.FoodDef) ||
                !JunkSharedRules.Active(commit.Eater) || commit.ItemCount <= 0 || commit.UsefulFraction <= 0d ||
                commit.UsefulFraction > 1d || commit.ServingUnitsPerItem <= 0d) return;
            double dose = commit.ItemCount * commit.UsefulFraction * commit.ServingUnitsPerItem;
            if (!SharedHabitState.Finite(dose) || dose <= 0d) return;
            var row = SharedRecord(commit.Eater);
            AdvanceShared(commit.Eater);
            bool acquired = row.State.Record(dose, cravingsEnabled, () => Rand.Chance(.01f));
            if (acquired && JunkSharedRules.HabitDef != null)
            {
                InternalCleanupDepth++;
                try { commit.Eater.health.AddHediff(JunkSharedRules.HabitDef); }
                finally { InternalCleanupDepth--; }
            }
            if (JunkSharedRules.TraitDegree(commit.Eater) > 0)
            {
                var chemical = commit.Eater.needs.TryGetNeed<Need_Chemical_Any>();
                if (chemical != null) chemical.CurLevel += (float)Math.Min(1d, .2d * dose);
            }
            ReconcileSharedPawn(commit.Eater);
            (commit.Eater.MentalState as MentalState_JunkFoodBinge)?.NotifyUsefulSnack();
            DirtyThoughts(commit.Eater);
        }

        public void ReconcileSharedPawn(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || !reconciling.Add(pawn)) return;
            try
            {
                var row = SharedRecord(pawn, false);
                if (!sharedMode || !contentEnabled || !cravingsEnabled || !JunkSharedRules.AdultKind(pawn))
                {
                    row?.State.ClearEffects();
                    InternalCleanupDepth++;
                    try { RemoveSharedEffects(pawn, false); }
                    finally { InternalCleanupDepth--; }
                    return;
                }
                bool native = JunkSharedRules.HasNativeHabit(pawn);
                if (native && (row == null || !row.State.Habit))
                {
                    row = SharedRecord(pawn); row.State.Habit = true;
                    row.State.NeedLevel = pawn.needs?.TryGetNeed<Need_JunkFood>()?.CurLevel ?? 0f;
                }
                if (row == null) return;
                if (row.State.Habit && !native && InternalCleanupDepth == 0) row.State.ClearEffects();
                // Pure four-active-day recovery removes the native counterpart.
                if (!row.State.Habit && native)
                {
                    InternalCleanupDepth++;
                    try { pawn.health.RemoveHediff(pawn.health.hediffSet.GetFirstHediffOfDef(JunkSharedRules.HabitDef)); }
                    finally { InternalCleanupDepth--; }
                }
                var exposure = pawn.health.hediffSet.GetFirstHediffOfDef(JunkSharedRules.ExposureDef);
                if (row.State.Exposure > 0d && exposure == null)
                    exposure = pawn.health.AddHediff(JunkSharedRules.ExposureDef);
                if (exposure != null)
                {
                    if (row.State.Exposure > 0d) exposure.Severity = (float)row.State.Exposure;
                    else { InternalCleanupDepth++; try { pawn.health.RemoveHediff(exposure); } finally { InternalCleanupDepth--; } }
                }
                pawn.needs?.AddOrRemoveNeedsAsAppropriate();
                var need = pawn.needs?.TryGetNeed<Need_JunkFood>();
                if (need != null) need.RestoreLevel((float)row.State.NeedLevel);
                DirtyThoughts(pawn);
            }
            finally { reconciling.Remove(pawn); }
        }

        public void GenuineHabitRemoval(Pawn pawn)
        {
            if (InternalCleanupDepth != 0) return;
            SharedRecord(pawn, false)?.State.ClearEffects();
            DirtyThoughts(pawn);
        }
        public void GenuineExposureRemoval(Pawn pawn)
        {
            if (InternalCleanupDepth != 0) return;
            var row = SharedRecord(pawn, false);
            if (row != null) row.State.Exposure = row.State.EligibleFraction = 0d;
        }

        private void RemoveSharedEffects(Pawn pawn, bool allJobs)
        {
            StopSharedJobs(pawn, allJobs);
            var effects = pawn.health.hediffSet.hediffs.Where(h =>
                h.def.defName == JunkSharedRules.HabitName || h.def.defName == JunkSharedRules.ExposureName).ToArray();
            // Default-off saves without our effects need no global need rebuild.
            // Keep cleanup targeted, including a stale orphaned shared need.
            if (effects.Length == 0 && pawn.needs?.TryGetNeed<Need_JunkFood>() == null) return;
            foreach (Hediff hediff in effects)
                pawn.health.RemoveHediff(hediff);
            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
            DirtyThoughts(pawn);
        }

        private static void StopSharedJobs(Pawn pawn, bool all)
        {
            var binge = pawn?.MentalState as MentalState_JunkFoodBinge;
            if (binge != null) binge.RecoverFromState();
            if (pawn?.CurJob?.def == null) return;
            bool owned = pawn.CurJob.def.defName == JunkSnackUtility.JobName ||
                pawn.CurJob.def.defName == JunkCarryUtility.JobName;
            if (owned && (all || JunkSnackUtility.PurposeOf(pawn.CurJob) == JunkSnackPurpose.Relief ||
                JunkSnackUtility.PurposeOf(pawn.CurJob) == JunkSnackPurpose.Binge))
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
        }
    }
}
