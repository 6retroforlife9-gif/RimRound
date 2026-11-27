using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.AI;
using RimRound.Comps;
using RimRound.Ideology;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    // The visit is a distinct duty stream: eating/sharing cooldowns never grant
    // or deny service. Physical food, fixed sitting budgets and live permissions
    // remain owned by the ordinary one-way driver.
    public sealed class BodyRoleFollowerMeals : MapComponent
    {
        public const int ServiceCooldownTicks = OfficerServiceCadence.IntervalTicks;
        private Dictionary<int, int> nextOpportunity = new Dictionary<int, int>();
        private List<OfficerBedsideVisit> visits = new List<OfficerBedsideVisit>();
        private int cursor;
        [ThreadStatic] private static OfficerBedsideVisit selecting;
        public BodyRoleFollowerMeals(Map map) : base(map) { }
        public static JobDef MealJob => DefDatabase<JobDef>.GetNamed("RR_FollowerRoleMeal");
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref nextOpportunity, "rrOfficerBedsideOpportunityUntil", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref visits, "rrOfficerBedsideVisits", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                nextOpportunity = nextOpportunity ?? new Dictionary<int, int>();
                visits = visits ?? new List<OfficerBedsideVisit>();
            }
        }

        public static OfficerBedsideVisit RecipientVisit(Pawn pawn) => selecting?.Recipient == pawn ? selecting :
            pawn?.Map?.GetComponent<BodyRoleFollowerMeals>()?.visits.FirstOrDefault(v => v != null && v.Recipient == pawn && v.MatchesJob);
        public static OfficerBedsideVisit HelperVisit(Pawn pawn) => selecting?.Helper == pawn ? selecting :
            pawn?.Map?.GetComponent<BodyRoleFollowerMeals>()?.visits.FirstOrDefault(v => v != null && v.Helper == pawn && v.MatchesJob);
        public static float ServiceTarget(Pawn pawn)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body == null ? 0f : Mathf.Min(body.HardLimit, body.SoftLimit * 1.1f);
        }
        private static bool SettingsPermit => ModsConfig.IdeologyActive && FeedOtherMod.Settings.bedsideFeedingEnabled &&
            FeedOtherMod.Settings.autonomousOneWayFeedingEnabled && FeedOtherMod.Settings.autonomousFrequencyMultiplier > 0f;

        private static bool HasPlayerOrder(Pawn pawn)
        {
            if (pawn?.jobs == null || pawn.CurJob?.playerForced == true) return true;
            for (int i = 0; i < pawn.jobs.jobQueue.Count; i++)
                if (pawn.jobs.jobQueue[i].job?.playerForced == true) return true;
            return false;
        }

        private static bool PerformingCare(Pawn pawn)
        {
            Job job = pawn?.CurJob;
            if (job == null) return false;
            // Player-interruptible does not mean an optional visit may replace
            // surgery. Native medical bills are not suspendable; keep their
            // in-progress work and patient reservation intact.
            if (job.def == JobDefOf.DoBill &&
                (job.bill is Bill_Medical || job.targetA.Thing is Pawn)) return true;
            string work = job.workGiverDef?.workType?.defName;
            if (work == "Doctor" || work == "Childcare") return true;
            // Urgent native childcare can come from a think tree without a
            // work giver, and the helper's hands are empty before pickup.
            // Names keep these guards inert when Biotech is not installed.
            switch (job.def?.defName)
            {
                case "BottleFeedBaby":
                case "Breastfeed":
                case "BreastfeedCarryToMom":
                case "CarryToMomAfterBirth":
                case "BringBabyToSafety":
                case "BringBabyToSafetyUnforced":
                    return true;
                default:
                    return false;
            }
        }

        public static bool ServiceHelperReady(Pawn helper) => SettingsPermit && SocialFeedingPolicy.ControlledHelper(helper) &&
            AbundanceRoles.Has(helper, AbundanceRoles.Feeder) && helper.Awake() && !helper.Downed &&
            !helper.WorkTagIsDisabled(WorkTags.Caring) && !helper.health.HasHediffsNeedingTend() &&
            !HealthAIUtility.ShouldSeekMedicalRest(helper) && !HasPlayerOrder(helper) && !PerformingCare(helper) &&
            PawnHelperEligibility.NotHungry(helper) &&
            helper.health.capacities.CapableOf(PawnCapacityDefOf.Moving) &&
            helper.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
            SocialFeedingPolicy.ScheduleWeight(helper) > 0f;

        // Do not call EaterEligible here: it invokes this guard for active service.
        public static bool ServiceRecipientReady(OfficerBedsideVisit visit)
        {
            Pawn holder = visit?.Recipient;
            Building_Bed bed = visit?.Bed;
            if (!SettingsPermit || holder == null || !SocialFeedingPolicy.ControlledAdult(holder) ||
                holder.Map != visit.Helper?.Map || holder.Ideo != visit.Ideo || visit.Helper.Ideo != visit.Ideo ||
                visit.Role?.Active != true || !visit.Role.IsAssigned(holder) || !BodyRoleExpectations.IsRelevantRole(visit.Role) ||
                BodyRoleExpectations.Instance?.For(holder)?.role != visit.Role ||
                !ServiceWilling(holder) ||
                HasPlayerOrder(holder) || holder.health.HasHediffsNeedingTend() ||
                bed == null || bed.Map != holder.Map || bed.IsForbidden(visit.Helper) || holder.IsForbidden(visit.Helper) ||
                !bed.OwnersForReading.Contains(holder) ||
                !bed.OccupiedRect().Contains(holder.Position) || !holder.GetPosture().InBed() ||
                !BedsideRoleAbilities.BedReservationsPermitSession(bed) ||
                !holder.health.capacities.CapableOf(RimRound.Defs.PawnCapacityDefOf.Eating)) return false;
            bool nativeRest = holder.jobs.curDriver is JobDriver_LayDown || holder.CurJobDef == JobDefOf.Wait_Downed;
            bool linkedRest = holder.CurJobDef == FeedOtherDefOf.RR_BeFedOtherPartner &&
                holder.CurJob.targetB.Pawn == visit.Helper && holder.CurJob.count == JobDriver_BeFedOtherPartner.RemainInPlaceMode;
            return (nativeRest || linkedRest) && !JoyUtility.LordPreventsGettingJoy(holder);
        }

        private static bool ServiceWilling(Pawn holder)
        {
            var attitude = holder.TryGetComp<ThingComp_PawnAttitude>();
            var body = holder.TryGetComp<FullnessAndDietStats_ThingComp>();
            // The existing opt-in and personal target remain consent. The solo
            // meal cycle is not consent: its awake/.9-soft gates would prevent
            // the very bedside service this separate cadence supplies.
            return attitude?.FollowerMealsEnabled == true && attitude.HasPreference &&
                BodyModelEligibility.CanFollowBodyGoal(holder) && body?.UsesBodyGoals == true &&
                body.Goal == RimRound.Simulation.BodyGoal.Gain && !body.Disabled && body.HasGainAppetite &&
                attitude.TryGetPreferredRangeKg(out float lower, out float upper) &&
                body.ProjectedBodyMassKg < lower;
        }

        private OfficerServiceCadence Cadence => Verse.Current.Game?.GetComponent<OfficerServiceState>()?.Cadence;
        public bool ServiceOnCooldown(Pawn pawn, int now) => pawn == null || Cadence == null ||
            Cadence.IsBlocked(pawn.thingIDNumber, now);

        public bool TryCreateServiceJob(Pawn helper, Pawn holder, out Job result)
        {
            result = null;
            int now = Find.TickManager.TicksGame;
            if (!ServiceHelperReady(helper) || !SocialFeedingPolicy.Available(helper, true) ||
                ServiceOnCooldown(helper, now) || ServiceOnCooldown(holder, now) || helper == holder ||
                visits.Any(v => v != null && v.OwnsJob && (v.Helper == helper || v.Recipient == holder))) return false;
            var role = BodyRoleExpectations.Instance?.For(holder)?.role;
            var visit = new OfficerBedsideVisit { Helper = helper, Recipient = holder, Bed = holder?.CurrentBed(),
                Role = role, Ideo = holder?.Ideo };
            if (!ServiceRecipientReady(visit) || helper.HostileTo(holder) || holder.HostileTo(helper) ||
                !SocialProperness.IsSociallyProper(helper, holder) || !SocialProperness.IsSociallyProper(holder, helper) ||
                !FactionUtility.IsPoliticallyProper(helper, holder) || !FactionUtility.IsPoliticallyProper(holder, helper) ||
                !helper.CanReserveAndReach(holder, PathEndMode.Touch, helper.NormalMaxDanger())) return false;
            var body = holder.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null || body.OccupiedVolume >= body.SoftLimit * .9f - .0001f) return false;
            OfficerBedsideVisit previous = selecting;
            selecting = visit;
            try
            {
                if (!SocialFeedingPolicy.CanEat(holder) ||
                    !FeedOtherUtility.TryFindSocialMeal(helper, holder, false, true, out Thing food)) return false;
                result = JobMaker.MakeJob(FeedOtherDefOf.RR_FeedOtherOneWay, food, holder, food.Position);
                result.count = 1;
                return true;
            }
            finally { selecting = previous; }
        }

        public bool StartService(Pawn helper, Job job)
        {
            Pawn holder = job?.targetB.Pawn;
            // Rebuild and revalidate immediately before committing the duty.
            // Reserve cadence before StartJob, whose first toils can recurse.
            if (job?.def != FeedOtherDefOf.RR_FeedOtherOneWay || job.playerForced ||
                !TryCreateServiceJob(helper, holder, out Job ignored)) return false;
            int now = Find.TickManager.TicksGame;
            var visit = new OfficerBedsideVisit { Helper = helper, Recipient = holder, Bed = holder.CurrentBed(),
                Role = BodyRoleExpectations.Instance.For(holder).role, Ideo = holder.Ideo, JobId = job.GetUniqueLoadID() };
            if (Cadence?.TryClaim(helper.thingIDNumber, holder.thingIDNumber, now) != true) return false;
            visits.Add(visit);
            helper.jobs.StartJob(job, JobCondition.InterruptOptional, resumeCurJobAfterwards: true);
            return visit.OwnsJob;
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 != map.uniqueID % 250) return;
            visits.RemoveAll(v => v == null || !v.OwnsJob);
            if (!SettingsPermit) return;
            Pawn[] pawns = map.mapPawns.FreeColonistsSpawned.ToArray();
            if (pawns.Length == 0) return;
            for (int n = 0; n < Math.Min(4, pawns.Length); n++)
            {
                Pawn helper = pawns[cursor++ % pawns.Length];
                if (cursor > 1000000) cursor = 0;
                if (!AbundanceRoles.Has(helper, AbundanceRoles.Feeder) ||
                    (nextOpportunity.TryGetValue(helper.thingIDNumber, out int until) && now < until)) continue;
                nextOpportunity[helper.thingIDNumber] = now + SocialFeedingPolicy.OpportunityIntervalTicks;
                if (!ServiceHelperReady(helper) || ServiceOnCooldown(helper, now)) continue;
                float chance = Mathf.Clamp01(.15f * SocialFeedingPolicy.ScheduleWeight(helper) *
                    FeedOtherMod.Settings.autonomousFrequencyMultiplier);
                if (!Rand.Chance(chance)) continue;
                foreach (Pawn holder in SocialFeedingPolicy.OrderedCandidates(helper, pawns.Where(p => p != helper)))
                    if (TryCreateServiceJob(helper, holder, out Job job)) { StartService(helper, job); return; }
            }
            if (now % 2500 == map.uniqueID % 250)
            {
                var present = new HashSet<int>(pawns.Select(p => p.thingIDNumber));
                foreach (int id in nextOpportunity.Keys.Where(id => !present.Contains(id)).ToArray()) nextOpportunity.Remove(id);
            }
        }
        public static bool RecipientReady(Pawn holder) => holder?.CurrentBed() != null &&
            holder.TryGetComp<ThingComp_PawnAttitude>()?.WillAcceptFollowerMeal == true &&
            !holder.InMentalState && !holder.Drafted && !holder.IsBurning() &&
            holder.health.capacities.CapableOf(RimWorld.PawnCapacityDefOf.Consciousness) &&
            SocialFeedingPolicy.EaterEligible(holder, true);

        public static bool CanAssist(Pawn helper, Pawn holder) => helper != holder && RecipientReady(holder) &&
            PawnHelperEligibility.OrdinaryColonist(helper) && helper.Spawned && helper.Map == holder.Map &&
            helper.Ideo != null && helper.Ideo == holder.Ideo && !helper.Drafted && !helper.Downed &&
            !helper.InMentalState && helper.Awake() && !helper.IsBurning() &&
            !helper.WorkTagIsDisabled(WorkTags.Caring) && helper.health.HasHediffsNeedingTend() == false &&
            PawnHelperEligibility.NotHungry(helper) &&
            helper.carryTracker?.CarriedThing == null &&
            helper.health.capacities.CapableOf(RimWorld.PawnCapacityDefOf.Manipulation) &&
            helper.CanReserveAndReach(holder, PathEndMode.Touch, Danger.Some) &&
            BedsideRoleAbilities.BedReservationsPermitSession(holder.CurrentBed()) &&
            helper.Map.reservationManager.ReservationsReadOnly.All(r =>
                r.Target.Thing != holder || r.Claimant == helper);

        private static bool HasSurplusStock(Pawn helper, Pawn holder, Thing selected)
        {
            // Any hungry colonist takes priority over optional role meals. A
            // second reachable serving remains in stock for ordinary colony use.
            if (helper.Map.mapPawns.FreeColonistsSpawned.Any(p => p != holder &&
                p.needs.food != null && p.needs.food.CurLevelPercentage <= p.needs.food.PercentageThreshHungry)) return false;
            return helper.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree)
                .Any(t => t.Spawned && !t.IsForbidden(helper) && OrdinaryFoodIntake.IsOrdinaryFood(t) &&
                    (t != selected || t.stackCount > 1) && FoodUtility.WillEat(holder, t, helper, true, false) &&
                    helper.CanReserveAndReach(t, PathEndMode.ClosestTouch, Danger.Some));
        }
        public static bool LiveMealAllowed(Pawn helper, Pawn holder, Thing food) =>
            RecipientReady(holder) && helper?.Ideo == holder.Ideo &&
            BodyRoleExpectations.Instance?.For(holder) != null &&
            food != null && OrdinaryFoodIntake.IsOrdinaryFood(food) &&
            FeedOtherUtility.CanAutonomousSessionEat(holder, food);
    }

    public sealed class OfficerServiceState : GameComponent
    {
        public readonly OfficerServiceCadence Cadence = new OfficerServiceCadence();
        public OfficerServiceState(Game game) { }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref Cadence.Until, "rrOfficerBedsideServiceUntil", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Cadence.Until == null)
                Cadence.Until = new Dictionary<int, int>();
        }
        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 2500 == 0) Cadence.Prune(now);
        }
    }

    public sealed class OfficerBedsideVisit : IExposable
    {
        public Pawn Helper, Recipient;
        public Building_Bed Bed;
        public Precept_Role Role;
        public Ideo Ideo;
        public string JobId;
        private bool Matches(Job job) => job != null && job.def == FeedOtherDefOf.RR_FeedOtherOneWay &&
            !job.playerForced && job.targetB.Pawn == Recipient && job.GetUniqueLoadID() == JobId;
        public bool MatchesJob => Matches(Helper?.CurJob);
        // Native StartJob may queue this exact duty behind a hauling/finalizer
        // prefix. Keep its identity until it runs or is removed from the queue;
        // intake permission still requires the current-only MatchesJob above.
        public bool OwnsJob
        {
            get
            {
                if (MatchesJob) return true;
                JobQueue queue = Helper?.jobs?.jobQueue;
                if (queue == null) return false;
                for (int i = 0; i < queue.Count; i++)
                    if (Matches(queue[i]?.job)) return true;
                return false;
            }
        }
        public void ExposeData()
        {
            Scribe_References.Look(ref Helper, "helper");
            Scribe_References.Look(ref Recipient, "recipient");
            Scribe_References.Look(ref Bed, "bed");
            Scribe_References.Look(ref Role, "role");
            Scribe_References.Look(ref Ideo, "ideo");
            Scribe_Values.Look(ref JobId, "jobId");
        }
    }

    public sealed class JobDriver_FollowerRoleMeal : JobDriver_FoodFeedPatientEatingSpeed
    {
        private Building_Bed originalBed;
        private Precept_Role originalRole;
        private Ideo originalIdeo;
        private bool captured;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref originalBed, "rrFollowerOriginalBed");
            Scribe_References.Look(ref originalRole, "rrFollowerOriginalRole");
            Scribe_References.Look(ref originalIdeo, "rrFollowerOriginalIdeo");
            Scribe_Values.Look(ref captured, "rrFollowerCaptured", false);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (!captured)
            {
                originalBed = job.targetB.Pawn?.CurrentBed();
                originalRole = BodyRoleExpectations.Instance?.For(job.targetB.Pawn)?.role;
                originalIdeo = job.targetB.Pawn?.Ideo;
                captured = true;
            }
            this.FailOn(() => !BodyRoleFollowerMeals.RecipientReady(job.targetB.Pawn) ||
                pawn.Ideo != originalIdeo || job.targetB.Pawn.Ideo != originalIdeo ||
                job.targetB.Pawn.CurrentBed() != originalBed ||
                BodyRoleExpectations.Instance?.For(job.targetB.Pawn)?.role != originalRole);
            foreach (Toil toil in base.MakeNewToils()) yield return toil;
        }
    }
}
