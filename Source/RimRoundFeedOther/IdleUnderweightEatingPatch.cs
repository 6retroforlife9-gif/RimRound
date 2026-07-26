using HarmonyLib;
using RimRound.Comps;
using RimRound.FeedingTube;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    /// <summary>
    /// A pawn who positively values weight but is currently unhappy about
    /// being too thin may occasionally choose a normal meal instead of an idle
    /// wander. Reusing RimWorld's food job giver preserves food policy, diet,
    /// reachability, reservation, forbidden-item, and ingestion checks.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Idle), "TryGiveJob")]
    public static class IdleUnderweightEatingPatch
    {
        private static readonly ExposedFoodJobGiver FoodJobGiver = new ExposedFoodJobGiver();
        private static readonly Dictionary<int, int> NextIdleMealAttemptTickByPawn =
            new Dictionary<int, int>();

        [HarmonyPrefix]
        public static bool Prefix(Pawn __0, ref Job __result)
        {
            Job eatingJob;
            if (!TryGetScheduledIdleFoodJob(__0, out eatingJob))
            {
                return true;
            }

            __result = eatingJob;
            return false;
        }

        public static bool TryGetScheduledIdleFoodJob(Pawn pawn, out Job eatingJob)
        {
            eatingJob = null;
            if (!ShouldConsiderIdleMeal(pawn))
            {
                if (pawn != null)
                {
                    NextIdleMealAttemptTickByPawn.Remove(pawn.thingIDNumber);
                }

                return false;
            }

            int currentTick = Find.TickManager.TicksGame;
            int nextAttemptTick;
            if (!NextIdleMealAttemptTickByPawn.TryGetValue(
                    pawn.thingIDNumber,
                    out nextAttemptTick))
            {
                ScheduleNextAttempt(pawn, currentTick);
                return false;
            }

            if (currentTick < nextAttemptTick)
            {
                return false;
            }

            // Schedule before asking for food so a temporarily unavailable meal
            // does not make the idle think tree scan every tick. Unlike the old
            // independent 20% roll, this randomized delay guarantees another
            // attempt after a short idle interval instead of allowing an
            // arbitrarily long streak of wander jobs.
            ScheduleNextAttempt(pawn, currentTick);

            eatingJob = FoodJobGiver.TryGiveIdleFoodJob(pawn);
            if (eatingJob == null ||
                !ClampIdleFoodJobCount(pawn, eatingJob, true))
            {
                eatingJob = null;
                return false;
            }

            // Route only this autonomous underweight-eating action through the
            // dedicated standing ingest flow. Ordinary hunger, drafted orders,
            // prisoner feeding and all player-forced eating retain vanilla table
            // behaviour.
            eatingJob.def = FeedOtherDefOf.RR_IdleEatToFullness;
            eatingJob.reportStringOverride = "eating to gain weight.";
            return true;
        }

        public static bool IsIdleEatToFullnessJob(Job job)
        {
            return job != null &&
                job.def == FeedOtherDefOf.RR_IdleEatToFullness;
        }

        public static IEnumerable<Toil> MakeStandingIdleEatingToils(
            JobDriver_Ingest driver)
        {
            Pawn pawn = driver == null ? null : driver.pawn;
            Job job = driver == null ? null : driver.job;
            if (!IsEligiblePlayerColonyPawn(pawn) || job == null)
            {
                yield break;
            }

            Thing initialSource = job.GetTarget(TargetIndex.A).Thing;
            Building_FoodFaucet faucet = initialSource as Building_FoodFaucet;
            if (faucet == null)
            {
                faucet = job.GetTarget(TargetIndex.C).Thing as
                    Building_FoodFaucet;
            }

            if (faucet != null)
            {
                job.SetTarget(TargetIndex.C, faucet);
                yield return Toils_Goto.GotoThing(
                        TargetIndex.C,
                        PathEndMode.InteractionCell)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.C);

                Building_FoodFaucet sourceFaucet = faucet;
                Toil dispense = ToilMaker.MakeToil(
                    "TakeIdleMealStackFromFoodNetwork");
                dispense.initAction = delegate
                {
                    Pawn actor = dispense.actor;
                    if (actor == null || actor.CurJob == null ||
                        actor.carryTracker == null)
                    {
                        actor?.jobs?.curDriver?.EndJobWith(
                            JobCondition.Incompletable);
                        return;
                    }

                    Thing existing = actor.carryTracker.CarriedThing;
                    CompFoodNetworkServing existingComp = existing == null
                        ? null
                        : existing.TryGetComp<CompFoodNetworkServing>();
                    if (existingComp != null &&
                        existingComp.IsInitialized)
                    {
                        actor.CurJob.SetTarget(TargetIndex.A, existing);
                        actor.CurJob.count = existing.stackCount;
                        actor.CurJob.ingestTotalCount = true;
                        return;
                    }

                    actor.rotationTracker.FaceTarget(sourceFaucet);
                    Thing serving =
                        FoodNetworkV2ServingUtility
                            .TryDispenseIdleMealsForPawn(
                                sourceFaucet,
                                actor);
                    if (serving == null ||
                        !actor.carryTracker.TryStartCarry(serving))
                    {
                        if (serving != null && !serving.Destroyed)
                        {
                            FoodNetworkV2ServingUtility.TryReturnToNetwork(
                                sourceFaucet,
                                serving);
                            serving.Destroy(DestroyMode.Vanish);
                        }
                        actor.jobs.curDriver.EndJobWith(
                            JobCondition.Incompletable);
                        return;
                    }

                    actor.CurJob.SetTarget(
                        TargetIndex.A,
                        actor.carryTracker.CarriedThing);
                    actor.CurJob.count =
                        actor.carryTracker.CarriedThing.stackCount;
                    actor.CurJob.ingestTotalCount = true;
                };
                dispense.defaultCompleteMode = ToilCompleteMode.Delay;
                dispense.defaultDuration =
                    Building_NutrientPasteDispenser.CollectDuration;
                yield return dispense;
            }
            else if (initialSource is Building_NutrientPasteDispenser)
            {
                yield return Toils_Goto.GotoThing(
                        TargetIndex.A,
                        PathEndMode.InteractionCell)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.A);
                yield return Toils_Ingest.TakeMealFromDispenser(
                    TargetIndex.A,
                    pawn);
            }
            else if (pawn.inventory != null &&
                initialSource != null &&
                pawn.inventory.Contains(initialSource))
            {
                yield return MakeLiveIdlePortionClampToil();
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(
                    pawn,
                    TargetIndex.A);
            }
            else
            {
                yield return Toils_Goto.GotoThing(
                        TargetIndex.A,
                        PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.A);
                yield return MakeLiveIdlePortionClampToil();
                yield return Toils_Ingest.PickupIngestible(
                    TargetIndex.A,
                    pawn);
            }

            // Eat on the collection cell. No CarryIngestibleToChewSpot and no
            // FindAdjacentEatSurface toil are used, so this idle weight-gain job
            // never searches for a dining table.
            Toil chew = FeedOtherUtility.ChewIngestibleWithEatingSpeed(
                pawn,
                1f,
                TargetIndex.A,
                TargetIndex.None);
            yield return chew;
            yield return Toils_Ingest.FinalizeIngest(
                pawn,
                TargetIndex.A);
            yield return Toils_Jump.JumpIf(
                chew,
                delegate
                {
                    return ShouldContinueStandingIdleEating(pawn);
                });

            Toil dropUnused = ToilMaker.MakeToil(
                "DropUnusedIdleMealRemainder");
            dropUnused.initAction = delegate
            {
                Pawn actor = dropUnused.actor;
                Thing carried = actor?.carryTracker?.CarriedThing;
                if (actor == null || carried == null)
                {
                    return;
                }

                Thing dropped;
                actor.carryTracker.TryDropCarriedThing(
                    actor.Position,
                    ThingPlaceMode.Near,
                    out dropped,
                    null);
            };
            dropUnused.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dropUnused;
        }

        private static bool ShouldContinueStandingIdleEating(Pawn pawn)
        {
            if (!IsEligiblePlayerColonyPawn(pawn) ||
                !IsIdleEatToFullnessJob(pawn.CurJob))
            {
                return false;
            }

            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried == null || carried.Destroyed ||
                carried.stackCount <= 0 || pawn.CurJob.count <= 0)
            {
                return false;
            }

            FullnessAndDietStats_ThingComp fullness =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness != null &&
                (fullness.DietMode == DietMode.Fullness ||
                 fullness.DietMode == DietMode.Hybrid))
            {
                float target;
                return TryGetIdleFullnessTarget(pawn, out target) &&
                    fullness.CurrentFullness +
                        FeedOtherUtility.FoodFitEpsilon < target &&
                    FeedOtherUtility.IsPortionFitAtFullnessTarget(
                        pawn,
                        carried,
                        1,
                        target);
            }

            // Nutrition mode may continue only while the next complete unit still
            // respects the same global 1.0-nutrition waste allowance.
            return FeedOtherUtility.IsAutomaticFoodSelectionFitAcceptable(
                pawn,
                carried,
                1);
        }

        private static Toil MakeLiveIdlePortionClampToil()
        {
            Toil clamp = ToilMaker.MakeToil("ClampIdleMealPortionToLiveTarget");
            clamp.initAction = delegate
            {
                Pawn actor = clamp.actor;
                if (!IsEligiblePlayerColonyPawn(actor) ||
                    actor.CurJob == null ||
                    !ClampIdleFoodJobCount(actor, actor.CurJob, false))
                {
                    actor?.jobs?.curDriver?.EndJobWith(
                        JobCondition.Incompletable);
                }
            };
            clamp.defaultCompleteMode = ToilCompleteMode.Instant;
            return clamp;
        }

        private static bool ClampIdleFoodJobCount(
            Pawn pawn,
            Job job,
            bool allowIncrease)
        {
            if (pawn == null || job == null)
            {
                return false;
            }

            Thing source = job.GetTarget(TargetIndex.A).Thing;
            if (source == null || source.Destroyed)
            {
                return false;
            }

            // Food Network faucets calculate their stack from the live target at
            // dispense time. A vanilla paste dispenser creates one meal at a time.
            if (source is Building_FoodFaucet ||
                source is Building_NutrientPasteDispenser)
            {
                job.count = 1;
                return true;
            }

            if (source.def?.ingestible == null || source.stackCount <= 0)
            {
                return false;
            }

            int desired = Mathf.Max(1, job.count);
            FullnessAndDietStats_ThingComp fullness =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            float target;
            if (fullness != null &&
                (fullness.DietMode == DietMode.Fullness ||
                 fullness.DietMode == DietMode.Hybrid) &&
                TryGetIdleFullnessTarget(pawn, out target))
            {
                float remaining = Mathf.Max(
                    0f,
                    target - fullness.CurrentFullness);
                float gainPerUnit =
                    FeedOtherUtility.EstimatedFullnessGain(pawn, source);
                if (remaining <= FeedOtherUtility.FoodFitEpsilon ||
                    gainPerUnit <= FeedOtherUtility.FoodFitEpsilon)
                {
                    return false;
                }

                desired = Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        (remaining - FeedOtherUtility.FoodFitEpsilon) /
                        gainPerUnit));
            }

            if (!allowIncrease)
            {
                desired = Mathf.Min(desired, Mathf.Max(1, job.count));
            }
            desired = Mathf.Min(desired, source.stackCount);

            // Vanilla chooses a source using one-unit optimality but can assign a
            // larger job.count afterwards. Recheck the complete selected portion,
            // stepping down until concentration-aware projected waste is <= 1.0.
            while (desired > 0 &&
                !FeedOtherUtility.IsAutomaticFoodSelectionFitAcceptable(
                    pawn,
                    source,
                    desired))
            {
                desired--;
            }

            if (desired <= 0)
            {
                return false;
            }

            job.count = desired;
            return true;
        }

        public static bool TryGetIdleFullnessTarget(
            Pawn pawn,
            out float target)
        {
            target = 0f;
            FullnessAndDietStats_ThingComp fullness =
                pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness == null || fullness.Disabled ||
                (fullness.DietMode != DietMode.Fullness &&
                 fullness.DietMode != DietMode.Hybrid))
            {
                return false;
            }

            Pair<float, float> ranges;
            try
            {
                ranges = fullness.GetRanges();
            }
            catch (Exception)
            {
                return false;
            }

            target = ranges.Second;
            if (!fullness.SetAboveHardLimit)
            {
                target = UnityEngine.Mathf.Min(
                    target,
                    fullness.HardLimit);
            }
            return target > FeedOtherUtility.FoodFitEpsilon;
        }

        private static void ScheduleNextAttempt(Pawn pawn, int currentTick)
        {
            NextIdleMealAttemptTickByPawn[pawn.thingIDNumber] = currentTick +
                Rand.RangeInclusive(
                    FeedOtherMod.Settings.IdleMinimumDelayTicks,
                    FeedOtherMod.Settings.IdleMaximumDelayTicks);
        }

        internal static bool IsEligiblePlayerColonyPawn(Pawn pawn)
        {
            // This is a colony-owned idle recreation feature. Visitors, quest
            // guests, prisoners and hostile pawns can all receive vanilla idle
            // or wander think nodes, but must never be enrolled merely because
            // they are spawned humanlikes. Player-faction humanlikes include
            // the colony's ordinary colonists and slaves.
            return pawn != null &&
                pawn.Faction == Faction.OfPlayer &&
                !pawn.IsPrisoner &&
                !pawn.IsQuestLodger() &&
                pawn.RaceProps != null &&
                pawn.RaceProps.Humanlike;
        }

        private static bool ShouldConsiderIdleMeal(Pawn pawn)
        {
            if (!FeedOtherMod.Settings.idleUnderweightEatingEnabled ||
                !IsEligiblePlayerColonyPawn(pawn) ||
                pawn.Dead || !pawn.Spawned || pawn.Downed || !pawn.Awake() ||
                pawn.Drafted || pawn.InMentalState ||
                pawn.needs?.food == null || pawn.needs?.mood == null ||
                !GlobalSettings.moodletsForWeightOpinions)
            {
                return false;
            }

            FullnessAndDietStats_ThingComp fullnessComp =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            PawnBodyType_ThingComp bodyComp = pawn.TryGetComp<PawnBodyType_ThingComp>();
            ThingComp_PawnAttitude attitudeComp = pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (fullnessComp == null || fullnessComp.Disabled ||
                fullnessComp.DietMode == DietMode.Disabled ||
                fullnessComp.FullnessGainedMultiplier <= 0.001f ||
                fullnessComp.CurrentFullness >=
                    fullnessComp.HardLimit * FeedOtherMod.Settings.IdleEatingTriggerFraction ||
                bodyComp == null || bodyComp.PersonallyExempt || bodyComp.CategoricallyExempt ||
                attitudeComp == null || attitudeComp.weightOpinion < WeightOpinion.NeutralPlus)
            {
                return false;
            }

            ThoughtDef weightThought;
            if (!WeightOpinionUtility.weightOpinionToThoughtDef.TryGetValue(
                    attitudeComp.weightOpinion,
                    out weightThought) ||
                weightThought?.stages == null ||
                ThoughtUtility.ThoughtNullified(pawn, weightThought))
            {
                return false;
            }

            int currentStage = WeightOpinionUtility.GetThoughtIndex(pawn);
            int firstNonNegativeStage =
                weightThought.stages.FindIndex(stage => stage.baseMoodEffect >= 0f);
            return firstNonNegativeStage > 0 && currentStage >= 0 &&
                currentStage < firstNonNegativeStage &&
                currentStage < weightThought.stages.Count &&
                weightThought.stages[currentStage].baseMoodEffect < 0f;
        }

        private sealed class ExposedFoodJobGiver : JobGiver_GetFood
        {
            public Job TryGiveIdleFoodJob(Pawn pawn)
            {
                return TryGiveJob(pawn);
            }
        }
    }

    /// <summary>
    /// Player colonists normally receive GotoWander or Wait_Wander from
    /// JobGiver_Wander before the generic JobGiver_Idle fallback is reached.
    /// Hook that actual idle-wander path as well, sharing the same per-pawn
    /// randomized schedule and food-selection logic.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Wander), "TryGiveJob")]
    public static class WanderUnderweightEatingPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn __0, ref Job __result)
        {
            return IdleUnderweightEatingPatch.Prefix(__0, ref __result);
        }
    }

    /// <summary>
    /// Fullness can cross below 25% while a pawn remains inside one continuing
    /// idle job. In that case no job giver is called again, so check the active
    /// idle job on its normal job-tracker tick and replace only that idle job
    /// when the randomized meal attempt becomes due.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), "JobTrackerTickInterval")]
    public static class ActiveIdleUnderweightEatingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_JobTracker __instance, Pawn ___pawn)
        {
            Pawn pawn = ___pawn;
            Job currentJob = pawn?.CurJob;
            if (pawn == null || currentJob == null || currentJob.playerForced ||
                pawn.jobs != __instance || !pawn.mindState.IsIdle ||
                !pawn.jobs.IsCurrentJobPlayerInterruptible())
            {
                return;
            }

            Job eatingJob;
            if (!IdleUnderweightEatingPatch.TryGetScheduledIdleFoodJob(
                    pawn,
                    out eatingJob))
            {
                return;
            }

            __instance.StartJob(
                eatingJob,
                JobCondition.InterruptForced,
                null,
                false,
                true,
                null,
                JobTag.SatisfyingNeeds,
                false,
                false);
        }
    }

    [HarmonyPatch(
        typeof(MemoryThoughtHandler),
        nameof(MemoryThoughtHandler.TryGainMemory),
        new Type[] { typeof(ThoughtDef), typeof(Pawn), typeof(Precept) })]
    internal static class IdleEatingNoTableThoughtPatch
    {
        private static bool Prefix(
            MemoryThoughtHandler __instance,
            ThoughtDef __0,
            Pawn ___pawn)
        {
            return __0 != ThoughtDefOf.AteWithoutTable ||
                !IdleUnderweightEatingPatch.IsIdleEatToFullnessJob(
                    ___pawn?.CurJob);
        }
    }

    [HarmonyPatch(
        typeof(MemoryThoughtHandler),
        nameof(MemoryThoughtHandler.TryGainMemoryFast),
        new Type[] { typeof(ThoughtDef), typeof(Precept) })]
    internal static class IdleEatingNoTableFastThoughtPatch
    {
        private static bool Prefix(ThoughtDef __0, Pawn ___pawn)
        {
            return __0 != ThoughtDefOf.AteWithoutTable ||
                !IdleUnderweightEatingPatch.IsIdleEatToFullnessJob(
                    ___pawn?.CurJob);
        }
    }

    [HarmonyPatch(
        typeof(MemoryThoughtHandler),
        nameof(MemoryThoughtHandler.TryGainMemoryFast),
        new Type[] { typeof(ThoughtDef), typeof(int), typeof(Precept) })]
    internal static class IdleEatingNoTableFastStageThoughtPatch
    {
        private static bool Prefix(ThoughtDef __0, Pawn ___pawn)
        {
            return __0 != ThoughtDefOf.AteWithoutTable ||
                !IdleUnderweightEatingPatch.IsIdleEatToFullnessJob(
                    ___pawn?.CurJob);
        }
    }


    [HarmonyPatch(
        typeof(MemoryThoughtHandler),
        nameof(MemoryThoughtHandler.TryGainMemory),
        new Type[] { typeof(Thought_Memory), typeof(Pawn) })]
    internal static class IdleEatingNoTableMemoryInstancePatch
    {
        private static bool Prefix(Thought_Memory __0, Pawn ___pawn)
        {
            return __0?.def != ThoughtDefOf.AteWithoutTable ||
                !IdleUnderweightEatingPatch.IsIdleEatToFullnessJob(
                    ___pawn?.CurJob);
        }
    }

}
