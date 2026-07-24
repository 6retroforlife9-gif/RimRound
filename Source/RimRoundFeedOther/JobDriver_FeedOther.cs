using RimRound.Comps;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public abstract class JobDriver_FeedOtherBase : JobDriver
    {
        protected abstract bool IsLeader { get; }

        protected Pawn Partner => job.targetB.Pawn;
        protected IntVec3 FoodAnchor => job.targetC.Cell;

        private bool partnerStarted;
        private bool cleaningUp;
        private bool feedingTargetReached;
        private bool cannotContinueFeeding;
        private bool sessionTimedOut;
        private int mealsCompleted;
        private int topUpRoundsCompleted;
        private bool currentMealIsTopUp;
        private IntVec3 diningSpot = IntVec3.Invalid;
        private bool diningDecisionMade;
        private bool usingDiningTable;
        private bool atDiningDestination;
        private FeedOtherConversationState conversationState = new FeedOtherConversationState();
        private bool postMealSocialRequested;
        private bool postMealSocialStarted;
        private int postMealSocialStartTick;
        private bool postMealSocialCompleted;
        private FeedOtherConversationState postMealConversationState = new FeedOtherConversationState();

        private JobDriver_FeedOtherBase PartnerDriver =>
            Partner?.jobs?.curDriver as JobDriver_FeedOtherBase;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref partnerStarted, "feedOtherPartnerStarted", false);
            Scribe_Values.Look(ref feedingTargetReached, "feedOtherTargetReached", false);
            Scribe_Values.Look(ref cannotContinueFeeding, "feedOtherCannotContinue", false);
            Scribe_Values.Look(ref sessionTimedOut, "feedOtherSessionTimedOut", false);
            Scribe_Values.Look(ref mealsCompleted, "feedOtherMealsCompleted", 0);
            Scribe_Values.Look(ref topUpRoundsCompleted, "feedOtherTopUpRoundsCompleted", 0);
            Scribe_Values.Look(ref currentMealIsTopUp, "feedOtherCurrentMealIsTopUp", false);
            Scribe_Values.Look(ref diningSpot, "feedOtherDiningSpot", IntVec3.Invalid);
            Scribe_Values.Look(ref diningDecisionMade, "feedOtherDiningDecisionMade", false);
            Scribe_Values.Look(ref usingDiningTable, "feedOtherUsingDiningTable", false);
            Scribe_Values.Look(ref atDiningDestination, "feedOtherAtDiningDestination", false);
            Scribe_Deep.Look(ref conversationState, "feedOtherConversationState");
            Scribe_Values.Look(ref postMealSocialRequested, "feedOtherPostMealRequested", false);
            Scribe_Values.Look(ref postMealSocialStarted, "feedOtherPostMealStarted", false);
            Scribe_Values.Look(ref postMealSocialStartTick, "feedOtherPostMealStartTick", 0);
            Scribe_Values.Look(ref postMealSocialCompleted, "feedOtherPostMealCompleted", false);
            Scribe_Deep.Look(ref postMealConversationState, "feedOtherPostMealConversationState");
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
            // The leader's reservation is enough to make the pair exclusive. A
            // reverse reservation from the linked partner job can conflict with
            // the leader's active pawn reservation in RimWorld 1.6.
            return Partner != null && (!IsLeader ||
                pawn.Reserve(Partner, job, 1, -1, null, errorOnFailed));
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.AddFinishAction(FinishSession);
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.FailOnMentalState(TargetIndex.B);
            this.FailOnNotAwake(TargetIndex.B);
            this.AddFailCondition(() => !FeedOtherUtility.IsRimRoundEligible(pawn) ||
                !FeedOtherUtility.IsRimRoundEligible(Partner) ||
                FeedOtherUtility.ShouldRemainInPlaceForFeeding(pawn) ||
                FeedOtherUtility.ShouldRemainInPlaceForFeeding(Partner));
            this.AddFailCondition(SessionLinkBroken);

            if (IsLeader)
            {
                this.AddEndCondition(SessionTimeLimitCondition);
                yield return MakeStartPartnerToil();
            }

            Toil waitForPartner = null;
            Toil waitForBothMeals = null;
            Toil postMealSocial = null;

            Toil findFood = ToilMaker.MakeToil("FindFeedOtherMeal");
            findFood.initAction = delegate
            {
                if (HasCompletedOwnPartOfSession())
                {
                    JumpToToil(waitForPartner);
                    return;
                }

                Thing meal;
                Thing partnerMeal = Partner?.CurJob?.targetA.Thing;
                bool followUpCollection = mealsCompleted > 0;
                bool foundMeal = followUpCollection
                    ? FeedOtherUtility.TryFindNearbyStoredMeal(
                        pawn,
                        pawn.Position,
                        partnerMeal,
                        out meal)
                    : FeedOtherUtility.TryFindStoredMeal(
                        pawn,
                        FoodAnchor,
                        partnerMeal,
                        out meal);
                if (!foundMeal)
                {
                    // Once the pair has eaten, do not send either participant
                    // back across the map. Complete their eating part and allow
                    // the partner/post-meal social phase to finish normally.
                    cannotContinueFeeding = true;
                    JumpToToil(waitForPartner);
                    return;
                }

                job.targetA = meal;
                currentMealIsTopUp = !FeedOtherUtility.IsPreparedMeal(meal);
                atDiningDestination = false;
                job.count = meal == partnerMeal
                    ? FeedOtherUtility.SharedStackCollectionCount(pawn, Partner, meal)
                    : FeedOtherUtility.MealCollectionCount(pawn, meal, pawn);
                job.count = FeedOtherUtility.ReserveMealStack(pawn, job, meal, job.count);
                if (job.count <= 0)
                {
                    cannotContinueFeeding = true;
                    JumpToToil(waitForPartner);
                    return;
                }
            };
            findFood.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findFood;

            foreach (Toil collectMeal in
                FeedOtherUtility.CollectSessionMealSourceToils(
                    pawn,
                    pawn))
            {
                yield return collectMeal;
            }

            waitForBothMeals = ToilMaker.MakeToil("WaitForFeedOtherMeals");
            waitForBothMeals.defaultCompleteMode = ToilCompleteMode.Never;
            waitForBothMeals.tickIntervalAction = delegate(int delta)
            {
                if (PartnerReadyForEatingRound())
                {
                    ReadyForNextToil();
                }
            };
            yield return waitForBothMeals;

            Toil chooseDiningSpots = ToilMaker.MakeToil("ChooseFeedOtherDiningSpots");
            chooseDiningSpots.initAction = delegate
            {
                if (diningDecisionMade)
                {
                    return;
                }

                if (!IsLeader)
                {
                    // If the leader ran out of food before the first dining
                    // round, let the remaining participant establish the
                    // normal adjacent fallback instead of waiting forever for
                    // a table decision the leader can no longer make.
                    if (PartnerCompletedOwnPartOfSession())
                    {
                        AssignDiningDecision(false, IntVec3.Invalid);
                        PartnerDriver?.AssignDiningDecision(false, IntVec3.Invalid);
                    }
                    return;
                }

                JobDriver_FeedOtherBase partnerDriver = PartnerDriver;
                Thing ownMeal = pawn.carryTracker?.CarriedThing;
                Thing partnerMeal = Partner?.carryTracker?.CarriedThing;
                IntVec3 ownSpot = IntVec3.Invalid;
                IntVec3 partnerSpot = IntVec3.Invalid;
                bool foundTable = partnerDriver != null &&
                    FeedOtherUtility.TryFindSharedDiningSpots(
                        pawn,
                        Partner,
                        ownMeal,
                        partnerMeal,
                        out ownSpot,
                        out partnerSpot);

                AssignDiningDecision(foundTable, foundTable ? ownSpot : IntVec3.Invalid);
                partnerDriver?.AssignDiningDecision(
                    foundTable,
                    foundTable ? partnerSpot : IntVec3.Invalid);
            };
            chooseDiningSpots.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return chooseDiningSpots;

            Toil waitForDiningDecision = ToilMaker.MakeToil("WaitForFeedOtherDiningDecision");
            waitForDiningDecision.defaultCompleteMode = ToilCompleteMode.Never;
            waitForDiningDecision.tickIntervalAction = delegate(int delta)
            {
                if (diningDecisionMade)
                {
                    ReadyForNextToil();
                }
            };
            yield return waitForDiningDecision;

            yield return MakeMoveToDiningOrPartnerToil();

            Toil markDiningArrival = ToilMaker.MakeToil("MarkFeedOtherDiningArrival");
            markDiningArrival.initAction = delegate
            {
                atDiningDestination = true;
            };
            markDiningArrival.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return markDiningArrival;

            Toil waitForPartnerAtDiningSpot = ToilMaker.MakeToil("WaitForFeedOtherDiningPartner");
            waitForPartnerAtDiningSpot.defaultCompleteMode = ToilCompleteMode.Never;
            waitForPartnerAtDiningSpot.socialMode = RandomSocialMode.Off;
            waitForPartnerAtDiningSpot.tickIntervalAction = delegate(int delta)
            {
                FeedOtherUtility.FaceDiningTableOrPawn(pawn, Partner);
                TickConversation();
                JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.None,
                    FeedOtherUtility.SharedMealRecreationFactor, null);
                if (PartnerCompletedOwnPartOfSession() ||
                    (PartnerDriver != null && PartnerDriver.atDiningDestination))
                {
                    ReadyForNextToil();
                }
            };
            yield return waitForPartnerAtDiningSpot;

            Toil chew = FeedOtherUtility.ChewIngestibleWithEatingSpeed(
                pawn,
                FeedOtherUtility.SharedEatingDurationFactor,
                TargetIndex.A,
                TargetIndex.None);
            chew.AddFailCondition(() => Partner == null ||
                !pawn.Position.InHorDistOf(Partner.Position, FeedOtherUtility.MaximumSharedDiningDistance) ||
                !FeedOtherUtility.CanFullyConsumePlannedPortion(pawn, pawn.carryTracker?.CarriedThing));
            chew.socialMode = RandomSocialMode.Off;
            chew.AddPreTickIntervalAction(delegate(int delta)
            {
                FeedOtherUtility.FaceDiningTableOrPawn(pawn, Partner);
                TickConversation();
                JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.None,
                    FeedOtherUtility.SharedMealRecreationFactor, null);
            });
            yield return chew;
            yield return Toils_Ingest.FinalizeIngest(pawn, TargetIndex.A);

            Toil recordCompletedMeal = ToilMaker.MakeToil("RecordFeedOtherMeal");
            recordCompletedMeal.initAction = delegate
            {
                mealsCompleted++;
                if (currentMealIsTopUp)
                {
                    topUpRoundsCompleted++;
                }

                FeedOtherUtility.TryGrantVeryFullMood(pawn);
                if (FeedOtherUtility.HasReachedFeedingTarget(pawn) ||
                    FeedOtherUtility.ShouldCompleteAfterRepeatedTopUps(pawn, topUpRoundsCompleted))
                {
                    feedingTargetReached = true;
                }
            };
            recordCompletedMeal.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return recordCompletedMeal;

            Toil waitForBothToFinishMeal = ToilMaker.MakeToil("WaitForFeedOtherMealCompletion");
            waitForBothToFinishMeal.defaultCompleteMode = ToilCompleteMode.Never;
            waitForBothToFinishMeal.socialMode = RandomSocialMode.Off;
            waitForBothToFinishMeal.tickIntervalAction = delegate(int delta)
            {
                FeedOtherUtility.FaceDiningTableOrPawn(pawn, Partner);
                TickConversation();

                JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.None,
                    FeedOtherUtility.SharedMealRecreationFactor, null);
                if (PartnerFinishedEatingRound())
                {
                    ReadyForNextToil();
                }
            };
            yield return waitForBothToFinishMeal;

            Toil chooseNextAction = ToilMaker.MakeToil("ContinueFeedOtherSession");
            chooseNextAction.initAction = delegate
            {
                if (postMealSocialRequested)
                {
                    JumpToToil(postMealSocial);
                }
                else if (IsLeader && BothPawnsCompletedSession())
                {
                    FeedOtherConversationUtility.TryFinishingConversation(
                        conversationState, pawn, Partner, FeedOtherConversationUtility.SharedMealActivity);
                    if (BeginPostMealSocial())
                    {
                        JumpToToil(postMealSocial);
                    }
                    else
                    {
                        EndJobWith(JobCondition.Succeeded);
                    }
                }
                else if (HasCompletedOwnPartOfSession())
                {
                    JumpToToil(waitForPartner);
                }
                else if (FeedOtherUtility.IsCarryingSessionMeal(pawn) &&
                    FeedOtherUtility.CanFullyConsumePlannedPortion(pawn, pawn.carryTracker.CarriedThing))
                {
                    JumpToToil(waitForBothMeals);
                }
                else
                {
                    // This pawn is still below Very Full. Drop any unusable
                    // remainder and collect another meal while a completed partner
                    // waits at the dining location.
                    FeedOtherUtility.DropCarriedSessionMeals(pawn);
                    JumpToToil(findFood);
                }
            };
            chooseNextAction.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return chooseNextAction;

            waitForPartner = ToilMaker.MakeToil("WaitForFeedOtherPartner");
            waitForPartner.defaultCompleteMode = ToilCompleteMode.Never;
            waitForPartner.socialMode = RandomSocialMode.Off;
            waitForPartner.tickIntervalAction = delegate(int delta)
            {
                if (postMealSocialRequested || PartnerDriver?.postMealSocialRequested == true)
                {
                    SynchronizePostMealStateFromPartner();
                    JumpToToil(postMealSocial);
                    return;
                }

                FeedOtherUtility.FaceDiningTableOrPawn(pawn, Partner);
                TickConversation();

                JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.None,
                    FeedOtherUtility.SharedMealRecreationFactor, null);

                if (IsLeader && BothPawnsCompletedSession())
                {
                    FeedOtherConversationUtility.TryFinishingConversation(
                        conversationState, pawn, Partner, FeedOtherConversationUtility.SharedMealActivity);
                    if (BeginPostMealSocial())
                    {
                        JumpToToil(postMealSocial);
                    }
                    else
                    {
                        EndJobWith(JobCondition.Succeeded);
                    }
                }
                else if (!HasCompletedOwnPartOfSession())
                {
                    ReadyForNextToil();
                }
            };
            yield return waitForPartner;
            yield return Toils_Jump.Jump(findFood);

            postMealSocial = ToilMaker.MakeToil("PostMealSocialRecreation");
            postMealSocial.defaultCompleteMode = ToilCompleteMode.Never;
            postMealSocial.socialMode = RandomSocialMode.Off;
            postMealSocial.initAction = delegate
            {
                SynchronizePostMealStateFromPartner();
                pawn.pather?.StopDead();
            };
            postMealSocial.tickIntervalAction = delegate(int delta)
            {
                SynchronizePostMealStateFromPartner();
                pawn.pather?.StopDead();
                FeedOtherUtility.FaceDiningTableOrPawn(pawn, Partner);
                PostMealSocialUtility.GainRecreation(pawn, delta);

                if (IsLeader)
                {
                    PostMealSocialUtility.TickRomanticHearts(pawn, Partner);
                }

                if (!IsLeader)
                {
                    return;
                }

                FeedOtherConversationUtility.TickConversation(
                    postMealConversationState,
                    pawn,
                    Partner,
                    FeedOtherConversationUtility.PostSharedMealActivity);

                if (PostMealSocialUtility.ShouldEnd(
                    pawn,
                    Partner,
                    postMealSocialStartTick,
                    false))
                {
                    CompletePostMealSocial();
                    EndJobWith(JobCondition.Succeeded);
                }
            };
            yield return postMealSocial;
        }

        private void TickConversation()
        {
            if (IsLeader)
            {
                FeedOtherConversationUtility.TickConversation(
                    conversationState, pawn, Partner, FeedOtherConversationUtility.SharedMealActivity);
            }
        }

        private void FinishSession(JobCondition condition)
        {
            Pawn partner = Partner;
            FeedOtherUtility.DropCarriedSessionMeals(pawn);
            if (IsLeader && !cleaningUp)
            {
                cleaningUp = true;
                if (postMealSocialStarted)
                {
                    CompletePostMealSocial();
                }
                bool fullyFed = partner != null && BothPawnsReachedTarget();
                bool completedSession = condition == JobCondition.Succeeded && partner != null &&
                    (fullyFed || sessionTimedOut || BothPawnsCompletedSession());

                if (completedSession && fullyFed)
                {
                    FeedOtherConversationUtility.TryFinishingConversation(
                        conversationState, pawn, partner, FeedOtherConversationUtility.SharedMealActivity);
                    GainSharedFeedingOpinion(pawn, partner);
                }

                if (partnerStarted && FeedOtherUtility.IsPartnerJobFor(partner, pawn))
                {
                    partner.jobs.EndCurrentJob(
                        completedSession ? JobCondition.Succeeded : JobCondition.InterruptForced,
                        true,
                        true);
                }
            }
        }

        private bool BeginPostMealSocial()
        {
            Pawn partner = Partner;
            if (!IsLeader || !PostMealSocialUtility.CanStart(pawn, partner, false))
            {
                return false;
            }

            int now = Find.TickManager.TicksGame;
            postMealSocialRequested = true;
            postMealSocialStarted = true;
            postMealSocialStartTick = now;
            postMealSocialCompleted = false;
            FeedOtherUtility.DropCarriedSessionMeals(pawn);
            FeedOtherUtility.DropCarriedSessionMeals(partner);
            pawn.pather?.StopDead();

            JobDriver_FeedOtherBase partnerDriver = PartnerDriver;
            if (partnerDriver != null)
            {
                partnerDriver.postMealSocialRequested = true;
                partnerDriver.postMealSocialStarted = true;
                partnerDriver.postMealSocialStartTick = now;
                partnerDriver.postMealSocialCompleted = false;
                partner?.pather?.StopDead();
            }

            return true;
        }

        private void SynchronizePostMealStateFromPartner()
        {
            JobDriver_FeedOtherBase partnerDriver = PartnerDriver;
            if (postMealSocialRequested || partnerDriver == null ||
                !partnerDriver.postMealSocialRequested)
            {
                return;
            }

            postMealSocialRequested = true;
            postMealSocialStarted = partnerDriver.postMealSocialStarted;
            postMealSocialStartTick = partnerDriver.postMealSocialStartTick;
            postMealSocialCompleted = partnerDriver.postMealSocialCompleted;
        }

        private void CompletePostMealSocial()
        {
            if (postMealSocialCompleted)
            {
                return;
            }

            postMealSocialCompleted = true;
            Pawn partner = Partner;
            FeedOtherConversationUtility.TryFinishingConversation(
                postMealConversationState,
                pawn,
                partner,
                FeedOtherConversationUtility.PostSharedMealActivity);
            PostMealSocialUtility.TryGrantAfterMealChatMemory(
                pawn,
                partner,
                postMealSocialStartTick);

            JobDriver_FeedOtherBase partnerDriver = PartnerDriver;
            if (partnerDriver != null)
            {
                partnerDriver.postMealSocialCompleted = true;
            }
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

        private Toil MakeStartPartnerToil()
        {
            Toil toil = ToilMaker.MakeToil("StartFeedOtherPartner");
            toil.initAction = delegate
            {
                Pawn partner = Partner;
                bool manualOrder = job.playerForced;
                if (!(manualOrder
                    ? FeedOtherUtility.IsAvailablePartnerForManualOrder(partner)
                    : FeedOtherUtility.IsAvailablePartner(partner)))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Thing partnerMeal;
                if (!FeedOtherUtility.TryFindStoredMeal(partner, FoodAnchor, job.targetA.Thing, out partnerMeal))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Job partnerJob = JobMaker.MakeJob(
                    FeedOtherDefOf.RR_FeedOtherPartner,
                    partnerMeal,
                    pawn,
                    FoodAnchor);
                partnerJob.count = 1;
                partnerJob.playerForced = manualOrder;
                partner.jobs.StartJob(
                    partnerJob,
                    JobCondition.InterruptForced,
                    null,
                    true,
                    true,
                    null,
                    null,
                    false,
                    false);

                partnerStarted = FeedOtherUtility.IsPartnerJobFor(partner, pawn);
                if (!partnerStarted)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (FeedOtherUtility.ShouldRecordSessionCooldown(manualOrder))
                {
                    FeedOtherCooldownComponent.NotifyAutonomousSessionStarted(
                        pawn,
                        partner,
                        false);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        private bool SessionLinkBroken()
        {
            if (Partner == null)
            {
                return true;
            }

            if (IsLeader)
            {
                return partnerStarted && !FeedOtherUtility.IsPartnerJobFor(Partner, pawn);
            }

            return !FeedOtherUtility.IsLeaderJobFor(Partner, pawn);
        }

        private bool PartnerReadyForEatingRound()
        {
            if (Partner == null)
            {
                return false;
            }

            if (IsHoldingSessionMeal(Partner))
            {
                return true;
            }

            return PartnerCompletedOwnPartOfSession();
        }

        private bool PartnerFinishedEatingRound()
        {
            if (Partner == null)
            {
                return false;
            }

            if (PartnerCompletedOwnPartOfSession())
            {
                return true;
            }

            return PartnerDriver != null && PartnerDriver.mealsCompleted >= mealsCompleted;
        }

        private static bool IsHoldingSessionMeal(Pawn holder)
        {
            return FeedOtherUtility.IsFeedOtherJob(holder?.CurJobDef) &&
                FeedOtherUtility.IsCarryingSessionMeal(holder);
        }

        private void AssignDiningDecision(bool useTable, IntVec3 spot)
        {
            usingDiningTable = useTable && spot.IsValid;
            diningSpot = usingDiningTable ? spot : IntVec3.Invalid;
            diningDecisionMade = true;
        }

        private Toil MakeMoveToDiningOrPartnerToil()
        {
            Toil toil = ToilMaker.MakeToil("MoveToFeedOtherDiningSpot");
            toil.initAction = delegate
            {
                atDiningDestination = false;
                if (usingDiningTable && diningSpot.IsValid &&
                    pawn.CanReach(diningSpot, PathEndMode.OnCell, pawn.NormalMaxDanger()) &&
                    pawn.ReserveSittableOrSpot(diningSpot, job, false))
                {
                    pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, diningSpot);
                    pawn.pather.StartPath(diningSpot, PathEndMode.OnCell);
                }
                else
                {
                    usingDiningTable = false;
                    diningSpot = IntVec3.Invalid;
                    pawn.pather.StartPath(Partner, PathEndMode.Touch);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            return toil;
        }

        private bool BothPawnsReachedTarget()
        {
            return HasReachedTargetForSession() && PartnerReachedTargetForSession();
        }

        private bool BothPawnsCompletedSession()
        {
            return HasCompletedOwnPartOfSession() && PartnerCompletedOwnPartOfSession();
        }

        public void NotifyParticipantReachedVeryFull(Pawn participant)
        {
            if (participant == pawn)
            {
                feedingTargetReached = true;
            }
        }

        private bool HasReachedTargetForSession()
        {
            if (!feedingTargetReached &&
                (FeedOtherUtility.HasReachedFeedingTarget(pawn) ||
                 FeedOtherUtility.ShouldCompleteAfterRepeatedTopUps(pawn, topUpRoundsCompleted)))
            {
                feedingTargetReached = true;
            }

            return feedingTargetReached;
        }

        private bool HasCompletedOwnPartOfSession()
        {
            return HasReachedTargetForSession() || cannotContinueFeeding;
        }

        private bool PartnerReachedTargetForSession()
        {
            return PartnerDriver != null
                ? PartnerDriver.HasReachedTargetForSession()
                : FeedOtherUtility.HasReachedFeedingTarget(Partner);
        }

        private bool PartnerCompletedOwnPartOfSession()
        {
            return PartnerDriver != null
                ? PartnerDriver.HasCompletedOwnPartOfSession()
                : FeedOtherUtility.HasReachedFeedingTarget(Partner);
        }

        private static void GainSharedFeedingOpinion(Pawn first, Pawn second)
        {
            if (!FeedOtherMod.Settings.socialOpinionMemoryEnabled ||
                FeedOtherDefOf.RR_SharedFeeding == null)
            {
                return;
            }

            first.needs?.mood?.thoughts?.memories?.TryGainMemory(FeedOtherDefOf.RR_SharedFeeding, second, null);
            second.needs?.mood?.thoughts?.memories?.TryGainMemory(FeedOtherDefOf.RR_SharedFeeding, first, null);
        }
    }

    public class JobDriver_FeedOther : JobDriver_FeedOtherBase
    {
        protected override bool IsLeader => true;
    }

    public class JobDriver_FeedOtherPartner : JobDriver_FeedOtherBase
    {
        protected override bool IsLeader => false;
    }
}
