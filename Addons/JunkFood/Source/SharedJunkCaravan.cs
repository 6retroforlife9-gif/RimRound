using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Oinky.JunkFood
{
    public sealed class JunkCaravanReserveRecord : IExposable
    {
        public Caravan Caravan;
        public bool Paused;
        public void ExposeData()
        {
            Scribe_References.Look(ref Caravan, "caravan");
            Scribe_Values.Look(ref Paused, "paused");
        }
    }
    public sealed partial class JunkFoodGameComponent
    {
        public JunkCaravanReserveRecord CaravanReserve(Caravan caravan)
        {
            var row = caravanReserves.FirstOrDefault(r => r.Caravan == caravan);
            if (row == null) { row = new JunkCaravanReserveRecord { Caravan = caravan }; caravanReserves.Add(row); }
            return row;
        }
    }

    public static class JunkCaravanUtility
    {
        // The world updates synchronously. An exclusive short transaction also
        // blocks reentrant callbacks from spending the same caravan snapshot.
        private static readonly HashSet<Caravan> leases = new HashSet<Caravan>();
        private sealed class Lease : IDisposable
        {
            private Caravan caravan;
            internal Lease(Caravan value) { caravan = value; leases.Add(value); }
            public void Dispose() { if (caravan != null) leases.Remove(caravan); caravan = null; }
        }
        private static bool OrdinaryStock(Thing food) => food != null && !food.Destroyed && food.stackCount > 0 &&
            food.def.category == ThingCategory.Item && food.def.IsNutritionGivingIngestible && !food.def.IsDrug &&
            food.def.defName != "fatpill" && !(food is Corpse) && !(food is Plant) && food.IngestibleNow &&
            !food.IsNotFresh() && !food.IsDessicated() && SpecialistContentPolicy.Allows(food) &&
            food.def.ingestible.outcomeDoers?.Any(d => d.GetType().Name.IndexOf("Hemogen", StringComparison.OrdinalIgnoreCase) >= 0) != true &&
            food.TryGetComp<CompIngredients>()?.ingredients.Any(d => d != null && (d.IsDrug || d.defName == "fatpill" ||
                d.ingestible?.outcomeDoers?.Any(o => o.GetType().Name.IndexOf("Hemogen", StringComparison.OrdinalIgnoreCase) >= 0) == true)) != true;

        private static double Expiry(Caravan caravan, Thing food)
        {
            var rot = food.TryGetComp<CompRottable>();
            double ticks = double.MaxValue;
            if (rot != null)
            {
                // Conservative travel estimate: never bank refrigeration, and
                // include hotter predicted seasonal temperatures over the guard
                // horizon. Future unknown routes/forage/milk are not invented.
                float temperature = 50f;
                for (int day = 0; day <= 4; day++)
                    temperature = Mathf.Max(temperature, GenTemperature.GetTemperatureFromSeasonAtTile(
                        Find.TickManager.TicksAbs + day * 60000, caravan.Tile));
                ticks = rot.TicksUntilRotAtTemp(temperature);
            }
            var represented = (food as ThingWithComps)?.AllComps.OfType<INetworkFoodServing>()
                .FirstOrDefault(c => c.FullnessPerNutrition >= 0f);
            if (represented != null)
            {
                FoodPreservationStamp stamp;
                if (!represented.TryReadPreservation(out stamp) || !stamp.Valid) return double.NaN;
                ticks = Math.Min(ticks, stamp.RemainingRotTicks);
            }
            return ticks / SharedHabitState.Day;
        }

        public static bool CanSpend(Caravan caravan, Pawn eater, Thing source, double cost)
        {
            var state = JunkFoodPolicy.Colony.CaravanReserve(caravan);
            try
            {
                if (caravan == null || caravan.Destroyed || !caravan.Tile.Valid || source == null ||
                    !FoodReserveAllocation.Finite(cost) || cost <= 0d || CaravanInventoryUtility.GetOwnerOf(caravan, source) == null)
                { state.Paused = true; return false; }
                Pawn[] eaters = caravan.PawnsListForReading.Where(p => !p.Dead && !p.Suspended &&
                    p.needs?.food != null && BodyModelEligibility.UsesFoodMetabolism(p)).ToArray();
                Thing[] foods = CaravanInventoryUtility.AllInventoryItems(caravan).Where(OrdinaryStock).ToArray();
                int sourceIndex = Array.IndexOf(foods, source);
                if (eaters.Length == 0 || sourceIndex < 0) { state.Paused = true; return false; }
                double[] supply = foods.Select(f => ColonyFoodReserve.StockCost(f, f.stackCount)).ToArray();
                double[] expiry = foods.Select(f => Expiry(caravan, f)).ToArray();
                double[] demand = eaters.Select(p => p.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed, true) * 60000d).ToArray();
                bool[][] accepts = foods.Select(f => eaters.Select(p => CaravanPawnsNeedsUtility.CanEatForNutritionEver(f.def, p) &&
                    FoodUtility.WillEat(p, f, CaravanInventoryUtility.GetOwnerOf(caravan, f), true, false)).ToArray()).ToArray();
                int eaterIndex = Array.IndexOf(eaters, eater);
                if (eaterIndex < 0 || !accepts[sourceIndex][eaterIndex] || supply[sourceIndex] + 1e-10d < cost) return false;
                for (int j = 0; j < eaters.Length; j++)
                {
                    double factor = 1d;
                    for (int i = 0; i < foods.Length; i++) if (accepts[i][j])
                        factor = Math.Min(factor, FoodUtility.NutritionForEater(eaters[j], foods[i]) / ColonyFoodReserve.StockCost(foods[i], 1));
                    if (!FoodReserveAllocation.Finite(factor) || factor <= 0d) { state.Paused = true; return false; }
                    demand[j] /= factor;
                }
                double days = FoodReserveAllocation.Days(supply, demand, accepts, expiryDays: expiry);
                double[] after = (double[])supply.Clone(); after[sourceIndex] = Math.Max(0d, after[sourceIndex] - cost);
                double projected = FoodReserveAllocation.Days(after, demand, accepts, expiryDays: expiry);
                return FoodReserveAllocation.Allows(days, projected, ref state.Paused);
            }
            catch (Exception error)
            {
                state.Paused = true;
                Log.WarningOnce("Junk-food caravan reserve unavailable; optional snacks deferred: " + error, 619423);
                return false;
            }
        }

        public static bool TrySnack(Caravan caravan, Pawn pawn, JunkSnackPurpose purpose)
        {
            if (!JunkSnackUtility.Wants(pawn, purpose, true) || leases.Contains(caravan)) return false;
            var row = JunkFoodPolicy.Colony.SharedRecord(pawn);
            if (JunkSharedRules.Now < row.RetryAfterTick) return false;
            using (new Lease(caravan))
            {
                foreach (Thing food in CaravanInventoryUtility.AllInventoryItems(caravan).ToArray()
                    .Where(t => JunkFoodPolicy.IsJunk(t.def)).OrderBy(t => JunkSnackUtility.Units(t.def)).ThenBy(t => t.thingIDNumber))
                {
                    if (!JunkSnackUtility.Suitable(pawn, food)) continue;
                    Thing preview = food;
                    bool package = AutomaticPackageServing.IsPackage(food);
                    bool temporary = package && PackageServing.TryMakePortions(food, out preview, true);
                    if (package && !temporary) continue;
                    bool acceptable;
                    try
                    {
                        MealPortionPlan plan;
                        acceptable = JunkSnackUtility.Preview(pawn, preview, purpose, out plan, true) &&
                            CanSpend(caravan, pawn, food, ColonyFoodReserve.StockCost(food, 1) / JunkSnackUtility.Units(food.def));
                    }
                    finally { if (temporary && preview != null && !preview.Destroyed) preview.Destroy(); }
                    if (!acceptable) continue;
                    Thing serving = food;
                    if (package)
                    {
                        if (!PackageServing.TryOpenHeld(food, out serving)) continue;
                        // Opening changes native food identity/eligibility even
                        // if the subsequent bite is deferred by a fresh guard.
                        caravan.RecacheInventory();
                    }
                    // The holder-safe opening changed identity, not quantity.
                    // Rebuild against that exact child immediately before intake.
                    if (!CanSpend(caravan, pawn, serving, ColonyFoodReserve.StockCost(serving, 1))) continue;
                    bool consumed = JunkSnackUtility.Consume(pawn, serving, purpose, true);
                    caravan.RecacheInventory();
                    if (consumed) { row.RetryAfterTick = JunkSharedRules.Now + 120; return true; }
                }
            }
            row.RetryAfterTick = JunkSharedRules.Now + 600;
            return false;
        }

        public static void Tick(Caravan caravan)
        {
            if (!JunkSharedRules.Enabled || caravan == null || caravan.Destroyed) return;
            foreach (Pawn pawn in caravan.PawnsListForReading.ToArray())
            {
                if (JunkSnackUtility.Wants(pawn, JunkSnackPurpose.Relief, true)) TrySnack(caravan, pawn, JunkSnackPurpose.Relief);
                else if (JunkSnackUtility.Wants(pawn, JunkSnackPurpose.Schedule, true)) TrySnack(caravan, pawn, JunkSnackPurpose.Schedule);
            }
        }
    }

    [HarmonyPatch(typeof(CaravanDrugPolicyUtility), nameof(CaravanDrugPolicyUtility.TryTakeScheduledDrugs), new[] { typeof(Caravan) })]
    internal static class SharedJunkCaravanSnacks
    { static void Postfix(Caravan caravan) { JunkCaravanUtility.Tick(caravan); } }
}
