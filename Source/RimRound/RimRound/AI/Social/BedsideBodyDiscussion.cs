using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.Ideology;
using RimRound.Simulation;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    // The bed occupants retain their native rest jobs. Only the mobile visitor
    // receives a movement job; both arrangements use the ordinary interaction
    // worker and the same preference/cooldown transaction as a walking pair.
    public static class BedsideBodyDiscussion
    {
        internal static JobDef VisitJob => DefDatabase<JobDef>.GetNamed("RR_VisitBodyDiscussion");
        internal static JobDef SessionJob => DefDatabase<JobDef>.GetNamed("RR_BedsideBodyDiscussionSession");
        internal static MapComponent_BedsideBodyDiscussions Sessions(Pawn pawn) => pawn?.Map?.GetComponent<MapComponent_BedsideBodyDiscussions>();
        public static bool HasSession(Pawn pawn) => Sessions(pawn)?.Contains(pawn) == true;
        internal static bool InBed(Pawn pawn) => BedsideRoleAbilities.IsReadyBedOccupant(pawn);
        internal static bool Applies(Pawn a, Pawn b) => InBed(a) || InBed(b);
        internal static bool CanApproach(Pawn a, Pawn b)
        {
            bool aBed = InBed(a), bBed = InBed(b);
            if (aBed && bBed) return BedsideRoleAbilities.HasLocalContact(a, b);
            return (aBed ? b.CanReach(a, PathEndMode.Touch, Danger.Some) : a.CanReach(b, PathEndMode.Touch, Danger.Some));
        }
        internal static bool Begin(Pawn speaker, Pawn recipient, bool ordered, out string reason)
        {
            reason = "RR_DiscussionBusy".Translate();
            if (!BodyPreferenceSocial.Eligible(speaker, recipient, true, false, out reason)) return false;
            reason = "RR_DiscussionBusy".Translate();
            if (HasSession(speaker) || HasSession(recipient) || BedsideRoleAbilities.HasSession(speaker) || BedsideRoleAbilities.HasSession(recipient)) return false;
            Pawn visitor = !InBed(speaker) ? speaker : !InBed(recipient) ? recipient : null;
            if (visitor != null && !(visitor == speaker && ordered) &&
                (visitor.CurJob?.playerForced == true || (visitor.CurJob != null && !visitor.CurJob.def.casualInterruptible))) return false;
            if (new[] { speaker, recipient }.Any(p => InBed(p) && !BedsideRoleAbilities.BedReservationsPermitSession(p.CurrentBed()))) return false;
            Pawn claimant = visitor ?? speaker;
            if (!claimant.CanReserve(speaker) || !claimant.CanReserve(recipient)) return false;
            return Sessions(speaker).Begin(speaker, recipient, visitor, ordered);
        }
        internal static void Cancel(Pawn speaker) => Sessions(speaker)?.Cancel(speaker);
        internal static void CommitFromInteraction(Pawn speaker, Pawn recipient) => Sessions(speaker)?.CommitFromInteraction(speaker, recipient);
    }

    public sealed class BedsideBodyDiscussionSession : IExposable
    {
        public Pawn Speaker, Recipient, Visitor;
        public Building_Bed SpeakerBed, RecipientBed;
        public IntVec3 SpeakerPosition, RecipientPosition;
        public Job ReservationJob;
        public int SpeakerRestJobId, RecipientRestJobId, VisitorJobId, Revision, CreatedTick, LastObservedTick = -1, ObservedTicks;
        public float TargetLower, TargetUpper;
        public bool Attempted, Completed;
        [Unsaved(false)] internal bool Attempting;
        public int Id => ReservationJob?.loadID ?? -1;
        internal Pawn Claimant => Visitor ?? Speaker;
        internal bool VisitorMatches => Visitor == null || VisitorJobMatches(Visitor.CurJob);
        private bool VisitorJobMatches(Job job) => Visitor != null && job?.def == BedsideBodyDiscussion.VisitJob &&
            job.loadID == VisitorJobId && job.count == Id &&
            job.targetA.Pawn == (Visitor == Speaker ? Recipient : Speaker);

        public void ExposeData()
        {
            Scribe_References.Look(ref Speaker, "speaker"); Scribe_References.Look(ref Recipient, "recipient");
            Scribe_References.Look(ref Visitor, "visitor");
            Scribe_References.Look(ref SpeakerBed, "speakerBed"); Scribe_References.Look(ref RecipientBed, "recipientBed");
            Scribe_Values.Look(ref SpeakerPosition, "speakerPosition"); Scribe_Values.Look(ref RecipientPosition, "recipientPosition");
            Scribe_Deep.Look(ref ReservationJob, "reservationJob");
            Scribe_Values.Look(ref SpeakerRestJobId, "speakerRestJobId"); Scribe_Values.Look(ref RecipientRestJobId, "recipientRestJobId");
            Scribe_Values.Look(ref VisitorJobId, "visitorJobId"); Scribe_Values.Look(ref Revision, "revision");
            Scribe_Values.Look(ref CreatedTick, "createdTick"); Scribe_Values.Look(ref LastObservedTick, "lastObservedTick", -1);
            Scribe_Values.Look(ref ObservedTicks, "observedTicks");
            Scribe_Values.Look(ref TargetLower, "targetLower"); Scribe_Values.Look(ref TargetUpper, "targetUpper");
            Scribe_Values.Look(ref Attempted, "attempted"); Scribe_Values.Look(ref Completed, "completed");
        }
        private bool BedUnchanged(Pawn p, Building_Bed bed, IntVec3 position, int restJobId) => p == Visitor ||
            (BedsideBodyDiscussion.InBed(p) && p.CurrentBed() == bed && p.Position == position &&
            p.CurJob?.loadID == restJobId && BedsideRoleAbilities.BedReservationsPermitSession(bed));
        internal bool Valid(Map map, bool local, bool reservations = true, Job startingVisitorJob = null)
        {
            int now = Find.TickManager.TicksGame;
            return !Attempted && Speaker != null && Recipient != null && Speaker.Map == map && Recipient.Map == map &&
                now >= CreatedTick && now - CreatedTick <= BedsideRoleAbilities.MaximumSessionTicks &&
                Speaker.TryGetComp<ThingComp_PawnAttitude>()?.MatchesDiscussionOrder(Recipient, Revision) == true &&
                BodyPreferenceSocial.Eligible(Speaker, Recipient, true, local, out _) &&
                BedUnchanged(Speaker, SpeakerBed, SpeakerPosition, SpeakerRestJobId) &&
                BedUnchanged(Recipient, RecipientBed, RecipientPosition, RecipientRestJobId) &&
                (startingVisitorJob == null ? VisitorMatches : VisitorJobMatches(startingVisitorJob)) &&
                (!reservations || HasReservations(map));
        }
        internal bool HasReservations(Map map) => Claimant != null && ReservationJob != null &&
            map.reservationManager.ReservedBy(Speaker, Claimant, ReservationJob) &&
            map.reservationManager.ReservedBy(Recipient, Claimant, ReservationJob) &&
            map.reservationManager.OnlyReservationsForJobDef(Speaker, BedsideBodyDiscussion.SessionJob, true) &&
            map.reservationManager.OnlyReservationsForJobDef(Recipient, BedsideBodyDiscussion.SessionJob, true) &&
            !map.physicalInteractionReservationManager.IsReserved(Speaker) && !map.physicalInteractionReservationManager.IsReserved(Recipient);
        internal bool Reserve(Map map) => Claimant != null && ReservationJob != null &&
            map.reservationManager.Reserve(Claimant, ReservationJob, Speaker, errorOnFailed: false) &&
            map.reservationManager.Reserve(Claimant, ReservationJob, Recipient, errorOnFailed: false);
        internal void CommitFromInteraction(Pawn recipient)
        {
            if (!Attempting || Completed || recipient != Recipient || ObservedTicks != BodySocialProgression.DiscussionTicks) return;
            Completed = BodyPreferenceSocial.CommitLocalDiscussion(Speaker, Recipient, TargetLower, TargetUpper,
                ObservedTicks, Speaker.ThingID + ":bedside:" + Id, Rand.Value, out _);
        }
    }

    public sealed class MapComponent_BedsideBodyDiscussions : MapComponent
    {
        private List<BedsideBodyDiscussionSession> sessions = new List<BedsideBodyDiscussionSession>();
        public MapComponent_BedsideBodyDiscussions(Map map) : base(map) { }
        public IReadOnlyList<BedsideBodyDiscussionSession> SessionsForReading => sessions;
        public bool Contains(Pawn pawn) => pawn != null && sessions.Any(s => s != null && (s.Speaker == pawn || s.Recipient == pawn));
        internal BedsideBodyDiscussionSession Find(int id) => sessions.FirstOrDefault(s => s != null && s.Id == id);
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Collections.Look(ref sessions, "rrBedsideBodyDiscussions", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && sessions == null) sessions = new List<BedsideBodyDiscussionSession>();
        }
        internal bool Begin(Pawn speaker, Pawn recipient, Pawn visitor, bool ordered)
        {
            var attitude = speaker.TryGetComp<ThingComp_PawnAttitude>();
            var s = new BedsideBodyDiscussionSession {
                Speaker = speaker, Recipient = recipient, Visitor = visitor,
                SpeakerBed = speaker.CurrentBed(), RecipientBed = recipient.CurrentBed(),
                SpeakerPosition = speaker.Position, RecipientPosition = recipient.Position,
                SpeakerRestJobId = speaker.CurJob?.loadID ?? -1, RecipientRestJobId = recipient.CurJob?.loadID ?? -1,
                CreatedTick = Verse.Find.TickManager.TicksGame, Revision = attitude.DiscussionOrderRevision,
                TargetLower = attitude.DiscussionTargetLower, TargetUpper = attitude.DiscussionTargetUpper,
                ReservationJob = JobMaker.MakeJob(BedsideBodyDiscussion.SessionJob, speaker, recipient)
            };
            sessions.Add(s);
            if (visitor == null)
            {
                if (s.Reserve(map)) return true;
            }
            else
            {
                var visit = JobMaker.MakeJob(BedsideBodyDiscussion.VisitJob, visitor == speaker ? recipient : speaker);
                visit.count = s.Id; s.VisitorJobId = visit.loadID;
                // Only the explicit speaker order can interrupt that speaker's
                // job. An invited listener never overrides native reservations.
                if (ordered && visitor == speaker) visitor.jobs.TryTakeOrderedJob(visit);
                else visitor.jobs.StartJob(visit, JobCondition.InterruptOptional, resumeCurJobAfterwards: true, preToilReservationsCanFail: true);
                if (s.VisitorMatches && s.HasReservations(map)) return true;
            }
            Remove(s, false); return false;
        }
        internal bool ReserveVisitor(Pawn visitor, Job visit)
        {
            var s = visit == null ? null : Find(visit.count);
            // TryTakeOrderedJob asks for reservations before replacing CurJob.
            // Accept only this session's exact pending job here; every running
            // tick still requires the visitor's actual current job to match.
            return s != null && s.Visitor == visitor && s.Valid(map, false, false, visit) && s.Reserve(map);
        }
        internal void VisitorFinished(Pawn visitor, int id)
        {
            var s = Find(id); if (s != null && s.Visitor == visitor) Remove(s, false);
        }
        internal void Cancel(Pawn speaker)
        {
            foreach (var s in sessions.Where(s => s?.Speaker == speaker).ToArray()) Remove(s, true);
        }
        internal void CommitFromInteraction(Pawn speaker, Pawn recipient)
        {
            sessions.FirstOrDefault(s => s?.Speaker == speaker && s.Recipient == recipient && s.Attempting)?.CommitFromInteraction(recipient);
        }
        private void Remove(BedsideBodyDiscussionSession s, bool endVisitor)
        {
            bool matches = s.Visitor != null && s.VisitorMatches;
            sessions.Remove(s);
            if (s.Claimant != null && s.ReservationJob != null) map.reservationManager.ReleaseClaimedBy(s.Claimant, s.ReservationJob);
            s.Speaker?.TryGetComp<ThingComp_PawnAttitude>()?.FinishBodyDiscussionOrder(s.Recipient, s.Revision);
            if (endVisitor && matches) s.Visitor.jobs.EndCurrentJob(s.Completed ? JobCondition.Succeeded : JobCondition.Incompletable);
        }
        public override void MapComponentTick()
        {
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                var s = sessions[i];
                if (s == null) { sessions.RemoveAt(i); continue; }
                if (!s.Valid(map, false)) { Remove(s, true); continue; }
                if (!BedsideRoleAbilities.HasLocalContact(s.Speaker, s.Recipient))
                {
                    if (s.ObservedTicks > 0) Remove(s, true);
                    continue;
                }
                // A recent unrelated native interaction can still be cooling
                // down when the visitor arrives. No discussion time is earned
                // while waiting; an already-started discussion still fails on
                // any loss of its complete local eligibility.
                if (s.ObservedTicks == 0 &&
                    (s.Speaker.interactions.InteractedTooRecentlyToInteract() || s.Recipient.interactions.InteractedTooRecentlyToInteract()))
                    continue;
                if (!s.Valid(map, true)) { Remove(s, true); continue; }
                int now = Verse.Find.TickManager.TicksGame;
                if (s.LastObservedTick == now) continue;
                if (s.LastObservedTick >= 0 && now != s.LastObservedTick + 1) { Remove(s, true); continue; }
                s.LastObservedTick = now;
                s.ObservedTicks++;
                if (s.ObservedTicks < BodySocialProgression.DiscussionTicks) continue;
                // Mark before native dispatch; a reload cannot replay an
                // uncertain partially executed interaction.
                s.Attempted = s.Attempting = true;
                try { s.Speaker.interactions.TryInteractWith(s.Recipient, BodyPreferenceSocial.Interaction); }
                finally { s.Attempting = false; Remove(s, true); }
            }
        }
        public override void MapRemoved()
        {
            foreach (var s in sessions.ToArray()) if (s != null) Remove(s, false);
            sessions.Clear(); base.MapRemoved();
        }
    }

    public sealed class JobDriver_VisitBodyDiscussion : JobDriver
    {
        private MapComponent_BedsideBodyDiscussions Owner => BedsideBodyDiscussion.Sessions(pawn);
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (Owner?.ReserveVisitor(pawn, job) != true ||
                !BodyPreferenceSocial.TryReserveBestInteractionCell(pawn, job.targetA.Pawn, job, out IntVec3 cell)) return false;
            job.targetB = cell;
            return true;
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Owner?.Find(job.count) == null);
            this.FailOn(() => !job.targetB.IsValid || !pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, job));
            AddFinishAction(condition => Owner?.VisitorFinished(pawn, job.count));
            yield return BodyPreferenceSocial.MakeReservedInteractionApproachToil(TargetIndex.B, null);
            Toil talk = ToilMaker.MakeToil("DiscussAtBedside");
            talk.initAction = () => pawn.pather.StopDead();
            talk.socialMode = RandomSocialMode.Off; talk.defaultCompleteMode = ToilCompleteMode.Never;
            yield return talk;
        }
    }
}
