using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Simulation;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.Utilities
{
    public sealed class FoodReserveSupply
    {
        public string Key;
        public double Nutrition;
        // Physical stock may contain an undispensable fractional meal.
        public double? UsableNutrition;
        public bool Liquid;
        public bool BreastMilk;
        public double DailyProduction, DailyProductionCost;
        public double ProductionLimit = double.MaxValue;
        public double ExpiryDays = double.MaxValue;
        public Pawn ProductionConsumer;
        public Thing Source;
        public int[] SourceIds;
        public Func<Pawn, bool> CanServe;
        public Func<Pawn, string> RejectionReason;
        public Func<Pawn, double> NutritionFactor;
    }

    public sealed class FoodReserveSnapshot
    {
        public double SolidNutrition, LiquidNutrition, UsableNutrition, DailyDemandNutrition, DaysRemaining;
        public double AnimalDailyDemandNutrition, HumanDailyDemandNutrition, ReservedNutrition;
        public double BreastMilkNutrition, BreastMilkPerDay, MilkProductionFoodPerDay;
        public bool OptionalPaused, Valid;
        public string Reason, Diagnostic;
        public int Tick, ResidentCount, AnimalCount, SupplyCount, DietGroupCount;
        internal FoodReserveSupply[] Supplies;
        internal double[] Amounts, Demand, DailyProduction, ProductionLimits, ExpiryDays;
        internal bool[][] Accepts;
        internal Pawn[] Eaters;
        internal bool[][] PawnAccepts;
    }

    /// <summary>Map-local physical stock, dietary coverage and short-lived commit reservations. All calls are main-thread.</summary>
    public static class ColonyFoodReserve
    {
        public const int CacheTicks = 250;
        private static readonly Dictionary<string, Func<Map, IReadOnlyList<Pawn>, IEnumerable<FoodReserveSupply>>> providers =
            new Dictionary<string, Func<Map, IReadOnlyList<Pawn>, IEnumerable<FoodReserveSupply>>>();
        [ThreadStatic] private static bool building;
        internal static int Tick => Find.TickManager?.TicksGame ?? 0;

        public static void RegisterLiquidProvider(string id, Func<Map, IReadOnlyList<Pawn>, IEnumerable<FoodReserveSupply>> provider)
        {
            if (string.IsNullOrEmpty(id) || provider == null) throw new ArgumentException("A reserve provider requires an identity and delegate.");
            providers[id] = provider;
            if (Find.Maps != null) foreach (Map map in Find.Maps) NotifyFoodChanged(map);
        }
        public static void NotifyFoodChanged(Map map)
        { var state = map?.GetComponent<ColonyFoodReserveMapComponent>(); if (state != null) state.Snapshot = null; }

        // Growing plants and corpses are deliberately excluded by AddSolid.
        // Their frequent spawn/despawn events must not rebuild every resident's
        // food-access matrix. Keep item and pawn changes conservatively live.
        internal static bool SpawnChangesReserve(Thing thing) => thing is Pawn ||
            (thing != null && !(thing is Corpse) && thing.def.category == ThingCategory.Item &&
                thing.def.IsNutritionGivingIngestible);

        /// <summary>Actual whole-item stock debit, independent of the eater's raw-food energy conversion. Invalid inputs return NaN.</summary>
        public static double StockCost(Thing food, int count)
        {
            if (food == null || food.Destroyed || count < 0 || count > food.stackCount) return double.NaN;
            if (count == 0) return 0d;
            var precise = OrdinaryFoodIntake.PreciseVolume(food);
            double perItem = precise == null ? food.GetStatValue(StatDefOf.Nutrition) : precise.PreciseNutritionPerItem;
            double cost = perItem * count;
            return FoodReserveAllocation.Finite(perItem) && perItem > 0d && FoodReserveAllocation.Finite(cost) && cost > 0d ? cost : double.NaN;
        }

        public static FoodReserveSnapshot GetSnapshot(Map map, bool forceRefresh = false)
        {
            if (map == null || building) return Invalid("Food reserve estimate unavailable");
            var state = map.GetComponent<ColonyFoodReserveMapComponent>();
            if (state == null) return Invalid("Food reserve state unavailable");
            state.ExpireLeases();
            if (!forceRefresh && state.Snapshot != null && Tick >= state.Snapshot.Tick && Tick - state.Snapshot.Tick < CacheTicks)
                return state.Snapshot;
            building = true;
            try
            {
                var snapshot = Build(map, state);
                bool paused = state.Paused;
                FoodReserveAllocation.Allows(snapshot.DaysRemaining, snapshot.DaysRemaining, ref paused);
                state.Paused = paused || !snapshot.Valid;
                snapshot.OptionalPaused = state.Paused;
                snapshot.Reason = !snapshot.Valid ? "Food reserve estimate unavailable" : state.Paused
                    ? "Optional feeding paused: " + snapshot.DaysRemaining.ToString("F1") + " days of food; keeping 3 days (resume at 3.5)"
                    : "Food reserve: " + snapshot.DaysRemaining.ToString("F1") + " days; keeping 3 days";
                return state.Estimate = state.Snapshot = snapshot;
            }
            catch (Exception exception)
            {
                state.Paused = true;
                var failed = Invalid("Food reserve estimate unavailable");
                failed.Diagnostic = exception.GetType().Name + ": " + exception.Message;
                return state.Estimate = state.Snapshot = failed;
            }
            finally { building = false; }
        }

        // AI planning and alerts tolerate a short-lived estimate. Stock changes
        // still invalidate GetSnapshot immediately, and TryReserveOptional always
        // rebuilds before committing food. Keep frequent hauling/processor updates
        // from recomputing the entire colony access matrix for each preview.
        internal static FoodReserveSnapshot GetEstimate(Map map)
        {
            if (map == null || building) return Invalid("Food reserve estimate unavailable");
            var estimate = map?.GetComponent<ColonyFoodReserveMapComponent>()?.Estimate;
            if (estimate != null && Tick >= estimate.Tick && Tick - estimate.Tick < CacheTicks)
                return estimate;
            return GetSnapshot(map);
        }

        public static bool CanSpendOptional(Pawn pawn, double nutrition, Thing source = null)
        {
            // Quoting any real debit participates in meal assembly. Refresh
            // invalidated stock so new/split food or a changed diet is not judged
            // against obsolete pools. Only zero-cost AI priority uses an estimate.
            var snapshot = source != null || nutrition > 0d
                ? GetSnapshot(pawn?.MapHeld) : GetEstimate(pawn?.MapHeld);
            return CanSpend(pawn, nutrition, source, snapshot);
        }

        public static bool TryReserveOptional(Pawn pawn, double nutrition, Thing source, out IDisposable lease)
        {
            lease = null;
            Map map = pawn?.MapHeld;
            if (!CanSpend(pawn, nutrition, source, GetSnapshot(map, true))) return false;
            var state = map.GetComponent<ColonyFoodReserveMapComponent>();
            var reserved = new FoodReserveLease(state, pawn, nutrition, source, Tick);
            state.Leases.Add(reserved); state.Snapshot = null; lease = reserved;
            return true;
        }

        /// <summary>Project a carry quota move without treating conserved food as eaten.</summary>
        public static bool CanMoveToInventory(Pawn holder, Thing food, int count)
        {
            if (building || holder?.Spawned != true || holder.inventory == null ||
                !IsResident(holder) || holder.Dead || food?.Spawned != true ||
                holder.Map != food.Map || count <= 0 || count > food.stackCount || !OrdinaryStock(food)) return false;
            var before = GetSnapshot(holder.Map, true);
            if (!before.Valid) return false;
            var state = holder.Map.GetComponent<ColonyFoodReserveMapComponent>();
            building = true;
            try
            {
                var after = Build(holder.Map, state, food, holder, count);
                bool paused = before.OptionalPaused;
                return after.Valid && FoodReserveAllocation.Allows(before.DaysRemaining, after.DaysRemaining, ref paused);
            }
            catch (Exception error)
            {
                Log.ErrorOnce("Food reserve inventory projection failed: " + error, 619422);
                return false;
            }
            finally { building = false; }
        }

        // A permitted conversion consumes specific ingredients even when its
        // eater cannot eat those ingredients raw. Protect every resident's diet
        // against the exact input debits before the native conversion happens.
        internal static bool TryReserveIngredients(Pawn pawn, FoodReserveIngredient[] ingredients, out IDisposable lease)
        {
            lease = null;
            Map map = pawn?.MapHeld;
            var snapshot = GetSnapshot(map, true);
            if (pawn == null || !snapshot.Valid || snapshot.OptionalPaused ||
                Array.IndexOf(snapshot.Eaters, pawn) < 0 || ingredients == null || ingredients.Length == 0) return false;
            double[] projected = (double[])snapshot.Amounts.Clone();
            if (!DebitIngredients(snapshot, projected, ingredients) ||
                !FoodReserveAllocation.Covers(projected, snapshot.Demand, snapshot.Accepts,
                    FoodReserveAllocation.ProtectedDays, snapshot.DailyProduction, snapshot.ProductionLimits, snapshot.ExpiryDays)) return false;
            var state = map.GetComponent<ColonyFoodReserveMapComponent>();
            var reserved = new FoodReserveLease(state, pawn, ingredients.Sum(i => i.Nutrition), null, Tick)
                { Ingredients = ingredients };
            state.Leases.Add(reserved); state.Snapshot = null; lease = reserved;
            return true;
        }

        private static bool DebitIngredients(FoodReserveSnapshot snapshot, double[] amounts, FoodReserveIngredient[] ingredients)
        {
            foreach (var ingredient in ingredients)
            {
                if (ingredient?.Source == null || !FoodReserveAllocation.Finite(ingredient.Nutrition) || ingredient.Nutrition <= 0d) return false;
                bool[] matches = snapshot.Supplies.Select(s => s.Source == ingredient.Source ||
                    (s.SourceIds != null && Array.IndexOf(s.SourceIds, ingredient.Source.thingIDNumber) >= 0)).ToArray();
                if (!FoodReserveAllocation.TryDebit(amounts, matches, matches, ingredient.Nutrition, true)) return false;
            }
            return true;
        }

        private static bool CanSpend(Pawn pawn, double nutrition, Thing source, FoodReserveSnapshot snapshot)
        {
            if (pawn == null || !snapshot.Valid || snapshot.OptionalPaused || !FoodReserveAllocation.Finite(nutrition) || nutrition < 0d)
                return false;
            if (nutrition == 0d) return true;
            double[] projected = (double[])snapshot.Amounts.Clone();
            if (!Debit(snapshot, projected, pawn, nutrition, source)) return false;
            return FoodReserveAllocation.Covers(projected, snapshot.Demand, snapshot.Accepts, FoodReserveAllocation.ProtectedDays, snapshot.DailyProduction, snapshot.ProductionLimits, snapshot.ExpiryDays);
        }

        private static bool Debit(FoodReserveSnapshot snapshot, double[] amounts, Pawn pawn, double cost, Thing source)
        {
            int eater = Array.IndexOf(snapshot.Eaters, pawn);
            if (eater < 0) return false;
            var matches = new bool[amounts.Length];
            var compatible = new bool[amounts.Length];
            for (int i = 0; i < amounts.Length; i++)
            {
                var pool = snapshot.Supplies[i];
                matches[i] = source != null && (pool.Source == source || (pool.SourceIds != null && Array.IndexOf(pool.SourceIds, source.thingIDNumber) >= 0));
                compatible[i] = snapshot.PawnAccepts[i][eater];
            }
            return FoodReserveAllocation.TryDebit(amounts, compatible, matches, cost, matches.Any(m => m));
        }

        private static FoodReserveSnapshot Build(Map map, ColonyFoodReserveMapComponent state,
            Thing movingFood = null, Pawn destinationHolder = null, int movingCount = 0)
        {
            var residents = map.mapPawns.AllPawns.Where(p => IsResident(p) && p.MapHeld == map)
                .Distinct().OrderBy(p => p.thingIDNumber).ToArray();
            // Non-eating helpers can hold/deliver stock without becoming an
            // unfed demand group. Do not use adult body eligibility: children
            // still need their ordinary food supply protected.
            var eaters = residents.Where(p => p.needs?.food != null && BodyModelEligibility.UsesFoodMetabolism(p)).ToArray();
            var supply = new List<FoodReserveSupply>();
            var keys = new HashSet<string>();
            var nativePaste = map.listerBuildings.allBuildingsColonist.OfType<Building_NutrientPasteDispenser>().ToArray();
            foreach (Thing food in map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                if (food != movingFood) AddSolid(food, null, supply, keys, nativePaste);
                else
                {
                    AddSolid(food, null, supply, keys, nativePaste, food.stackCount - movingCount);
                    AddSolid(food, destinationHolder, supply, keys, nativePaste, movingCount, ":projected-holder");
                }
            }
            foreach (Pawn holder in residents)
            {
                if (holder.inventory != null) foreach (Thing food in holder.inventory.innerContainer) AddSolid(food, holder, supply, keys, nativePaste);
                Thing carried = holder.carryTracker?.CarriedThing;
                if (carried != null) AddSolid(carried, holder, supply, keys, nativePaste);
            }
            foreach (var provider in providers.OrderBy(p => p.Key))
                foreach (var item in provider.Value(map, eaters) ?? Enumerable.Empty<FoodReserveSupply>())
                    if (item != null && keys.Add(item.Key ?? "")) supply.Add(item);
            // Optional feeding reserves cover humanlike children and adults.
            // Babies and animals retain native feeding, but their separate
            // diets/caregiver availability must not pause the colony's meals.
            // Do not add hypothetical nursing supply or its extra production cost.
            var snapshot = new FoodReserveSnapshot { Tick = Tick, Supplies = supply.ToArray(), Eaters = eaters,
                Amounts = supply.Select(s => s.UsableNutrition ?? s.Nutrition).ToArray(),
                DailyProduction = supply.Select(s => s.DailyProduction).ToArray(),
                ProductionLimits = supply.Select(s => s.ProductionLimit).ToArray(),
                ExpiryDays = supply.Select(s => s.ExpiryDays).ToArray(), PawnAccepts = new bool[supply.Count][] };
            var demand = new double[eaters.Length];
            for (int j = 0; j < eaters.Length; j++)
            {
                demand[j] = eaters[j].needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed, true) * 60000d;
                if (!FoodReserveAllocation.Finite(demand[j]) || demand[j] < 0d) throw new InvalidOperationException("Invalid resident food demand");
            }
            for (int i = 0; i < supply.Count; i++)
            {
                var item = supply[i];
                if (!FoodReserveAllocation.Finite(item.Nutrition) || item.Nutrition < 0d) throw new InvalidOperationException("Invalid physical stock");
                if (!FoodReserveAllocation.Finite(snapshot.Amounts[i]) || snapshot.Amounts[i] < 0d || snapshot.Amounts[i] > item.Nutrition)
                    throw new InvalidOperationException("Invalid usable stock");
                if (!FoodReserveAllocation.Finite(item.DailyProduction) || item.DailyProduction < 0d ||
                    !FoodReserveAllocation.Finite(item.DailyProductionCost) || item.DailyProductionCost < item.DailyProduction ||
                    !FoodReserveAllocation.Finite(item.ProductionLimit) || item.ProductionLimit < 0d ||
                    !FoodReserveAllocation.Finite(item.ExpiryDays) || item.ExpiryDays < 0d)
                    throw new InvalidOperationException("Unfunded food production");
                if (item.DailyProductionCost > 0d)
                {
                    int consumer = Array.IndexOf(eaters, item.ProductionConsumer);
                    if (consumer < 0) throw new InvalidOperationException("Milk producer absent from protected population");
                    demand[consumer] += item.DailyProductionCost;
                    snapshot.MilkProductionFoodPerDay += item.DailyProductionCost;
                }
                snapshot.PawnAccepts[i] = new bool[eaters.Length];
                for (int j = 0; j < eaters.Length; j++) snapshot.PawnAccepts[i][j] = item.CanServe != null && item.CanServe(eaters[j]);
                if (item.BreastMilk)
                {
                    snapshot.BreastMilkNutrition += item.Nutrition;
                    if (snapshot.PawnAccepts[i].Any(a => a)) snapshot.BreastMilkPerDay += item.DailyProduction;
                }
                else if (item.Liquid) snapshot.LiquidNutrition += item.Nutrition; else snapshot.SolidNutrition += item.Nutrition;
                if (snapshot.PawnAccepts[i].Any(a => a)) snapshot.UsableNutrition += snapshot.Amounts[i];
            }
            var groups = new Dictionary<string, int>();
            var grouped = new List<double>();
            var groupForPawn = new int[eaters.Length];
            for (int j = 0; j < eaters.Length; j++)
            {
                groupForPawn[j] = -1;
                double factor = 1d;
                for (int i = 0; i < supply.Count; i++) if (snapshot.PawnAccepts[i][j] && supply[i].NutritionFactor != null)
                    factor = Math.Min(factor, supply[i].NutritionFactor(eaters[j]));
                if (!FoodReserveAllocation.Finite(factor) || factor <= 0d) throw new InvalidOperationException("Invalid dietary nutrition conversion");
                double rawDemand = demand[j] / factor;
                string signature = new string(snapshot.PawnAccepts.Select(a => a[j] ? '1' : '0').ToArray());
                int group;
                if (!groups.TryGetValue(signature, out group)) { group = grouped.Count; groups.Add(signature, group); grouped.Add(0d); }
                grouped[group] += rawDemand; groupForPawn[j] = group;
                snapshot.ResidentCount++; snapshot.HumanDailyDemandNutrition += rawDemand;
            }
            snapshot.Demand = grouped.ToArray(); snapshot.DietGroupCount = grouped.Count; snapshot.SupplyCount = supply.Count;
            snapshot.Accepts = new bool[supply.Count][];
            for (int i = 0; i < supply.Count; i++)
            {
                snapshot.Accepts[i] = new bool[grouped.Count];
                for (int j = 0; j < eaters.Length; j++) if (groupForPawn[j] >= 0 && snapshot.PawnAccepts[i][j]) snapshot.Accepts[i][groupForPawn[j]] = true;
            }
            foreach (var lease in state.Leases)
            {
                if (!(lease.Ingredients != null ? DebitIngredients(snapshot, snapshot.Amounts, lease.Ingredients) :
                    Debit(snapshot, snapshot.Amounts, lease.Pawn, lease.Nutrition, lease.Source)))
                    throw new InvalidOperationException("An active food commitment is no longer covered");
                snapshot.ReservedNutrition += lease.Nutrition;
            }
            snapshot.UsableNutrition = snapshot.Amounts.Where((n, i) => snapshot.PawnAccepts[i].Any(a => a)).Sum();
            snapshot.DailyDemandNutrition = grouped.Sum();
            snapshot.DaysRemaining = FoodReserveAllocation.Days(snapshot.Amounts, snapshot.Demand, snapshot.Accepts, snapshot.DailyProduction, snapshot.ProductionLimits, snapshot.ExpiryDays);
            snapshot.Valid = snapshot.DailyDemandNutrition > 0d && FoodReserveAllocation.Finite(snapshot.DaysRemaining);
            snapshot.Diagnostic = snapshot.ResidentCount + " children/adults, " +
                snapshot.DietGroupCount + " diet/access groups, " + snapshot.SupplyCount + " food pools; animals and babies excluded";
            string[] missing = eaters.Where((p, j) => !p.IsAnimal && demand[j] > 0d && !snapshot.PawnAccepts.Any(a => a[j]))
                .Select(p => p.LabelShort).ToArray();
            if (missing.Length > 0) snapshot.Diagnostic += "\nNo accessible permitted food: " + string.Join(", ", missing);
            foreach (var pawn in eaters.Where((p, j) => demand[j] > 0d && !snapshot.PawnAccepts.Any(a => a[j])))
            {
                var reasons = supply.Where(s => s.RejectionReason != null)
                    .Select(s => s.RejectionReason(pawn)).Where(r => !string.IsNullOrEmpty(r)).Distinct().Take(3).ToArray();
                if (reasons.Length > 0) snapshot.Diagnostic += "\n" + pawn.LabelShort + ": " + string.Join("; ", reasons);
            }
            if (snapshot.BreastMilkNutrition > 0d || snapshot.MilkProductionFoodPerDay > 0d)
                snapshot.Diagnostic += "\nNursing milk: " + snapshot.BreastMilkNutrition.ToString("F2") + " stored, up to " +
                    snapshot.BreastMilkPerDay.ToString("F2") + "/day; maternal production food: " + snapshot.MilkProductionFoodPerDay.ToString("F2") + "/day included";
            double usableLiquid = snapshot.Amounts.Where((n, i) => supply[i].Liquid && snapshot.PawnAccepts[i].Any(a => a)).Sum();
            if (snapshot.LiquidNutrition > usableLiquid) snapshot.Diagnostic += "\n" +
                (snapshot.LiquidNutrition - usableLiquid).ToString("F1") + " liquid nutrition cannot currently serve a compatible resident (access, power, portion, policy or commitment)";
            return snapshot;
        }

        private static bool IsResident(Pawn pawn) => pawn != null && !pawn.Dead && !pawn.Suspended &&
            pawn.RaceProps.Humanlike && !pawn.IsAnimal && !InfantFoodReserve.IsInfant(pawn) &&
            (pawn.Faction == Faction.OfPlayer || pawn.IsPrisonerOfColony || pawn.HostFaction == Faction.OfPlayer);

        private static void AddSolid(Thing food, Pawn holder, List<FoodReserveSupply> supply, HashSet<string> keys,
            Building_NutrientPasteDispenser[] nativePaste, int count = -1, string keySuffix = "")
        {
            if (!OrdinaryStock(food) || count == 0 || !keys.Add("thing:" + food.thingIDNumber + keySuffix)) return;
            double perItem = StockCost(food, 1);
            Func<Pawn, bool> throughPaste = pawn => holder == null && NativePasteReserve.CanServeIngredient(pawn, food, nativePaste);
            supply.Add(new FoodReserveSupply { Key = "thing:" + food.thingIDNumber + keySuffix, Source = food,
                Nutrition = StockCost(food, count < 0 ? food.stackCount : count),
                CanServe = pawn => !food.Destroyed && food.IngestibleNow && !food.IsNotFresh() && !food.IsDessicated() &&
                    (holder != null ? (holder == pawn || CanDeliverInventory(holder, pawn)) && FoodUtility.WillEat(pawn, food, holder, true, false) : CanReachFood(pawn, food, false) || throughPaste(pawn)),
                NutritionFactor = pawn => throughPaste(pawn) ? 1d : perItem > 0d ? FoodUtility.NutritionForEater(pawn, food) / perItem : 1d });
        }

        private static bool OrdinaryStock(Thing food)
        {
            if (food == null || food.Destroyed || food is Corpse || food is Plant || food.def.category != ThingCategory.Item ||
                !food.def.IsNutritionGivingIngestible || food.def.IsDrug || food.def.defName == "fatpill") return false;
            if (food.def.ingestible?.outcomeDoers?.Any(d => d.GetType().Name.IndexOf("Hemogen", StringComparison.OrdinalIgnoreCase) >= 0) == true) return false;
            if (food.TryGetComp<CompIngredients>()?.ingredients?.Any(d => d != null && (d.IsDrug || d.defName == "fatpill" ||
                d.ingestible?.outcomeDoers?.Any(o => o.GetType().Name.IndexOf("Hemogen", StringComparison.OrdinalIgnoreCase) >= 0) == true)) == true) return false;
            return SpecialistContentPolicy.Allows(food);
        }

        private static bool CanDeliverInventory(Pawn holder, Pawn eater) => holder != null && eater != null && holder.MapHeld == eater.MapHeld &&
            (!InfantFoodReserve.IsInfant(eater) || InfantFoodReserve.CanDeliver(holder, eater)) &&
            holder.Spawned && holder.Faction == Faction.OfPlayer && !holder.Downed && !holder.Drafted && !holder.Dead && holder.RaceProps.ToolUser &&
            holder.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && CanReachCareRecipient(holder, eater);

        private static bool CanReachCareRecipient(Pawn helper, Pawn eater)
        {
            if (helper == null || eater == null || !helper.Spawned || helper.Map != eater.MapHeld) return false;
            // A carried resident has no spawned target position of their own.
            // Care access is through their actual carrier, while diet/policy
            // remains evaluated against the resident and the food getter.
            Pawn carrier = eater.CarriedBy;
            if (carrier != null) return carrier == helper || (carrier.Spawned && helper.CanReach(carrier, PathEndMode.Touch, Danger.Some));
            return eater.Spawned && helper.CanReach(eater, PathEndMode.Touch, Danger.Some);
        }

        /// <summary>Native access and diet checks, including delivery to a patient/prisoner by a capable resident helper.</summary>
        public static bool CanReachFood(Pawn eater, Thing source, bool dispenser)
        {
            if (eater == null || source == null || !source.Spawned || eater.MapHeld != source.Map) return false;
            if (InfantFoodReserve.IsInfant(eater)) return !dispenser && InfantFoodReserve.CanReceiveStock(eater, source);
            return FoodCollectors(eater, source, dispenser).Any();
        }

        // Keep the collector identity for getter-sensitive food restrictions.
        public static IEnumerable<Pawn> FoodCollectors(Pawn eater, Thing source, bool dispenser)
        {
            if (eater == null || source == null || !source.Spawned || eater.MapHeld != source.Map ||
                InfantFoodReserve.IsInfant(eater)) yield break;
            if (CanCollect(eater, eater, source, dispenser)) yield return eater;
            if (!(eater.Downed || eater.IsPrisonerOfColony || eater.InBed() || eater.CarriedBy != null)) yield break;
            foreach (Pawn helper in source.Map.mapPawns.FreeColonistsSpawned)
                if (helper != eater && !helper.Downed && !helper.Dead && !helper.Drafted &&
                    helper.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && CanReachCareRecipient(helper, eater) &&
                    CanCollect(helper, eater, source, dispenser)) yield return helper;
        }

        private static bool CanCollect(Pawn getter, Pawn eater, Thing source, bool dispenser)
        {
            if (getter.Downed || !getter.Spawned || source.IsForbidden(getter) ||
                !(source.IsSociallyProper(getter) || source.IsSociallyProper(eater, eater.IsPrisonerOfColony, !getter.IsAnimal))) return false;
            if (dispenser && (!getter.RaceProps.ToolUser || !getter.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                !source.InteractionCell.Standable(source.Map) || (source.Faction != getter.Faction && source.Faction != getter.HostFaction))) return false;
            if (!dispenser && (!FoodUtility.WillEat(eater, source, getter, true, false) || !source.IngestibleNow)) return false;
            return getter.CanReach(dispenser ? (LocalTargetInfo)source.InteractionCell : source, dispenser ? PathEndMode.OnCell : PathEndMode.ClosestTouch, Danger.Some);
        }
        private static FoodReserveSnapshot Invalid(string reason) => new FoodReserveSnapshot { Tick = Tick, OptionalPaused = true, Reason = reason, Diagnostic = reason };
    }

    public sealed class ColonyFoodReserveMapComponent : MapComponent
    {
        internal FoodReserveSnapshot Snapshot;
        internal FoodReserveSnapshot Estimate;
        internal bool Paused;
        internal readonly List<FoodReserveLease> Leases = new List<FoodReserveLease>();
        public ColonyFoodReserveMapComponent(Map map) : base(map) { }
        internal void ExpireLeases()
        {
            if (Leases.RemoveAll(l => l.Disposed || l.Tick != ColonyFoodReserve.Tick) > 0) Snapshot = null;
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref Paused, "rrOptionalFoodReservePaused", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { Estimate = Snapshot = null; Leases.Clear(); }
        }
    }
    internal sealed class FoodReserveLease : IDisposable
    {
        internal readonly Pawn Pawn; internal readonly double Nutrition; internal readonly Thing Source; internal readonly int Tick;
        internal FoodReserveIngredient[] Ingredients;
        internal bool Disposed; private readonly ColonyFoodReserveMapComponent state;
        internal FoodReserveLease(ColonyFoodReserveMapComponent state, Pawn pawn, double nutrition, Thing source, int tick)
        { this.state = state; Pawn = pawn; Nutrition = nutrition; Source = source; Tick = tick; }
        public void Dispose() { if (Disposed) return; Disposed = true; state.Leases.Remove(this); state.Snapshot = null; }
    }

    [HarmonyPatch(typeof(Alert_LowFood), nameof(Alert_LowFood.GetReport))]
    internal static class ColonyFoodReserveLowFoodReport
    {
        internal static Map LowMap() => Find.Maps?.FirstOrDefault(m => m.IsPlayerHome && m.mapPawns.AnyColonistSpawned &&
            (!ColonyFoodReserve.GetEstimate(m).Valid || ColonyFoodReserve.GetEstimate(m).OptionalPaused));
        public static bool Prefix(ref AlertReport __result)
        { __result = ColonyFoodReserve.Tick >= 150000 && LowMap() != null; return false; }
    }
    [HarmonyPatch(typeof(Alert_LowFood), nameof(Alert_LowFood.GetExplanation))]
    internal static class ColonyFoodReserveLowFoodExplanation
    {
        internal static string Explanation(FoodReserveSnapshot stock) =>
            "Food reserve estimate: " + stock.DaysRemaining.ToString("F1") + " days\n" +
            "Physical nutrition: " + stock.SolidNutrition.ToString("F1") + " food + " + stock.LiquidNutrition.ToString("F1") + " liquid\n" +
            "Usable nutrition: " + stock.UsableNutrition.ToString("F1") + "; daily demand: " + stock.DailyDemandNutrition.ToString("F2") + "\n" +
            stock.Reason + "\n" + stock.Diagnostic;
        public static bool Prefix(ref TaggedString __result)
        {
            Map map = ColonyFoodReserveLowFoodReport.LowMap();
            if (map == null) { __result = string.Empty; return false; }
            var stock = ColonyFoodReserve.GetEstimate(map);
            __result = Explanation(stock);
            return false;
        }
    }

    // Native AlertsReadout discovers leaf Alert subclasses. A low-priority
    // healthy-stock readout exposes the same estimate even when Low Food is hidden.
    public sealed class Alert_ColonyFoodReserve : Alert
    {
        public Alert_ColonyFoodReserve() { defaultPriority = AlertPriority.Medium; }
        private Map CurrentMap => Find.CurrentMap != null && Find.CurrentMap.IsPlayerHome ? Find.CurrentMap : null;
        public override AlertReport GetReport() => CurrentMap != null && ColonyFoodReserve.GetEstimate(CurrentMap).Valid &&
            (!ColonyFoodReserve.GetEstimate(CurrentMap).OptionalPaused || ColonyFoodReserve.Tick < 150000);
        public override string GetLabel() => "Food reserve: " + ColonyFoodReserve.GetEstimate(CurrentMap).DaysRemaining.ToString("F1") + " days" +
            (ColonyFoodReserve.GetEstimate(CurrentMap).OptionalPaused ? " (extra meals paused)" : "");
        public override TaggedString GetExplanation() => ColonyFoodReserveLowFoodExplanation.Explanation(ColonyFoodReserve.GetEstimate(CurrentMap));
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    internal static class ColonyFoodReserveIngestChanged
    {
        public static void Prefix(Thing __instance, out Map __state) { __state = __instance.MapHeld; }
        public static void Postfix(Map __state) { ColonyFoodReserve.NotifyFoodChanged(__state); }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    internal static class ColonyFoodReserveSpawnChanged
    {
        public static void Postfix(Thing __instance, Map map)
        { if (ColonyFoodReserve.SpawnChangesReserve(__instance)) ColonyFoodReserve.NotifyFoodChanged(map); }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    internal static class ColonyFoodReserveDespawnChanged
    {
        public static void Prefix(Thing __instance, out Map __state) { __state = __instance.Map; }
        public static void Postfix(Thing __instance, Map __state)
        { if (ColonyFoodReserve.SpawnChangesReserve(__instance)) ColonyFoodReserve.NotifyFoodChanged(__state); }
    }
}
