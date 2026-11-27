using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    // The native LayDown job owns movement, bed reservations and posture. This
    // request only waits for that exact job before opening the native dialog.
    public static class AbundanceBedEntry
    {
        internal const int MaximumPreparationTicks = 5000;
        internal static MapComponent_AbundanceBedEntries Requests(Pawn pawn) =>
            pawn?.Map?.GetComponent<MapComponent_AbundanceBedEntries>();
        internal static bool Candidate(Ability ability) => BedsideRituals.Supported(ability) &&
            !BedsideRoleAbilities.Immobile(ability.pawn) && AbundanceBeds.For(ability.pawn) != null;
        public static bool HasPending(Pawn pawn) => Requests(pawn)?.Find(pawn) != null;

        internal static bool IsRoleBed(Pawn actor, Precept_Ritual ritual, TargetInfo target) =>
            BedsideRituals.Supported(ritual) && actor != null && actor == BedsideRituals.Actor(ritual) &&
            target.Thing is Building_Bed bed && bed.def.defName == "NotRegalBed" &&
            AbundanceBeds.For(actor) == bed && AbundanceBeds.Valid(bed, ritual.ideo);

        internal static bool CanUseBed(Pawn actor, Building_Bed bed)
        {
            if (!BedsideRituals.OrganizerPredicatePatched || !BedsideRoleAbilities.AwakeAndConscious(actor) ||
                bed?.def.defName != "NotRegalBed" || AbundanceBeds.For(actor) != bed ||
                !AbundanceBeds.Valid(bed, actor.Ideo) || bed.Map != actor.Map || bed.IsForbidden(actor) ||
                actor.WorkTagIsDisabled(WorkTags.Social) || actor.skills?.GetSkill(SkillDefOf.Social).TotallyDisabled != false ||
                !actor.health.capacities.CapableOf(PawnCapacityDefOf.Talking) ||
                !BedsideRoleAbilities.BedReservationsPermitSession(bed) || bed.CurOccupants.Any(p => p != actor)) return false;
            if (actor.CurrentBed() == bed && BedsideRoleAbilities.NativeRestJob(actor)) return true;
            return !BedsideRoleAbilities.Immobile(actor) &&
                actor.CanReserve(bed, bed.SleepingSlotsCount, 0) && actor.CanReach(bed, PathEndMode.OnCell, Danger.Some);
        }

        public static bool TryOrder(Ability ability, LocalTargetInfo target, out string reason)
        {
            reason = "RR_BedsideRoleNotReady".Translate();
            Pawn actor = ability?.pawn;
            Building_Bed bed = AbundanceBeds.For(actor);
            if (!Candidate(ability) || !BedsideRituals.OwnsAbility(ability) || !CanUseBed(actor, bed) ||
                actor.GetLord() != null || !ability.CanQueueCast || !ability.CanApplyOn(target) ||
                BedsideRoleAbilities.HasSession(actor) || AI.BedsideBodyDiscussion.HasSession(actor)) return false;
            if (HasPending(actor)) { reason = "RR_BedsideRoleBusy".Translate(); return false; }
            if (ability.GizmoDisabled(out string disabled)) { reason = disabled; return false; }
            if (!BedsideRituals.TryResolveRitual(ability, target, out var ritual, out _) || target.Pawn == null ||
                target.Pawn.Map != actor.Map || !target.Pawn.Spawned) return false;

            var request = new AbundanceBedEntryRequest {
                Actor = actor, Ability = ability, OriginalVerb = ability.verb, Role = ability.sourcePrecept as Precept_RoleSingle,
                Bed = bed, BedPosition = bed.Position, BedRotation = bed.Rotation, Ritual = ritual, Target = target.Pawn,
                TargetIdeology = target.Pawn.Ideo, CreatedTick = Find.TickManager.TicksGame
            };
            // Preserve an existing ordinary rest job, including a walk already
            // ordered to this bed. Otherwise use the same order API as the UI.
            Job rest = actor.CurJob;
            bool reuse = rest?.def == JobDefOf.LayDown && rest.targetA.Thing == bed;
            if (!reuse) rest = JobMaker.MakeJob(JobDefOf.LayDown, bed);
            request.RestJobId = rest.loadID; request.IssuedRestJob = !reuse;
            var owner = Requests(actor);
            owner.Add(request);
            if (!reuse && !actor.jobs.TryTakeOrderedJob(rest, JobTag.Misc))
            {
                owner.Remove(request, true, false);
                return false;
            }
            if (request.RestJob() == null)
            {
                owner.Remove(request, true, false);
                return false;
            }
            reason = null;
            return true;
        }

        internal static bool KeepRestJob(Pawn pawn)
        {
            var request = Requests(pawn)?.Find(pawn);
            return request != null && !request.AbortAfterLoad && request.MatchesCurrentRestJob &&
                pawn.GetLord() == null && CanUseBed(pawn, request.Bed) &&
                BedsideRituals.OwnsAbility(request.Ability) && request.Ability.pawn == pawn;
        }

        internal static bool CanBegin(Pawn actor, Precept_Ritual ritual, TargetInfo target, Pawn recipient = null)
        {
            var request = Requests(actor)?.Find(actor);
            if (request == null) return true;
            return request.Ritual == ritual && request.Bed == target.Thing && request.Valid() &&
                request.MatchesCurrentRestJob && actor.CurrentBed() == request.Bed &&
                actor.GetPosture() == PawnPosture.LayingInBed &&
                (recipient == null || request.Target == recipient);
        }
    }

    public sealed class AbundanceBedEntryRequest : IExposable
    {
        public Pawn Actor, Target;
        public Ability Ability;
        public Precept_RoleSingle Role;
        public Precept_Ritual Ritual;
        public Ideo TargetIdeology;
        public Building_Bed Bed;
        public IntVec3 BedPosition;
        public Rot4 BedRotation;
        public int RestJobId, CreatedTick;
        public bool IssuedRestJob, DialogOpened;
        [Unsaved(false)] public bool AbortAfterLoad;
        [Unsaved(false)] public Verb OriginalVerb;
        [Unsaved(false)] public Window Dialog;

        public bool MatchesCurrentRestJob => Actor?.CurJobDef == JobDefOf.LayDown &&
            Actor.CurJob.loadID == RestJobId && Actor.CurJob.targetA.Thing == Bed;

        internal Job RestJob()
        {
            if (MatchesCurrentRestJob) return Actor.CurJob;
            if (Actor?.jobs?.jobQueue != null)
                for (int i = 0; i < Actor.jobs.jobQueue.Count; i++)
                {
                    Job queued = Actor.jobs.jobQueue[i].job;
                    if (queued.loadID == RestJobId && queued.def == JobDefOf.LayDown && queued.targetA.Thing == Bed) return queued;
                }
            return null;
        }

        internal bool Started => Actor?.GetLord()?.LordJob is LordJob_Ritual ritual &&
            ritual is IBedsideRitualLord bedside && ritual.Ritual == Ritual &&
            bedside.Bedside.Actor == Actor && bedside.Bedside.RestJobId == RestJobId;

        internal bool Valid()
        {
            int now = Find.TickManager.TicksGame;
            if (AbortAfterLoad || Actor?.Spawned != true || Actor.GetLord() != null ||
                Ability == null || Ability.pawn != Actor || Ability.verb != OriginalVerb || Ability.sourcePrecept != Role ||
                !BedsideRituals.OwnsAbility(Ability) || !AbundanceBedEntry.CanUseBed(Actor, Bed) ||
                Bed.Position != BedPosition || Bed.Rotation != BedRotation || RestJob() == null ||
                Target?.Spawned != true || Target.Dead || Target.Map != Actor.Map || Target.Ideo != TargetIdeology ||
                now < CreatedTick || now - CreatedTick > AbundanceBedEntry.MaximumPreparationTicks ||
                BedsideRoleAbilities.HasSession(Actor) || AI.BedsideBodyDiscussion.HasSession(Actor) ||
                !Ability.CanQueueCast || !Ability.CanApplyOn(new LocalTargetInfo(Target)) || Ability.GizmoDisabled(out _)) return false;
            return BedsideRituals.TryResolveRitual(Ability, Target, out var current, out _) && current == Ritual;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref Actor, "actor"); Scribe_References.Look(ref Target, "target");
            Scribe_References.Look(ref Ability, "ability"); Scribe_References.Look(ref Role, "role");
            Scribe_References.Look(ref Ritual, "ritual"); Scribe_References.Look(ref TargetIdeology, "targetIdeology");
            Scribe_References.Look(ref Bed, "bed"); Scribe_Values.Look(ref BedPosition, "bedPosition");
            Scribe_Values.Look(ref BedRotation, "bedRotation"); Scribe_Values.Look(ref RestJobId, "restJobId");
            Scribe_Values.Look(ref CreatedTick, "createdTick"); Scribe_Values.Look(ref IssuedRestJob, "issuedRestJob");
            Scribe_Values.Look(ref DialogOpened, "dialogOpened");
            // Windows and confirmation callbacks are not save state. Abort on
            // the main update rather than recreate a dialog or activate a cast.
            if (Scribe.mode == LoadSaveMode.PostLoadInit) AbortAfterLoad = true;
        }
    }

    public sealed class MapComponent_AbundanceBedEntries : MapComponent
    {
        private List<AbundanceBedEntryRequest> requests = new List<AbundanceBedEntryRequest>();
        public MapComponent_AbundanceBedEntries(Map map) : base(map) { }
        public IReadOnlyList<AbundanceBedEntryRequest> RequestsForReading => requests;
        internal AbundanceBedEntryRequest Find(Pawn actor) => requests.FirstOrDefault(r => r?.Actor == actor);
        internal void Add(AbundanceBedEntryRequest request) => requests.Add(request);
        internal void Remove(AbundanceBedEntryRequest request, bool abort, bool notify)
        {
            if (!requests.Remove(request)) return;
            if (request.Dialog != null && Verse.Find.WindowStack.IsOpen(request.Dialog)) request.Dialog.Close();
            if (abort && request.IssuedRestJob)
            {
                Job job = request.RestJob();
                if (job != null) request.Actor.jobs.EndCurrentOrQueuedJob(job, JobCondition.InterruptForced);
            }
            if (notify && request.Actor?.Spawned == true)
                Messages.Message("RR_BedsideRoleNotReady".Translate(), request.Actor, MessageTypeDefOf.RejectInput, false);
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref requests, "rrAbundanceBedEntries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && requests == null) requests = new List<AbundanceBedEntryRequest>();
        }
        public override void MapComponentUpdate()
        {
            if (Scribe.mode != LoadSaveMode.Inactive || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            foreach (var request in requests.ToArray())
            {
                if (request == null) { requests.Remove(null); continue; }
                // Successful native execution transfers lifetime ownership to
                // the bedside Lord and retains the very same LayDown job.
                if (request.Started) { Remove(request, false, false); continue; }
                if (!request.Valid()) { Remove(request, true, true); continue; }
                if (request.DialogOpened)
                {
                    if (!Verse.Find.WindowStack.IsOpen(request.Dialog)) Remove(request, true, false);
                    continue;
                }
                if (!request.MatchesCurrentRestJob || request.Actor.CurrentBed() != request.Bed ||
                    request.Actor.GetPosture() != PawnPosture.LayingInBed) continue;
                if (!BedsideRituals.TryOpenDialog(request.Ability, request.Target, out string reason, out Window window))
                {
                    Remove(request, true, false);
                    Messages.Message(reason ?? "RR_BedsideRoleNotReady".Translate().ToString(), request.Actor, MessageTypeDefOf.RejectInput, false);
                    continue;
                }
                request.Dialog = window; request.DialogOpened = true;
            }
        }
        public override void MapRemoved()
        {
            foreach (var request in requests.ToArray()) if (request != null) Remove(request, true, false);
            requests.Clear(); base.MapRemoved();
        }
    }

    // Preserve every native non-bed rule and saved filter. The exact current
    // leader/guide bed is the only extra site, including obligation queries.
    public sealed class RitualObligationTargetFilter_Bedside : RitualObligationTargetFilter
    {
        public RitualObligationTargetFilter Original;
        private Building_Bed Bed => AbundanceBeds.For(BedsideRituals.Actor(parent));
        protected override RitualTargetUseReport CanUseTargetInternal(TargetInfo target, RitualObligation obligation)
        {
            if (target.Thing is Building_Bed)
                return AbundanceBedEntry.IsRoleBed(BedsideRituals.Actor(parent), parent, target);
            return Original.CanUseTarget(target, obligation);
        }
        public override IEnumerable<TargetInfo> GetTargets(RitualObligation obligation, Map map)
        {
            foreach (TargetInfo target in Original.GetTargets(obligation, map)) yield return target;
            Building_Bed bed = Bed;
            if (bed?.Map == map && CanUseTarget(bed, obligation).canUse) yield return bed;
        }
        public override IEnumerable<string> GetTargetInfos(RitualObligation obligation)
        {
            foreach (string info in Original.GetTargetInfos(obligation)) yield return info;
            if (Bed is Building_Bed bed && AbundanceBeds.Valid(bed, parent.ideo)) yield return bed.LabelCap;
        }
        public override List<string> MissingTargetBuilding(Ideo ideo) =>
            Bed is Building_Bed bed && AbundanceBeds.Valid(bed, ideo) ? null : Original.MissingTargetBuilding(ideo);
        public override IEnumerable<string> GetBlockingIssues(TargetInfo target, RitualRoleAssignments assignments)
        {
            if (target.Thing is Building_Bed)
            {
                Pawn actor = assignments.FirstAssignedPawn(BedsideRituals.ActorRoleId(parent));
                Pawn recipient = assignments.FirstAssignedPawn(parent.def.defName == "Conversion" ? "convertee" : "convict");
                if (!AbundanceBedEntry.IsRoleBed(actor, parent, target) || !BedsideRituals.Ready(actor) ||
                    !AbundanceBedEntry.CanBegin(actor, parent, target, recipient))
                    yield return "RR_BedsideRoleNotReady".Translate();
            }
            foreach (string issue in Original.GetBlockingIssues(target, assignments)) yield return issue;
        }
        public override bool ObligationTargetsValid(RitualObligation obligation) => Original.ObligationTargetsValid(obligation);
        public override string LabelExtraPart(RitualObligation obligation) => Original.LabelExtraPart(obligation);
        public override bool ShouldGrayOut(Pawn pawn, ILordJobAssignmentsManager<RitualRole> assignments, out TaggedString reason) =>
            Original.ShouldGrayOut(pawn, assignments, out reason);
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Deep.Look(ref Original, "rrOriginalObligationTargetFilter");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Original != null) Original.parent = parent;
        }
    }
}
