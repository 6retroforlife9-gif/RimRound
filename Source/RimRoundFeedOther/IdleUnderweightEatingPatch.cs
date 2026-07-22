using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    /// <summary>
    /// A pawn who positively values weight but is currently unhappy about
    /// being too thin may occasionally choose a normal meal instead of an idle
    /// wander. Reusing RimWorld's food job giver preserves food policy, diet,
    /// reachability, reservation, forbidden-item, and ingestion checks.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Idle), "TryGiveJob")]
    public static class IdleUnderweightEatingPatch
    {
        private static readonly ExposedFoodJobGiver FoodJobGiver = new ExposedFoodJobGiver();
        private static readonly Dictionary<int, int> NextIdleMealAttemptTickByPawn =
            new Dictionary<int, int>();

        [HarmonyPrefix]
        public static bool Prefix(Pawn __0, ref Job __result)
        {
            Job eatingJob;
            if (!TryGetScheduledIdleFoodJob(__0, out eatingJob))
            {
                return true;
            }

            __result = eatingJob;
            return false;
        }

        public static bool TryGetScheduledIdleFoodJob(Pawn pawn, out Job eatingJob)
        {
            eatingJob = null;
            if (!ShouldConsiderIdleMeal(pawn))
            {
                if (pawn != null)
                {
                    NextIdleMealAttemptTickByPawn.Remove(pawn.thingIDNumber);
                }

                return false;
            }

            int currentTick = Find.TickManager.TicksGame;
            int nextAttemptTick;
            if (!NextIdleMealAttemptTickByPawn.TryGetValue(
                    pawn.thingIDNumber,
                    out nextAttemptTick))
            {
                ScheduleNextAttempt(pawn, currentTick);
                return false;
            }

            if (currentTick < nextAttemptTick)
            {
                return false;
            }

            // Schedule before asking for food so a temporarily unavailable meal
            // does not make the idle think tree scan every tick. Unlike the old
            // independent 20% roll, this randomized delay guarantees another
            // attempt after a short idle interval instead of allowing an
            // arbitrarily long streak of wander jobs.
            ScheduleNextAttempt(pawn, currentTick);

            eatingJob = FoodJobGiver.TryGiveIdleFoodJob(pawn);
            if (eatingJob == null)
            {
                return false;
            }

            return true;
        }

        private static void ScheduleNextAttempt(Pawn pawn, int currentTick)
        {
            NextIdleMealAttemptTickByPawn[pawn.thingIDNumber] = currentTick +
                Rand.RangeInclusive(
                    FeedOtherMod.Settings.IdleMinimumDelayTicks,
                    FeedOtherMod.Settings.IdleMaximumDelayTicks);
        }

        private static bool ShouldConsiderIdleMeal(Pawn pawn)
        {
            if (!FeedOtherMod.Settings.idleUnderweightEatingEnabled ||
                pawn == null || pawn.Dead || !pawn.Spawned || pawn.Downed || !pawn.Awake() ||
                pawn.Drafted || pawn.InMentalState || !pawn.RaceProps.Humanlike ||
                pawn.needs?.food == null || pawn.needs?.mood == null ||
                !GlobalSettings.moodletsForWeightOpinions)
            {
                return false;
            }

            FullnessAndDietStats_ThingComp fullnessComp =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            PawnBodyType_ThingComp bodyComp = pawn.TryGetComp<PawnBodyType_ThingComp>();
            ThingComp_PawnAttitude attitudeComp = pawn.TryGetComp<ThingComp_PawnAttitude>();
            if (fullnessComp == null || fullnessComp.Disabled ||
                fullnessComp.DietMode == DietMode.Disabled ||
                fullnessComp.FullnessGainedMultiplier <= 0.001f ||
                fullnessComp.CurrentFullness >=
                    fullnessComp.HardLimit * FeedOtherMod.Settings.IdleEatingTriggerFraction ||
                bodyComp == null || bodyComp.PersonallyExempt || bodyComp.CategoricallyExempt ||
                attitudeComp == null || attitudeComp.weightOpinion < WeightOpinion.NeutralPlus)
            {
                return false;
            }

            ThoughtDef weightThought;
            if (!WeightOpinionUtility.weightOpinionToThoughtDef.TryGetValue(
                    attitudeComp.weightOpinion,
                    out weightThought) ||
                weightThought?.stages == null ||
                ThoughtUtility.ThoughtNullified(pawn, weightThought))
            {
                return false;
            }

            int currentStage = WeightOpinionUtility.GetThoughtIndex(pawn);
            int firstNonNegativeStage =
                weightThought.stages.FindIndex(stage => stage.baseMoodEffect >= 0f);
            return firstNonNegativeStage > 0 && currentStage >= 0 &&
                currentStage < firstNonNegativeStage &&
                currentStage < weightThought.stages.Count &&
                weightThought.stages[currentStage].baseMoodEffect < 0f;
        }

        private sealed class ExposedFoodJobGiver : JobGiver_GetFood
        {
            public Job TryGiveIdleFoodJob(Pawn pawn)
            {
                return TryGiveJob(pawn);
            }
        }
    }

    /// <summary>
    /// Player colonists normally receive GotoWander or Wait_Wander from
    /// JobGiver_Wander before the generic JobGiver_Idle fallback is reached.
    /// Hook that actual idle-wander path as well, sharing the same per-pawn
    /// randomized schedule and food-selection logic.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Wander), "TryGiveJob")]
    public static class WanderUnderweightEatingPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn __0, ref Job __result)
        {
            return IdleUnderweightEatingPatch.Prefix(__0, ref __result);
        }
    }

    /// <summary>
    /// Fullness can cross below 25% while a pawn remains inside one continuing
    /// idle job. In that case no job giver is called again, so check the active
    /// idle job on its normal job-tracker tick and replace only that idle job
    /// when the randomized meal attempt becomes due.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), "JobTrackerTickInterval")]
    public static class ActiveIdleUnderweightEatingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_JobTracker __instance, Pawn ___pawn)
        {
            Pawn pawn = ___pawn;
            Job currentJob = pawn?.CurJob;
            if (pawn == null || currentJob == null || currentJob.playerForced ||
                pawn.jobs != __instance || !pawn.mindState.IsIdle ||
                !pawn.jobs.IsCurrentJobPlayerInterruptible())
            {
                return;
            }

            Job eatingJob;
            if (!IdleUnderweightEatingPatch.TryGetScheduledIdleFoodJob(
                    pawn,
                    out eatingJob))
            {
                return;
            }

            __instance.StartJob(
                eatingJob,
                JobCondition.InterruptForced,
                null,
                false,
                true,
                null,
                JobTag.SatisfyingNeeds,
                false,
                false);
        }
    }
}
