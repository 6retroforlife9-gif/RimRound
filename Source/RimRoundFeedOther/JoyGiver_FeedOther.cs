using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public class JoyGiver_FeedOther : JoyGiver
    {
        public override float GetChance(Pawn pawn)
        {
            // GetChance performs the same practical partner/food validation as
            // TryGiveJob. This prevents a high recreation weight from winning
            // only to fail immediately because no valid session can be formed.
            if (FeedOtherUtility.ShouldRemainInPlaceForFeeding(pawn))
            {
                return 0f;
            }

            Pawn partner;
            Thing meal;

            if (FeedOtherUtility.IsNonEatingOneWayFeederEligible(pawn))
            {
                if (!FeedOtherMod.Settings.autonomousOneWayFeedingEnabled)
                {
                    return 0f;
                }

                if (!FeedOtherUtility.TryFindFeedeeAndMeal(pawn, out partner, out meal) &&
                    !FeedOtherUtility.TryFindSleepingOneWayFeedeeAndMeal(
                        pawn,
                        out partner,
                        out meal))
                {
                    return 0f;
                }

                bool bedside = FeedOtherUtility.ShouldRemainInPlaceForFeeding(partner);
                return CalculateChance(pawn, partner, meal, false, bedside, false);
            }

            if (!FeedOtherUtility.IsRimRoundEligible(pawn) ||
                FeedOtherUtility.IsAtOrAboveStartingFullnessCutoff(pawn) ||
                !FeedOtherMod.Settings.autonomousSharedMealsEnabled)
            {
                return 0f;
            }

            if (FeedOtherUtility.IsBedsideFeederEligible(pawn) &&
                FeedOtherUtility.TryFindImmobileFeedeeAndMeal(pawn, out partner, out meal))
            {
                return CalculateChance(pawn, partner, meal, true, true, true);
            }

            if (FeedOtherUtility.TryFindPartnerAndMeal(pawn, out partner, out meal))
            {
                return CalculateChance(pawn, partner, meal, true, false, true);
            }

            if (FeedOtherUtility.TryFindSleepingSharedPartnerAndMeal(
                pawn,
                out partner,
                out meal))
            {
                return CalculateChance(pawn, partner, meal, true, true, true);
            }

            return 0f;
        }

        public override Job TryGiveJob(Pawn pawn)
        {
            if (FeedOtherUtility.ShouldRemainInPlaceForFeeding(pawn))
            {
                return null;
            }

            Pawn partner;
            Thing meal;

            if (FeedOtherUtility.IsNonEatingOneWayFeederEligible(pawn))
            {
                if (!FeedOtherMod.Settings.autonomousOneWayFeedingEnabled)
                {
                    return null;
                }

                if (!FeedOtherUtility.TryFindFeedeeAndMeal(pawn, out partner, out meal) &&
                    !FeedOtherUtility.TryFindSleepingOneWayFeedeeAndMeal(
                        pawn,
                        out partner,
                        out meal))
                {
                    return null;
                }

                Job feedingJob = JobMaker.MakeJob(
                    FeedOtherDefOf.RR_FeedOtherOneWay,
                    meal,
                    partner,
                    meal.Position);
                feedingJob.count = 1;
                return feedingJob;
            }

            if (!FeedOtherMod.Settings.autonomousSharedMealsEnabled)
            {
                return null;
            }

            if (FeedOtherUtility.IsBedsideFeederEligible(pawn) &&
                !FeedOtherUtility.IsAtOrAboveStartingFullnessCutoff(pawn) &&
                FeedOtherUtility.TryFindImmobileFeedeeAndMeal(pawn, out partner, out meal))
            {
                Job bedsideJob = JobMaker.MakeJob(
                    FeedOtherDefOf.RR_FeedOtherBedside,
                    meal,
                    partner,
                    meal.Position);
                bedsideJob.count = 1;
                return bedsideJob;
            }

            if (FeedOtherUtility.TryFindPartnerAndMeal(pawn, out partner, out meal))
            {
                Job job = JobMaker.MakeJob(def.jobDef, meal, partner, meal.Position);
                job.count = 1;
                return job;
            }

            if (FeedOtherUtility.TryFindSleepingSharedPartnerAndMeal(
                pawn,
                out partner,
                out meal))
            {
                Job sleepingBedsideJob = JobMaker.MakeJob(
                    FeedOtherDefOf.RR_FeedOtherBedside,
                    meal,
                    partner,
                    meal.Position);
                sleepingBedsideJob.count = 1;
                return sleepingBedsideJob;
            }

            return null;
        }

        private float CalculateChance(
            Pawn initiator,
            Pawn partner,
            Thing meal,
            bool sharedMeal,
            bool bedside,
            bool applyOpinionFactor)
        {
            float chance = def.baseChance;
            if (applyOpinionFactor)
            {
                chance *= FeedOtherUtility.OpinionChanceFactor(initiator);
            }

            chance *= FeedOtherUtility.RelationshipChanceFactor(initiator, partner, bedside);
            chance += FeedOtherUtility.SituationalChanceAdjustment(
                initiator,
                partner,
                meal,
                sharedMeal,
                bedside);
            return FeedOtherUtility.ClampAutonomousRecreationChance(chance);
        }
    }
}
