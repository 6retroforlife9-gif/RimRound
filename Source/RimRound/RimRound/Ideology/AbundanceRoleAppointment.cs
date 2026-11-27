using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    // RoleChange has no officiant. Its selected pawn, native role selection and
    // native outcome remain authoritative; a bed only changes local movement.
    public static class AbundanceRoleAppointment
    {
        public const string Changer = "role_changer", Host = "RR_appointment_bed_host";
        internal const string RestDuty = "RR_BedsideRitualActor";
        internal static readonly FieldInfo AssignmentTarget = AccessTools.Field(typeof(RitualRoleAssignments), "ritualTarget");
        internal static readonly FieldInfo DialogRitual = AccessTools.Field(typeof(Dialog_BeginRitual), "ritual");
        internal static readonly FieldInfo DialogTarget = AccessTools.Field(typeof(Dialog_BeginRitual), "target");
        internal static readonly FieldInfo DialogAssignments = AccessTools.Field(typeof(Dialog_BeginRitual), "assignments");
        internal static readonly MethodInfo CloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone");
        internal static T Clone<T>(T value) where T : class => (T)CloneMethod.Invoke(value, null);
        internal static bool Supported(Precept_Ritual ritual) => ritual?.def.defName == "RoleChange" &&
            AbundanceRoles.Enabled(ritual.ideo) && ritual.ideo.PreceptsListForReading.Contains(ritual) &&
            ritual.outcomeEffect?.GetType() == typeof(RitualOutcomeEffectWorker_RoleChange);
        internal static bool BedScope(Precept_Ritual ritual, TargetInfo target) => ritual?.def.defName == "RoleChange" &&
            ritual.behavior is RitualBehaviorWorker_AbundanceRoleAppointment && target.Thing is Building_Bed bed && bed.def.defName == "NotRegalBed";
        internal static bool DialogScope(Dialog_BeginRitual dialog) => BedScope((Precept_Ritual)DialogRitual.GetValue(dialog), (TargetInfo)DialogTarget.GetValue(dialog));
        internal static bool MatchesUse(Precept_Role role, AbundanceBedUse use) => role != null &&
            (use == AbundanceBedUse.Leader ? role.def.leaderRole : use == AbundanceBedUse.MoralGuide ? role.def.defName == "IdeoRole_Moralist" :
                use == AbundanceBedUse.Feedee && role.def.defName == AbundanceRoles.Feedee);
        internal static bool EligibleSelection(Pawn pawn, Precept_Role role) => role != null && role.Active &&
            role.ideo.PreceptsListForReading.Contains(role) && role.RequirementsMet(pawn) &&
            RitualUtility.AllRolesForPawn(pawn).Contains(role) && (!role.def.leaderRole || pawn.Ideo == Faction.OfPlayer.ideos.PrimaryIdeo);
        internal static bool Awake(Pawn pawn) => BedsideRoleAbilities.AwakeAndConscious(pawn) &&
            pawn.CurJob?.forceSleep != true && !pawn.health.hediffSet.HasHediff(HediffDefOf.Anesthetic);
        internal static bool Resting(Pawn pawn, Building_Bed bed) => Awake(pawn) && pawn.CurrentBed() == bed &&
            BedsideRoleAbilities.NativeRestJob(pawn) && pawn.GetPosture() == PawnPosture.LayingInBed;
        internal static bool Local(Pawn pawn, Building_Bed bed) => pawn?.Spawned == true && pawn.Map == bed?.Map &&
            pawn.Position.InHorDistOf(bed.GetSleepingSlotPos(0), 8f) && AbundanceBeds.SameRoom(pawn.Position, bed.Position, bed.Map) &&
            GenSight.LineOfSight(pawn.Position, bed.GetSleepingSlotPos(0), bed.Map, true);

        public static bool BedValid(Building_Bed bed, Precept_Ritual ritual)
        {
            var focus = AbundanceBeds.Focus(bed); Pawn owner = focus?.Owner;
            if (!Supported(ritual) || bed?.def.defName != "NotRegalBed" || bed.Spawned != true || bed.Faction != Faction.OfPlayer ||
                bed.Medical || bed.ForPrisoners || bed.IsBurning() || focus?.Designated != true ||
                !BedsideRoleAbilities.OrdinaryColonist(owner) || owner.Map != bed.Map || owner.Ideo != ritual.ideo ||
                focus.Ideology != ritual.ideo || bed.IsForbidden(owner) || !AbundanceBeds.HasTable(bed) ||
                !AbundanceBeds.ApproachCells(bed).Any() || !BedsideRoleAbilities.BedReservationsPermitSession(bed) ||
                bed.CurOccupants.Any(p => p != owner)) return false;
            Precept_Role current = owner.Ideo.GetRole(owner);
            // A prospective owner may use the ceremony to acquire precisely the
            // role for which their bed was designated. Selection is checked later.
            return current != null ? current.Active && MatchesUse(current, focus.Use) :
                RitualUtility.AllRolesForPawn(owner).Any(r => MatchesUse(r, focus.Use) && EligibleSelection(owner, r));
        }
        internal static bool CandidateReady(Pawn pawn, Building_Bed bed, Precept_Ritual ritual)
        {
            if (!Awake(pawn) || pawn.Ideo != ritual.ideo || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking)) return false;
            if (Resting(pawn, bed)) return AbundanceBeds.Focus(bed)?.Owner == pawn;
            return !BedsideRoleAbilities.Immobile(pawn) && pawn.CanReach(bed, PathEndMode.Touch, Danger.Some) &&
                AbundanceBeds.ApproachCells(bed).Any(c => pawn.CanReach(c, PathEndMode.OnCell, Danger.Some));
        }
        internal static string Problem(Precept_Ritual ritual, Building_Bed bed, RitualRoleAssignments assignments)
        {
            if (!BedValid(bed, ritual)) return "RR_BedsideRitualSite".Translate();
            Pawn pawn = assignments.FirstAssignedPawn(Changer), owner = AbundanceBeds.Focus(bed).Owner;
            if (pawn == null || !CandidateReady(pawn, bed, ritual)) return "RR_BedsideRoleNotReady".Translate();
            Precept_Role selected = assignments.RoleChangeSelection, current = pawn.Ideo.GetRole(pawn);
            if (selected == current || (selected != null && !EligibleSelection(pawn, selected)))
                return "MessageRoleChangeChooseDifferentRole".Translate(pawn.Named("PAWN"));
            if (owner.Ideo.GetRole(owner) == null && (pawn != owner || !MatchesUse(selected, AbundanceBeds.Focus(bed).Use)))
                return "RR_AbundanceBedOwnerMismatch".Translate();
            if (!Resting(owner, bed) || !assignments.Participants.Contains(owner) ||
                (owner != pawn && assignments.FirstAssignedPawn(Host) != owner)) return "RR_BedsideRoleNotReady".Translate();
            foreach (Pawn participant in new[] { pawn, owner }.Distinct())
                if (participant.GetLord() != null || BedsideRoleAbilities.HasSession(participant) ||
                    AI.BedsideBodyDiscussion.HasSession(participant) || AbundanceBedEntry.HasPending(participant) ||
                    !AbundanceAppointmentEntry.CanBegin(participant, ritual, bed))
                    return "RR_BedsideRoleBusy".Translate();
            return null;
        }
        internal static void AdaptAssignments(RitualRoleAssignments assignments)
        {
            TargetInfo target = (TargetInfo)AssignmentTarget.GetValue(assignments);
            if (!BedScope(assignments.Ritual, target) || assignments.AllRolesForReading == null ||
                assignments.AllRolesForReading.Any(r => r.id == Host)) return;
            int index = assignments.AllRolesForReading.FindIndex(r => r.id == Changer);
            if (index < 0) return;
            RitualRole changer = Clone(assignments.AllRolesForReading[index]);
            changer.allowDowned = true; changer.allowKeepLayingDown = true;
            changer.mustBeAbleToReachTarget = false; changer.defaultForSelectedColonist = true;
            assignments.AllRolesForReading[index] = changer;
            assignments.AllRolesForReading.Add(new RitualRole_AbundanceAppointmentHost());
        }
        internal static void PrepareCandidates(RitualRoleAssignments assignments)
        {
            AdaptAssignments(assignments);
            TargetInfo target = (TargetInfo)AssignmentTarget.GetValue(assignments);
            if (!BedScope(assignments.Ritual, target)) return;
            var bed = (Building_Bed)target.Thing;
            Pawn owner = AbundanceBeds.Focus(bed)?.Owner;
            // Dialog_BeginRitual filters downed pawns using the original def's
            // roles before Setup. Restore only the resting owner, after normal
            // eligibility has been checked against this ceremony's local roles.
            if (owner?.GetLord() == null && Resting(owner, bed) &&
                assignments.AllRolesForReading.Any(r => assignments.PawnNotAssignableReason(owner, r).NullOrEmpty()))
            {
                assignments.AllCandidatePawns.AddUnique(owner);
                assignments.NonAssignablePawns.Remove(owner);
            }
        }
        internal static void ConfigureRestRole(RitualRoleAssignments assignments, bool keep)
        {
            var role = assignments.AllRolesForReading.First(r => r.id == Changer);
            role.allowKeepLayingDown = keep; role.endJobOnRitualCleanup = !keep;
        }
        internal static void InstallAdapters()
        {
            if (!ModsConfig.IdeologyActive || Find.IdeoManager == null) return;
            foreach (Ideo ideo in Find.IdeoManager.IdeosListForReading)
                foreach (var ritual in ideo.PreceptsListForReading.OfType<Precept_Ritual>().Where(Supported))
                {
                    if (Find.Maps.Any(m => Find.IdeoManager.GetActiveRituals(m).Any(r => r.Ritual == ritual))) continue;
                    if (ritual.behavior?.GetType() == typeof(RitualBehaviorWorker) && ritual.behavior.def.defName == "RoleChange")
                        ritual.behavior = new RitualBehaviorWorker_AbundanceRoleAppointment(ritual.behavior.def);
                    if (ritual.behavior is RitualBehaviorWorker_AbundanceRoleAppointment && ritual.obligationTargetFilter != null &&
                        !(ritual.obligationTargetFilter is RitualTarget_AbundanceRoleAppointment))
                        ritual.obligationTargetFilter = new RitualTarget_AbundanceRoleAppointment {
                            Original = ritual.obligationTargetFilter, def = ritual.obligationTargetFilter.def, parent = ritual
                        };
                }
        }
        internal static bool RetainJob(Pawn pawn) => pawn?.GetLord()?.LordJob is LordJob_AbundanceRoleAppointment lord &&
            lord.State.Rests.Any(r => r.Pawn == pawn && r.Unchanged(lord.State.Bed));
    }

    // A click prepares only the owner's normal rest job. Native RoleChange
    // still owns the dialog, role choice, actual start and eventual assignment.
    public static class AbundanceAppointmentEntry
    {
        internal static MapComponent_AbundanceAppointmentEntries Requests(Pawn pawn) =>
            pawn?.Map?.GetComponent<MapComponent_AbundanceAppointmentEntries>();
        public static bool HasPending(Pawn pawn) => Requests(pawn)?.Find(pawn) != null;
        internal static bool CanUseBed(Pawn owner, Building_Bed bed, Precept_Ritual ritual)
        {
            if (!AbundanceRoleAppointment.BedValid(bed, ritual) || AbundanceBeds.Focus(bed).Owner != owner ||
                !AbundanceRoleAppointment.Awake(owner) || owner.Map != bed.Map) return false;
            if (owner.CurrentBed() == bed && BedsideRoleAbilities.NativeRestJob(owner)) return true;
            return !BedsideRoleAbilities.Immobile(owner) && owner.CanReserve(bed, bed.SleepingSlotsCount, 0) &&
                owner.CanReach(bed, PathEndMode.OnCell, Danger.Some);
        }
        internal static bool TryOrder(Precept_Ritual ritual, Building_Bed bed, RitualObligation obligation, Action onConfirm,
            Pawn organizer, Dictionary<string, Pawn> forcedForRole, Pawn selectedPawn, out string reason)
        {
            reason = "RR_BedsideRoleNotReady".Translate();
            Pawn owner = AbundanceBeds.Focus(bed)?.Owner;
            if (!CanUseBed(owner, bed, ritual)) return false;
            if (HasPending(owner) || owner.GetLord() != null || BedsideRoleAbilities.HasSession(owner) ||
                AI.BedsideBodyDiscussion.HasSession(owner) || AbundanceBedEntry.HasPending(owner))
            { reason = "RR_BedsideRoleBusy".Translate(); return false; }
            // An unrelated ordinary rest remains the player's existing order.
            // A walk already ordered to this exact bed can be reused safely.
            Job rest = owner.CurJob;
            bool reuse = rest?.def == JobDefOf.LayDown && rest.targetA.Thing == bed;
            if (BedsideRoleAbilities.NativeRestJob(owner) && !reuse) return false;
            string blocked = ritual.behavior.CanStartRitualNow(bed, ritual, selectedPawn, forcedForRole);
            if (!blocked.NullOrEmpty()) { reason = blocked; return false; }
            if (!ritual.behavior.CanExecuteOn(bed, obligation)) return false;
            var request = new AbundanceAppointmentEntryRequest {
                Owner = owner, OwnerRole = owner.Ideo.GetRole(owner), Ideology = owner.Ideo,
                Bed = bed, BedPosition = bed.Position, BedRotation = bed.Rotation, Use = AbundanceBeds.Focus(bed).Use,
                Ritual = ritual, Obligation = obligation, OnConfirm = onConfirm, Organizer = organizer,
                ForcedForRole = forcedForRole == null ? null : new Dictionary<string, Pawn>(forcedForRole), SelectedPawn = selectedPawn,
                CreatedTick = Find.TickManager.TicksGame, IssuedRestJob = !reuse
            };
            request.InputPawns = new[] { organizer, selectedPawn }.Concat((IEnumerable<Pawn>)forcedForRole?.Values ?? Enumerable.Empty<Pawn>())
                .Where(p => p != null).Distinct().ToDictionary(p => p, p => p.Ideo);
            if (!reuse) rest = JobMaker.MakeJob(JobDefOf.LayDown, bed);
            request.RestJobId = rest.loadID;
            var entries = Requests(owner);
            entries.Add(request);
            if ((!reuse && !owner.jobs.TryTakeOrderedJob(rest, JobTag.Misc)) || !request.MatchesCurrentRestJob || !request.Valid())
            { entries.Remove(request, true, false); return false; }
            reason = null; return true;
        }
        internal static bool KeepRestJob(Pawn pawn) => Requests(pawn)?.Find(pawn)?.Valid() == true;
        internal static bool CanBegin(Pawn pawn, Precept_Ritual ritual, Building_Bed bed)
        {
            var request = Requests(pawn)?.Find(pawn);
            return request == null || (request.Ritual == ritual && request.Bed == bed && request.Valid() && request.Settled);
        }
    }
    public sealed class AbundanceAppointmentEntryRequest : IExposable
    {
        public Pawn Owner;
        public Precept_Role OwnerRole;
        public Ideo Ideology;
        public Precept_Ritual Ritual;
        public Building_Bed Bed;
        public IntVec3 BedPosition;
        public Rot4 BedRotation;
        public AbundanceBedUse Use;
        public int RestJobId, CreatedTick;
        public bool IssuedRestJob, DialogOpened;
        [Unsaved(false)] public bool AbortAfterLoad, OpeningDialog;
        [Unsaved(false)] public Window Dialog;
        [Unsaved(false)] public RitualObligation Obligation;
        [Unsaved(false)] public Action OnConfirm;
        [Unsaved(false)] public Pawn Organizer, SelectedPawn;
        [Unsaved(false)] public Dictionary<string, Pawn> ForcedForRole;
        [Unsaved(false)] public Dictionary<Pawn, Ideo> InputPawns;
        public bool MatchesCurrentRestJob => Owner?.CurJobDef == JobDefOf.LayDown &&
            Owner.CurJob.loadID == RestJobId && Owner.CurJob.targetA.Thing == Bed;
        public bool Settled => MatchesCurrentRestJob && AbundanceRoleAppointment.Resting(Owner, Bed);
        internal Job RestJob()
        {
            if (MatchesCurrentRestJob) return Owner.CurJob;
            if (Owner?.jobs?.jobQueue != null)
                for (int i = 0; i < Owner.jobs.jobQueue.Count; i++)
                {
                    Job job = Owner.jobs.jobQueue[i].job;
                    if (job.loadID == RestJobId && job.def == JobDefOf.LayDown && job.targetA.Thing == Bed) return job;
                }
            return null;
        }
        internal bool Started => Owner?.GetLord()?.LordJob is LordJob_AbundanceRoleAppointment lord &&
            lord.Ritual == Ritual && lord.State.Owner == Owner && lord.State.Bed == Bed &&
            lord.State.Rests.Any(r => r.Pawn == Owner && r.JobId == RestJobId);
        internal bool Valid()
        {
            int now = Find.TickManager.TicksGame;
            if (AbortAfterLoad || Owner?.Spawned != true || Owner.GetLord() != null || !MatchesCurrentRestJob ||
                !AbundanceAppointmentEntry.CanUseBed(Owner, Bed, Ritual) || Bed.Position != BedPosition || Bed.Rotation != BedRotation ||
                AbundanceBeds.Focus(Bed).Use != Use || Owner.Ideo != Ideology || Ideology.GetRole(Owner) != OwnerRole ||
                now < CreatedTick || now - CreatedTick > 5000 || BedsideRoleAbilities.HasSession(Owner) ||
                AI.BedsideBodyDiscussion.HasSession(Owner) || AbundanceBedEntry.HasPending(Owner)) return false;
            return InputPawns == null || InputPawns.All(p => p.Key.Spawned && !p.Key.Dead && p.Key.Map == Owner.Map &&
                p.Key.Ideo == p.Value && p.Key.GetLord() == null);
        }
        public void ExposeData()
        {
            Scribe_References.Look(ref Owner, "owner"); Scribe_References.Look(ref OwnerRole, "ownerRole");
            Scribe_References.Look(ref Ideology, "ideology"); Scribe_References.Look(ref Ritual, "ritual");
            Scribe_References.Look(ref Bed, "bed"); Scribe_Values.Look(ref BedPosition, "bedPosition");
            Scribe_Values.Look(ref BedRotation, "bedRotation"); Scribe_Values.Look(ref Use, "bedUse");
            Scribe_Values.Look(ref RestJobId, "restJobId"); Scribe_Values.Look(ref CreatedTick, "createdTick");
            Scribe_Values.Look(ref IssuedRestJob, "issuedRestJob"); Scribe_Values.Look(ref DialogOpened, "dialogOpened");
            // Native dialogs and caller callbacks are not save state. Pending
            // entries abort after load; an already started Lord retains rest.
            if (Scribe.mode == LoadSaveMode.PostLoadInit) AbortAfterLoad = true;
        }
    }
    public sealed class MapComponent_AbundanceAppointmentEntries : MapComponent
    {
        List<AbundanceAppointmentEntryRequest> requests = new List<AbundanceAppointmentEntryRequest>();
        public MapComponent_AbundanceAppointmentEntries(Map map) : base(map) { }
        public IReadOnlyList<AbundanceAppointmentEntryRequest> RequestsForReading => requests;
        internal AbundanceAppointmentEntryRequest Find(Pawn owner) => requests.FirstOrDefault(r => r?.Owner == owner);
        internal void Add(AbundanceAppointmentEntryRequest request) => requests.Add(request);
        internal void Remove(AbundanceAppointmentEntryRequest request, bool abort, bool notify)
        {
            if (!requests.Remove(request)) return;
            if (request.Dialog != null && Verse.Find.WindowStack.IsOpen(request.Dialog)) request.Dialog.Close();
            if (abort && request.IssuedRestJob)
            {
                Job rest = request.RestJob();
                if (rest != null) request.Owner.jobs.EndCurrentOrQueuedJob(rest, JobCondition.InterruptForced);
            }
            if (notify && request.Owner?.Spawned == true)
                Messages.Message("RR_BedsideRoleNotReady".Translate(), request.Owner, MessageTypeDefOf.RejectInput, false);
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref requests, "rrAbundanceAppointmentEntries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && requests == null) requests = new List<AbundanceAppointmentEntryRequest>();
        }
        public override void MapComponentUpdate()
        {
            if (Scribe.mode != LoadSaveMode.Inactive || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            foreach (var request in requests.ToArray())
            {
                if (request == null) { requests.Remove(null); continue; }
                if (request.Started) { Remove(request, false, false); continue; }
                if (!request.Valid()) { Remove(request, true, true); continue; }
                if (request.DialogOpened)
                {
                    if (!Verse.Find.WindowStack.IsOpen(request.Dialog)) Remove(request, true, false);
                    continue;
                }
                if (!request.Settled) continue;
                string blocked = request.Ritual.behavior.CanStartRitualNow(request.Bed, request.Ritual, request.SelectedPawn, request.ForcedForRole);
                if (!blocked.NullOrEmpty())
                { Remove(request, true, false); Messages.Message(blocked, request.Owner, MessageTypeDefOf.RejectInput, false); continue; }
                try
                {
                    request.OpeningDialog = true;
                    Window dialog = request.Ritual.GetRitualBeginWindow(request.Bed, request.Obligation, request.OnConfirm,
                        request.Organizer, request.ForcedForRole, request.SelectedPawn);
                    if (dialog == null) { Remove(request, true, true); continue; }
                    request.Dialog = dialog; request.DialogOpened = true; Verse.Find.WindowStack.Add(dialog);
                }
                catch (Exception error)
                { Remove(request, true, true); Log.Error("[RimRound] Could not open native bedside role appointment: " + error); }
                finally { request.OpeningDialog = false; }
            }
        }
        public override void MapRemoved()
        {
            foreach (var request in requests.ToArray()) if (request != null) Remove(request, true, false);
            requests.Clear(); base.MapRemoved();
        }
    }

    public sealed class GameComponent_AbundanceRoleAppointment : GameComponent
    {
        int last = -1;
        public GameComponent_AbundanceRoleAppointment(Game game) { }
        public override void GameComponentUpdate()
        {
            if (Scribe.mode != LoadSaveMode.Inactive || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            int now = Find.TickManager.TicksGame;
            if (last >= 0 && now >= last && now - last < 250) return;
            last = now; AbundanceRoleAppointment.InstallAdapters();
        }
    }
    public sealed class RitualRole_AbundanceAppointmentHost : RitualRole
    {
        public RitualRole_AbundanceAppointmentHost()
        {
            label = "bed host"; id = AbundanceRoleAppointment.Host; maxCount = 1;
            allowDowned = true; allowKeepLayingDown = true; endJobOnRitualCleanup = false;
            allowChild = false; countsAsParticipant = true;
        }
        public override bool AppliesToPawn(Pawn p, out string reason, TargetInfo target, LordJob_Ritual ritual = null,
            RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            reason = null;
            if (!AppliesIfChild(p, out reason, skipReason)) return false;
            bool valid = target.Thing is Building_Bed bed && AbundanceBeds.Focus(bed)?.Owner == p &&
                AbundanceRoleAppointment.Resting(p, bed) && assignments?.FirstAssignedPawn(AbundanceRoleAppointment.Changer) != p;
            if (!valid && !skipReason) reason = "RR_BedsideRoleNotReady".Translate();
            return valid;
        }
        public override bool AppliesToRole(Precept_Role role, out string reason, Precept_Ritual ritual = null, Pawn pawn = null, bool skipReason = false)
        { reason = null; return false; }
    }
    public sealed class RitualTarget_AbundanceRoleAppointment : RitualObligationTargetFilter
    {
        public RitualObligationTargetFilter Original;
        protected override RitualTargetUseReport CanUseTargetInternal(TargetInfo target, RitualObligation obligation)
        {
            if (target.Thing is Building_Bed bed && bed.def.defName == "NotRegalBed")
                return AbundanceRoleAppointment.BedValid(bed, parent) ? (RitualTargetUseReport)true : "RR_BedsideRitualSite".Translate().ToString();
            return Original.CanUseTarget(target, obligation);
        }
        public override IEnumerable<TargetInfo> GetTargets(RitualObligation obligation, Map map)
        {
            foreach (TargetInfo target in Original.GetTargets(obligation, map)) yield return target;
            foreach (var bed in map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>())
                if (AbundanceRoleAppointment.BedValid(bed, parent) && CanUseTarget(bed, obligation).canUse) yield return bed;
        }
        public override IEnumerable<string> GetTargetInfos(RitualObligation obligation)
        {
            foreach (string info in Original.GetTargetInfos(obligation)) yield return info;
            if (AbundanceRoleAppointment.Supported(parent)) yield return "RR_AbundanceBedTarget".Translate();
        }
        public override IEnumerable<string> GetBlockingIssues(TargetInfo target, RitualRoleAssignments assignments)
        {
            if (AbundanceRoleAppointment.BedScope(parent, target))
            {
                string problem = AbundanceRoleAppointment.Problem(parent, (Building_Bed)target.Thing, assignments);
                if (!problem.NullOrEmpty()) yield return problem;
            }
            foreach (string issue in Original.GetBlockingIssues(target, assignments)) yield return issue;
        }
        public override List<string> MissingTargetBuilding(Ideo ideo) => Find.Maps.Any(m =>
            m.listerBuildings.allBuildingsColonist.OfType<Building_Bed>().Any(b => AbundanceRoleAppointment.BedValid(b, parent))) ? null : Original.MissingTargetBuilding(ideo);
        public override bool ObligationTargetsValid(RitualObligation obligation) => Original.ObligationTargetsValid(obligation);
        public override string LabelExtraPart(RitualObligation obligation) => Original.LabelExtraPart(obligation);
        public override bool ShouldGrayOut(Pawn pawn, ILordJobAssignmentsManager<RitualRole> assignments, out TaggedString reason) => Original.ShouldGrayOut(pawn, assignments, out reason);
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Deep.Look(ref Original, "rrOriginalRoleAppointmentTarget");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Original != null) Original.parent = parent;
        }
    }

    public sealed class RitualBehaviorWorker_AbundanceRoleAppointment : RitualBehaviorWorker
    {
        AbundanceRoleAppointmentState pending;
        public RitualBehaviorWorker_AbundanceRoleAppointment() { }
        public RitualBehaviorWorker_AbundanceRoleAppointment(RitualBehaviorDef def) : base(def) { }
        public override string CanStartRitualNow(TargetInfo target, Precept_Ritual ritual, Pawn selectedPawn = null, Dictionary<string, Pawn> forcedForRole = null)
        {
            if (AbundanceRoleAppointment.BedScope(ritual, target) && !AbundanceRoleAppointment.BedValid((Building_Bed)target.Thing, ritual))
                return "RR_BedsideRitualSite".Translate();
            return base.CanStartRitualNow(target, ritual, selectedPawn, forcedForRole);
        }
        public override bool PawnCanFillRole(Pawn pawn, RitualRole role, out string reason, TargetInfo target)
        {
            reason = null;
            if (target.Thing is Building_Bed bed && bed.def.defName == "NotRegalBed" && role?.id == AbundanceRoleAppointment.Changer)
            {
                var ritual = AbundanceBeds.Focus(bed)?.Ideology?.PreceptsListForReading.OfType<Precept_Ritual>().FirstOrDefault(r => r.def.defName == "RoleChange");
                if (ritual == null || !AbundanceRoleAppointment.CandidateReady(pawn, bed, ritual))
                { reason = "RR_BedsideRoleNotReady".Translate(); return false; }
            }
            return base.PawnCanFillRole(pawn, role, out reason, target);
        }
        public override void TryExecuteOn(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments, bool playerForced = false)
        {
            if (!AbundanceRoleAppointment.BedScope(ritual, target))
            { base.TryExecuteOn(target, organizer, ritual, obligation, assignments, playerForced); return; }
            var bed = (Building_Bed)target.Thing;
            AbundanceRoleAppointment.AdaptAssignments(assignments);
            string problem = CanStartRitualNow(target, ritual, null, assignments.ForcedRolesForReading) ??
                AbundanceRoleAppointment.Problem(ritual, bed, assignments);
            if (problem.NullOrEmpty() && !CanExecuteOn(target, obligation)) problem = "RR_BedsideRitualSite".Translate();
            if (problem.NullOrEmpty())
                problem = ritual.outcomeEffect.BlockingIssues(ritual, target, assignments).FirstOrDefault();
            if (problem.NullOrEmpty())
                foreach (Pawn pawn in assignments.Participants)
                {
                    problem = assignments.PawnNotAssignableReason(pawn, assignments.RoleForPawn(pawn));
                    if (!problem.NullOrEmpty()) break;
                }
            if (!problem.NullOrEmpty()) { Messages.Message(problem, target, MessageTypeDefOf.RejectInput, false); return; }
            pending = new AbundanceRoleAppointmentState(bed, ritual, assignments);
            if (!pending.Reserve())
            { pending.Release(bed.Map); pending = null; Messages.Message("RR_BedsideRoleBlocked".Translate(), target, MessageTypeDefOf.RejectInput, false); return; }
            AbundanceRoleAppointment.ConfigureRestRole(assignments, pending.Rests.Any(r => r.Pawn == pending.Candidate));
            try { base.TryExecuteOn(target, null, ritual, obligation, assignments, playerForced); }
            finally
            {
                if (!(pending.Candidate.GetLord()?.LordJob is LordJob_AbundanceRoleAppointment lord && lord.State == pending)) pending.Release(bed.Map);
                pending = null;
            }
        }
        protected override LordJob CreateLordJob(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments) =>
            pending == null ? base.CreateLordJob(target, organizer, ritual, obligation, assignments) :
                new LordJob_AbundanceRoleAppointment(target, ritual, obligation, assignments, pending);
    }

    public sealed class AbundanceAppointmentRest : IExposable
    {
        public Pawn Pawn;
        public int JobId;
        public IntVec3 Position;
        public bool Unchanged(Building_Bed bed) => Pawn?.Spawned == true && Pawn.CurJob?.loadID == JobId &&
            Pawn.Position == Position && AbundanceRoleAppointment.Resting(Pawn, bed);
        public void ExposeData()
        { Scribe_References.Look(ref Pawn, "pawn"); Scribe_Values.Look(ref JobId, "jobId"); Scribe_Values.Look(ref Position, "position"); }
    }
    public sealed class AbundanceRoleAppointmentState : IExposable
    {
        public Pawn Candidate, Owner, ReplacedHolder;
        public Precept_Role OriginalRole, OwnerRole, SelectedRole;
        public Ideo Ideology;
        public Building_Bed Bed;
        public IntVec3 BedPosition;
        public Rot4 BedRotation;
        public AbundanceBedUse Use;
        public Job ReservationJob;
        public List<AbundanceAppointmentRest> Rests = new List<AbundanceAppointmentRest>();
        public int CreatedTick, LastTick = -1, SpeechTicks;
        public bool Released;
        public AbundanceRoleAppointmentState() { }
        public AbundanceRoleAppointmentState(Building_Bed bed, Precept_Ritual ritual, RitualRoleAssignments assignments)
        {
            Candidate = assignments.FirstAssignedPawn(AbundanceRoleAppointment.Changer); Owner = AbundanceBeds.Focus(bed).Owner;
            OriginalRole = Candidate.Ideo.GetRole(Candidate); OwnerRole = Owner.Ideo.GetRole(Owner);
            SelectedRole = assignments.RoleChangeSelection; ReplacedHolder = (SelectedRole as Precept_RoleSingle)?.ChosenPawnSingle();
            Ideology = ritual.ideo; Bed = bed; BedPosition = bed.Position; BedRotation = bed.Rotation; Use = AbundanceBeds.Focus(bed).Use;
            CreatedTick = Find.TickManager.TicksGame; ReservationJob = JobMaker.MakeJob(BedsideRoleAbilities.SessionJob, Candidate);
            foreach (Pawn pawn in assignments.Participants.Where(p => AbundanceRoleAppointment.Resting(p, bed)))
                Rests.Add(new AbundanceAppointmentRest { Pawn = pawn, JobId = pawn.CurJob.loadID, Position = pawn.Position });
        }
        public void ExposeData()
        {
            Scribe_References.Look(ref Candidate, "candidate"); Scribe_References.Look(ref Owner, "owner"); Scribe_References.Look(ref ReplacedHolder, "replacedHolder");
            Scribe_References.Look(ref OriginalRole, "originalRole"); Scribe_References.Look(ref OwnerRole, "ownerRole"); Scribe_References.Look(ref SelectedRole, "selectedRole");
            Scribe_References.Look(ref Ideology, "ideology"); Scribe_References.Look(ref Bed, "bed");
            Scribe_Values.Look(ref BedPosition, "bedPosition"); Scribe_Values.Look(ref BedRotation, "bedRotation"); Scribe_Values.Look(ref Use, "bedUse");
            Scribe_Deep.Look(ref ReservationJob, "reservationJob"); Scribe_Collections.Look(ref Rests, "rests", LookMode.Deep);
            Scribe_Values.Look(ref CreatedTick, "createdTick"); Scribe_Values.Look(ref LastTick, "lastTick", -1);
            Scribe_Values.Look(ref SpeechTicks, "speechTicks"); Scribe_Values.Look(ref Released, "released");
        }
        internal bool Reserve()
        {
            foreach (var rest in Rests)
                if (!rest.Pawn.CanReserve(rest.Pawn) || !Bed.Map.reservationManager.Reserve(rest.Pawn, ReservationJob, rest.Pawn, errorOnFailed: false)) return false;
            return true;
        }
        internal void Release(Map map)
        {
            if (Released) return;
            Released = true;
            if (ReservationJob != null)
                foreach (var rest in Rests)
                    if (rest.Pawn != null) map?.reservationManager.ReleaseClaimedBy(rest.Pawn, ReservationJob);
        }
        public bool Valid(LordJob_AbundanceRoleAppointment lord)
        {
            int now = Find.TickManager.TicksGame;
            if (Released || !AbundanceRoleAppointment.BedValid(Bed, lord.Ritual) || Bed.Position != BedPosition || Bed.Rotation != BedRotation ||
                AbundanceBeds.Focus(Bed).Owner != Owner || AbundanceBeds.Focus(Bed).Use != Use ||
                Candidate?.Spawned != true || Candidate.Map != Bed.Map || Candidate.GetLord() != lord.lord || Candidate.Ideo != Ideology || Owner.Ideo != Ideology ||
                Ideology.GetRole(Candidate) != OriginalRole || Ideology.GetRole(Owner) != OwnerRole ||
                lord.assignments.FirstAssignedPawn(AbundanceRoleAppointment.Changer) != Candidate || lord.assignments.RoleChangeSelection != SelectedRole ||
                (SelectedRole != null && !AbundanceRoleAppointment.EligibleSelection(Candidate, SelectedRole)) ||
                (SelectedRole is Precept_RoleSingle single && single.ChosenPawnSingle() != ReplacedHolder) ||
                !AbundanceRoleAppointment.CandidateReady(Candidate, Bed, lord.Ritual) || now < CreatedTick || now - CreatedTick > 15000) return false;
            bool retainedCandidate = Rests.Any(r => r.Pawn == Candidate);
            if (!retainedCandidate && BedsideRoleAbilities.Immobile(Candidate)) return false;
            if (lord.StageIndex > 0 && !AbundanceRoleAppointment.Local(Candidate, Bed)) return false;
            if (lord.StageIndex == 0 && now - CreatedTick > 4000) return false;
            foreach (var rest in Rests)
                if (!rest.Unchanged(Bed) || rest.Pawn.GetLord() != lord.lord || !lord.assignments.Participants.Contains(rest.Pawn) ||
                    !Bed.Map.reservationManager.ReservedBy(rest.Pawn, rest.Pawn, ReservationJob) ||
                    Bed.Map.physicalInteractionReservationManager.IsReserved(rest.Pawn)) return false;
            return true;
        }
        internal void Tick(LordJob_AbundanceRoleAppointment lord)
        {
            int now = Find.TickManager.TicksGame;
            if (LastTick == now) return;
            if (LastTick >= 0 && now != LastTick + 1) { lord.Cancel(); return; }
            LastTick = now;
            if (!Rests.Any(r => r.Pawn == Candidate)) return;
            lord.AddTagForPawn(Candidate, StageEndTrigger_RolesArrived.ArrivalTag);
            if (lord.StageIndex == 0) return;
            Candidate.skills?.Learn(SkillDefOf.Social, .3f);
            if (++SpeechTicks < 480) return;
            SpeechTicks = 0; MoteMaker.MakeSpeechBubble(Candidate, JobDriver_AcceptRole.moteIcon);
            SocialInteractionUtility.ImitateSocialInteractionWithManyPawns(Candidate,
                lord.lord.ownedPawns.Where(p => p != Candidate && AbundanceRoleAppointment.Local(p, Bed)).ToList(),
                SelectedRole != null ? InteractionDefOf.Speech_AcceptRole : InteractionDefOf.Speech_RemoveRole);
        }
        internal List<RitualStage> Stages(Precept_Ritual ritual)
        {
            var stages = new List<RitualStage>();
            foreach (RitualStage original in ritual.behavior.def.stages)
            {
                RitualStage stage = AbundanceRoleAppointment.Clone(original);
                AccessTools.Field(typeof(RitualStage), "behaviorForRole").SetValue(stage, new Dictionary<string, RitualRoleBehavior>());
                stage.roleBehaviors = original.roleBehaviors?.Select(AbundanceRoleAppointment.Clone).ToList() ?? new List<RitualRoleBehavior>();
                stage.failTriggers = original.failTriggers?.Select(AbundanceRoleAppointment.Clone).ToList();
                stage.endTriggers = original.endTriggers?.Select(AbundanceRoleAppointment.Clone).ToList();
                foreach (var rest in Rests)
                {
                    string id = rest.Pawn == Candidate ? AbundanceRoleAppointment.Changer : AbundanceRoleAppointment.Host;
                    var behavior = stage.roleBehaviors.FirstOrDefault(b => b.roleId == id);
                    if (behavior == null) { behavior = new RitualRoleBehavior { roleId = id }; stage.roleBehaviors.Add(behavior); }
                    behavior.dutyDef = DefDatabase<DutyDef>.GetNamed(AbundanceRoleAppointment.RestDuty);
                    AccessTools.Field(typeof(RitualRoleBehavior), "customPositions").SetValue(behavior,
                        new List<RitualPosition> { new RitualPosition_AbundanceAppointmentRest() });
                }
                stages.Add(stage);
            }
            return stages;
        }
    }
    public sealed class RitualPosition_AbundanceAppointmentRest : RitualPosition
    {
        public override PawnStagePosition GetCell(IntVec3 spot, Pawn pawn, LordJob_Ritual ritual) =>
            new PawnStagePosition(((LordJob_AbundanceRoleAppointment)ritual).State.Rests.First(r => r.Pawn == pawn).Position, null, Rot4.Invalid, highlight);
    }
    public sealed class LordJob_AbundanceRoleAppointment : LordJob_Ritual
    {
        AbundanceRoleAppointmentState state;
        public AbundanceRoleAppointmentState State => state;
        public LordJob_AbundanceRoleAppointment() { }
        public LordJob_AbundanceRoleAppointment(TargetInfo target, Precept_Ritual ritual, RitualObligation obligation,
            RitualRoleAssignments assignments, AbundanceRoleAppointmentState state)
            : base(target, ritual, obligation, state.Stages(ritual), assignments, null, state.Bed.GetSleepingSlotPos(0)) { this.state = state; }
        protected override bool ShouldBeCalledOff() => !state.Valid(this) || base.ShouldBeCalledOff();
        public override bool EndPawnJobOnCleanup(Pawn p) => state.Rests.Any(r => r.Pawn == p) ? false : base.EndPawnJobOnCleanup(p);
        public override IEnumerable<Pawn> PawnsToCountTowardsPresence => base.PawnsToCountTowardsPresence.Where(p => AbundanceRoleAppointment.Local(p, state.Bed));
        public override void LordJobTick()
        {
            if (!state.Valid(this)) { Cancel(); return; }
            state.Tick(this); if (!state.Released) base.LordJobTick();
        }
        public override void ApplyOutcome(float progress, bool showFinishedMessage = true, bool showFailedMessage = true, bool cancelled = false)
        {
            if (!cancelled && !state.Valid(this)) { progress = 0; cancelled = true; }
            base.ApplyOutcome(progress, showFinishedMessage, showFailedMessage, cancelled);
        }
        public override void Cleanup() { state.Release(Map); base.Cleanup(); }
        public override void ExposeData() { base.ExposeData(); Scribe_Deep.Look(ref state, "rrAbundanceRoleAppointment"); }
    }

    internal static class AbundanceRoleAppointmentPatches
    {
        [HarmonyPatch(typeof(SocialCardUtility), nameof(SocialCardUtility.DrawPawnRoleSelection))]
        static class SocialRoleButtonLabel
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, "ChooseRole"))
                        instruction.operand = "RR_PlanRoleChange";
                    yield return instruction;
                }
            }
        }

        [HarmonyPatch(typeof(RitualUtility), nameof(RitualUtility.RoleChangeConfirmation))]
        static class ConfirmAppointment
        {
            static void Postfix(ref string __result)
            {
                // In 1.6 an empty explanation makes the native role picker call Assign
                // immediately. Its confirmation path only changes the pending selection.
                if (__result.NullOrEmpty()) __result = "RR_RoleTakesEffectAfterCeremony".Translate();
            }
        }

        [HarmonyPatch(typeof(Precept_Ritual), nameof(Precept_Ritual.GetRitualBeginWindow))]
        static class PrepareBedOwner
        {
            static bool Prefix(Precept_Ritual __instance, TargetInfo targetInfo, RitualObligation obligation, Action onConfirm,
                Pawn organizer, Dictionary<string, Pawn> forcedForRole, Pawn selectedPawn, ref Window __result)
            {
                if (!AbundanceRoleAppointment.BedScope(__instance, targetInfo)) return true;
                var bed = (Building_Bed)targetInfo.Thing;
                Pawn owner = AbundanceBeds.Focus(bed)?.Owner;
                var pending = AbundanceAppointmentEntry.Requests(owner)?.Find(owner);
                if (pending?.OpeningDialog == true && pending.Ritual == __instance && pending.Bed == bed &&
                    pending.Valid() && pending.Settled) return true;
                if (pending == null && AbundanceRoleAppointment.Resting(owner, bed)) return true;
                __result = null;
                if (!AbundanceAppointmentEntry.TryOrder(__instance, bed, obligation, onConfirm, organizer, forcedForRole, selectedPawn, out string reason))
                    Messages.Message(reason, targetInfo, MessageTypeDefOf.RejectInput, false);
                return false;
            }
        }
        [HarmonyPatch]
        static class ValidateAdaptedRole
        {
            static MethodBase TargetMethod() => AccessTools.Method(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.PawnNotAssignableReason),
                new[] { typeof(Pawn), typeof(RitualRole), typeof(Precept_Ritual), typeof(RitualRoleAssignments), typeof(TargetInfo), typeof(bool).MakeByRefType() });
            static void Prefix(ref RitualRole role, Precept_Ritual ritual, RitualRoleAssignments assignments, TargetInfo ritualTarget)
            {
                // Native BlockingIssues passes def.roles rather than this
                // dialog's cloned role. Run every native check on the latter.
                if (role?.id != AbundanceRoleAppointment.Changer || assignments == null ||
                    !AbundanceRoleAppointment.BedScope(ritual, ritualTarget)) return;
                RitualRole local = assignments.AllRolesForReading?.FirstOrDefault(r => r.id == AbundanceRoleAppointment.Changer);
                if (local != null) role = local;
            }
        }
        [HarmonyPatch(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.Setup))]
        static class SetupAssignments
        {
            static void Prefix(RitualRoleAssignments __instance, ref Pawn selectedPawn)
            {
                TargetInfo target = (TargetInfo)AbundanceRoleAppointment.AssignmentTarget.GetValue(__instance);
                if (selectedPawn == null && AbundanceRoleAppointment.BedScope(__instance.Ritual, target))
                    selectedPawn = AbundanceBeds.Focus((Building_Bed)target.Thing)?.Owner;
            }
            static void Postfix(RitualRoleAssignments __instance) => AbundanceRoleAppointment.PrepareCandidates(__instance);
        }
        [HarmonyPatch(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.ExposeData))]
        static class LoadAssignments
        { static void Postfix(RitualRoleAssignments __instance) { if (Scribe.mode == LoadSaveMode.PostLoadInit) AbundanceRoleAppointment.AdaptAssignments(__instance); } }
        [HarmonyPatch(typeof(CompAbundanceBed), nameof(CompAbundanceBed.CompGetGizmosExtra))]
        static class BedGizmos
        {
            static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, CompAbundanceBed __instance)
            {
                foreach (Gizmo gizmo in __result) yield return gizmo;
                if (!__instance.Designated || __instance.Bed?.Faction != Faction.OfPlayer) yield break;
                foreach (var ritual in __instance.Ideology.PreceptsListForReading.OfType<Precept_Ritual>().Where(r =>
                    AbundanceRoleAppointment.Supported(r) && r.behavior is RitualBehaviorWorker_AbundanceRoleAppointment &&
                    r.obligationTargetFilter is RitualTarget_AbundanceRoleAppointment))
                    foreach (Gizmo gizmo in ritual.GetGizmoFor(__instance.Bed)) yield return gizmo;
            }
        }
        [HarmonyPatch(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.UpdateRoleChangeTargetRole))]
        static class ProspectiveOwnerRole
        {
            static void Postfix(RitualRoleAssignments __instance, Pawn pawn)
            {
                TargetInfo target = (TargetInfo)AbundanceRoleAppointment.AssignmentTarget.GetValue(__instance);
                if (!AbundanceRoleAppointment.BedScope(__instance.Ritual, target) || pawn?.Ideo?.GetRole(pawn) != null) return;
                var focus = AbundanceBeds.Focus((Building_Bed)target.Thing);
                if (focus.Owner == pawn)
                    __instance.SetRoleChangeSelection(RitualUtility.AllRolesForPawn(pawn).FirstOrDefault(r =>
                        AbundanceRoleAppointment.MatchesUse(r, focus.Use) && AbundanceRoleAppointment.EligibleSelection(pawn, r)));
            }
        }
        [HarmonyPatch(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.TryAssign))]
        static class IncludeRestingHost
        {
            static void Postfix(RitualRoleAssignments __instance, RitualRole role, bool __result)
            {
                if (!__result || role?.id != AbundanceRoleAppointment.Changer) return;
                TargetInfo target = (TargetInfo)AbundanceRoleAppointment.AssignmentTarget.GetValue(__instance);
                if (!AbundanceRoleAppointment.BedScope(__instance.Ritual, target)) return;
                var bed = (Building_Bed)target.Thing;
                Pawn owner = AbundanceBeds.Focus(bed)?.Owner;
                if (AbundanceRoleAppointment.Resting(owner, bed) && __instance.FirstAssignedPawn(AbundanceRoleAppointment.Changer) != owner)
                {
                    var hostRole = __instance.AllRolesForReading.First(r => r.id == AbundanceRoleAppointment.Host);
                    __instance.TryAssign(owner, hostRole, out _);
                }
            }
        }
        [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.CheckForJobOverride))]
        static class KeepRest
        { static bool Prefix(Pawn ___pawn) => !AbundanceRoleAppointment.RetainJob(___pawn) && !AbundanceAppointmentEntry.KeepRestJob(___pawn); }
        [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.CanFallAsleep))]
        static class StayAwake
        {
            static void Postfix(Pawn pawn, ref bool __result)
            {
                if (__result && pawn?.jobs?.curDriver?.asleep == false && pawn.CurJob?.forceSleep != true &&
                    pawn.needs?.rest?.CurCategory != RestCategory.Exhausted &&
                    (AbundanceRoleAppointment.RetainJob(pawn) || AbundanceAppointmentEntry.KeepRestJob(pawn))) __result = false;
            }
        }
        [HarmonyPatch(typeof(Dialog_BeginRitual), "DrawRoleSelection")]
        static class SelectOnly
        {
            static bool Prefix(Dialog_BeginRitual __instance, Pawn pawn, Rect rect)
            {
                if (!AbundanceRoleAppointment.DialogScope(__instance)) return true;
                var assignments = (RitualRoleAssignments)AbundanceRoleAppointment.DialogAssignments.GetValue(__instance);
                var bed = (Building_Bed)((TargetInfo)AbundanceRoleAppointment.DialogTarget.GetValue(__instance)).Thing;
                Precept_Role current = pawn.Ideo.GetRole(pawn), selected = assignments.RoleChangeSelection;
                if (current == null && selected == null)
                {
                    selected = RitualUtility.AllRolesForPawn(pawn).FirstOrDefault(r => AbundanceRoleAppointment.EligibleSelection(pawn, r) &&
                        (AbundanceBeds.Focus(bed).Owner != pawn || AbundanceRoleAppointment.MatchesUse(r, AbundanceBeds.Focus(bed).Use)));
                    __instance.SetRoleToChangeTo(selected);
                }
                if (selected != null || current != null)
                    SocialCardUtility.DrawPawnRole(pawn, selected, selected != null ? selected.LabelCap : "RemoveRole".Translate(current.Label).Resolve(), rect, drawLine: false);
                Rect button = new Rect(rect.x + 220f, rect.y + 2f, 140f, 32f);
                if (!Widgets.ButtonText(button, "ChooseNewRole".Translate() + "...")) return false;
                var choices = new List<FloatMenuOption>();
                if (current != null) choices.Add(new FloatMenuOption("None".Translate(), () => Select(null)));
                foreach (var role in RitualUtility.AllRolesForPawn(pawn))
                {
                    Precept_Role next = role;
                    if (next == current || !AbundanceRoleAppointment.EligibleSelection(pawn, next) ||
                        (current == null && AbundanceBeds.Focus(bed).Owner == pawn && !AbundanceRoleAppointment.MatchesUse(next, AbundanceBeds.Focus(bed).Use))) continue;
                    choices.Add(new FloatMenuOption(next.LabelForPawn(pawn) + " (" + next.def.label + ")", () => Select(next), next.Icon, next.ideo.Color));
                }
                if (choices.Count > 0) Find.WindowStack.Add(new FloatMenu(choices));
                return false;
                void Select(Precept_Role next)
                {
                    __instance.SetRoleToChangeTo(next);
                    string confirm = next == null ? "ChooseRoleConfirmUnassign".Translate(current.Named("ROLE"), pawn.Named("PAWN")).ToString() :
                        "ChooseRoleConfirmAssign".Translate(next.Named("ROLE"), pawn.Named("PAWN")).ToString();
                    if (next != null)
                    {
                        string extra = RitualUtility.RoleChangeConfirmation(pawn, current, next);
                        if (!extra.NullOrEmpty()) confirm += "\n\n" + extra;
                    }
                    AccessTools.Field(typeof(Dialog_BeginRitual), "confirmText").SetValue(__instance,
                        confirm + "\n\n" + "ChooseRoleConfirmAssignPostfix".Translate());
                }
            }
        }
    }
}
