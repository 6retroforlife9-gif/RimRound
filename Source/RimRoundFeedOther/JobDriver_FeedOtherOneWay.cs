using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public class JobDriver_FeedOtherOneWay : JobDriver
    {
        private Pawn Recipient => job.targetB.Pawn;
        private IntVec3 FoodAnchor => job.targetC.Cell;

        private bool recipientStarted;
        private bool recipientBedLocked;
        private bool cleaningUp;
        private bool recipientReachedTarget;
        private bool recipientCannotContinue;
        private bool sessionTimedOut;
        private int recipientTopUpRoundsCompleted;
        private int mealsCompleted;
        private bool currentMealIsTopUp;
        private int nextRecipientPathRefreshTick;
        private FeedOtherConversationState conversationState = new FeedOtherConversationState();
        private bool recipientWasAsleepAtStart;
        private bool postMealSocialStarted;
        private int postMealSocialStartTick;
        private bool postMealSocialCompleted;
        private FeedOtherConversationState postMealConversationState = new FeedOtherConversationState();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref recipientStarted, "feedOtherOneWayRecipientStarted", false);
            Scribe_Values.Look(ref recipientBedLocked, "feedOtherOneWayRecipientBedLocked", false);
            Scribe_Values.Look(ref recipientReachedTarget, "feedOtherOneWayTargetReached", false);
            Scribe_Values.Look(ref recipientCannotContinue, "feedOtherOneWayCannotContinue", false);
            Scribe_Values.Look(ref sessionTimedOut, "feedOtherOneWaySessionTimedOut", false);
            Scribe_Values.Look(ref recipientTopUpRoundsCompleted, "feedOtherOneWayTopUpRoundsCompleted", 0);
            Scribe_Values.Look(ref mealsCompleted, "feedOtherOneWayMealsCompleted", 0);
            Scribe_Values.Look(ref currentMealIsTopUp, "feedOtherOneWayCurrentMealIsTopUp", false);
            Scribe_Deep.Look(ref conversationState, "feedOtherOneWayConversationState");
            Scribe_Values.Look(ref recipientWasAsleepAtStart, "feedOtherOneWayRecipientWasAsleep", false);
            Scribe_Values.Look(ref postMealSocialStarted, "feedOtherOneWayPostMealStarted", false);
            Scribe_Values.Look(ref postMealSocialStartTick, "feedOtherOneWayPostMealStartTick", 0);
            Scribe_Values.Look(ref postMealSocialCompleted, "feedOtherOneWayPostMealCompleted", false);
            Scribe_Deep.Look(ref postMealConversationState, "feedOtherOneWayPostMealConversationState");
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
            this.AddFailCondition(() => !job.playerForced && !Recipient.Awake() &&
                !FeedOtherUtility.IsAvailableSleepingOneWayRecipientFallback(Recipient));
            this.AddFailCondition(() => job.playerForced
                ? !FeedOtherUtility.IsManualFeederEligible(pawn) ||
                    !FeedOtherUtility.IsManualRecipientEligible(Recipient)
                : !FeedOtherUtility.IsNonEatingOneWayFeederEligible(pawn) ||
                    (!FeedOtherUtility.IsOneWayRecipientEligible(Recipient) &&
                     !FeedOtherUtility.IsSleepingOneWayRecipientFallbackEligible(Recipient)));
            this.AddEndCondition(SessionTimeLimitCondition);
            this.AddFailCondition(() => recipientStarted &&
                !FeedOtherUtility.IsOneWayRecipientJobFor(Recipient, pawn));

            yield return MakeStartRecipientToil();

            Toil postMealSocial = null;
            Toil findFood = ToilMaker.MakeToil("FindOneWayFeedingMeal");
            findFood.initAction = delegate
            {
                if (RecipientHasFinishedSession())
                {
                    FinishMealOrBeginPostMeal(postMealSocial);
                    return;
                }

                Thing meal;
                bool followUpCollection = mealsCompleted > 0;
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
                    if (mealsCompleted > 0)
                    {
                        // A completed feeding round is a valid event. Do not
                        // abandon the social phase merely because the next meal
                        // would require another long storage run.
                        FinishMealOrBeginPostMeal(postMealSocial);
                    }
                    else
                    {
                        EndJobWith(JobCondition.Succeeded);
                    }
                    return;
                }

                job.targetA = meal;
                currentMealIsTopUp = !FeedOtherUtility.IsPreparedMeal(meal);
                job.count = FeedOtherUtility.MealCollectionCount(pawn, meal, Recipient);
                job.count = FeedOtherUtility.ReserveMealStack(pawn, job, meal, job.count);
                if (job.count <= 0)
                {
                    recipientCannotContinue = true;
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
            };
            findFood.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findFood;

            foreach (Toil collectMeal in
                FeedOtherUtility.CollectSessionMealSourceToils(
                    pawn,
                    Recipient))
            {
                yield return collectMeal;
            }

            Toil shareCarriedMealTarget = ToilMaker.MakeToil("ShareOneWayFeedingMealTarget");
            shareCarriedMealTarget.initAction = delegate
            {
                Thing carriedMeal = pawn.carryTracker?.CarriedThing;
                if (carriedMeal == null ||
                    (recipientStarted && !FeedOtherUtility.IsOneWayRecipientJobFor(Recipient, pawn)))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // PickupIngestible can replace target A when it splits a stack.
                // A mobile linked recipient still receives the carried instance.
                // A native bed-bound recipient keeps LayDown untouched; a
                // sleeping-mobile fallback uses the linked awake-in-bed job.
                job.targetA = carriedMeal;
                if (recipientStarted)
                {
                    Recipient.CurJob.targetA = carriedMeal;
                }
            };
            shareCarriedMealTarget.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return shareCarriedMealTarget;

            // Do not complete merely because the feeder touches a recipient who
            // is still walking. The linked recipient job marks itself ready only
            // after reaching its chair or after meeting the feeder and stopping.
            Toil goToRecipient = MakeSynchronizeWithRecipientToil();
            yield return goToRecipient;

            Toil feed = FeedOtherUtility.ChewIngestibleWithEatingSpeed(
                    Recipient,
                    FeedOtherUtility.AssistedEatingDurationFactor,
                    TargetIndex.A,
                    TargetIndex.None)
                .FailOnCannotTouch(TargetIndex.B, PathEndMode.Touch);
            feed.AddFailCondition(() =>
                !FeedOtherUtility.CanFullyConsumePlannedPortion(Recipient, pawn.carryTracker?.CarriedThing));
            feed.socialMode = RandomSocialMode.Off;
            feed.AddPreTickIntervalAction(delegate(int delta)
            {
                if (Recipient != null)
                {
                    pawn.rotationTracker.FaceCell(Recipient.Position);
                }

                FeedOtherConversationUtility.TickConversation(
                    conversationState, pawn, Recipient, ConversationActivity());
                FeedOtherUtility.GainFeedOtherRecreation(
                    pawn, delta, FeedOtherUtility.OneWayFeedingRecreationFactor);
                FeedOtherUtility.GainFeedOtherRecreation(
                    Recipient,
                    delta,
                    recipientStarted && !recipientBedLocked
                        ? FeedOtherUtility.OneWayFeedingRecreationFactor
                        : FeedOtherUtility.BedsideFeedingRecreationFactor);
            });
            yield return feed;
            yield return Toils_Ingest.FinalizeIngest(Recipient, TargetIndex.A);

            Toil continueOrFinish = ToilMaker.MakeToil("ContinueOneWayFeeding");
            continueOrFinish.initAction = delegate
            {
                mealsCompleted++;
                if (currentMealIsTopUp)
                {
                    recipientTopUpRoundsCompleted++;
                }

                FeedOtherUtility.TryGrantVeryFullMood(Recipient);
                if (RecipientHasFinishedSession())
                {
                    FinishMealOrBeginPostMeal(postMealSocial);
                }
                else if (FeedOtherUtility.IsCarryingSessionMeal(pawn) &&
                    FeedOtherUtility.CanFullyConsumePlannedPortion(Recipient, pawn.carryTracker.CarriedThing))
                {
                    // Keep feeding from the carried stack while another complete
                    // serving still fits the recipient's current capacity.
                    if (recipientStarted)
                    {
                        Recipient.CurJob.targetA = pawn.carryTracker.CarriedThing;
                    }
                    JumpToToil(goToRecipient);
                }
                else
                {
                    // Recalculate from the recipient's live fullness. If the
                    // carried allocation was insufficient, return for another
                    // highest-priority acceptable meal instead of ending early.
                    FeedOtherUtility.DropCarriedSessionMeals(pawn);
                    JumpToToil(findFood);
                }
            };
            continueOrFinish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return continueOrFinish;

            postMealSocial = ToilMaker.MakeToil("PostMealOneWaySocialRecreation");
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
                ConversationActivity());

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

        private string ConversationActivity()
        {
            return !recipientStarted || recipientBedLocked
                ? FeedOtherConversationUtility.FeedingInPlaceActivity
                : FeedOtherConversationUtility.FeedingActivity;
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

        private Toil MakeStartRecipientToil()
        {
            Toil toil = ToilMaker.MakeToil("StartOneWayFeedingRecipient");
            toil.initAction = delegate
            {
                Pawn recipient = Recipient;
                recipientWasAsleepAtStart = recipient != null && !recipient.Awake();
                bool manualOrder = job.playerForced;
                bool sleepingBedFallback = !manualOrder &&
                    FeedOtherUtility.IsAvailableSleepingOneWayRecipientFallback(recipient);
                bool useMobileBedLock =
                    FeedOtherUtility.IsMobileBedLockCandidate(recipient);
                bool recipientAvailable = manualOrder
                    ? FeedOtherUtility.IsAvailableOneWayRecipientForManualOrder(recipient)
                    : FeedOtherUtility.IsAvailableOneWayRecipient(recipient) ||
                        sleepingBedFallback;
                if (!recipientAvailable)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Capture this before StartJob replaces or suspends a bed-rest
                // job. Once that transition starts, Pawn.InBed() may briefly
                // become false even though the pawn must never leave the bed.
                bool recipientMustRemainInPlace = manualOrder
                    ? FeedOtherUtility.ShouldRemainInPlaceForManualFeeding(recipient)
                    : FeedOtherUtility.ShouldRemainInPlaceForFeeding(recipient);
                IntVec3 recipientDestination = recipient.Position;
                int recipientMovementMode = JobDriver_BeFedOtherPartner.RemainInPlaceMode;

                if (recipientMustRemainInPlace && !useMobileBedLock)
                {
                    // Genuinely bed-bound or downed recipients keep their native
                    // LayDown/Wait_Downed job exactly like vanilla patient feeding.
                    // A healthy sleeping autonomous fallback or manual right-click
                    // sleeper is different: it receives the linked awake-in-bed
                    // job below so it cannot fall asleep or leave before the
                    // feeding session has ended.
                    recipientStarted = false;
                    recipientBedLocked = false;
                    if (FeedOtherUtility.ShouldRecordSessionCooldown(manualOrder))
                    {
                        FeedOtherCooldownComponent.NotifyAutonomousSessionStarted(
                            pawn,
                            recipient,
                            true);
                    }
                    return;
                }

                recipientBedLocked = useMobileBedLock;

                if (!recipientMustRemainInPlace)
                {
                    IntVec3 diningCell;
                    float maximumDistance = FeedOtherUtility.OneWayDiningSeatSearchRadius;
                    if (FeedOtherUtility.TryFindDiningSeat(
                            recipient,
                            job.targetA.Thing,
                            FoodAnchor,
                            out diningCell) &&
                        recipient.Position.DistanceToSquared(diningCell) <=
                            maximumDistance * maximumDistance)
                    {
                        // A nearby chair remains the preferred destination. The
                        // feeder follows the recipient but waits for them to sit
                        // before the chewing toil can start.
                        recipientDestination = diningCell;
                        recipientMovementMode = JobDriver_BeFedOtherPartner.FixedDiningSeatMode;
                    }
                    else
                    {
                        // Do not send a pawn across the map to a table near food
                        // storage. They dynamically approach the feeder instead.
                        recipientMovementMode = JobDriver_BeFedOtherPartner.MeetFeederMode;
                    }
                }

                // Re-clear at the hand-off point as a final guard. A sleeping
                // pawn may otherwise retain a queued vanilla Ingest job and run
                // it as soon as this feeding session finishes.
                if (manualOrder)
                {
                    recipient.jobs.ClearQueuedJobs();
                }

                Job recipientJob = JobMaker.MakeJob(
                    FeedOtherDefOf.RR_BeFedOtherPartner,
                    job.targetA.Thing,
                    pawn,
                    recipientDestination);
                recipientJob.count = recipientMovementMode;
                recipientJob.playerForced = manualOrder;
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

                recipientStarted = FeedOtherUtility.IsOneWayRecipientJobFor(recipient, pawn);
                if (!recipientStarted)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (FeedOtherUtility.ShouldRecordSessionCooldown(manualOrder))
                {
                    FeedOtherCooldownComponent.NotifyAutonomousSessionStarted(
                        pawn,
                        recipient,
                        true);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private Toil MakeSynchronizeWithRecipientToil()
        {
            Toil toil = ToilMaker.MakeToil("SynchronizeOneWayFeedingPositions");
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.initAction = UpdateSynchronizedApproach;
            toil.tickAction = UpdateSynchronizedApproach;
            return toil;
        }

        private void UpdateSynchronizedApproach()
        {
            Pawn recipient = Recipient;
            if (recipient == null)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (!recipientStarted)
            {
                // Bed-bound recipients retain their native LayDown job. Only the
                // feeder paths; the patient is never stopped, stood up or retasked.
                if (pawn.Position.AdjacentTo8WayOrInside(recipient.Position))
                {
                    pawn.pather?.StopDead();
                    ReadyForNextToil();
                }
                else
                {
                    StartOrRefreshPathToRecipient(recipient);
                }
                return;
            }

            if (!FeedOtherUtility.IsOneWayRecipientJobFor(recipient, pawn))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            Job recipientJob = recipient.CurJob;
            bool recipientReady = recipientJob.count <=
                    JobDriver_BeFedOtherPartner.RemainInPlaceMode ||
                recipientJob.count >= JobDriver_BeFedOtherPartner.ReadyMode;
            bool touching = pawn.Position.AdjacentTo8WayOrInside(recipient.Position);

            if (recipientReady)
            {
                // The recipient's waiting toil is now authoritative. Stop any
                // residual path and only advance once both pawns are stationary
                // and physically in feeding range.
                recipient.pather?.StopDead();
                if (touching)
                {
                    pawn.pather?.StopDead();
                    ReadyForNextToil();
                    return;
                }

                StartOrRefreshPathToRecipient(recipient);
                return;
            }

            if (recipientJob.count == JobDriver_BeFedOtherPartner.MeetFeederMode &&
                recipient.pather != null && recipient.pather.Moving)
            {
                // In meet mode the recipient is already walking toward the
                // feeder. Holding the feeder still prevents an endless chase.
                pawn.pather?.StopDead();
                return;
            }

            // Fixed-seat mode: follow the moving recipient, but deliberately do
            // not complete while merely touching them en route to the chair.
            StartOrRefreshPathToRecipient(recipient);
        }

        private void StartOrRefreshPathToRecipient(Pawn recipient)
        {
            if (pawn.pather == null)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (!pawn.pather.Moving || currentTick >= nextRecipientPathRefreshTick)
            {
                nextRecipientPathRefreshTick = currentTick + 30;
                pawn.pather.StartPath(recipient, PathEndMode.Touch);
            }
        }

        public void NotifyParticipantReachedVeryFull(Pawn participant)
        {
            if (participant == Recipient)
            {
                recipientReachedTarget = true;
            }
        }

        private bool RecipientHasFinishedSession()
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
            bool fullyFed = recipient != null && RecipientHasFinishedSession();
            bool completedSession = condition == JobCondition.Succeeded && recipient != null &&
                (fullyFed || recipientCannotContinue || sessionTimedOut);

            if (completedSession && fullyFed)
            {
                FeedOtherConversationUtility.TryFinishingConversation(
                    conversationState, pawn, recipient, ConversationActivity());
                GainSharedFeedingOpinion(pawn, recipient);
            }

            // Any unused serving is left at the table or beside the recipient,
            // rather than remaining stuck in the feeder's hands.
            FeedOtherUtility.DropCarriedSessionMeals(pawn);

            if (recipientStarted && FeedOtherUtility.IsOneWayRecipientJobFor(recipient, pawn))
            {
                // End the linked recipient task as an interruption. For a
                // sleeping-mobile fallback, StartJob suspended the original
                // LayDown task, so RimWorld immediately resumes that sleep job.
                // This also remains compatible with legacy linked bedside saves.
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

    public class JobDriver_BeFedOtherPartner : JobDriver
    {
        public const int RemainInPlaceMode = 0;
        public const int FixedDiningSeatMode = 1;
        public const int MeetFeederMode = 2;
        public const int ReadyMode = 3;

        private Pawn Feeder => job.targetB.Pawn;
        private bool MustRemainInPlace => job.count == RemainInPlaceMode;
        private bool UsesFixedDiningSeat => job.count == FixedDiningSeatMode;
        private bool MeetsFeeder => job.count == MeetFeederMode;
        private int nextFeederPathRefreshTick;

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            asleep = false;
        }

        public override void SetInitialPosture()
        {
            asleep = false;
            if (!MustRemainInPlace)
            {
                base.SetInitialPosture();
                return;
            }

            PawnPosture previousPosture = pawn.GetPosture();
            if (FeedOtherUtility.IsBedCell(job.targetC.Cell, pawn.Map))
            {
                pawn.jobs.posture = previousPosture.InBed()
                    ? previousPosture
                    : PawnPosture.LayingInBed;
            }
            else if (previousPosture.Laying())
            {
                pawn.jobs.posture = previousPosture;
            }
            else
            {
                pawn.jobs.posture = pawn.Downed
                    ? PawnPosture.LayingOnGroundNormal
                    : PawnPosture.Standing;
            }

            PortraitsCache.SetDirty(pawn);
        }

        public override bool CanBeginNowWhileLyingDown()
        {
            return MustRemainInPlace;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // The feeder owns the pawn reservation. Reserving in both directions
            // can prevent the linked recipient job from starting in RimWorld 1.6.
            if (Feeder == null)
            {
                return false;
            }

            if (!MustRemainInPlace && FeedOtherUtility.IsDiningSeat(job.targetC.Cell, pawn.Map))
            {
                IntVec3 chosenSeat = job.targetC.Cell;
                if (!pawn.ReserveSittableOrSpot(chosenSeat, job, false))
                {
                    // The originally selected chair may be taken between job
                    // creation and reservation. Try one fresh nearby chair, then
                    // continue the event standing with the feeder instead of
                    // failing the linked job.
                    IntVec3 alternateSeat;
                    if (FeedOtherUtility.TryFindDiningSeatExcept(
                            pawn,
                            job.targetA.Thing,
                            Feeder.Position,
                            chosenSeat,
                            out alternateSeat) &&
                        pawn.ReserveSittableOrSpot(alternateSeat, job, false))
                    {
                        chosenSeat = alternateSeat;
                        job.targetC = alternateSeat;
                    }
                    else
                    {
                        job.count = MeetFeederMode;
                        job.targetC = pawn.Position;
                        return true;
                    }
                }

                pawn.Map.pawnDestinationReservationManager.Reserve(
                    pawn,
                    job,
                    chosenSeat);
            }

            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.FailOnMentalState(TargetIndex.B);
            this.AddFailCondition(() => !job.playerForced && !Feeder.Awake());
            this.AddFailCondition(() => job.playerForced
                ? !FeedOtherUtility.IsOneWayRecipientEligibleForManualOrder(pawn)
                : !FeedOtherUtility.IsOneWayRecipientEligible(pawn));
            this.AddFailCondition(() => !FeedOtherUtility.IsOneWayFeederJobFor(Feeder, pawn));
            this.AddFailCondition(() => MustRemainInPlace && pawn.Position != job.targetC.Cell);

            // Target C is the destination chosen by the feeder before this job
            // interrupted the recipient's previous job. For a bed-bound pawn it
            // is their current bed cell, so never re-evaluate Pawn.InBed() here.
            if (UsesFixedDiningSeat && pawn.Position != job.targetC.Cell)
            {
                yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
                yield return MakeMarkReadyToil();
            }
            else if (UsesFixedDiningSeat)
            {
                yield return MakeMarkReadyToil();
            }
            else if (MeetsFeeder)
            {
                yield return MakeApproachFeederToil();
            }

            Toil wait = ToilMaker.MakeToil("WaitToBeFedOther");
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            wait.socialMode = RandomSocialMode.Off;
            wait.tickIntervalAction = delegate(int delta)
            {
                asleep = false;
                pawn.pather?.StopDead();
                if (MustRemainInPlace)
                {
                    MaintainRequiredPosture();
                }
                else
                {
                    FeedOtherUtility.FaceDiningTableOrPawn(pawn, Feeder);
                }

                FeedOtherUtility.GainFeedOtherRecreation(
                    pawn,
                    delta,
                    MustRemainInPlace
                        ? FeedOtherUtility.BedsideFeedingRecreationFactor
                        : FeedOtherUtility.OneWayFeedingRecreationFactor);
            };
            yield return wait;
        }

        private Toil MakeMarkReadyToil()
        {
            Toil toil = ToilMaker.MakeToil("MarkReadyToBeFedOther");
            toil.initAction = delegate
            {
                pawn.pather?.StopDead();
                job.count = ReadyMode;
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private Toil MakeApproachFeederToil()
        {
            Toil toil = ToilMaker.MakeToil("ApproachOneWayFeeder");
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.initAction = UpdateApproachToFeeder;
            toil.tickAction = UpdateApproachToFeeder;
            return toil;
        }

        private void UpdateApproachToFeeder()
        {
            Pawn feeder = Feeder;
            if (feeder == null || !FeedOtherUtility.IsOneWayFeederJobFor(feeder, pawn))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            bool feederHasMeal = FeedOtherUtility.IsCarryingSessionMeal(feeder);
            if (feederHasMeal && pawn.Position.AdjacentTo8WayOrInside(feeder.Position))
            {
                pawn.pather?.StopDead();
                job.targetC = pawn.Position;
                job.count = ReadyMode;
                ReadyForNextToil();
                return;
            }

            if (pawn.pather == null)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (!pawn.pather.Moving || currentTick >= nextFeederPathRefreshTick)
            {
                nextFeederPathRefreshTick = currentTick + 30;
                pawn.pather.StartPath(feeder, PathEndMode.Touch);
            }
        }

        private void MaintainRequiredPosture()
        {
            pawn.pather?.StopDead();
            if (FeedOtherUtility.IsBedCell(job.targetC.Cell, pawn.Map) &&
                !pawn.GetPosture().InBed())
            {
                pawn.jobs.posture = PawnPosture.LayingInBed;
                PortraitsCache.SetDirty(pawn);
            }
            else if (pawn.Downed && !pawn.GetPosture().Laying())
            {
                pawn.jobs.posture = PawnPosture.LayingOnGroundNormal;
                PortraitsCache.SetDirty(pawn);
            }
        }
    }
}
