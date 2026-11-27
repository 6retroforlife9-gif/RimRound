using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Simulation;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.Utilities
{
    /// <summary>Optional definition metadata; one represented item is one unit by default.</summary>
    public interface IOrdinaryFoodServingUnits
    {
        double ServingUnitsPerItem { get; }
    }

    /// <summary>Immutable useful dose, captured before the native stack is consumed.</summary>
    public sealed class OrdinaryIntakeCommit
    {
        public readonly Pawn Eater;
        public readonly ThingDef FoodDef;
        public readonly int ItemCount;
        public readonly double UsefulFraction;
        public readonly double ServingUnitsPerItem;
        public readonly double UsefulNutrition;
        // Represented volume, before the eater's fullness multiplier.
        public readonly double UsefulVolume;

        internal OrdinaryIntakeCommit(Pawn eater, ThingDef foodDef, int count,
            double units, MealPortionPlan plan)
        {
            Eater = eater; FoodDef = foodDef; ItemCount = count;
            UsefulFraction = plan.UsefulFraction; ServingUnitsPerItem = units;
            UsefulNutrition = plan.UsefulNutrition; UsefulVolume = plan.UsefulVolume;
        }
    }

    public interface IPreciseBodyFoodVolume : IBodyFoodVolume
    {
        double PreciseNutritionPerItem { get; }
        double PreciseFullnessPerItem { get; }
    }

    public struct OrdinaryIntakeBudget
    {
        public float Nutrition;
        public float FullnessTarget;
        public bool ProtectReserve;
        public bool NutritionIsLimit;
        internal bool AllowOrdinaryHungerFallback;
        internal bool Supplemental;
        public OrdinaryIntakeBudget(float nutrition, float fullnessTarget,
            bool protectReserve = false, bool nutritionIsLimit = false)
        {
            Nutrition = nutrition; FullnessTarget = fullnessTarget;
            ProtectReserve = protectReserve; NutritionIsLimit = nutritionIsLimit;
            AllowOrdinaryHungerFallback = false;
            Supplemental = false;
        }
    }

    /// <summary>One owner for ordinary adult food; native atomic doses remain native.</summary>
    public static class OrdinaryFoodIntake
    {
        // Companion supplies only an explicitly active recipient/session context.
        // This does not call NutritionWanted, whose autonomous checks preview food.
        public static Func<Pawn, OrdinaryIntakeBudget?> ActiveBudgetProvider;
        public static Action<Pawn, Thing, double, double> UsefulIntakeCommitted;
        // Existing care/ritual/feeder owners retain precedence. An add-on must
        // match its own saved job or explicit preview scope before supplying a budget.
        public static event Func<Pawn, OrdinaryIntakeBudget?> SupplementalBudgetProviders;
        public static event Action<OrdinaryIntakeCommit> UsefulIntakeSnapshotCommitted;
        [ThreadStatic] private static Transaction active;
        [ThreadStatic] private static bool notifyingSnapshot;

        public sealed class Transaction
        {
            public Thing Food;
            public Pawn Eater;
            public int Count;
            public MealPortionPlan Plan;
            public float Multiplier;
            public bool AmountsApplied;
            public bool Committed;
            internal Transaction Previous;
            internal IDisposable ReserveLease;
            internal OrdinaryIntakeCommit Snapshot;
            internal bool Supplemental;
        }

        public static bool IsAtomicDef(ThingDef def)
        {
            return def != null && (def.IsDrug || def.defName == "fatpill" ||
                (def.ingestible?.outcomeDoers != null &&
                 def.ingestible.outcomeDoers.Any(d => d.GetType().Name.IndexOf("Hemogen", StringComparison.OrdinalIgnoreCase) >= 0)));
        }

        public static bool IsOrdinaryFood(Thing food)
        {
            if (food == null || food is Corpse || !food.def.IsNutritionGivingIngestible || IsAtomicDef(food.def))
                return false;
            var ingredients = food.TryGetComp<CompIngredients>();
            return ingredients?.ingredients == null || !ingredients.ingredients.Any(IsAtomicDef);
        }

        public static bool IsProcessableFoodDef(ThingDef def) => def != null &&
            def.IsNutritionGivingIngestible && !IsAtomicDef(def) && !FoodEnergyReleasePolicy.IsTimedProduct(def);

        public static bool IsProcessableFood(Thing food) => IsOrdinaryFood(food) &&
            IsProcessableFoodDef(food.def) &&
            (food.TryGetComp<CompIngredients>()?.ingredients == null ||
             !food.TryGetComp<CompIngredients>().ingredients.Any(d => !IsProcessableFoodDef(d)));

        public static bool Applies(Pawn eater, Thing food)
        {
            return eater?.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel == true &&
                IsOrdinaryFood(food);
        }

        public static OrdinaryIntakeBudget LiveBudget(Pawn eater)
        {
            OrdinaryIntakeBudget budget;
            bool gain;
            return FoodSearchBudgetScope.TryGet(eater, out budget, out gain)
                ? budget : ComputeLiveBudget(eater);
        }

        internal static OrdinaryIntakeBudget ComputeLiveBudget(Pawn eater)
        {
            var ritualBudget=Ideology.AbundanceRituals.BudgetFor(eater);
            if(ritualBudget.HasValue)return ritualBudget.Value;
            var body = eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            float wanted = Mathf.Max(0f, eater.needs.food.MaxLevel - eater.needs.food.CurLevel);
            var connected = ConnectedFeederIntake.BudgetFor(eater);
            if (connected.HasValue) return connected.Value;
            var supplied = ActiveBudgetProvider?.Invoke(eater);
            if (supplied.HasValue) return supplied.Value;
            var supplemental = SupplementalBudget(eater);
            if (supplemental.HasValue) return supplemental.Value;
            if (BodyFeedingPolicy.UsesComfortableGainTarget(eater) && GainMealSession.HasOrdinaryBudget(eater))
                return new OrdinaryIntakeBudget(BodyFeedingPolicy.GainNutritionForFood(eater, null),
                    BodyFeedingPolicy.ComfortableGainTarget(eater), true, !body.ContinuousGain)
                    { AllowOrdinaryHungerFallback = true };
            if (BodyFeedingPolicy.UsesComfortableGainTarget(eater)) return OrdinaryHungerBudget(eater);
            wanted = BodyFeedingPolicy.GetRequestedNutrition(eater, wanted);
            float target = body.UsesBodyGoals ? body.SoftLimit : body.HardLimit;
            if (!body.UsesBodyGoals && body.DietMode != DietMode.Disabled &&
                !BodyFeedingPolicy.IsUrgentlyHungry(eater))
            {
                var range = body.GetRanges();
                if (body.DietMode == DietMode.Fullness || body.DietMode == DietMode.Hybrid)
                    target = Mathf.Min(target, range.Second);
            }
            return new OrdinaryIntakeBudget(wanted, target);
        }

        private static OrdinaryIntakeBudget? SupplementalBudget(Pawn eater)
        {
            var providers = SupplementalBudgetProviders;
            if (providers == null) return null;
            foreach (Func<Pawn, OrdinaryIntakeBudget?> provider in providers.GetInvocationList())
            {
                try
                {
                    var budget = provider(eater);
                    if (budget.HasValue)
                    {
                        var supplied = budget.Value;
                        supplied.Supplemental = true;
                        return supplied;
                    }
                }
                catch (Exception error)
                {
                    Log.ErrorOnce("Ordinary food supplemental budget failed: " + error, 619420);
                    return new OrdinaryIntakeBudget(0f, 0f);
                }
            }
            return null;
        }

        private static double ServingUnits(ThingDef def)
        {
            try
            {
                var metadata = def?.modExtensions?.OfType<IOrdinaryFoodServingUnits>().FirstOrDefault();
                double units = metadata?.ServingUnitsPerItem ?? 1d;
                return units > 0d && !double.IsNaN(units) && !double.IsInfinity(units) ? units : 1d;
            }
            catch (Exception error)
            {
                Log.WarningOnce("Food serving metadata unavailable for " + def?.defName + "; using one unit: " + error, 619424);
                return 1d;
            }
        }

        private static void NotifySnapshot(OrdinaryIntakeCommit snapshot)
        {
            var observers = UsefulIntakeSnapshotCommitted;
            if (snapshot == null || snapshot.UsefulNutrition <= 0d || observers == null) return;
            bool previous = notifyingSnapshot;
            notifyingSnapshot = true;
            try
            {
                foreach (Action<OrdinaryIntakeCommit> observer in observers.GetInvocationList())
                    try { observer(snapshot); }
                    catch (Exception error)
                    { Log.ErrorOnce("Ordinary food useful-dose observer failed: " + error, 619421); }
            }
            finally { notifyingSnapshot = previous; }
        }

        public static float Density(Thing food)
        {
            var represented = (food as ThingWithComps)?.AllComps.OfType<IBodyFoodVolume>()
                .FirstOrDefault(c => Finite(c.FullnessPerNutrition) && c.FullnessPerNutrition >= 0f);
            return represented?.FullnessPerNutrition ?? food.TryGetComp<ThingComp_FoodItems_NutritionDensity>()
                ?.Props.fullnessToNutritionRatio ?? FullnessAndDietStats_ThingComp.defaultFullnessToNutritionRatio;
        }

        // A negative density is the interface's explicit dormant marker. Native
        // paste carries a network-serving component too, but its uninitialized
        // exact fields do not represent that ordinary meal's nutrition/volume.
        public static IPreciseBodyFoodVolume PreciseVolume(Thing food) =>
            (food as ThingWithComps)?.AllComps.OfType<IPreciseBodyFoodVolume>()
                .FirstOrDefault(c => Finite(c.FullnessPerNutrition) && c.FullnessPerNutrition >= 0f);

        public static bool TryPlanPortion(Pawn eater, double nutrition, double volume,
            OrdinaryIntakeBudget budget, out MealPortionPlan plan)
        { return TryPlan(eater, nutrition, volume, budget, false, out plan); }

        public static bool TryPlanServing(Pawn eater, double nutrition, double volume,
            OrdinaryIntakeBudget budget, out MealPortionPlan plan)
        { return TryPlan(eater, nutrition, volume, budget, true, out plan); }

        private static bool TryPlan(Pawn eater, double nutrition, double volume,
            OrdinaryIntakeBudget budget, bool wholeServing, out MealPortionPlan plan)
        {
            plan = default(MealPortionPlan);
            var body = eater?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body?.UsesBodyModel != true || !Finite(budget.Nutrition) || !Finite(budget.FullnessTarget)) return false;
            float multiplier = body.FullnessGainedMultiplier;
            if (!Finite(multiplier) || multiplier < 0f) return false;
            double physicalRoom = Math.Max(0d,
                Math.Min((double)budget.FullnessTarget, body.HardLimit) - body.OccupiedVolume);
            // A zero volume modifier grants no physical load, but represented
            // volume still belongs in the conservation ledger.
            double room = multiplier > 0f ? physicalRoom / multiplier : double.MaxValue;
            bool planned = wholeServing && !budget.NutritionIsLimit
                ? MealPortionPlan.TryCreateServing(nutrition, volume, budget.Nutrition, room, out plan)
                : MealPortionPlan.TryCreate(nutrition, volume, budget.Nutrition, room, out plan);
            if (!planned) return false;
            // Native return values and physical fullness are floats. A positive
            // double that disappears or overflows there is a rejected intake.
            float useful = (float)plan.UsefulNutrition;
            float physical = (float)(plan.UsefulVolume * multiplier);
            return Finite(useful) && useful > 0f && Finite(physical);
        }

        public static bool TryPreview(Pawn eater, Thing food, int count, out MealPortionPlan plan,
            OrdinaryIntakeBudget? budget = null)
        {
            string reason;
            return TryPreview(eater, food, count, out plan, out reason, budget);
        }

        // Explicit diagnostic seam. Callers still use the same authoritative
        // gates; a fixture can identify rejection without weakening its test.
        public static bool TryPreview(Pawn eater, Thing food, int count, out MealPortionPlan plan,
            out string rejectionReason, OrdinaryIntakeBudget? budget = null)
        {
            plan = default(MealPortionPlan);
            rejectionReason = null;
            if (!Applies(eater, food)) { rejectionReason = "not-ordinary-adult-body-food"; return false; }
            if (count <= 0) { rejectionReason = "nonpositive-count"; return false; }
            if (food.Destroyed || !food.IngestibleNow) { rejectionReason = "destroyed-or-not-ingestible-now"; return false; }
            if (!CanEatForCurrentOrder(eater, food)) { rejectionReason = "native-suitability-or-policy-rejected"; return false; }
            var serving = (food as ThingWithComps)?.AllComps.OfType<INetworkFoodServing>().FirstOrDefault();
            FoodPreservationStamp stamp;
            if (serving != null && serving.FullnessPerNutrition >= 0f &&
                (!serving.TryReadPreservation(out stamp) || stamp.RemainingRotTicks <= 0f))
            { rejectionReason = "initialized-network-serving-preservation-rejected"; return false; }
            double energy, volume;
            RepresentedPerItem(eater, food, out energy, out volume);
            var intakeBudget = budget ?? LiveBudget(eater);
            if (!TryPlanServing(eater, energy * count, volume * count, intakeBudget, out plan))
            { rejectionReason = "no-positive-live-nutrition-or-volume-budget"; return false; }
            // Old saved social jobs can resume outside bounded collection. Keep
            // the optional-serving limit at the shared preview/commit boundary
            // as well, while preserving hunger meals and explicit orders.
            if (intakeBudget.ProtectReserve && (intakeBudget.Supplemental ||
                (!GainMealSession.Hungry(eater) &&
                !eater.TryGetComp<FullnessAndDietStats_ThingComp>().GainMealState.HungerStarted)) &&
                plan.UsefulFraction < .75d - 1e-7d)
            { rejectionReason = "optional-serving-below-75-percent-useful"; return false; }
            if (plan.UsefulFraction < 1d - 1e-7d)
            {
                rejectionReason = PartialEffectLimitation(food);
                if (rejectionReason != null) return false;
            }
            return true;
        }

        private static void RepresentedPerItem(Pawn eater, Thing food, out double energy, out double volume)
        {
            energy = FoodUtility.NutritionForEater(eater, food);
            volume = energy * Density(food);
            var precise = PreciseVolume(food);
            if (precise != null && precise.PreciseNutritionPerItem > 0d && precise.PreciseFullnessPerItem >= 0d)
            { energy = precise.PreciseNutritionPerItem; volume = precise.PreciseFullnessPerItem; }
        }

        private static bool SupportsPartial(Thing food)
        { return PartialEffectLimitation(food) == null; }

        public static string PartialEffectLimitation(Thing food)
        {
            // Unknown atomic outcomes must never receive a full dose at a
            // fractional ordinary food cost. Fail closed until an adapter exists.
            if (food.def.ingestible.outcomeDoers?.Count > 0) return "unknown-atomic-outcome:" + food.def.ingestible.outcomeDoers[0].GetType().FullName;
            if (food.GetType() != typeof(Thing) && food.GetType() != typeof(ThingWithComps)) return "unknown-food-class:" + food.GetType().FullName;
            var comps = (food as ThingWithComps)?.AllComps;
            if (comps == null) return null;
            foreach (var comp in comps)
            {
                string limitation = CompPartialLimitation(comp.GetType());
                if (limitation != null) return limitation;
            }
            return null;
        }

        private static readonly string[] IngestionCallbackNames = { "PrePostIngested", "PostIngested" };
        private static string CompPartialLimitation(Type compType)
        {
            foreach (string name in IngestionCallbackNames)
            {
                MethodInfo method = AccessTools.Method(compType, name);
                if (method == null || method.DeclaringType == typeof(ThingComp)) continue;
                // These native PostIngested declarations only preserve harmful
                // per-ingestion risks: prepared/rotten-food poisoning and
                // Anomaly food infection. They grant no fractional-cost benefit.
                // Subclasses overriding them are still unknown and fail closed.
                if (name == "PostIngested" && (method.DeclaringType == typeof(CompFoodPoisonable) ||
                    method.DeclaringType == typeof(CompRottable) || method.DeclaringType == typeof(CompMetalhorrorInfectible))) continue;
                return "unknown-comp-callback:" + compType.FullName + "." + name + " declared-by=" + method.DeclaringType.FullName;
            }
            return null;
        }

        public static bool SupportsPlan(Thing food, MealPortionPlan plan)
        { return plan.UsefulFraction >= 1d - 1e-7d || SupportsPartial(food); }

        // Menu preview precedes creation of the explicit job. Native suitability
        // is owned by the original menu; this checks the physical serving only.
        public static bool CanOrderServing(Pawn eater, Thing food)
        {
            if (!SpecialistContentPolicy.Allows(food)) return false;
            if (!Applies(eater, food)) return false;
            double nutrition, volume;
            RepresentedPerItem(eater, food, out nutrition, out volume);
            MealPortionPlan plan;
            return TryPlanServing(eater, nutrition, volume,
                new OrdinaryIntakeBudget(float.MaxValue,
                    eater.TryGetComp<FullnessAndDietStats_ThingComp>().SoftLimit), out plan) && SupportsPlan(food, plan);
        }

        private static int ExplicitOrderCount(Pawn eater, Thing food)
        {
            if (ConnectedFeederIntake.BudgetFor(eater).HasValue ||
                (ActiveBudgetProvider?.Invoke(eater)).HasValue) return 0;
            var job = eater.CurJob;
            if (job?.def != JobDefOf.Ingest || !job.playerForced || job.targetA.Thing != food) return 0;
            int count = Math.Min(food.stackCount, Math.Max(1, job.count));
            int maximum = food.def.ingestible.maxNumToIngestAtOnce;
            return maximum > 0 ? Math.Min(count, maximum) : count;
        }

        private static bool CanEatForCurrentOrder(Pawn eater, Thing food)
        {
            if (ExplicitOrderCount(eater, food) <= 0)
                return FoodUtility.WillEat(eater, food, null, true, false);

            // Native right-click consumption overrides the automatic food policy
            // and food preferences. Rechecking WillEat at the bite cancels that
            // already accepted order after pickup. Match the menu's live
            // suitability/title gates instead; capacity and portion accounting
            // still run below. ExplicitOrderCount excludes assisted/feeder
            // sessions and requires this eater's current Ingest target.
            return SpecialistContentPolicy.Allows(food) &&
                FoodUtility.FoodIsSuitable(eater, food.def) &&
                !FoodUtility.InappropriateForTitle(food.def, eater, allowIfStarving: true);
        }

        private static OrdinaryIntakeBudget ConsumptionBudget(Pawn eater, Thing food, float callerWanted)
        {
            var budget = LiveBudget(eater);
            budget.Nutrition = Mathf.Min(budget.Nutrition, callerWanted);
            // Scoped care/feeder budgets keep precedence over self-service orders.
            if (ConnectedFeederIntake.BudgetFor(eater).HasValue ||
                (ActiveBudgetProvider?.Invoke(eater)).HasValue) return budget;
            // A matching add-on job owns this bounded request. A suspended gain
            // session cannot enlarge it or be ended by its stock failure.
            if (budget.Supplemental) return budget;
            int ordered = ExplicitOrderCount(eater, food);
            if (ordered > 0)
            {
                double nutrition, volume;
                RepresentedPerItem(eater, food, out nutrition, out volume);
                return new OrdinaryIntakeBudget((float)Math.Min(float.MaxValue, nutrition * ordered),
                    eater.TryGetComp<FullnessAndDietStats_ThingComp>().SoftLimit);
            }
            if (budget.ProtectReserve && GainMealSession.HasOrdinaryBudget(eater))
            {
                budget.Nutrition = BodyFeedingPolicy.GainNutritionForFood(eater, food);
                int count = CountForBudget(eater, food, budget, 0);
                MealPortionPlan plan;
                if (count <= 0 || !TryPreview(eater, food, count, out plan, budget) ||
                    !ColonyFoodReserve.CanSpendOptional(eater, ColonyFoodReserve.StockCost(food, count), food))
                {
                    GainMealSession.NoFood(eater);
                    budget = OrdinaryHungerBudget(eater);
                    budget.Nutrition = Mathf.Min(budget.Nutrition, callerWanted);
                }
            }
            return budget;
        }

        private static OrdinaryIntakeBudget OrdinaryHungerBudget(Pawn eater)
        {
            var body = eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            bool hungry = eater.needs.food.CurLevelPercentage <= eater.RaceProps.FoodLevelPercentageWantEat ||
                BodyFeedingPolicy.IsUrgentlyHungry(eater);
            return new OrdinaryIntakeBudget(hungry ? Mathf.Max(0f, eater.needs.food.MaxLevel - eater.needs.food.CurLevel) : 0f,
                body.UsesBodyGoals ? body.SoftLimit : body.HardLimit);
        }

        public static int SelectedCount(Pawn eater, Thing food, float callerWanted)
        {
            if (!Finite(callerWanted)) return 0;
            return CountForBudget(eater, food, ConsumptionBudget(eater, food, callerWanted), ExplicitOrderCount(eater, food));
        }

        private static int CountForBudget(Pawn eater, Thing food, OrdinaryIntakeBudget budget, int ordered)
        {
            double perItem, itemVolume;
            RepresentedPerItem(eater, food, out perItem, out itemVolume);
            if (double.IsNaN(perItem) || double.IsInfinity(perItem) || perItem <= 0d || !Finite(budget.Nutrition)) return 0;
            // Respect native item count/cost. Ceiling preserves raw-food batches,
            // while a small live request still selects one whole giant meal.
            float wanted = Mathf.Max(0f, budget.Nutrition);
            var body = eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            float multiplier = body.FullnessGainedMultiplier;
            if (!Finite(multiplier) || multiplier < 0f) return 0;
            double room = multiplier > 0f ? Math.Max(0d,
                Math.Min((double)budget.FullnessTarget, body.HardLimit) - body.OccupiedVolume) / multiplier : double.MaxValue;
            int count;
            MealPortionPlan plan;
            // Do not reconstruct the authorized integer ceiling through float
            // nutrition: rounding can otherwise add a fourth item to an order
            // for three small raw-food units.
            return MealPortionPlan.TryCreateForItems(perItem, itemVolume,
                wanted, room, ordered > 0 ? ordered : food.stackCount, food.def.ingestible.maxNumToIngestAtOnce,
                out count, out plan) ? count : 0;
        }

        public static bool CanBegin(Pawn eater, Thing food, float wanted)
        {
            if (!SpecialistContentPolicy.Allows(food)) return false;
            if (!Applies(eater, food)) return true;
            MealPortionPlan plan;
            var budget = ConsumptionBudget(eater, food, wanted);
            int count = CountForBudget(eater, food, budget, ExplicitOrderCount(eater, food));
            return TryPreview(eater, food, count, out plan, budget) &&
                (!budget.ProtectReserve || ColonyFoodReserve.CanSpendOptional(eater,
                    ColonyFoodReserve.StockCost(food, count), food));
        }

        public static bool Begin(Thing food, Pawn eater, float wanted, out Transaction transaction)
        {
            transaction = null;
            if (notifyingSnapshot) return false;
            if (!SpecialistContentPolicy.Allows(food)) return false;
            if (!Applies(eater, food)) return true;
            // Reentrant food callbacks cannot reserve the same empty room twice.
            if (active != null && active.Eater == eater && !active.Committed) return false;
            var budget = ConsumptionBudget(eater, food, wanted);
            int count = CountForBudget(eater, food, budget, ExplicitOrderCount(eater, food));
            MealPortionPlan plan;
            if (!TryPreview(eater, food, count, out plan, budget)) return false;
            // Read optional metadata before reserving stock. A failing extension
            // cannot leave a lease behind or affect the existing intake owner.
            double units = ServingUnits(food.def);
            IDisposable lease = null;
            if (budget.ProtectReserve &&
                !ColonyFoodReserve.TryReserveOptional(eater, ColonyFoodReserve.StockCost(food, count), food, out lease))
            {
                // A social/connected invitation can defer; an ordinary hungry
                // self-feeder keeps the native survival serving available.
                if (!budget.AllowOrdinaryHungerFallback || !GainMealSession.IsActive(eater) ||
                    eater.TryGetComp<FullnessAndDietStats_ThingComp>().GainMealState.Owner != GainMealOwner.OrdinaryJob) return false;
                GainMealSession.NoFood(eater);
                budget = OrdinaryHungerBudget(eater);
                count = CountForBudget(eater, food, budget, 0);
                if (!TryPreview(eater, food, count, out plan, budget)) return false;
            }
            transaction = new Transaction { Food = food, Eater = eater, Count = count, Plan = plan,
                Multiplier = eater.TryGetComp<FullnessAndDietStats_ThingComp>().FullnessGainedMultiplier,
                Previous = active, ReserveLease = lease, Supplemental = budget.Supplemental,
                Snapshot = new OrdinaryIntakeCommit(eater, food.def, count, units, plan) };
            active = transaction;
            return true;
        }

        public static bool OverrideAmounts(Thing food, Pawn eater, ref int count, ref float nutrition)
        {
            if (active == null || active.Food != food || active.Eater != eater) return false;
            count = active.Count;
            nutrition = (float)active.Plan.UsefulNutrition;
            active.AmountsApplied = true;
            return true;
        }

        public static float ScaleNativeJoy(Pawn eater, float amount)
        {
            return active != null && active.Eater == eater && active.AmountsApplied && amount > 0f
                ? amount * (float)active.Plan.UsefulFraction : amount;
        }

        public static void Complete(Transaction transaction)
        {
            if (transaction == null || transaction.Committed || !transaction.AmountsApplied) return;
            if (!transaction.Eater.TryGetComp<FullnessAndDietStats_ThingComp>()
                .TryRecordOrdinaryIntake(transaction.Plan, transaction.Multiplier, FoodEnergyReleasePolicy.DurationTicks(transaction.Food))) return;
            transaction.Committed = true;
            Ideology.AbundanceRituals.Committed(transaction.Eater,transaction.Plan.UsefulNutrition,transaction.Plan.UsefulVolume);
            try
            {
                UsefulIntakeCommitted?.Invoke(transaction.Eater, transaction.Food,
                    transaction.Plan.UsefulNutrition, transaction.Plan.UsefulVolume * transaction.Multiplier);
            }
            finally { NotifySnapshot(transaction.Snapshot); }
        }

        public static void End(Transaction transaction)
        {
            transaction?.ReserveLease?.Dispose();
            if (transaction != null) transaction.ReserveLease = null;
            if (transaction?.Committed == true) ColonyFoodReserve.NotifyFoodChanged(transaction.Eater.MapHeld);
            if (transaction != null && active == transaction) active = transaction.Previous;
        }

        internal static bool HasActive(Thing food, Pawn eater)
        { return active != null && active.Food == food && active.Eater == eater; }

        internal static bool IsSupplementalCommit(Pawn eater)
        { return active != null && active.Eater == eater && active.Supplemental; }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    [HarmonyPatch(typeof(Thing), "IngestedCalculateAmounts")]
    internal static class OrdinaryIntakeAmountsPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Thing __instance, Pawn ingester, ref int numTaken, ref float nutritionIngested)
        { return !OrdinaryFoodIntake.OverrideAmounts(__instance, ingester, ref numTaken, ref nutritionIngested); }
    }

    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.FinalizeIngest))]
    internal static class OrdinaryIntakeFinalizePatch
    {
        private static void Postfix(Pawn ingester, TargetIndex ingestibleInd, Toil __result)
        {
            var original = __result.initAction;
            __result.initAction = delegate
            {
                var actor = __result.actor;
                var job = actor.CurJob;
                var food = job.GetTarget(ingestibleInd).Thing;
                // Match the native FinalizeIngest request. Counted servings
                // (including ritual meals) can be authorized after hunger is
                // satisfied; CanBegin still clamps them to the live budget.
                float wanted = ingester.needs?.food?.NutritionWanted ??
                    food.GetStatValue(StatDefOf.Nutrition) * food.stackCount;
                if (job.ingestTotalCount)
                    wanted = food.GetStatValue(StatDefOf.Nutrition) * food.stackCount;
                else if (job.overeat) wanted = Mathf.Max(wanted, .75f);
                if (!OrdinaryFoodIntake.CanBegin(ingester, food, wanted))
                { actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable); return; }
                original();
            };
        }
    }

    [HarmonyPatch(typeof(Need_Joy), nameof(Need_Joy.GainJoy))]
    internal static class OrdinaryIntakeJoyPatch
    {
        private static void Prefix(Pawn ___pawn, ref float amount)
        { amount = OrdinaryFoodIntake.ScaleNativeJoy(___pawn, amount); }
    }
}
