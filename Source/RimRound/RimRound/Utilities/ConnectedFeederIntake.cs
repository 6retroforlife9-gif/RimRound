using System;
using HarmonyLib;
using RimRound.Comps;
using RimRound.FeedingTube;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    // The override lives only for one synchronous connected-feeder transaction.
    // No policy is edited and ordinary eating never inherits the override.
    public sealed class ConnectedFeederIntake : IDisposable
    {
        [ThreadStatic] private static ConnectedFeederIntake active;
        private readonly ConnectedFeederIntake previous;
        private readonly Pawn pawn;
        private readonly OrdinaryIntakeBudget budget;
        public bool OptionalGainAuthorized { get; }
        public static Func<float> MaximumTargetFraction;
        public static float TargetFor(FullnessAndDietStats_ThingComp body, AutoFeederMode mode)
            => mode == AutoFeederMode.maxgain
                ? body.HardLimit * Mathf.Clamp(MaximumTargetFraction?.Invoke() ?? .9f, 0f, .95f)
                : body.SoftLimit * .9f;
        public ConnectedFeederIntake(Pawn pawn, AutoFeederMode mode, bool reserveExternally = false, bool ordinaryOnly = false)
        {
            this.pawn = pawn;
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            float target = body?.SoftLimit ?? float.MaxValue;
            float wanted = Mathf.Max(0f, pawn.needs.food.MaxLevel - pawn.needs.food.CurLevel);
            if (!ordinaryOnly && body?.UsesBodyModel == true && (mode == AutoFeederMode.gain || mode == AutoFeederMode.maxgain))
            {
                OptionalGainAuthorized = mode == AutoFeederMode.gain ? GainMealSession.BeginConnected(pawn) :
                    ColonyFoodReserve.CanSpendOptional(pawn, 0d);
                if (OptionalGainAuthorized)
                {
                    target = TargetFor(body, mode);
                    wanted = float.MaxValue;
                    if (body.Goal == RimRound.Simulation.BodyGoal.Gain && !body.ContinuousGain && mode == AutoFeederMode.gain)
                        wanted = body.AdditionalGoalNutrition;
                }
                else if (ColonyFoodReserve.CanSpendOptional(pawn, 0d) && !BodyFeedingPolicy.IsUrgentlyHungry(pawn))
                    wanted = 0f;
            }
            budget = new OrdinaryIntakeBudget(wanted, target,
                protectReserve: OptionalGainAuthorized && !reserveExternally, nutritionIsLimit: true);
            previous = active; active = this;
        }
        public static OrdinaryIntakeBudget? BudgetFor(Pawn pawn)
            => active != null && active.pawn == pawn ? active.budget : (OrdinaryIntakeBudget?)null;
        public void Dispose() { active = previous; }

        [HarmonyPatch(typeof(Pawn_FoodRestrictionTracker), nameof(Pawn_FoodRestrictionTracker.GetCurrentRespectedRestriction))]
        public static class PolicyScope
        {
            public static void Postfix(Pawn_FoodRestrictionTracker __instance, ref FoodPolicy __result)
            {
                if (active?.pawn.foodRestriction == __instance) __result = null;
            }
        }
    }
}
