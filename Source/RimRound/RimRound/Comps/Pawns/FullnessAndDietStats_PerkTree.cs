using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Comps
{
    public partial class FullnessAndDietStats_ThingComp
    {
        private readonly BodyPerkTreeState bodyPerks = new BodyPerkTreeState();
        private int completedBodyDiscussions;
        public BodyPerkTreeState PerkTree => bodyPerks;
        public int CompletedBodyDiscussions => completedBodyDiscussions;
        public bool PerkTreeActive => ColonyProgression.BodyPerksActive &&
            BodyModelEligibility.IsEligible(parent as Pawn) && bodyPerks.IsUsable;
        public BodyPerkEffects ActiveBodyPerkEffects => bodyPerks.GetEffects(PerkTreeActive, ColonyProgression.FantasyActive);
        public string PerkTreeInactiveReason => !bodyPerks.IsUsable ? "RR_TreeUnknownState".Translate().ToString() :
            !BodyModelEligibility.IsEligible(parent as Pawn) ? "RR_TreeIneligible".Translate().ToString() :
            ColonyProgression.LegacyPerksActive ? "RR_TreeLegacy".Translate().ToString() :
            !ColonyProgression.BodyPerksActive ? "RR_TreeDisabled".Translate().ToString() : null;

        private void ExposeBodyPerks()
        {
            Scribe_Values.Look(ref bodyPerks.SchemaVersion, "rrTreeSchema", 0);
            Scribe_Values.Look(ref bodyPerks.EarnedPoints, "rrTreeEarned", 0d);
            Scribe_Collections.Look(ref bodyPerks.PurchasedNodeIds, "rrTreeNodes", LookMode.Value);
            Scribe_Collections.Look(ref bodyPerks.PurchasedRawCosts, "rrTreeCosts", LookMode.Value);
            Scribe_Values.Look(ref bodyPerks.WindowActiveTicks, "rrTreeWindowTicks", 0);
            Scribe_Values.Look(ref bodyPerks.LifetimeActiveTicks, "rrTreeLifetimeTicks", 0L);
            Scribe_Values.Look(ref bodyPerks.WindowUsefulNutrition, "rrTreeUseful", 0d);
            Scribe_Values.Look(ref bodyPerks.WindowExpenditureNutrition, "rrTreeExpenditure", 0d);
            Scribe_Values.Look(ref bodyPerks.WindowCompletedDiscussions, "rrTreeWindowDiscussions", 0);
            Scribe_Values.Look(ref bodyPerks.LastSocialEventTick, "rrTreeLastDiscussion", -1);
            Scribe_Values.Look(ref bodyPerks.LastRespecTick, "rrTreeLastRespec", -1L);
            Scribe_Values.Look(ref completedBodyDiscussions, "rrCompletedBodyDiscussions", 0);
            // Only absent, empty state gets initialized. Future/malformed raw
            // purchases and signed values stay available to a compatible reader.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && bodyPerks.SchemaVersion == 0 &&
                bodyPerks.EarnedPoints == 0d && bodyPerks.PurchasedNodeIds == null &&
                bodyPerks.PurchasedRawCosts == null && bodyPerks.LifetimeActiveTicks == 0L &&
                bodyPerks.WindowActiveTicks == 0 && bodyPerks.WindowUsefulNutrition == 0d &&
                bodyPerks.WindowExpenditureNutrition == 0d && bodyPerks.WindowCompletedDiscussions == 0 &&
                bodyPerks.LastSocialEventTick == -1 && bodyPerks.LastRespecTick == -1L)
            {
                bodyPerks.SchemaVersion = BodyPerkTreeState.CurrentVersion;
                bodyPerks.PurchasedNodeIds = new List<string>();
                bodyPerks.PurchasedRawCosts = new List<int>();
            }
        }

        private bool CanEarnBodyPerks => PerkTreeActive && parent.Spawned &&
            BodyModelEligibility.HasColonyManagementScope(parent as Pawn);
        private void RecordPerkIntake(float nutrition)
            => bodyPerks.RecordUsefulCommittedNutrition(nutrition, CanEarnBodyPerks);
        private void AdvanceBodyPerks(int ticks, float expenditure)
        {
            double awarded;
            bodyPerks.RecordActiveInterval(ticks, expenditure, CanEarnBodyPerks, out awarded);
        }

        // Called exactly once for each participant after a real body discussion.
        // Requirements can remember discussions while points are disabled.
        public void NotifyBodyDiscussionCompleted(int eventTick)
        {
            if (!bodyPerks.IsUsable || eventTick < 0 || eventTick <= bodyPerks.LastSocialEventTick) return;
            bodyPerks.RecordCompletedBodyDiscussion(eventTick, CanEarnBodyPerks);
            if (bodyPerks.LastSocialEventTick == eventTick && completedBodyDiscussions >= 0 &&
                completedBodyDiscussions < int.MaxValue) completedBodyDiscussions++;
        }

        private bool HasPerk(string id) => bodyPerks.PurchasedNodeIds?.Contains(id) == true;
        public string PerkPurchaseUnavailableReason(string id)
        {
            string inactive = PerkTreeInactiveReason;
            if (inactive != null) return inactive;
            if (!BodyModelEligibility.CanManage(parent as Pawn)) return "RR_TreeNoControl".Translate();
            BodyPerkNodeDefinition node;
            if (!BodyPerkTreeCatalog.TryGet(id, out node)) return "RR_TreeUnknownState".Translate();
            if (HasPerk(id)) return "RR_TreePurchased".Translate();
            if (node.FantasyOnly && !ColonyProgression.FantasyActive) return "RR_TreeFantasy".Translate();
            if (node.RequiresExceptionalAdaptationResearch && !ExceptionalAdaptationResearched)
                return "RR_TreeResearch".Translate();
            if (node.Prerequisites.Any(p => !HasPerk(p))) return "RR_TreePrerequisite".Translate();
            if (node.Exclusions.Any(HasPerk)) return "RR_TreeExclusive".Translate();
            if (ActiveProgressionTicks / 60000d < node.MinimumActiveDays || EatingConditioning < node.MinimumHabitConditioning)
                return "RR_TreeHabit".Translate();
            if (completedBodyDiscussions < node.MinimumCompletedBodyDiscussions) return "RR_TreeDiscussions".Translate();
            if (bodyPerks.AvailablePoints < node.Cost) return "RR_TreePoints".Translate();
            return null;
        }
        private bool ExceptionalAdaptationResearched =>
            DefDatabase<ResearchProjectDef>.GetNamedSilentFail("RR_Research_ArchotechAdaptation")?.IsFinished == true;
        public bool TryPurchaseBodyPerk(string id)
        {
            if (PerkPurchaseUnavailableReason(id) != null) return false;
            if (!bodyPerks.TryPurchase(id, PerkTreeActive, ActiveProgressionTicks / 60000d,
                EatingConditioning, completedBodyDiscussions, ExceptionalAdaptationResearched,
                ColonyProgression.FantasyActive)) return false;
            ((Pawn)parent).health?.hediffSet.DirtyCache();
            return true;
        }

        public bool HasOwnedSocialOrCareJob
        {
            get
            {
                Pawn pawn = parent as Pawn;
                if (RimRound.Ideology.BedsideRoleAbilities.HasSession(pawn) || RimRound.AI.BedsideBodyDiscussion.HasSession(pawn) ||
                    RimRound.Ideology.BedsideRituals.HasSession(pawn)) return true;
                if (IsProgressionSensitiveJob(pawn?.CurJob?.def)) return true;
                return pawn?.Map?.reservationManager.ReservationsReadOnly.Any(r =>
                    (r.Target.Thing == pawn || (pawn.CurrentBed() != null && r.Target.Thing == pawn.CurrentBed())) &&
                    IsProgressionSensitiveJob(r.Job?.def)) == true;
            }
        }
        private static bool IsProgressionSensitiveJob(JobDef def)
        {
            if (def == null) return false;
            if (def == JobDefOf.FeedPatient || def == JobDefOf.TendPatient || def == JobDefOf.Rescue ||
                def == JobDefOf.DoBill || def == JobDefOf.Ingest || def == JobDefOf.CastAbilityOnThing) return true;
            string name = def.defName;
            return name.StartsWith("RR_FeedOther", StringComparison.Ordinal) || name == "RR_BeFedOtherPartner" ||
                name == "RR_BodyPreferenceDiscussion" || name == "RR_ListenBodyDiscussion" ||
                name == "RR_FollowerRoleMeal" ||
                name == "RR_VisitBedsideRole" || name == "RR_BedsideRoleSession" ||
                name == "RR_AssistMobilityAid" || name == "RR_JD_StuffPrisoner";
        }
        public string PerkRespecUnavailableReason
        {
            get
            {
                string inactive = PerkTreeInactiveReason;
                if (inactive != null) return inactive;
                if (!BodyModelEligibility.CanManage(parent as Pawn)) return "RR_TreeNoControl".Translate();
                if (HasOwnedSocialOrCareJob) return "RR_TreeBusy".Translate();
                if ((bodyPerks.PurchasedNodeIds?.Count ?? 0) == 0) return "RR_TreeEmpty".Translate();
                if (bodyPerks.LastRespecTick >= 0 && (long)(Find.TickManager?.TicksGame ?? 0) -
                    bodyPerks.LastRespecTick < BodyPerkTreeState.RespecCooldownTicks) return "RR_TreeRespecCooldown".Translate();
                return null;
            }
        }
        public bool TryRespecBodyPerks()
        {
            if (PerkRespecUnavailableReason != null) return false;
            double refund;
            if (!bodyPerks.TryRespec(PerkTreeActive, Find.TickManager?.TicksGame ?? 0,
                HasOwnedSocialOrCareJob, out refund)) return false;
            ((Pawn)parent).health?.hediffSet.DirtyCache();
            return true;
        }
        public RimRoundStatBonuses BodyPerkBonuses
        {
            get
            {
                BodyPerkEffects effects = ActiveBodyPerkEffects;
                return new RimRoundStatBonuses { softLimitMultiplier = effects.CapacityBonus,
                    digestionRateMultiplier = effects.DigestionBonus, eatingFlatBonus = effects.EatingCapacityBonus,
                    painMitigationMultBonus_Fullness = effects.FullnessPainMitigation };
            }
        }
    }
}
