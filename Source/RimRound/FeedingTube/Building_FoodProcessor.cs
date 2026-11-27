using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.FeedingTube.Comps;
using RimRound.FeedingTube.Utilities;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.FeedingTube
{
    public class Building_FoodProcessor : Building
    {
        private FoodNetTrader_ThingComp trader;
        private List<IntVec3> cachedAdjCells;
        private int nextProcessTick;

        // Both the classic operation and the Food Network v2 patch use this
        // saved deadline. A meal larger than one cycle cannot earn idle credit.
        public bool ReadyForProcessing =>
            Find.TickManager != null &&
            Find.TickManager.TicksGame >= nextProcessTick;

        public void RecordProcessedNutrition(float nutrition,
            float nutritionPerCycle)
        {
            if (!FoodNetStorage_ThingComp.PositiveFinite(nutrition) ||
                !FoodNetStorage_ThingComp.PositiveFinite(nutritionPerCycle) ||
                Find.TickManager == null)
                return;
            int now = Find.TickManager.TicksGame;
            // Divide in the same float precision as the saved nutrition and
            // cycle budget: .3f/.1f is three cycles, not a rounded-up fourth.
            float cycleRatio = nutrition / nutritionPerCycle;
            int cycles = (int)Math.Min(int.MaxValue / 60,
                Math.Max(1d, Math.Ceiling(cycleRatio)));
            nextProcessTick = now + Math.Min(int.MaxValue - now,
                cycles * 60);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            trader = GetComp<FoodNetTrader_ThingComp>();
            cachedAdjCells = null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextProcessTick, "classicNextProcessTick", 0);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            yield return new Command_Action
            {
                Disabled = !GenConstruct.CanPlaceBlueprintAt(
                    Defs.ThingDefOf.RR_Hopper, Position + new IntVec3(2, 0, 0),
                    Rot4.West, Map),
                defaultLabel = "Build Hopper",
                icon = Widgets.GetIconFor(Defs.ThingDefOf.RR_Hopper),
                action = delegate
                {
                    GenConstruct.PlaceBlueprintForBuild(Defs.ThingDefOf.RR_Hopper,
                        Position + new IntVec3(2, 0, 0), Map, Rot4.West,
                        Faction, null);
                }
            };
        }

        protected override void Tick()
        {
            base.Tick();
            if (FeedingTubeUtility.IsHashIntervalTick(60))
                ProcessFood();
        }

        private void ProcessFood()
        {
            if (!ReadyForProcessing || trader == null || !trader.IsOn ||
                !trader.CanBeOn || !trader.TransmitsFoodNow ||
                trader.FoodNet == null)
                return;
            Thing food = TryGetFoodInHopper();
            if (food == null || food.stackCount <= 0 || !food.IngestibleNow ||
                !OrdinaryFoodIntake.IsProcessableFood(food) || !SpecialistContentPolicy.Allows(food))
                return;
            float perItemNutrition = FoodUtility.NutritionForEater(null, food);
            float ratio = GetNutritionDensity(food);
            FoodPreservationStamp preservation =
                FoodPreservationStamp.FromFood(food);
            if (!FoodNetStorage_ThingComp.PositiveFinite(perItemNutrition) ||
                !FoodNetStorage_ThingComp.PositiveFinite(ratio) ||
                !preservation.Valid ||
                preservation.RemainingRotTicks <= 0f)
                return;
            double perItemVolume = (double)perItemNutrition * ratio;
            if (!(perItemVolume > 0d) || perItemVolume > float.MaxValue)
                return;

            FoodNet net = trader.FoodNet;
            using (var transaction = new ClassicFoodNetTransaction(net))
            {
                if (!transaction.IsValid)
                    return;
                double room = net.foodNetStorages.Sum(vat =>
                    Math.Max(0d, (double)vat.Capacity - vat.Stored));
                if (room < perItemVolume)
                    return;
                // One large meal is allowed, then repays its full nutrition
                // cost through a saved cooldown. Idle time earns no credit.
                const float nutritionPerCycle = 0.1f;
                int byCycle = Math.Max(1, (int)Math.Min(int.MaxValue,
                    Math.Floor((double)nutritionPerCycle /
                        perItemNutrition)));
                int byRoom = (int)Math.Min(int.MaxValue,
                    Math.Floor(room / perItemVolume));
                int count = Math.Min(food.stackCount, Math.Min(byCycle, byRoom));
                if (count <= 0)
                    return;
                double totalNutrition = (double)count * perItemNutrition;
                if (totalNutrition > float.MaxValue ||
                    !transaction.TryDepositNutrition(net,
                        (float)totalNutrition, ratio, preservation))
                    return;

                Thing consumed = food.SplitOff(count);
                if (consumed == null)
                    return;
                transaction.Commit();
                consumed.Destroy(DestroyMode.Vanish);
                RecordProcessedNutrition((float)totalNutrition,
                    nutritionPerCycle);
            }
        }

        private static float GetNutritionDensity(Thing food)
        {
            var represented = (food as ThingWithComps)?.AllComps
                .OfType<IBodyFoodVolume>().FirstOrDefault(comp =>
                    FoodNetStorage_ThingComp.Finite(
                        comp.FullnessPerNutrition) &&
                    comp.FullnessPerNutrition >= 0f);
            if (represented != null)
                return represented.FullnessPerNutrition;
            var density = food.TryGetComp<ThingComp_FoodItems_NutritionDensity>();
            return density == null ? 1f : density.Props.fullnessToNutritionRatio;
        }

        private List<IntVec3> AdjCellsCardinalInBounds
        {
            get
            {
                if (cachedAdjCells == null)
                    cachedAdjCells = GenAdj.CellsAdjacentCardinal(this)
                        .Where(cell => cell.InBounds(Map)).ToList();
                return cachedAdjCells;
            }
        }

        private bool IsAcceptableFeedstock(ThingDef def)
        {
            return OrdinaryFoodIntake.IsProcessableFoodDef(def) && SpecialistContentPolicy.Allows(def) && def.IsNutritionGivingIngestible &&
                def.ingestible.preferability != FoodPreferability.Undefined &&
                (def.ingestible.foodType & FoodTypeFlags.Plant) != FoodTypeFlags.Plant &&
                (def.ingestible.foodType & FoodTypeFlags.Tree) != FoodTypeFlags.Tree;
        }

        private Thing TryGetFoodInHopper()
        {
            foreach (IntVec3 cell in AdjCellsCardinalInBounds)
            {
                Thing food = null;
                bool hopper = false;
                foreach (Thing thing in cell.GetThingList(Map))
                {
                    if (IsAcceptableFeedstock(thing.def) && OrdinaryFoodIntake.IsProcessableFood(thing))
                        food = thing;
                    if (thing.def == ThingDefOf.Hopper ||
                        thing.def == Defs.ThingDefOf.RR_Hopper)
                        hopper = true;
                }
                if (hopper && food != null)
                    return food;
            }
            return null;
        }
    }
}
