using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public enum MobilityAidSource { Ground, HelperInventory, RecipientInventory, Worn }
    public sealed class JobPlan
    {
        public Apparel aid;
        public Pawn patient;
        public IntVec3 bedside;
        public MobilityAidSource source;
    }
    public static class MobilityAidJobs
    {
        public const string JobName = "RR_AssistMobilityAid";
        public static bool HasActiveJob(Pawn patient) => patient?.Map != null && patient.Map.reservationManager.ReservationsReadOnly
            .Any(r => r.Target.Thing == patient && r.Job?.def?.defName == JobName);
        public static bool HelperEligible(Pawn helper, Pawn patient, out string reason)
        {
            reason = "Choose a free colonist helper who can move and manipulate.";
            if (helper == null || helper == patient || !helper.Spawned || helper.Dead || helper.Downed || helper.Drafted ||
                helper.InMentalState || helper.IsPrisoner || helper.IsSlave || helper.IsQuestLodger() || !helper.IsColonistPlayerControlled ||
                helper.carryTracker == null || !helper.health.capacities.CapableOf(PawnCapacityDefOf.Moving) ||
                !helper.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) return false;
            if (patient?.IsPrisoner == true && helper.workSettings?.WorkIsActive(WorkTypeDefOf.Warden) != true)
            { reason = "Prisoner mobility assistance requires a helper with Warden work enabled."; return false; }
            if (helper.CurJob?.def == JobDefOf.Rescue || helper.CurJob?.def == JobDefOf.TendPatient || helper.CurJob?.def == JobDefOf.DoBill)
            { reason = "Current rescue, treatment or bill work takes priority."; return false; }
            reason = null; return true;
        }
        public static bool RecipientEligible(Pawn patient, out string reason)
        {
            MobilityAidAssignment a = MobilityAidController.CurrentController?.ForPawn(patient);
            if (!MobilityAidController.CanReceiveAssistance(a, out reason)) return false;
            if (!patient.Spawned || patient.InMentalState || patient.Drafted || !patient.InBed() || patient.CurrentBed() == null)
            { reason = "The recipient must remain in bed, undrafted and outside mental states."; return false; }
            if (patient.IsPrisoner && (patient.guest?.PrisonerIsSecure != true || !patient.Position.IsInPrisonCell(patient.Map)))
            { reason = "The prisoner must remain in actual secure colony custody."; return false; }
            if (HealthAIUtility.ShouldBeTendedNowByPlayerUrgent(patient) || HealthAIUtility.ShouldHaveSurgeryDoneNow(patient))
            { reason = "Urgent treatment or a scheduled operation takes priority."; return false; }
            return true;
        }
        public static bool SourceOf(Apparel aid, Pawn helper, Pawn patient, out MobilityAidSource source)
        {
            source = MobilityAidSource.Ground;
            if (aid?.Spawned == true && aid.Map == helper.Map) return true;
            if (helper.inventory?.innerContainer.Contains(aid) == true) { source = MobilityAidSource.HelperInventory; return true; }
            if (patient.inventory?.innerContainer.Contains(aid) == true) { source = MobilityAidSource.RecipientInventory; return true; }
            if (aid?.Wearer == patient) { source = MobilityAidSource.Worn; return true; }
            return false;
        }
        public static bool Live(Pawn helper, Pawn patient, Apparel aid, IntVec3 beside, MobilityAidSource source, bool carrying, int revision, out string reason)
        {
            if (!HelperEligible(helper, patient, out reason) || !RecipientEligible(patient, out reason)) return false;
            MobilityAidAssignment a = MobilityAidController.CurrentController.ForPawn(patient);
            if (a.revision != revision || a.request == MobilityAidRequest.None || a.RequestedAid != aid)
            { reason = "Assignment or assistance request changed or was cancelled."; return false; }
            if (helper.Map != patient.Map || aid == null || aid.Destroyed)
            { reason = "Recipient or original aid is no longer on this map."; return false; }
            Apparel old = a.request == MobilityAidRequest.Replace ? a.aid : null;
            if (!MobilityAidController.PhysicallyCompatible(patient, aid, old, out reason)) return false;
            if (CompBiocodable.IsBiocoded(aid) && !CompBiocodable.IsBiocodedFor(aid, patient))
            { reason = "The aid is biocoded for somebody else."; return false; }
            if (a.request == MobilityAidRequest.Remove || a.request == MobilityAidRequest.Replace)
            {
                if (a.aid.Wearer != patient) { reason = "The fitted aid changed before assistance could finish."; return false; }
            }
            if (a.request != MobilityAidRequest.Remove)
            {
                bool held = carrying && helper.carryTracker.CarriedThing == aid;
                if (!held)
                {
                    MobilityAidSource current;
                    if (!SourceOf(aid, helper, patient, out current) || current != source || current == MobilityAidSource.Worn)
                    { reason = "The original aid changed physical holder."; return false; }
                }
                if (aid.Wearer != null || (aid.Spawned && aid.IsForbidden(helper)))
                { reason = "The requested aid is worn by someone else or forbidden."; return false; }
            }
            Building_Bed bed = patient.CurrentBed();
            if (!beside.InBounds(helper.Map) || !beside.Standable(helper.Map) || !bed.OccupiedRect().ExpandedBy(1).Contains(beside) || bed.OccupiedRect().Contains(beside))
            { reason = "The recipient moved or the reserved bedside position is blocked."; return false; }
            if (patient.IsForbidden(helper) || beside.IsForbidden(helper))
            { reason = "The recipient or bedside is outside the helper's allowed area."; return false; }
            reason = null; return true;
        }
        public static bool TryPlan(Pawn helper, Pawn patient, bool manual, out JobPlan plan, out string reason)
        {
            plan = null;
            if (!HelperEligible(helper, patient, out reason) || !RecipientEligible(patient, out reason)) return false;
            MobilityAidAssignment a = MobilityAidController.CurrentController.ForPawn(patient);
            if (a.request == MobilityAidRequest.None) { reason = "No mobility assistance was requested."; return false; }
            if (!manual && (helper.workSettings?.WorkIsActive(WorkTypeDefOf.Doctor) != true || !MobilityAidController.CurrentController.CanTryAutomatically(a)))
            { reason = "Automatic assistance is disabled or waiting for its bounded retry."; return false; }
            Apparel aid = a.RequestedAid; MobilityAidSource source;
            if (!SourceOf(aid, helper, patient, out source)) { reason = "The real aid must be in storage or the recipient/helper inventory."; return false; }
            if (helper.carryTracker.CarriedThing != null) { reason = "The helper is already carrying something."; return false; }
            if (!helper.CanReserve(patient) || !helper.CanReserve(aid)) { reason = "The recipient or aid is reserved by another helper."; return false; }
            if (source == MobilityAidSource.Ground && !helper.CanReach(aid, PathEndMode.ClosestTouch, helper.NormalMaxDanger()))
            { reason = "The aid is not reachable."; return false; }
            IntVec3 beside = IntVec3.Invalid;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(patient.CurrentBed()).OrderBy(c => c.DistanceToSquared(helper.Position)))
                if (c.InBounds(helper.Map) && c.Standable(helper.Map) && !c.IsForbidden(helper) && helper.CanReserve(c) &&
                    helper.CanReach(c, PathEndMode.OnCell, helper.NormalMaxDanger())) { beside = c; break; }
            if (!beside.IsValid) { reason = "No reachable unreserved bedside position is available."; return false; }
            if (!Live(helper, patient, aid, beside, source, false, a.revision, out reason)) return false;
            plan = new JobPlan { aid = aid, patient = patient, bedside = beside, source = source }; return true;
        }
        public static Job MakeJob(JobPlan plan, bool manual)
        {
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(JobName), plan.aid, plan.patient, plan.bedside);
            job.count = 1; job.playerForced = manual; return job;
        }
        public static bool Start(Pawn helper, Pawn patient, out string reason)
        {
            JobPlan plan; if (!TryPlan(helper, patient, true, out plan, out reason))
            { MobilityAidAssignment a = MobilityAidController.CurrentController?.ForPawn(patient); if (a != null) a.blockedReason = reason; return false; }
            Job job = MakeJob(plan, true); helper.jobs.StartJob(job, JobCondition.InterruptForced);
            return helper.CurJob == job;
        }
        public static Command_Action HelperCommand(MobilityAidAssignment a) => new Command_Action {
            defaultLabel = "Choose helper for mobility assistance", defaultDesc = MobilityAidController.CurrentController.Status(a.pawn),
            action = () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetPawns = true, canTargetBuildings = false, canTargetLocations = false },
                target => { string reason; if (!Start(target.Pawn, a.pawn, out reason)) Messages.Message(reason ?? "The helper could not start assistance.", MessageTypeDefOf.RejectInput, false); })
        };
    }
    public sealed class WorkGiver_AssistMobilityAid : WorkGiver_Scanner
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => MobilityAidController.CurrentController?.Assignments
            .Where(a => a.pawn.Spawned && a.pawn.Map == pawn.Map && a.request != MobilityAidRequest.None && a.automaticCare)
            .Select(a => (Thing)a.pawn) ?? Enumerable.Empty<Thing>();
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        { JobPlan plan; string reason; return MobilityAidJobs.TryPlan(pawn, t as Pawn, false, out plan, out reason); }
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        { JobPlan plan; string reason; return MobilityAidJobs.TryPlan(pawn, t as Pawn, false, out plan, out reason) ? MobilityAidJobs.MakeJob(plan, false) : null; }
    }
    public sealed class JobDriver_AssistMobilityAid : JobDriver
    {
        private Apparel Aid => job.targetA.Thing as Apparel;
        private Pawn Patient => job.targetB.Pawn;
        private MobilityAidRequest request;
        private MobilityAidSource source;
        private int revision;
        private bool initialized, committed;
        private Building_Bed initialBed;
        private IntVec3 initialPatientPosition;
        private Apparel oldAid;
        private string failureReason;
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref request, "mobilityRequest"); Scribe_Values.Look(ref source, "mobilitySource");
            Scribe_Values.Look(ref revision, "mobilityRevision", 0); Scribe_Values.Look(ref initialized, "mobilityInitialized", false);
            Scribe_Values.Look(ref committed, "mobilityCommitted", false); Scribe_References.Look(ref initialBed, "mobilityBed");
            Scribe_References.Look(ref oldAid, "mobilityOldAid"); Scribe_Values.Look(ref initialPatientPosition, "mobilityPatientPosition");
            Scribe_Values.Look(ref failureReason, "mobilityFailure", null);
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            MobilityAidAssignment a = MobilityAidController.CurrentController?.ForPawn(Patient);
            if (a == null) return false;
            if (!initialized)
            {
                initialized = true; revision = a.revision; request = a.request; oldAid = a.aid;
                initialBed = Patient.CurrentBed(); initialPatientPosition = Patient.Position;
                if (!MobilityAidJobs.SourceOf(Aid, pawn, Patient, out source)) return false;
            }
            return Valid() && pawn.Reserve(Aid, job, 1, 1, null, errorOnFailed) &&
                pawn.Reserve(Patient, job, 1, -1, null, errorOnFailed) && pawn.Reserve(job.targetC, job, 1, -1, null, errorOnFailed);
        }
        private bool Valid()
        {
            if (Patient?.CurrentBed() != initialBed || Patient?.Position != initialPatientPosition)
            { failureReason = "Recipient moved from the original bedside."; return false; }
            return MobilityAidJobs.Live(pawn, Patient, Aid, job.targetC.Cell, source, true, revision, out failureReason);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(condition => {
                if (!committed && Aid != null && pawn.carryTracker?.CarriedThing == Aid)
                { Thing placed; pawn.carryTracker.TryDropCarriedThing(pawn.PositionHeld, ThingPlaceMode.Near, out placed); }
                if (!committed) MobilityAidController.CurrentController?.RecordFailure(Patient, revision, failureReason);
                // Native job tracker owns item, destination and interaction reservation cleanup.
            });
            AddFailCondition(() => !committed && !Valid());
            if (request != MobilityAidRequest.Remove)
            {
                if (source == MobilityAidSource.Ground)
                {
                    yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
                    yield return Toils_Haul.StartCarryThing(TargetIndex.A);
                }
                else
                {
                    if (source == MobilityAidSource.RecipientInventory) yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
                    Toil pickup = ToilMaker.MakeToil("RetrieveActualInventoryMobilityAid"); pickup.defaultCompleteMode = ToilCompleteMode.Instant;
                    pickup.initAction = () => {
                        Pawn owner = source == MobilityAidSource.HelperInventory ? pawn : Patient;
                        if (!Valid() || (owner != pawn && pawn.Position != job.targetC.Cell) ||
                            owner.inventory.innerContainer.TryTransferToContainer(Aid, pawn.carryTracker.innerContainer, 1) != 1)
                            EndJobWith(JobCondition.Incompletable);
                    };
                    yield return pickup;
                }
            }
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            Toil action = ToilMaker.MakeToil("AssistMobilityAidAtBedside"); action.defaultCompleteMode = ToilCompleteMode.Delay; action.defaultDuration = 180;
            action.WithProgressBarToilDelay(TargetIndex.B); yield return action;
            Toil commit = ToilMaker.MakeToil("CommitActualMobilityAidTransfer"); commit.defaultCompleteMode = ToilCompleteMode.Instant;
            commit.initAction = Commit; yield return commit;
        }
        private void Reown(Apparel item, ThingOwner<Thing> preferred)
        {
            if (item == null || item.Destroyed || item.Spawned || item.ParentHolder != null) return;
            if (!preferred.TryAdd(item, false) && !GenPlace.TryPlaceThing(item, pawn.PositionHeld, pawn.MapHeld, ThingPlaceMode.Near))
                Log.Error("Mobility aid transfer could not restore ownership of real item " + item.ThingID);
        }
        private void Commit()
        {
            if (!Valid() || pawn.Position != job.targetC.Cell || (request != MobilityAidRequest.Remove && pawn.carryTracker.CarriedThing != Aid))
            { EndJobWith(JobCondition.Incompletable); return; }
            MobilityAidController controller = MobilityAidController.CurrentController;
            controller.BeginTransfer(Patient);
            try
            {
                if (request == MobilityAidRequest.Remove)
                {
                    Patient.apparel.Remove(oldAid);
                    committed = pawn.carryTracker.innerContainer.TryAdd(oldAid, false);
                    if (!committed && oldAid.ParentHolder == null && !oldAid.Destroyed) Patient.apparel.Wear(oldAid, false, false);
                    if (committed) { Thing placed; pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out placed); }
                }
                else
                {
                    if (!pawn.carryTracker.innerContainer.Remove(Aid)) { EndJobWith(JobCondition.Incompletable); return; }
                    if (request == MobilityAidRequest.Replace) Patient.apparel.Remove(oldAid);
                    Patient.apparel.Wear(Aid, false, false); committed = Aid.Wearer == Patient;
                }
            }
            finally
            {
                try
                {
                    // Determine actual native outcome even if a notification threw.
                    if (request != MobilityAidRequest.Remove) committed = Aid?.Wearer == Patient;
                    if (committed)
                    {
                        if (request == MobilityAidRequest.Replace) Reown(oldAid, Patient.inventory.innerContainer);
                        controller.CompleteRequest(Patient, revision, request, Aid);
                    }
                    else
                    {
                        try
                        {
                            if (oldAid != null && !oldAid.Destroyed && oldAid.ParentHolder == null && !oldAid.Spawned)
                                Patient.apparel.Wear(oldAid, false, false);
                        }
                        finally
                        {
                            Reown(oldAid, Patient.inventory.innerContainer);
                            Reown(Aid, pawn.carryTracker.innerContainer);
                        }
                    }
                }
                finally { controller.EndTransfer(Patient); }
            }
            if (!committed) EndJobWith(JobCondition.Incompletable);
        }
    }
}
