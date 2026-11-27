using System;
using RimRound.Comps;
using RimRound.Simulation;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.Utilities
{
    // A real autonomous ingest job or a normal connected Gain pulse owns this
    // permission. Social/manual intake supplies its own budget and cannot start it.
    public static class GainMealSession
    {
        [ThreadStatic] private static Pawn selecting;
        internal static Pawn SetSelection(Pawn pawn) { Pawn previous = selecting; selecting = pawn; return previous; }
        private static int Now => Find.TickManager?.TicksGame ?? 0;

        private static bool Eligible(Pawn pawn, bool connected)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body?.UsesBodyModel == true && !body.Disabled && BodyModelEligibility.CanFollowBodyGoal(pawn) &&
                pawn.needs?.food != null && (connected || (pawn.Awake() && !pawn.Drafted && !pawn.InMentalState &&
                    BodyFeedingPolicy.UsesComfortableGainTarget(pawn)));
        }

        private static bool HasRoom(Pawn pawn, bool connected)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body != null && body.OccupiedVolume < body.SoftLimit * .9f - .0001f &&
                ((connected && body.Goal != BodyGoal.Gain) || body.HasGainAppetite);
        }

        public static bool Hungry(Pawn pawn) => pawn?.needs?.food != null &&
            (pawn.needs.food.CurLevelPercentage <= Math.Max(.25f, pawn.RaceProps.FoodLevelPercentageWantEat) ||
             BodyFeedingPolicy.IsUrgentlyHungry(pawn));

        public static bool AllowsExtra(Pawn pawn) => pawn?.Awake() == true &&
            ScheduledGoal(pawn) && pawn.CurJob?.playerForced != true &&
            !EssentialJob(pawn.CurJob?.def?.defName) &&
            (pawn.timetable?.CurrentAssignment != TimeAssignmentDefOf.Work ||
             pawn.TryGetComp<FullnessAndDietStats_ThingComp>()?.AssignedGoal == BodyGoal.ForceGain);

        private static bool EssentialJob(string name) => name == "TendPatient" || name == "FeedPatient" ||
            name == "Rescue" || name == "BeatFire" || name == "ExtinguishSelf" ||
            name == "WardenDeliverFood" || name == "RR_PrisonerMealDelivery";

        private static bool ScheduledGoal(Pawn pawn)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body != null && (body.ContinuousGain || body.AssignedGoal == BodyGoal.Auto);
        }

        public static bool IsActive(Pawn pawn)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null) return false;
            var state = body.GainMealState;
            bool connected = state.Owner == GainMealOwner.ConnectedFeeder;
            bool permitted = connected || !ScheduledGoal(pawn) || state.HungerStarted || Hungry(pawn) || AllowsExtra(pawn);
            state.Observe(Now, pawn.needs?.food?.CurLevelPercentage ?? float.NaN,
                Eligible(pawn, connected) && permitted, HasRoom(pawn, connected));
            if (state.Active && !connected && body.GainMealJob != null && pawn.CurJob != body.GainMealJob &&
                Now - state.ActivityTick > GainMealCycle.FollowupWindowTicks)
                End(pawn);
            if (state.Active && state.AwaitingFollowup && !state.SocialFollowup && pawn.CurJob != null &&
                pawn.CurJob.def != JobDefOf.Ingest &&
                BodyFeedingPolicy.SocialMealActive?.Invoke(pawn) != true &&
                !(pawn.CurJob.def == JobDefOf.Wait_MaintainPosture &&
                  pawn.CurJob.expiryInterval == 1 && !pawn.CurJob.playerForced))
                End(pawn);
            if (!state.Active) body.GainMealJob = null;
            return state.Active;
        }

        public static bool CanStartOrContinue(Pawn pawn, bool connected = false)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null) return false;
            if (IsActive(pawn))
                return body.GainMealState.Owner == (connected ? GainMealOwner.ConnectedFeeder : GainMealOwner.OrdinaryJob);
            float food = pawn.needs?.food?.CurLevelPercentage ?? float.NaN;
            return connected || !ScheduledGoal(pawn) ? body.GainMealState.CanStart(Now, food, Eligible(pawn, connected), HasRoom(pawn, connected)) :
                body.GainMealState.CanStartPermitted(Now, food, Eligible(pawn, false), HasRoom(pawn, false),
                    Hungry(pawn) || AllowsExtra(pawn));
        }

        public static bool BeginConnected(Pawn pawn)
        {
            if (!CanStartOrContinue(pawn, true) || !ColonyFoodReserve.CanSpendOptional(pawn, 0d)) return false;
            var state = pawn.TryGetComp<FullnessAndDietStats_ThingComp>().GainMealState;
            return state.Active || state.Begin(Now, pawn.needs.food.CurLevelPercentage,
                Eligible(pawn, true), HasRoom(pawn, true), GainMealOwner.ConnectedFeeder);
        }

        internal static bool HasOrdinaryBudget(Pawn pawn)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body != null && CanStartOrContinue(pawn) &&
                (body.GainMealState.Active || selecting == pawn) &&
                ColonyFoodReserve.CanSpendOptional(pawn, 0d);
        }

        internal static void BindJob(Pawn pawn, Job job)
        {
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (!CanStartOrContinue(pawn)) return;
            var state = body.GainMealState;
            if (!state.Active)
            {
                bool began = ScheduledGoal(pawn) ? state.BeginPermitted(Now, pawn.needs.food.CurLevelPercentage,
                    Eligible(pawn, false), HasRoom(pawn, false), Hungry(pawn) || AllowsExtra(pawn), Hungry(pawn)) :
                    state.Begin(Now, pawn.needs.food.CurLevelPercentage, Eligible(pawn, false), HasRoom(pawn, false), GainMealOwner.OrdinaryJob);
                if (!began) return;
            }
            state.AwaitingFollowup = false;
            state.SocialFollowup = false;
            state.ActivityTick = Now;
            body.GainMealJob = job;
            body.GainMealJobProgress = state.ProgressVersion;
        }

        internal static void JobEnded(Pawn pawn, Job job, JobCondition condition)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null || body.GainMealJob != job) return;
            if (condition == JobCondition.Succeeded && IsActive(pawn) &&
                body.GainMealState.ProgressVersion > body.GainMealJobProgress)
            {
                body.GainMealState.WaitForFollowup(Now);
                body.GainMealJob = null;
            }
            else if (body.GainMealState.Active) End(pawn);
        }

        public static void RecordProgress(Pawn pawn)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null || !body.GainMealState.Active) return;
            // Ordinary session progress must come from the bound job. Explicit
            // care/social/atomic transactions cannot extend that job's lifetime.
            if (body.GainMealState.Owner == GainMealOwner.OrdinaryJob &&
                (pawn.CurJob != body.GainMealJob ||
                (!OrdinaryFoodIntake.IsOrdinaryFood(pawn.CurJob?.targetA.Thing) &&
                !(pawn.CurJob?.targetA.Thing is RimRound.FeedingTube.Building_FoodFaucet &&
                  OrdinaryFoodIntake.IsOrdinaryFood(pawn.CurJob?.targetB.Thing))))) return;
            body.GainMealState.Progress(Now);
            IsActive(pawn);
        }

        public static void End(Pawn pawn, bool completed = false)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null) return;
            if (body.GainMealState.Active) body.GainMealState.End(Now, completed);
            body.GainMealJob = null;
        }

        // A social meal stops recruiting/feeding at Work. Its hungry eater may
        // finish alone, within the original meal deadline and idle timeout. Native
        // food packing/resumed work can precede the next food-priority evaluation.
        public static void ContinueHungryMealAfterSocial(Pawn pawn, int startedTick)
        {
            if (Now - startedTick >= GainMealCycle.MaximumSessionTicks ||
                pawn?.timetable?.CurrentAssignment != TimeAssignmentDefOf.Work ||
                !Eligible(pawn, false) || !HasRoom(pawn, false)) return;
            var state = pawn.TryGetComp<FullnessAndDietStats_ThingComp>().GainMealState;
            if (!state.BeginPermitted(Now, pawn.needs.food.CurLevelPercentage,
                true, true, true, true)) return;
            state.StartedTick = startedTick;
            state.SocialFollowup = true;
            state.WaitForFollowup(Now);
        }

        internal static void NoFood(Pawn pawn)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null) return;
            body.GainMealState.End(Now, false);
            body.GainMealJob = null;
        }

        // A successful delivery changes the failed-search condition. Let a
        // hungry recipient collect the delivered bundle on the next native
        // food decision instead of falling back to one meal during backoff.
        // This grants no job, reserve exemption or new stomach capacity.
        public static void NotifyMealDelivered(Pawn pawn)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null || body.GainMealState.Active || !Hungry(pawn) ||
                !Eligible(pawn, false) || !HasRoom(pawn, false)) return;
            body.GainMealState.RetryAfterTick = Math.Min(body.GainMealState.RetryAfterTick, Now);
        }

        public static string Reason(Pawn pawn)
        {
            if (BodyFeedingPolicy.SocialMealActive?.Invoke(pawn) == true) return "Taking part in a social meal";
            bool active = IsActive(pawn);
            if (!active && !BodyFeedingPolicy.UsesComfortableGainTarget(pawn))
                return pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.AssignedGoal == BodyGoal.Auto
                    ? "Auto: optional gaining is not needed for the current happy weight range" : null;
            if (!ColonyFoodReserve.CanSpendOptional(pawn, 0d)) return "Gain paused: keeping three days of food";
            if (active) return "Eating to comfortable fullness";
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (!pawn.Awake()) return "Gain waiting: asleep";
            if (!HasRoom(pawn, false)) return "Gain meal complete: comfortable fullness reached";
            if (!Hungry(pawn) && !AllowsExtra(pawn)) return "Gain waiting: Work schedule; eat when hungry";
            if (Now < body.GainMealState.RetryAfterTick) return "Gain meal waiting after interruption or no suitable food";
            return "Gain ready: eat to 90% comfortable fullness; solo if no social partner is available";
        }
    }
}
