using RimRound.Simulation;
using System.Linq;
using RimWorld;
using Verse;

namespace RimRound.Utilities
{
    public static class FoodEnergyReleasePolicy
    {
        // One catalogue for absorption and the no-processing/atomic-dose gate.
        // Density, body goals and dispenser settings never grant drug permission.
        public static bool IsTimedProduct(ThingDef def)
        {
            if (MetabolicDrugIntake.IsMetabolicDrug(def)) return true;
            switch (def?.defName)
            {
                case "RR_GluttoniumMeal": case "RR_GluttoniumMealLarge":
                case "RR_GluttoniumMealHuge": case "RR_GluttoniumMealEnormous":
                case "RR_GluttoniumMealMassive": case "RR_GluttoniumMealColossal":
                case "RR_LaBombe": case "RR_AlphaBall": case "RR_OmegaBall": return true;
                default: return false;
            }
        }

        public static int DurationTicks(ThingDef def) => !IsTimedProduct(def) ? 0 :
            ColonyProgression.FantasyActive ? FoodEnergyReleaseState.FantasyDoseTicks :
            FoodEnergyReleaseState.BalancedDoseTicks;

        public static int DurationTicks(Thing food) => food == null ? 0 :
            IsTimedProduct(food.def) || food.TryGetComp<CompIngredients>()?.ingredients?.Any(IsTimedProduct) == true
                ? (ColonyProgression.FantasyActive ? FoodEnergyReleaseState.FantasyDoseTicks : FoodEnergyReleaseState.BalancedDoseTicks) : 0;
    }
}
