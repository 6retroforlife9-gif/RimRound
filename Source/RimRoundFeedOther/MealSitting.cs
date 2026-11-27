using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public sealed class SittingRecipient : IExposable
    {
        public Pawn Pawn;
        public FaucetMealPurpose Purpose;
        public MealSittingBudget Budget = new MealSittingBudget();
        public bool ProtectReserve;
        public bool EssentialCare, EssentialCareCollected, EssentialCareConsumed;
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "recipient");
            Scribe_Values.Look(ref Purpose, "purpose");
            Scribe_Values.Look(ref ProtectReserve, "protectReserve");
            Scribe_Values.Look(ref EssentialCare, "essentialCare", false);
            Scribe_Values.Look(ref EssentialCareCollected, "essentialCareCollected", false);
            Scribe_Values.Look(ref EssentialCareConsumed, "essentialCareConsumed", false);
            Scribe_Values.Look(ref Budget.InitialNutrition, "initialNutrition");
            Scribe_Values.Look(ref Budget.InitialVolume, "initialVolume");
            Scribe_Values.Look(ref Budget.RemainingNutrition, "remainingNutrition");
            Scribe_Values.Look(ref Budget.RemainingVolume, "remainingVolume");
            Scribe_Values.Look(ref Budget.CollectedNutrition, "collectedNutrition");
            Scribe_Values.Look(ref Budget.CollectedVolume, "collectedVolume");
            Scribe_Values.Look(ref Budget.Tolerance, "tolerance");
        }
    }

    public sealed class SittingAllocation : IExposable
    {
        public Pawn Recipient;
        public Thing Food;
        public void ExposeData()
        {
            Scribe_References.Look(ref Recipient, "recipient");
            Scribe_References.Look(ref Food, "food");
        }
    }

    // Emergency ownership for a real item rejected by inventory and map placement.
    // It remains serialized until the original collector can drop it safely.
    public sealed class MealRecovery : IExposable, IThingHolder
    {
        public Pawn Collector;
        private ThingOwner food;
        public MealRecovery() { food = new ThingOwner<Thing>(this, false); }
        public IThingHolder ParentHolder => null;
        public ThingOwner GetDirectlyHeldThings() => food;
        public void GetChildHolders(List<IThingHolder> holders) => ThingOwnerUtility.AppendThingHoldersFromThings(holders, food);
        public void ExposeData()
        {
            Scribe_References.Look(ref Collector, "collector");
            Scribe_Deep.Look(ref food, "food", this);
        }
        public bool Recover()
        {
            if (Collector?.Spawned == true)
                foreach (Thing item in food.ToArray())
                {
                    Thing dropped;
                    food.TryDrop(item, Collector.Position, Collector.Map, ThingPlaceMode.Near, out dropped);
                }
            return food.Count == 0;
        }
    }

    /// <summary>Only references to real inventory/carry items; this is never a second food owner.</summary>
    public sealed class MealSitting : IExposable
    {
        public Pawn Collector;
        public string JobId;
        public List<SittingRecipient> Recipients = new List<SittingRecipient>();
        public List<SittingAllocation> Allocations = new List<SittingAllocation>();
        public bool Ready, InterruptedOnLoad;
        public string Outcome;
        public int SourcesVisited;
        public bool Live => Collector?.CurJob?.GetUniqueLoadID() == JobId && !InterruptedOnLoad;
        public SittingRecipient For(Pawn eater) => Recipients.FirstOrDefault(r => r.Pawn == eater);
        public void ExposeData()
        {
            Scribe_References.Look(ref Collector, "collector");
            Scribe_Values.Look(ref JobId, "jobId");
            Scribe_Collections.Look(ref Recipients, "recipients", LookMode.Deep);
            Scribe_Collections.Look(ref Allocations, "allocations", LookMode.Deep);
            Scribe_Values.Look(ref Ready, "ready");
            Scribe_Values.Look(ref Outcome, "outcome");
            Scribe_Values.Look(ref SourcesVisited, "sourcesVisited");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Recipients = Recipients ?? new List<SittingRecipient>();
                Allocations = Allocations ?? new List<SittingAllocation>();
                // Native toil indexes can resume between transfer and commit.
                // End safely after load; food is already persisted by its real owner.
                InterruptedOnLoad = true;
            }
        }
    }

    public sealed class MealSittingComponent : GameComponent
    {
        private List<MealSitting> sittings = new List<MealSitting>();
        private List<MealRecovery> recovery = new List<MealRecovery>();
        public MealSittingComponent(Game game) { }
        public static MealSittingComponent Current => Verse.Current.Game?.GetComponent<MealSittingComponent>();
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref sittings, "rrMealSittings", LookMode.Deep);
            Scribe_Collections.Look(ref recovery, "rrMealRecovery", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sittings = sittings ?? new List<MealSitting>();
                recovery = recovery ?? new List<MealRecovery>();
            }
        }
        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 == 0)
                recovery.RemoveAll(r => r.Recover());
            foreach (var sitting in sittings.ToArray())
            {
                if (sitting.InterruptedOnLoad && sitting.Collector?.CurJob?.GetUniqueLoadID() == sitting.JobId)
                {
                    sitting.Outcome = "session ended safely after reload";
                    sitting.Collector.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                else if (!sitting.Live) Finish(sitting.Collector, sitting.JobId, "session interrupted");
            }
        }
        public void RecoverUnownedFood(Pawn collector, Thing food)
        {
            if (food == null || food.Destroyed || food.Spawned || food.holdingOwner != null) return;
            if (collector?.Spawned == true && GenPlace.TryPlaceThing(food, collector.Position,
                collector.Map, ThingPlaceMode.Near)) return;
            var holder = new MealRecovery { Collector = collector };
            if (!holder.GetDirectlyHeldThings().TryAdd(food, false))
                throw new InvalidOperationException("Could not retain an unowned meal in its recovery container");
            recovery.Add(holder);
            collector?.TryGetComp<FullnessAndDietStats_ThingComp>()?.RecordMealOutcome("unused food retained until a safe drop is available");
        }
        public static bool Eligible(Pawn eater) => eater?.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel == true &&
            eater.TryGetComp<FullnessAndDietStats_ThingComp>().Goal == BodyGoal.Gain &&
            eater.TryGetComp<FullnessAndDietStats_ThingComp>().HasGainAppetite;
        public MealSitting Get(Pawn collector) => sittings.FirstOrDefault(s => s.Collector == collector &&
            s.JobId == collector?.CurJob?.GetUniqueLoadID());
        public MealSitting ForRecipient(Pawn eater) => sittings.FirstOrDefault(s =>
            s.Collector?.CurJob?.GetUniqueLoadID() == s.JobId && s.For(eater) != null);

        // A failed optional bundle must not strand a hungry patient. This is a
        // one-way phase of this exact automatic clinical job, never a general
        // reserve exemption or a change to the pawn's assigned body goal.
        private static bool ClinicalCareAllowed(MealSitting sitting, SittingRecipient recipient)
        {
            Pawn collector = sitting?.Collector, eater = recipient?.Pawn;
            var job = collector?.CurJob;
            var body = eater?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return sitting?.Live == true && sitting.Recipients.Count == 1 &&
                sitting.For(eater) == recipient && recipient.Purpose == FaucetMealPurpose.Routine &&
                job != null && !job.playerForced && job.targetB.Pawn == eater &&
                collector.jobs?.curDriver?.GetType() == typeof(JobDriver_FoodFeedPatientEatingSpeed) &&
                collector.jobs.curDriver.job == job && collector.Spawned && !collector.Dead &&
                !collector.Drafted && !collector.InMentalState &&
                collector.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
                eater?.Spawned == true && !eater.Dead && !eater.InMentalState &&
                eater.Map == collector.Map && !eater.IsForbidden(collector) &&
                body?.UsesBodyModel == true && body.Goal == BodyGoal.Gain &&
                eater.needs?.food != null && FoodUtility.ShouldBeFedBySomeone(eater) &&
                (FeedPatientUtility.IsHungry(eater) || BodyFeedingPolicy.IsUrgentlyHungry(eater));
        }

        internal static bool EssentialCareMode(Pawn collector, Pawn eater)
        {
            var sitting = Current?.Get(collector);
            return sitting?.For(eater)?.EssentialCare == true;
        }

        internal static bool CanAcquireEssentialCare(Pawn collector, Pawn eater)
        {
            var sitting = Current?.Get(collector);
            var recipient = sitting?.For(eater);
            return recipient?.EssentialCare == true && !recipient.EssentialCareCollected &&
                !recipient.EssentialCareConsumed && sitting.Allocations.Count == 0 &&
                ClinicalCareAllowed(sitting, recipient);
        }

        internal bool TryBeginEssentialCare(MealSitting sitting)
        {
            // Even a partial real allocation belongs to its original budget.
            // Do not discard, relabel, or supplement it through this fallback.
            if (sitting == null || sitting.Allocations.Count != 0 || sitting.Recipients.Count != 1) return false;
            var recipient = sitting.Recipients[0];
            if (recipient.EssentialCare || !ClinicalCareAllowed(sitting, recipient)) return false;
            var body = recipient.Pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            double nutrition = Math.Max(0f, recipient.Pawn.needs.food.MaxLevel - recipient.Pawn.needs.food.CurLevel);
            double target = Math.Min(body.HardLimit, body.UsesBodyGoals ? body.SoftLimit : body.HardLimit);
            double room = Math.Max(0d, target - body.OccupiedVolume);
            if (!MealSittingBudget.Valid(nutrition) || nutrition <= 0d || !MealSittingBudget.Valid(room) ||
                !MealSittingBudget.Valid(body.FullnessGainedMultiplier) ||
                (body.FullnessGainedMultiplier > 0f && room <= 0d)) return false;
            recipient.EssentialCare = true;
            recipient.Budget.Initialize(room, nutrition, 0d);
            sitting.Ready = false;
            sitting.Outcome = "essential care: optional meal unavailable";
            return true;
        }

        internal static void MarkEssentialCareCollected(Pawn collector, Pawn eater)
        {
            var recipient = Current?.Get(collector)?.For(eater);
            if (recipient?.EssentialCare == true) recipient.EssentialCareCollected = true;
        }

        public MealSitting Begin(Pawn collector, IEnumerable<Pawn> eaters, FaucetMealPurpose purpose)
        {
            var existing = Get(collector);
            if (existing != null) return existing;
            var sitting = new MealSitting { Collector = collector, JobId = collector.CurJob.GetUniqueLoadID() };
            foreach (Pawn eater in eaters.Distinct())
            {
                var body = eater?.TryGetComp<FullnessAndDietStats_ThingComp>();
                if (body?.UsesBodyModel != true) continue;
                var recipient = new SittingRecipient { Pawn = eater, Purpose = purpose,
                    ProtectReserve = !collector.CurJob.playerForced };
                double volume = Math.Max(0d, Math.Min(body.HardLimit, BodyRoleFollowerMeals.RecipientVisit(eater) != null
                    ? BodyRoleFollowerMeals.ServiceTarget(eater) : body.SoftLimit * .9f) - body.OccupiedVolume);
                if (BodyRoleFollowerMeals.RecipientVisit(eater) != null)
                    volume += OfficerServiceCollectionBudget.ExtraVolume(body.SoftLimit, body.FullnessGainedMultiplier);
                double energy = body.LimitGainNutrition(float.MaxValue);
                if (!Eligible(eater))
                {
                    energy = Math.Max(0f, eater.needs.food.MaxLevel - eater.needs.food.CurLevel);
                    volume = Math.Min(volume, .9d * body.FullnessGainedMultiplier);
                }
                recipient.Budget.Initialize(volume, energy, .09d * body.FullnessGainedMultiplier);
                sitting.Recipients.Add(recipient);
            }
            sittings.Add(sitting);
            return sitting;
        }

        public static void Amounts(Pawn eater, Thing food, out double nutrition, out double physicalVolume)
        {
            var precise = OrdinaryFoodIntake.PreciseVolume(food);
            nutrition = precise?.PreciseNutritionPerItem ?? FoodUtility.NutritionForEater(eater, food);
            double volume = precise?.PreciseFullnessPerItem ?? nutrition * OrdinaryFoodIntake.Density(food);
            physicalVolume = volume * eater.TryGetComp<FullnessAndDietStats_ThingComp>().FullnessGainedMultiplier;
        }

        public OrdinaryIntakeBudget? BudgetFor(Pawn eater)
        {
            var sitting = ForRecipient(eater);
            if (sitting == null) return null;
            var body = eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            var recipient = sitting.For(eater);
            // Loaded toils may be evaluated before GameComponentTick cancels them.
            // Keep their zero budget visible until cancellation instead of falling
            // through to a fresh ordinary-food budget and replaying an intake.
            if (sitting.InterruptedOnLoad)
                return new OrdinaryIntakeBudget(0f, body.OccupiedVolume, recipient.ProtectReserve, true);
            if (recipient.EssentialCare)
            {
                // Both useful nutrition and physical room are bounded by the
                // allowance at transition and by live state at every bite.
                float careTarget = (float)Math.Min(body.UsesBodyGoals ? body.SoftLimit : body.HardLimit,
                    body.OccupiedVolume + recipient.Budget.RemainingVolume);
                double careNutrition = !Permission(sitting, recipient) ? 0d :
                    Math.Min(recipient.Budget.RemainingNutrition,
                        Math.Max(0f, eater.needs.food.MaxLevel - eater.needs.food.CurLevel));
                return new OrdinaryIntakeBudget((float)careNutrition, careTarget,
                    protectReserve: false, nutritionIsLimit: true);
            }
            float target = (float)Math.Min(BodyRoleFollowerMeals.RecipientVisit(eater) != null
                ? BodyRoleFollowerMeals.ServiceTarget(eater) : body.SoftLimit * .9d,
                body.OccupiedVolume + recipient.Budget.RemainingVolume);
            double nutrition = recipient.Budget.Complete || !Permission(sitting, recipient) ? 0d :
                Math.Min(recipient.Budget.RemainingNutrition, body.LimitGainNutrition(float.MaxValue));
            return new OrdinaryIntakeBudget((float)Math.Min(float.MaxValue, nutrition), target,
                recipient.ProtectReserve, true);
        }

        public static bool Permission(MealSitting sitting, SittingRecipient recipient)
        {
            Pawn eater = recipient.Pawn, collector = sitting.Collector;
            if (!sitting.Live || eater?.Spawned != true || eater.Dead || eater.InMentalState ||
                collector?.Spawned != true || collector.Dead || collector.Drafted || collector.InMentalState ||
                eater.Map != collector.Map || eater.needs?.food == null) return false;
            if (recipient.EssentialCare)
                return !recipient.EssentialCareConsumed && ClinicalCareAllowed(sitting, recipient);
            if (recipient.Purpose == FaucetMealPurpose.Fatten)
                return PrisonerFatteningFoodPatch.CanContinueFattenJob(eater);
            if (collector.CurJob.playerForced) return true;
            // A sitting may outlive the reserve allowance that admitted it.
            // End its optional collection/chewing when the reserve pauses so
            // ordinary hunger feeding can resume instead of touring rejected stock.
            // This zero-cost check reuses the bounded planning estimate; actual
            // withdrawals still validate fresh physical stock and exact costs.
            if (recipient.ProtectReserve && !ColonyFoodReserve.CanSpendOptional(eater, 0d)) return false;
            if (collector == eater && collector.CurJob.def == JobDefOf.Ingest)
                return BodyFeedingPolicy.UsesComfortableGainTarget(eater) && GainMealSession.IsActive(eater);
            // Clinical care and secure delivery preserve their own driver gates.
            if (collector.jobs.curDriver is JobDriver_FoodFeedPatientEatingSpeed ||
                collector.jobs.curDriver is JobDriver_PrisonerMealDelivery) return Eligible(eater);
            return SocialFeedingPolicy.EaterEligible(eater, true) &&
                eater.TryGetComp<FullnessAndDietStats_ThingComp>().HasGainAppetite;
        }

        public void Committed(Pawn eater, Thing food, double nutrition, double volume)
        {
            var sitting = ForRecipient(eater);
            if (sitting == null || !sitting.Live || !sitting.Allocations.Any(a => a.Recipient == eater && a.Food == food)) return;
            sitting.For(eater).Budget.Consumed(nutrition, volume);
            if (sitting.For(eater).EssentialCare && nutrition > 0d)
            {
                sitting.For(eater).EssentialCareConsumed = true;
                sitting.Outcome = "essential care serving completed";
            }
        }

        public bool Promote(Pawn collector, Pawn eater)
        {
            var sitting = Get(collector);
            var recipient = sitting?.For(eater);
            if (recipient == null || (!recipient.EssentialCare && recipient.Budget.Complete) ||
                !Permission(sitting, recipient)) return false;
            Thing carried = collector.carryTracker.CarriedThing;
            if (carried != null)
            {
                if (sitting.Allocations.Any(a => a.Food == carried && a.Recipient == eater) &&
                    OrdinaryFoodIntake.TryPreview(eater, carried, 1, out MealPortionPlan current, BudgetFor(eater)) &&
                    (recipient.EssentialCare || current.UsefulFraction >= .75d - 1e-7d))
                {
                    collector.CurJob.targetA = carried;
                    collector.CurJob.count = carried.stackCount;
                    return true;
                }
                if (!collector.carryTracker.innerContainer.TryTransferToContainer(carried,
                        collector.inventory.innerContainer, false)) return false;
            }
            foreach (var allocation in sitting.Allocations.Where(a => a.Recipient == eater).ToArray())
            {
                Thing food = allocation.Food;
                if (food == null || food.Destroyed || !collector.inventory.innerContainer.Contains(food) ||
                    !FoodUtility.WillEat(eater, food, collector, true, false)) continue;
                MealPortionPlan plan;
                if (!OrdinaryFoodIntake.TryPreview(eater, food, 1, out plan, BudgetFor(eater)) ||
                    (!recipient.EssentialCare && plan.UsefulFraction < .75d - 1e-7d)) continue;
                Thing moved;
                int count = Math.Min(food.stackCount, collector.carryTracker.AvailableStackSpace(food.def));
                if (count <= 0 || collector.inventory.innerContainer.TryTransferToContainer(food,
                        collector.carryTracker.innerContainer, count, out moved, false) <= 0) continue;
                if (moved != food) sitting.Allocations.Add(new SittingAllocation { Recipient = eater, Food = moved });
                collector.CurJob.targetA = moved;
                collector.CurJob.count = moved.stackCount;
                return true;
            }
            sitting.Outcome = recipient.Budget.Complete ? "comfortable meal complete" :
                sitting.Outcome ?? "partial meal: available servings or portion rounding";
            return false;
        }

        public bool Completed(Pawn eater)
        {
            var sitting = ForRecipient(eater);
            return sitting != null && sitting.For(eater).Budget.Complete;
        }

        public bool DropDelivery(Pawn collector, Pawn recipient, IntVec3 cell)
        {
            var sitting = Get(collector);
            if (sitting == null) return true;
            bool all = true;
            foreach (var a in sitting.Allocations.Where(a => a.Recipient == recipient).ToArray())
            {
                Thing food = a.Food;
                if (food == null || food.Destroyed || food.Spawned) continue;
                Thing dropped;
                if (food.holdingOwner == collector.inventory.innerContainer || food.holdingOwner == collector.carryTracker.innerContainer)
                    all &= food.holdingOwner.TryDrop(food, cell, collector.Map, ThingPlaceMode.Near, out dropped);
            }
            if (all)
            {
                sitting.Outcome = "complete bundle delivered";
                GainMealSession.NotifyMealDelivered(recipient);
            }
            return all;
        }

        public void Finish(Pawn collector, string jobId, string outcome)
        {
            var sitting = sittings.FirstOrDefault(s => s.Collector == collector && s.JobId == jobId);
            if (sitting == null) return;
            sitting.Outcome = sitting.Outcome ?? outcome;
            foreach (var recipient in sitting.Recipients)
                recipient.Pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.RecordMealOutcome(
                    recipient.EssentialCare ? recipient.EssentialCareConsumed ? "essential care serving completed" :
                        "essential care ended without intake" :
                        recipient.Budget.Complete ? "comfortable meal complete" : sitting.Outcome);
            if (collector?.Spawned == true)
                foreach (var a in sitting.Allocations)
                {
                    Thing food = a.Food, dropped;
                    if (food != null && !food.Destroyed && !food.Spawned &&
                        (food.holdingOwner == collector.inventory?.innerContainer || food.holdingOwner == collector.carryTracker?.innerContainer))
                        food.holdingOwner.TryDrop(food, collector.Position, collector.Map, ThingPlaceMode.Near, out dropped);
                }
            ColonyFoodReserve.NotifyFoodChanged(collector?.MapHeld);
            sittings.Remove(sitting);
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.Cleanup))]
    internal static class MealSittingCleanup
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(JobDriver __instance, JobCondition condition)
        {
            var component = MealSittingComponent.Current;
            var sitting = component?.Get(__instance.pawn);
            if (sitting == null || sitting.JobId != __instance.job?.GetUniqueLoadID()) return;
            bool complete = sitting.Recipients.All(r => r.Budget.Complete);
            component.Finish(__instance.pawn, sitting.JobId, complete ? "comfortable meal complete" : "meal ended with a shortfall");
            if (__instance is JobDriver_Ingest) GainMealSession.End(__instance.pawn, complete);
        }
    }
}
