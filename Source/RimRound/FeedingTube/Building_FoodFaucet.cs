using RimRound.FeedingTube.Comps;
using RimWorld;
using RimRound.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.Sound;

namespace RimRound.FeedingTube
{
    public class Building_FoodFaucet : Building
    {
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = base.GetComp<CompPowerTrader>();
            foodNetTrader = base.GetComp<FoodNetTrader_ThingComp>();
        }

        public Thing TryDispenseFood() 
        {
            if (foodNetTrader == null)
                return null;
            foodNetTrader.IsOn = foodNetTrader.CanBeOn;
            FoodNet net = foodNetTrader.FoodNet;
            if (!foodNetTrader.IsOn || !foodNetTrader.TransmitsFoodNow ||
                !HasNutritionForDispense)
                return null;

            using (var transfer = new ClassicFoodNetTransaction(net))
            {
                if (!transfer.IsValid)
                    return null;
                // Wait for a complete portion; record its actual representable
                // debit, including float rounding, in the per-item serving.
                Thing serving = ThingMaker.MakeThing(DispensableDef);
                bool committed = false;
                try
                {
                    var metadata = (serving as ThingWithComps)?.AllComps
                        .OfType<INetworkFoodServing>().FirstOrDefault();
                    ClassicFoodPortion portion;
                    if (metadata == null ||
                        !transfer.TryWithdrawNutrition(net, nutritionPerDispense, out portion) ||
                        !InitializeServing(metadata, portion) ||
                        !metadata.TryInitializePreservation(
                            portion.Preservation))
                        return null;
                    CompIngredients ingredients = serving.TryGetComp<CompIngredients>();
                    if (ingredients != null && portion.Preservation.Ingredients != null)
                        foreach (ThingDef ingredient in portion.Preservation.Ingredients)
                            if (ingredient != null)
                                ingredients.RegisterIngredient(ingredient);
                    transfer.Commit();
                    committed = true;
                }
                finally
                {
                    if (!committed && !serving.Destroyed)
                        serving.Destroy(DestroyMode.Vanish);
                }
                if (Spawned && def.building?.soundDispense != null)
                    def.building.soundDispense.PlayOneShot(new TargetInfo(Position, Map, false));
                return serving;
            }
        }

        // Used by meal selection before a job commits to this source. The
        // transaction computes the same representable debit as collection,
        // but never withdraws it during preview.
        public bool TryPreviewMeal(float nutrition,
            out ClassicFoodPortion portion)
        {
            portion = default(ClassicFoodPortion);
            FoodNet net;
            if (!TryGetOperationalMealNet(out net) ||
                !FoodNetStorage_ThingComp.PositiveFinite(nutrition))
                return false;
            using (var transfer = new ClassicFoodNetTransaction(net))
                return transfer.IsValid && transfer.TryPreviewExactNutrition(
                    net, nutrition, out portion);
        }

        // Exact, whole-meal collection for the shared serving path. The
        // supplied Def owns meal appearance and base food behavior; the saved
        // metadata owns the actual debit, density and preservation.
        public Thing TryDispenseMeal(ThingDef servingDef, float nutrition)
        {
            FoodNet net;
            if (!OrdinaryFoodIntake.IsProcessableFoodDef(servingDef) ||
                !FoodNetStorage_ThingComp.PositiveFinite(nutrition) ||
                !TryGetOperationalMealNet(out net))
                return null;
            using (var transfer = new ClassicFoodNetTransaction(net))
            {
                if (!transfer.IsValid) return null;
                Thing serving = ThingMaker.MakeThing(servingDef);
                bool committed = false;
                try
                {
                    var metadata = (serving as ThingWithComps)?.AllComps
                        .OfType<INetworkFoodServing>().FirstOrDefault();
                    var quantity = metadata as INetworkFoodServingQuantity;
                    ClassicFoodPortion portion;
                    if (metadata == null || quantity == null ||
                        !transfer.TryWithdrawExactNutrition(net,
                            nutrition, out portion) ||
                        !InitializeServing(metadata, portion) ||
                        !metadata.TryInitializePreservation(
                            portion.Preservation))
                        return null;

                    FoodPreservationStamp saved;
                    if (quantity.RepresentedNutrition != portion.Nutrition ||
                        metadata.FullnessPerNutrition !=
                            portion.FullnessToNutritionRatio ||
                        !metadata.TryReadPreservation(out saved) ||
                        !saved.Valid || saved.RemainingRotTicks <= 0f ||
                        saved.RemainingRotTicks !=
                            portion.Preservation.RemainingRotTicks ||
                        saved.PoisonFraction !=
                            portion.Preservation.PoisonFraction ||
                        saved.PoisonCause != portion.Preservation.PoisonCause ||
                        saved.AssumedFreshLegacy !=
                            portion.Preservation.AssumedFreshLegacy)
                        return null;
                    CompIngredients ingredients =
                        serving.TryGetComp<CompIngredients>();
                    if (ingredients != null &&
                        portion.Preservation.Ingredients != null)
                        foreach (ThingDef ingredient in
                            portion.Preservation.Ingredients)
                            if (ingredient != null)
                                ingredients.RegisterIngredient(ingredient);
                    transfer.Commit();
                    committed = true;
                }
                finally
                {
                    if (!committed && !serving.Destroyed)
                        serving.Destroy(DestroyMode.Vanish);
                }
                if (Spawned && def.building?.soundDispense != null)
                    def.building.soundDispense.PlayOneShot(
                        new TargetInfo(Position, Map, false));
                return serving;
            }
        }

        public bool TryGetOperationalMealNet(out FoodNet net)
        {
            net = foodNetTrader?.FoodNet;
            return Spawned && foodNetTrader != null &&
                foodNetTrader.CanBeOn &&
                foodNetTrader.TransmitsFoodNow &&
                net?.foodNetStorages != null;
        }

        private static bool InitializeServing(INetworkFoodServing metadata, ClassicFoodPortion portion)
        {
            var precise = metadata as IPreciseNetworkFoodServing;
            return precise != null
                ? precise.TryInitializeExactServing(portion.PreciseNutrition, portion.PreciseVolume, portion.SourceCreatedTick)
                : metadata.TryInitializeServing(portion.Nutrition, portion.FullnessToNutritionRatio);
        }

        // A collected portion must remain accounted for even if a job cannot
        // take it. A failed return leaves the original serving intact.
        public bool TryRecoverServing(Thing serving, Pawn carrier)
        {
            if (serving == null || serving.Destroyed)
                return false;
            var metadata = (serving as ThingWithComps)?.AllComps
                .OfType<INetworkFoodServing>().FirstOrDefault();
            FoodPreservationStamp stamp;
            var quantity = metadata as INetworkFoodServingQuantity;
            float nutrition = quantity == null ? 0f :
                quantity.RepresentedNutrition;
            float ratio = metadata == null ? 0f : metadata.FullnessPerNutrition;
            var precise = metadata as IPreciseNetworkFoodServing;
            double exactNutrition = precise?.PreciseNutritionPerItem ?? nutrition;
            double exactVolume = precise?.PreciseFullnessPerItem ?? (double)nutrition * ratio;
            FoodNet net = foodNetTrader?.FoodNet;
            if (metadata != null && metadata.TryReadPreservation(out stamp) &&
                FoodNetStorage_ThingComp.PositiveFinite(nutrition) &&
                FoodNetStorage_ThingComp.PositiveFinite(ratio) && net != null)
            {
                using (var transfer = new ClassicFoodNetTransaction(net))
                {
                    if (transfer.IsValid && transfer.TryDepositExact(net,
                        exactNutrition * serving.stackCount, exactVolume * serving.stackCount, stamp,
                        precise?.SourceCreatedTick ?? Find.TickManager?.TicksGame ?? 0))
                    {
                        transfer.Commit();
                        serving.Destroy(DestroyMode.Vanish);
                        return true;
                    }
                }
            }
            if (serving.Spawned || carrier?.carryTracker?.CarriedThing == serving)
                return true;
            if (carrier != null && carrier.Spawned &&
                TryPlaceServing(serving, carrier.Position, carrier.Map))
                return true;
            if (Spawned && TryPlaceServing(serving, Position, Map))
                return true;
            if (carrier?.inventory?.innerContainer != null &&
                carrier.inventory.innerContainer.TryAdd(serving))
                return true;
            Log.Error("[RimRound] Could not refund or place a classic faucet serving.");
            return false;
        }

        private static bool TryPlaceServing(Thing serving, IntVec3 cell,
            Map map)
        {
            if (map == null || !cell.IsValid || !cell.InBounds(map))
                return false;
            try
            {
                Thing placed;
                return GenPlace.TryPlaceThing(serving, cell, map,
                    ThingPlaceMode.Near, out placed) &&
                    placed != null && placed.Spawned;
            }
            catch (Exception exception)
            {
                Log.Error("[RimRound] Classic serving placement failed: " +
                    exception);
                return serving.Spawned;
            }
        }

        public bool HasNutritionForDispense
        {
            get
            {
                FoodNet net = foodNetTrader?.FoodNet;
                if (net?.foodNetStorages == null ||
                    !FoodNetStorage_ThingComp.PositiveFinite(nutritionPerDispense))
                    return false;
                double available = 0d;
                foreach (FoodNetStorage_ThingComp store in net.foodNetStorages.Distinct())
                {
                    if (store == null || !FoodNetStorage_ThingComp.Finite(store.Stored) ||
                        store.Stored < 0f || !FoodNetStorage_ThingComp.PositiveFinite(store.Capacity) ||
                        !FoodNetStorage_ThingComp.PositiveFinite(store.FullnessToNutritionRatio))
                        return false;
                    store.ReadPreservation();
                    available += store.PreciseNutrition;
                }
                return available >= nutritionPerDispense;
            }
        }

        public ThingDef DispensableDef 
        {
            get 
            {
                return Defs.ThingDefOf.RR_FeedingTubeFluid;
            }
        }

        public float nutritionPerDispense = 0.1f;


        public CompPowerTrader powerComp;

        public FoodNetTrader_ThingComp foodNetTrader;
    }
}
