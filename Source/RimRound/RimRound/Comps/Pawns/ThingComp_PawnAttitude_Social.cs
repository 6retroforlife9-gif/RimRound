using System;
using System.Collections.Generic;
using RimRound.AI;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.Comps
{
    public partial class ThingComp_PawnAttitude
    {
        private readonly BodyDiscussionClock discussionClock = new BodyDiscussionClock();
        private readonly BodyPreferenceExperience preferenceExperience = new BodyPreferenceExperience();
        private Pawn discussionRecipient;
        private float discussionTargetLower, discussionTargetUpper;
        private bool discussionIntentEnabled;
        private int discussionOrderRevision;
        private int lastPreferenceExperienceTick = -1;
        public BodyDiscussionClock DiscussionClock => discussionClock;
        public Pawn DiscussionRecipient => discussionRecipient;
        public float DiscussionTargetLower => discussionTargetLower;
        public float DiscussionTargetUpper => discussionTargetUpper;
        public bool DiscussionIntentEnabled => discussionIntentEnabled;
        public int DiscussionOrderRevision => discussionOrderRevision;

        public void ExposeSocialState()
        {
            Scribe_References.Look(ref discussionRecipient, "rrBodyDiscussionRecipient");
            Scribe_Values.Look(ref discussionTargetLower, "rrBodyDiscussionTargetLower", 0f);
            Scribe_Values.Look(ref discussionTargetUpper, "rrBodyDiscussionTargetUpper", 0f);
            Scribe_Values.Look(ref discussionIntentEnabled, "rrBodyDiscussionEnabled", false);
            Scribe_Values.Look(ref discussionOrderRevision, "rrBodyDiscussionRevision", 0);
            Scribe_Values.Look(ref discussionClock.Schema, "rrBodySocialSchema", 1);
            Scribe_Values.Look(ref discussionClock.NextSpeakerTick, "rrBodyNextSpeakerTick", 0);
            Scribe_Values.Look(ref discussionClock.NextRecipientTick, "rrBodyNextRecipientTick", 0);
            Scribe_Values.Look(ref discussionClock.OutcomeDay, "rrBodyOutcomeDay", -1);
            Scribe_Values.Look(ref discussionClock.OutcomesToday, "rrBodyOutcomesToday", 0);
            Scribe_Values.Look(ref discussionClock.LastEventId, "rrBodyLastEventId");
            Scribe_Values.Look(ref discussionClock.LastOutcomeTick, "rrBodyLastOutcomeTick", -1);
            Scribe_Values.Look(ref discussionClock.LastOutcome, "rrBodyLastOutcome", 0);
            Scribe_Values.Look(ref discussionClock.Irritation, "rrBodyIrritation", 0f);
            Scribe_Values.Look(ref preferenceExperience.Schema, "rrPreferenceExperienceSchema", 1);
            Scribe_Values.Look(ref preferenceExperience.WindowTicks, "rrPreferenceExperienceTicks", 0);
            Scribe_Values.Look(ref preferenceExperience.PleasantTicks, "rrPreferencePleasantTicks", 0);
            Scribe_Values.Look(ref preferenceExperience.AdverseTicks, "rrPreferenceAdverseTicks", 0);
            Scribe_Values.Look(ref preferenceExperience.UsefulNutrition, "rrPreferenceUsefulNutrition", 0d);
            Scribe_Values.Look(ref preferenceExperience.ExpenditureNutrition, "rrPreferenceExpenditureNutrition", 0d);
            Scribe_Values.Look(ref lastPreferenceExperienceTick, "rrPreferenceLastExperienceTick", -1);
            // Unknown/malformed state is retained, but cannot run or gain progress.
        }

        public bool TryOrderBodyDiscussion(Pawn recipient, bool larger, bool ongoing, out string reason)
        {
            Pawn speaker = parent as Pawn;
            if (!BodyPreferenceSocial.Eligible(speaker, recipient, !ongoing, false, out reason) || discussionOrderRevision < 0 || discussionOrderRevision == int.MaxValue)
                return false;
            var target = recipient.TryGetComp<ThingComp_PawnAttitude>();
            float lower, upper;
            if (!BodySocialProgression.CaptureTarget(target.PreferredLowerRatio, target.PreferredUpperRatio, larger, out lower, out upper))
            { reason = "RR_DiscussionUnavailable".Translate(); return false; }
            Pawn previousRecipient = discussionRecipient;
            float previousLower = discussionTargetLower, previousUpper = discussionTargetUpper;
            bool previousEnabled = discussionIntentEnabled;
            int previousRevision = discussionOrderRevision;
            discussionRecipient = recipient;
            discussionTargetLower = lower; discussionTargetUpper = upper;
            discussionIntentEnabled = ongoing;
            discussionOrderRevision++;
            if (ongoing) return true;
            if (BodyPreferenceSocial.TryStartIntent(speaker, true, out reason)) return true;
            discussionRecipient = previousRecipient;
            discussionTargetLower = previousLower; discussionTargetUpper = previousUpper;
            discussionIntentEnabled = previousEnabled; discussionOrderRevision = previousRevision;
            return false;
        }

        public void StopBodyDiscussionIntent()
        {
            ClearBodyDiscussionIntent();
            if (discussionOrderRevision < int.MaxValue) discussionOrderRevision++;
            Pawn pawn = parent as Pawn;
            BedsideBodyDiscussion.Cancel(pawn);
            if (pawn?.jobs?.curDriver is JobDriver_BodyPreferenceDiscussion)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        internal bool MatchesDiscussionOrder(Pawn recipient, int revision) => discussionRecipient == recipient &&
            discussionOrderRevision >= 0 && discussionOrderRevision == revision && BodyPreferenceState.ValidRange(discussionTargetLower, discussionTargetUpper);
        internal void NotifyDiscussionTargetReached()
        {
            var target = discussionRecipient?.TryGetComp<ThingComp_PawnAttitude>();
            if (!discussionIntentEnabled || (target?.HasPreference == true &&
                BodySocialProgression.AtTarget(target.PreferredLowerRatio, target.PreferredUpperRatio, discussionTargetLower, discussionTargetUpper)))
                ClearBodyDiscussionIntent();
        }
        internal void FinishBodyDiscussionOrder(Pawn recipient, int revision)
        {
            if (!discussionIntentEnabled && discussionRecipient == recipient && discussionOrderRevision == revision)
                ClearBodyDiscussionIntent();
        }
        private void ClearBodyDiscussionIntent()
        {
            discussionIntentEnabled = false;
            discussionRecipient = null;
            discussionTargetLower = discussionTargetUpper = 0f;
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn pawn = parent as Pawn;
            if (!parent.IsHashIntervalTick(250) || Scribe.mode != LoadSaveMode.Inactive ||
                Current.ProgramState != ProgramState.Playing || !discussionClock.IsUsable) return;
            if (discussionRecipient != null && (discussionRecipient.Destroyed || discussionRecipient.Dead ||
                discussionRecipient.Faction != Faction.OfPlayer || !discussionRecipient.IsColonist ||
                discussionRecipient.IsPrisoner || discussionRecipient.IsSlave || discussionRecipient.IsQuestLodger()))
                StopBodyDiscussionIntent();
            if (!discussionIntentEnabled && discussionRecipient != null &&
                !(pawn.jobs?.curDriver is JobDriver_BodyPreferenceDiscussion) && !BedsideBodyDiscussion.HasSession(pawn)) ClearBodyDiscussionIntent();
            if (!discussionIntentEnabled || !BodyPreferenceSocial.OrdinarySpeaker(pawn) ||
                !BodyPreferenceSocial.IdleForOptionalDiscussion(pawn)) return;
            NotifyDiscussionTargetReached();
            if (!discussionIntentEnabled) return;
            string reason;
            BodyPreferenceSocial.TryStartIntent(pawn, false, out reason);
        }

        // Manager calls these beside the existing useful-intake and active-body
        // hooks. No preference read, goal assignment or UI path calls them.
        public void RecordPreferenceIntake(float usefulNutrition)
        {
            Pawn pawn = parent as Pawn;
            if (HasPreference && BodyPreferenceSocial.OrdinaryColonist(pawn) && pawn.Spawned)
                preferenceExperience.RecordIntake(usefulNutrition);
        }
        public void RecordPreferenceResourceExpenditure(float nutrition)
        {
            if (HasPreference && BodyPreferenceSocial.OrdinaryColonist(parent as Pawn) && parent.Spawned)
                preferenceExperience.RecordResourceExpenditure(nutrition);
        }
        public void ObservePreferenceExperience(int ticks, float expenditure)
        {
            Pawn pawn = parent as Pawn;
            int now = Find.TickManager.TicksGame;
            if (!HasPreference || !BodyPreferenceSocial.OrdinaryColonist(pawn) || !pawn.Spawned ||
                now <= lastPreferenceExperienceTick || ticks <= 0 || ticks > BodySocialProgression.MaximumInterval ||
                !BodySocialProgression.Finite(expenditure) || expenditure < 0f) return;
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null || body.BaselineBodyMassKg <= 0f) return;
            if (lastPreferenceExperienceTick >= 0 && ticks > now - lastPreferenceExperienceTick)
            {
                int observed = now - lastPreferenceExperienceTick;
                expenditure *= (float)observed / ticks;
                ticks = observed;
            }
            lastPreferenceExperienceTick = now;
            if (discussionClock.IsUsable)
                discussionClock.Irritation = BodySocialProgression.DecayIrritation(discussionClock.Irritation, ticks,
                    body.ActiveBodyPerkEffects.IrritationResistanceBonus);
            var fullness = pawn.health.hediffSet.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Fullness);
            bool fullnessPain = fullness != null && fullness.PainOffset > .001f;
            // Fullness pain includes its pain-shock downing. Mere coexistence
            // of a full stomach and combat downing is not causal evidence.
            bool adverse = fullnessPain;
            bool pleasant = !adverse && !pawn.Downed && !pawn.InMentalState && pawn.Awake() &&
                pawn.health.hediffSet.PainTotal < .1f && (pawn.needs.mood?.CurLevel ?? 0f) >= .6f;
            float lower, upper;
            if (preferenceExperience.Observe(ticks, expenditure, pawn.Weight() / body.BaselineBodyMassKg,
                pleasant, adverse, PreferredLowerRatio, PreferredUpperRatio, out lower, out upper))
                TrySetPreference(lower, upper, PreferenceConviction);
        }

        public IEnumerable<Gizmo> GetBodyDiscussionGizmos()
        {
            Pawn pawn = parent as Pawn;
            if (!BodyPreferenceSocial.OrdinarySpeaker(pawn) || !discussionClock.IsUsable) yield break;
            yield return new Command_Action {
                defaultLabel = "RR_DiscussionTitle".Translate(), defaultDesc = "RR_DiscussionDescription".Translate(),
                icon = Resources.INCREASE_WEIGHT_OPINION_ICON,
                action = () => Find.WindowStack.Add(new UI.Dialog_BodyDiscussion(pawn))
            };
        }
    }
}
