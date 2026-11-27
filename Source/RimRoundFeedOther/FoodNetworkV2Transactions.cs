using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.FeedingTube;
using RimRound.Utilities;
using Verse;

namespace RimRound.FeedOther
{
    public sealed partial class FoodNetworkV2
    {
        public bool TryPreviewServingBatch(IReadOnlyList<FoodAmount> servings,
            bool allowConcentration, out IReadOnlyList<FoodBatchV2> sources)
        {
            List<FoodNetworkDrawSliceV2> ordered; FoodServingDrawPlan plan;
            return PlanServings(servings, allowConcentration, out ordered, out plan, out sources);
        }

        internal bool TryDrawServingBatch(IReadOnlyList<FoodAmount> servings, bool allowConcentration,
            Func<IReadOnlyList<FoodBatchV2>, bool> acceptOutput, out IReadOnlyList<FoodBatchV2> sources)
        {
            List<FoodNetworkDrawSliceV2> ordered; FoodServingDrawPlan plan;
            if (!PlanServings(servings, allowConcentration, out ordered, out plan, out sources)) return false;
            var before = ordered.Select(s => new FoodAmount(s.source.PreciseNutrition, s.source.PreciseFullness)).ToArray();
            var snapshot = new TransactionSnapshot(this);
            bool committed = false;
            try
            {
                if (acceptOutput != null && !acceptOutput(sources)) return false;
                if (tanks.Count == 0 || ordered.Where((s, i) => s.source.PreciseNutrition != before[i].Nutrition ||
                    s.source.PreciseFullness != before[i].Volume || !s.tank.state.batches.Contains(s.source)).Any()) return false;
                for (int i = 0; i < ordered.Count; i++)
                    ordered[i].source.SetAmounts(plan.Remainders[i].Nutrition, plan.Remainders[i].Volume);
                foreach (FoodNetworkTankBindingV2 tank in tanks) tank.state.RemoveEmptyBatches();
                SyncLegacyStorageComps();
                committed = true;
                return true;
            }
            finally { if (!committed) snapshot.Restore(); }
        }

        private bool PlanServings(IReadOnlyList<FoodAmount> servings, bool allowConcentration,
            out List<FoodNetworkDrawSliceV2> ordered, out FoodServingDrawPlan plan,
            out IReadOnlyList<FoodBatchV2> sources)
        {
            AgeTanks();
            sources = null; plan = null;
            ordered = tanks.SelectMany(t => (t.state.batches ?? new List<FoodBatchV2>())
                .Where(b => b != null && !b.Empty && b.Preservation.RemainingRotTicks > 0f)
                .Select(b => new FoodNetworkDrawSliceV2 { tank = t, source = b }))
                .OrderBy(s => s.source.createdTick).ThenBy(s => s.tank.building.thingIDNumber).ToList();
            if (!FoodServingMath.TryPlan(ordered.Select(s => new FoodAmount(s.source.PreciseNutrition,
                s.source.PreciseFullness)).ToArray(), servings, allowConcentration, out plan)) return false;
            var outputs = new List<FoodBatchV2>();
            List<FoodNetworkDrawSliceV2> sourceOrder = ordered;
            for (int i = 0; i < servings.Count; i++)
            {
                var used = plan.Slices[i].Select(s => sourceOrder[s.SourceIndex].source).ToList();
                if (used.Any(b => b.ingredients != null && b.ingredients.Any(d => !OrdinaryFoodIntake.IsProcessableFoodDef(d)))) return false;
                FoodPreservationStamp stamp = used[0].Preservation;
                foreach (FoodBatchV2 source in used.Skip(1)) stamp = FoodPreservationStamp.Combine(stamp, source.Preservation);
                var result = new FoodBatchV2(0f, 0f, used.SelectMany(b => b.ingredients ?? new List<ThingDef>()).Distinct(),
                    used.Min(b => b.createdTick));
                result.SetAmounts(plan.SourceAmounts[i].Nutrition, plan.SourceAmounts[i].Volume);
                result.SetPreservation(stamp); outputs.Add(result);
            }
            sources = outputs.AsReadOnly(); return true;
        }

        internal bool TryDistillTo(FoodNetworkV2 output, double nutrition, int grade)
        {
            if (output == null || ReferenceEquals(this, output) || !FoodServingMath.GradeValid(grade) ||
                !FoodServingMath.Positive(nutrition) || tanks.Any(t => output.tanks.Any(o => ReferenceEquals(t.state, o.state)))) return false;
            AgeTanks(); output.AgeTanks();
            var inputBefore = new TransactionSnapshot(this);
            var outputBefore = new TransactionSnapshot(output);
            bool committed = false;
            try
            {
                // A transfer is not a meal: retain each source chunk's deadline
                // instead of making the whole output inherit the oldest one.
                List<FoodNetworkDrawSliceV2> ordered;
                FoodServingDrawPlan plan;
                IReadOnlyList<FoodBatchV2> preview;
                if (!PlanServings(new[] { new FoodAmount(nutrition, nutrition / grade) },
                    true, out ordered, out plan, out preview)) return false;
                for (int i = 0; i < ordered.Count; i++)
                {
                    double taken = ordered[i].source.PreciseNutrition -
                        plan.Remainders[i].Nutrition;
                    if (taken <= 0d) continue;
                    FoodBatchV2 chunk = ordered[i].source.PortionPrecise(taken, taken / grade);
                    if (!output.TryStore(chunk)) return false;
                }
                for (int i = 0; i < ordered.Count; i++)
                    ordered[i].source.SetAmounts(plan.Remainders[i].Nutrition, plan.Remainders[i].Volume);
                foreach (FoodNetworkTankBindingV2 tank in tanks) tank.state.RemoveEmptyBatches();
                SyncLegacyStorageComps();
                committed = true; return true;
            }
            finally
            {
                if (!committed) { inputBefore.Restore(); outputBefore.Restore(); }
            }
        }

        // Both transfer sides restore original batches rather than guessing a
        // refund into a possibly full or rearranged network. Metadata is cloned.
        private sealed class TransactionSnapshot
        {
            private sealed class TankSnapshot
            {
                internal FoodNetworkTankBindingV2 Tank;
                internal List<FoodBatchV2> Batches;
                internal int PreservationTick;
                internal double SpoiledN, SpoiledV, PurgedN, PurgedV;
            }
            private readonly FoodNetworkV2 owner;
            private readonly List<TankSnapshot> saved;
            internal TransactionSnapshot(FoodNetworkV2 owner)
            {
                this.owner = owner;
                saved = owner.tanks.Select(t => new TankSnapshot { Tank = t,
                    Batches = t.state.batches.Select(b => b?.Copy()).ToList(),
                    PreservationTick = t.state.preservationLastTick,
                    SpoiledN = t.state.spoiledNutrition, SpoiledV = t.state.spoiledFullness,
                    PurgedN = t.state.purgedNutrition, PurgedV = t.state.purgedFullness }).ToList();
            }
            internal void Restore()
            {
                foreach (TankSnapshot entry in saved)
                {
                    entry.Tank.state.batches = entry.Batches.Select(b => b?.Copy()).ToList();
                    entry.Tank.state.preservationLastTick = entry.PreservationTick;
                    entry.Tank.state.spoiledNutrition = entry.SpoiledN; entry.Tank.state.spoiledFullness = entry.SpoiledV;
                    entry.Tank.state.purgedNutrition = entry.PurgedN; entry.Tank.state.purgedFullness = entry.PurgedV;
                }
                owner.SyncLegacyStorageComps();
            }
        }
    }
}
