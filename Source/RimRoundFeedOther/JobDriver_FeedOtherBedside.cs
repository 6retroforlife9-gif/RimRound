using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public class JobDriver_FeedOtherBedside : JobDriver
    {
        private Pawn Recipient => job.targetB.Pawn;
        private IntVec3 FoodAnchor => job.targetC.Cell;

        private bool recipientStarted;
        private bool recipientBedLocked;
        private bool cleaningUp;
        private bool recipientReachedTarget;
        private bool feederReachedTarget;
        private bool recipientCannotContinue;
        private bool feederCannotContinue;
        private bool sessionTimedOut;
        private bool carriedMealsForRecipient;
        private int recipientTopUpRoundsCompleted;
        private int feederTopUpRoundsCompleted;
        private int recipientMealsCompleted;
        private int feederMealsCompleted;
        private bool currentRecipientMealIsTopUp;
        private bool currentFeederMealIsTopUp;
        private FeedOtherConversationState conversationState = new FeedOtherConversationState();
        private bool recipientWasAsleepAtStart;
        private bool postMealSocialStarted;
        private int postMealSocialStartTick;
        private bool postMealSocialCompleted;
        private FeedOtherConversationState postMealConversationState = new FeedOtherConversationState();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref recipientStarted, "feedOtherBedsideRecipientStarted", false);
            Scribe_Values.Look(ref recipientBedLocked, "feedOtherBedsideRecipientBedLocked", false);
            Scribe_Values.Look(ref recipientReachedTarget, "feedOtherBedsideRecipientTargetReached", false);
            Scribe_Values.Look(ref feederReachedTarget, "feedOtherBedsideFeederTargetReached", false);
            Scribe_Values.Look(ref recipientCannotContinue, "feedOtherBedsideRecipientCannotContinue", false);
            Scribe_Values.Look(ref feederCannotContinue, "feedOtherBedsideFeederCannotContinue", false);
            Scribe_Values.Look(ref sessionTimedOut, "feedOtherBedsideSessionTimedOut", false);
            Scribe_Values.Look(ref carriedMealsForRecipient, "feedOtherBedsideCarryingForRecipient", false);
            Scribe_Values.Look(ref recipientTopUpRoundsCompleted, "feedOtherBedsideRecipientTopUpRoundsCompleted", 0);
            Scribe_Values.Look(ref feederTopUpRoundsCompleted, "feedOtherBedsideFeederTopUpRoundsCompleted", 0);
            Scribe_Values.Look(ref recipientMealsCompleted, "feedOtherBedsideRecipientMealsCompleted", 0);
            Scribe_Values.Look(ref feederMealsCompleted, "feedOtherBedsideFeederMealsCompleted", 0);
            Scribe_Values.Look(ref currentRecipientMealIsTopUp, "feedOtherBedsideCurrentRecipientMealIsTopUp", false);
            Scribe_Values.Look(ref currentFeederMealIsTopUp, "feedOtherBedsideCurrentFeederMealIsTopUp", false);
            Scribe_Deep.Look(ref conversationState, "feedOtherBedsideConversationState");
            Scribe_Values.Look(ref recipientWasAsleepAtStart, "feedOtherBedsideRecipientWasAsleep", false);
            Scribe_Values.Look(ref postMealSocialStarted, "feedOtherBedsidePostMealStarted", false);
            Scribe_Values.Look(ref postMealSocialStartTick, "feedOtherBedsidePostMealStartTick", 0);
            Scribe_Values.Look(ref postMealSocialCompleted, "feedOtherBedsidePostMealCompleted", false);
            Scribe_Deep.Look(ref postMealConversationState, "feedOtherBedsidePostMealConversationState");
            if (conversationState == null)
            {
                conversationState = new FeedOtherConversationState();
            }
            if (postMealConversationState == null)
            {
                postMealConversationState = new FeedOtherConversationState();
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return Recipient != null && pawn.Reserve(Recipient, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.AddFinishAction(FinishSession);
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.FailOnMentalState(TargetIndex.B);
            this.AddFailCondition(() => !Recipient.Awake() &&
                !FeedOtherUtility.IsAvailableSleepingSharedPartnerFallback(Recipient));
            this.AddFailCondition(() => !FeedOtherUtility.IsBedsideFeederEligible(pawn) ||
                (!FeedOtherUtility.IsOneWayRecipientEligible(Recipient) &&
                 !FeedOtherUtility.IsSleepingSharedPartnerFallbackEligible(Recipient)));
            this.AddEndCondition(SessionTimeLimitCondition);
            this.AddFailCondition(() => recipientStarted &&
                !FeedOtherUtility.IsOneWayRecipientJobFor(Recipient, pawn));

            yield return MakeStartRecipientToil();

            Toil postMealSocial = null;
            Toil findRecipientFood = null;
            Toil findFeederFood = null;
            Toil goToRecipientWithRecipientFood = null;
            Toil goToRecipientWithFeederFood = null;

            Toil chooseNext = ToilMaker.MakeToil("ChooseBedsideFeedingAction");
            chooseNext.initAction = delegate
            {
                if (BothParticipantsCompleted())
                {
                    FinishMealOrBeginPostMeal(postMealSocial);
                }
                else if (!RecipientCompleted())
                {
                    if (carriedMealsForRecipient && FeedOtherUtility.IsCarryingSessionMeal(pawn) &&
                        FeedOtherUtility.CanFullyConsumePlannedPortion(Recipient, pawn.carryTracker.CarriedThing))
                    {
                        // The recipient keeps their native LayDown job; only a
                        // legacy/mobile linked job receives a meal target.
                        if (recipientStarted)
                        {
                            Recipient.CurJob.targetA = pawn.carryTracker.CarriedThing;
                        }
                        JumpToToil(goToRecipientWithRecipientFood);
                    }
                    else
                    {
                        // The recipient is still below Very Full. Drop any unusable
                        // remainder and collect another highest-priority meal.
                        FeedOtherUtility.DropCarriedSessionMeals(pawn);
                        carriedMealsForRecipient = false;
                        JumpToToil(findRecipientFood);
                    }
                }
                else
                {
                    Thing carriedMeal = pawn.carryTracker?.CarriedThing;
                    if (FeedOtherUtility.IsCarryingSessionMeal(pawn) &&
                        FeedOtherUtility.IsMealAcceptableForPawn(pawn, carriedMeal) &&
                        FeedOtherUtility.CanFullyConsumePlannedPortion(pawn, carriedMeal))
                    {
                        carriedMealsForRecipient = false;
                        currentFeederMealIsTopUp = !FeedOtherUtility.IsPreparedMeal(carriedMeal);
                        job.targetA = carriedMeal;
                        JumpToToil(goToRecipientWithFeederFood);
                    }
                    else
                    {
                        // The feeder also rechecks their own live fullness and may
                        // collect another meal after the recipient is complete.
                        FeedOtherUtility.DropCarriedSessionMeals(pawn);
                        JumpToToil(findFeederFood);
                    }
                }
            };
            chooseNext.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return chooseNext;

            findRecipientFood = ToilMaker.MakeToil("FindBedsideRecipientMeal");
            findRecipientFood.initAction = delegate
            {
                Thing meal;
                bool followUpCollection = recipientMealsCompleted > 0 ||
                    feederMealsCompleted > 0;
                bool foundMeal = followUpCollection
                    ? FeedOtherUtility.TryFindNearbyStoredMealForFeeding(
                        pawn,
                        Recipient,
                        Recipient?.Position ?? pawn.Position,
                        out meal)
                    : FeedOtherUtility.TryFindStoredMealForFeeding(
                        pawn,
                        Recipient,
                        FoodAnchor,
                        out meal);
                if (!foundMeal)
                {
                    recipientCannotContinue = true;
                    JumpToToil(chooseNext);
                    return;
                }

                job.targetA = meal;
                carriedMealsForRecipient = true;
                currentRecipientMealIsTopUp = !FeedOtherUtility.IsPreparedMeal(meal);
                // Prioritise the in-bed recipient. Any spare serving may later
                // be eaten by the feeder, but the recipient's allocation is never
                // rejected merely because a combined two-pawn pickup will not fit.
                bool canAlsoFeedCollector =
                    FeedOtherUtility.IsMealAcceptableForPawn(pawn, meal) &&
                    FeedOtherUtility.CanFullyConsumeOneUnit(pawn, meal);
                job.count = canAlsoFeedCollector
                    ? FeedOtherUtility.MealCollectionCount(
                        pawn,
                        meal,
                        Recipient,
                        pawn)
                    : FeedOtherUtility.MealCollectionCount(
                        pawn,
                        meal,
                        Recipient);
                job.count = FeedOtherUtility.ReserveMealStack(pawn, job, meal, job.count);
                if (job.count <= 0)
                {
                    recipientCannotContinue = true;
                    JumpToToil(chooseNext);
                }
            };
            findRecipientFood.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findRecipientFood;

            foreach (Toil collectRecipientMeal in
                FeedOtherUtility.CollectSessionMealSourceToils(
                    pawn,
                    Recipient))
            {
                yield return collectRecipientMeal;
            }

            Toil shareRecipientMealTarget = ToilMaker.MakeToil("ShareBedsideRecipientMealTarget");
            shareRecipientMealTarget.initAction = delegate
            {
                Thing carriedMeal = pawn.carryTracker?.CarriedThing;
                if (carriedMeal == null ||
                    (recipientStarted && !FeedOtherUtility.IsOneWayRecipientJobFor(Recipient, pawn)))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                job.targetA = carriedMeal;
                if (recipientStarted)
                {
                    Recipient.CurJob.targetA = carriedMeal;
                }
            };
            shareRecipientMealTarget.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return shareRecipientMealTarget;

            goToRecipientWithRecipientFood = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return goToRecipientWithRecipientFood;

            Toil feedRecipient = FeedOtherUtility.ChewIngestibleWithEatingSpeed(
                    Recipient,
                    FeedOtherUtility.AssistedEatingDurationFactor,
                    TargetIndex.A,
                    TargetIndex.None)
                .FailOnCannotTouch(TargetIndex.B, PathEndMode.Touch);
            feedRecipient.AddFailCondition(() =>
                !FeedOtherUtility.CanFullyConsumePlannedPortion(Recipient, pawn.carryTracker?.CarriedThing));
            DecorateSocialToil(feedRecipient);
            yield return feedRecipient;
            yield return Toils_Ingest.FinalizeIngest(Recipient, TargetIndex.A);

            Toil recordRecipientMeal = ToilMaker.MakeToil("RecordBedsideRecipientMeal");
            recordRecipientMeal.initAction = delegate
            {
                recipientMealsCompleted++;
                if (currentRecipientMealIsTopUp)
                {
                    recipientTopUpRoundsCompleted++;
                }

                FeedOtherUtility.TryGrantVeryFullMood(Recipient);
                RecipientReachedTarget();
            };
            recordRecipientMeal.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return recordRecipientMeal;
            yield return Toils_Jump.Jump(chooseNext);

            findFeederFood = ToilMaker.MakeToil("FindBedsideFeederMeal");
            findFeederFood.initAction = delegate
            {
                Thing meal;
                bool followUpCollection = recipientMealsCompleted > 0 ||
                    feederMealsCompleted > 0;
                bool foundMeal = followUpCollection
                    ? FeedOtherUtility.TryFindNearbyStoredMeal(
                        pawn,
                        Recipient?.Position ?? pawn.Position,
                        null,
                        out meal)
                    : FeedOtherUtility.TryFindStoredMeal(
                        pawn,
                        FoodAnchor,
                        null,
                        out meal);
                if (!foundMeal)
                {
                    feederCannotContinue = true;
                    JumpToToil(chooseNext);
                    return;
                }

                job.targetA = meal;
                carriedMealsForRecipient = false;
                currentFeederMealIsTopUp = !FeedOtherUtility.IsPreparedMeal(meal);
                job.count = FeedOtherUtility.MealCollectionCount(pawn, meal, pawn);
                job.count = FeedOtherUtility.ReserveMealStack(pawn, job, meal, job.count);
                if (job.count <= 0)
                {
                    feederCannotContinue = true;
                    JumpToToil(chooseNext);
                }
            };
            findFeederFood.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findFeederFood;

            foreach (Toil collectFeederMeal in
                FeedOtherUtility.CollectSessionMealSourceToils(
                    pawn,
                    pawn))
            {
                yield return collectFeederMeal;
            }
            goToRecipientWithFeederFood = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return goToRecipientWithFeederFood;

            Toil eatBesideRecipient = FeedOtherUtility.ChewIngestibleWithEatingSpeed(
                    pawn,
                    FeedOtherUtility.SharedEatingDurationFactor,
                    TargetIndex.A,
                    TargetIndex.None)
                .FailOnCannotTouch(TargetIndex.B, PathEndMode.Touch);
            eatBesideRecipient.AddFailCondition(() =>
                !FeedOtherUtility.CanFullyConsumePlannedPortion(pawn, pawn.carryTracker?.CarriedThing));
            DecorateSocialToil(eatBesideRecipient);
            yield return eatBesideRecipient;
            yield return Toils_Ingest.FinalizeIngest(pawn, TargetIndex.A);

            Toil recordFeederMeal = ToilMaker.MakeToil("RecordBedsideFeederMeal");
            recordFeederMeal.initAction = delegate
            {
                feederMealsCompleted++;
                if (currentFeederMealIsTopUp)
                {
                    feederTopUpRoundsCompleted++;
                }

                FeedOtherUtility.TryGrantVeryFullMood(pawn);
                FeederReachedTarget();
            };
            recordFeederMeal.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return recordFeederMeal;
            yield return Toils_Jump.Jump(chooseNext);

            postMealSocial = ToilMaker.MakeToil("PostMealBedsideSocialRecreation");
            postMealSocial.defaultCompleteMode = ToilCompleteMode.Never;
            postMealSocial.socialMode = RandomSocialMode.Off;
            postMealSocial.initAction = delegate
            {
                pawn.pather?.StopDead();
                if (recipientStarted)
                {
                    Recipient?.pather?.StopDead();
                }
            };
            postMealSocial.tickIntervalAction = delegate(int delta)
            {
                pawn.pather?.StopDead();
                FeedOtherUtility.FaceDiningTableOrPawn(pawn, Recipient);
                PostMealSocialUtility.GainRecreation(pawn, delta);
                if (!recipientStarted)
                {
                    PostMealSocialUtility.GainRecreation(Recipient, delta);
                }
                PostMealSocialUtility.TickRomanticHearts(pawn, Recipient);

                FeedOtherConversationUtility.TickConversation(
                    postMealConversationState,
                    pawn,
                    Recipient,
                    FeedOtherConversationUtility.PostFeedingActivity);

                if (PostMealSocialUtility.ShouldEnd(
                    pawn,
                    Recipient,
                    postMealSocialStartTick,
                    recipientWasAsleepAtStart))
                {
                    CompletePostMealSocial();
                    EndJobWith(JobCondition.Succeeded);
                }
            };
            yield return postMealSocial;
        }

        private void FinishMealOrBeginPostMeal(Toil postMealSocial)
        {
            FeedOtherConversationUtility.TryFinishingConversation(
                conversationState,
                pawn,
                Recipient,
                FeedOtherConversationUtility.FeedingInPlaceActivity);

            if (!PostMealSocialUtility.CanStart(
                pawn,
                Recipient,
                recipientWasAsleepAtStart))
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }

            FeedOtherUtility.DropCarriedSessionMeals(pawn);
            postMealSocialStarted = true;
            postMealSocialStartTick = Find.TickManager.TicksGame;
            postMealSocialCompleted = false;
            pawn.pather?.StopDead();
            JumpToToil(postMealSocial);
        }

        private void CompletePostMealSocial()
        {
            if (postMealSocialCompleted)
            {
                return;
            }

            postMealSocialCompleted = true;
            FeedOtherConversationUtility.TryFinishingConversation(
                postMealConversationState,
                pawn,
                Recipient,
                FeedOtherConversationUtility.PostFeedingActivity);
            PostMealSocialUtility.TryGrantAfterMealChatMemory(
                pawn,
                Recipient,
                postMealSocialStartTick);
        }

        private void DecorateSocialToil(Toil toil)
        {
            toil.socialMode = RandomSocialMode.Off;
            toil.AddPreTickIntervalAction(delegate(int delta)
            {
                FeedOtherUtility.FaceDiningTableOrPawn(pawn, Recipient);
                FeedOtherConversationUtility.TickConversation(
                    conversationState, pawn, Recipient, FeedOtherConversationUtility.FeedingInPlaceActivity);

                FeedOtherUtility.GainFeedOtherRecreation(
                    pawn, delta, FeedOtherUtility.BedsideFeedingRecreationFactor);
                FeedOtherUtility.GainFeedOtherRecreation(
                    Recipient, delta, FeedOtherUtility.BedsideFeedingRecreationFactor);
            });
        }

        private Toil MakeStartRecipientToil()
        {
            Toil toil = ToilMaker.MakeToil("StartBedsideFeedingRecipient");
            toil.initAction = delegate
            {
                Pawn recipient = Recipient;
                recipientWasAsleepAtStart = recipient != null && !recipient.Awake();
                bool manualOrder = job.playerForced;
                bool sleepingBedFallback = !manualOrder &&
                    FeedOtherUtility.IsAvailableSleepingSharedPartnerFallback(recipient);
                bool useMobileBedLock = !manualOrder &&
                    FeedOtherUtility.IsMobileBedLockCandidate(recipient);
                bool recipientAvailable = manualOrder
                    ? FeedOtherUtility.IsAvailableOneWayRecipientForManualOrder(recipient)
                    : FeedOtherUtility.IsAvailableOneWayRecipient(recipient) ||
                        sleepingBedFallback;
                if (!recipientAvailable ||
                    !FeedOtherUtility.ShouldRemainInPlaceForFeeding(recipient))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (useMobileBedLock)
                {
                    // Suspend the sleeper's LayDown job and replace it with the
                    // linked remain-in-bed recipient task. That task preserves the
                    // bed posture while holding its JobDriver asleep flag false.
                    // Ending it resumes the original LayDown job immediately.
                    Job recipientJob = JobMaker.MakeJob(
                        FeedOtherDefOf.RR_BeFedOtherPartner,
                        job.targetA.Thing,
                        pawn,
                        recipient.Position);
                    recipientJob.count = JobDriver_BeFedOtherPartner.RemainInPlaceMode;
                    recipient.jobs.StartJob(
                        recipientJob,
                        JobCondition.InterruptForced,
                        null,
                        true,
                        true,
                        null,
                        null,
                        false,
                        false);

                    recipientStarted = FeedOtherUtility.IsOneWayRecipientJobFor(
                        recipient,
                        pawn);
                    recipientBedLocked = recipientStarted;
                    if (!recipientStarted)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                }
                else
                {
                    // Genuinely immobile/downed patients keep their native
                    // LayDown or Wait_Downed job, reservation and posture.
                    recipientStarted = false;
                    recipientBedLocked = false;
                }

                if (FeedOtherUtility.ShouldRecordSessionCooldown(manualOrder))
                {
                    FeedOtherCooldownComponent.NotifyAutonomousSessionStarted(
                        pawn,
                        recipient,
                        false);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private JobCondition SessionTimeLimitCondition()
        {
            if (postMealSocialStarted)
            {
                return JobCondition.Ongoing;
            }

            if (Find.TickManager.TicksGame > startTick + FeedOtherUtility.MaximumSessionTicks)
            {
                sessionTimedOut = true;
                return JobCondition.Succeeded;
            }

            return JobCondition.Ongoing;
        }

        public void NotifyParticipantReachedVeryFull(Pawn participant)
        {
            if (participant == Recipient)
            {
                recipientReachedTarget = true;
            }
            else if (participant == pawn)
            {
                feederReachedTarget = true;
            }
        }

        private bool RecipientReachedTarget()
        {
            if (!recipientReachedTarget &&
                (FeedOtherUtility.HasReachedFeedingTarget(Recipient) ||
                 FeedOtherUtility.ShouldCompleteAfterRepeatedTopUps(
                     Recipient, recipientTopUpRoundsCompleted)))
            {
                recipientReachedTarget = true;
            }

            return recipientReachedTarget;
        }

        private bool FeederReachedTarget()
        {
            if (!feederReachedTarget &&
                (FeedOtherUtility.HasReachedFeedingTarget(pawn) ||
                 FeedOtherUtility.ShouldCompleteAfterRepeatedTopUps(
                     pawn, feederTopUpRoundsCompleted)))
            {
                feederReachedTarget = true;
            }

            return feederReachedTarget;
        }

        private bool RecipientCompleted()
        {
            return RecipientReachedTarget() || recipientCannotContinue;
        }

        private bool FeederCompleted()
        {
            return FeederReachedTarget() || feederCannotContinue;
        }

        private bool BothParticipantsCompleted()
        {
            return RecipientCompleted() && FeederCompleted();
        }

        private void FinishSession(JobCondition condition)
        {
            if (cleaningUp)
            {
                return;
            }

            cleaningUp = true;
            Pawn recipient = Recipient;
            if (postMealSocialStarted)
            {
                CompletePostMealSocial();
            }
            bool fullyFed = recipient != null && RecipientReachedTarget() && FeederReachedTarget();
            bool completedSession = condition == JobCondition.Succeeded && recipient != null &&
                (fullyFed || sessionTimedOut || BothParticipantsCompleted());

            if (completedSession && fullyFed)
            {
                FeedOtherConversationUtility.TryFinishingConversation(
                    conversationState, pawn, recipient, FeedOtherConversationUtility.FeedingInPlaceActivity);
                GainSharedFeedingOpinion(pawn, recipient);
            }

            FeedOtherUtility.DropCarriedSessionMeals(pawn);

            if (recipientStarted && FeedOtherUtility.IsOneWayRecipientJobFor(recipient, pawn))
            {
                // End the linked awake-in-bed task as an interruption. The
                // original sleeping LayDown job was suspended when this task
                // started, so RimWorld resumes it immediately. This remains
                // compatible with legacy linked bedside sessions as well.
                recipient.jobs.EndCurrentJob(
                    JobCondition.InterruptForced,
                    true,
                    true);

                // EndCurrentJob briefly resets posture while the suspended LayDown
                // job is resumed. Restore the in-bed posture in the same call so a
                // bedside recipient never visibly stands or pops out of the bed.
                if (FeedOtherUtility.IsBedCell(recipient.Position, recipient.Map))
                {
                    recipient.jobs.posture = PawnPosture.LayingInBed;
                    PortraitsCache.SetDirty(recipient);
                }
            }
        }

        private static void GainSharedFeedingOpinion(Pawn first, Pawn second)
        {
            if (!FeedOtherMod.Settings.socialOpinionMemoryEnabled ||
                FeedOtherDefOf.RR_SharedFeeding == null)
            {
                return;
            }

            first.needs?.mood?.thoughts?.memories?.TryGainMemory(
                FeedOtherDefOf.RR_SharedFeeding,
                second,
                null);
            second.needs?.mood?.thoughts?.memories?.TryGainMemory(
                FeedOtherDefOf.RR_SharedFeeding,
                first,
                null);
        }
    }
}
