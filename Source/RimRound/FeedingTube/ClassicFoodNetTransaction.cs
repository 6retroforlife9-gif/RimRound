using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.FeedingTube.Comps;
using RimRound.Utilities;
using Verse;

namespace RimRound.FeedingTube
{
    public struct ClassicFoodPortion
    {
        public double PreciseNutrition { get; internal set; }
        public double PreciseVolume { get; internal set; }
        public float Nutrition => (float)PreciseNutrition;
        public float Volume => (float)PreciseVolume;
        public float FullnessToNutritionRatio => PreciseNutrition > 0d
            ? (float)(PreciseVolume / PreciseNutrition) : 1f;
        public int SourceCreatedTick { get; internal set; }
        public FoodPreservationStamp Preservation { get; internal set; }
    }

    // One authoritative owner per vat. A transaction snapshots its exact
    // amounts and provenance; every failed operation restores those bits.
    public sealed class ClassicFoodNetTransaction : IDisposable
    {
        private sealed class VatState
        {
            internal FoodNetStorage_ThingComp Vat;
            internal FoodNetStorage_ThingComp.PreciseState Saved;
            internal double Nutrition, Volume;
            internal FoodPreservationStamp Preservation;
            internal int CreatedTick;
            internal VatState(FoodNetStorage_ThingComp vat)
            {
                Vat = vat; Saved = vat.CapturePreciseState();
                Nutrition = vat.PreciseNutrition; Volume = vat.PreciseVolume;
                Preservation = vat.ReadPreservation(); CreatedTick = vat.SourceCreatedTick;
            }
        }
        private readonly Dictionary<FoodNet, List<VatState>> networks = new Dictionary<FoodNet, List<VatState>>();
        private bool committed, disposed;
        public bool IsValid { get; private set; }

        public ClassicFoodNetTransaction(params FoodNet[] nets)
        {
            if (nets == null || nets.Length == 0) return;
            var owners = new Dictionary<FoodNetStorage_ThingComp, FoodNet>();
            foreach (FoodNet net in nets)
            {
                if (net?.foodNetStorages == null || net.foodNetStorages.Count == 0) return;
                if (networks.ContainsKey(net)) continue;
                var states = new List<VatState>();
                foreach (FoodNetStorage_ThingComp vat in net.foodNetStorages.Distinct())
                {
                    if (vat == null || !FoodNetStorage_ThingComp.PositiveFinite(vat.Capacity)) return;
                    FoodNet owner;
                    if (owners.TryGetValue(vat, out owner) && owner != net) return;
                    var state = new VatState(vat);
                    if (!new FoodAmount(state.Nutrition, state.Volume).Valid ||
                        state.Volume > vat.Capacity && !FoodServingMath.Same(state.Volume, vat.Capacity)) return;
                    owners[vat] = net; states.Add(state);
                }
                networks.Add(net, states);
            }
            IsValid = true;
        }

        public bool TryWithdrawNutrition(FoodNet net, float requestedNutrition, out ClassicFoodPortion portion)
        {
            portion = default(ClassicFoodPortion);
            List<VatState> originals;
            if (!Ready(net, out originals) || !FoodServingMath.Positive(requestedNutrition)) return false;
            double available = Capture(originals).Sum(s => s.Nutrition);
            double wanted = Math.Min(requestedNutrition, available);
            return wanted > 0d && TryWithdrawExactNutrition(net, wanted, out portion);
        }
        public bool TryPreviewExactNutrition(FoodNet net, float nutrition, out ClassicFoodPortion portion) =>
            TryPreviewExactNutrition(net, (double)nutrition, out portion);
        public bool TryPreviewExactNutrition(FoodNet net, double nutrition, out ClassicFoodPortion portion)
        {
            portion = default(ClassicFoodPortion);
            IReadOnlyList<ClassicFoodPortion> portions;
            if (!TryPreviewServingBatch(net, new[] { new FoodAmount(nutrition, double.MaxValue) },
                true, out portions)) return false;
            portion = portions[0]; return true;
        }
        public bool TryWithdrawExactNutrition(FoodNet net, float nutrition, out ClassicFoodPortion portion) =>
            TryWithdrawExactNutrition(net, (double)nutrition, out portion);
        public bool TryWithdrawExactNutrition(FoodNet net, double nutrition, out ClassicFoodPortion portion)
        {
            portion = default(ClassicFoodPortion);
            IReadOnlyList<ClassicFoodPortion> portions;
            if (!TryWithdrawServingBatch(net, new[] { new FoodAmount(nutrition, double.MaxValue) },
                true, null, out portions)) return false;
            portion = portions[0]; return true;
        }

        public bool TryPreviewServingBatch(FoodNet net, IReadOnlyList<FoodAmount> servings,
            bool allowConcentration, out IReadOnlyList<ClassicFoodPortion> portions)
        {
            List<VatState> before; FoodServingDrawPlan plan;
            return Plan(net, servings, allowConcentration, out before, out plan, out portions);
        }
        public bool TryWithdrawServingBatch(FoodNet net, IReadOnlyList<FoodAmount> servings,
            bool allowConcentration, Func<IReadOnlyList<ClassicFoodPortion>, bool> acceptOutput,
            out IReadOnlyList<ClassicFoodPortion> portions)
        {
            List<VatState> before; FoodServingDrawPlan plan;
            if (!Plan(net, servings, allowConcentration, out before, out plan, out portions)) return false;
            if (acceptOutput != null && !acceptOutput(portions)) return false;
            // An output callback may reject or change availability. Never apply
            // a stale debit after it; the caller retains output rollback ownership.
            if (before.Any(s => s.Vat.PreciseNutrition != s.Nutrition || s.Vat.PreciseVolume != s.Volume)) return false;
            try
            {
                for (int i = 0; i < before.Count; i++)
                    if (!before[i].Vat.SetPreciseAmounts(plan.Remainders[i].Nutrition,
                        plan.Remainders[i].Volume, before[i].CreatedTick))
                    { Restore(before); return false; }
                return true;
            }
            catch { Restore(before); throw; }
        }
        private bool Plan(FoodNet net, IReadOnlyList<FoodAmount> servings, bool allowConcentration,
            out List<VatState> before, out FoodServingDrawPlan plan,
            out IReadOnlyList<ClassicFoodPortion> portions)
        {
            before = null; plan = null; portions = null;
            List<VatState> originals;
            if (!Ready(net, out originals)) return false;
            before = Capture(originals).OrderBy(s => s.CreatedTick).ThenBy(s => s.Vat.parent?.thingIDNumber ?? 0).ToList();
            if (before.Any(s => s.Nutrition > 0d && (!s.Preservation.Valid || s.Preservation.RemainingRotTicks <= 0f))) return false;
            if (!FoodServingMath.TryPlan(before.Select(s => new FoodAmount(s.Nutrition, s.Volume)).ToArray(),
                servings, allowConcentration, out plan)) return false;
            var outputs = new List<ClassicFoodPortion>();
            for (int i = 0; i < servings.Count; i++)
            {
                FoodPreservationStamp stamp = FoodPreservationStamp.Fresh;
                bool any = false; int created = int.MaxValue;
                foreach (FoodDrawSlice slice in plan.Slices[i])
                {
                    VatState source = before[slice.SourceIndex];
                    if (source.Preservation.Ingredients != null &&
                        source.Preservation.Ingredients.Any(d => !OrdinaryFoodIntake.IsProcessableFoodDef(d))) return false;
                    stamp = any ? FoodPreservationStamp.Combine(stamp, source.Preservation) : source.Preservation;
                    created = Math.Min(created, source.CreatedTick); any = true;
                }
                outputs.Add(new ClassicFoodPortion { PreciseNutrition = plan.SourceAmounts[i].Nutrition,
                    PreciseVolume = plan.SourceAmounts[i].Volume, Preservation = stamp,
                    SourceCreatedTick = created == int.MaxValue ? 0 : created });
            }
            portions = outputs.AsReadOnly(); return true;
        }

        public bool TryDepositNutrition(FoodNet net, float nutrition, float ratio) =>
            TryDepositNutrition(net, nutrition, ratio, FoodPreservationStamp.LegacyGrace);
        public bool TryDepositNutrition(FoodNet net, float nutrition, float ratio, FoodPreservationStamp stamp) =>
            FoodServingMath.Positive(ratio) && TryDepositExact(net, nutrition, (double)nutrition * ratio,
                stamp, Find.TickManager?.TicksGame ?? 0);
        public bool TryDepositExact(FoodNet net, double nutrition, double volume,
            FoodPreservationStamp stamp, int createdTick)
        {
            List<VatState> originals;
            if (!Ready(net, out originals) || !FoodServingMath.Positive(nutrition) ||
                !FoodServingMath.Positive(volume) || !stamp.Valid || stamp.RemainingRotTicks <= 0f) return false;
            List<VatState> before = Capture(originals).OrderBy(s => s.Volume / s.Vat.Capacity)
                .ThenBy(s => s.Vat.parent?.thingIDNumber ?? 0).ToList();
            double free = before.Sum(s => Math.Max(0d, s.Vat.Capacity - s.Volume));
            if (volume > free && !FoodServingMath.Same(volume, free)) return false;
            double remainingN = nutrition, remainingV = volume;
            var after = new List<FoodAmount>();
            foreach (VatState state in before)
            {
                double room = Math.Max(0d, state.Vat.Capacity - state.Volume);
                double takeV = remainingV <= room || FoodServingMath.Same(remainingV, room) ? remainingV : room;
                double takeN = takeV == remainingV ? remainingN : remainingN * (takeV / remainingV);
                if (takeV <= 0d) takeN = 0d;
                after.Add(new FoodAmount(state.Nutrition + takeN, state.Volume + takeV));
                remainingN -= takeN; remainingV -= takeV;
            }
            if (remainingN > 0d || remainingV > 0d) return false;
            try
            {
                for (int i = 0; i < before.Count; i++)
                {
                    VatState state = before[i];
                    if (after[i].Volume == state.Volume) continue;
                    if (!state.Vat.SetPreciseAmounts(after[i].Nutrition, after[i].Volume,
                        state.Nutrition > 0d ? Math.Min(state.CreatedTick, createdTick) : createdTick))
                    { Restore(before); return false; }
                    state.Vat.SetPreservation(state.Nutrition > 0d
                        ? FoodPreservationStamp.Combine(state.Preservation, stamp) : stamp);
                }
                return true;
            }
            catch { Restore(before); throw; }
        }
        public void Commit()
        {
            if (!IsValid || disposed) throw new InvalidOperationException("Invalid classic food transaction");
            committed = true;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!committed && IsValid) foreach (List<VatState> states in networks.Values) Restore(states);
        }
        private bool Ready(FoodNet net, out List<VatState> states)
        { states = null; return IsValid && !disposed && !committed && net != null && networks.TryGetValue(net, out states); }
        private static List<VatState> Capture(List<VatState> states) => states.Select(s => new VatState(s.Vat)).ToList();
        private static void Restore(List<VatState> states)
        { foreach (VatState state in states) state.Vat.RestorePreciseState(state.Saved); }
    }
}
