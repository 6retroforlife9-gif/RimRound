using RimRound.FeedingTube.Comps;
using RimRound.FeedingTube.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.FeedingTube
{
    public class FoodNet
    {
        static int currentID;
        //Returns a positive number. Production per tick.

        public float Stored 
        {
            get 
            {
                double volume = foodNetStorages.Where(x =>
                    FoodNetStorage_ThingComp.PositiveFinite(x.Stored))
                    .Sum(x => (double)x.Stored);
                return (float)Math.Min(volume, float.MaxValue);
            } 
        }

        public float StorageCapacity
        {
            get 
            {
                double capacity = foodNetStorages.Where(x =>
                    FoodNetStorage_ThingComp.PositiveFinite(x.Capacity))
                    .Sum(x => (double)x.Capacity);
                return (float)Math.Min(capacity, float.MaxValue);
            }
        }

        public FoodNet(IEnumerable<FoodTransmitter_ThingComp> connectors, Map map)
        {
            this.netGrid = new BoolGrid(map);
            this.id = currentID++;
            
            foreach (FoodTransmitter_ThingComp x in connectors) 
            {
                AddNode(x);
            }
        }

        public string NetInfo 
        {
            get 
            {
                return "RR_FT_NetInfo".Translate(Stored.ToString("F1"), StorageCapacity.ToString("F1"));
            }
        }


        public void AddNode(FoodTransmitter_ThingComp x) 
        {
            x.FoodNet = this;

            foodTransmitters.Add(x);

            if (x is FoodNetStorage_ThingComp a)
                foodNetStorages.Add(a);

            if (x is FoodNetTrader_ThingComp b)
                foodNetTrader.Add(b);

            foreach (IntVec3 y in x.parent.OccupiedRect().Cells)
            {
                netGrid.Set(y, true);
            }
        }

        public void Tick() 
        {
            foreach (var x in foodNetTrader)
            {
                if (!x.CanBeOn)
                    x.IsOn = false;
            }
        }


        /// <returns>A positive number representing the amount that wasn't successfully drained.</returns>
        public float Drain(float amountToDrain) 
        {
            if (!FoodNetStorage_ThingComp.PositiveFinite(amountToDrain))
                return amountToDrain;

            List<FoodNetStorage_ThingComp> stores = foodNetStorages.Where(x =>
                FoodNetStorage_ThingComp.PositiveFinite(x.Stored)).ToList();
            if (stores.Any(x => !FoodNetStorage_ThingComp.PositiveFinite(
                    x.FullnessToNutritionRatio)))
                return amountToDrain;
            double total = stores.Sum(x => (double)x.Stored);
            if (total <= 0d)
                return amountToDrain;

            // Plan every proportional debit first. If a vat cannot represent
            // its share, draw nothing rather than omitting that constituent.
            // Float rounding can still perturb the proportions slightly.
            double target = Math.Min((double)amountToDrain, total);
            var plan = new List<KeyValuePair<FoodNetStorage_ThingComp, float>>();
            foreach (FoodNetStorage_ThingComp store in stores)
            {
                float share = FoodNetStorage_ThingComp.FloorToFloat(
                    target * store.Stored / total);
                if (share <= 0f || FoodNetStorage_ThingComp.CeilToFloat(
                        Math.Max(0d, (double)store.Stored - share)) >= store.Stored)
                    return amountToDrain;
                plan.Add(new KeyValuePair<FoodNetStorage_ThingComp, float>(store, share));
            }

            double drained = 0d;
            foreach (var portion in plan)
            {
                float before = portion.Key.Stored;
                portion.Key.Subtract(portion.Value);
                drained += (double)before - portion.Key.Stored;
            }
            return FoodNetStorage_ThingComp.CeilToFloat(
                Math.Max(0d, (double)amountToDrain - drained));
        }

        //returns 
        /// <returns>A positive number representing the amount that was not filled into anything.</returns>
        public float Fill(float amountToFill, float ftnRatio)
        {
            if (!FoodNetStorage_ThingComp.PositiveFinite(amountToFill) ||
                !FoodNetStorage_ThingComp.PositiveFinite(ftnRatio))
                return amountToFill;

            List<FoodNetStorage_ThingComp> stores = foodNetStorages.Where(x =>
                FoodNetStorage_ThingComp.PositiveFinite(x.Capacity) &&
                FoodNetStorage_ThingComp.Finite(x.Stored) &&
                x.Stored >= 0f && x.Stored < x.Capacity &&
                (x.Stored == 0f || FoodNetStorage_ThingComp.PositiveFinite(
                    x.FullnessToNutritionRatio))).ToList();
            double free = stores.Sum(x => (double)x.Capacity - x.Stored);
            if (free <= 0d)
                return amountToFill;

            double target = Math.Min((double)amountToFill, free);
            double accepted = 0d;
            foreach (FoodNetStorage_ThingComp store in stores)
            {
                float share = FoodNetStorage_ThingComp.FloorToFloat(
                    target * ((double)store.Capacity - store.Stored) / free);
                float before = store.Stored;
                store.Add(share, ftnRatio);
                accepted += (double)store.Stored - before;
            }
            return FoodNetStorage_ThingComp.CeilToFloat(
                Math.Max(0d, (double)amountToFill - accepted));
        }


        public void DestroyNet() 
        {
            foreach (FoodTransmitter_ThingComp comp in this.foodTransmitters) 
            {
                comp.FoodNet = null;
            }
        }

        public int id;

        public List<FoodNetStorage_ThingComp> foodNetStorages = new List<FoodNetStorage_ThingComp>();
        public List<FoodNetTrader_ThingComp> foodNetTrader = new List<FoodNetTrader_ThingComp>();
        public List<FoodTransmitter_ThingComp> foodTransmitters = new List<FoodTransmitter_ThingComp>();

        public BoolGrid netGrid;

        public float FullnessToNutritionRatio 
        {
            get 
            {
                double volume = 0d;
                double nutrition = 0d;
                foreach (FoodNetStorage_ThingComp storage in foodNetStorages)
                {
                    if (!FoodNetStorage_ThingComp.PositiveFinite(storage.Stored))
                        continue;
                    if (!FoodNetStorage_ThingComp.PositiveFinite(
                            storage.FullnessToNutritionRatio))
                        return 0f;
                    volume += storage.Stored;
                    nutrition += (double)storage.Stored /
                        storage.FullnessToNutritionRatio;
                }
                double ratio = volume / nutrition;
                return nutrition > 0d && ratio <= float.MaxValue &&
                    FoodNetStorage_ThingComp.PositiveFinite((float)ratio)
                    ? (float)ratio : 0f;
            }
        }

        public float NutritionToFullnessRatio
        {
            get 
            {
                float ratio = FullnessToNutritionRatio;
                if (ratio <= 0f)
                    return 0;
                float reciprocal = 1f / ratio;
                return FoodNetStorage_ThingComp.PositiveFinite(reciprocal)
                    ? reciprocal : 0f;
            }
        }
    }
}
