using RimRound.FeedingTube;
using RimRound.FeedingTube.Comps;
using RimRound.Utilities;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimRound.FeedOther
{
    internal static class FoodNetworkV2Constants
    {
        public const float Epsilon = 0.0001f;
        public const float DispenserMealNutrition = 0.90f;
        public const int MaximumDispenserMealsPerTrip = 4;
        public const string PipeDefName = "RR_TD_FeedingTubeConduit";
        public const string ValveDefName = "RR_TD_FeedingTubeValve";
        public const string SmallTankDefName = "RR_TD_FoodStorageVat_Small";
        public const string LargeTankDefName = "RR_TD_FoodStorageVat_Large";
        public const string MassiveTankDefName = "RR_TD_FoodStorageVat_Massive";
        public const string ProcessorDefName = "RR_TD_FeedingTubeFoodProcessor";
        public const string FaucetDefName = "RR_TD_FoodFaucet";
        public const string BasicFeederDefName = "RR_AutoFeeder";
        public const string AdvancedFeederDefName = "RR_AdvancedAutoFeeder";
        public const string DistillerDefName = "RR_TD_NutrientDistillery";

        public static bool PositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        // Inputs and classic migration can originate as float quantities even
        // though v2 stores doubles. Match classic's bounded rounding allowance
        // for a complete portion; every output still uses the actual draw.
        public static bool MeetsExactNutrition(double actual, float requested)
        {
            return PositiveFinite(requested) && actual > 0d &&
                actual <= requested && requested - actual <= requested * 0.0005d;
        }

        // A few double ULPs permit returning a just-drawn portion to a full
        // tank. This is vastly smaller than the former float-volume epsilon;
        // the complete batch is retained, never truncated to fit.
        public static double CapacityTolerance(double capacity)
        {
            return Math.Abs(capacity) * 2.2204460492503131e-16d * 8d;
        }

        public static bool IsStorage(Thing thing)
        {
            return thing != null && thing.TryGetComp<FoodNetStorage_ThingComp>() != null;
        }

        public static bool IsDistiller(Thing thing)
        {
            return thing != null && thing.def != null &&
                thing.def.defName == DistillerDefName;
        }

        public static bool IsFoodNetworkThing(Thing thing)
        {
            return thing != null &&
                thing.TryGetComp<FoodTransmitter_ThingComp>() != null;
        }
    }

    /// <summary>
    /// One conserved FIFO portion of liquid food. Nutrition is the useful food
    /// energy; fullness is the physical stomach volume. Their ratio is never
    /// inferred from a later transfer, so distillation and mixed tanks cannot
    /// create or erase food.
    /// </summary>
    public sealed class FoodBatchV2 : IExposable
    {
        public float nutrition;
        public float fullness;
        public int createdTick;
        public List<ThingDef> ingredients = new List<ThingDef>();
        private double nutritionRemainder;
        private double fullnessRemainder;
        private float nutritionProjection;
        private float fullnessProjection;
        public float remainingRotTicks = FoodPreservationStamp.VatLifeTicks;
        public bool preservationInitialized;
        public bool legacyGraceAssumed;
        public float poisonFraction;
        public FoodPoisonCause poisonCause;

        public FoodPreservationStamp Preservation
        {
            get
            {
                EnsurePreservation();
                return new FoodPreservationStamp
                {
                    RemainingRotTicks = remainingRotTicks,
                    PoisonFraction = poisonFraction,
                    PoisonCause = poisonCause,
                    AssumedFreshLegacy = legacyGraceAssumed,
                    Ingredients = ingredients == null
                        ? new List<ThingDef>()
                        : new List<ThingDef>(ingredients)
                };
            }
        }

        public void SetPreservation(FoodPreservationStamp stamp)
        {
            if (!stamp.Valid)
                return;
            stamp = stamp.WithoutPoison;
            remainingRotTicks = stamp.RemainingRotTicks;
            poisonFraction = stamp.PoisonFraction;
            poisonCause = stamp.PoisonCause;
            legacyGraceAssumed = stamp.AssumedFreshLegacy;
            if (stamp.Ingredients != null)
            {
                ingredients = ingredients ?? new List<ThingDef>();
                foreach (ThingDef ingredient in stamp.Ingredients)
                    if (ingredient != null && !ingredients.Contains(ingredient))
                        ingredients.Add(ingredient);
            }
            preservationInitialized = true;
        }

        public void EnsurePreservation()
        {
            poisonFraction = 0f;
            poisonCause = FoodPoisonCause.Unknown;
            if (preservationInitialized)
                return;
            SetPreservation(FoodPreservationStamp.LegacyGrace);
        }

        // Retain the existing public float fields and saved keys. The residual
        // preserves small transactions against large dense stores. A caller
        // replacing a public field invalidates that field's old residual.
        public double PreciseNutrition => (double)nutrition +
            (nutrition == nutritionProjection ? nutritionRemainder : 0d);
        public double PreciseFullness => (double)fullness +
            (fullness == fullnessProjection ? fullnessRemainder : 0d);

        internal void SetAmounts(double newNutrition, double newFullness)
        {
            nutrition = (float)newNutrition;
            fullness = (float)newFullness;
            nutritionProjection = nutrition;
            fullnessProjection = fullness;
            nutritionRemainder = newNutrition - nutrition;
            fullnessRemainder = newFullness - fullness;
        }

        public FoodBatchV2()
        {
        }

        public FoodBatchV2(
            float nutrition,
            float fullness,
            IEnumerable<ThingDef> ingredients,
            int createdTick)
        {
            SetAmounts(FoodNetworkV2Constants.PositiveFinite(nutrition) ? nutrition : 0f,
                FoodNetworkV2Constants.PositiveFinite(fullness) ? fullness : 0f);
            this.createdTick = createdTick;
            SetPreservation(FoodPreservationStamp.Fresh);
            if (ingredients != null)
            {
                this.ingredients = ingredients
                    .Where(delegate(ThingDef ingredient) { return ingredient != null; })
                    .Distinct()
                    .ToList();
            }
        }

        public float FullnessToNutritionRatio
        {
            get
            {
                return !Empty
                    ? (float)(PreciseFullness / PreciseNutrition)
                    : 1f;
            }
        }

        public bool Empty
        {
            get
            {
                // Volume is not a nutrition tolerance: concentrated food can
                // contain meaningful energy in a very small positive volume.
                return !FoodNetworkV2Constants.PositiveFinite(nutrition) ||
                    !FoodNetworkV2Constants.PositiveFinite(fullness) ||
                    PreciseNutrition <= 0d || PreciseFullness <= 0d ||
                    !FoodNetworkV2Constants.PositiveFinite((float)(PreciseFullness / PreciseNutrition));
            }
        }

        public FoodBatchV2 Copy()
        {
            return PortionPrecise(PreciseNutrition, PreciseFullness);
        }

        public FoodBatchV2 Portion(float portionNutrition, float portionFullness)
        {
            return PortionPrecise(portionNutrition, portionFullness);
        }

        internal FoodBatchV2 PortionPrecise(double portionNutrition, double portionFullness)
        {
            var result = new FoodBatchV2(0f, 0f, ingredients, createdTick);
            result.SetAmounts(portionNutrition, portionFullness);
            result.SetPreservation(Preservation);
            return result;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Discard stale residuals if another caller assigned a float.
                SetAmounts(PreciseNutrition, PreciseFullness);
            }
            Scribe_Values.Look(ref nutrition, "nutrition", 0f);
            Scribe_Values.Look(ref fullness, "fullness", 0f);
            Scribe_Values.Look(ref nutritionRemainder, "nutritionRemainder", 0d);
            Scribe_Values.Look(ref fullnessRemainder, "fullnessRemainder", 0d);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref remainingRotTicks,
                "remainingRotTicks", 0f);
            Scribe_Values.Look(ref preservationInitialized,
                "preservationInitialized", false);
            Scribe_Values.Look(ref legacyGraceAssumed,
                "legacyGraceAssumed", false);
            Scribe_Values.Look(ref poisonFraction,
                "poisonFraction", 0f);
            Scribe_Values.Look(ref poisonCause,
                "poisonCause", FoodPoisonCause.Unknown);
            Scribe_Collections.Look(
                ref ingredients,
                "ingredients",
                LookMode.Def);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!preservationInitialized)
                    SetPreservation(FoodPreservationStamp.LegacyGrace);
                else if (!Preservation.Valid)
                    SetPreservation(new FoodPreservationStamp
                    {
                        RemainingRotTicks = 0f
                    });
                if (ingredients == null)
                {
                    ingredients = new List<ThingDef>();
                }
                else
                {
                    ingredients.RemoveAll(delegate(ThingDef ingredient)
                    {
                        return ingredient == null;
                    });
                }

                nutrition = FoodNetworkV2Constants.PositiveFinite(nutrition) ? nutrition : 0f;
                fullness = FoodNetworkV2Constants.PositiveFinite(fullness) ? fullness : 0f;
                nutritionProjection = nutrition;
                fullnessProjection = fullness;
                if (double.IsNaN(nutritionRemainder) || double.IsInfinity(nutritionRemainder) ||
                    Math.Abs(nutritionRemainder) > Math.Abs((double)nutrition) * 0.0000001d)
                    nutritionRemainder = 0d;
                if (double.IsNaN(fullnessRemainder) || double.IsInfinity(fullnessRemainder) ||
                    Math.Abs(fullnessRemainder) > Math.Abs((double)fullness) * 0.0000001d)
                    fullnessRemainder = 0d;
            }
        }
    }

    public sealed class FoodTankStateV2 : IExposable
    {
        public int thingId;
        public bool legacyMigrated;
        public List<FoodBatchV2> batches = new List<FoodBatchV2>();
        public int preservationLastTick;
        public double spoiledNutrition;
        public double spoiledFullness;
        public double purgedNutrition;
        public double purgedFullness;
        public double classicSpoiledNutritionSeen;
        public double classicSpoiledFullnessSeen;
        public double classicPurgedNutritionSeen;
        public double classicPurgedFullnessSeen;
        public Building runtimeTank;

        public FoodTankStateV2()
        {
        }

        public FoodTankStateV2(int thingId)
        {
            this.thingId = thingId;
            preservationLastTick = Find.TickManager?.TicksGame ?? 0;
        }

        public void AgeTo(int tick, float temperature, bool cooling)
        {
            EnsureCollections();
            if (preservationLastTick <= 0 || tick <= preservationLastTick)
            {
                preservationLastTick = tick;
                return;
            }
            int elapsed = tick - preservationLastTick;
            preservationLastTick = tick;
            float rate = cooling ? 0f :
                GenTemperature.RotRateAtTemperature(temperature);
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate < 0f)
                rate = 0f;
            float effective = elapsed * rate;
            foreach (FoodBatchV2 batch in batches)
            {
                if (batch == null || batch.Empty)
                    continue;
                FoodPreservationStamp stamp = batch.Preservation.Aged(effective);
                batch.SetPreservation(stamp);
                if (stamp.RemainingRotTicks <= 0f)
                {
                    spoiledNutrition += batch.PreciseNutrition;
                    spoiledFullness += batch.PreciseFullness;
                }
            }
            batches.RemoveAll(b => b == null || b.Empty ||
                b.Preservation.RemainingRotTicks <= 0f);
        }

        public float StoredFullness
        {
            get
            {
                EnsureCollections();
                return (float)PreciseStoredFullness;
            }
        }

        public float StoredNutrition
        {
            get
            {
                EnsureCollections();
                return (float)PreciseStoredNutrition;
            }
        }

        public double PreciseStoredNutrition =>
            (batches ?? new List<FoodBatchV2>()).Sum(b => b == null || b.Empty ? 0d : b.PreciseNutrition);
        public double PreciseStoredFullness =>
            (batches ?? new List<FoodBatchV2>()).Sum(b => b == null || b.Empty ? 0d : b.PreciseFullness);

        public FoodPreservationStamp Preservation
        {
            get
            {
                EnsureCollections();
                FoodPreservationStamp result = FoodPreservationStamp.Fresh;
                bool any = false;
                foreach (FoodBatchV2 batch in batches)
                {
                    if (batch == null || batch.Empty)
                        continue;
                    result = any
                        ? FoodPreservationStamp.Combine(result,
                            batch.Preservation)
                        : batch.Preservation;
                    any = true;
                }
                return result;
            }
        }

        public float AverageFullnessToNutritionRatio
        {
            get
            {
                float nutrition = StoredNutrition;
                return FoodNetworkV2Constants.PositiveFinite(nutrition)
                    ? (float)(PreciseStoredFullness / PreciseStoredNutrition)
                    : 1f;
            }
        }

        public void Add(FoodBatchV2 batch)
        {
            if (batch == null || batch.Empty)
            {
                return;
            }

            EnsureCollections();
            FoodBatchV2 last = batches.Count == 0 ? null : batches[batches.Count - 1];
            if (last != null && CanMerge(last, batch))
            {
                last.SetAmounts(last.PreciseNutrition + batch.PreciseNutrition,
                    last.PreciseFullness + batch.PreciseFullness);
                last.SetPreservation(FoodPreservationStamp.Combine(
                    last.Preservation, batch.Preservation));
                last.createdTick = Mathf.Min(last.createdTick, batch.createdTick);
                return;
            }

            batches.Add(batch.Copy());
        }

        public void Purge()
        {
            EnsureCollections();
            purgedNutrition += PreciseStoredNutrition;
            purgedFullness += PreciseStoredFullness;
            batches.Clear();
        }

        public void AccumulateClassicLosses(FoodNetStorage_ThingComp classic)
        {
            if (classic == null) return;
            AddClassicDelta(classic.SpoiledNutrition,
                ref classicSpoiledNutritionSeen, ref spoiledNutrition);
            AddClassicDelta(classic.SpoiledFullness,
                ref classicSpoiledFullnessSeen, ref spoiledFullness);
            AddClassicDelta(classic.PurgedNutrition,
                ref classicPurgedNutritionSeen, ref purgedNutrition);
            AddClassicDelta(classic.PurgedFullness,
                ref classicPurgedFullnessSeen, ref purgedFullness);
        }

        private static void AddClassicDelta(double current,
            ref double seen, ref double total)
        {
            if (double.IsNaN(current) || double.IsInfinity(current) || current < 0d)
                return;
            if (current > seen) total += current - seen;
            seen = current;
        }

        public void RemoveEmptyBatches()
        {
            EnsureCollections();
            batches.RemoveAll(delegate(FoodBatchV2 batch)
            {
                return batch == null || batch.Empty;
            });
        }

        private static bool CanMerge(FoodBatchV2 first, FoodBatchV2 second)
        {
            // Only equivalent freshness within the same production hour forms
            // one chunk. Never give a new delivery an older chunk's deadline.
            if (first.createdTick / GenDate.TicksPerHour !=
                    second.createdTick / GenDate.TicksPerHour ||
                first.Preservation.RemainingRotTicks !=
                    second.Preservation.RemainingRotTicks ||
                first.legacyGraceAssumed != second.legacyGraceAssumed)
                return false;
            if (!Mathf.Approximately(
                    first.FullnessToNutritionRatio,
                    second.FullnessToNutritionRatio))
            {
                return false;
            }

            if (first.ingredients.Count != second.ingredients.Count)
            {
                return false;
            }

            return first.ingredients.All(delegate(ThingDef ingredient)
            {
                return second.ingredients.Contains(ingredient);
            });
        }

        private void EnsureCollections()
        {
            if (batches == null)
            {
                batches = new List<FoodBatchV2>();
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref thingId, "thingId", 0);
            Scribe_Values.Look(ref legacyMigrated, "legacyMigrated", false);
            Scribe_Values.Look(ref preservationLastTick,
                "preservationLastTick", 0);
            Scribe_Values.Look(ref spoiledNutrition,
                "spoiledNutrition", 0d);
            Scribe_Values.Look(ref spoiledFullness,
                "spoiledFullness", 0d);
            Scribe_Values.Look(ref purgedNutrition,
                "purgedNutrition", 0d);
            Scribe_Values.Look(ref purgedFullness,
                "purgedFullness", 0d);
            Scribe_Values.Look(ref classicSpoiledNutritionSeen,
                "classicSpoiledNutritionSeen", 0d);
            Scribe_Values.Look(ref classicSpoiledFullnessSeen,
                "classicSpoiledFullnessSeen", 0d);
            Scribe_Values.Look(ref classicPurgedNutritionSeen,
                "classicPurgedNutritionSeen", 0d);
            Scribe_Values.Look(ref classicPurgedFullnessSeen,
                "classicPurgedFullnessSeen", 0d);
            // The ID remains the compatibility key. A reference also lets a
            // packed vat be rebound after loading outside the map listers.
            Scribe_References.Look(ref runtimeTank, "runtimeTank");
            Scribe_Collections.Look(
                ref batches,
                "batches",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureCollections();
                RemoveEmptyBatches();
            }
        }
    }

    public sealed class AutoFeederLinkStateV2 : IExposable
    {
        public int feederThingId;
        public bool legacyTargetMigrated;
        public List<int> bedIds = new List<int>();
        // Current occupants are cached only so stale tube hediffs can be
        // removed when an occupant leaves a linked bed.
        public List<int> pawnIds = new List<int>();

        public AutoFeederLinkStateV2()
        {
        }

        public AutoFeederLinkStateV2(int feederThingId)
        {
            this.feederThingId = feederThingId;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref feederThingId, "feederThingId", 0);
            Scribe_Values.Look(
                ref legacyTargetMigrated,
                "legacyTargetMigrated",
                false);
            Scribe_Collections.Look(ref bedIds, "bedIds", LookMode.Value);
            Scribe_Collections.Look(ref pawnIds, "pawnIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                bedIds = bedIds ?? new List<int>();
                pawnIds = pawnIds ?? new List<int>();
            }
        }
    }

    /// <summary>
    /// Saved state is game-owned rather than map-owned so minified or moved
    /// tanks retain their exact batches and advanced feeders retain bed links.
    /// </summary>
    public sealed class FoodNetworkV2GameComponent : GameComponent
    {
        private List<FoodTankStateV2> tankStates = new List<FoodTankStateV2>();
        private List<AutoFeederLinkStateV2> feederStates =
            new List<AutoFeederLinkStateV2>();
        private double retiredSpoiledNutrition;
        private double retiredSpoiledFullness;
        private double retiredPurgedNutrition;
        private double retiredPurgedFullness;

        public double RetiredSpoiledNutrition => retiredSpoiledNutrition;
        public double RetiredSpoiledFullness => retiredSpoiledFullness;
        public double RetiredPurgedNutrition => retiredPurgedNutrition;
        public double RetiredPurgedFullness => retiredPurgedFullness;

        [Unsaved]
        private Dictionary<int, FoodTankStateV2> tankById;

        [Unsaved]
        private Dictionary<int, AutoFeederLinkStateV2> feederById;

        [Unsaved]
        private bool needsBindingScan = true;

        public FoodNetworkV2GameComponent(Game game)
        {
        }

        public static FoodNetworkV2GameComponent Instance
        {
            get
            {
                return Current.Game == null
                    ? null
                    : Current.Game.GetComponent<FoodNetworkV2GameComponent>();
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now <= 0 || now % 250 != 0)
                return;
            EnsureLookups();
            if (needsBindingScan)
                RefreshLiveTankBindings();
            foreach (FoodTankStateV2 state in tankStates)
            {
                if (state == null)
                    continue;
                Building building = state.runtimeTank;
                if (building == null || building.Destroyed)
                {
                    // Old integer-only records may have no discoverable
                    // building. Retain their conservative 21 C aging rather
                    // than freezing freshness forever.
                    state.AgeTo(now, 21f, false);
                    continue;
                }
                FoodNetStorage_ThingComp legacy =
                    building.TryGetComp<FoodNetStorage_ThingComp>();
                if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
                {
                    // A packed or gravship-carried classic comp receives no
                    // native CompTick. ReadPreservation applies its saved
                    // elapsed time, then updates the v2 shadow without taking
                    // ownership away from classic mode.
                    if (!building.Spawned && legacy != null)
                    {
                        Gravship gravship = Current.Game?.Gravship;
                        if (gravship != null && gravship.Tile.Valid &&
                            gravship.Things.Contains(building))
                            legacy.ReadPreservationAtTemperature(
                                TemperatureFor(building));
                        FoodNetworkV2MapComponent.ImportLegacyStorage(building);
                    }
                    continue;
                }
                bool cooling = CoolingActive(building);
                float temperature = TemperatureFor(building);
                state.AgeTo(now, temperature, cooling);
                if (legacy != null)
                {
                    legacy.V2PreservationProjection = true;
                    legacy.SetPreciseAmounts(state.PreciseStoredNutrition, state.PreciseStoredFullness,
                        state.batches.Where(b => b != null && !b.Empty).Select(b => b.createdTick).DefaultIfEmpty(0).Min());
                    legacy.SetPreservation(state.Preservation);
                }
            }
        }

        private void RefreshLiveTankBindings()
        {
            var held = DiscoverLiveTanks();
            foreach (FoodTankStateV2 state in tankStates)
            {
                if (state == null) continue;
                Building building;
                if (held.TryGetValue(state.thingId, out building))
                    state.runtimeTank = building;
            }
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
                foreach (Building building in held.Values)
                    if (!building.Spawned)
                        FoodNetworkV2MapComponent.ImportLegacyStorage(building);
            needsBindingScan = false;
        }

        internal static float TemperatureFor(Building building)
        {
            if (building == null) return 21f;
            // Native gravships keep their buildings in a deep-saved
            // dictionary, not a ThingOwner. Their unspawned Things have no
            // holder temperature, so use the current world tile.
            Gravship gravship = Current.Game?.Gravship;
            if (!building.Spawned && gravship != null &&
                gravship.Things.Contains(building) && gravship.Tile.Valid)
                return GenTemperature.GetTemperatureAtTile(gravship.Tile);
            return building.AmbientTemperature;
        }

        internal static Dictionary<int, Building> DiscoverLiveTanks()
        {
            var result = new Dictionary<int, Building>();
            if (Find.Maps != null)
                foreach (Map map in Find.Maps)
                    AddHeldTanks(map, result);
            if (Find.WorldObjects != null)
                foreach (WorldObject worldObject in Find.WorldObjects.AllWorldObjects)
                    AddHeldTanks(worldObject as IThingHolder, result);
            Gravship travelling = Current.Game?.Gravship;
            if (travelling != null)
            {
                foreach (Thing thing in travelling.Things)
                    AddTank(thing, result);
                foreach (Pawn pawn in travelling.Pawns)
                    AddHeldTanks(pawn, result);
            }
            return result;
        }

        private static void AddHeldTanks(IThingHolder holder,
            Dictionary<int, Building> result)
        {
            if (holder == null) return;
            foreach (Thing thing in ThingOwnerUtility.GetAllThingsRecursively(holder))
                AddTank(thing, result);
        }

        private static void AddTank(Thing thing,
            Dictionary<int, Building> result)
        {
            Building tank = thing as Building ??
                (thing as MinifiedThing)?.InnerThing as Building;
            if (tank != null && !tank.Destroyed &&
                FoodNetworkV2Constants.IsStorage(tank))
                result[tank.thingIDNumber] = tank;
        }

        internal static bool CoolingActive(Building building)
        {
            if (building == null || !building.Spawned)
                return false;
            CompPowerTrader power = building.TryGetComp<CompPowerTrader>();
            CompFlickable flick = building.TryGetComp<CompFlickable>();
            CompBreakdownable breakdown =
                building.TryGetComp<CompBreakdownable>();
            return power != null && power.PowerOn && flick != null &&
                flick.SwitchIsOn && breakdown != null &&
                !breakdown.BrokenDown;
        }

        public FoodTankStateV2 GetTankState(Building tank, bool create)
        {
            if (tank == null || tank.Destroyed || tank.Discarded)
            {
                return null;
            }

            EnsureLookups();
            FoodTankStateV2 state;
            if (tankById.TryGetValue(tank.thingIDNumber, out state))
            {
                state.runtimeTank = tank;
                return state;
            }

            if (!create)
            {
                return null;
            }

            state = new FoodTankStateV2(tank.thingIDNumber);
            state.runtimeTank = tank;
            tankStates.Add(state);
            tankById.Add(state.thingId, state);
            return state;
        }

        public AutoFeederLinkStateV2 GetFeederState(
            Building_AutoFeeder feeder,
            bool create)
        {
            if (feeder == null || feeder.Destroyed || feeder.Discarded)
            {
                return null;
            }

            EnsureLookups();
            AutoFeederLinkStateV2 state;
            if (feederById.TryGetValue(feeder.thingIDNumber, out state))
            {
                return state;
            }

            if (!create)
            {
                return null;
            }

            state = new AutoFeederLinkStateV2(feeder.thingIDNumber);
            feederStates.Add(state);
            feederById.Add(state.feederThingId, state);
            return state;
        }

        public IEnumerable<AutoFeederLinkStateV2> FeederStates
        {
            get
            {
                EnsureLookups();
                return feederStates;
            }
        }

        internal void RemoveDestroyedBuilding(Building building)
        {
            if (building == null || !building.Destroyed) return;
            EnsureLookups();
            int id = building.thingIDNumber;
            FoodTankStateV2 tank;
            if (tankById.TryGetValue(id, out tank))
            {
                // Empty the old object too: a cached topology must not retain
                // access to food after its physical container is destroyed.
                tank.Purge();
                RetireTankCounters(tank);
                tankStates.RemoveAll(state => state != null && state.thingId == id);
                tankById.Remove(id);
            }
            FoodNetStorage_ThingComp legacy = building.TryGetComp<FoodNetStorage_ThingComp>();
            if (legacy != null) legacy.Stored = 0f;

            AutoFeederLinkStateV2 feederState;
            if (feederById.TryGetValue(id, out feederState))
            {
                FoodNetworkV2AutoFeederUtility.DetachOccupants(
                    building as Building_AutoFeeder, feederState);
                feederState.bedIds.Clear();
                feederStates.RemoveAll(state => state != null && state.feederThingId == id);
                feederById.Remove(id);
            }
        }

        private void RetireTankCounters(FoodTankStateV2 tank)
        {
            retiredSpoiledNutrition += tank.spoiledNutrition;
            retiredSpoiledFullness += tank.spoiledFullness;
            retiredPurgedNutrition += tank.purgedNutrition;
            retiredPurgedFullness += tank.purgedFullness;
        }

        // Called before the map's holders are cleared. Only buildings still
        // physically held by that map are abandoned. Gravship-captured Things
        // and caravan inventories have already left its ThingOwner graph.
        internal void RemoveAbandonedMap(Map map)
        {
            if (map == null) return;
            EnsureLookups();
            var things = ThingOwnerUtility.GetAllThingsRecursively(map);
            var seen = new HashSet<int>();
            foreach (Thing thing in things)
            {
                Building building = thing as Building ??
                    (thing as MinifiedThing)?.InnerThing as Building;
                if (building == null || !seen.Add(building.thingIDNumber))
                    continue;
                if (building.TryGetComp<FoodNetStorage_ThingComp>() != null)
                {
                    if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
                        FoodNetworkV2MapComponent.ImportLegacyStorage(building);
                    FoodTankStateV2 tank;
                    if (tankById.TryGetValue(building.thingIDNumber, out tank))
                    {
                        tank.AgeTo(Find.TickManager?.TicksGame ?? 0,
                            TemperatureFor(building), CoolingActive(building));
                        tank.Purge();
                        RetireTankCounters(tank);
                        tankStates.Remove(tank);
                        tankById.Remove(building.thingIDNumber);
                    }
                    building.TryGetComp<FoodNetStorage_ThingComp>().Stored = 0f;
                }
                AutoFeederLinkStateV2 feeder;
                if (feederById.TryGetValue(building.thingIDNumber, out feeder))
                {
                    FoodNetworkV2AutoFeederUtility.DetachOccupants(
                        building as Building_AutoFeeder, feeder);
                    feeder.bedIds.Clear();
                    feederStates.Remove(feeder);
                    feederById.Remove(building.thingIDNumber);
                }
            }
            FoodNetworkV2MapComponent.For(map)?.MarkDirty();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving &&
                !FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                // Keep the shadow state current while the player is using the
                // classic fallback. This makes a later config-file re-enable
                // safe even if it happens before that save is loaded.
                FoodNetworkV2MapComponent.ImportLegacyStorageFromAllMaps(false);
                FoodNetworkV2AutoFeederUtility.ImportLegacyLinksFromAllMaps(false);
            }
            Scribe_Collections.Look(
                ref tankStates,
                "rrFoodNetworkV2Tanks",
                LookMode.Deep);
            Scribe_Collections.Look(
                ref feederStates,
                "rrFoodNetworkV2Feeders",
                LookMode.Deep);
            Scribe_Values.Look(ref retiredSpoiledNutrition,
                "rrFoodNetworkRetiredSpoiledNutrition", 0d);
            Scribe_Values.Look(ref retiredSpoiledFullness,
                "rrFoodNetworkRetiredSpoiledFullness", 0d);
            Scribe_Values.Look(ref retiredPurgedNutrition,
                "rrFoodNetworkRetiredPurgedNutrition", 0d);
            Scribe_Values.Look(ref retiredPurgedFullness,
                "rrFoodNetworkRetiredPurgedFullness", 0d);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                tankStates = tankStates ?? new List<FoodTankStateV2>();
                feederStates = feederStates ?? new List<AutoFeederLinkStateV2>();
                RebuildLookups();
                needsBindingScan = true;
            }
        }

        private void EnsureLookups()
        {
            if (tankById == null || feederById == null)
            {
                RebuildLookups();
            }
        }

        private void RebuildLookups()
        {
            tankStates = tankStates ?? new List<FoodTankStateV2>();
            feederStates = feederStates ?? new List<AutoFeederLinkStateV2>();

            tankStates.RemoveAll(delegate(FoodTankStateV2 state)
            {
                return state == null || state.thingId == 0;
            });
            feederStates.RemoveAll(delegate(AutoFeederLinkStateV2 state)
            {
                return state == null || state.feederThingId == 0;
            });

            tankById = new Dictionary<int, FoodTankStateV2>();
            foreach (FoodTankStateV2 state in tankStates)
            {
                tankById[state.thingId] = state;
            }

            feederById = new Dictionary<int, AutoFeederLinkStateV2>();
            foreach (AutoFeederLinkStateV2 state in feederStates)
            {
                feederById[state.feederThingId] = state;
            }
        }
    }

    internal sealed class FoodNetworkTankBindingV2
    {
        public Building building;
        public FoodNetStorage_ThingComp legacyComp;
        public FoodTankStateV2 state;

        public float Capacity
        {
            get { return legacyComp == null ? 0f : legacyComp.Capacity; }
        }

        public float Remaining
        {
            get { return (float)PreciseRemaining; }
        }

        public double PreciseRemaining => Math.Max(0d, Capacity - state.PreciseStoredFullness);
    }

    internal sealed class FoodNetworkDrawSliceV2
    {
        public FoodNetworkTankBindingV2 tank;
        public FoodBatchV2 source;
        public double nutrition;
        public double fullness;
    }

    public sealed partial class FoodNetworkV2
    {
        private readonly List<Thing> nodes;
        private readonly List<FoodNetworkTankBindingV2> tanks;

        internal FoodNetworkV2(
            int id,
            IEnumerable<Thing> nodes,
            IEnumerable<FoodNetworkTankBindingV2> tanks)
        {
            Id = id;
            this.nodes = nodes == null
                ? new List<Thing>()
                : nodes.Where(delegate(Thing thing) { return thing != null; }).ToList();
            this.tanks = tanks == null
                ? new List<FoodNetworkTankBindingV2>()
                : tanks.Where(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank != null && tank.state != null;
                }).ToList();
        }

        public int Id { get; private set; }

        internal void Retire()
        {
            // Connectivity is a snapshot. Callers holding an old network must
            // reacquire it after a valve change, packing or map transfer.
            nodes.Clear();
            tanks.Clear();
        }

        public IReadOnlyList<Thing> Nodes
        {
            get { return nodes; }
        }

        public float StoredFullness
        {
            get
            {
                return (float)PreciseStoredFullness;
            }
        }

        public float StoredNutrition
        {
            get
            {
                return (float)PreciseStoredNutrition;
            }
        }

        public double PreciseStoredNutrition
        {
            get { AgeTanks(); return tanks.Sum(t => t.state.PreciseStoredNutrition); }
        }
        public double PreciseStoredFullness
        {
            get { AgeTanks(); return tanks.Sum(t => t.state.PreciseStoredFullness); }
        }

        private void AgeTanks()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now <= 0)
                return;
            foreach (FoodNetworkTankBindingV2 tank in tanks)
                tank.state.AgeTo(now, tank.building.AmbientTemperature,
                    FoodNetworkV2GameComponent.CoolingActive(tank.building));
        }

        public float Capacity
        {
            get
            {
                return tanks.Sum(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.Capacity;
                });
            }
        }

        public float RemainingCapacity
        {
            get { return (float)Math.Max(0d, Capacity - PreciseStoredFullness); }
        }

        public float AverageFullnessToNutritionRatio
        {
            get
            {
                float nutrition = StoredNutrition;
                return FoodNetworkV2Constants.PositiveFinite(nutrition)
                    ? (float)(PreciseStoredFullness / PreciseStoredNutrition)
                    : 1f;
            }
        }

        public int BatchCount
        {
            get
            {
                return tanks.Sum(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.state.batches == null
                        ? 0
                        : tank.state.batches.Count;
                });
            }
        }

        public bool HasStorage
        {
            get { return tanks.Count > 0 && Capacity > FoodNetworkV2Constants.Epsilon; }
        }

        public bool CanStore(FoodBatchV2 batch)
        {
            return batch != null && !batch.Empty &&
                (batch.ingredients == null || batch.ingredients.All(OrdinaryFoodIntake.IsProcessableFoodDef)) &&
                batch.Preservation.Valid &&
                batch.Preservation.RemainingRotTicks > 0f &&
                FoodNetworkV2Constants.PositiveFinite(StoredNutrition + batch.nutrition) &&
                FoodNetworkV2Constants.PositiveFinite(StoredFullness + batch.fullness) &&
                Capacity - PreciseStoredFullness +
                    FoodNetworkV2Constants.CapacityTolerance(Capacity) >= batch.PreciseFullness;
        }

        public bool TryStore(FoodBatchV2 batch)
        {
            if (!CanStore(batch))
            {
                return false;
            }

            double nutritionBefore = PreciseStoredNutrition;
            double fullnessBefore = PreciseStoredFullness;
            double remainingNutrition = batch.PreciseNutrition;
            double remainingFullness = batch.PreciseFullness;
            // Plan the entire allocation before touching any tank. Failed
            // stores leave both quantities and ingredient history untouched.
            var portions = new List<KeyValuePair<FoodNetworkTankBindingV2, FoodBatchV2>>();
            List<FoodNetworkTankBindingV2> orderedTanks = tanks
                .OrderBy(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.Capacity <= FoodNetworkV2Constants.Epsilon
                        ? 1f
                        : tank.state.StoredFullness / tank.Capacity;
                })
                .ThenBy(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.building.thingIDNumber;
                })
                .ToList();

            foreach (FoodNetworkTankBindingV2 tank in orderedTanks)
            {
                if (remainingFullness <= 0f)
                {
                    break;
                }

                double rawRemaining = tank.Capacity - tank.state.PreciseStoredFullness;
                double portionFullness = remainingFullness <= rawRemaining +
                    FoodNetworkV2Constants.CapacityTolerance(tank.Capacity)
                    ? remainingFullness : Math.Min(tank.PreciseRemaining, remainingFullness);
                if (portionFullness <= 0d)
                {
                    continue;
                }

                double fraction = portionFullness / remainingFullness;
                double portionNutrition = remainingNutrition * fraction;
                FoodBatchV2 portion = batch.PortionPrecise(portionNutrition, portionFullness);
                if (portion.Empty)
                {
                    return false;
                }
                portions.Add(new KeyValuePair<FoodNetworkTankBindingV2, FoodBatchV2>(tank, portion));
                remainingNutrition -= portionNutrition;
                remainingFullness -= portionFullness;
            }

            if (remainingFullness > 0f || remainingNutrition > 0f)
            {
                return false;
            }
            foreach (var portion in portions)
            {
                portion.Key.state.Add(portion.Value);
            }
            SyncLegacyStorageComps();
            ValidateConservation(
                "store",
                PreciseStoredNutrition - nutritionBefore,
                PreciseStoredFullness - fullnessBefore,
                batch.PreciseNutrition,
                batch.PreciseFullness);
            return true;
        }

        public bool CanDrawNutrition(float nutrition)
        {
            return FoodNetworkV2Constants.PositiveFinite(nutrition) &&
                PreciseStoredNutrition >= nutrition;
        }

        public bool CanDraw(float maximumNutrition, float maximumFullness)
        {
            if (!FoodNetworkV2Constants.PositiveFinite(maximumNutrition) ||
                !FoodNetworkV2Constants.PositiveFinite(maximumFullness))
            {
                return false;
            }

            return BuildDrawPlan(maximumNutrition, maximumFullness)
                .Any(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.nutrition > 0f && slice.fullness > 0f;
                });
        }

        public bool TryPreviewExactNutrition(float nutrition, out FoodBatchV2 batch)
        {
            batch = null;
            if (!FoodNetworkV2Constants.PositiveFinite(nutrition))
            {
                return false;
            }

            List<FoodNetworkDrawSliceV2> plan = BuildDrawPlan(
                nutrition,
                float.MaxValue);
            double plannedNutrition = plan.Sum(delegate(FoodNetworkDrawSliceV2 slice)
            {
                return slice.nutrition;
            });
            if (!FoodNetworkV2Constants.MeetsExactNutrition(plannedNutrition, nutrition))
            {
                return false;
            }

            double plannedFullness = plan.Sum(delegate(FoodNetworkDrawSliceV2 slice)
            {
                return slice.fullness;
            });
            int createdTick = plan.Count == 0
                ? CurrentTick
                : plan.Min(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.source.createdTick;
                });
            List<ThingDef> ingredients = plan
                .SelectMany(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.source.ingredients ?? new List<ThingDef>();
                })
                .Where(delegate(ThingDef ingredient) { return ingredient != null; })
                .Distinct()
                .ToList();

            batch = new FoodBatchV2(
                (float)plannedNutrition,
                (float)plannedFullness,
                ingredients,
                createdTick);
            batch.SetAmounts(plannedNutrition, plannedFullness);
            batch.SetPreservation(CombinePlanPreservation(plan));
            return !batch.Empty;
        }

        public bool TryDrawExactNutrition(float nutrition, out FoodBatchV2 batch)
        {
            return TryDraw(
                nutrition,
                float.MaxValue,
                true,
                out batch);
        }

        // Validate a physical output before committing the shared draw plan.
        // Rejection leaves every source batch intact, including its provenance.
        internal bool TryDrawExactNutrition(float nutrition,
            Func<FoodBatchV2, bool> acceptOutput, out FoodBatchV2 batch)
        {
            return TryDraw(nutrition, float.MaxValue, true, out batch, acceptOutput);
        }

        public bool TryDraw(
            float maximumNutrition,
            float maximumFullness,
            bool requireMaximumNutrition,
            out FoodBatchV2 batch)
        {
            return TryDraw(maximumNutrition, maximumFullness,
                requireMaximumNutrition, out batch, null);
        }

        internal bool TryDraw(float maximumNutrition, float maximumFullness,
            bool requireMaximumNutrition, out FoodBatchV2 batch,
            Func<FoodBatchV2, bool> acceptOutput)
        {
            batch = null;
            if (!FoodNetworkV2Constants.PositiveFinite(maximumNutrition) ||
                !FoodNetworkV2Constants.PositiveFinite(maximumFullness))
            {
                return false;
            }

            double nutritionBefore = PreciseStoredNutrition;
            double fullnessBefore = PreciseStoredFullness;
            List<FoodNetworkDrawSliceV2> plan = BuildDrawPlan(
                maximumNutrition,
                maximumFullness);
            double plannedNutrition = plan.Sum(delegate(FoodNetworkDrawSliceV2 slice)
            {
                return slice.nutrition;
            });

            if (!FoodNetworkV2Constants.PositiveFinite((float)plannedNutrition) ||
                (requireMaximumNutrition &&
                 !FoodNetworkV2Constants.MeetsExactNutrition(
                     plannedNutrition, maximumNutrition)))
            {
                return false;
            }

            double plannedFullness = plan.Sum(slice => slice.fullness);
            if (!FoodNetworkV2Constants.PositiveFinite((float)plannedFullness) ||
                !FoodNetworkV2Constants.PositiveFinite((float)(plannedFullness / plannedNutrition)))
            {
                return false;
            }

            double drawnNutrition = 0d;
            double drawnFullness = 0d;
            int createdTick = int.MaxValue;
            List<ThingDef> ingredients = new List<ThingDef>();
            FoodPreservationStamp preservation =
                CombinePlanPreservation(plan);

            if (acceptOutput != null)
            {
                FoodBatchV2 prepared = new FoodBatchV2((float)plannedNutrition,
                    (float)plannedFullness, plan.SelectMany(slice => slice.source.ingredients)
                        .Where(ingredient => ingredient != null).Distinct(),
                    plan.Min(slice => slice.source.createdTick));
                prepared.SetAmounts(plannedNutrition, plannedFullness);
                prepared.SetPreservation(preservation);
                if (prepared.Empty || !acceptOutput(prepared)) return false;
            }

            foreach (FoodNetworkDrawSliceV2 slice in plan)
            {
                slice.source.SetAmounts(
                    Math.Max(0d, slice.source.PreciseNutrition - slice.nutrition),
                    Math.Max(0d, slice.source.PreciseFullness - slice.fullness));
                drawnNutrition += slice.nutrition;
                drawnFullness += slice.fullness;
                createdTick = Mathf.Min(createdTick, slice.source.createdTick);
                foreach (ThingDef ingredient in slice.source.ingredients)
                {
                    if (ingredient != null && !ingredients.Contains(ingredient))
                    {
                        ingredients.Add(ingredient);
                    }
                }
            }

            foreach (FoodNetworkTankBindingV2 tank in tanks)
            {
                tank.state.RemoveEmptyBatches();
            }

            SyncLegacyStorageComps();
            batch = new FoodBatchV2(
                (float)drawnNutrition,
                (float)drawnFullness,
                ingredients,
                createdTick == int.MaxValue ? CurrentTick : createdTick);
            batch.SetAmounts(drawnNutrition, drawnFullness);
            batch.SetPreservation(preservation);
            ValidateConservation(
                "draw",
                nutritionBefore - PreciseStoredNutrition,
                fullnessBefore - PreciseStoredFullness,
                batch.PreciseNutrition,
                batch.PreciseFullness);
            return !batch.Empty;
        }

        public void Purge()
        {
            foreach (FoodNetworkTankBindingV2 tank in tanks)
            {
                tank.state.Purge();
            }
            SyncLegacyStorageComps();
        }

        public string IngredientSummary(int maximumNames)
        {
            List<ThingDef> ingredients = tanks
                .SelectMany(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.state.batches ?? new List<FoodBatchV2>();
                })
                .Where(delegate(FoodBatchV2 batch) { return batch != null; })
                .SelectMany(delegate(FoodBatchV2 batch)
                {
                    return batch.ingredients ?? new List<ThingDef>();
                })
                .Where(delegate(ThingDef ingredient) { return ingredient != null; })
                .Distinct()
                .OrderBy(delegate(ThingDef ingredient) { return ingredient.label; })
                .ToList();

            if (ingredients.Count == 0)
            {
                return "RR_FoodNetworkNoIngredients".Translate();
            }

            List<string> names = ingredients
                .Take(maximumNames)
                .Select(delegate(ThingDef ingredient)
                {
                    return ingredient.LabelCap.ToString();
                })
                .ToList();
            if (ingredients.Count > maximumNames)
            {
                names.Add("+" + (ingredients.Count - maximumNames).ToString());
            }
            return string.Join(", ", names.ToArray());
        }

        public void SyncLegacyStorageComps()
        {
            AgeTanks();
            foreach (FoodNetworkTankBindingV2 tank in tanks)
            {
                if (tank.legacyComp == null)
                {
                    continue;
                }

                tank.legacyComp.V2PreservationProjection = true;
                tank.legacyComp.SetPreciseAmounts(tank.state.PreciseStoredNutrition, tank.state.PreciseStoredFullness,
                    tank.state.batches.Where(b => b != null && !b.Empty).Select(b => b.createdTick).DefaultIfEmpty(0).Min());
                tank.legacyComp.SetPreservation(
                    tank.state.Preservation);
            }
        }

        private static FoodPreservationStamp CombinePlanPreservation(
            List<FoodNetworkDrawSliceV2> plan)
        {
            FoodPreservationStamp result = FoodPreservationStamp.Fresh;
            bool any = false;
            foreach (FoodNetworkDrawSliceV2 slice in plan)
            {
                if (slice == null || slice.source == null ||
                    slice.nutrition <= 0d)
                    continue;
                result = any
                    ? FoodPreservationStamp.Combine(result,
                        slice.source.Preservation)
                    : slice.source.Preservation;
                any = true;
            }
            return result;
        }

        private List<FoodNetworkDrawSliceV2> BuildDrawPlan(
            float maximumNutrition,
            float maximumFullness)
        {
            AgeTanks();
            List<FoodNetworkDrawSliceV2> result =
                new List<FoodNetworkDrawSliceV2>();
            double nutritionRemaining = maximumNutrition;
            double fullnessRemaining = maximumFullness;

            IEnumerable<FoodNetworkDrawSliceV2> orderedBatches = tanks
                .SelectMany(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return (tank.state.batches ?? new List<FoodBatchV2>())
                        .Select(delegate(FoodBatchV2 batch)
                        {
                            return new FoodNetworkDrawSliceV2
                            {
                                tank = tank,
                                source = batch
                            };
                        });
                })
                .Where(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.source != null && !slice.source.Empty &&
                        slice.source.Preservation.RemainingRotTicks > 0f;
                })
                .OrderBy(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.source.createdTick;
                })
                .ThenBy(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.tank.building.thingIDNumber;
                });

            foreach (FoodNetworkDrawSliceV2 entry in orderedBatches)
            {
                if (nutritionRemaining <= 0f || fullnessRemaining <= 0f)
                {
                    break;
                }

                double ratio = entry.source.PreciseFullness / entry.source.PreciseNutrition;
                double nutritionByFullness = fullnessRemaining / ratio;
                double nutrition = Math.Min(entry.source.PreciseNutrition,
                    Math.Min(nutritionRemaining, nutritionByFullness));
                if (!FoodNetworkV2Constants.PositiveFinite((float)nutrition))
                {
                    continue;
                }

                double fullness = nutrition == entry.source.PreciseNutrition
                    ? entry.source.PreciseFullness : Math.Min(entry.source.PreciseFullness, nutrition * ratio);
                if (!FoodNetworkV2Constants.PositiveFinite((float)fullness))
                {
                    continue;
                }
                entry.nutrition = nutrition;
                entry.fullness = fullness;
                result.Add(entry);
                nutritionRemaining -= nutrition;
                fullnessRemaining -= fullness;
            }

            return result;
        }

        private static int CurrentTick
        {
            get { return Find.TickManager == null ? 0 : Find.TickManager.TicksGame; }
        }

        private void ValidateConservation(
            string operation,
            double actualNutrition,
            double actualFullness,
            double expectedNutrition,
            double expectedFullness)
        {
            if (!Prefs.DevMode)
            {
                return;
            }
            if (Math.Abs(actualNutrition - expectedNutrition) > 0.001d ||
                Math.Abs(actualFullness - expectedFullness) > 0.001d)
            {
                Log.Error(
                    "[RimRound Feed Other] Food Network v2 conservation " +
                    "check failed during " + operation + " on network " + Id +
                    ": expected " + expectedNutrition.ToString("F4") +
                    " nutrition/" + expectedFullness.ToString("F4") +
                    " fullness, observed " + actualNutrition.ToString("F4") +
                    "/" + actualFullness.ToString("F4") + ".");
            }
        }
    }

    /// <summary>
    /// Ephemeral topology. Save data remains in FoodNetworkV2GameComponent;
    /// this component only rebuilds the affected map when connectors change.
    /// </summary>
    public sealed class FoodNetworkV2MapComponent : MapComponent
    {
        private readonly List<FoodNetworkV2> networks = new List<FoodNetworkV2>();
        private readonly Dictionary<int, FoodNetworkV2> networkByThingId =
            new Dictionary<int, FoodNetworkV2>();
        private readonly Dictionary<IntVec3, FoodNetworkV2> networkByCell =
            new Dictionary<IntVec3, FoodNetworkV2>();
        private bool topologyDirty = true;
        private ThingDef[] autoFeederDefs;
        private int indexedDefCount;

        public FoodNetworkV2MapComponent(Map map) : base(map)
        {
        }

        public static FoodNetworkV2MapComponent For(Map map)
        {
            return map == null ? null : map.GetComponent<FoodNetworkV2MapComponent>();
        }

        internal IEnumerable<Thing> AutoFeederCandidates()
        {
            var defs = DefDatabase<ThingDef>.AllDefsListForReading;
            if (autoFeederDefs == null || indexedDefCount != defs.Count)
            {
                autoFeederDefs = defs.Where(def => typeof(Building_AutoFeeder).IsAssignableFrom(def.thingClass)).ToArray();
                indexedDefCount = defs.Count;
            }

            // The native lists include every faction and update on spawn and despawn.
            // Preserve cross-type spawn order when multiple feeder types share a map.
            List<Thing> matches = null;
            foreach (ThingDef def in autoFeederDefs)
            {
                var spawned = map.listerThings.ThingsOfDef(def);
                if (spawned.Count == 0) continue;
                if (matches != null)
                    return autoFeederDefs.All(d => d.IsBuildingArtificial)
                        ? map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                        : map.listerThings.AllThings;
                matches = spawned;
            }
            return matches ?? (IEnumerable<Thing>)Array.Empty<Thing>();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            FoodNetworkV2FaucetListerUtility.SyncMap(
                map,
                !FeedOtherMod.Settings.foodNetworkV2Enabled);
            topologyDirty = true;
            EnsureTopology();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return;
            }

            int ticks = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (topologyDirty || ticks % 60 == 0)
            {
                EnsureTopology();
            }

            if (ticks > 0 && ticks % 250 == 0)
            {
                FoodNetworkV2AutoFeederUtility.CleanupMapLinks(map);
                foreach (FoodNetworkV2 network in networks)
                {
                    network.SyncLegacyStorageComps();
                }
            }
        }

        public void MarkDirty()
        {
            foreach (FoodNetworkV2 network in networks) network.Retire();
            topologyDirty = true;
        }

        public void EnsureTopology()
        {
            if (!topologyDirty)
            {
                return;
            }

            RebuildTopology();
            topologyDirty = false;
        }

        public FoodNetworkV2 NetworkFor(Thing thing)
        {
            if (thing == null)
            {
                return null;
            }

            EnsureTopology();
            FoodNetworkV2 network;
            return networkByThingId.TryGetValue(thing.thingIDNumber, out network)
                ? network
                : null;
        }

        public FoodNetworkV2 NetworkAt(IntVec3 cell)
        {
            EnsureTopology();
            FoodNetworkV2 network;
            return networkByCell.TryGetValue(cell, out network) ? network : null;
        }

        public bool HasConnectorAt(IntVec3 cell)
        {
            return NetworkAt(cell) != null;
        }

        public IReadOnlyList<FoodNetworkV2> Networks
        {
            get
            {
                EnsureTopology();
                return networks;
            }
        }

        private void RebuildTopology()
        {
            foreach (FoodNetworkV2 network in networks) network.Retire();
            networks.Clear();
            networkByThingId.Clear();
            networkByCell.Clear();

            if (!FeedOtherMod.Settings.foodNetworkV2Enabled || map == null)
            {
                return;
            }

            List<Thing> nodes = map.listerThings.AllThings
                .Where(delegate(Thing thing)
                {
                    if (!FoodNetworkV2Constants.IsFoodNetworkThing(thing) ||
                        FoodNetworkV2Constants.IsDistiller(thing) ||
                        !thing.Spawned)
                    {
                        return false;
                    }

                    FoodTransmitter_ThingComp comp =
                        thing.TryGetComp<FoodTransmitter_ThingComp>();
                    return comp != null && comp.TransmitsFoodNow;
                })
                .ToList();

            Dictionary<IntVec3, List<Thing>> nodesByCell =
                new Dictionary<IntVec3, List<Thing>>();
            foreach (Thing node in nodes)
            {
                foreach (IntVec3 cell in ConnectionCells(node))
                {
                    List<Thing> atCell;
                    if (!nodesByCell.TryGetValue(cell, out atCell))
                    {
                        atCell = new List<Thing>();
                        nodesByCell.Add(cell, atCell);
                    }
                    atCell.Add(node);
                }
            }

            HashSet<Thing> unvisited = new HashSet<Thing>(nodes);
            while (unvisited.Count > 0)
            {
                Thing root = unvisited.First();
                Queue<Thing> queue = new Queue<Thing>();
                List<Thing> component = new List<Thing>();
                queue.Enqueue(root);
                unvisited.Remove(root);

                while (queue.Count > 0)
                {
                    Thing current = queue.Dequeue();
                    component.Add(current);
                    foreach (Thing neighbor in Neighbors(current, nodesByCell))
                    {
                        if (unvisited.Remove(neighbor))
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                AddNetwork(component);
            }
        }

        private void AddNetwork(List<Thing> component)
        {
            if (component == null || component.Count == 0)
            {
                return;
            }

            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            List<FoodNetworkTankBindingV2> tanks =
                new List<FoodNetworkTankBindingV2>();
            foreach (Building building in component.OfType<Building>())
            {
                FoodNetStorage_ThingComp legacy =
                    building.TryGetComp<FoodNetStorage_ThingComp>();
                if (legacy == null || saved == null)
                {
                    continue;
                }

                FoodTankStateV2 state = saved.GetTankState(building, true);
                MigrateLegacyStorage(legacy, state);
                tanks.Add(new FoodNetworkTankBindingV2
                {
                    building = building,
                    legacyComp = legacy,
                    state = state
                });
            }

            int id = component.Min(delegate(Thing thing)
            {
                return thing.thingIDNumber;
            });
            FoodNetworkV2 network = new FoodNetworkV2(id, component, tanks);
            networks.Add(network);
            foreach (Thing thing in component)
            {
                networkByThingId[thing.thingIDNumber] = network;
                foreach (IntVec3 cell in ConnectionCells(thing))
                {
                    networkByCell[cell] = network;
                }
            }
            network.SyncLegacyStorageComps();
        }

        private static void MigrateLegacyStorage(
            FoodNetStorage_ThingComp legacy,
            FoodTankStateV2 state)
        {
            if (legacy == null || state == null || state.legacyMigrated)
            {
                return;
            }

            if (FoodNetworkV2Constants.PositiveFinite(legacy.Stored))
            {
                float ratio = FoodNetworkV2Constants.PositiveFinite(legacy.FullnessToNutritionRatio)
                    ? legacy.FullnessToNutritionRatio : 1f;
                FoodPreservationStamp stamp = legacy.ReadPreservation();
                FoodBatchV2 migrated = new FoodBatchV2(
                    legacy.Stored / ratio,
                    legacy.Stored,
                    stamp.Ingredients ?? new List<ThingDef>(),
                    Find.TickManager == null ? 0 : Find.TickManager.TicksGame);
                migrated.SetAmounts(legacy.PreciseNutrition, legacy.PreciseVolume);
                if (legacy.SourceCreatedTick > 0) migrated.createdTick = legacy.SourceCreatedTick;
                migrated.SetPreservation(stamp);
                state.Add(migrated);
            }
            state.legacyMigrated = true;
        }

        private static IEnumerable<Thing> Neighbors(
            Thing thing,
            Dictionary<IntVec3, List<Thing>> nodesByCell)
        {
            HashSet<Thing> result = new HashSet<Thing>();
            foreach (IntVec3 cell in ConnectionCells(thing))
            {
                AddThingsAt(cell, thing, nodesByCell, result);
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    AddThingsAt(
                        cell + GenAdj.CardinalDirections[i],
                        thing,
                        nodesByCell,
                        result);
                }
            }
            return result;
        }

        private static void AddThingsAt(
            IntVec3 cell,
            Thing self,
            Dictionary<IntVec3, List<Thing>> nodesByCell,
            HashSet<Thing> result)
        {
            List<Thing> things;
            if (!nodesByCell.TryGetValue(cell, out things))
            {
                return;
            }

            foreach (Thing thing in things)
            {
                if (thing != self)
                {
                    result.Add(thing);
                }
            }
        }

        internal static IEnumerable<IntVec3> ConnectionCells(Thing thing)
        {
            if (thing == null)
            {
                yield break;
            }

            // Existing machines accepted pipes around their footprint. Keeping
            // those explicit footprint ports preserves old colonies. The
            // distiller is deliberately excluded and uses two directional
            // external ports instead.
            foreach (IntVec3 cell in thing.OccupiedRect().Cells)
            {
                yield return cell;
            }
        }

        public static void ImportLegacyStorageFromAllMaps()
        {
            ImportLegacyStorageFromAllMaps(true);
        }

        public static void ImportLegacyStorageFromAllMaps(bool adoptV2)
        {
            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            if (saved == null)
            {
                return;
            }

            foreach (Building tank in FoodNetworkV2GameComponent
                .DiscoverLiveTanks().Values)
            {
                FoodNetStorage_ThingComp legacy =
                    tank.TryGetComp<FoodNetStorage_ThingComp>();
                Gravship gravship = Current.Game?.Gravship;
                if (legacy == null) continue;
                if (!tank.Spawned && gravship != null &&
                    gravship.Things.Contains(tank) && gravship.Tile.Valid)
                    legacy.ReadPreservationAtTemperature(
                        FoodNetworkV2GameComponent.TemperatureFor(tank));
                ImportLegacyStorage(tank, adoptV2);
            }
        }

        internal static void ImportLegacyStorage(Building tank,
            bool adoptV2 = false)
        {
            FoodNetStorage_ThingComp legacy = tank?.TryGetComp<FoodNetStorage_ThingComp>();
            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            if (legacy == null || saved == null)
            {
                return;
            }

            FoodTankStateV2 state = saved.GetTankState(tank, true);
            FoodPreservationStamp classicStamp = legacy.ReadPreservation();
            state.AccumulateClassicLosses(legacy);
            float volume = FoodNetworkV2Constants.PositiveFinite(legacy.Stored) ? legacy.Stored : 0f;
            float ratio = FoodNetworkV2Constants.PositiveFinite(legacy.FullnessToNutritionRatio)
                ? legacy.FullnessToNutritionRatio : 1f;
            float nutrition = volume / ratio;
            // Toggling the fallback without consuming anything must not erase
            // known ingredients or turn old food into freshly made food.
            if (state.legacyMigrated && state.StoredFullness == volume &&
                state.AverageFullnessToNutritionRatio == ratio &&
                (!legacy.HasPreciseAmounts ||
                 FoodServingMath.Same(state.PreciseStoredNutrition, legacy.PreciseNutrition) &&
                 FoodServingMath.Same(state.PreciseStoredFullness, legacy.PreciseVolume)) &&
                classicStamp.RemainingRotTicks > 0f &&
                state.Preservation.RemainingRotTicks > 0f)
            {
                // The classic comp is just an aggregate projection while v2
                // owns this tank. Rebuilding pipes must not spread its minimum
                // expiry to every independent chunk.
                int now = Find.TickManager?.TicksGame ?? 0;
                if (legacy.V2PreservationProjection)
                    state.AgeTo(now, FoodNetworkV2GameComponent.TemperatureFor(tank),
                        FoodNetworkV2GameComponent.CoolingActive(tank));
                else
                {
                    float elapsedFreshness = Mathf.Max(0f,
                        state.Preservation.RemainingRotTicks - classicStamp.RemainingRotTicks);
                    foreach (FoodBatchV2 batch in state.batches)
                        if (batch != null && !batch.Empty)
                            batch.SetPreservation(batch.Preservation.Aged(elapsedFreshness));
                    state.preservationLastTick = now;
                }
                legacy.V2PreservationProjection = adoptV2;
                return;
            }

            var prior = state.batches.Where(b => b != null && !b.Empty).ToList();
            var ingredients = prior.SelectMany(b => b.ingredients ?? new List<ThingDef>())
                .Concat(classicStamp.Ingredients ?? new List<ThingDef>())
                .Where(d => d != null).Distinct().ToList();
            int oldest = prior.Count == 0
                ? (Find.TickManager == null ? 0 : Find.TickManager.TicksGame)
                : prior.Min(b => b.createdTick);
            if (legacy.SourceCreatedTick > 0) oldest = Math.Min(oldest, legacy.SourceCreatedTick);
            FoodPreservationStamp conservative = classicStamp;
            foreach (FoodBatchV2 batch in prior)
                conservative = FoodPreservationStamp.Combine(
                    conservative, batch.Preservation);
            state.batches.Clear();
            if (FoodNetworkV2Constants.PositiveFinite(volume) &&
                (conservative.RemainingRotTicks <= 0f ||
                 !FoodNetworkV2Constants.PositiveFinite(nutrition)))
            {
                // Classic volume is authoritative on handoff. If its known
                // food has expired, count that exact quantity before clearing
                // the projection rather than silently dropping it.
                if (conservative.RemainingRotTicks <= 0f)
                {
                    state.spoiledNutrition += (double)volume / ratio;
                    state.spoiledFullness += volume;
                }
                else
                {
                    state.purgedNutrition += (double)volume / ratio;
                    state.purgedFullness += volume;
                }
                legacy.V2PreservationProjection = adoptV2;
                legacy.Stored = 0f;
            }
            if (FoodNetworkV2Constants.PositiveFinite(nutrition) &&
                FoodNetworkV2Constants.PositiveFinite(volume) &&
                conservative.RemainingRotTicks > 0f)
            {
                FoodBatchV2 imported = new FoodBatchV2(nutrition,
                    volume, ingredients, oldest);
                imported.SetAmounts(legacy.PreciseNutrition, legacy.PreciseVolume);
                imported.SetPreservation(conservative);
                state.Add(imported);
            }
            state.legacyMigrated = true;
            state.preservationLastTick = Find.TickManager?.TicksGame ?? 0;
            legacy.V2PreservationProjection = adoptV2;
        }

        public static void ExportSavedStorageToLegacyAllMaps()
        {
            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            if (saved == null)
            {
                return;
            }

            foreach (Building tank in FoodNetworkV2GameComponent
                .DiscoverLiveTanks().Values)
            {
                FoodNetStorage_ThingComp legacy =
                    tank.TryGetComp<FoodNetStorage_ThingComp>();
                FoodTankStateV2 state = saved.GetTankState(tank, false);
                if (legacy == null || state == null)
                {
                    continue;
                }

                state.AgeTo(Find.TickManager?.TicksGame ?? 0,
                    FoodNetworkV2GameComponent.TemperatureFor(tank),
                    FoodNetworkV2GameComponent.CoolingActive(tank));

                legacy.V2PreservationProjection = true;
                legacy.SetPreciseAmounts(state.PreciseStoredNutrition, state.PreciseStoredFullness,
                    state.batches.Where(b => b != null && !b.Empty).Select(b => b.createdTick).DefaultIfEmpty(0).Min());
                legacy.SetPreservation(state.Preservation);
                legacy.V2PreservationProjection = false;
            }
        }

        public static void MarkAllMapsDirty()
        {
            if (Find.Maps == null)
            {
                return;
            }

            foreach (Map currentMap in Find.Maps)
            {
                FoodNetworkV2MapComponent component = For(currentMap);
                if (component != null)
                {
                    component.MarkDirty();
                }
            }
        }
    }
}
