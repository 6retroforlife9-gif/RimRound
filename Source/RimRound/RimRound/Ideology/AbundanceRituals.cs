using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    public sealed class AbundanceSupply
    {
        public Thing Source;
        public ThingDef OutputDef;
        public Pawn Collector;
        public float UnitMass, UnitBulk;
        public int Count;
        public double Nutrition, Volume;
        public bool AllowsPartial;
        public string ProviderId;
    }
    public sealed class AbundanceServing : IExposable
    {
        public Thing Source;
        public int Count;
        public bool Collected,Placed;
        public Pawn Collector;
        public Pawn BroughtBy;
        public int Trip;
        public ThingDef OutputDef;
        public float UnitMass, UnitBulk;
        public double SourceNutrition, SourceVolume;
        public string SourceProvider;
        public bool Serving;
        public int AcquisitionOrder=-1;
        public void ExposeData()
        {
            Scribe_References.Look(ref Source,"source"); Scribe_Values.Look(ref Count,"count");
            Scribe_Values.Look(ref Collected,"collected"); Scribe_Values.Look(ref Placed,"placed");
            Scribe_References.Look(ref Collector,"collector"); Scribe_Values.Look(ref Trip,"trip");
            Scribe_References.Look(ref BroughtBy,"rrBroughtBy");
            Scribe_Defs.Look(ref OutputDef,"outputDef"); Scribe_Values.Look(ref UnitMass,"unitMass"); Scribe_Values.Look(ref UnitBulk,"unitBulk");
            Scribe_Values.Look(ref SourceNutrition,"sourceNutrition"); Scribe_Values.Look(ref SourceVolume,"sourceVolume");
            Scribe_Values.Look(ref SourceProvider,"sourceProvider");
            Scribe_Values.Look(ref Serving,"serving");
            Scribe_Values.Look(ref AcquisitionOrder,"acquisitionOrder",-1);
        }
    }
    public sealed class AbundanceMeal : IExposable
    {
        public Pawn Eater, Server;
        public IntVec3 Seat;
        // The native target supplies the venue again when an active ritual is loaded.
        public IntVec3 Venue=IntVec3.Invalid;
        public bool AllowOutsideRoomFood=true;
        public IntVec3 EaterSeat=IntVec3.Invalid;
        public double NutritionLeft, VolumeLeft, NutritionEaten;
        public float FullnessCeiling;
        public bool Ceremonial;
        public bool OfficerCare;
        public int OfficerTopUpAttempts;
        public bool Finished;
        // One real dish may be held for a later appetite. A dispenser is only an
        // option until its current finite stock is revalidated and claimed.
        public bool AwaitingAppetite, ReservedForLater, ReserveUnavailable;
        public Thing LaterDispenser;
        public int NextAppetiteCheck;
        public double PlannedGradeNutrition, PlannedNutrition;
        public int PlanVersion;
        public FeastAllocationCompletion Completion;
        public int RetryAfter,FailedAttempts;
        public List<AbundanceServing> Servings=new List<AbundanceServing>();
        public IExposable Conversation;
        public void ExposeData()
        {
            Scribe_References.Look(ref Eater,"eater"); Scribe_References.Look(ref Server,"server");
            Scribe_Values.Look(ref Seat,"seat"); Scribe_Values.Look(ref NutritionLeft,"nutritionLeft");
            Scribe_Values.Look(ref AllowOutsideRoomFood,"rrAllowOutsideRoomFood",true);
            Scribe_Values.Look(ref VolumeLeft,"volumeLeft"); Scribe_Values.Look(ref NutritionEaten,"nutritionEaten");
            Scribe_Values.Look(ref FullnessCeiling,"fullnessCeiling"); Scribe_Values.Look(ref Finished,"finished");
            Scribe_Values.Look(ref PlanVersion,"feastPlanVersion"); Scribe_Values.Look(ref Completion,"feastCompletion");
            Scribe_Values.Look(ref AwaitingAppetite,"rrAwaitingAppetite");
            Scribe_Values.Look(ref ReservedForLater,"rrReservedForLater");
            Scribe_Values.Look(ref ReserveUnavailable,"rrReserveUnavailable");
            Scribe_References.Look(ref LaterDispenser,"rrLaterDispenser");
            Scribe_Values.Look(ref NextAppetiteCheck,"rrNextAppetiteCheck");
            Scribe_Values.Look(ref PlannedGradeNutrition,"rrPlannedGradeNutrition");
            Scribe_Values.Look(ref PlannedNutrition,"rrPlannedNutrition");
            Scribe_Values.Look(ref Ceremonial,"ceremonial");Scribe_Values.Look(ref FailedAttempts,"failedAttempts");
            Scribe_Values.Look(ref OfficerCare,"officerCare");Scribe_Values.Look(ref OfficerTopUpAttempts,"officerTopUpAttempts");
            Scribe_Values.Look(ref RetryAfter,"retryAfter");
            Scribe_Values.Look(ref EaterSeat,"eaterSeat",IntVec3.Invalid);
            Scribe_Deep.Look(ref Conversation,"conversation");
            Scribe_Collections.Look(ref Servings,"servings",LookMode.Deep);
            if(Scribe.mode==LoadSaveMode.PostLoadInit && Servings==null)Servings=new List<AbundanceServing>();
        }
        public OrdinaryIntakeBudget Budget()
        {
            var body=Eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            float target=body==null ? 0 : Mathf.Min(FullnessCeiling,body.HardLimit,
                body.SoftLimit*(Ceremonial || OfficerCare && AbundanceRituals.IsOfficer(Eater) ? 1.1f : 1f),CeilingFloat(body.OccupiedVolume+Math.Max(0,VolumeLeft)));
            return new OrdinaryIntakeBudget((float)Math.Max(0,NutritionLeft),target,false,true);
        }
        static float CeilingFloat(double value)
        {
            float rounded=(float)value;
            if(rounded>=value || float.IsInfinity(rounded) || float.IsNaN(rounded))return rounded;
            int bits=BitConverter.ToInt32(BitConverter.GetBytes(rounded),0);
            return BitConverter.ToSingle(BitConverter.GetBytes(rounded>=0 ? bits+1 : bits-1),0);
        }
    }
    public static class AbundanceRituals
    {
        public const string Ceremony="RR_CeremonialFeeding", Host="RR_host", Attendant="RR_attendant", MealJob="RR_AbundanceMeal", GatherJob="RR_AbundanceGather", SetTableJob="RR_AbundanceSetTable";
        public static Func<Pawn,Pawn,OrdinaryIntakeBudget,IEnumerable<AbundanceSupply>> Supplies;
        public static Func<List<AbundanceMeal>,List<Pawn>,string> PlanSupplies;
        public static Func<Pawn,Pawn,bool> CanShare;
        public static Func<Pawn,Job> SocialJob;
        public static Func<Pawn,Job> SetTable;
        public static Func<Pawn,Job> Collect;
        public static Action<LordJob_AbundanceMeals> UpdateCollection;
        public static Action<LordJob_AbundanceMeals> RefreshOfficerSupplies;
        public static Action<LordJob_AbundanceMeals> CheckLaterAppetites;
        public static bool IsFoodRitual(Precept_Ritual ritual) => ritual?.def.defName==Ceremony || ritual?.def.defName==CommunalFeast.RitualDefName;
        public static LordJob_AbundanceMeals Session(Pawn pawn) => pawn?.GetLord()?.LordJob as LordJob_AbundanceMeals;
        public static bool EligibleHost(Pawn p) => AbundanceRoles.Enabled(p?.Ideo) && BodyModelEligibility.IsEligible(p) &&
            (p.Ideo.GetRole(p)?.def.leaderRole==true || p.Ideo.GetRole(p)?.def.defName=="IdeoRole_Moralist" || AbundanceRoles.Has(p,AbundanceRoles.Feedee));
        public static bool IsOfficer(Pawn pawn)
        {
            var role=ModsConfig.IdeologyActive ? pawn?.Ideo?.GetRole(pawn) : null;
            return role?.Active==true && BodyRoleExpectations.IsRelevantRole(role);
        }
        public static bool OfficerNeedsTopUp(AbundanceMeal meal)
        {
            if(!meal.OfficerCare || meal.ReservedForLater || meal.ReserveUnavailable || !meal.Finished || meal.OfficerTopUpAttempts>=4 || meal.NutritionEaten<=0 || !IsOfficer(meal.Eater))return false;
            var body=meal.Eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body?.UsesBodyModel==true && body.FullnessGainedMultiplier>0 &&
                body.OccupiedVolume<Mathf.Min(body.HardLimit,body.SoftLimit*1.05f)-.0001f;
        }
        public static bool CapableServer(Pawn p) => p?.Spawned==true && p.IsFreeColonist && !p.Downed && !p.Drafted && !p.InMentalState && !p.IsBurning() &&
            p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && p.health.capacities.CapableOf(PawnCapacityDefOf.Moving);
        public static OrdinaryIntakeBudget InitialBudget(Pawn eater,bool ceremonial)
        {
            var body=eater?.TryGetComp<FullnessAndDietStats_ThingComp>();
            float hunger=Mathf.Max(0,eater?.needs?.food?.MaxLevel-eater?.needs?.food?.CurLevel ?? 0);
            if(body?.UsesBodyModel!=true)return new OrdinaryIntakeBudget(hunger,0,false,true);
            float ceiling=Mathf.Min(body.HardLimit,body.SoftLimit*(ceremonial || IsOfficer(eater)?1.1f:1f));
            // Physical room owns the feast allowance, independent of the Food bar.
            // Keep a finite safety ceiling for foods with no physical volume.
            return new OrdinaryIntakeBudget(body.FullnessGainedMultiplier>0f ? 10000f : hunger,ceiling,false,true);
        }
        static bool StartsFull(Pawn eater,bool ceremonial)
        {
            var body=eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            var budget=InitialBudget(eater,ceremonial);
            return budget.Nutrition<=.0001f || body?.UsesBodyModel==true && body.FullnessGainedMultiplier>0 &&
                body.OccupiedVolume>=budget.FullnessTarget-.0001f;
        }
        public static OrdinaryIntakeBudget? BudgetFor(Pawn eater)
        {
            var session=Session(eater);
            var meal=session?.Meals.FirstOrDefault(m=>m.Eater==eater && !m.Finished);
            if(meal==null)return null;
            bool serving=meal.Server?.CurJob?.def.defName==MealJob && meal.Server.CurJob.targetB.Pawn==eater;
            bool gathering=meal.Servings.Any(s=>s.Count>0 && !s.Collected && s.Collector?.CurJob?.def.defName==GatherJob &&
                s.Collector.CurJob.targetA.Thing==s.Source);
            if(!serving && !gathering)return null;
            return meal.Budget();
        }
        public static void Committed(Pawn eater,double nutrition,double volume)
        {
            var session=Session(eater); var meal=session?.Meals.FirstOrDefault(m=>m.Eater==eater && !m.Finished);
            if(meal==null || meal.Server?.CurJob?.def.defName!=MealJob || !BudgetFor(eater).HasValue || nutrition<=0)return;
            meal.FailedAttempts=0;
            meal.NutritionEaten+=nutrition; meal.NutritionLeft=Math.Max(0,meal.NutritionLeft-nutrition);
            var body=eater.TryGetComp<FullnessAndDietStats_ThingComp>();
            meal.VolumeLeft=Math.Max(0,meal.VolumeLeft-volume*(body?.FullnessGainedMultiplier ?? 1f));
            meal.Finished=meal.NutritionLeft<0.0001 || body?.UsesBodyModel==true && body.FullnessGainedMultiplier>0 && meal.VolumeLeft<0.0001;
            var food=meal.Server.CurJob.targetA.Thing;
            CommunalFeast.MealData(session)?.Record(eater,(float)nutrition,CommunalFeast.MealGrade(food?.def));
        }
        public static float DigestionMultiplier(Pawn pawn)
        {
            var reward=DefDatabase<HediffDef>.GetNamedSilentFail("RR_AbundantDigestion");
            return reward==null ? 1f : 1f+Mathf.Clamp(pawn.health.hediffSet.GetFirstHediffOfDef(reward)?.Severity ?? 0f,0f,.5f);
        }
        public static string Validate(TargetInfo target,Precept_Ritual ritual,RitualRoleAssignments assignments,out List<AbundanceMeal> meals)
        { return Validate(target,ritual,assignments,out meals,out var guests); }
        public static string Validate(TargetInfo target,Precept_Ritual ritual,RitualRoleAssignments assignments,out List<AbundanceMeal> meals,out List<RitualAttendanceGuest> guests)
        {
            string failure=ValidateCore(target,ritual,assignments,out meals,out guests);
            FeastDiagnostics.Publish(target,assignments,meals,guests,failure);
            return failure;
        }
        static string ValidateCore(TargetInfo target,Precept_Ritual ritual,RitualRoleAssignments assignments,out List<AbundanceMeal> meals,out List<RitualAttendanceGuest> guests)
        {
            meals=new List<AbundanceMeal>();
            guests=null;
            if(!target.IsValid || target.Map==null || assignments==null || Supplies==null)return "RR_AbundanceInvalid".Translate();
            foreach(var participant in assignments.Participants)
            {
                string risk=RitualBehaviorWorker_CommunalFeast.FeastStartRisk(participant);
                if(risk!=null)return risk;
            }
            bool ceremonial=ritual.def.defName==Ceremony;
            var bed=target.Thing as Building_Bed;
            Pawn host=assignments.FirstAssignedPawn(Host), server=assignments.FirstAssignedPawn(Attendant);
            if(bed!=null)
            {
                if(!AbundanceBeds.Valid(bed,ritual.ideo) || host!=AbundanceBeds.Focus(bed).Owner || !AbundanceBeds.ReadyForBegin(host))return "RR_AbundanceHostRequired".Translate();
                if(!CapableServer(server) || server==host || !AbundanceBeds.Approach(server,bed).IsValid)return "RR_AbundanceServerRequired".Translate();
                if(host.CurrentBed()!=bed && !RitualAttendance.NeedsCarrying(host) && !host.CanReserveAndReach(bed,PathEndMode.OnCell,Danger.Some))return "RR_AbundanceHostCannotSettle".Translate();
            }
            else if(ceremonial)return "RR_AbundanceHostRequired".Translate();
            if(bed!=null && !AbundanceBeds.HasTable(bed))return "RR_FeastRequiresTable".Translate();
            string attendanceReason=RitualAttendance.Validate(target,ritual,assignments,out guests);
            if(attendanceReason!=null)return attendanceReason;
            var plannedGuests=guests;
            var collectors=assignments.Participants.Where(p=>p!=host && CapableServer(p))
                .OrderByDescending(p=>p.GetStatValue(StatDefOf.MoveSpeed)).ThenBy(p=>p.Position.DistanceToSquared(target.Cell)).ThenBy(p=>p.thingIDNumber).ToList();
            if(collectors.Count==0)return "RR_FeastCollectorRequired".Translate();
            var diners=(ceremonial ? new[]{host} : assignments.Participants.Where(p=>p.needs?.food!=null))
                .Where(p=>p!=null).Distinct().ToList();
            var seats=AbundanceBeds.DiningSeats(target.Map,target.Cell).Distinct()
                .Where(c=>!plannedGuests.Any(g=>g.Place==c || g.Approach==c)).ToList();
            var full=new HashSet<Pawn>(diners.Where(p=>StartsFull(p,ceremonial)));
            var mobile=diners.Where(p=>p!=host && !plannedGuests.Any(g=>g.Guest==p) && !full.Contains(p)).OrderBy(p=>p.thingIDNumber).ToList();
            if (mobile.Count > FeastSeatPlanner.MaximumDiners || seats.Count > FeastSeatPlanner.MaximumSeats)
                return "RR_FeastPlanningLimit".Translate();
            var seatGraph=mobile.Select(p=>(IReadOnlyList<int>)Enumerable.Range(0,seats.Count)
                .Where(i=>p.CanReserveSittableOrSpot(seats[i]) && p.CanReach(seats[i],PathEndMode.OnCell,Danger.Some))
                .OrderBy(i=>p.Position.DistanceToSquared(seats[i])).ThenBy(i=>seats[i].x).ThenBy(i=>seats[i].z).ToArray()).ToArray();
            if(!FeastSeatPlanner.TryAssign(seatGraph,seats.Count,out var seatAssignment))
            {
                int available = seatGraph.SelectMany(options => options).Distinct().Count();
                if (mobile.Count > available) return "RR_AbundanceSeats".Translate(mobile.Count, available);
                int noSeat = Array.FindIndex(seatGraph, options => options.Count == 0);
                if (noSeat >= 0) return "RR_AbundanceSeatUnavailable".Translate(mobile[noSeat].LabelShort);
                if (seatGraph.Sum(options => options.Count) > FeastSeatPlanner.MaximumEdges)
                    return "RR_FeastPlanningLimit".Translate();
                return "RR_FeastConflictingSeats".Translate();
            }
            var assignedSeats=mobile.Select((p,i)=>new{Pawn=p,Cell=seats[seatAssignment[i]]}).ToDictionary(a=>a.Pawn,a=>a.Cell);
            foreach(var p in diners.Where(p=>p!=host && full.Contains(p) && !plannedGuests.Any(g=>g.Guest==p)))
            {
                // Full guests can share a later sitting. Prefer a free chair but
                // never displace someone already committed to eating there.
                var seat=seats.Where(c=>p.CanReserveSittableOrSpot(c) && p.CanReach(c,PathEndMode.OnCell,Danger.Some))
                    .OrderBy(c=>assignedSeats.Values.Count(v=>v==c)).ThenBy(c=>p.Position.DistanceToSquared(c))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
                if(!seat.IsValid)return "RR_AbundanceSeatUnavailable".Translate(p.LabelShort);
                assignedSeats.Add(p,seat);
            }
            var allocatedMeals=meals;
            foreach(Pawn diner in diners.OrderBy(p=>p.thingIDNumber))
            {
                var guest=plannedGuests.FirstOrDefault(g=>g.Guest==diner);
                Pawn servingHelper=diner==host ? server : guest!=null ? collectors.Where(p=>p!=diner &&
                    (ceremonial || !IsOfficer(p)) &&
                    p.CanReserveAndReach(guest.Approach,PathEndMode.OnCell,Danger.Some))
                    .OrderBy(p=>allocatedMeals.Count(m=>m.Server==p && m.Eater!=p))
                    .ThenByDescending(p=>AbundanceRoles.Has(p,AbundanceRoles.Feeder)).FirstOrDefault() : diner;
                if(!CapableServer(servingHelper))return "RR_FeastServerForGuest".Translate(diner.LabelShort);
                var budget=InitialBudget(diner,ceremonial);
                bool waiting=full.Contains(diner);
                if(waiting && ceremonial)return "RR_AbundanceNoDiners".Translate();
                IntVec3 seat=IntVec3.Invalid;
                if(diner==host)seat=guest!=null ? guest.Approach : AbundanceBeds.Approach(servingHelper,bed);
                else if(guest!=null)seat=guest.Approach;
                else
                {
                    seat=assignedSeats[diner];
                }
                if(!seat.IsValid || !servingHelper.CanReserveSittableOrSpot(seat) || !servingHelper.CanReach(seat,PathEndMode.OnCell,Danger.Some))
                    return "RR_AbundanceSeatUnavailable".Translate(diner.LabelShort);
                meals.Add(new AbundanceMeal{Eater=diner,Server=servingHelper,Seat=seat,Venue=target.Cell,EaterSeat=guest!=null ? guest.Place : IntVec3.Invalid,NutritionLeft=budget.Nutrition,
                    VolumeLeft=diner.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel==true ? Math.Max(0,budget.FullnessTarget-diner.TryGetComp<FullnessAndDietStats_ThingComp>().OccupiedVolume) : double.MaxValue,
                    FullnessCeiling=budget.FullnessTarget,Ceremonial=ceremonial,OfficerCare=!ceremonial && IsOfficer(diner),
                    AwaitingAppetite=waiting,ReservedForLater=waiting});
            }
            if(!ceremonial)
            {
                // Feedee is a permanent role; receiving a shared meal never
                // replaces it or the leader's/moral guide's role.
                foreach(var meal in meals.Where(m=>m.Eater!=host && !plannedGuests.Any(g=>g.Guest==m.Eater) &&
                    (IsOfficer(m.Eater) || AbundanceRoles.Has(m.Eater,AbundanceRoles.Feedee))))
                {
                    bool officer=IsOfficer(meal.Eater);
                    // Officers receive service even after the helper's own meal.
                    // Excluding other officers prevents two recipients waiting
                    // on each other instead of either starting their serving job.
                    var helpers=assignments.Participants.Where(p=>p!=meal.Eater && !IsOfficer(p) && CapableServer(p) &&
                        (officer || AbundanceRoles.Has(p,AbundanceRoles.Feeder) && CanShare?.Invoke(p,meal.Eater)==true))
                        .OrderBy(p=>allocatedMeals.Count(m=>m.Server==p && m.Eater!=p))
                        .ThenByDescending(p=>AbundanceRoles.Has(p,AbundanceRoles.Feeder)).ThenBy(p=>p.thingIDNumber).ToList();
                    if(helpers.Count==0)
                    {
                        if(officer && !meal.AwaitingAppetite)return "RR_FeastServerForGuest".Translate(meal.Eater.LabelShort);
                        continue;
                    }
                    Pawn helper=null;IntVec3 near=IntVec3.Invalid;
                    foreach(Pawn candidate in helpers)
                    {
                        near=GenAdj.AdjacentCells.Select(v=>meal.Seat+v).Where(c=>c.InBounds(target.Map) && c.Standable(target.Map) &&
                        AbundanceBeds.SameRoom(c,target.Cell,target.Map) && !allocatedMeals.Any(m=>m.Seat==c || m.EaterSeat==c) &&
                        !plannedGuests.Any(g=>g.Place==c || g.Approach==c) && candidate.CanReserveSittableOrSpot(c) && candidate.CanReach(c,PathEndMode.OnCell,Danger.Some))
                        .DefaultIfEmpty(IntVec3.Invalid).First();
                        if(near.IsValid) { helper=candidate;break; }
                    }
                    if(!near.IsValid)
                    {
                        if(officer && !meal.AwaitingAppetite)return "RR_FeastServerSpace".Translate(meal.Eater.LabelShort);
                        continue;
                    }
                    meal.EaterSeat=meal.Seat;meal.Seat=near;meal.Server=helper;
                }
            }
            return meals.Count==0 ? "RR_AbundanceNoDiners".Translate().ToString() :
                PlanSupplies==null ? "RR_AbundanceInvalid".Translate().ToString() : PlanSupplies(meals,collectors);
        }
    }
    public sealed class RitualRole_AbundanceHost : RitualRole
    {
        public override bool AppliesToPawn(Pawn p,out string reason,TargetInfo selectedTarget,LordJob_Ritual ritual=null,RitualRoleAssignments assignments=null,Precept_Ritual precept=null,bool skipReason=false)
        {
            reason=null;
            bool valid=selectedTarget.Thing is Building_Bed bed && AbundanceBeds.Focus(bed)?.Matches(p)==true && AbundanceBeds.ReadyForBegin(p) && AbundanceRituals.EligibleHost(p);
            if(!valid && !skipReason)reason="RR_AbundanceHostRequired".Translate();
            return valid;
        }
        public override bool AppliesToRole(Precept_Role role,out string reason,Precept_Ritual ritual=null,Pawn pawn=null,bool skipReason=false)
        { reason=null; return AbundanceRituals.EligibleHost(pawn); }
    }
    public sealed class RitualRole_AbundanceAttendant : RitualRole
    {
        public override bool AppliesToPawn(Pawn p, out string reason, TargetInfo selectedTarget,
            LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            if (ritual == null && selectedTarget.IsValid && !(selectedTarget.Thing is Building_Bed) &&
                (p == null || assignments?.FirstAssignedPawn(id) != p))
            {
                reason = skipReason ? null : "RR_FeastBedOnlyAttendant".Translate().ToString();
                return false;
            }
            return base.AppliesToPawn(p, out reason, selectedTarget, ritual, assignments, precept, skipReason);
        }

        public override bool AppliesToRole(Precept_Role role,out string reason,Precept_Ritual ritual=null,Pawn pawn=null,bool skipReason=false)
        { reason=null; bool valid=AbundanceRituals.CapableServer(pawn); if(!valid && !skipReason)reason="RR_AbundanceServerRequired".Translate();return valid; }
    }
    public sealed class LordJob_AbundanceMeals : LordJob_Ritual
    {
        public List<AbundanceMeal> Meals=new List<AbundanceMeal>();
        public List<RitualAttendanceGuest> PlannedGuests;
        public int CollectionVersion=2,NextTrip=1;
        public int NextCollectionCheck;
        public int AdditionalFoodPlanned;
        public bool InitialSittingFinished;
        public int LastServingDeadline=-1;
        public Pawn HostPawn;
        public Building_Bed Bed;
        public Precept_Role HostRole;
        public int CreatedTick,LastProgressTick,HostRestJob=-1,NextSocialTick,SocialInteractions;
        public bool FoodReady,CancelRequested;
        public string CancellationReason;
        public string CancellationSubject;
        string preparationState;
        bool cancellationReported;
        int nextProgressCheck;
        public const int PreparationStallTicks=6000;
        public bool Ceremonial => Ritual.def.defName==AbundanceRituals.Ceremony;
        public LordJob_AbundanceMeals() { }
        public LordJob_AbundanceMeals(TargetInfo target,Precept_Ritual ritual,RitualObligation obligation,RitualRoleAssignments assignments,List<AbundanceMeal> meals,List<RitualAttendanceGuest> guests=null)
            :base(target,ritual,obligation,ritual.behavior.def.stages,assignments)
        {
            Meals=meals; PlannedGuests=guests; HostPawn=assignments.FirstAssignedPawn(AbundanceRituals.Host); Bed=target.Thing as Building_Bed;
            HostRole=HostPawn?.Ideo?.GetRole(HostPawn); CreatedTick=LastProgressTick=Find.TickManager.TicksGame;
            if(HostPawn?.CurrentBed()==Bed && Bed!=null)HostRestJob=HostPawn.CurJob.loadID;
        }
        public bool HostSettled => Bed==null || (HostPawn?.CurrentBed()==Bed && HostPawn.GetPosture()==PawnPosture.LayingInBed);
        // Room structure is shared by all diners; refresh once per second,
        // including immediately after reload, instead of scanning per job/tick.
        int nextTableCheck;
        bool tablesRemain=true;
        public override IEnumerable<Pawn> PawnsToCountTowardsPresence => base.PawnsToCountTowardsPresence.Where(p=>
            p.Spawned && AbundanceBeds.SameRoom(p.Position,selectedTarget.Cell,Map));
        protected override bool ShouldBeCalledOff() => !Valid() || base.ShouldBeCalledOff();
        public void RequestCancellation(string reason,string subject=null)
        { CancelRequested=true;if(CancellationReason==null) { CancellationReason=reason;CancellationSubject=subject; } }
        public void MissedReserve(AbundanceMeal meal)
        {
            if(meal?.ReservedForLater==true)StopServing(meal);
        }
        public void StopServing(AbundanceMeal meal)
        {
            if(meal==null || meal.ReserveUnavailable)return;
            if(Ceremonial) { RequestCancellation("RR_FeastSupplyChanged");return; }
            // The original save field for an unavailable reserved dish now also
            // retires an ordinary diner's remaining service. Keep earned intake.
            meal.ReserveUnavailable=true;
            meal.AwaitingAppetite=false;meal.Finished=true;meal.LaterDispenser=null;
            foreach(var serving in meal.Servings)
            { serving.Count=0;serving.Source=null; }
            if(meal.ReservedForLater && meal.Eater?.Spawned==true && !meal.Eater.Drafted && !meal.Eater.InMentalState && meal.Eater.GetLord()==lord)
                Messages.Message("RR_FeastLateMealUnavailable".Translate(meal.Eater.LabelShort),meal.Eater,MessageTypeDefOf.NeutralEvent,false);
        }
        public bool ServiceParticipantsAvailable(AbundanceMeal meal)
        {
            var eater=meal.Eater;
            return eater!=null && !eater.Dead && !eater.Drafted && !eater.InMentalState &&
                (eater.Spawned && eater.GetLord()==lord || RitualAttendance.IsOwnedTransport(this,eater)) &&
                AbundanceRituals.CapableServer(meal.Server) && meal.Server.GetLord()==lord;
        }
        public bool AtMealPlace(AbundanceMeal meal) => meal.Eater==meal.Server ||
            (meal.Eater==HostPawn ? HostSettled : meal.Eater?.Spawned==true && meal.Eater.Position==meal.EaterSeat);
        public bool HasAssignedSeat(AbundanceMeal meal) => meal.Finished || meal.Eater==HostPawn ||
            RitualAttendance.For(this)?.Guests.Any(g=>g.Guest==meal.Eater)==true ||
            AbundanceBeds.DiningSeatRemains(Map,meal.EaterSeat.IsValid ? meal.EaterSeat : meal.Seat,selectedTarget.Cell);
        public override string GetJobReport(Pawn pawn) => FeastDiagnostics.Waiting(this) ?? base.GetJobReport(pawn);
        void CheckPreparationProgress()
        {
            int now=Find.TickManager.TicksGame;
            if(FoodReady || now<nextProgressCheck)return;
            nextProgressCheck=now+60;
            // Only custody, attendance, and actual preparation travel count.
            // Idle spectators and repeated failed jobs cannot keep a wait alive.
            string state=string.Join(";",Meals.SelectMany(m=>m.Servings).Select(s=>
                (s.Source?.thingIDNumber ?? -1)+":"+s.Count+":"+s.Collected+":"+s.Placed))+"|"+
                string.Join(";",(RitualAttendance.For(this)?.Guests ?? new List<RitualAttendanceGuest>()).Select(g=>g.Guest.thingIDNumber+":"+g.State))+"|"+
                string.Join(";",lord.ownedPawns.OrderBy(p=>p.thingIDNumber).Where(p=>
                    p.CurJob?.def.defName==AbundanceRituals.GatherJob || p.CurJob?.def.defName==AbundanceRituals.SetTableJob ||
                    p.CurJob?.def.defName==RitualAttendance.CarryJob || p==HostPawn && !HostSettled ||
                    p.CurJob?.def==JobDefOf.SpectateCeremony && p.pather.Moving &&
                        Meals.Any(m=>m.Server==p && m.Seat==p.CurJob.targetA.Cell))
                    .Select(p=>p.thingIDNumber+":"+p.PositionHeld));
            if(preparationState!=null && state!=preparationState)LastProgressTick=now;
            preparationState=state;
            if(now-LastProgressTick>=PreparationStallTicks)RequestCancellation("RR_FeastNoProgress");
        }
        void CancelWithExplanation()
        {
            if(!cancellationReported && CancellationReason!=null)
            { Messages.Message(CancellationSubject==null ? CancellationReason.Translate() : CancellationReason.Translate(CancellationSubject),selectedTarget,MessageTypeDefOf.NegativeEvent);cancellationReported=true; }
            Cancel();
        }
        public bool Valid()
        {
            if(CancelRequested)return false;
            if(selectedTarget.Thing?.Destroyed==true || !Ritual.ideo.PreceptsListForReading.Contains(Ritual))return false;
            if(Find.TickManager.TicksGame>=nextTableCheck)
            {
                tablesRemain=AbundanceBeds.HasTable(Map,selectedTarget.Cell);
                nextTableCheck=Find.TickManager.TicksGame+60;
            }
            if(!tablesRemain)return false;
            var lostSeat=Ceremonial ? Meals.FirstOrDefault(m=>!HasAssignedSeat(m)) : null;
            if(lostSeat!=null) { RequestCancellation("RR_AbundanceSeatUnavailable",lostSeat.Eater?.LabelShort);return false; }
            if(Bed!=null && (!AbundanceBeds.Valid(Bed,Ritual.ideo) ||
                (!AbundanceBeds.Ready(HostPawn) && !RitualAttendance.IsOwnedTransport(this,HostPawn)) || HostPawn.Ideo.GetRole(HostPawn)!=HostRole ||
                (HostRestJob>=0 && (!HostSettled || HostPawn.CurJob?.loadID!=HostRestJob))))return false;
            return (!Ceremonial || Meals.All(ServiceParticipantsAvailable)) &&
                Find.TickManager.TicksGame-CreatedTick<30000;
        }
        public override void LordJobTick()
        {
            if(CancelRequested) { CancelWithExplanation();return; }
            RitualAttendance.Ensure(this,PlannedGuests); PlannedGuests=null;
            if(!Ceremonial)
                foreach(var meal in Meals.Where(m=>!m.ReserveUnavailable && (!ServiceParticipantsAvailable(m) || !HasAssignedSeat(m))))
                    StopServing(meal);
            AbundanceRituals.UpdateCollection?.Invoke(this);
            CheckPreparationProgress();
            if(!Valid()) { CancelWithExplanation();return; }
            foreach(var meal in Meals.Where(m=>!m.Finished))
            {
                var body=meal.Eater.TryGetComp<FullnessAndDietStats_ThingComp>();
                if(body?.UsesBodyModel==true && body.FullnessGainedMultiplier>0 && body.OccupiedVolume>=meal.Budget().FullnessTarget-.0001f)
                    meal.Finished=true;
            }
            // A serving allowance is not a guarantee of live fullness: digestion
            // continues during collection and chewing. Bounded officer rounds
            // quote real additional stock after the current sitting is finished.
            if(!InitialSittingFinished && (Meals.Any(m=>m.OfficerTopUpAttempts>0) ||
                FoodReady && Meals.All(m=>m.Finished) && (!Ceremonial || Meals.Any(m=>m.NutritionEaten>0))))InitialSittingFinished=true;
            if(!Ceremonial && FoodReady && HostSettled && Find.TickManager.TicksGame%60==0 && Progress<1f)
                AbundanceRituals.CheckLaterAppetites?.Invoke(this);
            AbundanceRituals.RefreshOfficerSupplies?.Invoke(this);
            // A changed physical capacity can remove every appetite before the
            // first bite. End cleanly instead of waiting out a zero-progress feast.
            if(Ceremonial && Meals.All(m=>m.Finished) && !Meals.Any(m=>m.NutritionEaten>0)) { Cancel();return; }
            if(Progress>=1f && LastServingDeadline<0)LastServingDeadline=Find.TickManager.TicksGame+1200;
            if(HostSettled && Bed!=null && HostRestJob<0)HostRestJob=HostPawn.CurJob.loadID;
            if(!FoodReady && RitualAttendance.AllSettled(this) && Meals.All(m=>m.Servings.All(s=>s.Count<=0 || s.Placed || s.Serving)) &&
                Meals.Where(m=>!m.ReserveUnavailable).Select(m=>m.Server).Distinct().All(p=>AbundanceBeds.SameRoom(p.Position,selectedTarget.Cell,Map)))FoodReady=true;
            base.LordJobTick();
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Collections.Look(ref Meals,"rrMeals",LookMode.Deep);
            Scribe_References.Look(ref HostPawn,"rrHost"); Scribe_References.Look(ref HostRole,"rrHostRole");
            Scribe_References.Look(ref Bed,"rrBed"); Scribe_Values.Look(ref CreatedTick,"rrCreatedTick");
            Scribe_Values.Look(ref NextSocialTick,"rrNextSocialTick");Scribe_Values.Look(ref SocialInteractions,"rrSocialInteractions");
            Scribe_Values.Look(ref FoodReady,"rrFoodReady");
            Scribe_Values.Look(ref AdditionalFoodPlanned,"rrAdditionalFoodPlanned");
            Scribe_Values.Look(ref InitialSittingFinished,"rrInitialSittingFinished");
            Scribe_Values.Look(ref LastServingDeadline,"rrLastServingDeadline",-1);
            Scribe_Values.Look(ref CancelRequested,"rrCancelRequested");
            Scribe_Values.Look(ref CancellationReason,"rrCancellationReason");
            Scribe_Values.Look(ref CancellationSubject,"rrCancellationSubject");
            Scribe_Values.Look(ref cancellationReported,"rrCancellationReported");
            Scribe_Values.Look(ref LastProgressTick,"rrLastPreparationProgress",CreatedTick);
            Scribe_Values.Look(ref preparationState,"rrPreparationState");
            Scribe_Values.Look(ref HostRestJob,"rrHostRestJob",-1);
            Scribe_Values.Look(ref CollectionVersion,"rrCollectionVersion",0); Scribe_Values.Look(ref NextTrip,"rrNextTrip",1);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)
            {
                if(Ritual?.def?.useRepeatPenalty==false)repeatPenalty=false;
                if(Meals==null)Meals=new List<AbundanceMeal>();
                // Older in-flight plans used an effectively unbounded nutrition
                // allowance. Cancel through the normal physical return path;
                // never replay their intake or reconstruct consumed servings.
                if(Meals.Any(m=>m.PlanVersion<1))CancelRequested=true;
                foreach(var meal in Meals) { meal.Ceremonial=Ceremonial;meal.Venue=selectedTarget.Cell; }
                if(Meals.Any(m=>m.Servings.Any(s=>s.Count>0 && !s.Placed && !s.Serving)))FoodReady=false;
            }
        }
    }
    public sealed class JobGiver_AbundanceMeal : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var ritual=AbundanceRituals.Session(pawn);
            if(ritual==null || !ritual.Valid())return null;
            Job transport=RitualAttendance.TryGiveTransportJob(pawn);
            if(transport!=null)return transport;
            if(RitualAttendance.IsGuest(ritual,pawn))return null;
            if(pawn==ritual.HostPawn)
            {
                if(ritual.HostSettled)return null;
                return JobMaker.MakeJob(JobDefOf.LayDown,ritual.Bed);
            }
            var receiving=ritual.Meals.FirstOrDefault(m=>m.Eater==pawn && m.Server!=pawn && !m.Finished && m.EaterSeat.IsValid);
            if(!ritual.FoodReady)
            {
                if(!RitualAttendance.AllSettled(ritual))return null;
                Job collect=AbundanceRituals.Collect?.Invoke(pawn);
                if(collect!=null)return collect;
                if(receiving!=null)return WaitAtPlace(pawn,ritual,receiving.EaterSeat);
                // Native social waiting can leave a collector outside the venue
                // after all food is staged, permanently blocking FoodReady.
                // Keep finished/zero-fit servers too: presence counts them.
                var waiting=ritual.Meals.Where(m=>m.Server==pawn)
                    .OrderBy(m=>m.Eater==pawn ? 0 : 1).FirstOrDefault();
                if(waiting==null || !waiting.Seat.IsValid ||
                    !AbundanceBeds.SameRoom(waiting.Seat,ritual.selectedTarget.Cell,pawn.Map) ||
                    !pawn.CanReserveSittableOrSpot(waiting.Seat) ||
                    !pawn.CanReach(waiting.Seat,PathEndMode.OnCell,Danger.Some))return null;
                var returnToSeat=JobMaker.MakeJob(JobDefOf.SpectateCeremony,waiting.Seat,
                    ritual.selectedTarget.Cell,waiting.Seat.GetEdifice(pawn.Map));
                returnToSeat.expiryInterval=180;returnToSeat.checkOverrideOnExpire=true;return returnToSeat;
            }
            if(ritual.Meals.Any(m=>m.Servings.Any(s=>s.Collector==pawn && s.Count>0 && s.Collected && !s.Placed && !s.Serving)))
                return AbundanceRituals.SetTable?.Invoke(pawn);
            if(receiving!=null)return WaitAtPlace(pawn,ritual,receiving.EaterSeat);
            var meal=ritual.Meals.Where(m=>m.Server==pawn && !m.Finished && ritual.AtMealPlace(m) && ritual.ServiceParticipantsAvailable(m) && Find.TickManager.TicksGame>=m.RetryAfter)
                .OrderBy(m=>m.Eater==pawn ? 1 : 0).FirstOrDefault();
            if(meal==null)return ritual.Meals.Any(m=>m.Server==pawn && !m.Finished) ? null : AbundanceRituals.SocialJob?.Invoke(pawn);
            var planned=meal.Servings.FirstOrDefault(s=>s.Placed && s.Count>0 && s.Source?.Spawned==true &&
                !s.Source.IsForbidden(pawn) && AbundanceBeds.SameRoom(s.Source.Position,ritual.selectedTarget.Cell,pawn.Map));
            Thing source=planned?.Source;
            if(source==null)
            {
                if(meal.Servings.Any(s=>s.Count>0 && !s.Placed && s.Source!=null && !s.Source.Destroyed && s.Collector?.GetLord()==ritual.lord))return null;
                ritual.StopServing(meal);
                return null;
            }
            if(!pawn.CanReserveSittableOrSpot(meal.Seat) || (meal.Eater!=pawn && !pawn.CanReserve(meal.Eater)) || !pawn.CanReserve(source,10,1))return null;
            var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(AbundanceRituals.MealJob),source,meal.Eater,meal.Seat);
            job.count=1; job.ingestTotalCount=true; return job;
        }
        static Job WaitAtPlace(Pawn pawn,LordJob_AbundanceMeals ritual,IntVec3 place)
        {
            if(!pawn.CanReserveSittableOrSpot(place) || !pawn.CanReach(place,PathEndMode.OnCell,Danger.Some))return null;
            IntVec3 watch=ritual.selectedTarget.Cell;
            foreach(IntVec3 offset in GenAdj.CardinalDirections)
                if((place+offset).InBounds(pawn.Map) && (place+offset).GetThingList(pawn.Map).Any(FeastCompatibilityPolicy.IsDiningSurface))
                { watch=place+offset;break; }
            var wait=JobMaker.MakeJob(JobDefOf.SpectateCeremony,place,watch,place.GetEdifice(pawn.Map));
            wait.expiryInterval=180;wait.checkOverrideOnExpire=true;return wait;
        }
    }
    public sealed class RitualStage_AbundanceMeals : RitualStage
    {
        public override float ProgressPerTick(LordJob_Ritual ritual)
        {
            if(!(ritual is LordJob_AbundanceMeals meals))return base.ProgressPerTick(ritual);
            if(!meals.HostSettled)return 0f;
            if(!meals.Ceremonial)
                return (meals.FoodReady || meals.InitialSittingFinished) && ritual.Progress<1f && meals.PawnsToCountTowardsPresence.Any() ? 1f : 0f;
            // Optional officer refills run alongside the remaining ceremony.
            // Pausing its clock for those refills creates more digestion and
            // can prolong the very service they are intended to support.
            if(meals.InitialSittingFinished)return ritual.Progress<1f ? 1f : 0f;
            if(!meals.FoodReady || !meals.Meals.Any(m=>m.NutritionEaten>0))return 0f;
            return meals.Meals.All(m=>m.Finished) || ritual.Progress<.5f ? 1f : 0f;
        }
    }
    public sealed class StageEndTrigger_AbundanceMeals : StageEndTrigger
    {
        public override bool CountsTowardsProgress=>true;
        public override Trigger MakeTrigger(LordJob_Ritual ritual,TargetInfo spot,IEnumerable<TargetInfo> foci,RitualStage stage) =>
            new Trigger_TickCondition(()=>ritual is LordJob_AbundanceMeals meals &&
                (!meals.Ceremonial || meals.InitialSittingFinished || meals.Meals.All(m=>m.Finished)) && ritual.TicksPassedWithProgress>=ritual.DurationTicks &&
                (!meals.Meals.Any(m=>!m.Finished && (!meals.Ceremonial || m.ReservedForLater)) ||
                    meals.LastServingDeadline>=0 && Find.TickManager.TicksGame>=meals.LastServingDeadline));
    }
    public sealed class RitualOutcomeComp_AbundanceService : RitualOutcomeComp_Quality
    {
        public override bool DataRequired=>false;
        public override float Count(LordJob_Ritual ritual,RitualOutcomeComp_Data data) => ritual is LordJob_AbundanceMeals meals &&
            meals.Meals.Any(m=>m.Server!=m.Eater && m.NutritionEaten>0 && AbundanceRoles.Has(m.Server,AbundanceRoles.Feeder)) ? 1f : 0f;
        public override QualityFactor GetQualityFactor(Precept_Ritual ritual,TargetInfo target,RitualObligation obligation,
            RitualRoleAssignments assignments,RitualOutcomeComp_Data data)
        {
            bool planned=FeastDiagnostics.Projection(ritual,target,assignments)?.Feeder==true;
            float value=curve.Evaluate(planned ? 1f : 0f);
            return new QualityFactor {label=label.CapitalizeFirst(),count=planned ? "1 / 1" : "0 / 1",
                quality=value,qualityChange=ExpectedOffsetDesc(true,value),positive=value>0};
        }
    }
    public sealed class RitualOutcomeEffectWorker_CeremonialFeeding : RitualOutcomeEffectWorker_CommunalFeast
    {
        public RitualOutcomeEffectWorker_CeremonialFeeding() { }
        public RitualOutcomeEffectWorker_CeremonialFeeding(RitualOutcomeEffectDef def):base(def) { }
        protected override void ApplyExtraOutcome(Dictionary<Pawn,int> presence,LordJob_Ritual ritual,RitualOutcomePossibility outcome,out string text,ref LookTargets targets)
        {
            text=null;
            if(!(ritual is LordJob_AbundanceMeals meals) || meals.cancelled || outcome.positivityIndex<1 ||
                !meals.Meals.Any(m=>m.Eater==meals.HostPawn && m.NutritionEaten>0))return;
            var reward=DefDatabase<HediffDef>.GetNamed("RR_AbundantDigestion");
            var previous=meals.HostPawn.health.hediffSet.GetFirstHediffOfDef(reward);
            if(previous!=null)meals.HostPawn.health.RemoveHediff(previous);
            var effect=HediffMaker.MakeHediff(reward,meals.HostPawn); effect.Severity=outcome.positivityIndex>=2 ? .5f : .25f;
            meals.HostPawn.health.AddHediff(effect);
            text="RR_AbundanceDigestionReward".Translate((effect.Severity*100).ToString("0"));
        }
    }
    [HarmonyPatch(typeof(Pawn_JobTracker),nameof(Pawn_JobTracker.CheckForJobOverride))]
    internal static class AbundanceKeepRest
    { private static bool Prefix(Pawn ___pawn) { var r=AbundanceRituals.Session(___pawn);return r?.HostPawn!=___pawn || !r.HostSettled; } }
    [HarmonyPatch(typeof(RestUtility),nameof(RestUtility.CanFallAsleep))]
    internal static class AbundanceKeepAwake
    { private static void Postfix(Pawn pawn,ref bool __result) { var r=AbundanceRituals.Session(pawn); if(r?.HostPawn==pawn && pawn.Awake() && pawn.needs.rest.CurLevelPercentage>.05f)__result=false; } }
}
