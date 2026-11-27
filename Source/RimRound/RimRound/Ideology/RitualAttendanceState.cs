using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    public sealed class RitualAttendanceEvent : IExposable
    {
        public int EventId, CreatedTick;
        public Lord Lord;
        public Map AnchorMap;
        public TargetInfo Site;
        public bool ReturnRequested;
        public string Reason;
        public List<RitualAttendanceGuest> Guests=new List<RitualAttendanceGuest>();
        public List<Pawn> Helpers=new List<Pawn>();
        public LordJob_Ritual Ritual => Lord?.LordJob as LordJob_Ritual;
        // A consumable focus can vanish before the native ritual cleans up.
        // Return care belongs to this map, independently of that Thing's lifetime.
        public Map Map => AnchorMap ?? Site.Map ?? Lord?.Map;
        public bool AllSettled => !ReturnRequested && Guests.All(g=>g.Finished || Settled(g.Guest));
        public bool HasPendingTransport => Guests.Any(g=>!g.Finished && (g.NeedsTransport && !Settled(g.Guest) || g.State==RitualAttendanceState.ReturnRequested));
        public bool Finished => Guests.All(g=>g.Finished);
        public RitualAttendanceGuest GuestFor(Pawn pawn) => Guests.FirstOrDefault(g=>g.Guest==pawn && !g.Finished);

        public void ExposeData()
        {
            Scribe_Values.Look(ref EventId,"eventId"); Scribe_Values.Look(ref CreatedTick,"createdTick");
            Scribe_References.Look(ref Lord,"lord"); Scribe_TargetInfo.Look(ref Site,"site");
            Scribe_References.Look(ref AnchorMap,"anchorMap");
            Scribe_Values.Look(ref ReturnRequested,"returnRequested"); Scribe_Values.Look(ref Reason,"reason");
            Scribe_Collections.Look(ref Guests,"guests",LookMode.Deep); Scribe_Collections.Look(ref Helpers,"helpers",LookMode.Reference);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)
            {
                if(Guests==null)Guests=new List<RitualAttendanceGuest>();
                if(Helpers==null)Helpers=new List<Pawn>();
                // No jobs, references, ownership or outcome mutate during Scribe.
                // The first map tick reconciles each saved native holder/job pair.
            }
        }
        public bool Settled(Pawn pawn)
        {
            var g=GuestFor(pawn);
            return g!=null && !ReturnRequested && (g.State==RitualAttendanceState.Placed || g.State==RitualAttendanceState.Attending) &&
                pawn?.Spawned==true && pawn.Map==Map && pawn.Position==g.Place &&
                (g.DestinationBed==null || pawn.CurrentBed()==g.DestinationBed);
        }
        public bool Reserves(IntVec3 cell) => Guests.Any(g=>!g.Finished &&
            ((!ReturnRequested && (g.Place==cell || g.Approach==cell)) || (g.ReturningTrip && (g.ReturnPlace==cell || g.ReturnApproach==cell))));
        public bool OwnsJob(Pawn helper,Job job,out RitualAttendanceGuest guest)
        {
            guest=Guests.FirstOrDefault(g=>g.Helper==helper && g.JobId==job?.loadID);
            return guest!=null && job.def.defName==RitualAttendance.CarryJob && job.count==EventId && job.targetA.Pawn==guest.Guest;
        }
        public bool OwnedTransport(Pawn pawn)
        {
            var g=GuestFor(pawn);
            return g!=null && g.Helper?.carryTracker?.CarriedThing==pawn && OwnsJob(g.Helper,g.Helper.CurJob,out _) &&
                (g.State==RitualAttendanceState.Outbound || g.State==RitualAttendanceState.Returning);
        }
        internal bool PickupScope(Pawn pawn) => RitualAttendanceTransport.PickingEvent==this && RitualAttendanceTransport.PickingGuest==pawn &&
            GuestFor(pawn) is RitualAttendanceGuest g && g.Helper!=null && OwnsJob(g.Helper,g.Helper.CurJob,out _);

        public Job TryGiveJob(Pawn helper)
        {
            if(helper==null || helper.Map!=Map || !Helpers.Contains(helper))return null;
            if(!ReturnRequested && helper.GetLord()!=Lord)return null;
            if(ReturnRequested && helper.GetLord()!=null && helper.GetLord()!=Lord)return null;
            foreach(var g in Guests.Where(g=>!g.Finished && g.JobId<0 && RitualAttendance.Now>=g.RetryAfter &&
                g.Failures<RitualAttendance.MaximumFailures && (g.State==RitualAttendanceState.WaitingForPickup || g.State==RitualAttendanceState.ReturnRequested))
                .OrderBy(g=>g.State==RitualAttendanceState.ReturnRequested ? 0 : 1).ThenBy(g=>g.Guest?.thingIDNumber ?? int.MaxValue))
            {
                if(g.Guest?.Spawned!=true || g.Guest.Map!=Map)continue;
                if(g.Guest.Drafted || HasNewPlayerOrder(g) || (g.Guest.GetLord()!=null && g.Guest.GetLord()!=Lord))
                { g.State=RitualAttendanceState.Interrupted;g.Reason="RR_AttendancePlayerControl".Translate(g.Guest.LabelShort);continue; }
                if(!RitualAttendance.NeedsCarrying(g.Guest)) { MobilityRecovered(g);continue; }
                var candidates=Helpers.Where(h=>h?.Map==Map && RitualAttendance.CanHelper(h,g.Guest) &&
                    (ReturnRequested ? h.GetLord()==null || h.GetLord()==Lord : h.GetLord()==Lord) &&
                    h.CanReserveAndReach(g.Guest,PathEndMode.Touch,Danger.Some));
                Pawn fastest=candidates.OrderByDescending(p=>p.GetStatValue(StatDefOf.MoveSpeed))
                    .ThenBy(p=>RitualAttendance.RouteCost(p,g.Guest)).ThenBy(p=>p.thingIDNumber).FirstOrDefault();
                if(fastest!=helper)continue;
                bool returning=g.State==RitualAttendanceState.ReturnRequested;
                if(returning)
                {
                    // Keep native emergency care and deliberate orders free to
                    // take over. A safe waiting guest must release only its own
                    // superseded unforced bed-seeking job before we reserve bed.
                    if(!CanWaitForReturn(g.Guest))continue;
                    KeepReturnStationary(g);
                }
                if(returning && !PlanReturn(g,helper))continue;
                IntVec3 destination=returning ? g.ReturnPlace : g.Place;
                IntVec3 approach=returning ? g.ReturnApproach : g.Approach;
                Building_Bed bed=returning ? g.ReturnBed : g.DestinationBed;
                if(!RitualAttendance.PlaceSafe(g.Guest,destination,Map,bed,!returning && g.Chair) ||
                    !RitualAttendance.CanReserveDestination(helper,destination,bed) ||
                    !helper.CanReserveAndReach(approach,PathEndMode.OnCell,Danger.Some) ||
                    !RitualAttendance.CanTouchDestination(helper,approach,destination,bed))
                { FailedAttempt(g,"RR_AttendanceDestinationBlocked".Translate(g.Guest.LabelShort));continue; }
                if(!returning && RitualAttendance.SafetyReason(g.Guest)!=null) { BeginReturn(RitualAttendance.SafetyReason(g.Guest));return null; }
                var def=DefDatabase<JobDef>.GetNamedSilentFail(RitualAttendance.CarryJob);if(def==null)return null;
                Job job=JobMaker.MakeJob(def,g.Guest,destination,approach);
                job.count=EventId;job.expiryInterval=5000;job.checkOverrideOnExpire=false;
                // Deliberately no job.lord: this job may outlive ritual cleanup.
                g.Helper=helper;g.JobId=job.loadID;g.IssuedTick=RitualAttendance.Now;g.ReturningTrip=returning;
                return job;
            }
            return null;
        }
        bool PlanReturn(RitualAttendanceGuest g,Pawn helper)
        {
            var candidates=new List<Building_Bed>();
            if(g.OriginalBed!=null)candidates.Add(g.OriginalBed);
            if(g.Guest.ownership?.OwnedBed!=null && !candidates.Contains(g.Guest.ownership.OwnedBed))candidates.Add(g.Guest.ownership.OwnedBed);
            candidates.AddRange(Map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>().Where(b=>b.Medical).OrderBy(b=>b.Position.DistanceToSquared(g.Guest.Position)));
            foreach(Building_Bed bed in candidates)
            {
                if(!RitualAttendance.BedValid(g.Guest,bed,Map))continue;
                IntVec3 cell=RestUtility.GetBedSleepingSlotPosFor(g.Guest,bed);
                if(!RitualAttendance.PlaceSafe(g.Guest,cell,Map,bed))continue;
                IntVec3 approach=RitualAttendance.FindApproach(helper,cell,null,bed);
                if(!approach.IsValid || !helper.CanReserve(bed,bed.SleepingSlotsCount,0))continue;
                g.ReturnBed=bed;g.ReturnPlace=cell;g.ReturnApproach=approach;
                g.Reason=bed==g.OriginalBed ? null : "RR_AttendanceReturnAlternative".Translate(g.Guest.LabelShort,bed.LabelShort).ToString();
                if(g.Reason!=null)Messages.Message(g.Reason,g.Guest,MessageTypeDefOf.NeutralEvent,false);
                return true;
            }
            // Remain at a real safe site with a visible obligation. Do not claim
            // someone else's bed, spawn a guest bed, or teleport to the old cell.
            FailedAttempt(g,"RR_AttendanceReturnPending".Translate(g.Guest.LabelShort));
            return false;
        }
        internal void FailedAttempt(RitualAttendanceGuest g,string reason)
        {
            g.Failures++;g.RetryAfter=RitualAttendance.Now+Math.Min(1800,120*g.Failures);g.Reason=reason;
            if(g.Failures==1 || g.Failures==RitualAttendance.MaximumFailures)
                Messages.Message(reason,g.Guest,MessageTypeDefOf.CautionInput,false);
            // A failed attempt can be reported from a driver's finish action.
            // Save the obligation now; Tick cancels outside native job cleanup.
            if(!ReturnRequested && g.Failures>=3)BeginReturn(reason);
        }
        static bool HasNewPlayerOrder(RitualAttendanceGuest g) =>
            g.Guest?.jobs?.AllJobs().Any(j=>j?.playerForced==true && j.loadID!=g.InitialJobId)==true;
        internal bool PlayerHasControl(RitualAttendanceGuest g) => g?.Guest!=null &&
            (g.Guest.Drafted || HasNewPlayerOrder(g) || (g.Guest.GetLord()!=null && g.Guest.GetLord()!=Lord));
        internal bool TransportActive(RitualAttendanceGuest g) => g!=null && !g.Finished &&
            g.Guest!=null && !g.Guest.Dead && !g.Guest.Destroyed && !PlayerHasControl(g) &&
            (g.ReturningTrip ? ReturnRequested && g.WasTransported &&
                (g.State==RitualAttendanceState.ReturnRequested || g.State==RitualAttendanceState.Returning) &&
                (!g.Guest.Spawned || CanWaitForReturn(g.Guest)) :
                !ReturnRequested && (g.State==RitualAttendanceState.WaitingForPickup || g.State==RitualAttendanceState.Outbound));

        public bool CanWaitForReturn(Pawn pawn)
        {
            var g=GuestFor(pawn);
            if(!ReturnRequested || g==null || !g.WasTransported || g.RecoveryPending ||
                g.State!=RitualAttendanceState.ReturnRequested || pawn?.Spawned!=true || pawn.Map!=Map ||
                !RitualAttendance.NeedsCarrying(pawn) || PlayerHasControl(g) ||
                RitualAttendance.SafetyReason(pawn)!=null || pawn.CurrentBed()!=null ||
                !RitualAttendance.PlaceSafe(pawn,pawn.Position,Map,allowChair:true,allowPassingPawns:true))return false;
            Job current=pawn.CurJob;
            if(current==null || current.def==JobDefOf.Wait_Downed || current.def==JobDefOf.Wait_MaintainPosture)return true;
            if(current.def.defName==RitualAttendance.StayJob)
                return current.count==EventId && current.targetA.Cell==pawn.Position;
            // This is the observed native crawl-to-own-bed race. Never replace
            // a player order, a medical-bed job, a rescue holder or a flee job.
            var bed=current.targetA.Thing as Building_Bed;
            return !current.playerForced && current.def==JobDefOf.LayDown && bed!=null && !bed.Medical &&
                (bed==g.OriginalBed || bed==pawn.ownership?.OwnedBed);
        }
        internal Job ReturnWaitingJob(Pawn pawn)
        {
            if(!CanWaitForReturn(pawn))return null;
            if(pawn.CurJob?.def.defName==RitualAttendance.StayJob)return pawn.CurJob;
            var def=DefDatabase<JobDef>.GetNamedSilentFail(RitualAttendance.StayJob);if(def==null)return null;
            Job job=JobMaker.MakeJob(def,pawn.Position);job.count=EventId;return job;
        }
        void KeepReturnStationary(RitualAttendanceGuest g)
        {
            Job job=ReturnWaitingJob(g.Guest);
            if(job!=null && job!=g.Guest.CurJob)
                g.Guest.jobs.StartJob(job,JobCondition.InterruptOptional);
        }
        static void ReleaseExternalHandoff(RitualAttendanceGuest g,string reason)
        {
            // Another actual job now controls the pawn/holder. Release only our
            // stale bookkeeping; never end that job or move its carried pawn.
            g.State=RitualAttendanceState.Interrupted;g.Reason=reason;
            g.JobId=-1;g.Helper=null;g.RecoveryPending=false;
        }
        public void BeginReturn(string reason=null)
        {
            if(ReturnRequested)return;
            ReturnRequested=true;Reason=reason;
            // Return routing needs the map/location, not a retired ritual focus.
            // Dropping the Thing reference also handles a destroyed focus on save.
            if(Map!=null)Site=new TargetInfo(Site.Cell,Map);
            foreach(var g in Guests.Where(g=>!g.Finished))
            {
                if(!g.WasTransported && !OwnedTransport(g.Guest))
                {
                    g.State=RitualAttendanceState.Returned;
                    if(g.Helper?.CurJob?.loadID==g.JobId)g.Helper.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    g.JobId=-1;g.Helper=null;continue;
                }
                g.State=OwnedTransport(g.Guest) ? RitualAttendanceState.Returning : RitualAttendanceState.ReturnRequested;
                // An outbound job already holding this guest finishes by safely
                // dropping at the carrier's current cell; a return route is a
                // separately revalidated job, never an in-flight destination swap.
                if(g.Helper?.CurJob?.loadID==g.JobId && !g.ReturningTrip)g.Helper.jobs.EndCurrentJob(JobCondition.InterruptForced);
                if(g.JobId<0)g.State=RitualAttendanceState.ReturnRequested;
                g.Failures=0;g.RetryAfter=RitualAttendance.Now;
            }
            // Clear references to the retired Lord before the next save. Neither
            // the return state nor new return jobs require a dead Lord reference.
            if(Lord!=null && (Lord.lordManager==null || !Lord.lordManager.lords.Contains(Lord)))DetachLord();
        }
        public void DetachLord()
        {
            if(!ReturnRequested)BeginReturn();
            var retired=Lord;if(retired==null)return;
            // Native roles can deliberately retain their current job after the
            // ritual ends (for example GiveSpeech). Queued jobs also serialize
            // their issuing Lord. Release only this event's retired reference,
            // without ending jobs, changing their queue, or replaying cleanup.
            var participants=retired.ownedPawns
                .Concat((retired.LordJob as LordJob_Ritual)?.assignments?.Participants ?? Enumerable.Empty<Pawn>())
                .Concat(Helpers).Concat(Guests.Select(g=>g.Guest)).Concat(Guests.Select(g=>g.Helper));
            DetachJobs(retired,participants);
            // Native trial audience stencils can fade after their Lord ends.
            // Keep their visual fade, but not a save reference to that dead Lord.
            var drawers=Map?.lordManager?.stencilDrawers;
            if(drawers!=null)foreach(var drawer in drawers)
                if(drawer.sourceLord==retired)drawer.sourceLord=null;
            Lord=null;
        }
        internal void MobilityRecovered(RitualAttendanceGuest g)
        {
            if(g.JobId>=0)return;
            if(ReturnRequested || g.State==RitualAttendanceState.ReturnRequested)
            {
                g.State=RitualAttendanceState.Returned;g.Reason=null;
                if(g.Guest.CurJob?.def.defName==RitualAttendance.StayJob)g.Guest.jobs.EndCurrentJob(JobCondition.Succeeded);
            }
            else
            {
                // The native walking spectator resumes with the original duty.
                g.State=RitualAttendanceState.Interrupted;g.Reason="RR_AttendanceMobilityRecovered".Translate(g.Guest.LabelShort);
                if(g.Guest.CurJob?.def.defName==RitualAttendance.StayJob)g.Guest.jobs.EndCurrentJob(JobCondition.Succeeded);
                Lord?.CurLordToil?.UpdateAllDuties();
            }
        }
        internal void KeepStationary(RitualAttendanceGuest g)
        {
            Pawn pawn=g.Guest;
            if(pawn?.Spawned!=true || pawn.Map!=Map || !Settled(pawn) || RitualAttendance.SafetyReason(pawn)!=null)return;
            if(g.DestinationBed!=null)
            {
                // In particular, preserve an already settled host's exact rest job.
                if(pawn.CurrentBed()!=g.DestinationBed && pawn.Position==g.Place)
                    pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown,g.DestinationBed),JobCondition.InterruptOptional,tag:JobTag.TuckedIntoBed);
                return;
            }
            if(HasNewPlayerOrder(g))return;
            if(pawn.CurJob?.def.defName==RitualAttendance.StayJob)return;
            var def=DefDatabase<JobDef>.GetNamedSilentFail(RitualAttendance.StayJob);if(def==null)return;
            var job=JobMaker.MakeJob(def,g.Place);job.count=EventId;
            pawn.jobs.StartJob(job,JobCondition.InterruptOptional);
        }
        internal static void DetachJobs(Lord retired,IEnumerable<Pawn> participants)
        {
            foreach(Pawn pawn in participants.Where(p=>p?.jobs!=null).Distinct())
                foreach(Job job in pawn.jobs.AllJobs())
                    if(job?.lord==retired)job.lord=null;
        }
        public void Tick()
        {
            if(Map==null)return;
            if(!ReturnRequested && (Lord==null || !Map.lordManager.lords.Contains(Lord))) { BeginReturn();DetachLord(); }
            if(!ReturnRequested && !AllSettled && RitualAttendance.Now-CreatedTick>RitualAttendance.PreparationLimit)
            { Reason="RR_AttendancePreparationTimeout".Translate();Ritual?.Cancel();BeginReturn(Reason); }
            foreach(var g in Guests.Where(g=>!g.Finished).ToList())
            {
                Pawn pawn=g.Guest;
                if(pawn==null || pawn.Dead || pawn.Destroyed) { g.State=RitualAttendanceState.Interrupted;g.Reason="RR_AttendanceInvalid".Translate();continue; }
                if(g.JobId>=0 && !OwnsJob(g.Helper,g.Helper?.CurJob,out _))
                {
                    if(RitualAttendance.Now-g.IssuedTick<120)continue;
                    // Do not touch a pawn intentionally transferred into another
                    // native holder/job. A failed exact job secures its own carry.
                    if(g.Helper?.carryTracker?.CarriedThing==pawn)
                    {
                        ReleaseExternalHandoff(g,"RR_AttendanceHolderChanged".Translate(pawn.LabelShort));
                        Messages.Message(g.Reason,pawn,MessageTypeDefOf.CautionInput,false);continue;
                    }
                    g.JobId=-1;g.Helper=null;
                    g.State=g.WasTransported ? RitualAttendanceState.ReturnRequested : RitualAttendanceState.WaitingForPickup;
                    FailedAttempt(g,"RR_AttendanceCarrierUnavailable".Translate(pawn.LabelShort));
                }
                if(g.RecoveryPending)
                {
                    if(g.Helper?.carryTracker?.CarriedThing==pawn)
                    {
                        // The only unowned-container recovery is the passenger
                        // our own finish action could not put down. Never adopt
                        // unrelated carried pawns or interrupt a new player order.
                        if(g.Helper.CurJob?.playerForced==true || (g.Helper.CurJob?.targetA.Pawn==pawn && g.Helper.CurJob.def.defName!=RitualAttendance.CarryJob))
                        { ReleaseExternalHandoff(g,"RR_AttendancePlayerControl".Translate(pawn.LabelShort));continue; }
                        bool secured=false;
                        if(g.Helper.Spawned && g.Helper.Map==Map)
                            foreach(IntVec3 cell in GenAdj.AdjacentCellsAndInside.Select(v=>g.Helper.Position+v).Where(c=>c.InBounds(Map) && c.Standable(Map) &&
                                c.GetEdifice(Map)==null && !c.GetThingList(Map).Any(t=>t is Fire || (t is Pawn other && other!=g.Helper)))
                                .OrderBy(c=>c.GetDangerFor(pawn,Map)))
                                if(g.Helper.carryTracker.TryDropCarriedThing(cell,ThingPlaceMode.Direct,out Thing result) && result==pawn) { secured=true;break; }
                        if(!secured)continue;
                    }
                    g.RecoveryPending=false;g.Helper=null;g.JobId=-1;g.State=RitualAttendanceState.ReturnRequested;
                }
                if(OwnedTransport(pawn))
                {
                    if(!AbundanceRituals.CapableServer(g.Helper))g.Helper.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    continue;
                }
                if(!pawn.Spawned || pawn.Map!=Map)
                {
                    g.State=RitualAttendanceState.Interrupted;g.Reason="RR_AttendanceNotOnMap".Translate(pawn.LabelShort);continue;
                }
                if(!RitualAttendance.NeedsCarrying(pawn)) { MobilityRecovered(g);continue; }
                if(pawn.Drafted || HasNewPlayerOrder(g) || (pawn.GetLord()!=null && pawn.GetLord()!=Lord))
                {
                    g.State=RitualAttendanceState.Interrupted;g.Reason="RR_AttendancePlayerControl".Translate(pawn.LabelShort);
                    // Finished-record lookup remains available to this exact
                    // driver's finish action, which releases its identity.
                    if(OwnsJob(g.Helper,g.Helper?.CurJob,out _))g.Helper.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    continue;
                }
                if(!ReturnRequested && (Ritual?.assignments?.PawnParticipating(pawn)!=true || pawn.GetLord()!=Lord ||
                    RitualAttendance.SafetyReason(pawn)!=null || Site.Thing?.Destroyed==true))
                { Ritual?.Cancel();BeginReturn(RitualAttendance.SafetyReason(pawn)); }
                if(!ReturnRequested && (g.State==RitualAttendanceState.Placed || g.State==RitualAttendanceState.Attending))
                {
                    if(!RitualAttendance.PlaceSafe(pawn,g.Place,Map,g.DestinationBed,g.Chair,allowPassingPawns:true) || pawn.Position!=g.Place)
                    {
                        // Keep the reason before native Cancel synchronously
                        // cleans the Lord and starts the saved return lifecycle.
                        var active=Ritual;
                        BeginReturn("RR_AttendanceDestinationBlocked".Translate(pawn.LabelShort));active?.Cancel();
                    }
                    else { g.State=RitualAttendanceState.Attending;KeepStationary(g); }
                }
                if(ReturnRequested && g.State==RitualAttendanceState.ReturnRequested && pawn.CurrentBed()!=null &&
                    RitualAttendance.BedValid(pawn,pawn.CurrentBed(),Map))g.State=RitualAttendanceState.Returned;
                else if(ReturnRequested)KeepReturnStationary(g);
            }
            if(ReturnRequested)
            {
                // BeginReturn can run inside a native driver's finish action.
                // Cancel only here, after that cleanup has returned, so Lord
                // cleanup cannot recursively erase a newly accepted player job.
                if(Lord!=null && Map.lordManager.lords.Contains(Lord) && Ritual?.cancelled==false)Ritual.Cancel();
                if(Lord!=null && !Map.lordManager.lords.Contains(Lord))DetachLord();
                // The selected assistants remain the only candidates after the
                // ritual. Do not interrupt direct player orders, other Lords,
                // emergency needs or a pawn with any carried stock/person.
                foreach(Pawn helper in Helpers.Where(p=>p?.Spawned==true && p.Map==Map).OrderByDescending(p=>p.GetStatValue(StatDefOf.MoveSpeed)).ToList())
                {
                    Job job=TryGiveJob(helper);
                    if(job!=null)helper.jobs.StartJob(job,JobCondition.InterruptOptional);
                }
            }
        }
        public void RetryReturns()
        {
            foreach(var g in Guests.Where(g=>!g.Finished && g.State==RitualAttendanceState.ReturnRequested))
            { g.Failures=0;g.RetryAfter=RitualAttendance.Now; }
        }
        // Read-only native harness hook. These are state/holder invariants; a
        // clean result is not evidence for routes, visuals or outcome acceptance.
        public List<string> InvariantViolations()
        {
            var errors=new List<string>();
            if(!ReturnRequested && Lord==null)errors.Add("Active attendance has no Lord.");
            if(Guests.Where(g=>!g.Finished).GroupBy(g=>g.Guest).Any(g=>g.Count()>1))errors.Add("Duplicate guest record.");
            if(Guests.Where(g=>g.JobId>=0).GroupBy(g=>g.Helper).Any(g=>g.Key==null || g.Count()>1))errors.Add("Duplicate or missing transport helper.");
            foreach(var g in Guests)
            {
                if(g.Place==g.Approach)errors.Add("Guest and helper approach overlap.");
                if(g.WasTransported && !g.OriginalPlace.IsValid)errors.Add("Transported guest has no original location.");
                if(g.Finished && g.Helper?.carryTracker?.CarriedThing==g.Guest && g.Helper.CurJob?.loadID==g.JobId)
                    errors.Add("Completed guest remains in the owned transport holder.");
                if(g.Finished && g.JobId>=0 && g.Helper?.CurJob?.loadID!=g.JobId)
                    errors.Add("Completed transport retains a stale job identity after its native job closed.");
                if(g.JobId<0 && g.Helper!=null && !g.RecoveryPending)
                    errors.Add("Released transport retains an assistant without a recovery obligation.");
                if((g.State==RitualAttendanceState.Outbound || g.State==RitualAttendanceState.Returning) && !OwnedTransport(g.Guest) && !PickupScope(g.Guest))
                    errors.Add("Transport state has no exact native holder/job pair.");
                if(g.JobId>=0 && g.Helper?.CurJob?.loadID==g.JobId && g.Helper.carryTracker.CarriedThing!=null && g.Helper.carryTracker.CarriedThing!=g.Guest)
                    errors.Add("Transport helper is carrying another object.");
            }
            return errors;
        }
    }

    public sealed class MapComponent_RitualAttendance : MapComponent
    {
        public int NextId=1;
        public List<RitualAttendanceEvent> Events=new List<RitualAttendanceEvent>();
        public MapComponent_RitualAttendance(Map map):base(map) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref NextId,"rrAttendanceNextId",1);
            Scribe_Collections.Look(ref Events,"rrAttendanceEvents",LookMode.Deep);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)
            {
                if(Events==null)Events=new List<RitualAttendanceEvent>();
                // Upgrade active saves using their actual owning map component;
                // never infer a map from another pawn or mutate jobs during load.
                foreach(var e in Events)e.AnchorMap=map;
            }
        }
        public override void MapComponentTick()
        {
            if(!ModsConfig.IdeologyActive || RitualAttendance.Now%30!=0)return;
            foreach(var e in Events.ToList()){e.AnchorMap=map;e.Tick();}
            Events.RemoveAll(e=>e.Finished && e.Guests.All(g=>g.JobId<0 && !g.RecoveryPending));
        }
    }
}
