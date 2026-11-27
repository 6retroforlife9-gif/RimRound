using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.FeedingTube;
using RimRound.FeedingTube.Utilities;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimRound.FeedOther
{
    public sealed class FaucetMealRequest
    {
        public Pawn Recipient { get; }
        public FaucetMealPurpose Purpose { get; }
        public FaucetPortion Portion { get; }
        public int Count { get; }
        public FaucetMealRequest(Pawn recipient, FaucetMealPurpose purpose, FaucetPortion portion, int count)
        { Recipient = recipient; Purpose = purpose; Portion = portion; Count = count; }
    }
    public sealed class FaucetQuotedServing
    {
        public Pawn Recipient { get; }
        public FaucetMealPurpose Purpose { get; }
        public FaucetPortion Portion { get; }
        public double SourceNutrition { get; }
        public double SourceVolume { get; }
        internal FaucetQuotedServing(FaucetMealRequest request, FoodBatchV2 source)
        {
            Recipient = request.Recipient; Purpose = request.Purpose; Portion = request.Portion;
            SourceNutrition = source.PreciseNutrition; SourceVolume = source.PreciseFullness;
        }
    }
    public sealed class FaucetBatchQuote
    {
        public Building_FoodFaucet Faucet { get; }
        public Pawn Collector { get; }
        public IReadOnlyList<FaucetQuotedServing> Servings { get; }
        public double TotalNutrition => Servings.Sum(s => s.SourceNutrition);
        public double TotalVolume => Servings.Sum(s => s.Portion.Volume);
        public double TotalSourceVolume => Servings.Sum(s => s.SourceVolume);
        internal readonly IReadOnlyList<FaucetMealRequest> Requests;
        internal readonly object Network;
        internal readonly bool V2, ReserveAlreadyAuthorized;
        internal readonly string EssentialCareJobId;
        internal bool Collected;
        internal FaucetBatchQuote(Building_FoodFaucet faucet, Pawn collector,
            IReadOnlyList<FaucetMealRequest> requests, IList<FaucetQuotedServing> servings,
            object network, bool v2, bool reserveAlreadyAuthorized, string essentialCareJobId = null)
        {
            Faucet = faucet; Collector = collector; Requests = Array.AsReadOnly(requests.ToArray());
            Servings = Array.AsReadOnly(servings.ToArray()); Network = network;
            V2 = v2; ReserveAlreadyAuthorized = reserveAlreadyAuthorized;
            EssentialCareJobId = essentialCareJobId;
        }
    }
    public sealed class CollectedFaucetServing
    {
        public Pawn Recipient { get; }
        public FaucetMealPurpose Purpose { get; }
        public Thing Thing { get; }
        internal CollectedFaucetServing(FaucetQuotedServing quote, Thing thing)
        { Recipient = quote.Recipient; Purpose = quote.Purpose; Thing = thing; }
    }
    public sealed class CollectedFaucetBatch
    {
        public IReadOnlyList<CollectedFaucetServing> Servings { get; }
        internal CollectedFaucetBatch(IList<CollectedFaucetServing> servings)
        { Servings = Array.AsReadOnly(servings.ToArray()); }
    }

    [StaticConstructorOnStartup]
    public static partial class FaucetMealAcquisition
    {
        private static readonly Dictionary<long, int> failedUntil = new Dictionary<long, int>();
        private static readonly WeakReference failureGame = new WeakReference(null);
        static FaucetMealAcquisition() { CompFoodFaucetPortion.DensityStatusProvider = SupplyDescription; }
        private static void EnsureFailureGameContext()
        {
            Game game = Current.Game;
            if (ReferenceEquals(failureGame.Target, game)) return;
            failedUntil.Clear();
            failureGame.Target = game;
        }
        private static long FailureKey(Building_FoodFaucet faucet, Pawn eater) =>
            ((long)(faucet?.thingIDNumber ?? 0) << 32) | (uint)(eater?.thingIDNumber ?? 0);
        private static bool RetryingTooSoon(Building_FoodFaucet faucet, Pawn eater)
        {
            EnsureFailureGameContext();
            int until;
            return failedUntil.TryGetValue(FailureKey(faucet, eater), out until) &&
                until > (Find.TickManager?.TicksGame ?? 0) && until - (Find.TickManager?.TicksGame ?? 0) <= 120;
        }
        private static void NoteCollectionFailure(Building_FoodFaucet faucet, Pawn eater)
        {
            EnsureFailureGameContext();
            if (failedUntil.Count > 1024) failedUntil.Clear();
            failedUntil[FailureKey(faucet, eater)] = (Find.TickManager?.TicksGame ?? 0) + 120;
        }
        public static bool UsesPreparedServing(Pawn eater)
        {
            var body = eater?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body?.UsesBodyModel == true && body.RawAssignedBodyGoal != BodyGoal.Maintain &&
                body.RawAssignedBodyGoal != BodyGoal.Lose &&
                (body.Goal == BodyGoal.Gain || body.Goal == BodyGoal.ForceGain || body.ContinuousGain);
        }

        public static int ResolveSupplyGrade(Building_FoodFaucet faucet)
            => ResolveSupplyGrade(faucet, portion =>
            {
                FoodBatchV2 source;
                return TryPreviewServing(faucet, portion, out source);
            });

        internal static int ResolveSupplyGrade(Building_FoodFaucet faucet, Func<FaucetPortion, bool> available)
        {
            int selected = faucet?.TryGetComp<CompFoodFaucetPortion>()?.DensityOverride ?? 0;
            if (selected > 0) return selected;
            List<Building_NutrientDistillery> suppliers = DirectSuppliers(faucet);
            if (suppliers.Count == 1) return suppliers[0].TargetDensity;
            for (int grade = 5; grade >= 1; grade--)
            {
                if (available(FoodFaucetPortions.Get(FaucetPortionMode.Normal).WithDensity(grade))) return grade;
            }
            return 1; // explicitly displayed as unavailable when no grade fits
        }
        private static List<Building_NutrientDistillery> DirectSuppliers(Building_FoodFaucet faucet)
        {
            var result = new List<Building_NutrientDistillery>();
            if (faucet?.Spawned != true) return result;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(FoodNetworkV2Constants.DistillerDefName);
            if (def == null) return result;
            object target = SourceNetwork(faucet);
            if (target == null) return result;
            foreach (Building_NutrientDistillery distiller in faucet.Map.listerThings.ThingsOfDef(def).OfType<Building_NutrientDistillery>())
            {
                object input, output;
                if (FeedOtherMod.Settings.foodNetworkV2Enabled)
                {
                    FoodNetworkV2 i, o; FoodNetworkV2MachineUtility.GetDistillerNetworks(distiller, out i, out o);
                    input = i; output = o;
                }
                else
                {
                    FoodTransmitter_NetManager manager = FoodTransmitter_NetManager.For(faucet.Map);
                    input = manager?.FoodNetAt(FixedDistilleryGeometry.Input(distiller));
                    output = manager?.FoodNetAt(FixedDistilleryGeometry.Output(distiller));
                }
                if (ReferenceEquals(output, target) && input != null && !ReferenceEquals(input, output)) result.Add(distiller);
            }
            return result;
        }
        private static string SupplyDescription(Building_FoodFaucet faucet)
        {
            int grade = ResolveSupplyGrade(faucet);
            FoodBatchV2 preview;
            bool available = TryPreviewServing(faucet, FoodFaucetPortions.Get(FaucetPortionMode.Normal).WithDensity(grade), out preview);
            int selected = faucet?.TryGetComp<CompFoodFaucetPortion>()?.DensityOverride ?? 0;
            string reason = selected > 0 ? "RR_FaucetDensityOverride" :
                DirectSuppliers(faucet).Count == 1 ? "RR_FaucetDensitySupplyResolved" : "RR_FaucetDensitySupplyAmbiguous";
            return reason.Translate(grade) + "\n" + (available ? "RR_FaucetDensityReady" : "RR_FaucetDensityWaiting").Translate();
        }
        internal static object SourceNetwork(Building_FoodFaucet faucet)
        {
            if (faucet?.Spawned != true || !FoodNetworkV2MachineUtility.IsOperational(faucet)) return null;
            if (FeedOtherMod.Settings.foodNetworkV2Enabled) return FoodNetworkV2MachineUtility.NetworkFor(faucet);
            FoodNet net; return faucet.TryGetOperationalMealNet(out net) ? net : null;
        }
        private static FoodBatchV2 ToBatch(ClassicFoodPortion source)
        {
            var batch = new FoodBatchV2(0f, 0f, source.Preservation.Ingredients, source.SourceCreatedTick);
            batch.SetAmounts(source.PreciseNutrition, source.PreciseVolume); batch.SetPreservation(source.Preservation);
            return batch;
        }
        internal static bool PreviewSources(object network, IReadOnlyList<FoodAmount> amounts, out IReadOnlyList<FoodBatchV2> sources)
        {
            sources = null;
            var v2 = network as FoodNetworkV2;
            if (v2 != null) return v2.TryPreviewServingBatch(amounts, false, out sources);
            var classic = network as FoodNet;
            if (classic == null) return false;
            using (var transaction = new ClassicFoodNetTransaction(classic))
            {
                IReadOnlyList<ClassicFoodPortion> portions;
                if (!transaction.IsValid || !transaction.TryPreviewServingBatch(classic, amounts, false, out portions)) return false;
                sources = Array.AsReadOnly(portions.Select(ToBatch).ToArray()); return true;
            }
        }
        internal static bool TryPreviewServing(Building_FoodFaucet faucet, FaucetPortion portion, out FoodBatchV2 batch)
        {
            batch = null;
            if (portion?.FoodDef == null || !portion.Unlocked || !FoodServingMath.GradeValid(portion.Density)) return false;
            IReadOnlyList<FoodBatchV2> sources;
            if (!PreviewSources(SourceNetwork(faucet), new[] { new FoodAmount(portion.PreciseNutrition, portion.Volume) }, out sources)) return false;
            batch = sources[0].PortionPrecise(sources[0].PreciseNutrition, portion.Volume); return true;
        }

        public static bool TryQuoteBatch(Building_FoodFaucet faucet, Pawn collector,
            IReadOnlyList<FaucetMealRequest> requests, out FaucetBatchQuote quote, out string failure,
            bool reserveAlreadyAuthorized = false)
        {
            quote = null; failure = "invalid-request";
            if (faucet?.Spawned != true || collector?.Spawned != true || collector.Dead ||
                collector.Map != faucet.Map || collector.inventory?.innerContainer == null ||
                requests == null || requests.Count == 0) return false;
            var flat = new List<FaucetMealRequest>();
            string essentialCareJobId = null;
            foreach (FaucetMealRequest request in requests)
            {
                if (request?.Recipient == null || request.Recipient.Dead || request.Recipient.MapHeld != faucet.Map ||
                    request.Recipient.needs?.food == null || request.Portion?.FoodDef == null ||
                    !request.Portion.Unlocked || FoodFaucetPortions.Get(request.Portion.Mode)?.Unlocked != true ||
                    request.Count <= 0 || request.Count > 256 ||
                    !FoodServingMath.GradeValid(request.Portion.Density)) return false;
                bool essentialCare = MealSittingComponent.EssentialCareMode(collector, request.Recipient);
                if (essentialCare)
                {
                    if (requests.Count != 1 || request.Count != 1 || request.Purpose != FaucetMealPurpose.Routine ||
                        request.Portion.Mode != FaucetPortionMode.Normal || request.Portion.Density != 1 ||
                        !MealSittingComponent.CanAcquireEssentialCare(collector, request.Recipient))
                    { failure = "essential-care-request-changed"; return false; }
                    essentialCareJobId = collector.CurJob.GetUniqueLoadID();
                }
                bool prepared = !essentialCare && (UsesPreparedServing(request.Recipient) || request.Purpose==FaucetMealPurpose.Ritual);
                bool standardFallback = false;
                if (prepared && request.Portion.Mode == FaucetPortionMode.Normal && request.Portion.Density == 1)
                {
                    Selection preferred;
                    // Revalidate the same preference at quote/commit. Retry
                    // cooldown and current carry contents are not evidence that
                    // the source lacks a preferred inventory serving.
                    standardFallback = !TrySelectPreferred(faucet, collector, request.Recipient,
                        request.Purpose, out preferred, reserveAlreadyAuthorized, false);
                }
                if ((!prepared && (request.Portion.Mode != FaucetPortionMode.Normal || request.Portion.Density != 1 || request.Count != 1)) ||
                    (prepared && request.Purpose!=FaucetMealPurpose.Ritual && request.Portion.Density != ResolveSupplyGrade(faucet) && !standardFallback))
                { failure = "recipient-serving-changed"; return false; }
                for (int i = 0; i < request.Count; i++) flat.Add(request);
            }
            if (flat.Count > 256) return false;
            if (flat.GroupBy(r => r.Recipient).Any(g => !UsesPreparedServing(g.Key) && g.Any(r=>r.Purpose!=FaucetMealPurpose.Ritual) && g.Count() != 1)) return false;
            object network = SourceNetwork(faucet);
            IReadOnlyList<FoodBatchV2> sources;
            if (!PreviewSources(network, flat.Select(r => new FoodAmount(r.Portion.PreciseNutrition, r.Portion.Volume)).ToArray(), out sources))
            { failure = "supply-not-ready-for-grade-or-count"; return false; }
            var servings = new List<FaucetQuotedServing>();
            for (int i = 0; i < flat.Count; i++)
            {
                FaucetMealRequest request = flat[i];
                FoodBatchV2 output = sources[i].PortionPrecise(sources[i].PreciseNutrition, request.Portion.Volume);
                Thing preview = FoodNetworkV2ServingUtility.MakeServing(output, request.Portion.FoodDef, request.Portion.FoodDef.defName);
                if (preview == null) { failure = "invalid-serving-metadata"; return false; }
                try
                {
                    if (!Permitted(request.Recipient, collector, preview, request.Purpose, faucet, reserveAlreadyAuthorized))
                    { failure = "recipient-refused"; return false; }
                }
                finally { preview.Destroy(DestroyMode.Vanish); }
                servings.Add(new FaucetQuotedServing(request, sources[i]));
            }
            foreach (var group in servings.GroupBy(s => s.Recipient))
            {
                if(group.All(s=>s.Purpose==FaucetMealPurpose.Ritual))continue; // The ritual plans its entire fixed allowance before collection.
                var body = group.Key.TryGetComp<FullnessAndDietStats_ThingComp>();
                if (body?.UsesBodyModel != true || group.Count() == 1) continue;
                float target = group.Any(s => s.Purpose == FaucetMealPurpose.Fatten)
                    ? PrisonerFatteningFoodPatch.FattenFullnessTarget(group.Key, body)
                    : group.Any(s => s.Purpose == FaucetMealPurpose.Social)
                        ? FeedOtherUtility.FeedingTarget(group.Key) : BodyFeedingPolicy.ComfortableGainTarget(group.Key);
                double room = Math.Max(0d, target - body.OccupiedVolume);
                // Only the last real item may exceed the target, and at least
                // 75% of that item must remain useful. Ingest owns actual waste.
                if (!FoodServingMath.FitsBatchWithFinalWaste(room + FoodNetworkV2Constants.Epsilon,
                    group.Select(s => s.Portion.Volume * body.FullnessGainedMultiplier).ToArray()))
                { failure = "recipient-batch-does-not-fit"; return false; }
            }
            quote = new FaucetBatchQuote(faucet, collector, requests, servings, network,
                FeedOtherMod.Settings.foodNetworkV2Enabled, reserveAlreadyAuthorized, essentialCareJobId);
            failure = null; return true;
        }

        private static bool EssentialCareQuoteLive(FaucetBatchQuote quote) =>
            quote.EssentialCareJobId == null ||
            (quote.Requests.Count == 1 && quote.Collector?.CurJob?.GetUniqueLoadID() == quote.EssentialCareJobId &&
             MealSittingComponent.CanAcquireEssentialCare(quote.Collector, quote.Requests[0].Recipient));

        public static bool TryCollectBatch(FaucetBatchQuote quote, ThingOwner destination,
            out CollectedFaucetBatch collected, out string failure)
        {
            collected = null; failure = "invalid-collection-owner";
            if (quote == null || quote.Collected || quote.Collector == null || destination == null ||
                quote.Collector.Position != quote.Faucet?.InteractionCell) return false;
            bool inventory = quote.Collector.inventory?.innerContainer == destination;
            bool hand = quote.Collector.carryTracker?.innerContainer == destination &&
                quote.Collector.carryTracker.CarriedThing == null && quote.Servings.Count == 1 &&
                quote.Collector.carryTracker.AvailableStackSpace(quote.Servings[0].Portion.FoodDef) >= 1;
            if (!inventory && !hand) return false;
            if (!EssentialCareQuoteLive(quote))
            { failure = "essential-care-job-or-permission-changed"; return false; }
            FaucetBatchQuote current;
            if (quote.V2 != FeedOtherMod.Settings.foodNetworkV2Enabled ||
                !ReferenceEquals(quote.Network, SourceNetwork(quote.Faucet)))
            { failure = "source-network-changed"; return false; }
            if (!TryQuoteBatch(quote.Faucet, quote.Collector, quote.Requests, out current, out failure,
                quote.ReserveAlreadyAuthorized))
            {
                foreach (var request in quote.Requests) NoteCollectionFailure(quote.Faucet, request.Recipient);
                return false;
            }
            if (current.EssentialCareJobId != quote.EssentialCareJobId)
            { failure = "essential-care-mode-changed"; return false; }
            var created = new List<CollectedFaucetServing>();
            bool committed = false;
            try
            {
                Func<IReadOnlyList<FoodBatchV2>, bool> prepare = sources =>
                {
                    if (!EssentialCareQuoteLive(current)) return false;
                    for (int i = 0; i < current.Servings.Count; i++)
                    {
                        FaucetQuotedServing entry = current.Servings[i];
                        FoodBatchV2 output = sources[i].PortionPrecise(sources[i].PreciseNutrition, entry.Portion.Volume);
                        Thing thing = FoodNetworkV2ServingUtility.MakeServing(output, entry.Portion.FoodDef, entry.Portion.FoodDef.defName);
                        if (thing == null) return false;
                        created.Add(new CollectedFaucetServing(entry, thing));
                        // No merge: each quoted serving keeps its exact recipient
                        // identity and provenance until the session consumes it.
                        if (!destination.TryAdd(thing, false)) return false;
                    }
                    return true;
                };
                var amounts = current.Servings.Select(s => new FoodAmount(s.Portion.PreciseNutrition, s.Portion.Volume)).ToArray();
                IReadOnlyList<FoodBatchV2> drawn;
                if (current.V2)
                    committed = ((FoodNetworkV2)current.Network).TryDrawServingBatch(amounts, false, prepare, out drawn);
                else
                {
                    FoodNet net = (FoodNet)current.Network;
                    using (var transaction = new ClassicFoodNetTransaction(net))
                    {
                        IReadOnlyList<ClassicFoodPortion> portions;
                        committed = transaction.IsValid && transaction.TryWithdrawServingBatch(net, amounts, false,
                            list => prepare(Array.AsReadOnly(list.Select(ToBatch).ToArray())), out portions);
                        if (committed) transaction.Commit();
                    }
                }
                if (!committed) { failure = "output-or-stock-commit-rejected"; return false; }
                quote.Collected = true;
                if (current.EssentialCareJobId != null)
                    MealSittingComponent.MarkEssentialCareCollected(current.Collector, current.Requests[0].Recipient);
                collected = new CollectedFaucetBatch(created);
                ColonyFoodReserve.NotifyFoodChanged(quote.Faucet.Map);
                quote.Faucet.def.building?.soundDispense?.PlayOneShot(new TargetInfo(quote.Faucet.Position, quote.Faucet.Map));
                failure = null; return true;
            }
            finally
            {
                if (!committed)
                {
                    foreach (CollectedFaucetServing output in created)
                    {
                        if (destination.Contains(output.Thing)) destination.Remove(output.Thing);
                        if (!output.Thing.Destroyed) output.Thing.Destroy(DestroyMode.Vanish);
                    }
                    foreach (var request in quote.Requests) NoteCollectionFailure(quote.Faucet, request.Recipient);
                }
            }
        }
    }
}
