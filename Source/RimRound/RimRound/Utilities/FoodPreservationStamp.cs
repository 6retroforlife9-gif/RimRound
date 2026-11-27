using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimRound.Utilities
{
    // Freshness metadata only. Stored nutrition and fullness remain owned by the
    // classic vat or v2 batch that carries this stamp.
    public struct FoodPreservationStamp
    {
        // Use the game's simple-meal clock (four days in vanilla 1.6).
        // The fallback is only for callers running before Defs are available.
        public static float VatLifeTicks =>
            DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple")
                ?.GetCompProperties<CompProperties_Rottable>()?.TicksToRotStart
            ?? 4 * GenDate.TicksPerDay;

        public float RemainingRotTicks;
        public float PoisonFraction;
        public FoodPoisonCause PoisonCause;
        public bool AssumedFreshLegacy;
        public List<ThingDef> Ingredients;

        public static FoodPreservationStamp Fresh
        {
            get { return new FoodPreservationStamp { RemainingRotTicks = VatLifeTicks }; }
        }

        public static FoodPreservationStamp LegacyGrace
        {
            get
            {
                return new FoodPreservationStamp
                {
                    RemainingRotTicks = VatLifeTicks,
                    AssumedFreshLegacy = true
                };
            }
        }

        // Old saves and external callers may still carry poison fields. Liquid
        // food no longer uses them, including when an old value is invalid.
        public bool Valid => Finite(RemainingRotTicks) && RemainingRotTicks >= 0f;

        public FoodPreservationStamp WithoutPoison
        {
            get
            {
                FoodPreservationStamp result = this;
                result.PoisonFraction = 0f;
                result.PoisonCause = FoodPoisonCause.Unknown;
                return result;
            }
        }

        public FoodPreservationStamp Aged(float effectiveTicks)
        {
            if (!Valid || !Finite(effectiveTicks) || effectiveTicks <= 0f)
                return this;
            FoodPreservationStamp result = WithoutPoison;
            result.RemainingRotTicks = UnityEngine.Mathf.Max(0f,
                RemainingRotTicks - effectiveTicks);
            return result;
        }

        public static FoodPreservationStamp Combine(
            FoodPreservationStamp first, FoodPreservationStamp second)
        {
            if (!first.Valid || !second.Valid)
                return new FoodPreservationStamp { RemainingRotTicks = 0f };
            var ingredients = new List<ThingDef>();
            if (first.Ingredients != null)
                foreach (ThingDef ingredient in first.Ingredients)
                    if (ingredient != null && !ingredients.Contains(ingredient))
                        ingredients.Add(ingredient);
            if (second.Ingredients != null)
                foreach (ThingDef ingredient in second.Ingredients)
                    if (ingredient != null && !ingredients.Contains(ingredient))
                        ingredients.Add(ingredient);
            return new FoodPreservationStamp
            {
                RemainingRotTicks = UnityEngine.Mathf.Min(
                    first.RemainingRotTicks, second.RemainingRotTicks),
                AssumedFreshLegacy = first.AssumedFreshLegacy ||
                    second.AssumedFreshLegacy,
                Ingredients = ingredients
            };
        }

        public static FoodPreservationStamp FromFood(Thing food)
        {
            FoodPreservationStamp result = Fresh;
            ThingWithComps withComps = food as ThingWithComps;
            if (withComps == null)
            {
                if (food?.def != null)
                    result.Ingredients = new List<ThingDef> { food.def };
                return result;
            }
            bool carriedServing = false;
            foreach (ThingComp comp in withComps.AllComps)
            {
                INetworkFoodServing serving = comp as INetworkFoodServing;
                FoodPreservationStamp carried;
                if (serving != null &&
                    serving.TryReadPreservation(out carried))
                {
                    result = Combine(result, carried);
                    carriedServing = true;
                    break;
                }
            }

            CompRottable rottable = food.TryGetComp<CompRottable>();
            if (rottable != null)
            {
                float remaining = rottable.PropsRot.TicksToRotStart -
                    rottable.RotProgress;
                // Processing ordinary ingredients starts a cooked-food clock.
                // Returning collected network food must never renew its age.
                // Already rotten ingredients are never usable.
                if (Finite(remaining) && (carriedServing || remaining <= 0f))
                    result.RemainingRotTicks = UnityEngine.Mathf.Min(
                        result.RemainingRotTicks,
                        UnityEngine.Mathf.Max(0f, remaining));
            }
            CompIngredients sourceIngredients =
                food.TryGetComp<CompIngredients>();
            List<ThingDef> ingredients = result.Ingredients == null
                ? new List<ThingDef>()
                : new List<ThingDef>(result.Ingredients);
            if (sourceIngredients != null &&
                sourceIngredients.ingredients != null)
                foreach (ThingDef ingredient in sourceIngredients.ingredients)
                    if (ingredient != null &&
                        !ingredients.Contains(ingredient))
                        ingredients.Add(ingredient);
            if (ingredients.Count == 0 && food.def != null)
                ingredients.Add(food.def);
            result.Ingredients = ingredients;
            return result;
        }

        public string TimeUntilSpoilage(float temperature)
        {
            float rate = GenTemperature.RotRateAtTemperature(temperature);
            double ticks = rate > 0f ? RemainingRotTicks / (double)rate : int.MaxValue;
            return ((int)System.Math.Min(int.MaxValue, System.Math.Max(0d, ticks)))
                .ToStringTicksToPeriod();
        }

        // Retained for binary compatibility with older liquid-feed callers.
        // Contamination is no longer a mechanic of this food network.
        public void ApplyNativePoison(Pawn pawn, Thing foodSource)
        {
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
