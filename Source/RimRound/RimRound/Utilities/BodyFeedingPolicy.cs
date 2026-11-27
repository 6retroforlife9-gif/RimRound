using RimRound.Comps;
using RimRound.Simulation;
using RimWorld;
using UnityEngine;
using Verse;
using System.Linq;

namespace RimRound.Utilities
{
    // A native food search is synchronous and does not ingest anything. Keep
    // its recipient budget local to that invocation, including nested searches.
    // The public CanFitFood entry point remains intact for care/prisoner patches.
    internal sealed class FoodSearchBudgetScope : System.IDisposable
    {
        [System.ThreadStatic] private static FoodSearchBudgetScope current;
        private readonly FoodSearchBudgetScope previous;
        private readonly Pawn eater;
        private readonly bool packing;
        private OrdinaryIntakeBudget budget;
        private bool ordinaryGain, ready, initializing, disposed;

        internal FoodSearchBudgetScope(Pawn eater, bool packing = false)
        { this.eater = eater; this.packing = packing; previous = current; current = this; }

        internal static bool IsPacking(Pawn pawn) => current?.eater == pawn && current?.packing == true;

        internal static bool TryGet(Pawn pawn, out OrdinaryIntakeBudget value, out bool gain)
        {
            value = default(OrdinaryIntakeBudget); gain = false;
            var scope = current;
            if (scope == null || scope.eater != pawn || scope.initializing) return false;
            if (!scope.ready)
            {
                scope.initializing = true;
                try
                {
                    scope.budget = OrdinaryFoodIntake.ComputeLiveBudget(pawn);
                    scope.ordinaryGain = GainMealSession.HasOrdinaryBudget(pawn);
                    scope.ready = true;
                }
                finally { scope.initializing = false; }
            }
            value = scope.budget; gain = scope.ordinaryGain; return true;
        }

        public void Dispose()
        { if (disposed) return; disposed = true; if (current == this) current = previous; }
    }

    // Packing is a native inventory job for a later meal. Today's stomach room
    // must not reject every candidate while the pawn is already comfortably fed.
    [HarmonyLib.HarmonyPatch(typeof(JobGiver_PackFood), "TryGiveJob")]
    internal static class PackFoodSearchScope
    {
        private static void Prefix(Pawn pawn, out FoodSearchBudgetScope __state)
        { __state = new FoodSearchBudgetScope(pawn, packing: true); }
        private static System.Exception Finalizer(System.Exception __exception, FoodSearchBudgetScope __state)
        { __state?.Dispose(); return __exception; }
    }

    /// <summary>
    /// Shared decision boundary for ordinary food jobs. Hunger remains owned by
    /// Need_Food; stomach volume is a physical limit, not future nutrition.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class GainMealJobCountPatch
    {
        private static readonly System.Reflection.FieldInfo MinCategory = HarmonyLib.AccessTools.Field(typeof(JobGiver_GetFood), "minCategory");
        private static readonly System.Reflection.FieldInfo MaximumLevel = HarmonyLib.AccessTools.Field(typeof(JobGiver_GetFood), "maxLevelPercentage");
        public struct SelectionState
        {
            public Pawn Previous;
            public bool Permitted, ChangedBounds;
            public HungerCategory Minimum;
            public float Maximum;
        }
        public static bool Prefix(JobGiver_GetFood __instance, Pawn pawn, ref Verse.AI.Job __result, out SelectionState __state)
        {
            __state = new SelectionState { Previous = GainMealSession.SetSelection(pawn) };
            if (!BodyFeedingPolicy.UsesComfortableGainTarget(pawn)) return true;
            bool hungry = pawn.needs.food.CurLevelPercentage <= pawn.RaceProps.FoodLevelPercentageWantEat;
            __state.Permitted = BodyFeedingPolicy.ShouldSeekFood(pawn, hungry);
            if (__state.Permitted)
            {
                if (GainMealSession.CanStartOrContinue(pawn))
                {
                    // Native think-tree copies can have stricter hunger bounds.
                    // Authorized non-hungry starts and continuations need these. Scope
                    // those bounds to this selection; never change the Food need.
                    __state.Minimum = (HungerCategory)MinCategory.GetValue(__instance);
                    __state.Maximum = (float)MaximumLevel.GetValue(__instance);
                    __state.ChangedBounds = true;
                    MinCategory.SetValue(__instance, HungerCategory.Fed);
                    MaximumLevel.SetValue(__instance, 1f);
                }
                return true;
            }
            __result = null;
            return false;
        }

        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.Last)]
        [HarmonyLib.HarmonyAfter("RimRound.FeedOther")]
        public static void Postfix(Pawn pawn, ref Verse.AI.Job __result, SelectionState __state)
        {
            Thing food = __result?.targetA.Thing;
            if (!__state.Permitted || !BodyFeedingPolicy.UsesComfortableGainTarget(pawn)) return;
            if (__result == null) { GainMealSession.NoFood(pawn); return; }
            if (__result.def == JobDefOf.Ingest && !__result.playerForced && !GainMealSession.IsActive(pawn))
            {
                Verse.AI.Job social = BodyFeedingPolicy.SocialMealJobProvider?.Invoke(pawn);
                if (social != null) { __result = social; return; }
            }
            if (__result.def == JobDefOf.Ingest && !__result.playerForced &&
                (food is Building_NutrientPasteDispenser || food is RimRound.FeedingTube.Building_FoodFaucet) &&
                GainMealSession.HasOrdinaryBudget(pawn))
            {
                // Bind the final selected job after dispenser adapters finish.
                // Its real collected meal is checked by the item transaction;
                // the building itself has no per-item calories.
                __result.count = 1;
                GainMealSession.BindJob(pawn, __result);
                return;
            }
            if (__result.def != JobDefOf.Ingest || __result.playerForced ||
                food == null || !OrdinaryFoodIntake.IsOrdinaryFood(food) || food.stackCount <= 0 ||
                !GainMealSession.HasOrdinaryBudget(pawn)) return;
            float perItem = FoodUtility.NutritionForEater(pawn, food);
            if (perItem <= .0001f) return;
            int desired = Mathf.CeilToInt(BodyFeedingPolicy.GainNutritionForFood(pawn, food) / perItem);
            int carry = pawn.carryTracker?.AvailableStackSpace(food.def) ?? 0;
            int count = Mathf.Min(food.stackCount, carry, Mathf.Max(1, desired));
            // The driver performs one ordinary ingest operation; subsequent
            // bounded collections remain part of this session when necessary.
            if (food.def.ingestible.maxNumToIngestAtOnce > 0)
                count = Mathf.Min(count, food.def.ingestible.maxNumToIngestAtOnce);
            if (count <= 0 || !ColonyFoodReserve.CanSpendOptional(pawn, ColonyFoodReserve.StockCost(food, count), food))
            { GainMealSession.NoFood(pawn); return; }
            __result.count = count;
            GainMealSession.BindJob(pawn, __result);
        }

        public static System.Exception Finalizer(System.Exception __exception, JobGiver_GetFood __instance, SelectionState __state)
        {
            if (__state.ChangedBounds)
            {
                MinCategory.SetValue(__instance, __state.Minimum);
                MaximumLevel.SetValue(__instance, __state.Maximum);
            }
            GainMealSession.SetSelection(__state.Previous);
            return __exception;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Verse.AI.JobDriver), nameof(Verse.AI.JobDriver.Cleanup))]
    public static class GainMealJobCleanupPatch
    {
        public static void Postfix(Verse.AI.JobDriver __instance, Verse.AI.JobCondition condition)
        {
            if (__instance is JobDriver_Ingest)
                GainMealSession.JobEnded(__instance.pawn, __instance.job, condition);
        }
    }

    public static class BodyFeedingPolicy
    {
        public static bool IsPackingFood(Pawn pawn) => FoodSearchBudgetScope.IsPacking(pawn);
        // Optional companion bridge. Candidate selection creates no live job or reservation.
        public static System.Func<Pawn, Verse.AI.Job> SocialMealJobProvider;
        public static System.Func<Pawn, bool> SocialMealActive;
        private const float Epsilon = 0.0001f;
        private const int ExtraMealCooldownTicks = 6000;
        private const float MinimumExtraPortionNutrition = 0.1f;
        private const float ExtraMealNutrition = 0.45f;
        private const float RecoveryMealNutrition = 0.3f;
        private const float GainDailyIntakeFactor = 1.25f;
        private const float RecoveryDailyIntakeFactor = 1.10f;

        public static bool UsesComfortableGainTarget(Pawn pawn)
        {
            var comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return comp?.UsesBodyGoals == true && comp.Goal == BodyGoal.Gain &&
                comp.HasGainAppetite && BodyModelEligibility.CanFollowBodyGoal(pawn);
        }

        public static float ComfortableGainTarget(Pawn pawn)
            => pawn.TryGetComp<FullnessAndDietStats_ThingComp>().SoftLimit * .9f;

        public static float GainNutritionForFood(Pawn pawn, Thing food)
        {
            var comp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            float ratio = (food == null ? FullnessAndDietStats_ThingComp.defaultFullnessToNutritionRatio :
                OrdinaryFoodIntake.Density(food)) * comp.FullnessGainedMultiplier;
            float room = ratio > Epsilon ? Mathf.Max(0f, ComfortableGainTarget(pawn) - comp.OccupiedVolume) / ratio
                : 0f;
            float wanted = comp.LimitGainNutrition(room);
            return IsUrgentlyHungry(pawn) ? Mathf.Max(wanted,
                pawn.needs.food.MaxLevel - pawn.needs.food.CurLevel) : wanted;
        }

        public static bool IsUrgentlyHungry(Pawn pawn)
        {
            var food = pawn?.needs?.food;
            // Native malnutrition recovers over time whenever the food need is
            // above zero. Its lingering hediff must not request another urgent
            // meal after feeding has already satisfied the pawn's hunger.
            return food != null &&
                (food.CurCategory >= HungerCategory.UrgentlyHungry ||
                (food.CurLevelPercentage <= Mathf.Max(.25f, pawn.RaceProps.FoodLevelPercentageWantEat) &&
                 pawn.health?.hediffSet?.GetFirstHediffOfDef(RimWorld.HediffDefOf.Malnutrition) != null));
        }

        public static bool CanUseExtraGainMeal(Pawn pawn)
        {
            FullnessAndDietStats_ThingComp comp =
                pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || !comp.UsesBodyModel || !comp.UsesBodyGoals ||
                !BodyModelEligibility.CanFollowBodyGoal(pawn) ||
                (comp.Goal != BodyGoal.Gain &&
                 comp.Goal != BodyGoal.Recovery) ||
                pawn.needs?.food == null ||
                pawn.Dead || pawn.Drafted || pawn.InMentalState)
                return false;
            if (!comp.HasGainAppetite) return false;
            if (comp.Goal == BodyGoal.Recovery &&
                pawn.Weight() >= comp.TargetBodyMassKg)
                return false;

            if (comp.Goal == BodyGoal.Gain)
                return GainMealSession.CanStartOrContinue(pawn) && ColonyFoodReserve.CanSpendOptional(pawn, 0d);

            if (pawn.needs.food.CurCategory >= HungerCategory.Hungry) return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (comp.LastBodyMealTick > 0 &&
                now - comp.LastBodyMealTick < ExtraMealCooldownTicks)
                return false;

            float allowance = RemainingMealAllowance(comp);
            if (!Finite(allowance) ||
                allowance < MinimumExtraPortionNutrition - Epsilon)
                return false;

            return Finite(comp.SoftLimit) &&
                comp.SoftLimit - comp.OccupiedVolume >=
                    MinimumExtraPortionNutrition *
                    FullnessAndDietStats_ThingComp.defaultFullnessToNutritionRatio *
                    Mathf.Max(0f, comp.FullnessGainedMultiplier);
        }

        public static bool ShouldSeekFood(Pawn pawn, bool vanillaDecision)
        {
            FullnessAndDietStats_ThingComp comp =
                pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || comp.Disabled || pawn.needs?.food == null)
                return vanillaDecision;
            if (IsUrgentlyHungry(pawn))
                return true;
            if (comp.UsesBodyModel && !BodyModelEligibility.CanFollowBodyGoal(pawn))
                return vanillaDecision;
            if (comp.UsesBodyGoals)
            {
                if (comp.Goal == BodyGoal.Lose && !ReadyForLossMeal(pawn)) return false;
                if (comp.Goal == BodyGoal.Gain && UsesComfortableGainTarget(pawn))
                    return ColonyFoodReserve.CanSpendOptional(pawn, 0d)
                        ? CanUseExtraGainMeal(pawn) : vanillaDecision;
                return vanillaDecision || CanUseExtraGainMeal(pawn);
            }

            if (comp.DietMode == DietMode.Disabled)
                return vanillaDecision;
            Pair<float, float> range;
            try { range = comp.GetRanges(); }
            catch { return vanillaDecision; }
            if (comp.DietMode == DietMode.Fullness)
                return comp.OccupiedVolume <= range.First;
            return pawn.needs.food.CurLevel + comp.PendingLegacyNutrition <=
                range.First;
        }

        public static bool ShouldFeedPatient(Pawn pawn, bool vanillaDecision)
        {
            // Optional gain meals belong to an awake pawn's own food job. Medical
            // care follows the game's patient eligibility and hunger decision.
            FullnessAndDietStats_ThingComp comp =
                pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp != null && comp.UsesBodyModel &&
                !BodyModelEligibility.CanFollowBodyGoal(pawn))
                return vanillaDecision;
            if (comp?.UsesBodyGoals ?? false)
                return vanillaDecision || IsUrgentlyHungry(pawn);
            return ShouldSeekFood(pawn, vanillaDecision);
        }

        public static float GetFeedingPriority(Pawn pawn, float vanillaPriority)
        {
            FullnessAndDietStats_ThingComp comp =
                pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || comp.Disabled || pawn.needs?.food == null)
                return vanillaPriority;
            if (comp.UsesBodyModel && !BodyModelEligibility.CanFollowBodyGoal(pawn))
                return vanillaPriority;
            if (comp.UsesBodyGoals)
            {
                if (IsUrgentlyHungry(pawn))
                    return Mathf.Max(vanillaPriority, 9.5f);
                if (comp.Goal == BodyGoal.Lose && !ReadyForLossMeal(pawn)) return 0f;
                if (comp.Goal == BodyGoal.Gain && UsesComfortableGainTarget(pawn))
                {
                    if (!ColonyFoodReserve.CanSpendOptional(pawn, 0d)) return vanillaPriority;
                    // A started hungry meal retains food-job priority across
                    // collection trips; the rising bar does not turn its next
                    // serving into a fresh discretionary invitation.
                    return CanUseExtraGainMeal(pawn)
                        ? Mathf.Max(vanillaPriority, GainMealSession.IsActive(pawn) ||
                            GainMealSession.Hungry(pawn) || (comp.AssignedGoal == BodyGoal.ForceGain &&
                            pawn.timetable?.CurrentAssignment == TimeAssignmentDefOf.Work) ? 9.5f : 6f) : 0f;
                }
                // During Anything time, routine work has priority 5.5. An
                // optional meal must occasionally beat that work or busy Gain
                // and Recovery pawns never act on their goal. Scheduled Work
                // (9), sleep and urgent hunger still take precedence; the meal
                // cooldown, daily budget and whole-portion fit remain required.
                return CanUseExtraGainMeal(pawn)
                    ? Mathf.Max(vanillaPriority, 6f)
                    : vanillaPriority;
            }
            return ShouldSeekFood(pawn, vanillaPriority > 0f)
                ? Mathf.Max(vanillaPriority, 9.5f)
                : 0f;
        }

        public static float GetRequestedNutrition(Pawn pawn, float vanillaWanted)
        {
            FullnessAndDietStats_ThingComp comp =
                pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || comp.Disabled || pawn.needs?.food == null)
                return vanillaWanted;
            if (comp.UsesBodyModel && !BodyModelEligibility.CanFollowBodyGoal(pawn))
                return vanillaWanted;
            if (comp.UsesBodyGoals)
            {
                bool hungry = pawn.needs.food.CurLevelPercentage <= pawn.RaceProps.FoodLevelPercentageWantEat;
                // Live decision before commit: another meal, preference change
                // or helper may have satisfied the original optional request.
                // Explicit self/social/connected-feeder budgets still own their
                // separate authorization in OrdinaryFoodIntake.
                if (!ShouldSeekFood(pawn, hungry)) return 0f;
                if (UsesComfortableGainTarget(pawn) && CanUseExtraGainMeal(pawn))
                {
                    float ratio = FullnessAndDietStats_ThingComp.defaultFullnessToNutritionRatio * comp.FullnessGainedMultiplier;
                    float wanted = ratio > Epsilon ? Mathf.Max(0f, ComfortableGainTarget(pawn) - comp.OccupiedVolume) / ratio : vanillaWanted;
                    wanted = comp.LimitGainNutrition(wanted);
                    return IsUrgentlyHungry(pawn) ? Mathf.Max(vanillaWanted, wanted) : wanted;
                }
                if (CanUseExtraGainMeal(pawn))
                {
                    float allowance = RemainingMealAllowance(comp);
                    float optionalPortion = Mathf.Min(comp.Goal == BodyGoal.Recovery
                            ? RecoveryMealNutrition : ExtraMealNutrition,
                            Mathf.Max(0f, allowance));
                    return pawn.needs.food.CurLevelPercentage > pawn.RaceProps.FoodLevelPercentageWantEat
                        ? optionalPortion : Mathf.Max(vanillaWanted, optionalPortion);
                }
                return vanillaWanted;
            }
            if (comp.DietMode == DietMode.Disabled || IsUrgentlyHungry(pawn))
                return vanillaWanted;
            Pair<float, float> range;
            try { range = comp.GetRanges(); }
            catch { return vanillaWanted; }
            if (comp.DietMode == DietMode.Fullness ||
                comp.DietMode == DietMode.Hybrid)
            {
                float ratio = Mathf.Max(Epsilon,
                    comp.CurrentFullnessToNutritionRatio);
                return Mathf.Max(0f, (range.Second - comp.OccupiedVolume) /
                    ratio);
            }
            return Mathf.Max(0f, range.Second - pawn.needs.food.CurLevel -
                comp.PendingLegacyNutrition);
        }

        public static bool CanFitFood(Pawn pawn, Thing food, float nutrition)
        {
            if (!SpecialistContentPolicy.Allows(food)) return false;
            FullnessAndDietStats_ThingComp comp =
                pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || comp.Disabled || food == null ||
                !Finite(nutrition) || nutrition <= 0f)
                return true;
            if (comp.UsesBodyModel && !BodyModelEligibility.CanFollowBodyGoal(pawn) && comp.PassengerVolume <= 0f)
                return true;
            if (comp.UsesBodyModel)
            {
                if (!OrdinaryFoodIntake.IsOrdinaryFood(food)) return false;
                OrdinaryIntakeBudget budget;
                bool gain;
                bool cached = FoodSearchBudgetScope.TryGet(pawn, out budget, out gain);
                MealPortionPlan plan;
                bool fits = OrdinaryFoodIntake.TryPlanServing(pawn, nutrition,
                    (double)nutrition * OrdinaryFoodIntake.Density(food),
                    cached ? budget : OrdinaryFoodIntake.LiveBudget(pawn), out plan) &&
                    OrdinaryFoodIntake.SupportsPlan(food, plan);
                if (!fits) return false;
                // Hunger already permits this fitting source, regardless of the
                // optional reserve. Avoid rebuilding the colony-wide reserve for
                // each hungry search; collection and intake keep their live checks.
                if (!(cached ? gain : GainMealSession.HasOrdinaryBudget(pawn)) ||
                    pawn.needs.food.CurLevelPercentage <= pawn.RaceProps.FoodLevelPercentageWantEat || IsUrgentlyHungry(pawn)) return true;
                return ColonyFoodReserve.CanSpendOptional(pawn, ColonyFoodReserve.StockCost(food, 1), food);
            }
            float ratio = OrdinaryFoodIntake.Density(food);
            return CanFitPortion(pawn, nutrition, ratio);
        }

        // Positive useful fit; large rejected remainder is waste at commit.
        public static bool CanFitPortion(Pawn pawn, float nutrition, float fullnessPerNutrition)
        {
            var comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || comp.Disabled || !comp.UsesBodyModel) return true;
            MealPortionPlan plan;
            return OrdinaryFoodIntake.TryPlanPortion(pawn, nutrition,
                (double)nutrition * fullnessPerNutrition,
                OrdinaryFoodIntake.LiveBudget(pawn), out plan);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool ReadyForLossMeal(Pawn pawn)
        {
            // Whole standard meals otherwise overshoot the hunger bar and can
            // defeat a loss goal even with reserve use. Native patient/warden
            // care starts before urgent hunger: keep that same permission at
            // food selection and the final bite, including Maintain's Lose
            // regulation action. ShouldSeekFood still requires ordinary hunger.
            return FoodUtility.ShouldBeFedBySomeone(pawn) ||
                pawn.needs.food.CurLevelPercentage <= pawn.needs.food.PercentageThreshUrgentlyHungry + 0.01f ||
                IsUrgentlyHungry(pawn);
        }

        private static float RemainingMealAllowance(FullnessAndDietStats_ThingComp comp)
        {
            return DailyIntakeFactor(comp) * comp.EstimatedDailyExpenditureNutrition +
                comp.CarriedMealAllowance - comp.DailyIntakeNutrition;
        }

        internal static float DailyIntakeFactor(
            FullnessAndDietStats_ThingComp comp)
        {
            return comp.Goal == BodyGoal.Recovery
                ? RecoveryDailyIntakeFactor : GainDailyIntakeFactor;
        }
    }
}
