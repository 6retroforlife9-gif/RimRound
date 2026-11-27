using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.FeedingTube;
using RimRound.FeedingTube.Comps;
using RimRound.Utilities;
using RimRound.Simulation;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    /// <summary>Liquid authority adapter. No meal previews, intake budgets or gain policies are called here.</summary>
    [StaticConstructorOnStartup]
    public static class FoodNetworkFoodReserve
    {
        static FoodNetworkFoodReserve()
        {
            NativePasteReserve.OutputDietAllows = LiquidDietAllows;
            ColonyFoodReserve.RegisterLiquidProvider("RimRound.Liquid", Collect);
        }

        public static IEnumerable<FoodReserveSupply> Collect(Map map, IReadOnlyList<Pawn> residents)
        {
            if (map == null) yield break;
            var buildings = map.listerThings.AllThings.OfType<Building>().Where(b => b.Spawned).ToArray();
            var failures = new Dictionary<Building, Dictionary<Pawn, string>>();
            // Keep an outlet's identity even when no whole serving is available,
            // or it is empty, disconnected or offline. Otherwise an absent pool
            // makes an exact outlet debit look like food of unknown origin and
            // lets unrelated solid stock authorize its reservation.
            foreach (Building outlet in buildings.Where(IsOutlet))
                yield return new FoodReserveSupply { Key = "liquid:outlet:" + outlet.thingIDNumber,
                    Source = outlet, Liquid = true, Nutrition = 0d, CanServe = p => false,
                    RejectionReason = pawn => OutletReason(outlet, pawn, failures) };
            var tanks = buildings.Where(t => t.TryGetComp<FoodNetStorage_ThingComp>() != null).ToArray();
            if (FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                var topology = FoodNetworkV2MapComponent.For(map);
                topology?.EnsureTopology();
                var consumed = new HashSet<int>();
                foreach (FoodNetworkV2 network in topology?.Networks ?? new FoodNetworkV2[0])
                {
                    var members = network.Nodes.OfType<Building>().Where(t => t.TryGetComp<FoodNetStorage_ThingComp>() != null).ToArray();
                    foreach (Building tank in members) consumed.Add(tank.thingIDNumber);
                    var batches = new List<FoodBatchV2>();
                    var batchOwners = new Dictionary<FoodBatchV2, int>();
                    foreach (Building tank in members)
                    {
                        FoodTankStateV2 state = FoodNetworkV2GameComponent.Instance?.GetTankState(tank, false);
                        if (state == null) continue;
                        state.AgeTo(ColonyTick, FoodNetworkV2GameComponent.TemperatureFor(tank), FoodNetworkV2GameComponent.CoolingActive(tank));
                        foreach (var batch in state.batches.Where(b => b != null && !b.Empty && b.Preservation.RemainingRotTicks > 0f))
                        { batches.Add(batch); batchOwners[batch] = tank.thingIDNumber; }
                    }
                    double nutrition = batches.Sum(b => b.PreciseNutrition);
                    if (nutrition <= 0d) continue;
                    var ingredients = batches.SelectMany(b => b.ingredients ?? new List<ThingDef>()).Where(d => d != null).Distinct().ToArray();
                    var outlets = network.Nodes.OfType<Building>().Where(IsOutlet).ToArray();
                    var amounts = batches.OrderBy(b => b.createdTick).ThenBy(b => batchOwners[b])
                        .Select(b => new FoodAmount(b.PreciseNutrition, b.PreciseFullness)).ToArray();
                    // FIFO can mix ingredients between tanks. Using the union
                    // conservatively refuses a diet rather than skipping a forbidden front batch.
                    foreach (FoodReserveSupply pool in CreatePools("liquid:v2:" + map.uniqueID + ":" + network.Id,
                        nutrition, ingredients, outlets, network.Nodes, true, residents, amounts, failures)) yield return pool;
                }
                // Closed valves/disconnected tanks remain physical stock. They
                // cannot supply any diet without an active topology/outlet.
                foreach (Building tank in tanks.Where(t => !consumed.Contains(t.thingIDNumber)))
                {
                    FoodTankStateV2 saved = FoodNetworkV2GameComponent.Instance?.GetTankState(tank, false);
                    double nutrition;
                    if (saved?.legacyMigrated == true)
                    {
                        saved.AgeTo(ColonyTick, FoodNetworkV2GameComponent.TemperatureFor(tank), FoodNetworkV2GameComponent.CoolingActive(tank));
                        nutrition = saved.PreciseStoredNutrition;
                    }
                    else nutrition = ClassicNutrition(tank.TryGetComp<FoodNetStorage_ThingComp>());
                    if (nutrition > 0d) yield return new FoodReserveSupply { Key = "liquid:isolated:" + tank.thingIDNumber,
                        Liquid = true, Nutrition = nutrition, UsableNutrition = 0d, Source = tank, CanServe = p => false };
                }
                yield break;
            }

            var seen = new HashSet<FoodNet>();
            foreach (Building tank in tanks)
            {
                FoodNetStorage_ThingComp store = tank.TryGetComp<FoodNetStorage_ThingComp>();
                FoodNet network = store.FoodNet;
                if (network == null || !store.TransmitsFoodNow)
                {
                    double physical = ClassicNutrition(store);
                    if (physical > 0d) yield return new FoodReserveSupply { Key = "liquid:classic-isolated:" + tank.thingIDNumber,
                        Source = tank, Nutrition = physical, UsableNutrition = 0d, Liquid = true, CanServe = p => false };
                    continue;
                }
                if (!seen.Add(network)) continue;
                var stores = network.foodNetStorages.Where(s => s.parent.Spawned && s.parent.Map == map && s.TransmitsFoodNow).ToArray();
                double nutrition = stores.Sum(ClassicNutrition);
                if (nutrition <= 0d) continue;
                var ingredients = stores.SelectMany(s => s.ReadPreservation().Ingredients ?? new List<ThingDef>()).Where(d => d != null).Distinct().ToArray();
                var outlets = network.foodTransmitters.Select(c => c.parent).OfType<Building>().Where(IsOutlet).ToArray();
                var amounts = stores.Where(s => ClassicNutrition(s) > 0d).OrderBy(s => s.SourceCreatedTick)
                    .ThenBy(s => s.parent.thingIDNumber).Select(s => new FoodAmount(s.PreciseNutrition, s.PreciseVolume)).ToArray();
                foreach (FoodReserveSupply pool in CreatePools("liquid:classic:" + map.uniqueID + ":" + stores.Min(s => s.parent.thingIDNumber),
                    nutrition, ingredients, outlets, network.foodTransmitters.Select(c => c.parent), false,
                    residents, amounts, failures)) yield return pool;
            }
        }

        private static int ColonyTick => Find.TickManager?.TicksGame ?? 0;
        private sealed class OutletCoverage
        {
            internal Building Outlet;
            internal Dictionary<Pawn, double> Capacity = new Dictionary<Pawn, double>();
        }

        private static string OutletReason(Building outlet, Pawn pawn,
            Dictionary<Building, Dictionary<Pawn, string>> failures)
        {
            Dictionary<Pawn, string> byPawn;
            string reason;
            if (!IsOperational(outlet)) reason = "not powered or switched on";
            else if (!failures.TryGetValue(outlet, out byPawn) || !byPawn.TryGetValue(pawn, out reason))
                reason = "no connected usable liquid";
            return string.IsNullOrEmpty(reason) ? null : outlet.LabelShortCap + ": " + reason;
        }

        private static IEnumerable<FoodReserveSupply> CreatePools(string key, double nutrition, ThingDef[] ingredients,
            Building[] outlets, IEnumerable<Thing> nodeSource, bool v2, IReadOnlyList<Pawn> residents,
            IReadOnlyList<FoodAmount> amounts, Dictionary<Building, Dictionary<Pawn, string>> failures)
        {
            var nodes = nodeSource.ToArray();
            var cache = new Dictionary<string, double>();
            Func<FaucetPortion, double> capacity = portion =>
            {
                if (portion?.FoodDef == null || !portion.Unlocked) return 0d;
                string id = portion.Mode + ":" + portion.Density;
                double result;
                if (!cache.TryGetValue(id, out result))
                {
                    result = LiquidReserveAllocation.PrefixCapacity(amounts,
                        new FoodAmount(portion.PreciseNutrition, portion.Volume));
                    cache[id] = result;
                }
                return result;
            };
            var coverage = new List<OutletCoverage>();
            foreach (var outlet in outlets)
            {
                var route = new OutletCoverage { Outlet = outlet };
                coverage.Add(route);
                var reasons = new Dictionary<Pawn, string>();
                failures[outlet] = reasons;
                var faucet = outlet as Building_FoodFaucet;
                int grade = faucet != null && IsOperational(faucet)
                    ? FaucetMealAcquisition.ResolveSupplyGrade(faucet, p => capacity(p) > 0d) : 1;
                foreach (var pawn in residents)
                {
                    double available = 0d;
                    string reason;
                    if (!IsOperational(outlet)) reason = "not powered or switched on";
                    else if (faucet != null)
                    {
                        var collectors = ColonyFoodReserve.FoodCollectors(pawn, faucet, true).ToArray();
                        if (collectors.Length == 0) reason = "no permitted collector can reach it and this resident";
                        else
                        {
                            var portions = FaucetMealAcquisition.ReservePortions(faucet, pawn, grade)
                                .Where(p => p?.FoodDef != null && p.Unlocked &&
                                    collectors.Any(getter => LiquidDietAllows(pawn, p.FoodDef, ingredients, getter))).ToArray();
                            available = portions.Select(capacity).DefaultIfEmpty(0d).Max();
                            reason = available > 0d ? null : portions.Length == 0
                                ? "food restriction or ingredients exclude its meals"
                                : "not enough compatible liquid for a complete serving";
                        }
                    }
                    else
                    {
                        available = CanUseAutoFeeder(outlet as Building_AutoFeeder, pawn, ingredients, v2) ? nutrition : 0d;
                        reason = available > 0d ? null : "no permitted automatic feed to this resident in bed";
                    }
                    route.Capacity[pawn] = available;
                    reasons[pawn] = reason;
                }
            }
            // Split one physical pool at route-specific ceilings. Each unit is
            // counted once; a coarse or inaccessible outlet cannot remove another
            // recipient's Normal service or lend its remainder to that recipient.
            int index = 0;
            foreach (var band in LiquidReserveAllocation.Bands(nutrition, coverage.SelectMany(c => c.Capacity.Values)))
            {
                double upper = band.UpperBound;
                yield return new FoodReserveSupply { Key = key + ":band:" + index++, Liquid = true, Nutrition = band.Nutrition,
                    SourceIds = nodes.Where(n => !IsOutlet(n as Building) || coverage.Any(c => c.Outlet == n &&
                        c.Capacity.Values.Any(q => q >= upper))).Select(n => n.thingIDNumber).ToArray(),
                    CanServe = pawn => coverage.Any(c => c.Capacity.TryGetValue(pawn, out double q) && q >= upper) };
            }
        }
        private static bool IsOutlet(Building building) => building is Building_FoodFaucet || building is Building_AutoFeeder;
        private static bool IsOperational(Building building) => FoodNetworkV2MachineUtility.IsOperational(building) &&
            building.TryGetComp<FoodTransmitter_ThingComp>()?.TransmitsFoodNow == true;

        private static double ClassicNutrition(FoodNetStorage_ThingComp storage)
        {
            if (storage == null || storage.ReadPreservation().RemainingRotTicks <= 0f || storage.Stored <= 0f) return 0d;
            float ratio = storage.FullnessToNutritionRatio;
            return ratio > 0f && !float.IsInfinity(ratio) && !float.IsNaN(ratio) ? storage.PreciseNutrition : double.NaN;
        }

        private static bool CanUseAutoFeeder(Building_AutoFeeder feeder, Pawn pawn, ThingDef[] ingredients, bool v2)
        {
            if (feeder == null || !IsOperational(feeder) || pawn == null || pawn.Dead ||
                feeder.IsForbidden(pawn) || FoodNetworkV2AutoFeederUtility.GetMode(feeder) == AutoFeederMode.off) return false;
            bool linked = v2 ? FoodNetworkV2AutoFeederUtility.LinkedPawns(feeder).Contains(pawn) : feeder.CurrentPawn == pawn;
            if (!linked || !pawn.InBed()) return false;
            if (v2 && !FoodNetworkV2AutoFeederUtility.TryGetLinkedBed(feeder, pawn, out Building_Bed bed)) return false;
            return LiquidDietAllows(pawn, RimRound.FeedingTube.Defs.ThingDefOf.RR_FeedingTubeFluid, ingredients, null);
        }

        // Evaluate native definition/policy and known ingredient filters directly;
        // unknown content filters conservatively refuse liquid. No fake Thing is made.
        public static bool LiquidDietAllows(Pawn pawn, ThingDef food, IEnumerable<ThingDef> ingredientSource, Pawn getter)
        {
            if (!OrdinaryFoodIntake.IsProcessableFoodDef(food) || !SpecialistContentPolicy.Allows(food) || !FoodUtility.WillEat(pawn, food, getter, true, false)) return false;
            var ingredients = ingredientSource?.Where(d => d != null).Distinct().ToArray() ?? new ThingDef[0];
            if (ingredients.Any(d => !OrdinaryFoodIntake.IsProcessableFoodDef(d) || !SpecialistContentPolicy.Allows(d) || d.IsDrug || d.defName == "fatpill" ||
                d.ingestible?.outcomeDoers?.Any(o => o.GetType().Name.IndexOf("Hemogen", StringComparison.OrdinalIgnoreCase) >= 0) == true ||
                FoodUtility.IsVeneratedAnimalMeatOrCorpse(d, pawn))) return false;
            FoodPolicy restriction = pawn.foodRestriction?.GetCurrentRespectedRestriction(getter);
            if (restriction == null || pawn.DevelopmentalStage.Baby() || pawn.IsSubhuman) return true;
            FoodKind kind = ingredients.Length == 0 ? FoodUtility.GetFoodKind(food) :
                ingredients.Any(d => FoodUtility.GetFoodKind(d) == FoodKind.Meat) ? FoodKind.Meat :
                ingredients.Any(d => d.IsAnimalProduct) ? FoodKind.Any : FoodKind.NonMeat;
            bool human = ingredients.Any(d => FoodUtility.GetMeatSourceCategory(d) == MeatSourceCategory.Humanlike);
            bool insect = ingredients.Any(d => FoodUtility.GetMeatSourceCategory(d) == MeatSourceCategory.Insect);
            bool vegetarian = food.IsProcessedFood || (kind != FoodKind.Meat && FoodUtility.GetFoodKind(food) != FoodKind.Meat);
            bool carnivore = food.IsProcessedFood || (kind != FoodKind.NonMeat && FoodUtility.GetFoodKind(food) != FoodKind.NonMeat) || food.IsDrug;
            foreach (SpecialThingFilterDef filter in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading)
            {
                if (restriction.filter.Allows(filter) || !food.IsWithinCategory(filter.parentCategory)) continue;
                string worker = filter.Worker.GetType().Name;
                bool matches;
                switch (worker)
                {
                    case "SpecialThingFilterWorker_Cannibal": matches = human; break;
                    case "SpecialThingFilterWorker_InsectMeat": matches = insect; break;
                    case "SpecialThingFilterWorker_Vegetarian": matches = vegetarian && !carnivore; break;
                    case "SpecialThingFilterWorker_Carnivore": matches = !vegetarian && carnivore && !human && !insect; break;
                    case "SpecialThingFilterWorker_Rotten": matches = false; break; // all authority batches were aged and expired contents removed
                    case "SpecialThingFilterWorker_Fresh": matches = true; break;
                    case "SpecialThingFilterWorker_PlantFood": matches = (food.ingestible.foodType & FoodTypeFlags.Plant) != 0; break;
                    default: if (filter.Worker.CanEverMatch(food)) return false; continue;
                }
                if (matches) return false;
            }
            return true;
        }
    }

    // Mutation invalidation is intentionally metadata-only. Native feeding and
    // survival transactions continue independently of the optional reserve guard.
    [HarmonyPatch(typeof(FoodNetworkV2), nameof(FoodNetworkV2.TryStore))]
    internal static class FoodReserveV2StoreChanged
    {
        public static void Postfix(FoodNetworkV2 __instance, bool __result)
        { if (__result) ColonyFoodReserve.NotifyFoodChanged(__instance.Nodes.FirstOrDefault()?.Map); }
    }
    [HarmonyPatch(typeof(FoodNetworkV2), nameof(FoodNetworkV2.TryDraw),
        new Type[] { typeof(float), typeof(float), typeof(bool), typeof(FoodBatchV2) },
        new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
    internal static class FoodReserveV2DrawChanged
    {
        public static void Postfix(FoodNetworkV2 __instance, bool __result)
        { if (__result) ColonyFoodReserve.NotifyFoodChanged(__instance.Nodes.FirstOrDefault()?.Map); }
    }
    [HarmonyPatch(typeof(FoodNetworkV2MapComponent), nameof(FoodNetworkV2MapComponent.MarkDirty))]
    internal static class FoodReserveV2TopologyChanged
    {
        public static void Postfix(Map ___map) { ColonyFoodReserve.NotifyFoodChanged(___map); }
    }
    [HarmonyPatch(typeof(FoodNetStorage_ThingComp), nameof(FoodNetStorage_ThingComp.Stored), MethodType.Setter)]
    internal static class FoodReserveClassicStorageChanged
    {
        public static void Postfix(FoodNetStorage_ThingComp __instance) { ColonyFoodReserve.NotifyFoodChanged(__instance.parent.Map); }
    }
}
