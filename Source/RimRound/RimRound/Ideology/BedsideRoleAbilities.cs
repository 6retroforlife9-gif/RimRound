using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.Ideology
{
    // Movement is adapted here; the role's original Ability, touch verb, effect
    // validators, native warmup and PreActivate cooldown remain the owners.
    public static class BedsideRoleAbilities
    {
        public const int MaximumSessionTicks = 5000;
        internal static bool DownedPredicatePatched;
        internal static JobDef VisitJob => DefDatabase<JobDef>.GetNamedSilentFail("RR_VisitBedsideRole");
        internal static JobDef SessionJob => DefDatabase<JobDef>.GetNamedSilentFail("RR_BedsideRoleSession");

        public static bool OrdinaryColonist(Pawn p) => p != null && !p.Destroyed && !p.Dead &&
            p.Spawned && p.Faction == Faction.OfPlayer && p.IsColonist && !p.IsPrisoner &&
            !p.IsSlave && !p.IsQuestLodger() && p.RaceProps.Humanlike;

        internal static bool NativeRestJob(Pawn p) => p?.CurJobDef == JobDefOf.LayDown || p?.CurJobDef == JobDefOf.Wait_Downed;
        internal static bool Immobile(Pawn p) => p != null && (p.Downed || !p.health.capacities.CapableOf(PawnCapacityDefOf.Moving));
        internal static bool AwakeAndConscious(Pawn p) => OrdinaryColonist(p) && !p.Suspended && !p.Deathresting &&
            !p.InMentalState && !p.Drafted && !p.IsBurning() && p.Awake() &&
            p.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness);

        // Shared with body conversations. Does not require immobility, Social
        // work, a role, a particular ideology or a body-model eligibility flag.
        public static bool IsReadyBedOccupant(Pawn p) => AwakeAndConscious(p) && NativeRestJob(p) &&
            p.CurrentBed() != null && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking);

        public static bool HasLocalContact(Pawn a, Pawn b) => a != null && b != null && a != b &&
            a.Spawned && b.Spawned && a.Map == b.Map &&
            ReachabilityImmediate.CanReachImmediate(a.Position, b, a.Map, PathEndMode.Touch, null) &&
            GenSight.LineOfSight(a.Position, b.Position, a.Map, true);

        // Do not reserve the bed: its existing slot reservations belong to the
        // resting occupants. A helper's bed/cell lock must still block a visit,
        // even when that helper has not reserved the patient pawn.
        public static bool BedReservationsPermitSession(Building_Bed bed)
        {
            if (bed == null || !bed.Spawned || bed.Destroyed || bed.IsBurning()) return false;
            Map map = bed.Map;
            CellRect footprint = bed.OccupiedRect();
            if (map.physicalInteractionReservationManager.IsReserved(bed)) return false;
            foreach (IntVec3 cell in footprint)
                if (map.physicalInteractionReservationManager.IsReserved(cell)) return false;
            foreach (ReservationManager.Reservation r in map.reservationManager.ReservationsReadOnly)
            {
                bool bedTarget = r.Target.Thing == bed;
                bool cellTarget = !r.Target.HasThing && footprint.Contains(r.Target.Cell);
                if (!bedTarget && !cellTarget) continue;
                // Vanilla LayDown reserves stackCount 0, maxPawns equal to
                // sleeping slots. Allow the other sleeper's occupied slot and
                // a normal LayDown reservation on the way to their slot.
                bool normalRestSlot = bedTarget && r.MaxPawns == bed.SleepingSlotsCount && r.StackCount == 0 &&
                    r.Claimant?.Spawned == true && r.Claimant.Map == map && r.Job != null &&
                    (r.Job.def == JobDefOf.LayDown || r.Job.def == JobDefOf.Wait_Downed) &&
                    (r.Claimant.CurrentBed() == bed || r.Job.targetA.Thing == bed);
                if (!normalRestSlot) return false;
            }
            return true;
        }

        internal static bool Supported(Ability a)
        {
            if (!ModsConfig.IdeologyActive || a == null || a.GetType() != typeof(Ability) ||
                a.verb?.GetType() != typeof(Verb_CastAbilityTouch)) return false;
            string n = a.def.defName;
            return n == "Convert" || n == "Counsel" || n == "Reassure" || n == "PreachHealth" || n == "WorkDrive";
        }

        internal static bool OwnsCurrentRoleAbility(Ability a)
        {
            var role = a?.sourcePrecept as Precept_RoleSingle;
            return a?.pawn != null && Supported(a) && role != null && role.Active && role.IsAssigned(a.pawn) &&
                role.ideo != null && role.ideo == a.pawn.Ideo && role.ideo.PreceptsListForReading.Contains(role) &&
                (role.def.leaderRole || role.def.defName == "IdeoRole_Moralist") &&
                role.AbilitiesForReading != null && role.AbilitiesForReading.Contains(a) &&
                a.pawn.abilities?.AllAbilitiesForReading.Contains(a) == true;
        }

        internal static bool BedCandidate(Ability a) => Supported(a) && Immobile(a.pawn) && a.pawn.CurrentBed() != null;
        internal static bool CasterReady(Ability a) => DownedPredicatePatched && OwnsCurrentRoleAbility(a) &&
            IsReadyBedOccupant(a.pawn) && Immobile(a.pawn) && !a.pawn.WorkTagIsDisabled(WorkTags.Social) &&
            a.pawn.skills?.GetSkill(SkillDefOf.Social).TotallyDisabled == false;

        // This replaces exactly one get_Downed call inside Ability.GizmoDisabled.
        // The rest of that original method still runs, including all later gates.
        internal static bool DownedBlocksAbility(Pawn pawn, Ability ability) => pawn.Downed && !CasterReady(ability) && !BedsideRituals.CasterReady(ability);

        internal static MapComponent_BedsideRoleSessions Sessions(Pawn pawn) => pawn?.Map?.GetComponent<MapComponent_BedsideRoleSessions>();
        public static bool HasSession(Pawn pawn) => Sessions(pawn)?.Contains(pawn) == true;

        internal static void BeforeRoleUnassigned(Precept_Role role, Pawn oldPawn)
        {
            // Native role reassignment reuses Ability/Verb objects and rewrites
            // their owner. Stop our exact old holder's warmup before that write,
            // rather than waiting for the next map tick after a pawn stance tick.
            // Scribe callbacks must leave cleanup to the main postload tick.
            if (Scribe.mode != LoadSaveMode.Inactive || Current.ProgramState != ProgramState.Playing ||
                !(role is Precept_RoleSingle single) || oldPawn == null) return;
            Sessions(oldPawn)?.CancelForRole(single, oldPawn);
        }

        internal static bool NativeTargetValid(Ability ability, Pawn target) => OrdinaryColonist(target) && target != ability.pawn &&
            target.Map == ability.pawn.Map && ability.verb.targetParams.CanTarget(target) &&
            ability.verb.ValidateTarget(target, false) && ability.CanApplyOn(new LocalTargetInfo(target));

        internal static bool TargetReady(Ability ability, Pawn target, bool stationary)
        {
            if (!NativeTargetValid(ability, target) || target.InMentalState || target.Drafted || target.IsBurning() ||
                target.Suspended || target.Deathresting) return false;
            if (stationary)
            {
                if (!Immobile(target) || target.CurrentBed() == null || !NativeRestJob(target)) return false;
                // Preach health deliberately permits an unconscious patient in
                // vanilla. The speaking caster must always remain awake.
                return ability.def.defName == "PreachHealth" || AwakeAndConscious(target);
            }
            return !Immobile(target) && AwakeAndConscious(target);
        }

        internal static bool CanOrder(Ability ability, Pawn target, out string reason)
        {
            reason = "RR_BedsideRoleNotReady".Translate();
            if (!CasterReady(ability) || !OrdinaryColonist(target) || !ability.CanQueueCast ||
                ability.verb.WarmingUp || ability.verb.state != VerbState.Idle) return false;
            if (ability.GizmoDisabled(out string nativeReason)) { reason = nativeReason; return false; }
            bool stationary = Immobile(target);
            if (!TargetReady(ability, target, stationary)) return false;
            if (HasSession(ability.pawn) || HasSession(target) || AI.BedsideBodyDiscussion.HasSession(ability.pawn) || AI.BedsideBodyDiscussion.HasSession(target))
            { reason = "RR_BedsideRoleBusy".Translate(); return false; }
            if (!BedReservationsPermitSession(ability.pawn.CurrentBed()) ||
                (stationary && !BedReservationsPermitSession(target.CurrentBed())))
            { reason = "RR_BedsideRoleBusy".Translate(); return false; }
            if (stationary)
            {
                if (!HasLocalContact(ability.pawn, target)) { reason = "RR_BedsideRoleNoContact".Translate(); return false; }
            }
            else
            {
                if (target.CurJob?.playerForced == true || (target.CurJob != null && !target.CurJob.def.casualInterruptible))
                { reason = "RR_BedsideRoleRecipientBusy".Translate(); return false; }
                if (!target.CanReach(ability.pawn, PathEndMode.Touch, target.NormalMaxDanger()))
                { reason = "RR_BedsideRoleNoContact".Translate(); return false; }
            }
            Pawn claimant = stationary ? ability.pawn : target;
            if (!claimant.CanReserve(ability.pawn) || !claimant.CanReserve(target))
            { reason = "RR_BedsideRoleBusy".Translate(); return false; }
            reason = null;
            return true;
        }

        // Public entry for native fixtures and other explicitly ordered UI.
        // Confirmation is native and revalidates after the player confirms.
        public static bool TryOrder(Ability ability, Pawn target, out string reason)
        {
            if (!CanOrder(ability, target, out reason)) return false;
            Action begin = () => {
                if (!CanOrder(ability, target, out string failure) || !Sessions(ability.pawn).Begin(ability, target))
                    Messages.Message(failure ?? "RR_BedsideRoleBusy".Translate().ToString(), target, MessageTypeDefOf.RejectInput, false);
            };
            Window confirmation = ability.ConfirmationDialog(new LocalTargetInfo(target), begin);
            if (confirmation != null) Find.WindowStack.Add(confirmation);
            else begin();
            return true;
        }
    }

    // Owned by the map, never a static pawn registry. The reservation Job is a
    // real saved native Job handle, separate from both occupants' rest jobs.
    public sealed class BedsideRoleSession : IExposable
    {
        public Ability Ability;
        public Pawn Caster, Recipient;
        public Building_Bed CasterBed, RecipientBed;
        public Ideo CasterIdeo, RecipientIdeo;
        public Precept_RoleSingle Role;
        public Job ReservationJob;
        public IntVec3 CasterPosition, RecipientPosition;
        public int CasterRestJobId, RecipientRestJobId, VisitorJobId;
        public int CreatedTick, StartedTick, LastObservationTick, InitialWarmupTicks, LastWarmupTicks, ObservedWarmupTicks;
        public bool Stationary, Started, Attempted, Completed, Cancelled;
        [Unsaved(false)] public bool NeedsPostLoadCheck;
        public int Id => ReservationJob?.loadID ?? -1;
        internal Pawn Claimant => Stationary ? Caster : Recipient;
        public bool Finished => Completed || Cancelled || Attempted;

        public void ExposeData()
        {
            Scribe_References.Look(ref Ability, "ability");
            Scribe_References.Look(ref Caster, "caster");
            Scribe_References.Look(ref Recipient, "recipient");
            Scribe_References.Look(ref CasterBed, "casterBed");
            Scribe_References.Look(ref RecipientBed, "recipientBed");
            Scribe_References.Look(ref CasterIdeo, "casterIdeo");
            Scribe_References.Look(ref RecipientIdeo, "recipientIdeo");
            Scribe_References.Look(ref Role, "role");
            Scribe_Deep.Look(ref ReservationJob, "reservationJob");
            Scribe_Values.Look(ref CasterPosition, "casterPosition");
            Scribe_Values.Look(ref RecipientPosition, "recipientPosition");
            Scribe_Values.Look(ref CasterRestJobId, "casterRestJobId");
            Scribe_Values.Look(ref RecipientRestJobId, "recipientRestJobId");
            Scribe_Values.Look(ref VisitorJobId, "visitorJobId");
            Scribe_Values.Look(ref CreatedTick, "createdTick");
            Scribe_Values.Look(ref StartedTick, "startedTick");
            Scribe_Values.Look(ref LastObservationTick, "lastObservationTick");
            Scribe_Values.Look(ref InitialWarmupTicks, "initialWarmupTicks");
            Scribe_Values.Look(ref LastWarmupTicks, "lastWarmupTicks");
            Scribe_Values.Look(ref ObservedWarmupTicks, "observedWarmupTicks");
            Scribe_Values.Look(ref Stationary, "stationary");
            Scribe_Values.Look(ref Started, "started");
            Scribe_Values.Look(ref Attempted, "attempted");
            Scribe_Values.Look(ref Completed, "completed");
            Scribe_Values.Look(ref Cancelled, "cancelled");
            // No pawn/job/reservation mutation in a load worker.
            if (Scribe.mode == LoadSaveMode.PostLoadInit) NeedsPostLoadCheck = true;
        }

        internal bool HasReservations(Map map) => ReservationJob != null && Claimant != null &&
            map.reservationManager.ReservedBy(Caster, Claimant, ReservationJob) &&
            map.reservationManager.ReservedBy(Recipient, Claimant, ReservationJob) &&
            map.reservationManager.OnlyReservationsForJobDef(Caster, BedsideRoleAbilities.SessionJob, true) &&
            map.reservationManager.OnlyReservationsForJobDef(Recipient, BedsideRoleAbilities.SessionJob, true) &&
            !map.physicalInteractionReservationManager.IsReserved(Caster) &&
            !map.physicalInteractionReservationManager.IsReserved(Recipient);

        internal bool Reserve(Map map) => Claimant != null && ReservationJob != null &&
            map.reservationManager.Reserve(Claimant, ReservationJob, Caster, errorOnFailed: false) &&
            map.reservationManager.Reserve(Claimant, ReservationJob, Recipient, errorOnFailed: false);

        internal bool VisitorMatches() => Recipient?.CurJobDef == BedsideRoleAbilities.VisitJob &&
            Recipient.CurJob.loadID == VisitorJobId && Recipient.CurJob.count == Id &&
            Recipient.CurJob.ability == Ability && Recipient.CurJob.targetA.Pawn == Caster;

        internal bool Valid(Map map, bool local, bool reservations = true)
        {
            int now = Find.TickManager.TicksGame;
            if (Cancelled || Completed || Attempted || Ability == null || Ability.pawn != Caster ||
                !BedsideRoleAbilities.CasterReady(Ability) || !BedsideRoleAbilities.TargetReady(Ability, Recipient, Stationary) ||
                Caster.Map != map || Recipient.Map != map || Role != Ability.sourcePrecept ||
                Caster.Ideo != CasterIdeo || Recipient.Ideo != RecipientIdeo ||
                Caster.CurrentBed() != CasterBed || Caster.Position != CasterPosition ||
                Caster.CurJob?.loadID != CasterRestJobId ||
                !BedsideRoleAbilities.BedReservationsPermitSession(CasterBed) ||
                now < CreatedTick || now - CreatedTick > BedsideRoleAbilities.MaximumSessionTicks ||
                (reservations && !HasReservations(map))) return false;
            if (Stationary)
            {
                if (Recipient.CurrentBed() != RecipientBed || Recipient.Position != RecipientPosition ||
                    Recipient.CurJob?.loadID != RecipientRestJobId ||
                    !BedsideRoleAbilities.BedReservationsPermitSession(RecipientBed)) return false;
            }
            else if (!VisitorMatches()) return false;
            if (local && !BedsideRoleAbilities.HasLocalContact(Caster, Recipient)) return false;
            // Native GizmoDisabled retains cooldown, comp, lord and queue gates.
            // The separate busy postfix excludes this session while validating.
            return !BedsideRoleAbilityPatches.NativeGizmoDisabled(Ability, out _);
        }

        internal bool StartCast(Map map)
        {
            if (Started) return !Finished;
            if (NeedsPostLoadCheck || !Valid(map, true) || Ability.verb.state != VerbState.Idle || Ability.verb.WarmingUp) return false;
            Started = true;
            StartedTick = LastObservationTick = Find.TickManager.TicksGame;
            // GetJob recaches native warmup motes; its returned caster job is
            // never ordered, because the caster keeps their existing rest job.
            Job unusedNativeJob = Ability.GetJob(Recipient, LocalTargetInfo.Invalid);
            unusedNativeJob.Clear();
            Ability.Notify_StartedCasting();
            if (!Ability.verb.TryStartCastOn(Recipient, LocalTargetInfo.Invalid)) return false;
            InitialWarmupTicks = LastWarmupTicks = Ability.verb.WarmupTicksLeft;
            return InitialWarmupTicks > 0 && Ability.verb.WarmingUp;
        }

        internal bool ObserveWarmup()
        {
            int now = Find.TickManager.TicksGame;
            int remaining = Ability.verb.WarmupTicksLeft;
            int elapsed = now - LastObservationTick;
            int advanced = LastWarmupTicks - remaining;
            // Map-component and pawn-stance ticks can occur in either order in
            // a game tick. Observe at most one native decrement per sample;
            // never synthesize progress from elapsed time or a reload gap.
            if (elapsed < 0 || elapsed > 1 || advanced < 0 || advanced > 1) return false;
            ObservedWarmupTicks += advanced;
            LastWarmupTicks = remaining;
            LastObservationTick = now;
            return true;
        }

        internal bool BeforeNativeEffect(Map map, LocalTargetInfo target)
        {
            if (target.Pawn != Recipient || !Started || NeedsPostLoadCheck || !Valid(map, true) ||
                Ability.verb.CurrentTarget.Pawn != Recipient || !ObserveWarmup() || InitialWarmupTicks <= 0 ||
                ObservedWarmupTicks != InitialWarmupTicks || LastWarmupTicks != 0) return false;
            Attempted = true; // Set before native effect; never retry an uncertain partial cast.
            return true;
        }

        internal void StopNativeWarmup()
        {
            if (!Started || Ability?.verb == null || Ability.verb.CurrentTarget.Pawn != Recipient) return;
            if (Caster?.stances?.curStance is Stance_Warmup warmup && warmup.verb == Ability.verb)
                warmup.Interrupt();
            Ability.verb.Reset();
        }
    }

    public sealed class MapComponent_BedsideRoleSessions : MapComponent
    {
        private List<BedsideRoleSession> sessions = new List<BedsideRoleSession>();
        public MapComponent_BedsideRoleSessions(Map map) : base(map) { }
        public IReadOnlyList<BedsideRoleSession> SessionsForReading => sessions;
        public bool Contains(Pawn pawn) => pawn != null && sessions.Any(s => s != null && (s.Caster == pawn || s.Recipient == pawn));
        internal BedsideRoleSession Find(int id) => sessions.FirstOrDefault(s => s != null && s.Id == id);
        internal BedsideRoleSession Find(Ability ability) => sessions.FirstOrDefault(s => s != null && s.Ability == ability);
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref sessions, "rrBedsideRoleSessions", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && sessions == null) sessions = new List<BedsideRoleSession>();
        }

        internal bool Begin(Ability ability, Pawn recipient)
        {
            if (!BedsideRoleAbilities.CanOrder(ability, recipient, out _) || BedsideRoleAbilities.VisitJob == null ||
                BedsideRoleAbilities.SessionJob == null) return false;
            Pawn caster = ability.pawn;
            var session = new BedsideRoleSession {
                Ability = ability, Caster = caster, Recipient = recipient,
                CasterBed = caster.CurrentBed(), RecipientBed = recipient.CurrentBed(),
                CasterIdeo = caster.Ideo, RecipientIdeo = recipient.Ideo, Role = ability.sourcePrecept as Precept_RoleSingle,
                CasterPosition = caster.Position, RecipientPosition = recipient.Position,
                CasterRestJobId = caster.CurJob.loadID, RecipientRestJobId = recipient.CurJob?.loadID ?? -1,
                Stationary = BedsideRoleAbilities.Immobile(recipient), CreatedTick = Verse.Find.TickManager.TicksGame,
                ReservationJob = JobMaker.MakeJob(BedsideRoleAbilities.SessionJob, caster, recipient)
            };
            sessions.Add(session);
            if (session.Stationary)
            {
                if (session.Reserve(map)) return true;
            }
            else
            {
                Job visit = JobMaker.MakeJob(BedsideRoleAbilities.VisitJob, caster, session.CasterBed);
                visit.count = session.Id;
                visit.ability = ability;
                session.VisitorJobId = visit.loadID;
                // Never playerForced: no native reservation override or forced
                // interruption of recipient work is authorized by this order.
                recipient.jobs.StartJob(visit, JobCondition.InterruptOptional, resumeCurJobAfterwards: true,
                    preToilReservationsCanFail: true);
                if (session.VisitorMatches() && session.HasReservations(map)) return true;
            }
            Remove(session, false);
            return false;
        }

        internal bool ReserveVisitor(Pawn visitor, int id)
        {
            BedsideRoleSession session = Find(id);
            return session != null && !session.Stationary && session.Recipient == visitor &&
                session.Valid(map, false, false) && session.Reserve(map);
        }
        internal bool StartVisitorCast(Pawn visitor, int id)
        {
            BedsideRoleSession session = Find(id);
            return session != null && session.Recipient == visitor && session.StartCast(map);
        }
        internal void VisitorFinished(Pawn visitor, int id)
        {
            BedsideRoleSession session = Find(id);
            if (session != null && session.Recipient == visitor) Remove(session, false);
        }

        internal void CancelForRole(Precept_RoleSingle role, Pawn oldPawn)
        {
            // Snapshot only this role's sessions: ending the visitor job invokes
            // its finish action synchronously and may reenter this component.
            foreach (BedsideRoleSession session in sessions.Where(s => s != null &&
                s.Role == role && s.Caster == oldPawn).ToArray()) Remove(session, true);
        }

        private void Remove(BedsideRoleSession session, bool endVisitor)
        {
            // Remove before any cleanup callback; a repeated cancellation must
            // not reset a later verb/stance or end a replacement visitor job.
            if (session == null || !sessions.Remove(session)) return;
            bool matched = session.VisitorMatches();
            if (!session.Completed) session.Cancelled = true;
            session.StopNativeWarmup();
            if (session.Claimant != null && session.ReservationJob != null)
                map.reservationManager.ReleaseClaimedBy(session.Claimant, session.ReservationJob);
            if (endVisitor && !session.Stationary && matched)
                session.Recipient.jobs.EndCurrentJob(session.Completed ? JobCondition.Succeeded : JobCondition.Incompletable);
        }

        public override void MapComponentTick()
        {
            // Empty maps perform no pawn scan. Validation and cleanup occur on
            // the main tick thread, including after Scribe has resolved refs.
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                BedsideRoleSession session = sessions[i];
                if (session == null) { sessions.RemoveAt(i); continue; }
                if (session.Finished) { Remove(session, true); continue; }
                if (!session.Valid(map, session.Started || session.Stationary)) { Remove(session, true); continue; }
                if (session.NeedsPostLoadCheck)
                {
                    session.NeedsPostLoadCheck = false;
                    // A saved cast continues only when the exact native stance
                    // and saved observed time still agree. Never restart it.
                    if (session.Started && (!session.Ability.verb.WarmingUp ||
                        session.Ability.verb.CurrentTarget.Pawn != session.Recipient))
                    { Remove(session, true); continue; }
                }
                if (!session.Started)
                {
                    if (session.Stationary && !session.StartCast(map)) Remove(session, true);
                    continue;
                }
                if (!session.Ability.verb.WarmingUp || !session.ObserveWarmup()) Remove(session, true);
            }
        }

        public override void MapRemoved()
        {
            foreach (BedsideRoleSession session in sessions.ToArray())
                if (session != null) Remove(session, false);
            sessions.Clear();
            base.MapRemoved();
        }
    }

    public sealed class JobDriver_VisitBedsideRole : JobDriver
    {
        private MapComponent_BedsideRoleSessions Owner => BedsideRoleAbilities.Sessions(pawn);
        public override bool TryMakePreToilReservations(bool errorOnFailed) => Owner?.ReserveVisitor(pawn, job.count) == true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.FailOn(() => Owner?.Find(job.count) == null);
            AddFinishAction(condition => Owner?.VisitorFinished(pawn, job.count));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil attend = ToilMaker.MakeToil("AttendBedsideRoleAbility");
            attend.initAction = () => {
                pawn.pather.StopDead();
                if (Owner?.StartVisitorCast(pawn, job.count) != true) EndJobWith(JobCondition.Incompletable);
            };
            attend.defaultCompleteMode = ToilCompleteMode.Never;
            attend.socialMode = RandomSocialMode.Off;
            yield return attend;
        }
    }

    internal static class BedsideRoleAbilityPatches
    {
        [ThreadStatic] private static int validatingNativeGizmo;
        internal static bool NativeGizmoDisabled(Ability ability, out string reason)
        {
            validatingNativeGizmo++;
            try { return ability.GizmoDisabled(out reason); }
            finally { validatingNativeGizmo--; }
        }

        [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.Notify_PawnUnassigned), new[] { typeof(Pawn) })]
        private static class RoleUnassigned
        {
            private static void Prefix(Precept_Role __instance, Pawn oldPawn) =>
                BedsideRoleAbilities.BeforeRoleUnassigned(__instance, oldPawn);
        }

        [HarmonyPatch(typeof(Ideo), nameof(Ideo.RemovePrecept), new[] { typeof(Precept), typeof(bool) })]
        private static class RolePreceptRemoved
        {
            private static void Prefix(Ideo __instance, Precept precept)
            {
                // Removing/replacing a precept does not call native unassignment.
                if (precept is Precept_RoleSingle role && role.ideo == __instance &&
                    __instance.PreceptsListForReading.Contains(role))
                    BedsideRoleAbilities.BeforeRoleUnassigned(role, role.ChosenPawnValue);
            }
        }

        [HarmonyPatch(typeof(Ideo), nameof(Ideo.ClearPrecepts))]
        private static class RolePreceptsCleared
        {
            private static void Prefix(Ideo __instance)
            {
                foreach (Precept_RoleSingle role in __instance.PreceptsListForReading.OfType<Precept_RoleSingle>().ToArray())
                    if (role.ideo == __instance) BedsideRoleAbilities.BeforeRoleUnassigned(role, role.ChosenPawnValue);
            }
        }

        [HarmonyPatch(typeof(Ability), nameof(Ability.GizmoDisabled))]
        private static class Gizmo
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                MethodInfo downed = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Downed));
                int count = code.Count(i => i.Calls(downed));
                BedsideRoleAbilities.DownedPredicatePatched = count == 1;
                if (count != 1)
                {
                    Log.Error("[RimRound] Bedside role abilities disabled: expected exactly one Ability.GizmoDisabled Downed predicate, found " + count);
                    return code;
                }
                int index = code.FindIndex(i => i.Calls(downed));
                var loadAbility = new CodeInstruction(OpCodes.Ldarg_0);
                loadAbility.labels.AddRange(code[index].labels);
                loadAbility.blocks.AddRange(code[index].blocks);
                code[index] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BedsideRoleAbilities), "DownedBlocksAbility"));
                code.Insert(index, loadAbility);
                return code;
            }
            private static void Postfix(Ability __instance, ref bool __result, ref string reason)
            {
                if (validatingNativeGizmo == 0 && !__result && BedsideRoleAbilities.BedCandidate(__instance) &&
                    BedsideRoleAbilities.HasSession(__instance.pawn))
                { __result = true; reason = "RR_BedsideRoleBusy".Translate(); }
            }
        }

        [HarmonyPatch(typeof(Ability), nameof(Ability.QueueCastingJob), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })]
        private static class Queue
        {
            private static bool Prefix(Ability __instance, LocalTargetInfo target)
            {
                if (!BedsideRoleAbilities.BedCandidate(__instance)) return true;
                if (!BedsideRoleAbilities.TryOrder(__instance, target.Pawn, out string reason))
                    Messages.Message(reason, __instance.pawn, MessageTypeDefOf.RejectInput, false);
                return false;
            }
        }

        [HarmonyPatch(typeof(Ability), nameof(Ability.Activate), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })]
        private static class ActualEffect
        {
            private static bool Prefix(Ability __instance, LocalTargetInfo target, ref bool __result, out BedsideRoleSession __state)
            {
                __state = BedsideRoleAbilities.Sessions(__instance.pawn)?.Find(__instance);
                if (__state == null) return true;
                if (__state.BeforeNativeEffect(__instance.pawn.Map, target)) return true;
                __state.Cancelled = true;
                __result = false;
                return false;
            }
            private static void Postfix(bool __result, BedsideRoleSession __state)
            {
                if (__state != null && __state.Attempted) __state.Completed = __result;
            }
        }
    }
}
