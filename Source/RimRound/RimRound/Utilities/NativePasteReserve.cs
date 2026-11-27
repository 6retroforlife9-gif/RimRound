using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    internal sealed class FoodReserveIngredient
    {
        internal Thing Source;
        internal double Nutrition;
    }

    /// <summary>Reserve the native dispenser's actual hopper debit, not its output calories.</summary>
    public static class NativePasteReserve
    {
        // The companion supplies the same ingredient-aware diet check used by
        // liquid meals, without introducing an assembly dependency on it.
        public static Func<Pawn, ThingDef, System.Collections.Generic.IEnumerable<ThingDef>, Pawn, bool> OutputDietAllows;

        internal static bool CanServeIngredient(Pawn eater, Thing ingredient,
            Building_NutrientPasteDispenser[] dispensers)
        {
            if (eater == null || ingredient?.Spawned != true ||
                !Building_NutrientPasteDispenser.IsAcceptableFeedstock(ingredient.def) ||
                OutputDietAllows == null || !ingredient.Position.GetThingList(ingredient.Map).Any(t => t.IsHopper())) return false;
            foreach (var dispenser in dispensers)
            {
                if (dispenser.GetType() != typeof(Building_NutrientPasteDispenser) || !dispenser.CanDispenseNow ||
                    !dispenser.AdjCellsCardinalInBounds.Contains(ingredient.Position) ||
                    !ColonyFoodReserve.CanReachFood(eater, dispenser, true)) continue;
                var inputs = dispenser.AdjCellsCardinalInBounds
                    .Where(c => c.GetThingList(dispenser.Map).Any(t => t.IsHopper()))
                    .SelectMany(c => c.GetThingList(dispenser.Map))
                    .Where(t => Building_NutrientPasteDispenser.IsAcceptableFeedstock(t.def)).ToArray();
                // Credit only existing raw nutrition, once. No conversion bonus
                // or hypothetical hauling/production. Reject altered recipes
                // whose rounded input cost could exceed their output nutrition.
                double cost = dispenser.def.building.nutritionCostPerDispense;
                double output = dispenser.DispensableDef.GetStatValueAbstract(StatDefOf.Nutrition);
                double largestUnit = inputs.Select(t => (double)t.GetStatValue(StatDefOf.Nutrition)).DefaultIfEmpty(double.PositiveInfinity).Max();
                if (!Simulation.FoodReserveAllocation.Finite(cost) || cost <= 0d ||
                    !Simulation.FoodReserveAllocation.Finite(largestUnit) || largestUnit <= 0d ||
                    !Simulation.FoodReserveAllocation.Finite(output) || output < cost + largestUnit) continue;
                if (OutputDietAllows(eater, dispenser.DispensableDef, inputs.Select(t => t.def), null)) return true;
            }
            return false;
        }

        public static bool TryReserve(Pawn eater, Building_NutrientPasteDispenser dispenser,
            out IDisposable lease)
        {
            lease = null;
            if (eater?.MapHeld == null || dispenser?.Map != eater.MapHeld ||
                dispenser.GetType() != typeof(Building_NutrientPasteDispenser) || !dispenser.CanDispenseNow ||
                !FoodUtility.WillEat(eater, dispenser.DispensableDef, null, true, false)) return false;

            // Mirror 1.6 FindFeedInAnyHopper order and TryDispenseFood's integer
            // rounding without changing stock. Exhausted stacks reveal the next
            // eligible thing in that cell before scanning subsequent hoppers.
            var remaining = new Dictionary<Thing, int>();
            foreach (IntVec3 cell in dispenser.AdjCellsCardinalInBounds)
                if (cell.GetThingList(dispenser.Map).Any(t => t.IsHopper()))
                    foreach (Thing food in cell.GetThingList(dispenser.Map))
                        if (Building_NutrientPasteDispenser.IsAcceptableFeedstock(food.def))
                            remaining[food] = food.stackCount;
            float wanted = dispenser.def.building.nutritionCostPerDispense - .0001f;
            if (float.IsNaN(wanted) || float.IsInfinity(wanted) || wanted <= 0f) return false;
            var debits = new List<FoodReserveIngredient>();
            while (wanted > 0f)
            {
                Thing next = null;
                foreach (IntVec3 cell in dispenser.AdjCellsCardinalInBounds)
                {
                    foreach (Thing food in cell.GetThingList(dispenser.Map))
                        if (remaining.TryGetValue(food, out int available) && available > 0) next = food;
                    if (next != null) break;
                }
                if (next == null || (debits.Count == 0 && dispenser.FindFeedInAnyHopper() != next)) return false;
                float nutrition = next.GetStatValue(StatDefOf.Nutrition);
                if (float.IsNaN(nutrition) || float.IsInfinity(nutrition) || nutrition <= 0f) return false;
                int count = Math.Min(remaining[next], Mathf.CeilToInt(wanted / nutrition));
                if (count <= 0) return false;
                double cost = ColonyFoodReserve.StockCost(next, count);
                if (!Simulation.FoodReserveAllocation.Finite(cost) || cost <= 0d) return false;
                debits.Add(new FoodReserveIngredient { Source = next, Nutrition = cost });
                remaining[next] -= count;
                wanted -= count * nutrition;
            }
            return ColonyFoodReserve.TryReserveIngredients(eater, debits.ToArray(), out lease);
        }
    }
}
