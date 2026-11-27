using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    public enum RitualAttendanceState { WaitingForPickup, Outbound, Placed, Attending, ReturnRequested, Returning, Returned, Interrupted }

    // References are owned by the map, not the ritual Lord. A carried pawn is
    // scribed by its native holder; this record only references that same pawn.
    public sealed class RitualAttendanceGuest : IExposable
    {
        public Pawn Guest, Helper;
        public Building_Bed OriginalBed, DestinationBed, ReturnBed;
        public IntVec3 OriginalPlace=IntVec3.Invalid, Place=IntVec3.Invalid, Approach=IntVec3.Invalid;
        public IntVec3 ReturnPlace=IntVec3.Invalid, ReturnApproach=IntVec3.Invalid;
        public RitualAttendanceState State;
        public bool NeedsTransport, WasTransported, Chair, FloorSeat, ReturningTrip, RecoveryPending;
        public int JobId=-1, InitialJobId=-1, IssuedTick, RetryAfter, Failures;
        public string Reason;
        public bool Finished => State==RitualAttendanceState.Returned || State==RitualAttendanceState.Interrupted;
        public void ExposeData()
        {
            Scribe_References.Look(ref Guest,"guest"); Scribe_References.Look(ref Helper,"helper");
            Scribe_References.Look(ref OriginalBed,"originalBed"); Scribe_References.Look(ref DestinationBed,"destinationBed");
            Scribe_References.Look(ref ReturnBed,"returnBed");
            Scribe_Values.Look(ref OriginalPlace,"originalPlace",IntVec3.Invalid); Scribe_Values.Look(ref Place,"place",IntVec3.Invalid);
            Scribe_Values.Look(ref Approach,"approach",IntVec3.Invalid); Scribe_Values.Look(ref ReturnPlace,"returnPlace",IntVec3.Invalid);
            Scribe_Values.Look(ref ReturnApproach,"returnApproach",IntVec3.Invalid); Scribe_Values.Look(ref State,"state");
            Scribe_Values.Look(ref NeedsTransport,"needsTransport"); Scribe_Values.Look(ref WasTransported,"wasTransported");
            Scribe_Values.Look(ref Chair,"chair"); Scribe_Values.Look(ref ReturningTrip,"returningTrip");
            Scribe_Values.Look(ref FloorSeat,"floorSeat");
            Scribe_Values.Look(ref RecoveryPending,"recoveryPending");
            Scribe_Values.Look(ref JobId,"jobId",-1); Scribe_Values.Look(ref IssuedTick,"issuedTick");
            Scribe_Values.Look(ref InitialJobId,"initialJobId",-1);
            Scribe_Values.Look(ref RetryAfter,"retryAfter"); Scribe_Values.Look(ref Failures,"failures"); Scribe_Values.Look(ref Reason,"reason");
        }
    }

    public static class RitualAttendance
    {
        public const string CarryJob="RR_RitualAttendanceCarry", StayJob="RR_RitualAttendanceStay";
        public const int PreparationLimit=15000, MaximumFailures=6;
        sealed class StartFailure { public string Reason; }
        static readonly ConditionalWeakTable<LordJob_Ritual,StartFailure> failedStarts=new ConditionalWeakTable<LordJob_Ritual,StartFailure>();
        internal static bool StartBlocked(LordJob_Ritual ritual) => ritual!=null && failedStarts.TryGetValue(ritual,out _);
        static void RejectStart(LordJob_Ritual ritual,string reason)
        {
            // Cancel is a native transition signal. Until GotoToil has selected
            // the first source toil, remember failure and suppress job overrides;
            // the MakeNewLord postfix sends the signal exactly once afterwards.
            if(ritual.lord.CurLordToil==null)
            {
                if(!failedStarts.TryGetValue(ritual,out _))failedStarts.Add(ritual,new StartFailure { Reason=reason });
                return;
            }
            Messages.Message(reason,ritual.selectedTarget,MessageTypeDefOf.RejectInput,false);ritual.Cancel();
        }
        internal static bool CompleteStart(LordJob_Ritual ritual)
        {
            if(!failedStarts.TryGetValue(ritual,out var failure))return true;
            // Remove before dispatch: native cleanup can re-enter patched code.
            failedStarts.Remove(ritual);
            Messages.Message(failure.Reason,ritual.selectedTarget,MessageTypeDefOf.RejectInput,false);ritual.Cancel();
            return false;
        }
        internal static int Now => Find.TickManager?.TicksGame ?? 0;
        internal static MapComponent_RitualAttendance Component(Map map) => map?.GetComponent<MapComponent_RitualAttendance>();
        public static RitualAttendanceEvent For(LordJob_Ritual ritual) => ritual?.lord==null ? null : Component(ritual.Map)?.Events.FirstOrDefault(e=>e.Lord==ritual.lord);
        public static RitualAttendanceEvent For(Pawn pawn) => Component(pawn?.MapHeld)?.Events.FirstOrDefault(e=>e.Guests.Any(g=>g.Guest==pawn && !g.Finished));
        public static bool NeedsCarrying(Pawn pawn) => pawn!=null && (pawn.Downed || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving));
        public static bool IsGuest(LordJob_Ritual ritual,Pawn pawn) => For(ritual)?.Guests.Any(g=>g.Guest==pawn && !g.Finished)==true;
        public static bool IsSettled(LordJob_Ritual ritual,Pawn pawn) => For(ritual)?.Settled(pawn)==true;
        public static bool AllSettled(LordJob_Ritual ritual) => !StartBlocked(ritual) && (For(ritual)?.AllSettled ?? true);
        public static bool IsOwnedTransport(LordJob_Ritual ritual,Pawn pawn) => For(ritual)?.OwnedTransport(pawn)==true;
        public static bool IsTransportBusy(Pawn pawn) => Component(pawn?.MapHeld)?.Events.Any(e=>e.Guests.Any(g=>g.Helper==pawn && g.JobId>=0 && !g.Finished))==true;
        public static bool IsReservedCell(Map map,IntVec3 cell) => Component(map)?.Events.Any(e=>e.Reserves(cell))==true;
        public static Job TryGiveTransportJob(Pawn pawn)
        {
            // The constant think tree revisits high-priority ritual duties every
            // 30 ticks. Return the very same job while it remains ours; returning
            // null would let the native spectator job replace the walk to pickup
            // or the loaded trip. Earlier emergency/player branches still run.
            Job retained=RetainedTransportJob(pawn);
            if(retained!=null)return retained;
            var events=Component(pawn?.Map)?.Events;
            if(events==null)return null;
            foreach(var e in events) { Job job=e.TryGiveJob(pawn); if(job!=null)return job; }
            return null;
        }
        static Job RetainedTransportJob(Pawn pawn)
        {
            Job job=pawn?.CurJob;
            if(job?.def.defName!=CarryJob || SafetyReason(pawn)!=null || NeedsCarrying(pawn))return null;
            var e=RitualAttendanceTransport.EventFor(pawn,job);
            if(e==null || !e.OwnsJob(pawn,job,out var g) || !e.TransportActive(g) || g.RecoveryPending)return null;
            if(!g.ReturningTrip && (e.ReturnRequested || SafetyReason(g.Guest)!=null))return null;
            if(g.Guest==null || g.Guest.Dead || g.Guest.Destroyed)return null;
            bool walkingToPickup=g.Guest.Spawned && pawn.carryTracker.CarriedThing==null && NeedsCarrying(g.Guest);
            return walkingToPickup || e.OwnedTransport(g.Guest) ? job : null;
        }
        public static void BeginReturn(LordJob_Ritual ritual,string reason=null) { For(ritual)?.BeginReturn(reason); }

        public static RitualAttendanceEvent Ensure(LordJob_Ritual ritual,List<RitualAttendanceGuest> planned=null)
        {
            if(ritual?.lord==null || ritual.Map==null || !ModsConfig.IdeologyActive || !Supported(ritual.Ritual) || ritual.assignments==null)return null;
            if(StartBlocked(ritual))return null;
            var existing=For(ritual); if(existing!=null)return existing;
            string reason=null;
            if(planned==null)reason=Validate(ritual.selectedTarget,ritual.Ritual,ritual.assignments,out planned);
            if(reason!=null) { RejectStart(ritual,reason);return null; }
            if(planned==null || planned.Count==0)return null;
            var component=Component(ritual.Map);
            if(planned.Any(g=>component.Events.Any(e=>e.Guests.Any(other=>!other.Finished && other.Guest==g.Guest)) ||
                IsReservedCell(ritual.Map,g.Place) || IsReservedCell(ritual.Map,g.Approach)))
            { RejectStart(ritual,"RR_AttendanceInvalid".Translate());return null; }
            var created=new RitualAttendanceEvent {
                EventId=component.NextId++, Lord=ritual.lord, Site=ritual.selectedTarget, AnchorMap=ritual.Map, CreatedTick=Now,
                Guests=planned, Helpers=ritual.assignments.Participants.Where(p=>HelperRoleAllowed(p,ritual.assignments,ritual.Ritual)).Distinct().ToList()
            };
            component.Events.Add(created);
            foreach(var g in created.Guests)
            {
                g.Helper=null; g.JobId=-1;
                g.State=g.NeedsTransport ? RitualAttendanceState.WaitingForPickup : RitualAttendanceState.Placed;
                if(!g.NeedsTransport)created.KeepStationary(g);
            }
            return created;
        }

        // This method is deliberately read-only. Dialog previews never reserve,
        // move a pawn, modify an assignment, or claim a home bed.
        public static string Validate(TargetInfo target,Precept_Ritual ritual,RitualRoleAssignments assignments,out List<RitualAttendanceGuest> guests)
        {
            guests=new List<RitualAttendanceGuest>();
            if(assignments==null || !target.IsValid || target.Map==null)return "RR_AttendanceInvalid".Translate();
            var selected=assignments.Participants.ToList();
            bool food=AbundanceRituals.IsFoodRitual(ritual);
            var occupied=new HashSet<IntVec3>();
            foreach(Pawn pawn in selected.Where(p=>NeedsCarrying(p)).OrderBy(p=>p.thingIDNumber))
            {
                var role=assignments.RoleForPawn(pawn);
                // Existing bedside actors keep their independently validated rest
                // session and never enter this spectator transport lifecycle.
                if(!food && role!=null && BedsideRituals.Supported(ritual) &&
                    role.id==BedsideRituals.ActorRoleId(ritual) && BedsideRituals.Ready(pawn))continue;
                // Native forced/downed recipients (for example a prisoner being
                // carried by a conversion role) retain their own native adapter.
                if(!food && role!=null && (role.allowDowned || !role.addToLord || !pawn.IsFreeColonist))continue;
                string reason=AdmissionReason(pawn,role,ritual,target);
                if(reason!=null)return reason;
                string native=RitualRoleAssignments.PawnNotAssignableReason(pawn,role,ritual,assignments,target,out _);
                if(native!=null)return native;
                if(Component(target.Map)?.Events.Any(e=>e.Guests.Any(g=>!g.Finished && g.Guest==pawn))==true)
                    return "RR_AttendanceOtherEvent".Translate(pawn.LabelShort);
                var guest=new RitualAttendanceGuest { Guest=pawn, OriginalBed=pawn.ownership?.OwnedBed,
                    OriginalPlace=pawn.Position, InitialJobId=pawn.CurJob?.loadID ?? -1, NeedsTransport=true };
                if(food && role?.id==AbundanceRituals.Host && target.Thing is Building_Bed hostBed)
                {
                    guest.DestinationBed=hostBed; guest.Place=RestUtility.GetBedSleepingSlotPosFor(pawn,hostBed);
                    guest.NeedsTransport=pawn.CurrentBed()!=hostBed;
                }
                else if(food && AbundanceRituals.IsOfficer(pawn) && pawn.CurrentBed() is Building_Bed officerBed &&
                    officerBed==pawn.ownership?.OwnedBed && BedValid(pawn,officerBed,target.Map) &&
                    AbundanceBeds.SameRoom(officerBed.Position,target.Cell,target.Map))
                {
                    // Each officer keeps their own rest session in a shared hall;
                    // only guests outside it need a physical attendance trip.
                    guest.DestinationBed=officerBed;
                    guest.Place=RestUtility.GetBedSleepingSlotPosFor(pawn,officerBed);
                    guest.NeedsTransport=false;
                }
                var helpers=selected.Where(p=>HelperRoleAllowed(p,assignments,ritual) && CanHelper(p,pawn,true,food)).ToList();
                var rankedHelpers=helpers.OrderByDescending(p=>p.GetStatValue(StatDefOf.MoveSpeed)).ThenBy(p=>RouteCost(p,pawn)).ThenBy(p=>p.thingIDNumber).ToList();
                var candidates=guest.DestinationBed!=null ? new[]{guest.Place} : CandidatePlaces(pawn,target,ritual);
                bool found=false;
                foreach(IntVec3 place in candidates)
                {
                    if(occupied.Contains(place) || IsReservedCell(target.Map,place) ||
                        !PlaceSafe(pawn,place,target.Map,guest.DestinationBed,guest.DestinationBed==null && ChairCompatible(pawn)))continue;
                    Pawn helper=null; IntVec3 approach=IntVec3.Invalid;
                    bool already=pawn.Position==place && (guest.DestinationBed==null || pawn.CurrentBed()==guest.DestinationBed);
                    if(already && !CanReserveDestination(pawn,place,guest.DestinationBed))continue;
                    foreach(Pawn h in rankedHelpers)
                    {
                        if(!h.CanReach(pawn,PathEndMode.Touch,Danger.Some) ||
                            (!already && !CanReserveDestination(h,place,guest.DestinationBed)))continue;
                        approach=FindApproach(h,place,occupied,guest.DestinationBed);
                        if(approach.IsValid) { helper=h;break; }
                    }
                    if(helper==null && guest.NeedsTransport && !already)continue;
                    if(!approach.IsValid)approach=GenAdj.AdjacentCells.Select(v=>place+v).Where(c=>c.InBounds(target.Map) && c.Standable(target.Map) &&
                        !occupied.Contains(c) && !IsReservedCell(target.Map,c) && c.GetEdifice(target.Map)==null &&
                        !c.GetThingList(target.Map).Any(t=>t is Building_Bed))
                        .DefaultIfEmpty(IntVec3.Invalid).First();
                    if(!approach.IsValid || approach==place)continue;
                    guest.Place=place; guest.Approach=approach;guest.Helper=helper;
                    guest.Chair=guest.DestinationBed==null && place.GetEdifice(target.Map)?.def.building?.isSittable==true;
                    guest.FloorSeat=guest.Chair && IsGroundRitualSeat(place.GetEdifice(target.Map)?.def);
                    if(pawn.Spawned && pawn.Position==place && (guest.DestinationBed==null || pawn.CurrentBed()==guest.DestinationBed))guest.NeedsTransport=false;
                    occupied.Add(place);occupied.Add(approach);found=true;break;
                }
                if(!found)return (helpers.Count==0 ? "RR_AttendanceNoHelper" : "RR_AttendanceNoPlace").Translate(pawn.LabelShort);
                guests.Add(guest);
            }
            return null;
        }

        public static bool Supported(Precept_Ritual ritual)
        {
            if(AbundanceRituals.IsFoodRitual(ritual))return true;
            string name=ritual?.behavior?.def?.defName;
            if(name==null)return false;
            // Explicit native worker AND duty inventory. A modded worker with a
            // familiar label or a new action duty must not acquire this exception.
            if(!SupportedBehaviors.Contains(name))return false;
            Type worker=ritual.behavior.GetType();
            bool native=worker.Assembly==typeof(RitualBehaviorWorker).Assembly;
            bool bedside=worker==typeof(RitualBehaviorWorker_BedsideSpeech) || worker==typeof(RitualBehaviorWorker_BedsideConversion) || worker==typeof(RitualBehaviorWorker_BedsideTrial);
            return (native || bedside) && ritual.behavior.def.stages.All(s=>s.defaultDuty!=null && SupportedDuties.Contains(s.defaultDuty.defName));
        }
        static readonly HashSet<string> SupportedBehaviors=new HashSet<string> {
            "LeaderSpeech","Funeral","Conversion","RoleChange","Trial","TrialPrisoner","TrialMentalState",
            "DateRitual","BurnCircle","TreeConnection","RR_BedsideLeaderSpeech","RR_BedsideConversion","RR_BedsideTrial","RR_BedsideTrialPrisoner","RR_BedsideTrialMentalState"
        };
        internal static readonly HashSet<string> SupportedDuties=new HashSet<string> { "Spectate","SpectateCircle","SpectateSocial","Party" };
        internal static bool HelperRoleAllowed(Pawn pawn,RitualRoleAssignments assignments,Precept_Ritual ritual)
        {
            var role=assignments?.RoleForPawn(pawn);
            return role==null || (AbundanceRituals.IsFoodRitual(ritual) && role.id==AbundanceRituals.Attendant);
        }
        public static string AdmissionReason(Pawn pawn,RitualRole role,Precept_Ritual ritual,TargetInfo target)
        {
            if(!Supported(ritual))return "RR_AttendanceUnsupported".Translate(ritual?.LabelCap ?? "ritual");
            if(role!=null && !(AbundanceRituals.IsFoodRitual(ritual) && role.id==AbundanceRituals.Host))
                return "RR_AttendancePerformer".Translate(pawn.LabelShort,role.LabelCap);
            string safety=SafetyReason(pawn,AbundanceRituals.IsFoodRitual(ritual));
            if(safety!=null)return safety;
            if(pawn.Spawned!=true || (target.Map!=null && pawn.Map!=target.Map))return "RR_AttendanceNotOnMap".Translate(pawn.LabelShort);
            return null;
        }
        public static bool CanWakeForFeast(Pawn pawn) => pawn?.Spawned==true && pawn.CurJobDef==JobDefOf.LayDown &&
            pawn.CurJob.playerForced!=true && !pawn.Deathresting && !pawn.Suspended && !pawn.Drafted && !pawn.InMentalState &&
            pawn.needs?.rest?.CurLevelPercentage>=.1f && pawn.health.capacities.CanBeAwake &&
            !pawn.health.hediffSet.HasHediff(HediffDefOf.Anesthetic) && !pawn.health.HasHediffsNeedingTend() &&
            !HealthAIUtility.ShouldHaveSurgeryDoneNow(pawn) && !pawn.health.hediffSet.InLabor() &&
            pawn.health.hediffSet.BleedRateTotal<=0f && !pawn.health.hediffSet.HasTemperatureInjury(TemperatureInjuryStage.Serious);
        internal static string SafetyReason(Pawn pawn,bool allowFeastWake=false)
        {
            if(pawn==null || pawn.Dead || pawn.Destroyed)return "RR_AttendanceInvalid".Translate();
            if(pawn.Faction!=Faction.OfPlayer || !pawn.RaceProps.Humanlike || pawn.IsPrisoner || pawn.IsSlave || pawn.IsQuestLodger() ||
                pawn.DevelopmentalStage==DevelopmentalStage.Baby || pawn.DevelopmentalStage==DevelopmentalStage.Newborn)
                return "RR_AttendanceIneligible".Translate(pawn.LabelShort);
            if(pawn.Drafted || pawn.InMentalState || pawn.Suspended || pawn.Deathresting || pawn.IsBurning() ||
                !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness))return "RR_AttendanceUnsafe".Translate(pawn.LabelShort);
            // ShouldSeekMedicalRestUrgent itself treats every downed pawn as an
            // emergency. Check its medical causes explicitly, retaining stable
            // non-injury immobility as the only exception.
            if(pawn.health.HasHediffsNeedingTend() || HealthAIUtility.ShouldHaveSurgeryDoneNow(pawn) || pawn.health.hediffSet.InLabor() ||
                pawn.health.hediffSet.HasTemperatureInjury(TemperatureInjuryStage.Serious) || pawn.health.hediffSet.BleedRateTotal>0f)
                return "RR_AttendanceMedical".Translate(pawn.LabelShort);
            if((pawn.Spawned && !pawn.Awake() && !RestingFeastOfficer(pawn) && !(allowFeastWake && CanWakeForFeast(pawn))) || pawn.needs?.rest?.CurLevelPercentage<.1f || pawn.needs?.food?.Starving==true)
                return "RR_AttendanceNeeds".Translate(pawn.LabelShort);
            return null;
        }
        static bool RestingFeastOfficer(Pawn pawn)
        {
            if(!AbundanceRituals.IsOfficer(pawn) || pawn.CurJobDef!=JobDefOf.LayDown || pawn.CurJob.playerForced)return false;
            var attendance=For(pawn);var guest=attendance?.GuestFor(pawn);
            return attendance?.Ritual is LordJob_AbundanceMeals meals && !meals.cancelled && !meals.CancelRequested &&
                attendance.Settled(pawn) && guest!=null && !guest.NeedsTransport && guest.DestinationBed!=null &&
                pawn.CurJob.loadID==guest.InitialJobId && pawn.CurrentBed()==guest.DestinationBed &&
                pawn.ownership?.OwnedBed==guest.DestinationBed &&
                meals.Meals.Any(m=>m.Eater==pawn && m.OfficerCare);
        }
        internal static bool CanHelper(Pawn pawn,Pawn guest,bool preview=false,bool allowFeastWake=false)
        {
            if(pawn==null || pawn==guest || !AbundanceRituals.CapableServer(pawn) || !pawn.Awake() && !(preview && allowFeastWake && CanWakeForFeast(pawn)) || pawn.Deathresting || pawn.Suspended ||
                pawn.needs?.food?.Starving==true || pawn.needs?.rest?.CurLevelPercentage<.1f || pawn.health.HasHediffsNeedingTend() ||
                pawn.carryTracker==null || pawn.carryTracker.CarriedThing!=null || pawn.carryTracker.AvailableStackSpace(guest.def)<1 ||
                pawn.CurJob?.playerForced==true)return false;
            if(!preview && IsTransportBusy(pawn))return false;
            return !pawn.WorkTagIsDisabled(WorkTags.ManualDumb);
        }
        static int pathCostTick=-1;
        static readonly Dictionary<Tuple<Pawn,IntVec3,LocalTargetInfo,PathEndMode>,float> pathCosts=new Dictionary<Tuple<Pawn,IntVec3,LocalTargetInfo,PathEndMode>,float>();
        public static float RouteCost(Pawn pawn,LocalTargetInfo target,PathEndMode endMode=PathEndMode.Touch)
        {
            if(pawn?.Spawned!=true || !target.IsValid)return float.PositiveInfinity;
            if(pathCostTick!=Now || pathCosts.Count>256) { pathCosts.Clear();pathCostTick=Now; }
            var key=Tuple.Create(pawn,pawn.Position,target,endMode);
            if(pathCosts.TryGetValue(key,out float cached))return cached;
            using(var path=pawn.Map.pathFinder.FindPathNow(pawn.Position,target,TraverseParms.For(pawn,Danger.Some),peMode:endMode))
            { float cost=path.Found ? path.TotalCost : float.PositiveInfinity;pathCosts[key]=cost;return cost; }
        }
        internal static bool ChairCompatible(Pawn pawn) => !pawn.Downed && pawn.GetPosture()==PawnPosture.Standing;
        public static bool IsRitualSeat(ThingDef def) => def?.building?.isSittable==true && def.building.buildingTags?.Contains("RitualSeat")==true;
        public static bool IsGroundRitualSeat(ThingDef def) => IsRitualSeat(def) && def.altitudeLayer==AltitudeLayer.FloorEmplacement && def.building.sitIgnoreOrientation;
        static IEnumerable<IntVec3> CandidatePlaces(Pawn guest,TargetInfo target,Precept_Ritual ritual)
        {
            Map map=target.Map;
            IEnumerable<IntVec3> places;
            if(AbundanceRituals.IsFoodRitual(ritual))
            {
                var seats=map.listerThings.AllThings.Where(t=>t.Spawned && IsRitualSeat(t.def) && AbundanceBeds.SameRoom(t.Position,target.Cell,map)).ToList();
                // The ideoligion's mats, pillows and pews are audience places in
                // their own right; a table need not touch them. A lying guest may
                // use a floor mat/pillow, or clear ground beside a raised bench.
                var ritualPlaces=seats.SelectMany(t=>ChairCompatible(guest) || IsGroundRitualSeat(t.def) ?
                    t.OccupiedRect().ToList() : GenAdj.CellsAdjacent8Way(t).ToList());
                places=ritualPlaces.Concat(map.listerThings.AllThings.Where(t=>t.Spawned && t.def.IsTable && AbundanceBeds.SameRoom(t.Position,target.Cell,map))
                    .SelectMany(t=>GenAdj.CellsAdjacent8Way(t))).Distinct();
            }
            else places=GenRadial.RadialCellsAround(target.Cell,12f,true).Where(c=>c.InBounds(map) && AudienceCell(c,target,ritual) &&
                    AbundanceBeds.SameRoom(c,target.Cell,map) && GenSight.LineOfSight(c,target.Cell,map,true));
            return places.Where(c=>c.InBounds(map)).OrderBy(c=>c==guest.Position ? 0 : 1)
                .ThenBy(c=>c.GetEdifice(map)?.def==ritual.ideo.RitualSeatDef ? 0 : 1)
                .ThenBy(c=>(ChairCompatible(guest) || IsGroundRitualSeat(c.GetEdifice(map)?.def)) && c.GetEdifice(map)?.def.building?.isSittable==true ? 0 : 1)
                .ThenBy(c=>c.DistanceToSquared(target.Cell)).ThenBy(c=>c.x).ThenBy(c=>c.z);
        }
        static bool AudienceCell(IntVec3 cell,TargetInfo target,Precept_Ritual ritual)
        {
            CellRect focus=target.Thing?.OccupiedRect() ?? CellRect.SingleCell(target.Cell);
            var margin=focus.ExpandedBy(1);
            if(margin.Contains(cell) || (target.Thing is Building building && cell==building.InteractionCell))return false;
            var defaultSides=target.Thing?.def.ritualFocus?.allowedSpectateSides ?? SpectateRectSide.All;
            foreach(var stage in ritual.behavior.def.stages)
            {
                SpectateRectSide sides=stage.allowedSpectateSidesOverride!=SpectateRectSide.None ? stage.allowedSpectateSidesOverride : defaultSides;
                if(target.Thing!=null)sides=sides.Rotated(target.Thing.Rotation);
                bool allowed=cell.z>margin.maxZ && (sides & SpectateRectSide.Up)!=0 || cell.z<margin.minZ && (sides & SpectateRectSide.Down)!=0 ||
                    cell.x>margin.maxX && (sides & SpectateRectSide.Right)!=0 || cell.x<margin.minX && (sides & SpectateRectSide.Left)!=0;
                if(!allowed)return false;
                if(stage.defaultDuty.defName=="SpectateCircle")
                {
                    IntRange distance=stage.spectateDistanceOverride!=IntRange.Zero ? stage.spectateDistanceOverride :
                        target.Thing?.def.ritualFocus?.spectateDistance ?? new IntRange(2,2);
                    int squared=cell.DistanceToSquared(focus.ClosestCellTo(cell));
                    if(squared<distance.min*distance.min || squared>distance.max*distance.max)return false;
                }
            }
            var chair=cell.GetEdifice(target.Map);
            return chair?.def.building?.isSittable!=true || SpectatorCellFinder.IsCorrectlyRotatedChair(cell,chair.Rotation,chair.def,focus);
        }
        internal static bool SafePassingPawn(Pawn passer,Pawn guest) => passer?.Spawned==true && !passer.Dead &&
            !passer.HostileTo(guest) && !passer.InMentalState && !passer.IsBurning() && !NeedsCarrying(passer) &&
            passer.GetPosture()==PawnPosture.Standing;
        internal static bool PlaceSafe(Pawn guest,IntVec3 place,Map map,Building_Bed bed=null,bool allowChair=false,
            bool allowPassingPawns=false,bool allowTransitPassers=false)
        {
            // Native beds are PassThroughOnly: their sleeping slots are walkable,
            // but deliberately not ordinary standing places. Restrict that
            // exception to the valid bed's actual assigned/free sleeping slot.
            if(map==null || !place.IsValid || !place.InBounds(map) ||
                (bed==null ? !place.Standable(map) : !place.Walkable(map)) || place.GetTerrain(map).dangerous ||
                place.GetDangerFor(guest,map)>Danger.Some || place.IsForbidden(guest) ||
                !guest.SafeTemperatureRange().IncludesEpsilon(place.GetTemperature(map)))return false;
            var things=place.GetThingList(map);
            // A settled guest may share a cell briefly with ordinary foot traffic.
            // A carrier may also tolerate it while travelling, but the eventual
            // drop still uses this method's strict default occupancy check.
            bool passersAllowed=allowTransitPassers || (allowPassingPawns && guest.Spawned && guest.Map==map && guest.Position==place);
            if(things.Any(t=>t is Fire || (t is Pawn p && p!=guest &&
                !(passersAllowed && SafePassingPawn(p,guest))) ||
                t.def.category==ThingCategory.Item))return false;
            Building edifice=place.GetEdifice(map);
            // Synthetic and mod beds need not occupy the edifice grid. Require
            // the actual bed at its native sleeping slot, while still rejecting
            // a different blocking edifice. A bed footprint is never floor seating.
            if(bed!=null)return things.Contains(bed) && (edifice==null || edifice==bed) &&
                BedValid(guest,bed,map) && place==RestUtility.GetBedSleepingSlotPosFor(guest,bed);
            if(things.Any(t=>t is Building_Bed))return false;
            return edifice==null || (!edifice.IsBurning() && (IsGroundRitualSeat(edifice.def) ||
                (allowChair && ChairCompatible(guest) && edifice.def.building?.isSittable==true)));
        }
        internal static bool CanReserveDestination(Pawn claimant,IntVec3 place,Building_Bed bed) =>
            claimant?.Spawned==true && place.InBounds(claimant.Map) && (bed==null
                ? claimant.CanReserveSittableOrSpot(place)
                : claimant.CanReserve(place) && claimant.CanReserve(bed,bed.SleepingSlotsCount,0));
        internal static IntVec3 FindApproach(Pawn helper,IntVec3 place,HashSet<IntVec3> occupied=null,Building_Bed bed=null)
        {
            // Large rotated beds can have their sleeping slot inside the
            // footprint. Native take-to-bed approaches the bed, not that cell.
            var candidates=bed!=null ? AbundanceBeds.ApproachCells(bed) : GenAdj.AdjacentCells.Select(v=>place+v);
            return candidates.Where(c=>c.InBounds(helper.Map) && c.Standable(helper.Map) &&
                (occupied==null || !occupied.Contains(c)) && !IsReservedCell(helper.Map,c) && c.GetEdifice(helper.Map)==null &&
                !c.GetThingList(helper.Map).Any(t=>t is Fire || t is Pawn || t is Building_Bed || t.def.category==ThingCategory.Item) &&
                helper.CanReserveAndReach(c,PathEndMode.OnCell,Danger.Some) &&
                CanTouchDestination(helper,c,place,bed))
                .OrderBy(c=>helper.Position.DistanceToSquared(c)).ThenBy(c=>c.x).ThenBy(c=>c.z).DefaultIfEmpty(IntVec3.Invalid).First();
        }
        internal static bool CanTouchDestination(Pawn helper,IntVec3 from,IntVec3 place,Building_Bed bed) =>
            ReachabilityImmediate.CanReachImmediate(from,bed!=null ? new LocalTargetInfo(bed) : new LocalTargetInfo(place),
                helper.Map,PathEndMode.Touch,helper);
        internal static bool BedValid(Pawn guest,Building_Bed bed,Map map)
        {
            if(bed==null || !bed.Spawned || bed.Map!=map || bed.Destroyed || bed.IsBurning() || bed.ForPrisoners || bed.IsForbidden(guest))return false;
            if(!bed.Medical && !bed.OwnersForReading.Contains(guest))return false;
            if(bed.CurOccupants.Any(p=>p!=guest))return false;
            return RestUtility.CanUseBedNow(bed,guest,true);
        }
    }
}
