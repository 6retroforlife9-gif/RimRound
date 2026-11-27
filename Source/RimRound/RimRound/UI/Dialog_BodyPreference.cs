using RimRound.AI;
using RimRound.Comps;
using UnityEngine;
using Verse;

namespace RimRound.UI
{
    public sealed class Dialog_BodyPreference : Window
    {
        private readonly Pawn pawn;
        private Vector2 scroll;
        private float contentHeight = 510f;
        public Dialog_BodyPreference(Pawn pawn)
        {
            this.pawn = pawn;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }
        public override Vector2 InitialSize => new Vector2(480f, 500f);
        public override void DoWindowContents(Rect rect)
        {
            var attitude = pawn?.TryGetComp<ThingComp_PawnAttitude>();
            if (attitude == null) { Close(); return; }
            Rect viewport = rect.ContractedBy(10f);
            Rect content = new Rect(0f, 0f, viewport.width - 18f, contentHeight);
            Widgets.BeginScrollView(viewport, ref scroll, content);
            var listing = new Listing_Standard { maxOneColumn = true };
            listing.Begin(content);
            Text.Font = GameFont.Medium;
            listing.Label("RR_PreferencePawnTitle".Translate(pawn.LabelShortCap));
            Text.Font = GameFont.Small;
            listing.GapLine();
            listing.Label("RR_PreferenceOwnSize".Translate());
            listing.Label(attitude.PreferenceDescription);
            listing.Gap();
            listing.Label("RR_PreferenceAssignment".Translate(
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>()?.BodyGoalLabel ?? "-"));
            string regulation = pawn.TryGetComp<FullnessAndDietStats_ThingComp>()?.GoalRegulationDescription;
            if (regulation != null) listing.Label(regulation);
            listing.Gap();
            listing.Label("RR_PreferenceExplanation".Translate());
            DrawSocialBodyTaste(listing, attitude);
            string roleDescription = RimRound.Ideology.BodyRoleExpectations.Description(pawn);
            if (roleDescription != null)
            {
                listing.Gap();
                listing.Label(roleDescription);
                bool enabled = attitude.FollowerMealsEnabled;
                listing.CheckboxLabeled("RR_FollowerMealsEnabled".Translate(), ref enabled,
                    "RR_FollowerMealsExplanation".Translate());
                if (enabled != attitude.FollowerMealsEnabled) attitude.SetFollowerMealsEnabled(enabled);
                if (enabled) listing.Label(attitude.FollowerMealStatus);
            }
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body != null)
            {
                string mealReason = RimRound.Utilities.GainMealSession.Reason(pawn);
                if (mealReason != null) listing.Label(mealReason);
                listing.GapLine();
                listing.Label("RR_HabitSummary".Translate(body.EatingConditioning.ToStringPercent(),
                    body.SizeConditioning.ToStringPercent(), (body.ActiveProgressionTicks / 60000d).ToString("0.0")));
                if (listing.ButtonText("RR_TreeOpen".Translate()))
                    Find.WindowStack.Add(new Dialog_BodyPerks(pawn));
            }
            contentHeight = listing.CurHeight + 10f;
            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawSocialBodyTaste(Listing_Standard listing, ThingComp_PawnAttitude attitude)
        {
            listing.GapLine();
            if (!attitude.HasSocialBodyTaste)
            {
                listing.Label("RR_SocialBodyTasteUnavailable".Translate());
                listing.Label("RR_SocialBodyTasteUnavailableExplanation".Translate());
                return;
            }

            listing.Label("RR_SocialBodyTasteSummary".Translate(
                ("RR_SocialBodyTasteLevel" + attitude.SocialBodyTasteLevel).Translate()));
            listing.Label("RR_SocialBodyTasteExplanation".Translate());
            if (!ThoughtWorker_SocialBodyOpinion.CanObserveBodyOpinion(pawn))
            {
                listing.Label("RR_SocialBodyTasteInactive".Translate());
                return;
            }
            if (!pawn.story.CaresAboutOthersAppearance)
                listing.Label("RR_SocialBodyTasteAppearanceSuppressed".Translate());
            if (ThoughtWorker_SocialBodyOpinion.HasEmbodiedAbundance(pawn))
                listing.Label("RR_SocialBodyTasteAbundance".Translate());
        }
    }
}
