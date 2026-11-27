using System.Collections.Generic;
using RimRound.FeedingTube;
using RimRound.Comps;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimRound.FeedOther
{
    public enum FaucetMealPurpose { Routine, Social, Fatten, Ritual }
    // One selection/collection contract for either network implementation.
    // The collector owns carry space; the recipient owns food policy and fit.
    public static partial class FaucetMealAcquisition
    {
        // Shared durable candidate contract. Current appetite, carry contents,
        // optional reserve permission and retry timing belong to job selection.
        public static IEnumerable<FaucetPortion> PreferredPortions(Building_FoodFaucet faucet,
            Pawn eater, int preparedGrade)
        {
            bool prepared = UsesPreparedServing(eater);
            var configured = prepared ? (faucet?.GetComp<CompFoodFaucetPortion>()?.Candidates ??
                new[] { FoodFaucetPortions.Get(FaucetPortionMode.Normal) }) :
                new[] { FoodFaucetPortions.Get(FaucetPortionMode.Normal) };
            foreach (var portion in configured)
            {
                var candidate = portion?.WithDensity(prepared ? preparedGrade : 1);
                if (candidate?.FoodDef != null && candidate.Unlocked) yield return candidate;
            }
        }

        public static FaucetPortion StandardFallback =>
            FoodFaucetPortions.Get(FaucetPortionMode.Normal).WithDensity(1);

        public static IEnumerable<FaucetPortion> ReservePortions(Building_FoodFaucet faucet,
            Pawn eater, int preparedGrade)
        {
            foreach (var portion in PreferredPortions(faucet, eater, preparedGrade)) yield return portion;
            if (UsesPreparedServing(eater)) yield return StandardFallback;
        }

        public sealed class Selection
        {
            public FaucetPortion Portion { get; internal set; }
            public FoodBatchV2 Batch { get; internal set; }
        }

        internal static bool TrySelect(Building_FoodFaucet faucet, Pawn getter,
            Pawn eater, out Selection selection)
            => TrySelectForPurpose(faucet, getter, eater, FaucetMealPurpose.Routine, out selection);

        public static bool TrySelectForPurpose(Building_FoodFaucet faucet, Pawn getter,
            Pawn eater, FaucetMealPurpose purpose, out Selection selection)
            => TrySelectForPurposeCore(faucet,getter,eater,purpose,out selection,false);

        // A joint feast can consume an earlier batch for another diner first.
        // Offer discovery therefore checks output capability without binding
        // this diner to the current front batch. The cloned allocation ledger
        // checks the actual next ingredients before every planned withdrawal.
        internal static bool TrySelectForFeastPlan(Building_FoodFaucet faucet,Pawn getter,Pawn eater,out Selection selection)
            => TrySelectForPurposeCore(faucet,getter,eater,FaucetMealPurpose.Ritual,out selection,true);

        internal static Thing FeastPlanningPreview(FoodBatchV2 source,FaucetPortion portion)
        {
            var capability=source.Copy();capability.ingredients.Clear();
            return FoodNetworkV2ServingUtility.MakeServing(capability,portion.FoodDef,portion.FoodDef.defName);
        }

        private static bool TrySelectForPurposeCore(Building_FoodFaucet faucet,Pawn getter,
            Pawn eater,FaucetMealPurpose purpose,out Selection selection,bool feastPlanning)
        {
            selection = null;
            if (faucet == null || !faucet.Spawned || getter?.Spawned != true ||
                eater == null || eater.Dead || eater.needs?.food == null ||
                getter.Map != faucet.Map || eater.MapHeld != faucet.Map ||
                getter.carryTracker == null || getter.carryTracker.CarriedThing != null ||
                !FoodNetworkV2MachineUtility.IsOperational(faucet) || RetryingTooSoon(faucet, eater))
                return false;

            if (MealSittingComponent.EssentialCareMode(getter, eater))
            {
                if (purpose != FaucetMealPurpose.Routine ||
                    !MealSittingComponent.CanAcquireEssentialCare(getter, eater)) return false;
                FaucetPortion ordinary = StandardFallback;
                FoodBatchV2 ordinarySource;
                if (getter.carryTracker.AvailableStackSpace(ordinary.FoodDef) < 1 ||
                    !TryPreviewServing(faucet, ordinary, out ordinarySource)) return false;
                Thing ordinaryPreview = FoodNetworkV2ServingUtility.MakeServing(ordinarySource,
                    ordinary.FoodDef, ordinary.FoodDef.defName);
                if (ordinaryPreview == null) return false;
                try
                {
                    if (!Permitted(eater, getter, ordinaryPreview, purpose, faucet)) return false;
                    selection = new Selection { Portion = ordinary, Batch = ordinarySource };
                    return true;
                }
                finally { ordinaryPreview.Destroy(DestroyMode.Vanish); }
            }

            if (TrySelectPreferred(faucet, getter, eater, purpose, out selection,feastPlanning:feastPlanning)) return true;
            if (!UsesPreparedServing(eater)) return false;
            // Weak/short stock or an unsuitable selected size must not strand
            // a hungry pawn at the only food source. This is an honest standard
            // 1x serving; the faucet never manufactures the requested density.
            FaucetPortion fallback = StandardFallback;
            FoodBatchV2 source;
            if (getter.carryTracker.AvailableStackSpace(fallback.FoodDef) < 1 ||
                !TryPreviewServing(faucet, fallback, out source)) return false;
            Thing preview = feastPlanning ? FeastPlanningPreview(source,fallback) : FoodNetworkV2ServingUtility.MakeServing(source, fallback.FoodDef, fallback.FoodDef.defName);
            if (preview == null) return false;
            try
            {
                if (!(feastPlanning ? AbundanceMealJobs.ClaimPolicyAllows(eater,preview,getter) : Permitted(eater, getter, preview, purpose, faucet))) return false;
                selection = new Selection { Portion = fallback, Batch = source };
                return true;
            }
            finally { preview.Destroy(DestroyMode.Vanish); }
        }

        private static bool TrySelectPreferred(Building_FoodFaucet faucet, Pawn getter,
            Pawn eater, FaucetMealPurpose purpose, out Selection selection,
            bool reserveAlreadyAuthorized = false, bool checkCarrySpace = true,bool feastPlanning=false)
        {
            selection = null;

            CompFoodFaucetPortion setting = faucet.GetComp<CompFoodFaucetPortion>();
            bool prepared = UsesPreparedServing(eater);
            int grade = prepared ? ResolveSupplyGrade(faucet) : 1;
            float wanted = BodyFeedingPolicy.GetRequestedNutrition(eater,
                Mathf.Max(0f, eater.needs.food.MaxLevel - eater.needs.food.CurLevel));
            bool automatic = setting?.Mode == FaucetPortionMode.Auto;
            Selection belowRequest = null;
            foreach (FaucetPortion portion in PreferredPortions(faucet, eater, grade))
            {
                if (portion == null || !portion.Unlocked || portion.FoodDef == null ||
                    (checkCarrySpace && getter.carryTracker.AvailableStackSpace(portion.FoodDef) < 1))
                    continue;
                FoodBatchV2 batch;
                if (!TryPreviewServing(faucet, portion, out batch)) continue;
                Thing preview = feastPlanning ? FeastPlanningPreview(batch,portion) : FoodNetworkV2ServingUtility.MakeServing(
                    batch, portion.FoodDef, portion.FoodDef.defName);
                if (preview == null) continue;
                bool permitted;
                bool meetsRequest;
                try
                {
                    permitted = feastPlanning ? AbundanceMealJobs.ClaimPolicyAllows(eater,preview,getter) : Permitted(eater, getter, preview, purpose, faucet, reserveAlreadyAuthorized);
                    if (purpose == FaucetMealPurpose.Routine && BodyFeedingPolicy.UsesComfortableGainTarget(eater))
                        meetsRequest = eater.TryGetComp<FullnessAndDietStats_ThingComp>().OccupiedVolume +
                            FeedOtherUtility.EstimatedFullnessGain(eater, preview) + FoodNetworkV2Constants.Epsilon >=
                            BodyFeedingPolicy.ComfortableGainTarget(eater);
                    else if (purpose == FaucetMealPurpose.Routine)
                        meetsRequest = portion.Nutrition + FoodNetworkV2Constants.Epsilon >= wanted;
                    else
                    {
                        FullnessAndDietStats_ThingComp body =
                            eater.TryGetComp<FullnessAndDietStats_ThingComp>();
                        float target = purpose == FaucetMealPurpose.Fatten
                            ? PrisonerFatteningFoodPatch.FattenFullnessTarget(eater, body)
                            : FeedOtherUtility.FeedingTarget(eater);
                        meetsRequest = body != null && body.OccupiedVolume +
                            FeedOtherUtility.EstimatedFullnessGain(eater, preview) +
                            FoodNetworkV2Constants.Epsilon >= target;
                    }
                }
                finally { preview.Destroy(DestroyMode.Vanish); }
                if (!permitted) continue;
                Selection candidate = new Selection { Portion = portion, Batch = batch };
                // Candidates are ascending: urgent Auto takes the smallest
                // permitted serving. Otherwise meet the live request with the
                // smallest fitting size, or use the largest available below it.
                if (!automatic || (BodyFeedingPolicy.IsUrgentlyHungry(eater) &&
                    !BodyFeedingPolicy.UsesComfortableGainTarget(eater)) || meetsRequest)
                {
                    selection = candidate;
                    return true;
                }
                belowRequest = candidate;
            }
            selection = belowRequest;
            return selection != null;
        }

        internal static bool TryPreviewBatch(Building_FoodFaucet faucet,
            float nutrition, out FoodBatchV2 batch)
        {
            batch = null;
            if (faucet == null || !FoodNetworkV2MachineUtility.IsOperational(faucet))
                return false;
            if (FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                FoodNetworkV2 network = FoodNetworkV2MachineUtility.NetworkFor(faucet);
                return network != null && network.TryPreviewExactNutrition(nutrition, out batch);
            }
            ClassicFoodPortion portion;
            if (!faucet.TryPreviewMeal(nutrition, out portion)) return false;
            batch = new FoodBatchV2(portion.Nutrition, portion.Volume,
                portion.Preservation.Ingredients, portion.SourceCreatedTick);
            batch.SetAmounts(portion.PreciseNutrition, portion.PreciseVolume);
            batch.SetPreservation(portion.Preservation);
            return !batch.Empty;
        }

        internal static Thing Collect(Building_FoodFaucet faucet, Pawn getter, Pawn eater)
            => CollectForPurpose(faucet, getter, eater, FaucetMealPurpose.Routine);

        internal static Thing CollectForPurpose(Building_FoodFaucet faucet, Pawn getter,
            Pawn eater, FaucetMealPurpose purpose)
        {
            Selection selected;
            FaucetBatchQuote quote;
            CollectedFaucetBatch collected;
            string failure;
            if (!TrySelectForPurpose(faucet, getter, eater, purpose, out selected) ||
                !TryQuoteBatch(faucet, getter, new[] { new FaucetMealRequest(eater, purpose, selected.Portion, 1) },
                    out quote, out failure) ||
                !TryCollectBatch(quote, getter.inventory.innerContainer, out collected, out failure))
            {
                NoteCollectionFailure(faucet, eater);
                return null;
            }
            // Compatibility for existing single-serving callers, which take
            // carry ownership immediately. Batch callers retain inventory ownership.
            Thing serving = collected.Servings[0].Thing;
            getter.inventory.innerContainer.Remove(serving);
            return serving;
        }
        private static bool Permitted(Pawn eater, Pawn getter, Thing serving,
            FaucetMealPurpose purpose, Building_FoodFaucet faucet, bool postDraw = false)
        {
            if (!SpecialistContentPolicy.Allows(serving) || !OrdinaryFoodIntake.IsProcessableFood(serving)) return false;
            if (!FoodUtility.WillEat(eater, serving, getter, true, false)) return false;
            if (MealSittingComponent.EssentialCareMode(getter, eater))
            {
                MealPortionPlan carePlan;
                return purpose == FaucetMealPurpose.Routine &&
                    MealSittingComponent.CanAcquireEssentialCare(getter, eater) &&
                    OrdinaryFoodIntake.TryPreview(eater, serving, 1, out carePlan,
                        MealSittingComponent.Current.BudgetFor(eater));
            }
            if (purpose == FaucetMealPurpose.Ritual) return AbundanceMealJobs.Permitted(eater,serving);
            if (purpose == FaucetMealPurpose.Fatten)
                return PrisonerFatteningFoodPatch.CanProvideFattenServing(eater, serving, 1,
                    faucet, checkReserve: !postDraw);
            // The serving has already passed its projected reserve check before
            // withdrawal. Until the collection toil holds it, it is conserved
            // food in transit, absent from both map stores and carried stock.
            // Recheck physical/policy permission here; the actual ingest commit
            // performs the reserve debit after it becomes a held serving.
            if (purpose != FaucetMealPurpose.Fatten && SocialFeedingPolicy.IsAutomaticContext(eater))
                return SocialFeedingPolicy.CanEat(eater, serving, faucet, checkReserve: !postDraw);
            if (postDraw && purpose == FaucetMealPurpose.Routine && OrdinaryFoodIntake.Applies(eater, serving))
            {
                RimRound.Simulation.MealPortionPlan plan;
                return OrdinaryFoodIntake.TryPreview(eater, serving, 1, out plan);
            }
            if (purpose == FaucetMealPurpose.Social &&
                !FeedOtherUtility.IsMealAcceptableForPawn(eater, serving)) return false;
            if (purpose == FaucetMealPurpose.Routine)
                return BodyFeedingPolicy.CanFitFood(eater, serving,
                    FoodUtility.NutritionForEater(eater, serving));
            FullnessAndDietStats_ThingComp body = eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null || body.Disabled) return false;
            float target = purpose == FaucetMealPurpose.Fatten
                ? PrisonerFatteningFoodPatch.FattenFullnessTarget(eater, body)
                : FeedOtherUtility.FeedingTarget(eater);
            return FeedOtherUtility.IsPortionFitAtFullnessTarget(eater, serving, 1, target);
        }

        internal static bool Recover(Building_FoodFaucet faucet, Thing serving, Pawn getter)
        {
            return !FeedOtherMod.Settings.foodNetworkV2Enabled && faucet != null
                ? faucet.TryRecoverServing(serving, getter)
                : FoodNetworkServingRecovery.TryReturnOrPlace(faucet, serving, getter);
        }

        internal static IEnumerable<Toil> CollectionToils(Pawn getter, Pawn eater)
        {
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);
            Toil take = ToilMaker.MakeToil("CollectRecipientNetworkMeal");
            take.initAction = delegate
            {
                Thing target = getter.CurJob.targetA.Thing;
                if (getter.carryTracker.CarriedThing == target &&
                    target?.TryGetComp<CompFoodNetworkServing>()?.IsInitialized == true)
                    return;
                Building_FoodFaucet faucet = target as Building_FoodFaucet;
                Thing serving = Collect(faucet, getter, eater);
                if (serving == null || !getter.carryTracker.TryStartCarry(serving))
                {
                    if (serving != null && !serving.Destroyed) Recover(faucet, serving, getter);
                    getter.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                    return;
                }
                getter.CurJob.targetA = getter.carryTracker.CarriedThing;
                getter.CurJob.count = 1;
            };
            take.defaultCompleteMode = ToilCompleteMode.Delay;
            take.defaultDuration = Building_NutrientPasteDispenser.CollectDuration;
            yield return take;
        }
    }
}
