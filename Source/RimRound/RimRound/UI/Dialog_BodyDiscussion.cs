using System;
using System.Linq;
using RimRound.AI;
using RimRound.Comps;
using RimRound.Simulation;
using RimRound.Utilities;
using UnityEngine;
using Verse;

namespace RimRound.UI
{
    public sealed class Dialog_BodyDiscussion : Window
    {
        private readonly Pawn speaker;
        private Pawn recipient;
        private bool ongoing;
        private string message;
        public Dialog_BodyDiscussion(Pawn speaker)
        {
            this.speaker = speaker;
            recipient = speaker.TryGetComp<ThingComp_PawnAttitude>()?.DiscussionRecipient;
            doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = true;
        }
        public override Vector2 InitialSize => new Vector2(560f, 650f);
        public override void DoWindowContents(Rect rect)
        {
            var attitude = speaker?.TryGetComp<ThingComp_PawnAttitude>();
            if (attitude == null) { Close(); return; }
            var listing = new Listing_Standard();
            listing.Begin(rect.ContractedBy(12f));
            Text.Font = GameFont.Medium;
            listing.Label("RR_DiscussionPawnTitle".Translate(speaker.LabelShortCap));
            Text.Font = GameFont.Small;
            listing.GapLine();
            listing.Label("RR_DiscussionDescription".Translate());
            if (listing.ButtonText(recipient == null ? "RR_DiscussionChoose".Translate().ToString() : recipient.LabelShortCap.ToString()))
            {
                var options = speaker.Map?.mapPawns.FreeColonistsSpawned.Where(p => p != speaker && BodyPreferenceSocial.OrdinaryColonist(p) &&
                    p.TryGetComp<ThingComp_PawnAttitude>()?.HasPreference == true).OrderBy(p => p.LabelShort).Select(p =>
                        new FloatMenuOption(p.LabelShortCap, () => { recipient = p; message = null; })).ToList();
                if (options != null && options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
                else message = "RR_DiscussionNoRecipients".Translate();
            }
            var target = recipient?.TryGetComp<ThingComp_PawnAttitude>();
            if (target != null)
            {
                listing.Label(target.PreferenceDescription);
                int now = Find.TickManager.TicksGame;
                int next = Math.Max(Math.Max(attitude.DiscussionClock.NextSpeakerTick, attitude.DiscussionClock.NextRecipientTick),
                    Math.Max(target.DiscussionClock.NextSpeakerTick, target.DiscussionClock.NextRecipientTick));
                int outcomes = target.DiscussionClock.OutcomeDay == BodyPreferenceSocial.Day ? target.DiscussionClock.OutcomesToday : 0;
                listing.Label("RR_DiscussionLimits".Translate(Math.Max(0f, (next - now) / 2500f).ToString("0.0"), outcomes,
                    target.DiscussionClock.Irritation.ToStringPercent()));
                string reason;
                if (!BodyPreferenceSocial.Eligible(speaker, recipient, true, false, out reason)) listing.Label(reason);
                listing.CheckboxLabeled("RR_DiscussionOngoing".Translate(), ref ongoing);
                if (listing.ButtonText("RR_DiscussionLarger".Translate())) Order(attitude, true);
                if (listing.ButtonText("RR_DiscussionSmaller".Translate())) Order(attitude, false);
            }
            if (attitude.DiscussionRecipient != null)
            {
                listing.GapLine();
                var intended = attitude.DiscussionRecipient.TryGetComp<ThingComp_PawnAttitude>();
                float low, high;
                if (intended != null && intended.TryGetPreferredRangeKg(out low, out high) && intended.PreferredLowerRatio > 0f &&
                    BodyPreferenceState.ValidRange(attitude.DiscussionTargetLower, attitude.DiscussionTargetUpper))
                {
                    float scale = GlobalSettings.usePoundsWherePossible ? 2.20462f : 1f;
                    float baseline = low / intended.PreferredLowerRatio;
                    listing.Label("RR_DiscussionFixedTarget".Translate(attitude.DiscussionRecipient.LabelShortCap,
                        (attitude.DiscussionTargetLower * baseline * scale).ToString("F1"),
                        (attitude.DiscussionTargetUpper * baseline * scale).ToString("F1"), GlobalSettings.usePoundsWherePossible ? "lb" : "kg"));
                }
                listing.Label((attitude.DiscussionIntentEnabled ? "RR_DiscussionIntentOn" : "RR_DiscussionIntentOff").Translate());
                if (listing.ButtonText("RR_DiscussionStop".Translate())) { attitude.StopBodyDiscussionIntent(); message = "RR_DiscussionStopped".Translate(); }
            }
            if (attitude.DiscussionClock.LastOutcomeTick >= 0)
                listing.Label("RR_DiscussionLast".Translate(("RR_DiscussionShort_" + (BodyDiscussionOutcome)attitude.DiscussionClock.LastOutcome).Translate()));
            if (!message.NullOrEmpty()) { listing.Gap(); listing.Label(message); }
            listing.End();
        }
        private void Order(ThingComp_PawnAttitude attitude, bool larger)
        {
            string reason;
            message = attitude.TryOrderBodyDiscussion(recipient, larger, ongoing, out reason)
                ? (ongoing ? "RR_DiscussionScheduled" : "RR_DiscussionOrdered").Translate().ToString()
                : reason ?? "RR_DiscussionUnavailable".Translate().ToString();
        }
    }
}
