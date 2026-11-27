using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Oinky.JunkFood
{
    public enum JunkSnackPurpose { Recreation, Relief, Schedule, Binge }

    public static class JunkSnackUtility
    {
        public const string JobName = "Oinky_SharedJunkSnack";
        [ThreadStatic] private static SnackScope scope;
        private sealed class SnackScope : IDisposable
        {
            internal Pawn Pawn; internal Thing Food; internal JunkSnackPurpose Purpose; internal bool Caravan;
            private readonly SnackScope previous;
            internal SnackScope(Pawn pawn, Thing food, JunkSnackPurpose purpose, bool caravan)
            { previous = scope; Pawn = pawn; Food = food; Purpose = purpose; Caravan = caravan; scope = this; }
            public void Dispose() { scope = previous; }
        }
        public static IDisposable ForIntake(Pawn pawn, Thing food, JunkSnackPurpose purpose, bool caravan = false)
            => new SnackScope(pawn, food, purpose, caravan);
        public static JunkSnackPurpose PurposeOf(Job job) => job?.def?.defName == JobName && job.targetB.IsValid
            ? (JunkSnackPurpose)job.targetB.Cell.x : JunkSnackPurpose.Recreation;
        public static double Units(ThingDef def) => def?.GetModExtension<JunkFoodExtension>()?.ServingUnitsPerItem ?? 1d;
        public static bool Ready(Pawn pawn, bool binge = false, bool caravan = false) => JunkSharedRules.Enabled &&
            JunkSharedRules.Active(pawn) && pawn.Faction == Faction.OfPlayer && !pawn.Downed && !pawn.Drafted &&
            (caravan || pawn.Spawned) && (binge || !pawn.InMentalState) &&
            !RimRound.Ideology.AbundanceRituals.BudgetFor(pawn).HasValue &&
            !ConnectedFeederIntake.BudgetFor(pawn).HasValue &&
            !(OrdinaryFoodIntake.ActiveBudgetProvider?.Invoke(pawn)).HasValue &&
            pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
            (caravan || pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving));

        public static bool Wants(Pawn pawn, JunkSnackPurpose purpose, bool caravan = false, bool prospectiveBinge = false)
        {
            bool binge = purpose == JunkSnackPurpose.Binge;
            if (!Ready(pawn, binge, caravan)) return false;
            if (binge) return JunkSharedRules.HabitsEnabled &&
                (prospectiveBinge || pawn.MentalState is MentalState_JunkFoodBinge);
            var colony = JunkFoodPolicy.Colony;
            var policy = colony.PolicyFor(pawn);
            if (policy == null) return false;
            bool trait = JunkSharedRules.TraitDegree(pawn) > 0;
            switch (purpose)
            {
                case JunkSnackPurpose.Recreation:
                    return pawn.needs.joy != null && pawn.needs.joy.CurLevel < 1f && (policy.Recreation || trait);
                case JunkSnackPurpose.Relief:
                    var state = colony.SharedRecord(pawn, false)?.State;
                    return JunkSharedRules.HabitsEnabled && state?.Habit == true && state.NeedLevel <= .1d && (policy.Addiction || trait);
                case JunkSnackPurpose.Schedule:
                    return policy.Scheduled && (policy.MoodBelow >= 1f || pawn.needs.mood?.CurLevel < policy.MoodBelow) &&
                        (policy.JoyBelow >= 1f || pawn.needs.joy?.CurLevel < policy.JoyBelow) &&
                        colony.SharedRecord(pawn).State.ScheduleDue(policy.Days);
                default: return false;
            }
        }

        private static OrdinaryIntakeBudget MakeBudget(Pawn pawn, Thing food, JunkSnackPurpose purpose, bool caravan)
        {
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            float target = purpose == JunkSnackPurpose.Binge ? body.HardLimit : body.SoftLimit;
            if (purpose != JunkSnackPurpose.Binge && (body.DietMode == DietMode.Fullness || body.DietMode == DietMode.Hybrid))
            {
                float cap = body.GetRanges().Second;
                if (cap >= 0f) target = Mathf.Min(target, cap);
            }
            double unitNutrition = FoodUtility.NutritionForEater(pawn, food) / Units(food.def);
            double wantedUnits = 1d;
            if (purpose == JunkSnackPurpose.Recreation)
            {
                double unitJoy = food.def.ingestible.joy / Units(food.def);
                wantedUnits = unitJoy > 0d ? Math.Min(1d, (1d - pawn.needs.joy.CurLevel) / unitJoy) : 0d;
            }
            return new OrdinaryIntakeBudget((float)(unitNutrition * wantedUnits), target,
                purpose != JunkSnackPurpose.Binge && !caravan, true);
        }

        public static OrdinaryIntakeBudget? BudgetFor(Pawn pawn)
        {
            Thing food; JunkSnackPurpose purpose; bool caravan;
            if (scope != null && scope.Pawn == pawn)
            { food = scope.Food; purpose = scope.Purpose; caravan = scope.Caravan; }
            else return null;
            // The saved driver reconstructs this exact scope from targetA/B at
            // its commit toil. Merely having our job current is not permission
            // for another caller to ingest unrelated food under its budget.
            if (food == null || !JunkFoodPolicy.IsJunk(food.def) || !Wants(pawn, purpose, caravan))
                return new OrdinaryIntakeBudget(0f, 0f, false, true);
            return MakeBudget(pawn, food, purpose, caravan);
        }

        public static bool Suitable(Pawn pawn, Thing food, bool prospectiveBinge = false)
        {
            if (!JunkFoodPolicy.IsJunk(food?.def) || food.Destroyed || food.stackCount <= 0 || !food.IngestibleNow) return false;
            if (!prospectiveBinge) return FoodUtility.WillEat(pawn, food, pawn, true, false);
            // Before entering the mental state, mirror WillEat's non-policy
            // gates. Once entered, native GetCurrentRespectedRestriction is null.
            return FoodUtility.FoodIsSuitable(pawn, food.def) &&
                !FoodUtility.InappropriateForTitle(food.def, pawn, true) &&
                !FoodUtility.IsVeneratedAnimalMeatOrCorpseOrHasIngredients(food, pawn);
        }

        public static bool Unclaimed(Pawn pawn, Thing food, Job ownJob = null)
        {
            if (food?.Spawned != true) return food?.ParentHolder == pawn?.inventory;
            if (AutomaticPackageServing.HasFeastClaim(food)) return false;
            if (food.Map.reservationManager.ReservationsReadOnly.Any(r => r.Target.Thing == food &&
                (r.Claimant != pawn || r.Job != ownJob))) return false;
            var physical = new HashSet<Pawn>();
            food.Map.physicalInteractionReservationManager.ReserversOf(food, physical);
            return physical.All(p => ownJob != null && p == pawn);
        }

        public static bool Preview(Pawn pawn, Thing food, JunkSnackPurpose purpose, out MealPortionPlan plan,
            bool caravan = false, bool prospectiveBinge = false)
        {
            plan = default(MealPortionPlan);
            if (!Wants(pawn, purpose, caravan, prospectiveBinge) || !Suitable(pawn, food, prospectiveBinge) ||
                (purpose == JunkSnackPurpose.Recreation && food.def.ingestible.joy <= 0f)) return false;
            var budget = MakeBudget(pawn, food, purpose, caravan);
            if (prospectiveBinge)
            {
                double energy = FoodUtility.NutritionForEater(pawn, food);
                return OrdinaryFoodIntake.TryPlanServing(pawn, energy, energy * OrdinaryFoodIntake.Density(food), budget, out plan) &&
                    (plan.UsefulFraction >= 1d - 1e-7d || OrdinaryFoodIntake.PartialEffectLimitation(food) == null);
            }
            using (ForIntake(pawn, food, purpose, caravan))
                return OrdinaryFoodIntake.TryPreview(pawn, food, 1, out plan, budget) &&
                    (purpose == JunkSnackPurpose.Binge || plan.UsefulFraction >= .75d - 1e-7d);
        }

        internal static bool SourceAllowed(Pawn pawn, Thing food, JunkSnackPurpose purpose, bool prospective = false, Job ownJob = null)
        {
            if (!Suitable(pawn, food, prospective) || !Unclaimed(pawn, food, ownJob)) return false;
            bool binge = purpose == JunkSnackPurpose.Binge;
            if (!food.Spawned) return food.ParentHolder == pawn.inventory;
            return food.Map == pawn.Map && !food.IsBurning() && (binge || !food.IsForbidden(pawn)) &&
                food.IsSociallyProper(pawn) && FactionUtility.IsPoliticallyProper(food, pawn) &&
                pawn.CanReserveAndReach(food, PathEndMode.Touch, pawn.NormalMaxDanger(), 1, 1);
        }

        public static Thing FindFood(Pawn pawn, JunkSnackPurpose purpose, Func<Thing, bool> extra = null, bool prospective = false)
        {
            if (!Wants(pawn, purpose, false, prospective)) return null;
            IEnumerable<Thing> stock = pawn.inventory != null ? pawn.inventory.innerContainer.AsEnumerable() : Enumerable.Empty<Thing>();
            stock = stock.Concat(pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree));
            foreach (Thing food in stock.Where(t => JunkFoodPolicy.IsJunk(t.def))
                .OrderBy(t => Units(t.def)).ThenBy(t => t.Spawned ? pawn.Position.DistanceToSquared(t.Position) : -1)
                .ThenBy(t => t.thingIDNumber))
            {
                if ((extra != null && !extra(food)) || !SourceAllowed(pawn, food, purpose, prospective)) continue;
                Thing preview = food;
                bool temporary = AutomaticPackageServing.IsPackage(food) && PackageServing.TryMakePortions(food, out preview, true);
                if (AutomaticPackageServing.IsPackage(food) && !temporary) continue;
                try
                {
                    MealPortionPlan plan;
                    if (!Preview(pawn, preview, purpose, out plan, false, prospective)) continue;
                    // Opening conserves food; the prospective debit is one child,
                    // against the original physical source, never the whole box.
                    double cost = ColonyFoodReserve.StockCost(food, 1) / Units(food.def);
                    if (purpose != JunkSnackPurpose.Binge && !ColonyFoodReserve.CanSpendOptional(pawn, cost, food)) continue;
                    if (temporary && food.Spawned && !AutomaticPackageServing.CanPrepareShared(pawn, food, purpose, prospective, out _)) continue;
                    return food;
                }
                finally { if (temporary && preview != null && !preview.Destroyed) preview.Destroy(); }
            }
            return null;
        }

        public static Job TryJob(Pawn pawn, JunkSnackPurpose purpose, Func<Thing, bool> extra = null)
        {
            if (!Wants(pawn, purpose)) return null;
            var row = JunkFoodPolicy.Colony.SharedRecord(pawn);
            if (purpose != JunkSnackPurpose.Binge && JunkSharedRules.Now < row.RetryAfterTick) return null;
            Thing food = FindFood(pawn, purpose, extra);
            if (food == null) { row.RetryAfterTick = JunkSharedRules.Now + 600; return null; }
            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(JobName), food);
            job.targetB = new IntVec3((int)purpose, 0, 0); job.count = 1;
            return job;
        }

        public static bool Consume(Pawn pawn, Thing food, JunkSnackPurpose purpose, bool caravan = false)
        {
            MealPortionPlan plan;
            if (!Preview(pawn, food, purpose, out plan, caravan)) return false;
            using (ForIntake(pawn, food, purpose, caravan))
            {
                float wanted = MakeBudget(pawn, food, purpose, caravan).Nutrition;
                if (!(wanted > 0f) || OrdinaryFoodIntake.SelectedCount(pawn, food, wanted) != 1) return false;
                float actual = food.Ingested(pawn, wanted);
                if (actual > 0f) pawn.needs.food.CurLevel += actual;
                return actual > 0f;
            }
        }
    }

    public sealed class JobDriver_JunkSnack : JobDriver
    {
        private JunkSnackPurpose Purpose => JunkSnackUtility.PurposeOf(job);
        public override bool TryMakePreToilReservations(bool errorOnFailed) => !job.targetA.Thing.Spawned ||
            pawn.Reserve(job.targetA, job, 1, 1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !JunkSnackUtility.Wants(pawn, Purpose) || job.targetA.Thing == null ||
                !JunkSnackUtility.SourceAllowed(pawn, job.targetA.Thing, Purpose, false, job));
            var walk = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_Jump.JumpIf(walk, () => job.targetA.Thing.Spawned);
            var prepare = Toils_General.Do(() =>
            {
                Thing food = job.targetA.Thing;
                if (!AutomaticPackageServing.IsPackage(food)) return;
                Thing child;
                bool opened = food.Spawned
                    ? AutomaticPackageServing.TryOpenShared(pawn, food, Purpose, job, out child)
                    : PackageServing.TryOpenHeld(food, out child);
                if (!opened) { AutomaticPackageServing.Defer(food); EndJobWith(JobCondition.Incompletable); return; }
                job.targetA = child;
                if (child.Spawned && !pawn.Reserve(child, job, 1, 1)) EndJobWith(JobCondition.Incompletable);
            });
            // Jump around the map walk for food already in this pawn's inventory.
            yield return Toils_Jump.Jump(prepare);
            yield return walk;
            yield return prepare;
            var approachChild = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var chew = Toils_General.Wait(120).WithProgressBarToilDelay(TargetIndex.A);
            chew.tickAction = () => { if (job.targetA.Thing.Spawned) pawn.rotationTracker.FaceTarget(job.targetA); };
            yield return Toils_Jump.JumpIf(chew, () => !job.targetA.Thing.Spawned);
            yield return approachChild;
            yield return chew;
            yield return Toils_General.Do(() =>
            { if (!JunkSnackUtility.Consume(pawn, job.targetA.Thing, Purpose)) EndJobWith(JobCondition.Incompletable); });
        }
    }

    [HarmonyPatch(typeof(JobGiver_SatisfyChemicalNeed), nameof(JobGiver_SatisfyChemicalNeed.GetPriority))]
    internal static class SharedJunkReliefPriority
    { static void Postfix(Pawn pawn, ref float __result) { if (JunkSnackUtility.Wants(pawn, JunkSnackPurpose.Relief)) __result = Mathf.Max(__result, 9.25f); } }
    [HarmonyPatch(typeof(JobGiver_SatisfyChemicalNeed), "TryGiveJob")]
    internal static class SharedJunkReliefJob
    { static void Postfix(Pawn pawn, ref Job __result) { if (__result == null) __result = JunkSnackUtility.TryJob(pawn, JunkSnackPurpose.Relief); } }
    [HarmonyPatch(typeof(JobGiver_TakeDrugsForDrugPolicy), nameof(JobGiver_TakeDrugsForDrugPolicy.GetPriority))]
    internal static class SharedJunkSchedulePriority
    { static void Postfix(Pawn pawn, ref float __result) { if (JunkSnackUtility.Wants(pawn, JunkSnackPurpose.Schedule)) __result = Mathf.Max(__result, 7.5f); } }
    [HarmonyPatch(typeof(JobGiver_TakeDrugsForDrugPolicy), "TryGiveJob")]
    internal static class SharedJunkScheduleJob
    { static void Postfix(Pawn pawn, ref Job __result) { if (__result == null) __result = JunkSnackUtility.TryJob(pawn, JunkSnackPurpose.Schedule); } }
}
