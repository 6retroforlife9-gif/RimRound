using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    internal static class RitualAttendanceTransport
    {
        // Native DeSpawn can announce a loss before the carry container receives
        // the pawn. This synchronous, exception-safe scope identifies that one
        // pickup; it is never a global downed/despawn exception.
        [ThreadStatic] internal static RitualAttendanceEvent PickingEvent;
        [ThreadStatic] internal static Pawn PickingGuest;
        internal static RitualAttendanceEvent EventFor(Pawn helper,Job job) =>
            RitualAttendance.Component(helper?.MapHeld)?.Events.FirstOrDefault(e=>e.OwnsJob(helper,job,out _));
    }

    public sealed class JobDriver_RitualAttendanceCarry : JobDriver
    {
        RitualAttendanceEvent Event => RitualAttendanceTransport.EventFor(pawn,job);
        RitualAttendanceGuest Guest => Event?.Guests.FirstOrDefault(g=>g.Guest==job.targetA.Pawn && g.JobId==job.loadID);
        Pawn Takee => job.targetA.Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var e=Event;var g=Guest;
            if(e==null || g==null || Takee==null)return false;
            if(!e.TransportActive(g))
            {
                // Native reservations run before MakeNewToils registers finish
                // actions. Close this exact rejected allocation here as well.
                FinishTransport(JobCondition.InterruptForced);return false;
            }
            if(!pawn.Reserve(Takee,job,1,-1,null,errorOnFailed) ||
                !pawn.Reserve(job.targetC,job,1,-1,null,errorOnFailed))return false;
            var bed=g.ReturningTrip ? g.ReturnBed : g.DestinationBed;
            return bed==null ? pawn.ReserveSittableOrSpot(job.targetB.Cell,job,errorOnFailed) :
                pawn.Reserve(job.targetB,job,1,-1,null,errorOnFailed) &&
                pawn.Reserve(bed,job,bed.SleepingSlotsCount,0,null,errorOnFailed);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(()=>Event==null || Guest==null || !Event.TransportActive(Guest) || pawn.Downed || pawn.Drafted || pawn.InMentalState || pawn.IsBurning() ||
                (!Guest.ReturningTrip && (Event.ReturnRequested || RitualAttendance.SafetyReason(Takee)!=null)));
            AddFinishAction(FinishTransport);
            Toil walk=Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            walk.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            walk.FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            walk.FailOn(()=>!RitualAttendance.NeedsCarrying(Takee) || !DestinationTravelSafe());
            Toil pickup=ToilMaker.MakeToil("RR_AttendancePickup");
            pickup.initAction=()=>
            {
                var e=Event;var g=Guest;
                if(e==null || g==null || !e.TransportActive(g) || !DestinationTravelSafe() || !Takee.Spawned ||
                    (!g.ReturningTrip && RitualAttendance.SafetyReason(Takee)!=null) || pawn.carryTracker.CarriedThing!=null ||
                    pawn.carryTracker.AvailableStackSpace(Takee.def)<1 ||
                    !ReachabilityImmediate.CanReachImmediate(pawn.Position,Takee,Map,PathEndMode.Touch,pawn))
                { EndJobWith(JobCondition.Incompletable);return; }
                g.State=g.ReturningTrip ? RitualAttendanceState.Returning : RitualAttendanceState.Outbound;
                RitualAttendanceTransport.PickingEvent=e;RitualAttendanceTransport.PickingGuest=Takee;
                try
                {
                    int count=pawn.carryTracker.TryStartCarry(Takee,1);
                    if(count!=1 || pawn.carryTracker.CarriedThing!=Takee) { EndJobWith(JobCondition.Incompletable);return; }
                    g.WasTransported=true;
                }
                finally { RitualAttendanceTransport.PickingEvent=null;RitualAttendanceTransport.PickingGuest=null; }
            };
            pickup.defaultCompleteMode=ToilCompleteMode.Instant;
            Toil route=Toils_Goto.GotoCell(TargetIndex.C,PathEndMode.OnCell);
            route.FailOn(()=>Event?.OwnedTransport(Takee)!=true || !DestinationTravelSafe());
            // A loaded native carry resumes its existing route. It never tries
            // to pick up a second copy or deserializes the guest a second time.
            yield return Toils_Jump.JumpIf(route,()=>Event?.OwnedTransport(Takee)==true);
            yield return walk;yield return pickup;yield return route;
            // Foot traffic does not invalidate a distant place while we walk.
            // Once there, wait at most 180 native saved toil ticks for both cells
            // to clear. A hard hazard still fails immediately; a blocked drop
            // after the wait follows the existing safe local recovery path.
            Toil clear=Toils_General.Wait(180,TargetIndex.B);
            clear.FailOn(()=>Event?.OwnedTransport(Takee)!=true || !DestinationTravelSafe() || pawn.Position!=job.targetC.Cell);
            clear.tickAction=()=> { if(DestinationSafe())ReadyForNextToil(); };
            yield return clear;
            Toil place=ToilMaker.MakeToil("RR_AttendancePlace");
            place.initAction=()=>
            {
                var e=Event;var g=Guest;
                if(e==null || g==null || !e.OwnedTransport(Takee) || !DestinationSafe() || pawn.Position!=job.targetC.Cell ||
                    !RitualAttendance.CanTouchDestination(pawn,pawn.Position,job.targetB.Cell,g.ReturningTrip ? g.ReturnBed : g.DestinationBed))
                { EndJobWith(JobCondition.Incompletable);return; }
                if(!pawn.carryTracker.TryDropCarriedThing(job.targetB.Cell,ThingPlaceMode.Direct,out Thing result) || result!=Takee || !Takee.Spawned)
                { EndJobWith(JobCondition.Incompletable);return; }
                // Release only this carry job's claims, including a single-seat
                // Thing reservation, before the guest reserves its native spot.
                // Event ownership remains a logical cell claim.
                Map.reservationManager.ReleaseClaimedBy(pawn,job);
                var bed=g.ReturningTrip ? g.ReturnBed : g.DestinationBed;
                if(bed!=null)
                {
                    // The actual drop is already at the correct sleeping slot.
                    // Notify_TuckedIntoBed teleports to that slot; it is unnecessary.
                    Takee.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown,bed),JobCondition.InterruptForced,
                        tag:JobTag.TuckedIntoBed,continueSleeping:false);
                }
                if(g.ReturningTrip)g.State=RitualAttendanceState.Returned;
                else
                {
                    g.State=RitualAttendanceState.Placed;
                    if(e.Lord!=null && Takee.GetLord()==null)e.Lord.AddPawn(Takee);
                    e.KeepStationary(g);
                }
                g.Reason=null;
            };
            place.defaultCompleteMode=ToilCompleteMode.Instant;
            yield return place;
        }
        bool DestinationSafe() => DestinationSafeCore(false);
        bool DestinationTravelSafe() => DestinationSafeCore(true);
        bool DestinationSafeCore(bool allowPassers)
        {
            var e=Event;var g=Guest;
            if(e==null || g==null)return false;
            var bed=g.ReturningTrip ? g.ReturnBed : g.DestinationBed;
            return RitualAttendance.PlaceSafe(Takee,job.targetB.Cell,Map,bed,!g.ReturningTrip && g.Chair,allowTransitPassers:allowPassers) &&
                RitualAttendance.CanReserveDestination(pawn,job.targetB.Cell,bed) &&
                job.targetC.Cell.InBounds(Map) && job.targetC.Cell.Standable(Map) &&
                !job.targetC.Cell.GetThingList(Map).Any(t=>t is Fire || t.def.category==ThingCategory.Item ||
                    (t is Pawn other && other!=pawn && !(allowPassers && RitualAttendance.SafePassingPawn(other,Takee)))) &&
                pawn.CanReach(job.targetC,PathEndMode.OnCell,Danger.Some);
        }
        void FinishTransport(JobCondition condition)
        {
            var e=Event;var g=Guest;
            if(e==null || g==null)return;
            if(!g.Finished && e.PlayerHasControl(g))
            { g.State=RitualAttendanceState.Interrupted;g.Reason="RR_AttendancePlayerControl".Translate(Takee.LabelShort); }
            bool placedLocally=false;
            if(pawn.carryTracker?.CarriedThing==Takee)
            {
                // Always put down this exact pawn locally before handing over to
                // return or emergency care. No ownership or world-pawn mutation.
                bool dropped=false;
                foreach(IntVec3 cell in GenAdj.AdjacentCellsAndInside.Select(v=>pawn.Position+v)
                    .Where(c=>c.InBounds(Map) && c.Standable(Map) && c.GetEdifice(Map)==null &&
                        !c.GetThingList(Map).Any(t=>t is Fire || (t is Pawn other && other!=pawn)))
                    .OrderBy(c=>c.GetDangerFor(Takee,Map)).ThenBy(c=>c.DistanceToSquared(pawn.Position)))
                    if(pawn.carryTracker.TryDropCarriedThing(cell,ThingPlaceMode.Direct,out Thing result) && result==Takee) { dropped=true;break; }
                if(!dropped)
                {
                    // Native job cleanup retains responsibility for its holder;
                    // retain this record and report the exceptional obstruction.
                    g.Reason="RR_AttendanceCannotPutDown".Translate(Takee.LabelShort);
                    Messages.Message(g.Reason,pawn,MessageTypeDefOf.CautionInput,false);
                    g.RecoveryPending=true;g.JobId=-1;g.State=RitualAttendanceState.ReturnRequested;
                    ReleaseOrderedHelper(e);
                    e.BeginReturn(g.Reason);
                    return;
                }
                placedLocally=true;
            }
            // Release our job identity before cancellation can re-enter native
            // cleanup; a failed finish must never end the same job recursively.
            g.JobId=-1;g.Helper=null;
            ReleaseOrderedHelper(e);
            if(!g.Finished && g.State!=RitualAttendanceState.Placed && g.State!=RitualAttendanceState.Attending)
            {
                g.State=g.WasTransported ? RitualAttendanceState.ReturnRequested : RitualAttendanceState.WaitingForPickup;
                string reason=placedLocally ? "RR_AttendanceTransportInterrupted" : "RR_AttendancePickupInterrupted";
                e.FailedAttempt(g,reason.Translate(Takee.LabelShort));
                if(g.WasTransported && !e.ReturnRequested)e.BeginReturn(g.Reason);
            }
        }
        void ReleaseOrderedHelper(RitualAttendanceEvent e)
        {
            // TryTakeOrderedJob reserves and queues the new forced order before
            // ending this driver. Native Lord cleanup must not end that order
            // later, and must not re-enter this still-current cleanup now.
            // The carry identity has already been released by both callers.
            if(pawn.CurJob!=job || !job.playerInterruptedForced || e.Lord==null || pawn.GetLord()!=e.Lord ||
                !pawn.jobs.AllJobs().Any(next=>next!=job && next.playerForced))return;
            // Native removal precedes loss callbacks. Do not end jobs, rebuild
            // the queue, or change the saved return obligation for the guest.
            e.Lord.Notify_PawnLost(pawn,PawnLostCondition.ForcedByPlayerAction);
        }
    }

    public sealed class JobDriver_RitualAttendanceStay : JobDriver
    {
        private float heldGroundAngle=-1f;
        public override Rot4 ForcedLayingRotation => Rot4.South;
        internal float HeldGroundAngle => heldGroundAngle;
        private float FallbackProneAngle => pawn.thingIDNumber%2==0 ? Rot4.West.AsAngle : Rot4.East.AsAngle;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref heldGroundAngle,"rrAssistedGuestGroundAngle",-1f);
            // Older active jobs have no saved angle. Pick a stable side once, without
            // initializing or sampling a renderer while the game is resolving references.
            if(Scribe.mode==LoadSaveMode.PostLoadInit && pawn!=null && !(heldGroundAngle>=0f && heldGroundAngle<360f))
                heldGroundAngle=FallbackProneAngle;
        }

        public override string GetReport() => RitualAttendance.For(pawn)?.ReturnRequested==true ?
            "RR_AttendanceWaitingReturnReport".Translate().ToString() : base.GetReport();
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.ReserveSittableOrSpot(job.targetA.Cell,job,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil stay=ToilMaker.MakeToil("RR_AttendanceSpectate");
            stay.initAction=()=> { pawn.pather.StopDead();SetPostureAndFacing(); };
            stay.tickAction=()=>
            {
                var e=RitualAttendance.For(pawn);
                if(e==null || (e.ReturnRequested ? !e.CanWaitForReturn(pawn) : !e.Settled(pawn)) ||
                    RitualAttendance.SafetyReason(pawn)!=null || !RitualAttendance.NeedsCarrying(pawn))
                { EndJobWith(JobCondition.Succeeded);return; }
                SetPostureAndFacing();
                pawn.GainComfortFromCellIfPossible(1);
            };
            stay.handlingFacing=true;stay.socialMode=RandomSocialMode.SuperActive;
            stay.defaultCompleteMode=ToilCompleteMode.Never;
            yield return stay;
        }
        void SetPostureAndFacing()
        {
            pawn.jobs.posture=pawn.Downed ? PawnPosture.LayingOnGroundNormal : PawnPosture.Standing;
            if(heldGroundAngle<0f && pawn.Downed)
            {
                // Native human prone angles start at 45–135 or 225–315 degrees.
                // A large-body override may have set the internal wiggler to zero.
                float angle=UnityEngine.Mathf.Repeat(pawn.Drawer.renderer.wiggler.downedAngle,360f);
                heldGroundAngle=(angle>=45f && angle<=135f || angle>=225f && angle<=315f) ? angle : FallbackProneAngle;
            }
            if(pawn.Rotation!=Rot4.South)pawn.Rotation=Rot4.South;
        }
    }
}
