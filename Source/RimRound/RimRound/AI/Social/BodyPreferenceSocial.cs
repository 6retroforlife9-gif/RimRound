using System;
using System.Collections.Generic;
using RimRound.Comps;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    public static class BodyPreferenceSocial
    {
        public static JobDef DiscussionJob => DefDatabase<JobDef>.GetNamed("RR_BodyPreferenceDiscussion");
        public static JobDef ListenJob => DefDatabase<JobDef>.GetNamed("RR_ListenBodyDiscussion");
        public static InteractionDef Interaction => DefDatabase<InteractionDef>.GetNamed("RR_BodyPreferenceDiscussion");
        public static int Day => GenTicks.TicksAbs / BodySocialProgression.DayTicks;
        public static bool OrdinaryColonist(Pawn p) => BodyModelEligibility.IsEligible(p) &&
            p.Faction == Faction.OfPlayer && p.IsColonist && !p.IsPrisoner && !p.IsSlave && !p.IsQuestLodger();
        public static bool OrdinarySpeaker(Pawn p) => PawnHelperEligibility.OrdinaryColonist(p) &&
            PawnHelperEligibility.HasSocialIdentity(p) && p.interactions != null;
        public static bool IdleForOptionalDiscussion(Pawn p) => p?.CurJob == null ||
            p.CurJobDef == JobDefOf.Wait_Wander || p.CurJobDef == JobDefOf.GotoWander || p.CurJobDef == JobDefOf.Wait_MaintainPosture ||
            BedsideBodyDiscussion.InBed(p);

        internal const int MaximumApproachTicks = 1800;
        internal const int MaximumCooldownWaitTicks = 180;

        // Keep the physical position gate distinct from the native cooldown /
        // interaction gate. Bedside visitors share these exact predicates.
        internal static bool HasLocalDiscussionPosition(Pawn speaker, Pawn recipient) =>
            speaker != null && recipient != null && speaker.Spawned && recipient.Spawned && speaker.Map == recipient.Map &&
            speaker.CanReachImmediate(recipient, PathEndMode.Touch) &&
            SocialInteractionUtility.IsGoodPositionForInteraction(speaker, recipient);

        internal static bool NativeDiscussionInteractionReady(Pawn speaker, Pawn recipient) =>
            speaker?.interactions != null && recipient?.interactions != null &&
            speaker.interactions.CanInteractNowWith(recipient, Interaction) &&
            !recipient.interactions.InteractedTooRecentlyToInteract();

        // Deterministic native-path candidate search: no random state is
        // consumed, and the reserved cell itself must pass Touch and LOS.
        internal static bool TryReserveBestInteractionCell(Pawn actor, Pawn target, Job job, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (actor == null || target == null || job == null || !actor.Spawned || !target.Spawned ||
                actor.Map != target.Map || actor.Map == null) return false;

            Map map = actor.Map;
            IntVec3 targetCell = target.Position;
            float bestDistance = float.MaxValue;
            IntVec3 best = IntVec3.Invalid;
            for (int i = 0; i < GenAdj.AdjacentCellsAndInside.Length; i++)
            {
                IntVec3 candidate = targetCell + GenAdj.AdjacentCellsAndInside[i];
                if (!candidate.InBounds(map) || candidate == targetCell || !candidate.Standable(map) ||
                    !candidate.Walkable(map) || candidate.IsForbidden(actor) ||
                    !SocialInteractionUtility.IsGoodPositionForInteraction(candidate, targetCell, map) ||
                    !ReachabilityImmediate.CanReachImmediate(candidate, target, map, PathEndMode.Touch, actor) ||
                    !actor.CanReserveAndReach(candidate, PathEndMode.OnCell, Danger.Some, 1, -1, null, false)) continue;

                float distance = actor.Position.DistanceToSquared(candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            if (!best.IsValid || !actor.Reserve(best, job, 1, -1, null, false)) return false;
            cell = best;
            return true;
        }

        internal static Toil MakeReservedInteractionApproachToil(TargetIndex target, Action<int> tickIntervalAction)
        {
            Toil toil = Toils_Goto.GotoCell(target, PathEndMode.OnCell);
            if (tickIntervalAction != null) toil.tickIntervalAction += tickIntervalAction;
            return toil;
        }

        // Walking pairs and bedside sessions share the same eligibility and
        // final commit. The visitor supplies reachability for a bed occupant.
        public static bool Eligible(Pawn speaker, Pawn recipient, bool checkClocks, bool requireLocal, out string reason)
        {
            reason = "RR_DiscussionUnavailable".Translate();
            if (speaker == recipient || !OrdinarySpeaker(speaker) || !OrdinaryColonist(recipient)) return false;
            var a = speaker.TryGetComp<ThingComp_PawnAttitude>();
            var b = recipient.TryGetComp<ThingComp_PawnAttitude>();
            if (a == null || b?.HasPreference != true || !a.DiscussionClock.IsUsable || !b.DiscussionClock.IsUsable) return false;
            if (!speaker.Spawned || !recipient.Spawned || speaker.Map != recipient.Map)
            { reason = "RR_DiscussionSameMap".Translate(); return false; }
            if (!Ready(speaker) || !Ready(recipient) ||
                speaker.WorkTagIsDisabled(WorkTags.Social) || speaker.skills?.GetSkill(SkillDefOf.Social).TotallyDisabled != false ||
                !SocialInteractionUtility.CanInitiateInteraction(speaker, Interaction) ||
                !SocialInteractionUtility.CanReceiveInteraction(recipient, Interaction))
            { reason = "RR_DiscussionNotReady".Translate(); return false; }
            if (checkClocks && (!a.DiscussionClock.CanParticipate(Find.TickManager.TicksGame, Day) ||
                !b.DiscussionClock.CanParticipate(Find.TickManager.TicksGame, Day)))
            { reason = "RR_DiscussionCooldown".Translate(); return false; }
            if (!BedsideBodyDiscussion.CanApproach(speaker, recipient))
            { reason = "RR_DiscussionUnreachable".Translate(); return false; }
            if (requireLocal && (!HasLocalDiscussionPosition(speaker, recipient) ||
                !NativeDiscussionInteractionReady(speaker, recipient)))
            { reason = "RR_DiscussionNotLocal".Translate(); return false; }
            reason = null;
            return true;
        }
        // Receiving persuasion requires communication, not the ability to do Social work.
        // Eligible applies the work/skill requirement to the speaker separately.
        private static bool Ready(Pawn p) => !p.Drafted && !p.InMentalState && !p.Suspended && !p.Deathresting && p.Awake() && !p.IsBurning() &&
            Capacity(p, PawnCapacityDefOf.Talking) > 0f && Capacity(p, PawnCapacityDefOf.Hearing) > 0f &&
            Capacity(p, PawnCapacityDefOf.Consciousness) > 0f && p.interactions != null &&
            (BedsideBodyDiscussion.InBed(p) || (!p.Downed && Capacity(p, PawnCapacityDefOf.Moving) > 0f));
        private static float Capacity(Pawn p, PawnCapacityDef capacity) => p.health.capacities.GetLevel(capacity);

        public static bool TryStartIntent(Pawn speaker, bool ordered, out string reason)
        {
            var attitude = speaker?.TryGetComp<ThingComp_PawnAttitude>();
            Pawn recipient = attitude?.DiscussionRecipient;
            if (!Eligible(speaker, recipient, true, false, out reason)) return false;
            if (!attitude.MatchesDiscussionOrder(recipient, attitude.DiscussionOrderRevision))
            { reason = "RR_DiscussionUnavailable".Translate(); return false; }
            if (!ordered && (!IdleForOptionalDiscussion(speaker) || !IdleForOptionalDiscussion(recipient)))
            { reason = "RR_DiscussionBusy".Translate(); return false; }
            if (BedsideBodyDiscussion.Applies(speaker, recipient))
                return BedsideBodyDiscussion.Begin(speaker, recipient, ordered, out reason);
            if (recipient.CurJob?.playerForced == true || recipient.jobs.curDriver is JobDriver_BodyPreferenceDiscussion ||
                recipient.jobs.curDriver is JobDriver_ListenBodyDiscussion ||
                (recipient.CurJob != null && !recipient.CurJob.def.casualInterruptible) ||
                !speaker.CanReserve(recipient) || !speaker.CanReserve(speaker))
            { reason = "RR_DiscussionBusy".Translate(); return false; }
            Job job = JobMaker.MakeJob(DiscussionJob, recipient);
            job.count = attitude.DiscussionOrderRevision;
            job.playerForced = ordered;
            if (ordered) speaker.jobs.TryTakeOrderedJob(job);
            else speaker.jobs.StartJob(job, JobCondition.InterruptOptional, resumeCurJobAfterwards: true);
            if (speaker.CurJob == job) return true;
            reason = "RR_DiscussionBusy".Translate();
            return false;
        }

        public static bool CommitLocalDiscussion(Pawn speaker, Pawn recipient, float lower, float upper,
            int observedTicks, string eventId, float sample, out BodyDiscussionResult result)
        {
            result = default(BodyDiscussionResult);
            string reason;
            int now = Find.TickManager.TicksGame;
            if (observedTicks < BodySocialProgression.DiscussionTicks || now > int.MaxValue - BodySocialProgression.CooldownTicks ||
                !Eligible(speaker, recipient, true, true, out reason)) return false;
            var a = speaker.TryGetComp<ThingComp_PawnAttitude>();
            var b = recipient.TryGetComp<ThingComp_PawnAttitude>();
            if (!a.DiscussionClock.CanRecord(now, Day, eventId) || !b.DiscussionClock.CanRecord(now, Day, eventId)) return false;
            var actorBody = speaker.TryGetComp<FullnessAndDietStats_ThingComp>();
            var targetBody = recipient.TryGetComp<FullnessAndDietStats_ThingComp>();
            result = BodySocialProgression.Discuss(b.PreferredLowerRatio, b.PreferredUpperRatio, b.PreferenceConviction,
                lower, upper, speaker.skills.GetSkill(SkillDefOf.Social).Level,
                Math.Min(Capacity(speaker, PawnCapacityDefOf.Talking), Capacity(recipient, PawnCapacityDefOf.Talking)),
                Math.Min(Capacity(speaker, PawnCapacityDefOf.Hearing), Capacity(recipient, PawnCapacityDefOf.Hearing)),
                Math.Min(Capacity(speaker, PawnCapacityDefOf.Consciousness), Capacity(recipient, PawnCapacityDefOf.Consciousness)),
                recipient.relations.OpinionOf(speaker), b.DiscussionClock.Irritation,
                actorBody?.ActiveBodyPerkEffects.BodyPersuasionBonus ?? 0f, sample);
            if (!result.Completed || !b.TrySetPreference(result.Lower, result.Upper, result.Conviction)) return false;
            // All validation precedes this single synchronous commit. No job,
            // thought, XML outcome or caller owns another preference writer.
            a.DiscussionClock.Record(now, Day, eventId, result.Outcome, true);
            b.DiscussionClock.Record(now, Day, eventId, result.Outcome, false);
            b.DiscussionClock.Irritation = result.Irritation;
            actorBody?.NotifyBodyDiscussionCompleted(now);
            targetBody?.NotifyBodyDiscussionCompleted(now);
            a.NotifyDiscussionTargetReached();
            Messages.Message(("RR_DiscussionOutcome_" + result.Outcome).Translate(speaker.LabelShortCap, recipient.LabelShortCap),
                new LookTargets(speaker, recipient), MessageTypeDefOf.NeutralEvent, historical: false);
            return true;
        }
    }

    public sealed class JobDriver_BodyPreferenceDiscussion : JobDriver
    {
        private float targetLower, targetUpper;
        private IntVec3 interactionCell = IntVec3.Invalid;
        private IntVec3 recipientAnchor = IntVec3.Invalid;
        private int observedTicks, approachTicks, cooldownWaitTicks;
        private bool captured, committed, attemptingCommit, listenerStarted, interactionCellChosen, recipientAnchorSet;
        private Pawn Recipient => job.targetA.Pawn;
        public bool ListeningSession(Pawn listener, int id) => listener == Recipient && job.loadID == id && !committed;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref targetLower, "rrDiscussionLower", 0f);
            Scribe_Values.Look(ref targetUpper, "rrDiscussionUpper", 0f);
            Scribe_Values.Look(ref interactionCell, "rrDiscussionInteractionCell", IntVec3.Invalid);
            Scribe_Values.Look(ref recipientAnchor, "rrDiscussionRecipientAnchor", IntVec3.Invalid);
            Scribe_Values.Look(ref observedTicks, "rrDiscussionObservedTicks", 0);
            Scribe_Values.Look(ref approachTicks, "rrDiscussionApproachTicks", 0);
            Scribe_Values.Look(ref cooldownWaitTicks, "rrDiscussionCooldownWaitTicks", 0);
            Scribe_Values.Look(ref captured, "rrDiscussionCaptured", false);
            Scribe_Values.Look(ref committed, "rrDiscussionCommitted", false);
            Scribe_Values.Look(ref listenerStarted, "rrDiscussionListenerStarted", false);
            Scribe_Values.Look(ref interactionCellChosen, "rrDiscussionInteractionCellChosen", false);
            Scribe_Values.Look(ref recipientAnchorSet, "rrDiscussionRecipientAnchorSet", false);
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            string reason;
            return BodyPreferenceSocial.Eligible(pawn, Recipient, true, false, out reason) &&
                pawn.Reserve(Recipient, job, 1, -1, null, errorOnFailed) && pawn.Reserve(pawn, job, 1, -1, null, errorOnFailed);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => {
                string reason;
                return committed ? false : !BodyPreferenceSocial.Eligible(pawn, Recipient, true, false, out reason) ||
                    pawn.TryGetComp<ThingComp_PawnAttitude>()?.MatchesDiscussionOrder(Recipient, job.count) != true ||
                    (listenerStarted && !ListenerStillMatches());
            });
            AddFinishAction(condition => {
                if (Recipient?.CurJobDef == BodyPreferenceSocial.ListenJob && Recipient.CurJob.targetA.Pawn == pawn && Recipient.CurJob.count == job.loadID)
                    Recipient.jobs.EndCurrentJob(JobCondition.InterruptForced);
                (pawn.Map ?? Recipient?.Map)?.reservationManager.ReleaseClaimedBy(pawn, job);
                pawn.TryGetComp<ThingComp_PawnAttitude>()?.FinishBodyDiscussionOrder(Recipient, job.count);
            });
            Toil capture = ToilMaker.MakeToil("CaptureBodyDiscussionRange");
            capture.initAction = () => {
                if (captured) return;
                var attitude = pawn.TryGetComp<ThingComp_PawnAttitude>();
                targetLower = attitude.DiscussionTargetLower; targetUpper = attitude.DiscussionTargetUpper; captured = true;
            };
            capture.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return capture;

            Toil startListenerAndChoosePosition = ToilMaker.MakeToil("StartBodyDiscussionListener");
            startListenerAndChoosePosition.initAction = () =>
            {
                if (!EnsureListenerStarted()) EndJobWith(JobCondition.Incompletable);
            };
            startListenerAndChoosePosition.tickIntervalAction = delta =>
            {
                if (delta <= 0 || delta > BodySocialProgression.MaximumInterval || !AdvanceApproachTicks(delta))
                { EndJobWith(JobCondition.Incompletable); return; }
                var listener = Recipient?.jobs.curDriver as JobDriver_ListenBodyDiscussion;
                if (listener == null || !listener.ListeningSession(pawn, job.loadID))
                { EndJobWith(JobCondition.Incompletable); return; }
                if (!listener.ReadyForSession(pawn, job.loadID)) return;

                if (!recipientAnchorSet)
                {
                    recipientAnchor = Recipient.Position;
                    recipientAnchorSet = true;
                }
                if (Recipient.Position != recipientAnchor)
                { EndJobWith(JobCondition.Incompletable); return; }

                if (!interactionCellChosen)
                {
                    if (!BodyPreferenceSocial.TryReserveBestInteractionCell(pawn, Recipient, job, out interactionCell))
                    { EndJobWith(JobCondition.Incompletable); return; }
                    interactionCellChosen = true;
                    job.targetB = interactionCell;
                }
                else if (!interactionCell.IsValid || !pawn.Reserve(interactionCell, job, 1, -1, null, false))
                { EndJobWith(JobCondition.Incompletable); return; }
                ReadyForNextToil();
            };
            startListenerAndChoosePosition.socialMode = RandomSocialMode.Off;
            startListenerAndChoosePosition.defaultCompleteMode = ToilCompleteMode.Never;
            yield return startListenerAndChoosePosition;

            yield return BodyPreferenceSocial.MakeReservedInteractionApproachToil(TargetIndex.B, delta =>
            {
                if (delta <= 0 || delta > BodySocialProgression.MaximumInterval || !AdvanceApproachTicks(delta) ||
                    Recipient.Position != recipientAnchor || !pawn.Reserve(interactionCell, job, 1, -1, null, false))
                { EndJobWith(JobCondition.Incompletable); return; }
                if (approachTicks > BodyPreferenceSocial.MaximumApproachTicks) EndJobWith(JobCondition.Incompletable);
            });

            Toil waitForNativeCooldown = ToilMaker.MakeToil("WaitForBodyDiscussionCooldown");
            waitForNativeCooldown.initAction = () =>
            {
                pawn.pather.StopDead();
                if (!AtReservedInteractionPosition()) { EndJobWith(JobCondition.Incompletable); return; }
                if (!NativeCooldownPending())
                {
                    string reason;
                    if (!BodyPreferenceSocial.Eligible(pawn, Recipient, true, true, out reason))
                    { EndJobWith(JobCondition.Incompletable); return; }
                    ReadyForNextToil();
                }
            };
            waitForNativeCooldown.tickIntervalAction = delta =>
            {
                if (delta <= 0 || delta > BodySocialProgression.MaximumInterval || !AtReservedInteractionPosition())
                { EndJobWith(JobCondition.Incompletable); return; }
                if (NativeCooldownPending())
                {
                    if (!AdvanceCooldownWaitTicks(delta)) EndJobWith(JobCondition.Incompletable);
                    return;
                }
                string reason;
                if (!BodyPreferenceSocial.Eligible(pawn, Recipient, true, true, out reason))
                { EndJobWith(JobCondition.Incompletable); return; }
                ReadyForNextToil();
            };
            waitForNativeCooldown.socialMode = RandomSocialMode.Off;
            waitForNativeCooldown.defaultCompleteMode = ToilCompleteMode.Never;
            yield return waitForNativeCooldown;

            Toil talk = ToilMaker.MakeToil("DiscussBodyPreference");
            talk.initAction = () => {
                pawn.pather.StopDead();
                if (!AtReservedInteractionPosition() || !ListenerStillMatches()) EndJobWith(JobCondition.Incompletable);
            };
            talk.tickIntervalAction = delta => {
                string reason;
                if (delta <= 0 || delta > BodySocialProgression.MaximumInterval ||
                    !AtReservedInteractionPosition() || !ListenerStillMatches() ||
                    !BodyPreferenceSocial.Eligible(pawn, Recipient, true, true, out reason))
                { EndJobWith(JobCondition.Incompletable); return; }
                observedTicks = Math.Min(BodySocialProgression.DiscussionTicks, observedTicks + delta);
                if (observedTicks >= BodySocialProgression.DiscussionTicks) ReadyForNextToil();
            };
            talk.socialMode = RandomSocialMode.Off;
            talk.defaultCompleteMode = ToilCompleteMode.Never;
            yield return talk;
            Toil finish = ToilMaker.MakeToil("CompleteBodyDiscussion");
            finish.initAction = () => {
                if (committed) return;
                attemptingCommit = true;
                try { pawn.interactions.TryInteractWith(Recipient, BodyPreferenceSocial.Interaction); }
                finally { attemptingCommit = false; }
                if (!committed) EndJobWith(JobCondition.Incompletable);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
        internal void CommitFromInteraction(Pawn recipient)
        {
            if (!attemptingCommit || committed || recipient != Recipient) return;
            BodyDiscussionResult result;
            committed = BodyPreferenceSocial.CommitLocalDiscussion(pawn, recipient, targetLower, targetUpper, observedTicks,
                pawn.ThingID + ":" + job.loadID, Rand.Value, out result);
        }

        private bool EnsureListenerStarted()
        {
            if (ListenerStillMatches())
            {
                listenerStarted = true;
                return true;
            }
            if (listenerStarted || Recipient?.jobs == null || Recipient.CurJob?.playerForced == true ||
                (Recipient.CurJob != null && !Recipient.CurJob.def.casualInterruptible)) return false;
            Job listen = JobMaker.MakeJob(BodyPreferenceSocial.ListenJob, pawn);
            listen.count = job.loadID;
            Recipient.jobs.StartJob(listen, JobCondition.InterruptOptional, resumeCurJobAfterwards: true);
            listenerStarted = ListenerStillMatches();
            return listenerStarted;
        }

        private bool ListenerStillMatches() => Recipient?.CurJobDef == BodyPreferenceSocial.ListenJob &&
            Recipient.CurJob.targetA.Pawn == pawn && Recipient.CurJob.count == job.loadID;

        private bool AtReservedInteractionPosition() => interactionCellChosen && interactionCell.IsValid &&
            recipientAnchorSet && Recipient?.Position == recipientAnchor && pawn.Position == interactionCell &&
            ListenerStillMatches() && BodyPreferenceSocial.HasLocalDiscussionPosition(pawn, Recipient) &&
            pawn.Reserve(Recipient, job, 1, -1, null, false) && pawn.Reserve(pawn, job, 1, -1, null, false) &&
            pawn.Reserve(interactionCell, job, 1, -1, null, false);

        private bool NativeCooldownPending() => pawn.interactions?.InteractedTooRecentlyToInteract() == true ||
            Recipient?.interactions?.InteractedTooRecentlyToInteract() == true;

        private bool AdvanceApproachTicks(int delta)
        {
            if (approachTicks < 0 || approachTicks > BodyPreferenceSocial.MaximumApproachTicks - delta) return false;
            approachTicks += delta;
            return approachTicks <= BodyPreferenceSocial.MaximumApproachTicks;
        }

        private bool AdvanceCooldownWaitTicks(int delta)
        {
            if (cooldownWaitTicks < 0 || cooldownWaitTicks > BodyPreferenceSocial.MaximumCooldownWaitTicks - delta) return false;
            cooldownWaitTicks += delta;
            return true;
        }
    }

    public sealed class JobDriver_ListenBodyDiscussion : JobDriver
    {
        private bool listenerReady;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref listenerReady, "rrDiscussionListenerReady", false);
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true; // Initiator reserves both participants.
        public bool ListeningSession(Pawn speaker, int id) => job.targetA.Pawn == speaker && job.count == id;
        public bool ReadyForSession(Pawn speaker, int id) => listenerReady &&
            ListeningSession(speaker, id);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !(job.targetA.Pawn?.jobs.curDriver is JobDriver_BodyPreferenceDiscussion driver) || !driver.ListeningSession(pawn, job.count));
            Toil listen = ToilMaker.MakeToil("ListenToBodyDiscussion");
            listen.initAction = () => { pawn.pather.StopDead(); listenerReady = true; };
            listen.socialMode = RandomSocialMode.Off;
            listen.defaultCompleteMode = ToilCompleteMode.Never;
            yield return listen;
        }
    }

    public sealed class InteractionWorker_BodyPreferenceDiscussion : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient) => 0f;
        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = letterLabel = null; letterDef = null; lookTargets = null;
            (initiator.jobs.curDriver as JobDriver_BodyPreferenceDiscussion)?.CommitFromInteraction(recipient);
            BedsideBodyDiscussion.CommitFromInteraction(initiator, recipient);
        }
    }
}
