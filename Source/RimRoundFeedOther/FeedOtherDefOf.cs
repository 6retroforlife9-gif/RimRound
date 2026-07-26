using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    [DefOf]
    public static class FeedOtherDefOf
    {
        static FeedOtherDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FeedOtherDefOf));
        }

        public static JobDef RR_FeedOther;
        public static JobDef RR_FeedOtherPartner;
        public static JobDef RR_FeedOtherOneWay;
        public static JobDef RR_FeedOtherBedside;
        public static JobDef RR_FattenPrisonerDirectFeed;
        public static JobDef RR_PrisonerMealDelivery;
        public static JobDef RR_FattenPrisonerBedLock;
        public static JobDef RR_BeFedOtherPartner;
        public static JobDef RR_IdleEatToFullness;
        public static JoyKindDef RR_FeedOtherJoy;
        public static ThoughtDef RR_SharedFeeding;
        public static ThoughtDef RR_FeedOtherVeryFullMood;
        public static ThoughtDef RR_FeedOtherVeryFullDiscomfort;
        public static ThoughtDef RR_AfterMealChatMood;
        public static AbilityDef RR_IncreaseWeightOpinion;
        public static AbilityDef RR_DecreaseWeightOpinion;
        public static InteractionDef RR_FeedOtherConversation;
        public static LogEntryDef RR_FeedOtherConversationLog;
    }
}
