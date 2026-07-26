using RimRound.Comps;
using RimRound.FeedingTube;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public static class FeedOtherUtility
    {
        public static float FeedingTargetFractionOfHardLimit
        {
            get { return FeedOtherMod.Settings.FeedingTargetFraction; }
        }
        public static int FeedingTargetPercent
        {
            get { return FeedOtherMod.Settings.FeedingTargetPercentRounded; }
        }
        // Feed Other ends at the configured target (70% by default). A final
        // whole small-food item may overshoot nutritionally, but the job-scoped
        // fullness setter discards that excess before RimRound can observe it.
        public const float FeedingCompletionToleranceFractionOfHardLimit = 0f;
        public const float MinimumFeedingCompletionTolerance = 0f;
        // Retained for save/API compatibility with sessions started by older
        // builds. v1.0.52 no longer treats repeated top-up batches as close enough:
        // every participant must genuinely cross the active feeding target.
        public const int RepeatedTopUpRoundsForCloseEnoughCompletion = 2;
        // Retained for source/API compatibility with older builds.
        public const float RepeatedTopUpCompletionToleranceFractionOfHardLimit = 0.05f;
        public const float MinimumRepeatedTopUpCompletionTolerance = 0.05f;
        // RimRound's real Very Full hediff stage remains fixed at 70%. Feeding
        // may stop below or above it, but mood memories and dialogue must continue
        // to describe the pawn's actual health stage rather than the chosen target.
        public const float VeryFullFractionOfHardLimit = 0.70f;
        // A pawn exactly at the configured starting cutoff remains eligible.
        // Only values strictly above it block a new feeding session.
        public static float MaximumStartingFullnessFractionOfHardLimit
        {
            get { return FeedOtherMod.Settings.StartingFullnessFraction; }
        }
        public const float SharedFoodSearchRadius = 30f;
        public const float MaximumSharedDiningDistance = 6f;
        public const float OneWayDiningSeatSearchRadius = 30f;
        // Once eating or feeding has begun, a participant may only leave the
        // social location for another serving when the source is genuinely
        // nearby. Initial collection still uses the normal 30-cell search.
        public const float FollowUpMealSearchRadius = 8f;
        public static int MaximumSessionTicks
        {
            get { return FeedOtherMod.Settings.MaximumSessionTicks; }
        }
        public const float MaximumAutonomousRecreationChance = 5f;
        public const float LowFullnessBonusThreshold = 0.40f;
        public const float VeryLowFullnessBonusThreshold = 0.25f;
        public const float NearbyMealDistance = 15f;
        public const float FarMealDistance = 30f;
        // Recreation is earned continuously while the social feeding activity is
        // actually taking place. It is never forced to full at job completion.
        public const float SharedMealRecreationFactor = 0.80f;
        public const float OneWayFeedingRecreationFactor = 0.60f;
        public const float BedsideFeedingRecreationFactor = 0.70f;
        // JoyUtility.JoyTickCheckEnd reads the participant's current JobDef. A
        // patient who correctly keeps vanilla LayDown/Wait_Downed has no Feed
        // Other joy kind, so calling that helper on them throws. Apply the same
        // vanilla per-tick joy amount directly through Need_Joy instead.
        private const float FeedOtherJoyPerTick = 0.36f / 2500f;
        // Small tolerance used only for floating-point comparison. It must never
        // be large enough to permit a genuinely oversized serving.
        public const float FoodFitEpsilon = 0.0001f;
        // A whole selected portion may discard at most this much nutrition when
        // the job-scoped fullness clamp stops at its active target. Waste is
        // converted back from excess fullness using the food's real concentration
        // and the pawn's live fullness-gain multiplier.
        public const float MaximumWastedNutrition = 1.0f;
        // Duration factors are relative to normal self-eating. Lower values mean
        // less chew time. The eater's live Eating Speed stat is still applied.
        public const float SharedEatingDurationFactor = 0.90f;
        public const float AssistedEatingDurationFactor = 0.85f;
        private const float MinimumEatingSpeedForDuration = 0.01f;

        public static float EatingDurationMultiplier(
            Pawn eater,
            Thing food,
            float relativeDurationFactor)
        {
            float factor = Mathf.Max(0.01f, relativeDurationFactor);
            IngestibleProperties ingestible = food?.def?.ingestible;
            if (eater == null || ingestible == null || !ingestible.useEatingSpeedStat)
            {
                return factor;
            }

            float eatingSpeed = Mathf.Max(
                MinimumEatingSpeedForDuration,
                eater.GetStatValue(StatDefOf.EatingSpeed));
            return factor / eatingSpeed;
        }

        /// <summary>
        /// Vanilla ChewIngestible accepts a fixed multiplier calculated before
        /// the toil starts. This version calculates the duration when chewing
        /// actually begins, after dispensers and split stacks have produced the
        /// real food item, and uses the fed/eating pawn's live Eating Speed stat.
        /// </summary>
        public static Toil ChewIngestibleWithEatingSpeed(
            Pawn chewer,
            float relativeDurationFactor,
            TargetIndex ingestibleInd,
            TargetIndex eatSurfaceInd = TargetIndex.None)
        {
            int durationTicks = 1;
            Toil toil = ToilMaker.MakeToil("ChewIngestibleWithEatingSpeed");
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Thing thing = actor?.CurJob?.GetTarget(ingestibleInd).Thing;
                if (actor == null || thing == null || !thing.IngestibleNow)
                {
                    actor?.jobs?.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                actor.pather?.StopDead();
                float multiplier = EatingDurationMultiplier(
                    chewer,
                    thing,
                    relativeDurationFactor);
                durationTicks = Mathf.Max(
                    1,
                    Mathf.RoundToInt(
                        thing.def.ingestible.baseIngestTicks * multiplier));
                actor.jobs.curDriver.ticksLeftThisToil = durationTicks;

                if (thing.Spawned && chewer?.Map != null)
                {
                    thing.Map.physicalInteractionReservationManager.Reserve(
                        chewer,
                        actor.CurJob,
                        thing);
                }
            };
            toil.tickIntervalAction = delegate(int delta)
            {
                Pawn actor = toil.actor;
                if (actor == null)
                {
                    return;
                }

                if (chewer != actor && chewer != null)
                {
                    actor.rotationTracker.FaceCell(chewer.Position);
                }
                else
                {
                    Thing thing = actor.CurJob?.GetTarget(ingestibleInd).Thing;
                    if (thing != null && thing.Spawned)
                    {
                        actor.rotationTracker.FaceCell(thing.Position);
                    }
                    else if (eatSurfaceInd != TargetIndex.None &&
                        actor.CurJob != null &&
                        actor.CurJob.GetTarget(eatSurfaceInd).IsValid)
                    {
                        actor.rotationTracker.FaceCell(
                            actor.CurJob.GetTarget(eatSurfaceInd).Cell);
                    }
                }

                actor.GainComfortFromCellIfPossible(delta);
            };
            toil.WithProgressBar(ingestibleInd, delegate
            {
                JobDriver driver = toil.actor?.jobs?.curDriver;
                return driver == null
                    ? 1f
                    : 1f - (float)driver.ticksLeftThisToil /
                        Mathf.Max(1, durationTicks);
            });
            toil.defaultCompleteMode = ToilCompleteMode.Delay;
            toil.FailOnDestroyedOrNull(ingestibleInd);
            toil.AddFinishAction(delegate
            {
                Pawn actor = toil.actor;
                Thing thing = actor?.CurJob?.GetTarget(ingestibleInd).Thing;
                Map map = thing?.MapHeld ?? chewer?.MapHeld;
                if (thing != null && chewer != null && map != null &&
                    map.physicalInteractionReservationManager.IsReservedBy(
                        chewer,
                        thing))
                {
                    map.physicalInteractionReservationManager.Release(
                        chewer,
                        actor?.CurJob,
                        thing);
                }
            });
            toil.handlingFacing = true;
            Toils_Ingest.AddIngestionEffects(
                toil,
                chewer,
                ingestibleInd,
                eatSurfaceInd);
            return toil;
        }

        public static void GainFeedOtherRecreation(
            Pawn participant,
            int delta,
            float extraJoyGainFactor)
        {
            if (participant?.needs?.joy == null || delta <= 0 ||
                extraJoyGainFactor <= 0f || FeedOtherDefOf.RR_FeedOtherJoy == null)
            {
                return;
            }

            participant.needs.joy.GainJoy(
                extraJoyGainFactor * FeedOtherJoyPerTick * delta,
                FeedOtherDefOf.RR_FeedOtherJoy);
        }

        public static bool IsFeedOtherJob(JobDef jobDef)
        {
            return jobDef == FeedOtherDefOf.RR_FeedOther ||
                jobDef == FeedOtherDefOf.RR_FeedOtherPartner ||
                jobDef == FeedOtherDefOf.RR_FeedOtherOneWay ||
                jobDef == FeedOtherDefOf.RR_FeedOtherBedside ||
                jobDef == FeedOtherDefOf.RR_BeFedOtherPartner;
        }

        public static bool IsFeedOtherEatingJob(JobDef jobDef)
        {
            return jobDef == FeedOtherDefOf.RR_FeedOther ||
                jobDef == FeedOtherDefOf.RR_FeedOtherPartner ||
                jobDef == FeedOtherDefOf.RR_FeedOtherBedside ||
                jobDef == FeedOtherDefOf.RR_BeFedOtherPartner;
        }

        // A healthy shared-meal pawn has a Feed Other job of their own. A patient
        // being fed in bed deliberately keeps vanilla LayDown/Wait_Downed, so the
        // caregiver's active job must also identify them as a Feed Other eater.
        public static bool IsActiveFeedOtherEater(Pawn eater)
        {
            if (eater == null)
            {
                return false;
            }

            if (IsFeedOtherEatingJob(eater.CurJobDef))
            {
                return true;
            }

            Pawn feeder;
            JobDriver driver;
            return TryGetCaregiverFeedingDriver(eater, out feeder, out driver);
        }

        public static bool TryGetActiveSessionFood(Pawn eater, out Thing food)
        {
            food = null;
            if (eater == null)
            {
                return false;
            }

            if (IsFeedOtherEatingJob(eater.CurJobDef))
            {
                Thing ownTarget = eater.CurJob?.targetA.Thing;
                if (ownTarget != null && !ownTarget.Destroyed && ownTarget.def.IsIngestible)
                {
                    food = ownTarget;
                    return true;
                }
            }

            Pawn feeder;
            JobDriver driver;
            if (!TryGetCaregiverFeedingDriver(eater, out feeder, out driver))
            {
                return false;
            }

            Thing carried = feeder.carryTracker?.CarriedThing;
            if (carried == null || carried.Destroyed || !carried.def.IsIngestible)
            {
                carried = feeder.CurJob?.targetA.Thing;
            }

            if (carried == null || carried.Destroyed || !carried.def.IsIngestible)
            {
                return false;
            }

            food = carried;
            return true;
        }

        public static void NotifyVeryFullReached(Pawn eater)
        {
            if (eater == null)
            {
                return;
            }

            TryGrantVeryFullMood(eater);
            TryGrantVeryFullDiscomfort(eater);

            JobDriver_FeedOtherBase sharedDriver = eater.jobs?.curDriver as JobDriver_FeedOtherBase;
            sharedDriver?.NotifyParticipantReachedVeryFull(eater);

            JobDriver_FeedOtherBedside ownBedsideDriver =
                eater.jobs?.curDriver as JobDriver_FeedOtherBedside;
            ownBedsideDriver?.NotifyParticipantReachedVeryFull(eater);

            Pawn feeder;
            JobDriver caregiverDriver;
            if (!TryGetCaregiverFeedingDriver(eater, out feeder, out caregiverDriver))
            {
                return;
            }

            JobDriver_FeedOtherOneWay oneWay = caregiverDriver as JobDriver_FeedOtherOneWay;
            oneWay?.NotifyParticipantReachedVeryFull(eater);

            JobDriver_FeedOtherBedside bedside = caregiverDriver as JobDriver_FeedOtherBedside;
            bedside?.NotifyParticipantReachedVeryFull(eater);
        }

        private static bool TryGetCaregiverFeedingDriver(
            Pawn eater,
            out Pawn feeder,
            out JobDriver driver)
        {
            feeder = null;
            driver = null;
            Map map = eater?.Map;
            if (map == null)
            {
                return false;
            }

            foreach (Pawn candidate in map.mapPawns.AllPawnsSpawned)
            {
                if (candidate == null || candidate == eater || candidate.CurJob == null ||
                    candidate.CurJob.targetB.Pawn != eater)
                {
                    continue;
                }

                JobDriver candidateDriver = candidate.jobs?.curDriver;
                if (candidateDriver is JobDriver_FeedOtherOneWay ||
                    candidateDriver is JobDriver_FeedOtherBedside)
                {
                    feeder = candidate;
                    driver = candidateDriver;
                    return true;
                }
            }

            return false;
        }

        // Direct right-click feeding is intentionally broader than autonomous
        // recreation. The selected pawn only needs to be a physically capable,
        // controllable humanlike feeder; weight opinion, exemption, diet mode,
        // age, fullness, sleep, drafting and the current job do not disqualify it.
        public static bool IsManualFeederEligible(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.Spawned && !pawn.Downed &&
                !pawn.InMentalState && pawn.RaceProps != null && pawn.RaceProps.Humanlike &&
                pawn.health != null && pawn.health.capacities != null &&
                pawn.health.capacities.CapableOf(RimWorld.PawnCapacityDefOf.Moving) &&
                pawn.health.capacities.CapableOf(RimWorld.PawnCapacityDefOf.Manipulation);
        }

        // The recipient may be asleep, drafted, exempt, or hold any weight
        // opinion. They still need an active RimRound fullness system and the
        // physical capacity to eat, otherwise the selected target cannot work.
        public static bool IsManualRecipientEligible(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.InMentalState ||
                pawn.RaceProps == null || !pawn.RaceProps.Humanlike || pawn.needs == null ||
                pawn.needs.food == null || pawn.needs.joy == null ||
                pawn.health == null || pawn.health.capacities == null ||
                !pawn.health.capacities.CapableOf(RimRound.Defs.PawnCapacityDefOf.Eating))
            {
                return false;
            }

            FullnessAndDietStats_ThingComp fullnessComp =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            return fullnessComp != null && !fullnessComp.Disabled &&
                fullnessComp.DietMode != DietMode.Disabled &&
                fullnessComp.FullnessGainedMultiplier > 0.001f;
        }

        public static bool IsRimRoundEligible(Pawn pawn)
        {
            return IsRimRoundEligible(pawn, false);
        }

        public static bool IsRimRoundEligibleForManualOrder(Pawn pawn)
        {
            return IsRimRoundEligible(pawn, true);
        }

        private static bool IsRimRoundEligible(Pawn pawn, bool allowSleeping)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Downed ||
                (!allowSleeping && !pawn.Awake()) ||
                pawn.Drafted || pawn.InMentalState || !pawn.RaceProps.Humanlike || pawn.needs == null ||
                pawn.needs.food == null || pawn.needs.joy == null)
            {
                return false;
            }

            FullnessAndDietStats_ThingComp fullnessComp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            PawnBodyType_ThingComp bodyComp = pawn.TryGetComp<PawnBodyType_ThingComp>();
            ThingComp_PawnAttitude attitudeComp = pawn.TryGetComp<ThingComp_PawnAttitude>();

            if (fullnessComp == null || fullnessComp.Disabled || fullnessComp.DietMode == DietMode.Disabled ||
                fullnessComp.FullnessGainedMultiplier <= 0.001f || bodyComp == null ||
                bodyComp.PersonallyExempt || bodyComp.CategoricallyExempt || attitudeComp == null ||
                attitudeComp.weightOpinion < WeightOpinion.NeutralPlus)
            {
                return false;
            }

            return pawn.health != null && pawn.health.capacities != null &&
                pawn.health.capacities.CapableOf(RimWorld.PawnCapacityDefOf.Moving) &&
                pawn.health.capacities.CapableOf(RimWorld.PawnCapacityDefOf.Manipulation) &&
                pawn.health.capacities.CapableOf(RimRound.Defs.PawnCapacityDefOf.Eating);
        }

        public static bool IsNonEatingOneWayFeederEligible(Pawn pawn)
        {
            return IsNonEatingOneWayFeederEligible(pawn, false);
        }

        public static bool IsNonEatingOneWayFeederEligibleForManualOrder(Pawn pawn)
        {
            return IsManualFeederEligible(pawn);
        }

        private static bool IsNonEatingOneWayFeederEligible(Pawn pawn, bool allowSleeping)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Downed ||
                (!allowSleeping && !pawn.Awake()) ||
                pawn.Drafted || pawn.InMentalState || !pawn.RaceProps.Humanlike || pawn.needs == null ||
                pawn.needs.joy == null || pawn.ageTracker == null || pawn.ageTracker.AgeBiologicalYears < 18)
            {
                return false;
            }

            PawnBodyType_ThingComp bodyComp = pawn.TryGetComp<PawnBodyType_ThingComp>();
            ThingComp_PawnAttitude attitudeComp = pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (bodyComp == null)
            {
                return false;
            }

            // A pawn who is personally or categorically outside RimRound can
            // still take the non-eating helper role regardless of attitude.
            // RimRound-enabled pawns qualify at Neutral or lower.
            bool exempt = bodyComp.PersonallyExempt || bodyComp.CategoricallyExempt;
            bool lowOpinion = attitudeComp != null && IsNeutralOrLower(attitudeComp.weightOpinion);
            if (!exempt && !lowOpinion)
            {
                return false;
            }

            // The feeder never ingests anything, so their food need, current
            // fullness, RimRound diet mode, and Eating capacity are deliberately
            // irrelevant to this path.
            return pawn.health != null && pawn.health.capacities != null &&
                pawn.health.capacities.CapableOf(RimWorld.PawnCapacityDefOf.Moving) &&
                pawn.health.capacities.CapableOf(RimWorld.PawnCapacityDefOf.Manipulation) &&
                (allowSleeping || !ShouldRemainInPlaceForFeeding(pawn));
        }

        public static bool IsBedsideFeederEligible(Pawn pawn)
        {
            return IsRimRoundEligible(pawn) && !ShouldRemainInPlaceForFeeding(pawn);
        }

        public static bool IsBedsideFeederEligibleForManualOrder(Pawn pawn)
        {
            // A normal sleeping pawn is currently lying in a bed, but a direct
            // player order is allowed to wake them. Moving capacity still
            // protects genuinely bed-bound or immobile pawns here.
            return IsRimRoundEligibleForManualOrder(pawn);
        }

        public static bool IsOneWayRecipientEligible(Pawn pawn)
        {
            return IsOneWayRecipientEligible(pawn, false);
        }

        public static bool IsOneWayRecipientEligibleForManualOrder(Pawn pawn)
        {
            return IsManualRecipientEligible(pawn);
        }

        private static bool IsOneWayRecipientEligible(Pawn pawn, bool allowSleeping)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned ||
                (!allowSleeping && !pawn.Awake()) || pawn.Drafted || pawn.InMentalState ||
                !pawn.RaceProps.Humanlike || pawn.needs == null ||
                pawn.needs.food == null || pawn.needs.joy == null)
            {
                return false;
            }

            FullnessAndDietStats_ThingComp fullnessComp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            PawnBodyType_ThingComp bodyComp = pawn.TryGetComp<PawnBodyType_ThingComp>();
            ThingComp_PawnAttitude attitudeComp = pawn.TryGetComp<ThingComp_PawnAttitude>();

            if (fullnessComp == null || fullnessComp.Disabled || fullnessComp.DietMode == DietMode.Disabled ||
                fullnessComp.FullnessGainedMultiplier <= 0.001f || bodyComp == null ||
                bodyComp.PersonallyExempt || bodyComp.CategoricallyExempt || attitudeComp == null ||
                attitudeComp.weightOpinion < WeightOpinion.NeutralPlus)
            {
                return false;
            }

            // A fed recipient does not need to move or manipulate the meal. The
            // feeder carries it and the feed-patient ingestion toils apply it.
            return pawn.health != null && pawn.health.capacities != null &&
                pawn.health.capacities.CapableOf(RimRound.Defs.PawnCapacityDefOf.Eating);
        }

        public static bool IsAvailablePartner(Pawn pawn)
        {
            return IsAvailablePartner(pawn, false);
        }

        public static bool IsAvailablePartnerForManualOrder(Pawn pawn)
        {
            return IsAvailablePartner(pawn, true);
        }

        private static bool IsAvailablePartner(Pawn pawn, bool manualOrder)
        {
            if (manualOrder)
            {
                // Manual right-click orders are allowed to wake and interrupt the
                // pawn. Eligibility, fullness and physical capability still apply.
                return IsRimRoundEligibleForManualOrder(pawn) &&
                    !ShouldRemainInPlaceForFeeding(pawn) &&
                    !IsAtOrAboveStartingFullnessCutoff(pawn) &&
                    pawn.jobs != null && !JoyUtility.LordPreventsGettingJoy(pawn);
            }

            if (!IsRimRoundEligible(pawn) || ShouldRemainInPlaceForFeeding(pawn) ||
                IsAtOrAboveStartingFullnessCutoff(pawn) || pawn.jobs == null ||
                pawn.carryTracker?.CarriedThing != null ||
                !pawn.jobs.IsCurrentJobPlayerInterruptible() || JoyUtility.LordPreventsGettingJoy(pawn) ||
                JoyUtility.TimetablePreventsGettingJoy(pawn) ||
                !SocialInteractionUtility.CanInitiateInteraction(pawn, null) ||
                !SocialInteractionUtility.CanReceiveRandomInteraction(pawn))
            {
                return false;
            }

            Job currentJob = pawn.CurJob;
            if (currentJob != null && (currentJob.playerForced || IsFeedOtherJob(currentJob.def) ||
                (!pawn.mindState.IsIdle && currentJob.def.joyKind == null)))
            {
                return false;
            }

            return true;
        }

        public static bool IsAvailableOneWayRecipient(Pawn pawn)
        {
            return IsAvailableOneWayRecipient(pawn, false);
        }

        public static bool IsAvailableOneWayRecipientForManualOrder(Pawn pawn)
        {
            return IsAvailableOneWayRecipient(pawn, true);
        }

        private static bool IsAvailableOneWayRecipient(Pawn pawn, bool manualOrder)
        {
            bool remainsInPlace = ShouldRemainInPlaceForFeeding(pawn);
            if (manualOrder)
            {
                // The right-click command may replace ordinary movement, work
                // and forced jobs. A sleeping mobile pawn in bed is instead
                // suspended in place and restored after the feeding session.
                return IsManualRecipientEligible(pawn) &&
                    !IsAtOrAboveStartingFullnessCutoff(pawn) && pawn.jobs != null;
            }

            if (!IsOneWayRecipientEligible(pawn) || IsAtOrAboveStartingFullnessCutoff(pawn) || pawn.jobs == null ||
                pawn.carryTracker?.CarriedThing != null ||
                (!remainsInPlace && !pawn.jobs.IsCurrentJobPlayerInterruptible()) ||
                JoyUtility.LordPreventsGettingJoy(pawn) ||
                JoyUtility.TimetablePreventsGettingJoy(pawn) ||
                (!remainsInPlace && !SocialInteractionUtility.CanReceiveRandomInteraction(pawn)))
            {
                return false;
            }

            Job currentJob = pawn.CurJob;
            if (currentJob != null && (currentJob.playerForced || IsFeedOtherJob(currentJob.def) ||
                (!remainsInPlace && !pawn.mindState.IsIdle && currentJob.def.joyKind == null)))
            {
                return false;
            }

            return true;
        }

        public static bool IsMobileBedLockCandidate(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Downed ||
                pawn.Drafted || pawn.InMentalState || pawn.jobs == null ||
                pawn.jobs.curDriver == null || !(pawn.jobs.curDriver is JobDriver_LayDown) ||
                pawn.CurJob == null || pawn.CurJob.playerForced ||
                !pawn.jobs.IsCurrentJobPlayerInterruptible() ||
                !pawn.GetPosture().InBed() || !IsOnBedCell(pawn))
            {
                return false;
            }

            return pawn.health != null && pawn.health.capacities != null &&
                pawn.health.capacities.CapableOf(RimWorld.PawnCapacityDefOf.Moving) &&
                pawn.health.capacities.CapableOf(RimRound.Defs.PawnCapacityDefOf.Eating);
        }

        public static bool IsSleepingMobileBedFallbackCandidate(Pawn pawn)
        {
            return pawn != null && !pawn.Awake() && IsMobileBedLockCandidate(pawn);
        }

        public static bool IsSleepingSharedPartnerFallbackEligible(Pawn pawn)
        {
            return IsSleepingMobileBedFallbackCandidate(pawn) &&
                IsRimRoundEligible(pawn, true);
        }

        public static bool IsSleepingOneWayRecipientFallbackEligible(Pawn pawn)
        {
            return IsSleepingMobileBedFallbackCandidate(pawn) &&
                IsOneWayRecipientEligible(pawn, true);
        }

        public static bool IsAvailableSleepingSharedPartnerFallback(Pawn pawn)
        {
            return IsSleepingSharedPartnerFallbackEligible(pawn) &&
                !IsAtOrAboveStartingFullnessCutoff(pawn) &&
                pawn.carryTracker?.CarriedThing == null &&
                !JoyUtility.LordPreventsGettingJoy(pawn) &&
                pawn.CurJob != null && !IsFeedOtherJob(pawn.CurJob.def);
        }

        public static bool IsAvailableSleepingOneWayRecipientFallback(Pawn pawn)
        {
            return IsSleepingOneWayRecipientFallbackEligible(pawn) &&
                !IsAtOrAboveStartingFullnessCutoff(pawn) &&
                pawn.carryTracker?.CarriedThing == null &&
                !JoyUtility.LordPreventsGettingJoy(pawn) &&
                pawn.CurJob != null && !IsFeedOtherJob(pawn.CurJob.def);
        }

        public static bool ShouldRemainInPlaceForFeeding(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            bool cannotMove = pawn.health?.capacities == null ||
                !pawn.health.capacities.CapableOf(RimWorld.PawnCapacityDefOf.Moving);
            bool layingDown = pawn.Downed || pawn.GetPosture().Laying() ||
                pawn.jobs?.curDriver is JobDriver_LayDown;
            return cannotMove || layingDown || IsOnBedCell(pawn);
        }

        public static bool ShouldRemainInPlaceForManualFeeding(Pawn pawn)
        {
            // Manual feeding now uses the same authoritative in-place test as
            // autonomous bedside feeding. Healthy sleepers and awake resters keep
            // their LayDown job and bed, while downed/immobile recipients retain
            // their native patient task. Ordinary standing mobile recipients still
            // use the nearby-chair or meet-feeder route.
            return ShouldRemainInPlaceForFeeding(pawn);
        }

        public static bool IsOnBedCell(Pawn pawn)
        {
            return pawn != null && IsBedCell(pawn.Position, pawn.Map);
        }

        public static bool IsBedCell(IntVec3 cell, Map map)
        {
            if (!cell.IsValid || map == null || !cell.InBounds(map))
            {
                return false;
            }

            List<Thing> things = cell.GetThingList(map);
            for (int index = 0; index < things.Count; index++)
            {
                if (things[index] is Building_Bed)
                {
                    return true;
                }
            }

            return false;
        }

        public static float FeedingTarget(Pawn pawn)
        {
            FullnessAndDietStats_ThingComp comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return comp == null ? 0f : comp.HardLimit * FeedingTargetFractionOfHardLimit;
        }

        public static float FeedingCompletionTolerance(Pawn pawn)
        {
            FullnessAndDietStats_ThingComp comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return comp == null
                ? 0f
                : Mathf.Max(
                    MinimumFeedingCompletionTolerance,
                    comp.HardLimit * FeedingCompletionToleranceFractionOfHardLimit);
        }

        public static float FeedingCompletionThreshold(Pawn pawn)
        {
            return Mathf.Max(0f, FeedingTarget(pawn) - FeedingCompletionTolerance(pawn));
        }

        public static bool HasReachedFeedingTarget(Pawn pawn)
        {
            FullnessAndDietStats_ThingComp comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return comp != null &&
                comp.CurrentFullness + FoodFitEpsilon >= FeedingCompletionThreshold(pawn);
        }

        public static float RepeatedTopUpCompletionThreshold(Pawn pawn)
        {
            FullnessAndDietStats_ThingComp comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null)
            {
                return 0f;
            }

            float tolerance = Mathf.Max(
                MinimumRepeatedTopUpCompletionTolerance,
                comp.HardLimit * RepeatedTopUpCompletionToleranceFractionOfHardLimit);
            return Mathf.Max(0f, FeedingTarget(pawn) - tolerance);
        }

        public static bool ShouldCompleteAfterRepeatedTopUps(Pawn pawn, int completedTopUpRounds)
        {
            // Save/API compatibility only. Repeated small-food batches can no
            // longer finish a session below the selected target; they simply
            // cause another food search until it is crossed or food runs out.
            return completedTopUpRounds > 0 && HasReachedFeedingTarget(pawn);
        }

        public static bool HasReachedVeryFull(Pawn pawn)
        {
            FullnessAndDietStats_ThingComp comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return comp != null && comp.CurrentFullness + FoodFitEpsilon >=
                comp.HardLimit * VeryFullFractionOfHardLimit;
        }

        public static void TryGrantVeryFullMood(Pawn pawn)
        {
            if (!FeedOtherMod.Settings.positiveVeryFullMoodEnabled || pawn == null ||
                FeedOtherDefOf.RR_FeedOtherVeryFullMood == null ||
                !HasReachedVeryFull(pawn))
            {
                return;
            }

            ThingComp_PawnAttitude attitudeComp = pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (attitudeComp == null)
            {
                return;
            }

            int moodStage;
            switch (attitudeComp.weightOpinion)
            {
                case WeightOpinion.NeutralPlus:
                case WeightOpinion.Like:
                    moodStage = 0;
                    break;
                case WeightOpinion.Love:
                    moodStage = 1;
                    break;
                case WeightOpinion.Fanatical:
                case WeightOpinion.Extreme:
                    moodStage = 2;
                    break;
                default:
                    return;
            }

            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            if (memories != null &&
                memories.GetFirstMemoryOfDef(FeedOtherDefOf.RR_FeedOtherVeryFullMood) == null)
            {
                memories.TryGainMemory(
                    ThoughtMaker.MakeThought(FeedOtherDefOf.RR_FeedOtherVeryFullMood, moodStage),
                    null);
            }
        }

        public static void TryGrantVeryFullDiscomfort(Pawn pawn)
        {
            if (!FeedOtherMod.Settings.negativeVeryFullMoodEnabled || pawn == null ||
                FeedOtherDefOf.RR_FeedOtherVeryFullDiscomfort == null ||
                !HasReachedVeryFull(pawn))
            {
                return;
            }

            ThingComp_PawnAttitude attitudeComp = pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (attitudeComp == null)
            {
                return;
            }

            int moodStage;
            switch (attitudeComp.weightOpinion)
            {
                case WeightOpinion.NeutralMinus:
                    moodStage = 0;
                    break;
                case WeightOpinion.Dislike:
                    moodStage = 1;
                    break;
                case WeightOpinion.Hate:
                    moodStage = 2;
                    break;
                default:
                    return;
            }

            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            if (memories != null &&
                memories.GetFirstMemoryOfDef(FeedOtherDefOf.RR_FeedOtherVeryFullDiscomfort) == null)
            {
                memories.TryGainMemory(
                    ThoughtMaker.MakeThought(FeedOtherDefOf.RR_FeedOtherVeryFullDiscomfort, moodStage),
                    null);
            }
        }

        public static bool IsAtOrAboveStartingFullnessCutoff(Pawn pawn)
        {
            FullnessAndDietStats_ThingComp comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return comp != null && comp.CurrentFullness >
                comp.HardLimit * MaximumStartingFullnessFractionOfHardLimit;
        }

        public static bool IsLikeOrHigher(Pawn pawn)
        {
            WeightOpinion opinion = pawn?.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion
                ?? WeightOpinion.None;
            return opinion >= WeightOpinion.Like;
        }

        public static bool AutonomousCooldownBlocksPair(Pawn first, Pawn second)
        {
            if (first == null || second == null)
            {
                return true;
            }

            // Like, Love, Fanatical and Extreme pawns may feed or be fed again
            // immediately whenever the recipient is otherwise eligible. Keep
            // recording cooldowns so the other participant is still blocked
            // from unrelated lower-opinion pairings.
            if (FeedOtherMod.Settings.likeOrHigherCooldownBypass &&
                (IsLikeOrHigher(first) || IsLikeOrHigher(second)))
            {
                return false;
            }

            return FeedOtherCooldownComponent.IsParticipantOnCooldown(first) ||
                FeedOtherCooldownComponent.IsParticipantOnCooldown(second) ||
                FeedOtherCooldownComponent.IsPairOnCooldown(first, second);
        }

        public static bool CooldownBlocksManualOrder(Pawn first, Pawn second)
        {
            return !FeedOtherMod.Settings.manualOrdersIgnoreCooldowns &&
                AutonomousCooldownBlocksPair(first, second);
        }

        public static bool ShouldRecordSessionCooldown(bool manualOrder)
        {
            return !manualOrder || !FeedOtherMod.Settings.manualOrdersIgnoreCooldowns;
        }

        public static float OpinionChanceFactor(Pawn pawn)
        {
            WeightOpinion opinion = pawn?.TryGetComp<ThingComp_PawnAttitude>()?.weightOpinion ?? WeightOpinion.None;
            switch (opinion)
            {
                case WeightOpinion.NeutralPlus:
                    return 1f;
                case WeightOpinion.Like:
                    return 1.15f;
                case WeightOpinion.Love:
                    return 1.30f;
                case WeightOpinion.Fanatical:
                    return 1.45f;
                case WeightOpinion.Extreme:
                    return 1.50f;
                default:
                    return 0f;
            }
        }

        public static float RelationshipChanceFactor(Pawn initiator, Pawn partner, bool bedside)
        {
            if (initiator == null || partner == null)
            {
                return 1f;
            }

            if (LovePartnerRelationUtility.LovePartnerRelationExists(initiator, partner))
            {
                return bedside ? 1.35f : 1.25f;
            }

            int opinion = initiator.relations?.OpinionOf(partner) ?? 0;
            if (opinion >= 40)
            {
                return bedside ? 1.15f : 1.10f;
            }

            return 1f;
        }

        public static float FullnessFractionOfHardLimit(Pawn pawn)
        {
            FullnessAndDietStats_ThingComp comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || comp.HardLimit <= 0.001f)
            {
                return 1f;
            }

            return Mathf.Max(0f, comp.CurrentFullness / comp.HardLimit);
        }

        public static float SituationalChanceAdjustment(
            Pawn initiator,
            Pawn partner,
            Thing meal,
            bool sharedMeal,
            bool bedside)
        {
            if (initiator == null || partner == null || meal == null)
            {
                return 0f;
            }

            float adjustment = 0f;
            float initiatorFullness = FullnessFractionOfHardLimit(initiator);
            float partnerFullness = FullnessFractionOfHardLimit(partner);

            if (sharedMeal && initiatorFullness < LowFullnessBonusThreshold &&
                partnerFullness < LowFullnessBonusThreshold)
            {
                adjustment += 0.50f;
            }

            if (partnerFullness < VeryLowFullnessBonusThreshold)
            {
                adjustment += 0.75f;
            }

            float mealDistanceSquared = initiator.Position.DistanceToSquared(meal.Position);
            bool nearbyMeal = mealDistanceSquared <= NearbyMealDistance * NearbyMealDistance;
            if (mealDistanceSquared > FarMealDistance * FarMealDistance)
            {
                adjustment -= 0.50f;
            }

            bool practicalLocation = false;
            if (bedside)
            {
                practicalLocation = initiator.Position.DistanceToSquared(partner.Position) <=
                    NearbyMealDistance * NearbyMealDistance;
            }
            else if (sharedMeal)
            {
                Thing partnerMeal;
                IntVec3 initiatorSeat;
                IntVec3 partnerSeat;
                practicalLocation = TryFindStoredMeal(partner, meal.Position, meal, out partnerMeal) &&
                    TryFindSharedDiningSpots(
                        initiator,
                        partner,
                        meal,
                        partnerMeal,
                        out initiatorSeat,
                        out partnerSeat);
            }
            else
            {
                IntVec3 diningSeat;
                practicalLocation = TryFindDiningSeat(
                        partner,
                        meal,
                        meal.Position,
                        out diningSeat) &&
                    partner.Position.DistanceToSquared(diningSeat) <=
                        OneWayDiningSeatSearchRadius * OneWayDiningSeatSearchRadius;
            }

            if (nearbyMeal && practicalLocation)
            {
                adjustment += 0.25f;
            }

            return adjustment;
        }

        public static float ClampAutonomousRecreationChance(float chance)
        {
            return Mathf.Clamp(
                chance * FeedOtherMod.Settings.autonomousFrequencyMultiplier,
                0f,
                MaximumAutonomousRecreationChance);
        }

        public static bool TryFindPartnerAndMeal(Pawn initiator, out Pawn partner, out Thing initiatorMeal)
        {
            partner = null;
            initiatorMeal = null;

            if (!IsRimRoundEligible(initiator) || ShouldRemainInPlaceForFeeding(initiator) ||
                IsAtOrAboveStartingFullnessCutoff(initiator) || initiator.Map == null ||
                initiator.carryTracker?.CarriedThing != null ||
                !SocialInteractionUtility.CanInitiateInteraction(initiator, null))
            {
                return false;
            }

            List<Pawn> candidates = OrderCandidatesWithRomanticPartnersFirst(
                initiator,
                initiator.Map.mapPawns.AllPawnsSpawned
                    .Where(candidate => IsValidPartnerFor(initiator, candidate)));

            List<Thing> initiatorMeals = SessionMealSources(
                initiator,
                initiator,
                IntVec3.Invalid,
                null);

            // Check every viable meal pairing with a current romantic partner
            // before considering unrelated pawns. If the partner is unavailable
            // or has no suitable reachable meal, the normal candidates remain as
            // a fallback.
            foreach (Pawn candidate in candidates)
            {
                foreach (Thing meal in initiatorMeals)
                {
                    Building_FoodFaucet faucet = meal as Building_FoodFaucet;
                    if (faucet != null)
                    {
                        if (CanFaucetSupplyCombined(
                            faucet,
                            initiator,
                            candidate))
                        {
                            partner = candidate;
                            initiatorMeal = faucet;
                            return true;
                        }
                        continue;
                    }

                    Thing candidateMeal;
                    if (TryFindStoredMeal(candidate, meal.Position, meal, out candidateMeal))
                    {
                        partner = candidate;
                        initiatorMeal = meal;
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool TryFindPartnerAndMeal(
            Pawn initiator,
            Pawn requestedPartner,
            out Thing initiatorMeal)
        {
            initiatorMeal = null;

            if (!IsRimRoundEligibleForManualOrder(initiator) ||
                IsAtOrAboveStartingFullnessCutoff(initiator) || initiator.Map == null ||
                !IsValidPartnerFor(initiator, requestedPartner, true))
            {
                return false;
            }

            foreach (Thing meal in SessionMealSources(
                initiator,
                initiator,
                IntVec3.Invalid,
                null))
            {
                Building_FoodFaucet faucet = meal as Building_FoodFaucet;
                if (faucet != null)
                {
                    if (CanFaucetSupplyCombined(
                        faucet,
                        initiator,
                        requestedPartner))
                    {
                        initiatorMeal = faucet;
                        return true;
                    }
                    continue;
                }

                Thing partnerMeal;
                if (TryFindStoredMeal(requestedPartner, meal.Position, meal, out partnerMeal))
                {
                    initiatorMeal = meal;
                    return true;
                }
            }

            return false;
        }

        public static bool TryFindFeedeeAndMeal(Pawn feeder, out Pawn feedee, out Thing meal)
        {
            feedee = null;
            meal = null;

            if (!IsNonEatingOneWayFeederEligible(feeder) || ShouldRemainInPlaceForFeeding(feeder) || feeder.Map == null ||
                feeder.carryTracker?.CarriedThing != null ||
                !SocialInteractionUtility.CanInitiateInteraction(feeder, null))
            {
                return false;
            }

            List<Pawn> candidates = OrderCandidatesWithRomanticPartnersFirst(
                feeder,
                feeder.Map.mapPawns.AllPawnsSpawned
                    .Where(candidate => IsValidFeedeeFor(feeder, candidate, false)));

            foreach (Pawn candidate in candidates)
            {
                Thing candidateMeal;
                if (TryFindStoredMealForFeeding(feeder, candidate, IntVec3.Invalid, out candidateMeal))
                {
                    feedee = candidate;
                    meal = candidateMeal;
                    return true;
                }
            }

            return false;
        }

        public static bool TryFindSleepingOneWayFeedeeAndMeal(
            Pawn feeder,
            out Pawn feedee,
            out Thing meal)
        {
            return TryFindSleepingBedFallbackAndMeal(feeder, false, out feedee, out meal);
        }

        public static bool TryFindSleepingSharedPartnerAndMeal(
            Pawn feeder,
            out Pawn feedee,
            out Thing meal)
        {
            return TryFindSleepingBedFallbackAndMeal(feeder, true, out feedee, out meal);
        }

        private static bool TryFindSleepingBedFallbackAndMeal(
            Pawn feeder,
            bool sharedPartner,
            out Pawn feedee,
            out Thing meal)
        {
            feedee = null;
            meal = null;

            if (!FeedOtherMod.Settings.bedsideFeedingEnabled)
            {
                return false;
            }

            bool feederEligible = sharedPartner
                ? IsBedsideFeederEligible(feeder) &&
                    !IsAtOrAboveStartingFullnessCutoff(feeder)
                : IsNonEatingOneWayFeederEligible(feeder);

            if (!feederEligible || ShouldRemainInPlaceForFeeding(feeder) ||
                feeder.Map == null || feeder.carryTracker?.CarriedThing != null ||
                !SocialInteractionUtility.CanInitiateInteraction(feeder, null))
            {
                return false;
            }

            List<Pawn> candidates = OrderCandidatesWithRomanticPartnersFirst(
                feeder,
                feeder.Map.mapPawns.AllPawnsSpawned.Where(candidate =>
                    IsValidSleepingBedFallbackFor(feeder, candidate, sharedPartner)));

            foreach (Pawn candidate in candidates)
            {
                Thing candidateMeal;
                if (TryFindStoredMealForFeeding(
                    feeder,
                    candidate,
                    IntVec3.Invalid,
                    out candidateMeal))
                {
                    feedee = candidate;
                    meal = candidateMeal;
                    return true;
                }
            }

            return false;
        }

        public static bool TryFindFeedeeAndMeal(Pawn feeder, Pawn requestedFeedee, out Thing meal)
        {
            meal = null;

            if (!IsManualFeederEligible(feeder) || feeder.Map == null ||
                !IsValidFeedeeFor(feeder, requestedFeedee, false, true))
            {
                return false;
            }

            return TryFindStoredMealForFeeding(
                feeder,
                requestedFeedee,
                IntVec3.Invalid,
                out meal);
        }

        public static bool TryFindImmobileFeedeeAndMeal(Pawn feeder, out Pawn feedee, out Thing meal)
        {
            feedee = null;
            meal = null;

            if (!FeedOtherMod.Settings.bedsideFeedingEnabled)
            {
                return false;
            }

            if (!IsBedsideFeederEligible(feeder) || ShouldRemainInPlaceForFeeding(feeder) || feeder.Map == null ||
                feeder.carryTracker?.CarriedThing != null ||
                !SocialInteractionUtility.CanInitiateInteraction(feeder, null))
            {
                return false;
            }

            List<Pawn> candidates = OrderCandidatesWithRomanticPartnersFirst(
                feeder,
                feeder.Map.mapPawns.AllPawnsSpawned
                    .Where(candidate => IsValidFeedeeFor(feeder, candidate, true)));

            foreach (Pawn candidate in candidates)
            {
                Thing candidateMeal;
                if (TryFindStoredMealForFeeding(feeder, candidate, IntVec3.Invalid, out candidateMeal))
                {
                    feedee = candidate;
                    meal = candidateMeal;
                    return true;
                }
            }

            return false;
        }

        public static bool TryFindImmobileFeedeeAndMeal(
            Pawn feeder,
            Pawn requestedFeedee,
            out Thing meal)
        {
            meal = null;

            if (!IsBedsideFeederEligibleForManualOrder(feeder) ||
                IsAtOrAboveStartingFullnessCutoff(feeder) || feeder.Map == null ||
                !IsValidFeedeeFor(feeder, requestedFeedee, true, true))
            {
                return false;
            }

            return TryFindStoredMealForFeeding(
                feeder,
                requestedFeedee,
                IntVec3.Invalid,
                out meal);
        }

        public static bool TryFindStoredMeal(
            Pawn pawn,
            IntVec3 anchor,
            Thing otherPawnMeal,
            out Thing meal)
        {
            meal = SessionMealSources(
                    pawn,
                    pawn,
                    anchor,
                    otherPawnMeal)
                .FirstOrDefault();
            return meal != null;
        }

        public static bool TryFindStoredMealForFeeding(
            Pawn feeder,
            Pawn feedee,
            IntVec3 anchor,
            out Thing meal)
        {
            meal = OrderMealCandidatesByFit(
                    feedee,
                    feeder,
                    StoredMealCandidates(feedee, anchor, null, feeder)
                        .Where(candidate => FeederCanCollectMeal(feeder, candidate)),
                    anchor)
                .FirstOrDefault();
            if (meal != null)
            {
                return true;
            }

            Building_FoodFaucet faucet;
            if (TryFindSessionFaucet(
                feeder,
                feedee,
                anchor,
                null,
                out faucet))
            {
                meal = faucet;
                return true;
            }

            return false;
        }

        public static bool TryFindNearbyStoredMeal(
            Pawn pawn,
            IntVec3 sessionOrigin,
            Thing otherPawnMeal,
            out Thing meal)
        {
            meal = SessionMealSources(
                    pawn,
                    pawn,
                    sessionOrigin,
                    otherPawnMeal)
                .Where(source => IsMealSourceWithinDistance(
                    source,
                    sessionOrigin,
                    FollowUpMealSearchRadius))
                .FirstOrDefault();
            return meal != null;
        }

        public static bool TryFindNearbyStoredMealForFeeding(
            Pawn feeder,
            Pawn feedee,
            IntVec3 sessionOrigin,
            out Thing meal)
        {
            meal = OrderMealCandidatesByFit(
                    feedee,
                    feeder,
                    StoredMealCandidates(
                            feedee,
                            sessionOrigin,
                            null,
                            feeder)
                        .Where(candidate => FeederCanCollectMeal(feeder, candidate))
                        .Where(candidate => IsMealSourceWithinDistance(
                            candidate,
                            sessionOrigin,
                            FollowUpMealSearchRadius)),
                    sessionOrigin)
                .FirstOrDefault();
            if (meal != null)
            {
                return true;
            }

            Building_FoodFaucet faucet;
            if (TryFindSessionFaucet(
                    feeder,
                    feedee,
                    sessionOrigin,
                    null,
                    out faucet) &&
                IsMealSourceWithinDistance(
                    faucet,
                    sessionOrigin,
                    FollowUpMealSearchRadius))
            {
                meal = faucet;
                return true;
            }

            return false;
        }

        public static bool IsMealSourceWithinDistance(
            Thing source,
            IntVec3 origin,
            float radius)
        {
            if (source == null || !origin.IsValid || radius < 0f)
            {
                return false;
            }

            Building_FoodFaucet faucet = source as Building_FoodFaucet;
            IntVec3 sourceCell = faucet != null
                ? faucet.InteractionCell
                : source.Position;
            return sourceCell.IsValid &&
                origin.DistanceToSquared(sourceCell) <= radius * radius;
        }

        public static float EstimatedFullnessGain(Pawn eater, Thing food)
        {
            if (food == null)
            {
                return 0f;
            }

            float nutrition = FoodUtility.NutritionForEater(eater, food);
            return EstimatedFullnessGain(
                eater,
                nutrition,
                FullnessToNutritionRatio(food));
        }

        public static float EstimatedFullnessGain(
            Pawn eater,
            float nutrition,
            float fullnessToNutritionRatio)
        {
            FullnessAndDietStats_ThingComp comp =
                eater?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || nutrition <= FoodFitEpsilon)
            {
                return 0f;
            }

            return Mathf.Max(
                0f,
                nutrition * Mathf.Max(FoodFitEpsilon, fullnessToNutritionRatio) *
                    comp.FullnessGainedMultiplier);
        }

        public static float FullnessToNutritionRatio(Thing food)
        {
            CompFoodNetworkServing networkServing =
                food?.TryGetComp<CompFoodNetworkServing>();
            if (networkServing != null && networkServing.IsInitialized)
            {
                return networkServing.FullnessToNutritionRatio;
            }

            return food?.TryGetComp<ThingComp_FoodItems_NutritionDensity>()
                       ?.Props?.fullnessToNutritionRatio ??
                FullnessAndDietStats_ThingComp.defaultFullnessToNutritionRatio;
        }

        public static float RemainingFeedingCapacity(Pawn eater)
        {
            FullnessAndDietStats_ThingComp comp = eater?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || HasReachedFeedingTarget(eater))
            {
                return 0f;
            }

            return Mathf.Max(0f, FeedingTarget(eater) - comp.CurrentFullness);
        }

        private static float NutritionWasteFromFullness(
            float totalNutrition,
            float projectedFullnessGain,
            float remainingFullness)
        {
            if (totalNutrition <= FoodFitEpsilon ||
                projectedFullnessGain <= FoodFitEpsilon ||
                remainingFullness <= FoodFitEpsilon)
            {
                return float.MaxValue;
            }

            float excessFullness = Mathf.Max(
                0f,
                projectedFullnessGain - remainingFullness);
            return excessFullness <= FoodFitEpsilon
                ? 0f
                : totalNutrition * excessFullness / projectedFullnessGain;
        }

        public static bool IsNutritionWasteAcceptable(float nutritionWaste)
        {
            return nutritionWaste <= MaximumWastedNutrition + FoodFitEpsilon;
        }

        public static float ProjectedSessionNutritionWaste(
            Pawn eater,
            Thing food,
            int units = 1)
        {
            if (eater == null || food == null || units <= 0)
            {
                return float.MaxValue;
            }

            float nutritionPerUnit = FoodUtility.NutritionForEater(eater, food);
            float gainPerUnit = EstimatedFullnessGain(eater, food);
            return NutritionWasteFromFullness(
                nutritionPerUnit * units,
                gainPerUnit * units,
                RemainingFeedingCapacity(eater));
        }

        public static bool IsSessionPortionFitAcceptable(
            Pawn eater,
            Thing food,
            int units = 1)
        {
            return IsNutritionWasteAcceptable(
                ProjectedSessionNutritionWaste(eater, food, units));
        }

        public static float ProjectedNutritionWasteAtFullnessTarget(
            Pawn eater,
            Thing food,
            int units,
            float targetFullness)
        {
            FullnessAndDietStats_ThingComp fullness =
                eater?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness == null || food == null || units <= 0)
            {
                return float.MaxValue;
            }

            float nutritionPerUnit = FoodUtility.NutritionForEater(eater, food);
            float gainPerUnit = EstimatedFullnessGain(eater, food);
            float remaining = Mathf.Max(
                0f,
                targetFullness - fullness.CurrentFullness);
            return NutritionWasteFromFullness(
                nutritionPerUnit * units,
                gainPerUnit * units,
                remaining);
        }

        public static bool IsPortionFitAtFullnessTarget(
            Pawn eater,
            Thing food,
            int units,
            float targetFullness)
        {
            return IsNutritionWasteAcceptable(
                ProjectedNutritionWasteAtFullnessTarget(
                    eater,
                    food,
                    units,
                    targetFullness));
        }

        public static float ProjectedSelfFeedingNutritionWaste(
            Pawn eater,
            Thing food,
            int units = 1)
        {
            if (eater == null || food == null || units <= 0 ||
                food is Building_NutrientPasteDispenser ||
                food is Building_FoodFaucet ||
                food.def?.ingestible == null)
            {
                return 0f;
            }

            float nutritionPerUnit = FoodUtility.NutritionForEater(eater, food);
            float totalNutrition = nutritionPerUnit * units;
            if (totalNutrition <= FoodFitEpsilon)
            {
                return float.MaxValue;
            }

            FullnessAndDietStats_ThingComp fullness =
                eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness == null || fullness.Disabled ||
                fullness.DietMode == DietMode.Disabled)
            {
                float wanted = eater.needs?.food == null
                    ? totalNutrition
                    : Mathf.Max(0f, eater.needs.food.NutritionWanted);
                return Mathf.Max(0f, totalNutrition - wanted);
            }

            Pair<float, float> ranges;
            try
            {
                ranges = fullness.GetRanges();
            }
            catch (Exception)
            {
                // Never break a vanilla food search because another mod left the
                // fullness component temporarily uninitialised.
                return 0f;
            }

            if (fullness.DietMode == DietMode.Nutrition)
            {
                if (eater.needs?.food == null)
                {
                    return 0f;
                }

                float digestingNutrition = fullness.CurrentFullness /
                    Mathf.Max(
                        FoodFitEpsilon,
                        fullness.CurrentFullnessToNutritionRatio);
                float remainingNutrition = Mathf.Max(
                    0f,
                    ranges.Second - eater.needs.food.CurLevel -
                        digestingNutrition);
                return Mathf.Max(0f, totalNutrition - remainingNutrition);
            }

            float target = ranges.Second;
            if (!fullness.SetAboveHardLimit)
            {
                target = Mathf.Min(target, fullness.HardLimit);
            }

            float gainPerUnit = EstimatedFullnessGain(eater, food);
            float remainingFullness = Mathf.Max(
                0f,
                target - fullness.CurrentFullness);
            return NutritionWasteFromFullness(
                totalNutrition,
                gainPerUnit * units,
                remainingFullness);
        }

        public static bool IsSelfFeedingPortionFitAcceptable(
            Pawn eater,
            Thing food,
            int units = 1)
        {
            return IsNutritionWasteAcceptable(
                ProjectedSelfFeedingNutritionWaste(eater, food, units));
        }

        public static bool IsAutomaticFoodSelectionFitAcceptable(
            Pawn eater,
            Thing food,
            int units = 1)
        {
            if (PrisonerFatteningFoodPatch.IsFattenPrisoner(eater))
            {
                FullnessAndDietStats_ThingComp fullness =
                    eater?.TryGetComp<FullnessAndDietStats_ThingComp>();
                return fullness != null &&
                    IsPortionFitAtFullnessTarget(
                        eater,
                        food,
                        units,
                        PrisonerFatteningFoodPatch.FattenFullnessTarget(
                            eater,
                            fullness));
            }

            return IsSelfFeedingPortionFitAcceptable(eater, food, units);
        }

        public static bool CanFullyConsumeOneUnit(Pawn eater, Thing food)
        {
            float gain = EstimatedFullnessGain(eater, food);
            return gain > FoodFitEpsilon &&
                RemainingFeedingCapacity(eater) > FoodFitEpsilon &&
                IsSessionPortionFitAcceptable(eater, food, 1);
        }

        // Prepared meals remain individual servings. Small stackable foods are
        // treated as one pre-calculated batch so berries and similar top-up food
        // are eaten in a single feeding action rather than one item per loop.
        public static int PlannedIngestUnitCount(Pawn eater, Thing food)
        {
            if (!CanFullyConsumeOneUnit(eater, food))
            {
                return 0;
            }

            if (IsPreparedMeal(food))
            {
                return 1;
            }

            int safeUnits = MaximumWholeUnitsThatFit(eater, food);
            return Mathf.Min(food.stackCount, safeUnits);
        }

        public static bool CanFullyConsumePlannedPortion(Pawn eater, Thing food)
        {
            int plannedUnits = PlannedIngestUnitCount(eater, food);
            if (plannedUnits <= 0)
            {
                return false;
            }

            // A bedside shared session may carry one combined stack for both
            // eaters. The first pawn consumes only their pre-calculated portion
            // and leaves the second pawn's allocation in the same carried stack.
            // All other variants still collect an exact one-pawn allocation.
            return true;
        }

        public static int MaximumWholeUnitsThatFit(Pawn eater, Thing food)
        {
            float gainPerUnit = EstimatedFullnessGain(eater, food);
            float remaining = RemainingFeedingCapacity(eater);
            if (gainPerUnit <= FoodFitEpsilon || remaining <= FoodFitEpsilon)
            {
                return 0;
            }

            // Round upward to the first whole-item count that reaches the target,
            // then step back only when that complete portion would discard more
            // than the global 1.0-nutrition waste allowance. The job may search
            // for a smaller top-up source after consuming the reduced count.
            int units = Mathf.Max(0, Mathf.FloorToInt(
                (remaining + gainPerUnit - FoodFitEpsilon) / gainPerUnit));
            while (units > 0 &&
                !IsSessionPortionFitAcceptable(eater, food, units))
            {
                units--;
            }
            return units;
        }

        public static int RequiredMealCount(Pawn eater, Thing food)
        {
            return MaximumWholeUnitsThatFit(eater, food);
        }

        public static int MealCollectionCount(Pawn collector, Thing food, params Pawn[] eaters)
        {
            Building_FoodFaucet faucet = food as Building_FoodFaucet;
            if (faucet != null)
            {
                return NetworkMealCollectionCount(
                    collector,
                    faucet,
                    eaters);
            }

            if (collector?.carryTracker == null || food == null || food.stackCount <= 0)
            {
                return 0;
            }

            int requested = 0;
            if (eaters != null)
            {
                foreach (Pawn eater in eaters)
                {
                    if (!IsMealAcceptableForPawn(eater, food))
                    {
                        return 0;
                    }

                    requested += RequiredMealCount(eater, food);
                }
            }

            int carrySpace = collector.carryTracker.AvailableStackSpace(food.def);
            int available = Mathf.Min(food.stackCount, carrySpace);
            if (requested <= 0 || available <= 0)
            {
                return 0;
            }

            // Carry one spare prepared serving when possible. The spare is never
            // force-eaten: it covers stomach-limit changes or calculation drift and
            // is dropped intact once the participant reaches the selected target.
            if (IsPreparedMeal(food) && requested < available)
            {
                requested++;
            }

            // Take the useful allocation available on this collection. After a
            // feeding/eating round, another source is considered only when it is
            // within FollowUpMealSearchRadius of the social location.
            return Mathf.Min(requested, available);
        }

        public static int SharedStackCollectionCount(Pawn eater, Pawn partner, Thing food)
        {
            Building_FoodFaucet faucet = food as Building_FoodFaucet;
            if (faucet != null)
            {
                int faucetRequested = NetworkMealsNeeded(
                    eater,
                    faucet,
                    true);
                int partnerNeeded = NetworkMealsNeeded(
                    partner,
                    faucet,
                    false);
                int availableForEater = Mathf.Max(
                    0,
                    CompleteNetworkMeals(faucet) - partnerNeeded);
                int carrySpace = eater?.carryTracker == null ||
                    ThingDefOf.MealNutrientPaste == null
                    ? 0
                    : eater.carryTracker.AvailableStackSpace(
                        ThingDefOf.MealNutrientPaste);
                return Mathf.Max(
                    0,
                    Mathf.Min(
                        faucetRequested,
                        availableForEater,
                        carrySpace,
                        FoodNetworkV2Constants.MaximumDispenserMealsPerTrip));
            }

            if (eater?.carryTracker == null || food == null || food.stackCount <= 0 ||
                !IsMealAcceptableForPawn(eater, food))
            {
                return 0;
            }

            int requested = RequiredMealCount(eater, food);
            int available = Mathf.Min(
                food.stackCount,
                eater.carryTracker.AvailableStackSpace(food.def));

            // Do not add the normal spare serving when both pawns target the same
            // physical stack; each reservation must leave at least one valid share
            // for the other pawn. A later round may collect only from a source
            // beside the current dining location.
            return requested <= 0 || available <= 0
                ? 0
                : Mathf.Min(requested, available);
        }

        public static int ReserveMealStack(Pawn collector, Job job, Thing food, int desiredCount)
        {
            if (collector == null || job == null || food == null)
            {
                return 0;
            }

            // Food Network withdrawals happen transactionally at the faucet.
            // The building itself is not reserved, matching vanilla paste
            // dispensers and allowing several pawns to queue safely.
            if (IsFoodNetworkFaucet(food))
            {
                return Mathf.Max(0, desiredCount);
            }

            // A Feed Other job is now single-collection. Never silently reserve
            // a smaller partial allocation, because that would force another
            // storage run after the carried stack is exhausted.
            if (desiredCount <= 0 || desiredCount > food.stackCount)
            {
                return 0;
            }

            return collector.Reserve(food, job, 2, desiredCount, null, false)
                ? desiredCount
                : 0;
        }

        public static bool IsMealAcceptableForPawn(Pawn pawn, Thing food)
        {
            if (pawn == null || food == null || food.Destroyed || food.stackCount <= 0 ||
                !food.def.IsIngestible || !food.def.IsNutritionGivingIngestible || food.def.IsDrug ||
                food.def.ingestible == null || !food.def.ingestible.HumanEdible ||
                !FoodUtility.WillEat(pawn, food, null, true, false) ||
                WouldCauseDislikedFoodThought(pawn, food))
            {
                return false;
            }

            return !food.Spawned || (!food.IsForbidden(pawn) &&
                SocialProperness.IsSociallyProper(food, pawn) &&
                FactionUtility.IsPoliticallyProper(food, pawn));
        }

        public static bool WouldCauseDislikedFoodThought(Pawn pawn, Thing food)
        {
            if (pawn == null || food?.def?.ingestible == null)
            {
                return true;
            }

            // These are technically edible by adult humanlikes in emergencies,
            // but should never be selected for a voluntary social feeding event.
            if (food.def == ThingDefOf.Kibble || food.def == ThingDefOf.BabyFood ||
                food.def.ingestible.preferability <= FoodPreferability.DesperateOnly)
            {
                return true;
            }

            try
            {
                List<FoodUtility.ThoughtFromIngesting> thoughts =
                    FoodUtility.ThoughtsFromIngesting(pawn, food, food.def);
                return thoughts != null && thoughts.Any(result =>
                    result.thought?.stages != null &&
                    result.thought.stages.Any(stage => stage.baseMoodEffect < 0f));
            }
            catch (Exception)
            {
                // A modded food with a broken thought worker should not be chosen
                // automatically. Failing closed avoids repeated hated-food meals.
                return true;
            }
        }

        public static bool TryFindDiningSeat(
            Pawn pawn,
            Thing meal,
            IntVec3 searchOrigin,
            out IntVec3 diningCell)
        {
            return TryFindDiningSeatExcept(
                pawn,
                meal,
                searchOrigin,
                IntVec3.Invalid,
                out diningCell);
        }

        public static bool TryFindDiningSeatExcept(
            Pawn pawn,
            Thing meal,
            IntVec3 searchOrigin,
            IntVec3 excludedCell,
            out IntVec3 diningCell)
        {
            diningCell = IntVec3.Invalid;
            if (pawn?.Map == null || meal?.def?.ingestible == null ||
                meal.def.ingestible.chairSearchRadius <= 0f)
            {
                return false;
            }

            float radius = OneWayDiningSeatSearchRadius;
            float bestScore = float.MaxValue;
            foreach (Building table in pawn.Map.listerBuildings.allBuildingsColonist)
            {
                if (!IsUsableDiningTable(table, pawn.Map))
                {
                    continue;
                }

                foreach (IntVec3 seat in DiningSeatsForTable(table))
                {
                    if (seat == excludedCell ||
                        !IsDiningSeatUsable(pawn, seat, searchOrigin, radius))
                    {
                        continue;
                    }

                    float score = searchOrigin.DistanceToSquared(seat) +
                        pawn.Position.DistanceToSquared(seat) * 0.25f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        diningCell = seat;
                    }
                }
            }

            return diningCell.IsValid;
        }

        public static bool TryFindSharedDiningSpots(
            Pawn first,
            Pawn second,
            Thing firstMeal,
            Thing secondMeal,
            out IntVec3 firstCell,
            out IntVec3 secondCell)
        {
            firstCell = IntVec3.Invalid;
            secondCell = IntVec3.Invalid;
            if (first?.Map == null || second?.Map != first.Map ||
                firstMeal?.def?.ingestible == null || secondMeal?.def?.ingestible == null)
            {
                return false;
            }

            float firstRadius = firstMeal.def.ingestible.chairSearchRadius;
            float secondRadius = secondMeal.def.ingestible.chairSearchRadius;
            if (firstRadius <= 0f || secondRadius <= 0f)
            {
                return false;
            }

            float bestScore = float.MaxValue;
            foreach (Building table in first.Map.listerBuildings.allBuildingsColonist)
            {
                if (!IsUsableDiningTable(table, first.Map))
                {
                    continue;
                }

                List<IntVec3> seats = DiningSeatsForTable(table).Distinct().ToList();
                foreach (IntVec3 candidateFirst in seats)
                {
                    if (!IsDiningSeatUsable(first, candidateFirst, first.Position, firstRadius))
                    {
                        continue;
                    }

                    foreach (IntVec3 candidateSecond in seats)
                    {
                        if (candidateSecond == candidateFirst ||
                            candidateFirst.DistanceToSquared(candidateSecond) >
                                MaximumSharedDiningDistance * MaximumSharedDiningDistance ||
                            !IsDiningSeatUsable(second, candidateSecond, second.Position, secondRadius))
                        {
                            continue;
                        }

                        float score = first.Position.DistanceToSquared(candidateFirst) +
                            second.Position.DistanceToSquared(candidateSecond) +
                            candidateFirst.DistanceToSquared(candidateSecond) * 2f;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            firstCell = candidateFirst;
                            secondCell = candidateSecond;
                        }
                    }
                }
            }

            return firstCell.IsValid && secondCell.IsValid;
        }

        public static bool IsDiningSeat(IntVec3 cell, Map map)
        {
            if (!cell.IsValid || map == null || !cell.InBounds(map))
            {
                return false;
            }

            Building chair = cell.GetEdifice(map);
            if (chair?.def?.building == null || !chair.def.building.isSittable)
            {
                return false;
            }

            foreach (IntVec3 direction in GenAdj.CardinalDirections)
            {
                Building table = (cell + direction).GetEdifice(map);
                if (table?.def?.surfaceType == SurfaceType.Eat)
                {
                    return true;
                }
            }

            return false;
        }

        public static void FaceDiningTableOrPawn(Pawn actor, Pawn otherPawn)
        {
            if (actor?.Map != null)
            {
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 tableCell = actor.Position + direction;
                    Building table = tableCell.GetEdifice(actor.Map);
                    if (table?.def?.surfaceType == SurfaceType.Eat)
                    {
                        actor.rotationTracker.FaceCell(tableCell);
                        return;
                    }
                }
            }

            if (actor != null && otherPawn != null)
            {
                actor.rotationTracker.FaceCell(otherPawn.Position);
            }
        }

        public static bool IsCarryingSessionMeal(Pawn carrier)
        {
            Thing carried = carrier?.carryTracker?.CarriedThing;
            return carried != null && !carried.Destroyed && carried.stackCount > 0 &&
                carried.def.IsIngestible && carrier.CurJob?.targetA.Thing == carried;
        }

        public static void DropCarriedSessionMeals(Pawn carrier)
        {
            if (!IsCarryingSessionMeal(carrier) || carrier.Map == null)
            {
                return;
            }

            Thing dropped;
            carrier.carryTracker.TryDropCarriedThing(
                carrier.Position,
                ThingPlaceMode.Near,
                out dropped,
                null);
        }

        public static float NutritionWantedForSession(Pawn pawn)
        {
            FullnessAndDietStats_ThingComp comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            Thing food;
            if (comp == null || !TryGetActiveSessionFood(pawn, out food) ||
                !CanFullyConsumePlannedPortion(pawn, food))
            {
                // Never partially consume and destroy a serving merely to stop at
                // the target. The job will retain/drop the intact food and search
                // for a smaller top-up item instead.
                return 0f;
            }

            float nutritionPerUnit = FoodUtility.NutritionForEater(pawn, food);

            // RimWorld's PickupIngestible toil consults NutritionWanted while the
            // source stack is still spawned. Return the complete reserved
            // collection allocation at that stage so a pawn can pick up all meals
            // needed for the session in one trip. Once the food is carried, return
            // only the live per-round portion so prepared meals are still consumed
            // one complete serving at a time and the target is rechecked after
            // every meal.
            int pickupUnits = SessionPickupUnitCount(pawn, food);
            int units = pickupUnits > 0
                ? pickupUnits
                : PlannedIngestUnitCount(pawn, food);
            return Mathf.Max(0f, nutritionPerUnit * units);
        }

        private static int SessionPickupUnitCount(Pawn eater, Thing food)
        {
            if (eater == null || food == null || !food.Spawned ||
                food.stackCount <= 0)
            {
                return 0;
            }

            Job ownJob = eater.CurJob;
            if (IsFeedOtherEatingJob(eater.CurJobDef) &&
                ownJob != null && ownJob.targetA.Thing == food)
            {
                return Mathf.Min(food.stackCount, Mathf.Max(0, ownJob.count));
            }

            Pawn feeder;
            JobDriver caregiverDriver;
            if (TryGetCaregiverFeedingDriver(
                    eater,
                    out feeder,
                    out caregiverDriver))
            {
                Job caregiverJob = feeder?.CurJob;
                if (caregiverJob != null && caregiverJob.targetA.Thing == food)
                {
                    return Mathf.Min(
                        food.stackCount,
                        Mathf.Max(0, caregiverJob.count));
                }
            }

            return 0;
        }

        public static bool IsLeaderJobFor(Pawn possibleLeader, Pawn partner)
        {
            return possibleLeader != null && possibleLeader.CurJobDef == FeedOtherDefOf.RR_FeedOther &&
                possibleLeader.CurJob?.targetB.Pawn == partner;
        }

        public static bool IsPartnerJobFor(Pawn possiblePartner, Pawn leader)
        {
            return possiblePartner != null && possiblePartner.CurJobDef == FeedOtherDefOf.RR_FeedOtherPartner &&
                possiblePartner.CurJob?.targetB.Pawn == leader;
        }

        public static bool IsOneWayFeederJobFor(Pawn possibleFeeder, Pawn feedee)
        {
            return possibleFeeder != null &&
                (possibleFeeder.CurJobDef == FeedOtherDefOf.RR_FeedOtherOneWay ||
                    possibleFeeder.CurJobDef == FeedOtherDefOf.RR_FeedOtherBedside) &&
                possibleFeeder.CurJob?.targetB.Pawn == feedee;
        }

        public static bool IsOneWayRecipientJobFor(Pawn possibleRecipient, Pawn feeder)
        {
            return possibleRecipient != null && possibleRecipient.CurJobDef == FeedOtherDefOf.RR_BeFedOtherPartner &&
                possibleRecipient.CurJob?.targetB.Pawn == feeder;
        }

        private static bool IsValidPartnerFor(
            Pawn initiator,
            Pawn candidate,
            bool manualOrder = false)
        {
            bool candidateAvailable = manualOrder
                ? IsAvailablePartnerForManualOrder(candidate)
                : IsAvailablePartner(candidate);
            if (candidate == initiator || !candidateAvailable || candidate.Map != initiator.Map ||
                (!manualOrder && AutonomousCooldownBlocksPair(initiator, candidate)) ||
                initiator.HostileTo(candidate) || candidate.HostileTo(initiator) ||
                !SocialProperness.IsSociallyProper(candidate, initiator) ||
                !SocialProperness.IsSociallyProper(initiator, candidate) ||
                !FactionUtility.IsPoliticallyProper(candidate, initiator) ||
                !FactionUtility.IsPoliticallyProper(initiator, candidate))
            {
                return false;
            }

            bool initiatorCanReach = manualOrder
                ? initiator.CanReach(candidate, PathEndMode.Touch, initiator.NormalMaxDanger())
                : initiator.CanReserveAndReach(candidate, PathEndMode.Touch, initiator.NormalMaxDanger(), 1, -1, null, false);
            return initiatorCanReach &&
                candidate.CanReach(initiator, PathEndMode.Touch, candidate.NormalMaxDanger());
        }

        private static List<Pawn> OrderCandidatesWithRomanticPartnersFirst(
            Pawn initiator,
            IEnumerable<Pawn> candidates)
        {
            List<Pawn> romanticPartners = new List<Pawn>();
            List<Pawn> closeFriends = new List<Pawn>();
            List<Pawn> otherCandidates = new List<Pawn>();

            foreach (Pawn candidate in candidates)
            {
                if (LovePartnerRelationUtility.LovePartnerRelationExists(initiator, candidate))
                {
                    romanticPartners.Add(candidate);
                }
                else if ((initiator.relations?.OpinionOf(candidate) ?? 0) >= 40)
                {
                    closeFriends.Add(candidate);
                }
                else
                {
                    otherCandidates.Add(candidate);
                }
            }

            // Current romantic partners are tried first, followed by close
            // friends, then the ordinary fallback pool. Shuffle inside each tier
            // so colonies with several equally suitable candidates stay varied.
            romanticPartners.Shuffle();
            closeFriends.Shuffle();
            otherCandidates.Shuffle();
            romanticPartners.AddRange(closeFriends);
            romanticPartners.AddRange(otherCandidates);
            return romanticPartners;
        }

        private static bool IsValidFeedeeFor(
            Pawn feeder,
            Pawn candidate,
            bool mustRemainInPlace,
            bool manualOrder = false)
        {
            bool candidateAvailable = manualOrder
                ? IsAvailableOneWayRecipientForManualOrder(candidate)
                : IsAvailableOneWayRecipient(candidate);
            if (candidate == feeder || !candidateAvailable || candidate.Map != feeder.Map ||
                (!manualOrder && AutonomousCooldownBlocksPair(feeder, candidate)) ||
                (!FeedOtherMod.Settings.bedsideFeedingEnabled &&
                    ShouldRemainInPlaceForFeeding(candidate)) ||
                (mustRemainInPlace && !ShouldRemainInPlaceForFeeding(candidate)) ||
                feeder.HostileTo(candidate) || candidate.HostileTo(feeder))
            {
                return false;
            }

            if (manualOrder)
            {
                // A direct order may target any non-hostile humanlike pawn; the
                // autonomous social/political propriety filters do not apply.
                return feeder.CanReach(candidate, PathEndMode.Touch, feeder.NormalMaxDanger());
            }

            return SocialProperness.IsSociallyProper(candidate, feeder) &&
                SocialProperness.IsSociallyProper(feeder, candidate) &&
                FactionUtility.IsPoliticallyProper(candidate, feeder) &&
                FactionUtility.IsPoliticallyProper(feeder, candidate) &&
                feeder.CanReserveAndReach(candidate, PathEndMode.Touch,
                    feeder.NormalMaxDanger(), 1, -1, null, false);
        }

        private static bool IsValidSleepingBedFallbackFor(
            Pawn feeder,
            Pawn candidate,
            bool sharedPartner)
        {
            bool candidateAvailable = sharedPartner
                ? IsAvailableSleepingSharedPartnerFallback(candidate)
                : IsAvailableSleepingOneWayRecipientFallback(candidate);

            if (!FeedOtherMod.Settings.bedsideFeedingEnabled ||
                candidate == feeder || !candidateAvailable ||
                candidate.Map != feeder.Map ||
                AutonomousCooldownBlocksPair(feeder, candidate) ||
                feeder.HostileTo(candidate) || candidate.HostileTo(feeder))
            {
                return false;
            }

            return SocialProperness.IsSociallyProper(candidate, feeder) &&
                SocialProperness.IsSociallyProper(feeder, candidate) &&
                FactionUtility.IsPoliticallyProper(candidate, feeder) &&
                FactionUtility.IsPoliticallyProper(feeder, candidate) &&
                feeder.CanReserveAndReach(candidate, PathEndMode.Touch,
                    feeder.NormalMaxDanger(), 1, -1, null, false);
        }

        private static bool FeederCanCollectMeal(Pawn feeder, Thing meal)
        {
            return feeder != null && meal != null && !meal.IsForbidden(feeder) &&
                SocialProperness.IsSociallyProper(meal, feeder) &&
                FactionUtility.IsPoliticallyProper(meal, feeder) &&
                feeder.CanReserveAndReach(meal, PathEndMode.ClosestTouch, feeder.NormalMaxDanger(), 2, 1, null, false);
        }

        private static bool IsUsableDiningTable(Building table, Map map)
        {
            return table != null && table.Spawned && table.Map == map &&
                table.def?.surfaceType == SurfaceType.Eat && !table.IsBurning();
        }

        private static IEnumerable<IntVec3> DiningSeatsForTable(Building table)
        {
            if (table == null)
            {
                yield break;
            }

            foreach (IntVec3 cell in GenAdj.CellsAdjacentCardinal(table))
            {
                Building chair = cell.GetEdifice(table.Map);
                if (chair?.def?.building != null && chair.def.building.isSittable)
                {
                    yield return cell;
                }
            }
        }

        private static bool IsDiningSeatUsable(
            Pawn pawn,
            IntVec3 seat,
            IntVec3 searchOrigin,
            float searchRadius)
        {
            if (pawn?.Map == null || !seat.InBounds(pawn.Map) ||
                searchOrigin.DistanceToSquared(seat) > searchRadius * searchRadius)
            {
                return false;
            }

            Building chair = seat.GetEdifice(pawn.Map);
            if (chair?.def?.building == null || !chair.def.building.isSittable ||
                chair.IsForbidden(pawn) || chair.IsBurning() || chair.HostileTo(pawn) ||
                !SocialProperness.IsSociallyProper(chair, pawn) ||
                !pawn.CanReserveSittableOrSpot(seat, false))
            {
                return false;
            }

            if (chair.Faction != pawn.Faction && pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                return false;
            }

            return pawn.CanReach(seat, PathEndMode.OnCell, pawn.NormalMaxDanger());
        }

        private static bool IsNeutralOrLower(WeightOpinion opinion)
        {
            switch (opinion)
            {
                case WeightOpinion.Hate:
                case WeightOpinion.Dislike:
                case WeightOpinion.NeutralMinus:
                case WeightOpinion.Neutral:
                    return true;
                default:
                    return false;
            }
        }

        private static List<Thing> SessionMealSources(
            Pawn eater,
            Pawn collector,
            IntVec3 anchor,
            Thing otherPawnMeal)
        {
            List<Thing> sources = OrderMealCandidatesByFit(
                    eater,
                    collector,
                    StoredMealCandidates(
                        eater,
                        anchor,
                        otherPawnMeal,
                        collector),
                    anchor)
                .ToList();

            Building_FoodFaucet faucet;
            if (TryFindSessionFaucet(
                collector,
                eater,
                anchor,
                otherPawnMeal,
                out faucet) &&
                !sources.Contains(faucet))
            {
                sources.Add(faucet);
            }

            return sources;
        }

        private static bool TryFindSessionFaucet(
            Pawn collector,
            Pawn eater,
            IntVec3 anchor,
            Thing otherPawnMeal,
            out Building_FoodFaucet faucet)
        {
            faucet = null;
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled ||
                collector == null || eater == null || collector.Map == null ||
                eater.Map != collector.Map || collector.carryTracker == null ||
                ThingDefOf.MealNutrientPaste == null ||
                collector.carryTracker.AvailableStackSpace(
                    ThingDefOf.MealNutrientPaste) <= 0)
            {
                return false;
            }

            bool desperate = eater.needs?.food != null &&
                eater.needs.food.CurCategory == HungerCategory.Starving;
            float score;
            if (!FoodNetworkV2FaucetSearchUtility.TryFindBestFaucet(
                collector,
                eater,
                desperate,
                FoodPreferability.MealLavish,
                false,
                false,
                FoodPreferability.Undefined,
                false,
                out faucet,
                out score))
            {
                return false;
            }

            if (anchor.IsValid &&
                anchor.DistanceToSquared(faucet.InteractionCell) >
                    SharedFoodSearchRadius * SharedFoodSearchRadius)
            {
                faucet = null;
                return false;
            }

            if (ReferenceEquals(otherPawnMeal, faucet) &&
                CompleteNetworkMeals(faucet) < 2)
            {
                faucet = null;
                return false;
            }

            return true;
        }

        private static int CompleteNetworkMeals(Building_FoodFaucet faucet)
        {
            FoodNetworkV2 network =
                FoodNetworkV2MachineUtility.NetworkFor(faucet);
            return network == null
                ? 0
                : Mathf.FloorToInt(
                    (network.StoredNutrition + FoodNetworkV2Constants.Epsilon) /
                    FoodNetworkV2Constants.DispenserMealNutrition);
        }

        private static int NetworkMealsNeeded(
            Pawn eater,
            Building_FoodFaucet faucet,
            bool capForTrip)
        {
            if (eater == null || faucet == null)
            {
                return 0;
            }

            FoodBatchV2 preview;
            if (!FoodNetworkV2ServingUtility.TryPreviewMeal(
                faucet,
                out preview))
            {
                return 0;
            }

            float gainPerMeal = EstimatedFullnessGain(
                eater,
                FoodNetworkV2Constants.DispenserMealNutrition,
                preview.FullnessToNutritionRatio);
            float remaining = RemainingFeedingCapacity(eater);
            if (gainPerMeal <= FoodFitEpsilon ||
                remaining <= FoodFitEpsilon)
            {
                return 0;
            }

            int needed = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (remaining - FoodFitEpsilon) / gainPerMeal));
            return capForTrip
                ? Mathf.Min(
                    needed,
                    FoodNetworkV2Constants.MaximumDispenserMealsPerTrip)
                : needed;
        }

        private static bool CanFaucetSupplyCombined(
            Building_FoodFaucet faucet,
            Pawn first,
            Pawn second)
        {
            int firstNeeded = NetworkMealsNeeded(first, faucet, false);
            int secondNeeded = NetworkMealsNeeded(second, faucet, false);
            return firstNeeded > 0 && secondNeeded > 0 &&
                CompleteNetworkMeals(faucet) >=
                    firstNeeded + secondNeeded;
        }

        private static int NetworkMealCollectionCount(
            Pawn collector,
            Building_FoodFaucet faucet,
            params Pawn[] eaters)
        {
            if (collector?.carryTracker == null || faucet == null ||
                ThingDefOf.MealNutrientPaste == null)
            {
                return 0;
            }

            int requested = 0;
            if (eaters != null)
            {
                foreach (Pawn eater in eaters)
                {
                    requested += NetworkMealsNeeded(
                        eater,
                        faucet,
                        true);
                }
            }

            int carrySpace = collector.carryTracker.AvailableStackSpace(
                ThingDefOf.MealNutrientPaste);
            return Mathf.Max(
                0,
                Mathf.Min(
                    requested,
                    CompleteNetworkMeals(faucet),
                    carrySpace,
                    FoodNetworkV2Constants.MaximumDispenserMealsPerTrip));
        }

        public static bool IsFoodNetworkFaucet(Thing source)
        {
            return FeedOtherMod.Settings.foodNetworkV2Enabled &&
                source is Building_FoodFaucet;
        }

        public static IEnumerable<Toil> CollectSessionMealSourceToils(
            Pawn collector,
            Pawn eater)
        {
            Toil goToFaucet = Toils_Goto.GotoThing(
                    TargetIndex.A,
                    PathEndMode.InteractionCell)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);
            Toil collectionComplete = ToilMaker.MakeToil(
                "CompleteSessionMealCollection");
            collectionComplete.defaultCompleteMode =
                ToilCompleteMode.Instant;

            yield return Toils_Jump.JumpIf(
                goToFaucet,
                delegate
                {
                    Job currentJob = collector == null
                        ? null
                        : collector.CurJob;
                    Thing source = currentJob == null
                        ? null
                        : currentJob.GetTarget(TargetIndex.A).Thing;
                    return IsFoodNetworkFaucet(source);
                });

            yield return Toils_Goto.GotoThing(
                    TargetIndex.A,
                    PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Ingest.PickupIngestible(
                TargetIndex.A,
                eater);
            yield return Toils_Jump.Jump(collectionComplete);

            yield return goToFaucet;
            Toil dispense = ToilMaker.MakeToil(
                "TakeFixedMealFromFoodNetwork");
            dispense.initAction = delegate
            {
                Pawn actor = dispense.actor;
                if (actor == null || actor.CurJob == null ||
                    actor.carryTracker == null ||
                    actor.carryTracker.CarriedThing != null)
                {
                    if (actor?.jobs?.curDriver != null)
                    {
                        actor.jobs.curDriver.EndJobWith(
                            JobCondition.Incompletable);
                    }
                    return;
                }

                Building_FoodFaucet faucet = actor.CurJob
                    .GetTarget(TargetIndex.A).Thing as
                        Building_FoodFaucet;
                int requestedMeals = Mathf.Clamp(
                    actor.CurJob.count,
                    1,
                    FoodNetworkV2Constants.MaximumDispenserMealsPerTrip);
                Thing serving =
                    FoodNetworkV2ServingUtility.TryDispenseMeals(
                        faucet,
                        eater,
                        requestedMeals);
                if (serving == null ||
                    !actor.carryTracker.TryStartCarry(serving))
                {
                    if (serving != null && !serving.Destroyed)
                    {
                        FoodNetworkV2ServingUtility.TryReturnToNetwork(
                            faucet,
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
            };
            dispense.defaultCompleteMode = ToilCompleteMode.Delay;
            dispense.defaultDuration =
                Building_NutrientPasteDispenser.CollectDuration;
            yield return dispense;
            yield return collectionComplete;
        }

        private static IOrderedEnumerable<Thing> OrderMealCandidatesByFit(
            Pawn eater,
            Pawn collector,
            IEnumerable<Thing> candidates,
            IntVec3 anchor)
        {
            Pawn traveler = collector ?? eater;

            // Proper prepared meals are always considered before processed snacks,
            // milk and acceptable raw foods. A source no longer has to contain the
            // pawn's complete allocation: the job consumes what can be carried,
            // rechecks live fullness, and searches again when still below 70%.
            return candidates
                .Where(candidate => CanFullyConsumeOneUnit(eater, candidate))
                .OrderBy(candidate => IsPreparedMeal(candidate) ? 0 : 1)
                .ThenBy(candidate => MealFitScore(eater, traveler, candidate))
                .ThenByDescending(candidate => (int)candidate.def.ingestible.preferability)
                .ThenBy(candidate => traveler.Position.DistanceToSquared(candidate.Position) +
                    (anchor.IsValid ? anchor.DistanceToSquared(candidate.Position) * 2f : 0f));
        }

        public static bool IsPreparedMeal(Thing food)
        {
            if (IsFoodNetworkFaucet(food))
            {
                return true;
            }

            return food?.def?.ingestible != null &&
                food.def.ingestible.preferability >= FoodPreferability.MealAwful;
        }

        private static float MealFitScore(Pawn eater, Pawn collector, Thing food)
        {
            if (IsPreparedMeal(food))
            {
                // Prepared meals are already the first candidate tier. Within
                // that tier, prefer the serving that discards the least actual
                // nutrition after concentration and pawn fullness gain are applied.
                return ProjectedSessionNutritionWaste(eater, food, 1);
            }

            // Non-meal fallbacks are quality-tiered before fit or distance. A
            // higher FoodPreferability produces a lower score, so sensible
            // processed foods and treats beat basic raw ingredients. The small
            // remainder term still breaks ties between equally preferred foods.
            return 100f - (int)food.def.ingestible.preferability +
                RemainingAfterCollectingSafeUnits(eater, collector, food);
        }

        private static float RemainingAfterCollectingSafeUnits(Pawn eater, Pawn collector, Thing food)
        {
            int safeCount = MaximumWholeUnitsThatFit(eater, food);
            int carrySpace = collector?.carryTracker == null
                ? 1
                : collector.carryTracker.AvailableStackSpace(food.def);
            int available = Mathf.Min(safeCount, food.stackCount, carrySpace);
            float filled = EstimatedFullnessGain(eater, food) * Mathf.Max(0, available);
            return Mathf.Max(0f, RemainingFeedingCapacity(eater) - filled);
        }

        private static IEnumerable<Thing> StoredMealCandidates(
            Pawn pawn,
            IntVec3 anchor,
            Thing otherPawnMeal,
            Pawn collector = null)
        {
            if (pawn?.Map == null)
            {
                yield break;
            }

            Pawn gatherer = collector ?? pawn;

            List<Thing> foodSources = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSource);
            foreach (Thing food in foodSources)
            {
                if (food == null || !food.Spawned || food.Map != pawn.Map || food.stackCount <= 0 ||
                    !StoreUtility.IsInValidStorage(food) ||
                    !IsMealAcceptableForPawn(pawn, food))
                {
                    continue;
                }

                if (anchor.IsValid && anchor.DistanceToSquared(food.Position) >
                    SharedFoodSearchRadius * SharedFoodSearchRadius)
                {
                    continue;
                }

                if (food == otherPawnMeal && food.stackCount < 2)
                {
                    continue;
                }

                if (!gatherer.CanReserveAndReach(
                    food,
                    PathEndMode.ClosestTouch,
                    gatherer.NormalMaxDanger(),
                    2,
                    1,
                    null,
                    false))
                {
                    continue;
                }

                yield return food;
            }
        }
    }
}
