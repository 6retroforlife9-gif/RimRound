using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Shared rules for the optional social recreation phase that follows a
    /// completed Share Meal or Feed Other session.
    /// </summary>
    public static class PostMealSocialUtility
    {
        public static float RecreationCompletionLevel =>
            FeedOtherMod.Settings.PostMealRecreationTargetFraction;
        public static int MaximumDurationTicks =>
            FeedOtherMod.Settings.MaximumPostMealChatTicks;
        public const int MinimumRewardDurationTicks = 600;
        public const float RecreationGainFactor = 0.75f;
        public const float MaximumConversationDistance = 6f;
        public const int RomanticHeartIntervalTicks = 300;

        public static bool CanStart(Pawn first, Pawn second, bool secondWasAsleepAtStart)
        {
            return FeedOtherMod.Settings.postMealSocialEnabled &&
                !secondWasAsleepAtStart &&
                ParticipantsCanSocialize(first, second) &&
                EitherStillBored(first, second);
        }

        public static bool ShouldEnd(
            Pawn first,
            Pawn second,
            int socialStartTick,
            bool secondWasAsleepAtStart)
        {
            if (!FeedOtherMod.Settings.postMealSocialEnabled ||
                secondWasAsleepAtStart || !ParticipantsCanSocialize(first, second))
            {
                return true;
            }

            if (Find.TickManager.TicksGame >= socialStartTick + MaximumDurationTicks)
            {
                return true;
            }

            return !EitherStillBored(first, second);
        }

        public static bool EitherStillBored(Pawn first, Pawn second)
        {
            return JoyBelowTarget(first) || JoyBelowTarget(second);
        }


        public static void TickRomanticHearts(Pawn first, Pawn second)
        {
            if (first == null || second == null || !first.Spawned || !second.Spawned ||
                first.Map == null || first.Map != second.Map ||
                FleckDefOf.Heart == null ||
                !LovePartnerRelationUtility.LovePartnerRelationExists(first, second) ||
                !first.IsHashIntervalTick(RomanticHeartIntervalTicks))
            {
                return;
            }

            int interval = Find.TickManager.TicksGame / RomanticHeartIntervalTicks;
            Pawn target = (interval & 1) == 0 ? first : second;
            if (target.Spawned && target.Map == first.Map)
            {
                FleckMaker.ThrowMetaIcon(
                    target.Position,
                    target.Map,
                    FleckDefOf.Heart,
                    0.42f);
            }
        }

        public static void GainRecreation(Pawn participant, int delta)
        {
            FeedOtherUtility.GainFeedOtherRecreation(
                participant,
                delta,
                RecreationGainFactor);
        }

        public static void TryGrantAfterMealChatMemory(
            Pawn first,
            Pawn second,
            int socialStartTick)
        {
            if (!FeedOtherMod.Settings.afterMealMoodEnabled ||
                first == null || second == null || socialStartTick <= 0 ||
                Find.TickManager.TicksGame - socialStartTick < MinimumRewardDurationTicks ||
                FeedOtherDefOf.RR_AfterMealChatMood == null)
            {
                return;
            }

            TryGrant(first);
            TryGrant(second);
        }

        private static void TryGrant(Pawn pawn)
        {
            MemoryThoughtHandler memories = pawn?.needs?.mood?.thoughts?.memories;
            if (memories != null &&
                memories.GetFirstMemoryOfDef(FeedOtherDefOf.RR_AfterMealChatMood) == null)
            {
                memories.TryGainMemory(FeedOtherDefOf.RR_AfterMealChatMood, null, null);
            }
        }

        private static bool JoyBelowTarget(Pawn pawn)
        {
            return pawn?.needs?.joy != null &&
                pawn.needs.joy.CurLevel < RecreationCompletionLevel;
        }

        private static bool ParticipantsCanSocialize(Pawn first, Pawn second)
        {
            if (first == null || second == null || !first.Spawned || !second.Spawned ||
                first.Map == null || first.Map != second.Map ||
                first.Drafted || second.Drafted ||
                first.InMentalState || second.InMentalState ||
                !first.Awake() || !second.Awake())
            {
                return false;
            }

            return first.Position.InHorDistOf(second.Position, MaximumConversationDistance);
        }
    }
}
