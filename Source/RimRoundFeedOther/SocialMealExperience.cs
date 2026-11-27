using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    public interface ISocialMealExperienceDriver
    {
        void ObserveSocialFood(Pawn eater, Thing food);
    }

    /// <summary>Saved once-per-session receipt, independent of the optional chat phase.</summary>
    public sealed class SocialMealExperience : IExposable
    {
        private bool captured, rewarded, oneWay, failed;
        private double firstIntake, secondIntake;
        private float firstPain, secondPain;
        private bool firstDisliked, secondDisliked;
        private bool firstHungry, secondHungry, eatingEnded;
        private int eatingStartedTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref captured, "captured", false);
            Scribe_Values.Look(ref rewarded, "rewarded", false);
            Scribe_Values.Look(ref failed, "failed", false);
            Scribe_Values.Look(ref oneWay, "oneWay", false);
            Scribe_Values.Look(ref firstIntake, "firstIntake", 0d);
            Scribe_Values.Look(ref secondIntake, "secondIntake", 0d);
            Scribe_Values.Look(ref firstPain, "firstPain", 0f);
            Scribe_Values.Look(ref secondPain, "secondPain", 0f);
            Scribe_Values.Look(ref firstDisliked, "firstDisliked", false);
            Scribe_Values.Look(ref secondDisliked, "secondDisliked", false);
            Scribe_Values.Look(ref firstHungry, "firstHungry", false);
            Scribe_Values.Look(ref secondHungry, "secondHungry", false);
            Scribe_Values.Look(ref eatingEnded, "eatingEnded", false);
            Scribe_Values.Look(ref eatingStartedTick, "eatingStartedTick", 0);
        }

        private static double Intake(Pawn pawn) =>
            pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.MealIntakeNutrition ?? 0d;
        private static float Pain(Pawn pawn) => pawn?.health?.hediffSet?.PainTotal ?? 1f;

        public void Begin(Pawn first, Pawn second, bool feedingOnly)
        {
            if (captured) return;
            captured = true;
            oneWay = feedingOnly;
            firstIntake = Intake(first); secondIntake = Intake(second);
            firstPain = Pain(first); secondPain = Pain(second);
            firstHungry = !feedingOnly && GainMealSession.Hungry(first);
            secondHungry = GainMealSession.Hungry(second);
            eatingStartedTick = Find.TickManager.TicksGame;
        }

        public void EndEating(Pawn first, Pawn second, bool manual)
        {
            if (!captured || eatingEnded || manual) return;
            eatingEnded = true;
            if (firstHungry) GainMealSession.ContinueHungryMealAfterSocial(first, eatingStartedTick);
            if (secondHungry) GainMealSession.ContinueHungryMealAfterSocial(second, eatingStartedTick);
        }

        public void Observe(Pawn first, Pawn second, Pawn eater, Thing food)
        {
            if (!captured || food == null || eater == null) return;
            bool disliked = FeedOtherUtility.WouldCauseDislikedFoodThought(eater, food);
            if (eater == first) firstDisliked |= disliked;
            if (eater == second) secondDisliked |= disliked;
        }

        public void MarkFailed() { failed = true; }

        public void Finish(Pawn first, Pawn second, bool successful)
        {
            if (!captured || rewarded || failed || !successful) return;
            rewarded = true;
            bool recipientAte = Intake(second) - secondIntake >= .05d;
            if (Enjoyed(second, first, recipientAte, secondDisliked, secondPain))
                TryGrant(second, oneWay ? 1 : 0);
            bool firstParticipated = oneWay ? recipientAte &&
                (SocialFeedingPolicy.LikesLargerBodies(first) || SocialFeedingPolicy.HasRoleMotivation(first)) :
                Intake(first) - firstIntake >= .05d;
            if (Enjoyed(first, second, firstParticipated, firstDisliked || (oneWay && secondDisliked), firstPain))
                TryGrant(first, oneWay ? 2 : 0);
        }

        private static bool Enjoyed(Pawn pawn, Pawn other, bool meaningful, bool disliked, float initialPain)
        {
            if (!meaningful || disliked || pawn == null || other == null || pawn.Dead || pawn.InMentalState ||
                pawn.Drafted || (pawn.relations?.OpinionOf(other) ?? -1) < 0 || Pain(pawn) > initialPain + .01f ||
                Pain(pawn) >= .35f || pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Consciousness) != true)
                return false;
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            return body != null && body.CurrentFullness <= body.SoftLimit + .0001f;
        }

        public static bool HasReward(Pawn pawn)
        {
            var memories = pawn?.needs?.mood?.thoughts?.memories;
            return memories != null && (memories.GetFirstMemoryOfDef(FeedOtherDefOf.RR_EnjoyedSocialMealMood) != null ||
                memories.GetFirstMemoryOfDef(FeedOtherDefOf.RR_AfterMealChatMood) != null);
        }

        private static void TryGrant(Pawn pawn, int stage)
        {
            if (!FeedOtherMod.Settings.afterMealMoodEnabled || HasReward(pawn)) return;
            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(
                ThoughtMaker.MakeThought(FeedOtherDefOf.RR_EnjoyedSocialMealMood, stage), null);
        }
    }
}
