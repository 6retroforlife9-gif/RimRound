using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.AI;
using RimRound.Comps;
using RimRound.Ideology;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    /// <summary>Voluntary social food permission is independent of the solo hunger cycle.</summary>
    public static class SocialFeedingPolicy
    {
        public const int MinimumCooldownTicks = 4 * 2500;
        public const int OpportunityIntervalTicks = 2500;
        [ThreadStatic] private static int selectionDepth;
        public static bool IsAutomaticContext(Pawn pawn) => selectionDepth > 0 ||
            FeedOtherUtility.IsAutonomousFeedOtherEater(pawn);
        public static IDisposable SelectingMeal() { selectionDepth++; return new SelectionScope(); }
        private sealed class SelectionScope : IDisposable
        {
            private bool disposed;
            public void Dispose() { if (!disposed) { disposed = true; selectionDepth--; } }
        }
        private static readonly HashSet<string> EssentialJobs = new HashSet<string>
        { "TendPatient", "FeedPatient", "Rescue", "BeatFire", "ExtinguishSelf", "WardenDeliverFood", "RR_PrisonerMealDelivery" };

        public static bool ControlledAdult(Pawn pawn) => BodyPreferenceSocial.OrdinaryColonist(pawn) &&
            ReadyForActivity(pawn);

        public static bool ControlledHelper(Pawn pawn) => PawnHelperEligibility.OrdinaryColonist(pawn) &&
            ReadyForActivity(pawn);

        private static bool ReadyForActivity(Pawn pawn) =>
            pawn.Spawned && !pawn.Dead && !pawn.Drafted && !pawn.InMentalState &&
            !pawn.Suspended && !pawn.Deathresting && !pawn.IsBurning() &&
            pawn.health?.capacities != null &&
            pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness);

        public static bool LikesLargerBodies(Pawn pawn)
        {
            var taste = pawn?.TryGetComp<ThingComp_PawnAttitude>();
            return taste?.HasSocialBodyTaste == true && taste.SocialBodyTasteLevel >= 3 &&
                pawn.story?.CaresAboutOthersAppearance == true;
        }

        public static bool HasRoleMotivation(Pawn pawn) => pawn?.Ideo != null &&
            BodyRoleExpectations.IdealActive(BodyRoleExpectations.IdealFor(pawn.Ideo));

        public static bool HelperEligible(Pawn pawn)
        {
            if (BodyRoleFollowerMeals.HelperVisit(pawn) != null && !BodyRoleFollowerMeals.ServiceHelperReady(pawn)) return false;
            return ControlledHelper(pawn) && pawn.Awake() && CanWalkForFeeding(pawn) &&
                pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
                !pawn.WorkTagIsDisabled(WorkTags.Caring) &&
                (LikesLargerBodies(pawn) || HasRoleMotivation(pawn)) && ScheduleWeight(pawn) > 0f;
        }

        // A walking helper may cross a bed footprint on the way to its occupant.
        // Recipient bed-cell locks must not cancel that helper's active job.
        // Actual rest, incapacity and the existing quarter-Moving limit still
        // prevent a pawn from taking the mobile helper/initiator role.
        private static bool CanWalkForFeeding(Pawn pawn) => pawn != null && !pawn.Downed &&
            !pawn.GetPosture().Laying() && !(pawn.jobs?.curDriver is JobDriver_LayDown) &&
            pawn.health?.capacities != null && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving) &&
            pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving) > .25f;

        public static bool SharedInitiatorEligible(Pawn pawn) => EaterEligible(pawn) && CanWalkForFeeding(pawn) &&
            pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);

        public static bool EaterEligible(Pawn pawn, bool allowSleeping = false)
        {
            var visit = BodyRoleFollowerMeals.RecipientVisit(pawn);
            bool service = visit != null;
            if (service && (!BodyRoleFollowerMeals.ServiceRecipientReady(visit) ||
                !BodyRoleFollowerMeals.ServiceHelperReady(visit.Helper))) return false;
            if (!ControlledAdult(pawn) || (!allowSleeping && !service && !pawn.Awake()) ||
                pawn.needs?.food == null || pawn.needs.joy == null ||
                (!service && ScheduleWeight(pawn) <= 0f && !BedsideHungerOrActiveMeal(pawn)) ||
                !pawn.health.capacities.CapableOf(RimRound.Defs.PawnCapacityDefOf.Eating)) return false;
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body?.UsesBodyGoals == true && body.Goal == BodyGoal.Gain && !body.Disabled &&
                (body.ContinuousGain || body.AssignedGoal == BodyGoal.Auto) &&
                body.FullnessGainedMultiplier > .0001f;
        }

        // Hunger-triggered bedside care can wake a resting recipient at 25% of
        // the food bar. Once accepted, finish that fixed meal even after the
        // first serving raises hunger above the trigger; the linked driver
        // restores the original LayDown job when the visit ends.
        private static bool BedsideHungerOrActiveMeal(Pawn pawn)
        {
            if (!FeedOtherMod.Settings.bedsideFeedingEnabled || pawn?.needs?.food == null ||
                !pawn.InBed()) return false;
            if (pawn.needs.food.CurLevelPercentage <= .25f &&
                FeedOtherUtility.IsMobileBedLockCandidate(pawn)) return true;
            if (pawn.CurJobDef != FeedOtherDefOf.RR_BeFedOtherPartner) return false;
            Pawn feeder = pawn.CurJob.targetB.Pawn;
            return feeder?.CurJob?.playerForced == false && feeder.CurJob.targetB.Pawn == pawn &&
                (feeder.jobs.curDriver is JobDriver_FeedOtherBedside ||
                 feeder.jobs.curDriver is JobDriver_FeedOtherOneWay);
        }

        public static float ComfortableTarget(Pawn pawn)
            => BodyRoleFollowerMeals.RecipientVisit(pawn) != null ? BodyRoleFollowerMeals.ServiceTarget(pawn) :
                pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.SoftLimit * .9f ?? 0f;

        public static bool CanEat(Pawn pawn, Thing food = null, Thing reserveSource = null, bool checkReserve = true)
        {
            if (!EaterEligible(pawn, true)) return false;
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body.OccupiedVolume >= ComfortableTarget(pawn) - .0001f || !body.HasGainAppetite)
                return false;
            // Eligibility without a selected source is only an intent check.
            // Reuse the short-lived estimate instead of quoting an invented
            // debit on every partner/wait check. Real source previews and
            // first-serving/collection/intake commitments remain live below.
            if (food == null && reserveSource == null)
                return !checkReserve || ColonyFoodReserve.CanSpendOptional(pawn, 0d);
            if (food == null || FeedOtherUtility.IsFoodNetworkFaucet(food))
                return !checkReserve || ColonyFoodReserve.CanSpendOptional(pawn, .1d, reserveSource ?? food);
            if (!OrdinaryFoodIntake.IsOrdinaryFood(food)) return false;
            MealPortionPlan portion;
            var budget = Budget(pawn, food);
            float perUnit = FoodUtility.NutritionForEater(pawn, food);
            if (!budget.HasValue || perUnit <= .0001f) return false;
            int count = FeedOtherUtility.IsPreparedMeal(food) ? 1 : Math.Min(food.stackCount,
                Math.Max(1, Mathf.CeilToInt(budget.Value.Nutrition / perUnit)));
            return count > 0 && budget.HasValue &&
                OrdinaryFoodIntake.TryPreview(pawn, food, count, out portion, budget) &&
                (!checkReserve || ColonyFoodReserve.CanSpendOptional(pawn, ColonyFoodReserve.StockCost(food, count), reserveSource ?? food));
        }

        public static OrdinaryIntakeBudget? Budget(Pawn pawn, Thing food)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body?.UsesBodyModel != true || food == null) return null;
            float target = ComfortableTarget(pawn);
            float ratio = OrdinaryFoodIntake.Density(food) * body.FullnessGainedMultiplier;
            float requested = ratio > .0001f ? Mathf.Max(0f, target - body.OccupiedVolume) / ratio : 0f;
            requested = body.LimitGainNutrition(requested);
            if (!EaterEligible(pawn, true)) requested = 0f;
            return new OrdinaryIntakeBudget(requested, target, protectReserve: true, nutritionIsLimit: true);
        }

        public static bool PairHasFirstServings(Pawn first, Thing firstFood, Pawn second, Thing secondFood)
        {
            IDisposable firstLease = null, secondLease = null;
            try
            {
                return FirstServingCost(first, firstFood, out double firstCost) &&
                    FirstServingCost(second, secondFood, out double secondCost) &&
                    ColonyFoodReserve.TryReserveOptional(first, firstCost, firstFood, out firstLease) &&
                    ColonyFoodReserve.TryReserveOptional(second, secondCost, secondFood, out secondLease);
            }
            finally { secondLease?.Dispose(); firstLease?.Dispose(); }
        }

        private static bool FirstServingCost(Pawn eater, Thing food, out double cost)
        {
            cost = 0d;
            var faucet = food as RimRound.FeedingTube.Building_FoodFaucet;
            if (faucet != null)
            {
                FaucetMealAcquisition.Selection selection;
                if (!FaucetMealAcquisition.TrySelect(faucet, eater, eater, out selection)) return false;
                cost = selection.Batch.PreciseNutrition;
                return cost > 0d;
            }
            var budget = Budget(eater, food);
            float perItem = food == null ? 0f : FoodUtility.NutritionForEater(eater, food);
            if (!budget.HasValue || perItem <= .0001f) return false;
            int count = FeedOtherUtility.IsPreparedMeal(food) ? 1 : Math.Min(food.stackCount,
                Math.Max(1, Mathf.CeilToInt(budget.Value.Nutrition / perItem)));
            cost = ColonyFoodReserve.StockCost(food, count);
            return cost > 0d && !double.IsNaN(cost) && !double.IsInfinity(cost);
        }

        public static float ScheduleWeight(Pawn pawn)
        {
            var assignment = pawn?.timetable?.CurrentAssignment;
            if (assignment == TimeAssignmentDefOf.Sleep) return 0f;
            if (assignment == TimeAssignmentDefOf.Work)
                return pawn.TryGetComp<FullnessAndDietStats_ThingComp>()?.AssignedGoal == BodyGoal.ForceGain ? 1f : 0f;
            return assignment == TimeAssignmentDefOf.Joy ? 4f : 1f;
        }

        public static float PairScheduleWeight(Pawn first, Pawn second)
            => Mathf.Min(ScheduleWeight(first), BedsideHungerOrActiveMeal(second)
                ? Mathf.Max(1f, ScheduleWeight(second)) : ScheduleWeight(second));

        public static bool Available(Pawn pawn, bool helper) => Available(pawn, helper, false);

        public static bool Available(Pawn pawn, bool helper, bool shared)
        {
            // A player-started feast already has its own food, server and place.
            // An ordinary invitation can otherwise queue its duty and pull both
            // participants away from the laid table without leaving the Lord.
            if (AbundanceRituals.Session(pawn) != null) return false;
            if (helper ? (shared ? !SharedInitiatorEligible(pawn) : !HelperEligible(pawn)) : !EaterEligible(pawn, true)) return false;
            if (pawn.jobs == null || pawn.CurJob?.playerForced == true ||
                FeedOtherUtility.IsFeedOtherJob(pawn.CurJobDef) ||
                pawn.CurJobDef == JobDefOf.Ingest || GainMealSession.IsActive(pawn) ||
                JoyUtility.LordPreventsGettingJoy(pawn) || pawn.carryTracker?.CarriedThing != null ||
                (pawn.CurJobDef != null && EssentialJobs.Contains(pawn.CurJobDef.defName))) return false;
            if (helper && (BodyFeedingPolicy.IsUrgentlyHungry(pawn) || HealthAIUtility.ShouldSeekMedicalRest(pawn))) return false;
            if (!pawn.Awake() && (helper || (!BedsideHungerOrActiveMeal(pawn) &&
                BodyRoleFollowerMeals.RecipientVisit(pawn) == null))) return false;
            if (FeedOtherUtility.ShouldRemainInPlaceForFeeding(pawn)) return !helper;
            return pawn.jobs.IsCurrentJobPlayerInterruptible();
        }

        public static bool RoleHolderFor(Pawn observer, Pawn candidate) => ModsConfig.IdeologyActive &&
            observer?.Ideo != null && observer.Ideo == candidate?.Ideo && observer.Ideo.PreceptsListForReading
                .OfType<Precept_Role>().Any(r => BodyRoleExpectations.IsRelevantRole(r) && r.IsAssigned(candidate));

        public static int PartnerTier(Pawn helper, Pawn candidate) =>
            LovePartnerRelationUtility.LovePartnerRelationExists(helper, candidate) ? 0 : RoleHolderFor(helper, candidate) ? 1 : 2;

        public static IEnumerable<Pawn> OrderedCandidates(Pawn helper, IEnumerable<Pawn> candidates)
            => candidates.OrderBy(p => PartnerTier(helper, p)).ThenByDescending(p => helper.relations?.OpinionOf(p) ?? 0)
                .ThenBy(p => helper.Position.DistanceToSquared(p.Position)).ThenBy(p => p.thingIDNumber);

        public static bool PairAvailable(Pawn helper, Pawn recipient) => PairAvailable(helper, recipient, false);

        public static bool PairAvailable(Pawn helper, Pawn recipient, bool shared)
        {
            if (helper == recipient || helper?.Map == null || helper.Map != recipient?.Map ||
                !Available(helper, true, shared) || !Available(recipient, false) || !CanEat(recipient) ||
                FeedOtherUtility.AutonomousCooldownBlocksPair(helper, recipient) ||
                helper.HostileTo(recipient) || recipient.HostileTo(helper) ||
                (!shared && !LikesLargerBodies(helper) && !BodyRoleFollowerMeals.CanAssist(helper, recipient))) return false;
            if (recipient.CurJobDef == FeedOtherDefOf.RR_RequestGainMeal && recipient.CurJob.targetA.Pawn != helper) return false;
            if (FeedOtherUtility.ShouldRemainInPlaceForFeeding(recipient) && !FeedOtherMod.Settings.bedsideFeedingEnabled) return false;
            return SocialProperness.IsSociallyProper(helper, recipient) && SocialProperness.IsSociallyProper(recipient, helper) &&
                FactionUtility.IsPoliticallyProper(helper, recipient) && FactionUtility.IsPoliticallyProper(recipient, helper) &&
                helper.CanReserveAndReach(recipient, PathEndMode.Touch, helper.NormalMaxDanger(), 1, -1, null, false);
        }

        public static bool TryCreateJob(Pawn helper, out Job result)
        {
            result = null;
            if (helper?.Map == null || !AutomaticMealsEnabled || RetryPending(helper) ||
                FeedOtherCooldownComponent.IsParticipantOnCooldown(helper)) return false;
            var candidates = OrderedCandidates(helper, helper.Map.mapPawns.FreeColonistsSpawned.Where(p => p != helper)).ToArray();
            FeedOtherUtility.SocialMealSearch search = null;
            foreach (bool shared in new[] { true, false })
            foreach (Pawn recipient in candidates)
            {
                if (TryCreatePairJobWithSearch(helper, recipient, shared, out result, ref search)) return true;
            }
            return false;
        }

        private static bool AutomaticMealsEnabled => FeedOtherMod.Settings.autonomousSharedMealsEnabled ||
            FeedOtherMod.Settings.autonomousOneWayFeedingEnabled;

        public static bool TryCreatePairJob(Pawn helper, Pawn recipient, bool shared, out Job result)
        {
            FeedOtherUtility.SocialMealSearch search = null;
            return TryCreatePairJobWithSearch(helper, recipient, shared, out result, ref search);
        }

        private static bool TryCreatePairJobWithSearch(Pawn helper, Pawn recipient, bool shared, out Job result,
            ref FeedOtherUtility.SocialMealSearch search)
        {
            result = null;
            if (RetryPending(helper) || RetryPending(recipient)) return false;
            if (shared ? !FeedOtherMod.Settings.autonomousSharedMealsEnabled : !FeedOtherMod.Settings.autonomousOneWayFeedingEnabled) return false;
            if (!PairAvailable(helper, recipient, shared) || (shared && !CanEat(helper))) return false;
            bool bedside = FeedOtherUtility.ShouldRemainInPlaceForFeeding(recipient);
            Thing food;
            using (SelectingMeal())
            {
                if (search == null) search = new FeedOtherUtility.SocialMealSearch(helper.Map);
                if (!FeedOtherUtility.TryFindSocialMealWithSearch(helper, recipient, shared, bedside, out food, search)) return false;
            }
            result = JobMaker.MakeJob(shared ? (bedside ? FeedOtherDefOf.RR_FeedOtherBedside : FeedOtherDefOf.RR_FeedOther) :
                FeedOtherDefOf.RR_FeedOtherOneWay, food, recipient, food.Position);
            result.count = 1;
            return true;
        }

        private static bool RetryPending(Pawn pawn) => Find.TickManager.TicksGame <
            (pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.GainMealState.SocialRetryAfterTick ?? 0);

        // StartJob may fail inside the linked partner's first instant toil. Mark
        // the attempt before that call: waiting until the partner has started
        // permits GetFood to invite the same pair recursively in the same tick.
        public static void NotifyAttempt(Pawn initiator, Pawn recipient, Job job)
        {
            if (job?.playerForced != false) return;
            int retry = Find.TickManager.TicksGame + GainMealCycle.RetryDelayTicks;
            foreach (Pawn pawn in new[] { initiator, recipient })
            {
                var state = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.GainMealState;
                if (state != null) state.SocialRetryAfterTick = Math.Max(state.SocialRetryAfterTick, retry);
            }
        }

        // Called only while selecting a real food job. Previews do not start jobs.
        public static Job PreferSocialMeal(Pawn eater)
        {
            if (!AutomaticMealsEnabled) return null;
            var assignment = eater?.timetable?.CurrentAssignment;
            if (assignment != TimeAssignmentDefOf.Anything && assignment != TimeAssignmentDefOf.Joy) return null;
            var body = eater?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null || Find.TickManager.TicksGame < body.GainMealState.SocialRetryAfterTick ||
                !Available(eater, false) || !CanEat(eater)) return null;
            Pawn[] candidates = OrderedCandidates(eater, eater.Map.mapPawns.FreeColonistsSpawned.Where(p => p != eater)).ToArray();
            Job result;
            FeedOtherUtility.SocialMealSearch search = null;
            foreach (Pawn partner in candidates)
                if (TryCreatePairJobWithSearch(eater, partner, true, out result, ref search)) return result;
            foreach (bool shared in new[] { true, false })
            foreach (Pawn helper in candidates)
                if (TryCreatePairJobWithSearch(helper, eater, shared, out result, ref search))
                {
                    Job request = JobMaker.MakeJob(FeedOtherDefOf.RR_RequestGainMeal, helper);
                    request.count = shared ? 1 : 0;
                    return request;
                }
            return null;
        }
    }

    /// <summary>One bounded coordinator; recreation does not create a second invitation stream.</summary>
    public sealed class SocialFeedingScheduler : MapComponent
    {
        private int cursor;
        private Dictionary<int, int> nextAttempt = new Dictionary<int, int>();
        public SocialFeedingScheduler(Map map) : base(map) { }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref nextAttempt, "rrSocialMealAttempts", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && nextAttempt == null) nextAttempt = new Dictionary<int, int>();
        }
        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 != map.uniqueID % 250) return;
            Pawn[] pawns = map.mapPawns.FreeColonistsSpawned.ToArray();
            if (pawns.Length == 0) return;
            for (int n = 0; n < Math.Min(4, pawns.Length); n++)
            {
                Pawn helper = pawns[cursor++ % pawns.Length];
                if (cursor > 1000000) cursor = 0;
                int until;
                if (nextAttempt.TryGetValue(helper.thingIDNumber, out until) && now < until) continue;
                nextAttempt[helper.thingIDNumber] = now + SocialFeedingPolicy.OpportunityIntervalTicks;
                Job job;
                if (!SocialFeedingPolicy.TryCreateJob(helper, out job)) continue;
                float chance = Mathf.Clamp01(.15f * SocialFeedingPolicy.PairScheduleWeight(helper, job.targetB.Pawn) *
                    FeedOtherMod.Settings.autonomousFrequencyMultiplier);
                if (!Rand.Chance(chance)) return;
                helper.jobs.StartJob(job, JobCondition.InterruptOptional, resumeCurJobAfterwards: true);
                return;
            }
            if (nextAttempt.Count > pawns.Length * 2 + 16)
            {
                var current = new HashSet<int>(pawns.Select(p => p.thingIDNumber));
                foreach (int id in nextAttempt.Keys.Where(id => !current.Contains(id)).ToArray()) nextAttempt.Remove(id);
            }
        }
    }
}
