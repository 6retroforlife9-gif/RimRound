using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimRound.FeedingTube;
using RimWorld;
using RimWorld.Planet;
using System;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    // Native map-food search passes through the core body-goal fit check before
    // the warden can construct a Fatten job. Use the same whole-portion target
    // there as in the Fatten driver; otherwise the ordinary soft limit can
    // reject every prepared meal even with an empty stomach.
    [HarmonyPatch(typeof(BodyFeedingPolicy), nameof(BodyFeedingPolicy.CanFitFood))]
    public static class FattenFoodSelectionFitPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, Thing food, float nutrition,
            ref bool __result)
        {
            if (food == null ||
                !PrisonerFatteningFoodPatch.IsFattenFoodSearchFor(pawn) ||
                !PrisonerFatteningFoodPatch.IsFattenPrisoner(pawn)) return;

            FullnessAndDietStats_ThingComp fullness =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            float perUnit = FoodUtility.NutritionForEater(pawn, food);
            if (fullness == null || fullness.Disabled ||
                perUnit <= 0f || float.IsNaN(perUnit) || float.IsInfinity(perUnit) ||
                nutrition <= 0f || float.IsNaN(nutrition) || float.IsInfinity(nutrition))
                return;

            int units = Mathf.Max(1, Mathf.CeilToInt(nutrition / perUnit));
            __result = PrisonerFatteningFoodPatch.CanProvideFattenServing(pawn, food, units);
        }
    }

    /// <summary>
    /// Deterministic prisoner food handling.
    ///
    /// Non-Fatten interaction modes receive one controlled food serving whenever
    /// the prisoner is hungry or eligible for a body-goal extra meal, no more
    /// often than once every two in-game hours.
    /// Fatten prisoners who are already in bed are directly fed one serving at
    /// a time; standing hungry prisoners receive ordinary meal delivery.
    /// A saved hysteresis latch then blocks further Fatten feeding until physical
    /// fullness falls strictly below 10% of the current hard stomach capacity.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_Warden_DeliverFood), nameof(WorkGiver_Warden_DeliverFood.JobOnThing))]
    public static class PrisonerFatteningFoodPatch
    {
        // Keep automatically repaired UI ranges at Very Full so RimRound never
        // opens its dangerous-target confirmation dialog during pawn UI drawing.
        public const float SafeHardLimitFraction = 0.70f;
        public static float FattenSessionTargetFraction =>
            FeedOtherMod.Settings.PrisonerFattenTargetFraction;
        public static float FattenResumeFraction =>
            FeedOtherMod.Settings.PrisonerFattenResumeFraction;
        public const float DefaultLowerTargetPercent = 0.30f;
        public const float DefaultUpperTargetPercent = 0.90f;

        private const float Epsilon = 0.0001f;
        private const string FattenInteractionDefName = "RR_Fatten";
        [ThreadStatic]
        private static Pawn fattenFoodSearchPawn;

        internal static bool IsFattenFoodSearchFor(Pawn pawn)
        {
            return pawn != null && pawn == fattenFoodSearchPawn;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("RRHarmony")]
        public static bool Prefix(Pawn __0, Thing __1, bool __2, ref Job __result)
        {
            if (!FeedOtherMod.Settings.prisonerFeedingOverhaulEnabled)
            {
                return true;
            }

            Pawn warden = __0;
            Pawn prisoner = __1 as Pawn;
            if (prisoner == null || !prisoner.IsPrisonerOfColony)
            {
                return true;
            }

            if (prisoner.TryGetComp<FullnessAndDietStats_ThingComp>()
                    ?.UsesBodyModel != true)
            {
                // The themed Fatten and delivery jobs are adult body-model
                // routes. Keep ordinary wardening and clinical feeding intact.
                return true;
            }

            // Allow RimRound's original Fatten implementation to run when only
            // this patch's Fatten overhaul is disabled, while keeping the patch's
            // ordinary prisoner delivery rules available for other interactions.
            if (HasFattenInteraction(prisoner) &&
                !FeedOtherMod.Settings.prisonerFattenEnabled)
            {
                return true;
            }

            // Fully replace both vanilla and RimRound's original DeliverFood
            // decision for prisoners. This prevents stale diet/fullness state and
            // the room-food heuristic from silently suppressing urgent deliveries.
            __result = null;
            EnsureValidPrisonerDietRanges(prisoner);

            if (!CanWardenHandlePrisoner(warden, prisoner, __2))
            {
                return false;
            }

            if (IsFattenPrisoner(prisoner))
            {
                EnsureFattenBedLock(prisoner);
                // Native LayDown owns posture. A mobile prisoner gets a meal in
                // the cell when hungry; direct feeding is for an existing bed
                // occupant, including one whose hunger has become urgent.
                if (!prisoner.InBed())
                {
                    if (WantsNormalDelivery(prisoner) &&
                        (!FeedOtherCooldownComponent.IsPrisonerMealDeliveryOnCooldown(prisoner) ||
                         BodyFeedingPolicy.IsUrgentlyHungry(prisoner)) &&
                        !IsActivePrisonerMealDeliveryRecipient(prisoner))
                    {
                        TryMakeNormalDeliveryJob(warden, prisoner, __2, out __result);
                    }
                    return false;
                }
                Job fattenJob;
                if (TryMakeDirectFattenJob(warden, prisoner, __2, out fattenJob))
                {
                    __result = fattenJob;
                }

                return false;
            }

            // Downed or medically resting prisoners remain the responsibility of
            // vanilla WorkGiver_Warden_Feed. Do not create a cell-delivery job for
            // them here.
            if (WardenFeedUtility.ShouldBeFed(prisoner))
            {
                return false;
            }

            if (!WantsNormalDelivery(prisoner) ||
                (FeedOtherCooldownComponent.IsPrisonerMealDeliveryOnCooldown(prisoner) &&
                 !BodyFeedingPolicy.IsUrgentlyHungry(prisoner)) ||
                IsActivePrisonerMealDeliveryRecipient(prisoner))
            {
                return false;
            }

            Job deliveryJob;
            if (TryMakeNormalDeliveryJob(warden, prisoner, __2, out deliveryJob))
            {
                __result = deliveryJob;
            }

            return false;
        }

        public static bool IsFattenPrisoner(Pawn pawn)
        {
            return FeedOtherMod.Settings.prisonerFeedingOverhaulEnabled &&
                FeedOtherMod.Settings.prisonerFattenEnabled &&
                pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel == true &&
                HasFattenInteraction(pawn);
        }

        private static bool HasFattenInteraction(Pawn pawn)
        {
            return pawn != null && pawn.IsPrisonerOfColony && pawn.guest != null &&
                pawn.guest.ExclusiveInteractionMode != null &&
                pawn.guest.ExclusiveInteractionMode.defName == FattenInteractionDefName;
        }

        private static bool CanWardenHandlePrisoner(
            Pawn warden,
            Pawn prisoner,
            bool forced)
        {
            if (warden == null || prisoner == null || warden.Map == null ||
                prisoner.Map != warden.Map || !prisoner.Spawned ||
                prisoner.guest == null || !prisoner.guest.PrisonerIsSecure ||
                !prisoner.guest.CanBeBroughtFood || prisoner.InMentalState ||
                warden.Drafted || warden.InMentalState ||
                prisoner.IsForbidden(warden) || prisoner.IsFormingCaravan() ||
                !prisoner.Position.IsInPrisonCell(prisoner.Map) ||
                warden.health?.capacities == null ||
                !warden.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                return false;
            }

            return warden.CanReserveAndReach(
                prisoner,
                PathEndMode.OnCell,
                warden.NormalMaxDanger(),
                1,
                -1,
                null,
                forced);
        }

        private static bool WantsNormalDelivery(Pawn prisoner)
        {
            Need_Food food = prisoner?.needs?.food;
            return food != null &&
                (food.CurLevelPercentage < food.PercentageThreshHungry + 0.02f ||
                 BodyFeedingPolicy.CanUseExtraGainMeal(prisoner));
        }

        private static bool TryMakeNormalDeliveryJob(
            Pawn warden,
            Pawn prisoner,
            bool forced,
            out Job job)
        {
            job = null;
            // Let the prisoner's existing food job finish before offering
            // another delivery, including during urgent-hunger cooldown bypass.
            if (prisoner.CurJobDef == JobDefOf.Ingest) return false;
            Thing foodSource;
            ThingDef foodDef;
            int count;
            if (!TryFindOneFoodServing(
                    warden,
                    prisoner,
                    forced,
                    false,
                    out foodSource,
                    out foodDef,
                    out count))
            {
                return false;
            }

            // Urgency can bypass the delivery cooldown, but must not make a
            // warden repeatedly pick up the meal they just placed in the cell.
            // Check the actual selected serving and the prisoner's own access;
            // an unusable meal elsewhere in the room must not block assistance.
            Room room = prisoner.GetRoom();
            if (foodSource.Spawned && !(foodSource is Building) &&
                room?.IsPrisonCell == true && foodSource.GetRoom() == room &&
                !foodSource.IsForbidden(prisoner) &&
                FoodUtility.WillEat(prisoner, foodSource, prisoner, true, false) &&
                prisoner.CanReserveAndReach(foodSource, PathEndMode.ClosestTouch,
                    Danger.None))
                return false;

            job = JobMaker.MakeJob(
                FeedOtherDefOf.RR_PrisonerMealDelivery,
                foodSource,
                prisoner);
            job.count = count;
            return true;
        }

        public static bool TryMakeDirectFattenJob(
            Pawn warden,
            Pawn prisoner,
            bool forced,
            out Job job)
        {
            job = null;
            if (!CanContinueFattenJob(prisoner) ||
                IsActiveFattenFeedRecipient(prisoner))
            {
                return false;
            }

            FullnessAndDietStats_ThingComp fullness =
                prisoner.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness == null || fullness.Disabled ||
                fullness.FullnessGainedMultiplier <= Epsilon ||
                (!BodyFeedingPolicy.IsUrgentlyHungry(prisoner) &&
                 HasReachedFattenSessionTarget(prisoner, fullness)))
            {
                return false;
            }

            Thing foodSource;
            ThingDef foodDef;
            int count;
            if (!TryFindOneFoodServing(
                    warden,
                    prisoner,
                    forced,
                    !BodyFeedingPolicy.IsUrgentlyHungry(prisoner),
                    out foodSource,
                    out foodDef,
                    out count))
            {
                return false;
            }

            job = JobMaker.MakeJob(
                FeedOtherDefOf.RR_FattenPrisonerDirectFeed,
                foodSource,
                prisoner);
            job.count = count;
            return true;
        }

        private static bool TryFindOneFoodServing(
            Pawn warden,
            Pawn prisoner,
            bool forced,
            bool enforceFattenTarget,
            out Thing foodSource,
            out ThingDef foodDef,
            out int count)
        {
            foodSource = null;
            foodDef = null;
            count = 0;

            bool foundFood;
            Pawn previousSearchPawn = fattenFoodSearchPawn;
            try
            {
                // Limit the Fatten fit rule to this direct-care search. Ordinary
                // self-feeding and Gain/Recovery keep their soft/daily limits.
                fattenFoodSearchPawn = enforceFattenTarget ? prisoner : null;
                foundFood = FoodUtility.TryFindBestFoodSourceFor(
                    warden,
                    prisoner,
                    prisoner.needs.food.CurCategory == HungerCategory.Starving,
                    out foodSource,
                    out foodDef,
                    canRefillDispenser: false,
                    canUseInventory: true,
                    canUsePackAnimalInventory: false,
                    allowForbidden: false,
                    allowCorpse: false,
                    allowSociallyImproper: false,
                    allowHarvest: false,
                    forceScanWholeMap: false,
                    ignoreReservations: false,
                    calculateWantedStackCount: true);
            }
            finally
            {
                fattenFoodSearchPawn = previousSearchPawn;
            }
            if (!foundFood || !FeedOtherUtility.IsPreparedMeal(foodSource))
            {
                Building_FoodFaucet faucet;
                float faucetScore;
                FaucetMealAcquisition.Selection selected;
                if (FoodNetworkV2FaucetSearchUtility.TryFindBestFaucet(
                        warden,
                        prisoner,
                        prisoner.needs.food.CurCategory ==
                            HungerCategory.Starving,
                        FoodPreferability.MealLavish,
                        false,
                        false,
                        FoodPreferability.Undefined,
                        false,
                        out faucet,
                        out faucetScore, enforceFattenTarget
                            ? FaucetMealPurpose.Fatten : FaucetMealPurpose.Routine) &&
                    FaucetMealAcquisition.TrySelectForPurpose(faucet, warden, prisoner,
                        enforceFattenTarget ? FaucetMealPurpose.Fatten : FaucetMealPurpose.Routine,
                        out selected))
                {
                    foodSource = faucet;
                    foodDef = selected.Portion.FoodDef;
                    foundFood = true;
                }
                if (!foundFood) return false;
            }

            if (foodSource == null || foodDef == null)
            {
                return false;
            }

            bool preparedMeal = foodDef.ingestible != null &&
                foodDef.ingestible.preferability >= FoodPreferability.MealAwful;
            if (foodSource is Building_NutrientPasteDispenser ||
                foodSource is Building_FoodFaucet || preparedMeal)
            {
                count = 1;
            }
            else
            {
                float nutrition = FoodUtility.GetNutrition(
                    prisoner,
                    foodSource,
                    foodDef);
                count = Mathf.Max(
                    1,
                    FoodUtility.WillIngestStackCountOf(
                        prisoner,
                        foodDef,
                        nutrition));
            }

            if (!(foodSource is Building_NutrientPasteDispenser) &&
                !(foodSource is Building_FoodFaucet))
            {
                count = Mathf.Min(count, foodSource.stackCount);
                if (warden.carryTracker != null)
                {
                    count = Mathf.Min(
                        count,
                        warden.carryTracker.AvailableStackSpace(foodSource.def));
                }

                FullnessAndDietStats_ThingComp fullness =
                    prisoner.TryGetComp<FullnessAndDietStats_ThingComp>();
                while (count > 0)
                {
                    bool acceptable = enforceFattenTarget
                        ? fullness != null && CanProvideFattenServing(prisoner, foodSource, count)
                        : FeedOtherUtility
                            .IsSelfFeedingPortionFitAcceptable(
                                prisoner,
                                foodSource,
                                count);
                    if (acceptable)
                    {
                        break;
                    }
                    count--;
                }
            }

            return count > 0;
        }

        public static bool CanContinueFattenJob(Pawn prisoner)
        {
            if (!IsFattenPrisoner(prisoner) || prisoner.Dead ||
                !prisoner.Spawned || prisoner.guest == null ||
                !prisoner.guest.PrisonerIsSecure ||
                !prisoner.guest.CanBeBroughtFood || prisoner.InMentalState ||
                prisoner.IsFormingCaravan() || !prisoner.InBed())
            {
                return false;
            }

            FullnessAndDietStats_ThingComp fullness =
                prisoner.TryGetComp<FullnessAndDietStats_ThingComp>();
            return fullness != null && !fullness.Disabled &&
                fullness.FullnessGainedMultiplier > Epsilon &&
                (NeedsEssentialFattenCare(prisoner) || ColonyFoodReserve.CanSpendOptional(prisoner, 0d)) &&
                (BodyFeedingPolicy.IsUrgentlyHungry(prisoner) ||
                 (!FeedOtherCooldownComponent.IsPrisonerFattenOnCooldown(prisoner) &&
                  !HasReachedFattenSessionTarget(prisoner, fullness)));
        }

        public static void CompleteFattenServing(Pawn prisoner)
        {
            // Recalculate after every one-serving job. Reaching exact 80% arms a
            // saved fullness latch; otherwise the warden scanner may immediately
            // create the next serving job.
            UpdateFatteningContinuation(prisoner);
            EnsureFattenBedLock(prisoner);
        }

        public static bool HasReachedFattenTarget(Pawn prisoner)
        {
            FullnessAndDietStats_ThingComp fullness =
                prisoner?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return fullness == null ||
                HasReachedFattenSessionTarget(prisoner, fullness);
        }

        public static bool HasReachedFattenSessionTarget(Pawn prisoner)
        {
            FullnessAndDietStats_ThingComp fullness =
                prisoner?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return fullness == null ||
                HasReachedFattenSessionTarget(prisoner, fullness);
        }

        private static bool HasReachedFattenSessionTarget(
            Pawn prisoner,
            FullnessAndDietStats_ThingComp fullness)
        {
            return fullness.OccupiedVolume + Epsilon >=
                FattenFullnessTarget(prisoner, fullness);
        }

        public static float FattenFullnessTarget(
            Pawn prisoner,
            FullnessAndDietStats_ThingComp fullness)
        {
            if (fullness == null) return 0f;
            // Assigned Gain follows the same comfortable sitting as other
            // assisted meals. Legacy Fatten retains its explicit hard target.
            return Mathf.Max(0f, MealSittingComponent.Eligible(prisoner)
                ? Mathf.Min(fullness.HardLimit, fullness.SoftLimit * .9f)
                : fullness.HardLimit * FattenSessionTargetFraction);
        }

        public static float SafeTarget(
            Pawn prisoner,
            FullnessAndDietStats_ThingComp fullness)
        {
            return FattenFullnessTarget(prisoner, fullness);
        }

        public static void UpdateFatteningContinuation(Pawn prisoner)
        {
            if (!IsFattenPrisoner(prisoner))
            {
                return;
            }

            FullnessAndDietStats_ThingComp fullness =
                prisoner.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness == null || fullness.Disabled || fullness.HardLimit <= Epsilon)
            {
                return;
            }

            float current = fullness.OccupiedVolume;
            float stopTarget = FattenFullnessTarget(prisoner, fullness);
            float resumeTarget = fullness.HardLimit * FattenResumeFraction;

            if (current + Epsilon >= stopTarget)
            {
                FeedOtherCooldownComponent.NotifyPrisonerFattenSessionCompleted(prisoner);
            }
            else if (current + Epsilon < resumeTarget)
            {
                FeedOtherCooldownComponent.ClearPrisonerFattenCooldown(prisoner);
            }
        }

        public static float NutritionWantedForActiveFattenFeed(Pawn prisoner)
        {
            return BudgetForActiveFattenFeed(prisoner).Nutrition;
        }

        internal static OrdinaryIntakeBudget BudgetForActiveFattenFeed(Pawn prisoner)
        {
            Pawn feeder;
            Job feedJob;
            Thing food;
            if (!TryGetActiveFattenFeed(
                    prisoner,
                    out feeder,
                    out feedJob,
                    out food) || food == null)
            {
                return new OrdinaryIntakeBudget(0f, 0f);
            }

            int availableUnits = food.stackCount;
            if (feedJob.count > 0)
            {
                availableUnits = Mathf.Min(availableUnits, feedJob.count);
            }

            return FattenServingBudget(prisoner, food, Mathf.Max(1, availableUnits));
        }

        internal static bool NeedsEssentialFattenCare(Pawn prisoner)
        {
            return prisoner?.needs?.food != null &&
                (prisoner.needs.food.CurLevelPercentage <= prisoner.needs.food.PercentageThreshHungry + 0.02f ||
                 BodyFeedingPolicy.IsUrgentlyHungry(prisoner));
        }

        internal static OrdinaryIntakeBudget FattenServingBudget(Pawn prisoner, Thing food,
            int count, Thing reserveSource = null, bool checkReserve = true)
        {
            var body = prisoner?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null || prisoner.needs?.food == null || food == null)
                return new OrdinaryIntakeBudget(0f, 0f);
            double cost = ColonyFoodReserve.StockCost(food, count);
            if (!checkReserve || ColonyFoodReserve.CanSpendOptional(prisoner, cost, reserveSource ?? food))
                return new OrdinaryIntakeBudget(
                    Mathf.Max(0f, FoodUtility.GetNutrition(prisoner, food, FoodUtility.GetFinalIngestibleDef(food)) * count),
                    FattenFullnessTarget(prisoner, body), protectReserve: true);
            // Scarcity preserves necessary care, not the optional hard-limit
            // extension. Limit useful intake to the live native food deficit.
            return new OrdinaryIntakeBudget(NeedsEssentialFattenCare(prisoner)
                    ? Mathf.Max(0f, prisoner.needs.food.MaxLevel - prisoner.needs.food.CurLevel) : 0f,
                body.UsesBodyGoals ? body.SoftLimit : body.HardLimit, nutritionIsLimit: true);
        }

        internal static bool CanProvideFattenServing(Pawn prisoner, Thing food, int count,
            Thing reserveSource = null, bool checkReserve = true)
        {
            RimRound.Simulation.MealPortionPlan plan;
            return OrdinaryFoodIntake.TryPreview(prisoner, food, count, out plan,
                FattenServingBudget(prisoner, food, count, reserveSource, checkReserve)) &&
                (!MealSittingComponent.Eligible(prisoner) || plan.UsefulFraction >= .75d - 1e-7d);
        }

        public static bool IsActiveFattenFeedRecipient(Pawn prisoner)
        {
            Pawn feeder;
            Job job;
            Thing food;
            return TryGetActiveFattenFeed(
                prisoner,
                out feeder,
                out job,
                out food);
        }

        public static bool ShouldBlockFattenSelfEating(Pawn prisoner)
        {
            // A feeding designation must never suppress emergency self-care.
            return IsFattenPrisoner(prisoner) &&
                !BodyFeedingPolicy.IsUrgentlyHungry(prisoner) &&
                TryGetActiveFattenFeed(prisoner, out _, out _, out _);
        }

        private static bool TryGetActiveFattenFeed(
            Pawn prisoner,
            out Pawn feeder,
            out Job feedJob,
            out Thing food)
        {
            feeder = null;
            feedJob = null;
            food = null;
            if (prisoner == null || prisoner.Map == null ||
                FeedOtherDefOf.RR_FattenPrisonerDirectFeed == null)
            {
                return false;
            }

            foreach (Pawn candidate in prisoner.Map.mapPawns.AllPawnsSpawned)
            {
                Job candidateJob = candidate?.CurJob;
                if (candidateJob != null &&
                    candidateJob.def == FeedOtherDefOf.RR_FattenPrisonerDirectFeed &&
                    candidateJob.targetB.Pawn == prisoner)
                {
                    feeder = candidate;
                    feedJob = candidateJob;
                    food = candidateJob.targetA.Thing ??
                        candidate.carryTracker?.CarriedThing;
                    return true;
                }
            }

            return false;
        }

        public static bool IsActivePrisonerMealDeliveryRecipient(Pawn prisoner)
        {
            if (prisoner == null || prisoner.Map == null ||
                FeedOtherDefOf.RR_PrisonerMealDelivery == null)
            {
                return false;
            }

            foreach (Pawn candidate in prisoner.Map.mapPawns.AllPawnsSpawned)
            {
                Job candidateJob = candidate?.CurJob;
                if (candidateJob != null &&
                    candidateJob.def == FeedOtherDefOf.RR_PrisonerMealDelivery &&
                    candidateJob.targetB.Pawn == prisoner)
                {
                    return true;
                }
            }

            return false;
        }

        public static void EnsureFattenBedLock(Pawn prisoner)
        {
            // Retire a lock loaded from an older save. The warden's feeding
            // job reserves the recipient, while native LayDown owns bed posture.
            ReleaseFattenBedLock(prisoner);
        }

        public static void ReleaseFattenBedLock(Pawn prisoner)
        {
            if (prisoner?.CurJob != null &&
                prisoner.CurJob.def == FeedOtherDefOf.RR_FattenPrisonerBedLock)
            {
                prisoner.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        public static void EnforceAllFattenBedLocks()
        {
            if (Find.Maps == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                if (map == null)
                {
                    continue;
                }

                foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                {
                    if (IsFattenPrisoner(prisoner))
                    {
                        EnsureValidPrisonerDietRanges(prisoner);
                        EnsureFattenBedLock(prisoner);
                    }
                    else
                    {
                        ReleaseFattenBedLock(prisoner);
                    }
                }
            }
        }

        public static void EnsureValidPrisonerDietRanges(Pawn pawn)
        {
            if (!FeedOtherMod.Settings.prisonerFeedingOverhaulEnabled ||
                pawn == null || !pawn.IsPrisonerOfColony ||
                pawn.needs?.food == null)
            {
                return;
            }

            FullnessAndDietStats_ThingComp fullness =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness == null || !fullness.UsesBodyModel || fullness.Disabled)
            {
                return;
            }

            if (IsFattenPrisoner(pawn) && fullness.DietMode == DietMode.Disabled)
            {
                fullness.DietMode = DietMode.Fullness;
            }

            if (fullness.DietMode == DietMode.Disabled)
            {
                return;
            }

            Pair<float, float>? ranges = TryGetDietRanges(fullness);
            if (!RangesAreUninitialised(ranges))
            {
                return;
            }

            fullness.UpdateDietBars();
            ranges = TryGetDietRanges(fullness);
            if (!RangesAreUninitialised(ranges))
            {
                return;
            }

            switch (fullness.DietMode)
            {
                case DietMode.Nutrition:
                    fullness.SetRangesByPercent(
                        DefaultLowerTargetPercent,
                        DefaultUpperTargetPercent);
                    break;
                case DietMode.Hybrid:
                    fullness.SetRangesByValue(
                        pawn.needs.food.MaxLevel * DefaultLowerTargetPercent,
                        fullness.HardLimit * SafeHardLimitFraction);
                    break;
                case DietMode.Fullness:
                    fullness.SetRangesByValue(
                        fullness.HardLimit * DefaultLowerTargetPercent,
                        fullness.HardLimit * SafeHardLimitFraction);
                    break;
            }

            fullness.UpdateDietBars();
        }

        private static Pair<float, float>? TryGetDietRanges(
            FullnessAndDietStats_ThingComp fullness)
        {
            try
            {
                return fullness?.GetRanges();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool RangesAreUninitialised(Pair<float, float>? ranges)
        {
            if (!ranges.HasValue)
            {
                return true;
            }

            float first = ranges.Value.First;
            float second = ranges.Value.Second;
            return first < -Epsilon || second < -Epsilon ||
                (Mathf.Abs(first) <= Epsilon && Mathf.Abs(second) <= Epsilon);
        }
    }

    [HarmonyPatch(typeof(FullnessAndDietStats_ThingComp), nameof(FullnessAndDietStats_ThingComp.PostSpawnSetup))]
    public static class PrisonerDietRangePostSpawnRepairPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(FullnessAndDietStats_ThingComp __instance)
        {
            Pawn pawn = __instance?.parent as Pawn;
            PrisonerFatteningFoodPatch.EnsureValidPrisonerDietRanges(pawn);
            PrisonerFatteningFoodPatch.EnsureFattenBedLock(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class PrisonerDietRangeGuestStatusRepairPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("RRHarmony")]
        public static void Postfix(Pawn ___pawn)
        {
            PrisonerFatteningFoodPatch.EnsureValidPrisonerDietRanges(___pawn);
            PrisonerFatteningFoodPatch.EnsureFattenBedLock(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetExclusiveInteraction))]
    public static class PrisonerFattenInteractionBedLockPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn ___pawn)
        {
            if (PrisonerFatteningFoodPatch.IsFattenPrisoner(___pawn))
            {
                PrisonerFatteningFoodPatch.EnsureValidPrisonerDietRanges(___pawn);
                PrisonerFatteningFoodPatch.EnsureFattenBedLock(___pawn);
            }
            else
            {
                PrisonerFatteningFoodPatch.ReleaseFattenBedLock(___pawn);
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetFood), "GetPriority")]
    public static class PrisonerDietRangeSelfEatingRepairPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix(Pawn __0)
        {
            PrisonerFatteningFoodPatch.EnsureValidPrisonerDietRanges(__0);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn __0, ref float __result)
        {
            if (PrisonerFatteningFoodPatch.ShouldBlockFattenSelfEating(__0))
            {
                __result = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class PrisonerFatteningSelfEatingBlockPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn __0, ref Job __result)
        {
            if (PrisonerFatteningFoodPatch.ShouldBlockFattenSelfEating(__0))
            {
                __result = null;
            }
        }
    }


    [HarmonyPatch(typeof(WorkGiver_Warden_Feed), nameof(WorkGiver_Warden_Feed.JobOnThing))]
    public static class FattenPrisonerVanillaPatientFeedBlockPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Thing __1, ref Job __result)
        {
            if (PrisonerFatteningFoodPatch.ShouldBlockFattenSelfEating(__1 as Pawn))
            {
                // Fatten uses only RR_FattenPrisonerDirectFeed. This prevents a
                // second vanilla medical-feeding route from bypassing the 80/10
                // fullness cycle when the bed-locked prisoner is downed or hungry.
                __result = null;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(FeedPatientUtility), nameof(FeedPatientUtility.IsHungry))]
    public static class PrisonerDietRangePatientFeedingRepairPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix(Pawn __0)
        {
            PrisonerFatteningFoodPatch.EnsureValidPrisonerDietRanges(__0);
        }
    }

    [HarmonyPatch(typeof(PrisonBreakUtility), nameof(PrisonBreakUtility.CanParticipateInPrisonBreak))]
    public static class FattenPrisonerPrisonBreakBlockPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn __0, ref bool __result)
        {
            if (PrisonerFatteningFoodPatch.IsFattenPrisoner(__0))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(RimRound.AI.WorkGiver_Warden_ReduceReluctanceChat), nameof(RimRound.AI.WorkGiver_Warden_ReduceReluctanceChat.JobOnThing))]
    public static class FattenPrisonerReduceReluctanceChatBlockPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Thing __1, ref Job __result)
        {
            if (PrisonerFatteningFoodPatch.IsFattenPrisoner(__1 as Pawn))
            {
                __result = null;
                return false;
            }

            return true;
        }
    }
}
