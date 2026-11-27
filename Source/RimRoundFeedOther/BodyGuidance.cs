using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimRound.AI;
using RimRound.Comps;
using RimRound.Ideology;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public static class BodyGuidanceUtility
    {
        public const string Larger = "RR_EncourageLargerBody", Smaller = "RR_EncourageSmallerBody";
        public static bool IsGuidance(Ability ability) => IsGuidance(ability?.def);
        public static bool IsGuidance(AbilityDef def) => def != null && (def.defName == Larger || def.defName == Smaller);

        public static bool OwnsRole(Ability ability)
        {
            var role = ability?.sourcePrecept as Precept_RoleSingle;
            return ModsConfig.IdeologyActive && IsGuidance(ability) && ability.pawn != null && role != null &&
                role.def == PreceptDefOf.IdeoRole_Moralist && role.Active && role.IsAssigned(ability.pawn) &&
                role.ideo != null && role.ideo == ability.pawn.Ideo && role.ideo.PreceptsListForReading.Contains(role) &&
                role.AbilitiesForReading?.Contains(ability) == true &&
                ability.pawn.abilities?.AllAbilitiesForReading.Contains(ability) == true;
        }

        private static float Capacity(Pawn p, PawnCapacityDef def) => p.health.capacities.GetLevel(def);
        // Public readiness is the guide's gate; recipients only need communication readiness.
        public static bool Ready(Pawn p) => ReadyToCommunicate(p) &&
            !p.WorkTagIsDisabled(WorkTags.Social) && p.skills?.GetSkill(SkillDefOf.Social).TotallyDisabled == false;

        private static bool ReadyToCommunicate(Pawn p) => BodyPreferenceSocial.OrdinarySpeaker(p) && p.Spawned &&
            p.TryGetComp<ThingComp_PawnAttitude>() != null && !p.Drafted && !p.InMentalState &&
            !p.Suspended && !p.Deathresting && p.Awake() && !p.IsBurning() &&
            Capacity(p, PawnCapacityDefOf.Talking) > 0f && Capacity(p, PawnCapacityDefOf.Hearing) > 0f &&
            Capacity(p, PawnCapacityDefOf.Consciousness) > 0f &&
            (BedsideRoleAbilities.IsReadyBedOccupant(p) || (!p.Downed && Capacity(p, PawnCapacityDefOf.Moving) > 0f));

        public static bool TargetReady(Ability ability, Pawn recipient, bool larger, out string reason)
        {
            reason = "RR_GuidanceRoleRequired".Translate();
            if (!OwnsRole(ability)) return false;
            Pawn speaker = ability.pawn;
            reason = "RR_GuidanceNotReady".Translate();
            if (speaker == recipient || !Ready(speaker) || !ReadyToCommunicate(recipient) || speaker.Map != recipient.Map) return false;
            if (!BodyPreferenceSocial.OrdinaryColonist(recipient) ||
                recipient.TryGetComp<ThingComp_PawnAttitude>()?.HasPreference != true) return false;
            var a = speaker.TryGetComp<ThingComp_PawnAttitude>();
            var b = recipient.TryGetComp<ThingComp_PawnAttitude>();
            int now = Find.TickManager.TicksGame;
            reason = "RR_DiscussionCooldown".Translate();
            if (now > int.MaxValue - BodySocialProgression.CooldownTicks ||
                !a.DiscussionClock.CanParticipate(now, BodyPreferenceSocial.Day) ||
                !b.DiscussionClock.CanParticipate(now, BodyPreferenceSocial.Day)) return false;
            reason = "RR_GuidanceAtLimit".Translate();
            if (!BodySocialProgression.CaptureTarget(b.PreferredLowerRatio, b.PreferredUpperRatio, larger,
                out float lower, out float upper) || BodySocialProgression.AtTarget(b.PreferredLowerRatio, b.PreferredUpperRatio, lower, upper)) return false;
            reason = null;
            return true;
        }

        // Keep the canonical ordinary-discussion calculation for refusal and
        // influence inputs. Scale one outcome, before breakthrough/interpolation;
        // never award three events or repeatedly reset conviction.
        public static BodyDiscussionResult Calculate(Pawn speaker, Pawn recipient, bool larger, float sample)
        {
            var a = speaker.TryGetComp<FullnessAndDietStats_ThingComp>();
            var b = recipient.TryGetComp<ThingComp_PawnAttitude>();
            if (!BodySocialProgression.CaptureTarget(b.PreferredLowerRatio, b.PreferredUpperRatio, larger,
                out float lower, out float upper)) return default(BodyDiscussionResult);
            BodyDiscussionResult result = BodySocialProgression.Discuss(b.PreferredLowerRatio, b.PreferredUpperRatio,
                b.PreferenceConviction, lower, upper, speaker.skills.GetSkill(SkillDefOf.Social).Level,
                Math.Min(Capacity(speaker, PawnCapacityDefOf.Talking), Capacity(recipient, PawnCapacityDefOf.Talking)),
                Math.Min(Capacity(speaker, PawnCapacityDefOf.Hearing), Capacity(recipient, PawnCapacityDefOf.Hearing)),
                Math.Min(Capacity(speaker, PawnCapacityDefOf.Consciousness), Capacity(recipient, PawnCapacityDefOf.Consciousness)),
                recipient.relations.OpinionOf(speaker), b.DiscussionClock.Irritation,
                a?.ActiveBodyPerkEffects.BodyPersuasionBonus ?? 0f, sample);
            if (result.Outcome != BodyDiscussionOutcome.Receptive && result.Outcome != BodyDiscussionOutcome.Breakthrough) return result;
            result.Influence = Math.Min(.9f, result.Influence * 3f);
            result.Conviction = Math.Max(0f, b.PreferenceConviction - result.Influence);
            if (result.Conviction <= 0f)
            {
                result.Lower = lower; result.Upper = upper; result.Conviction = .5f;
                result.Outcome = BodyDiscussionOutcome.Breakthrough;
            }
            else
            {
                result.Lower = LogLerp(b.PreferredLowerRatio, lower, result.Influence);
                result.Upper = LogLerp(b.PreferredUpperRatio, upper, result.Influence);
                result.Outcome = BodyDiscussionOutcome.Receptive;
            }
            return result;
        }
        private static float LogLerp(float from, float to, float fraction) =>
            BodySocialProgression.Clamp((float)Math.Exp(Math.Log(from) + Math.Log(to / from) * fraction),
                BodyPreferenceState.MinimumRatio, BodyPreferenceState.MaximumRatio);

        // This is a view of the character card only. Saved abilities, ticking,
        // role ownership and the normal command bar retain their original list.
        public static List<Ability> CharacterCardAbilities(Pawn_AbilityTracker tracker) =>
            tracker.AllAbilitiesForReading.Where(a =>
                (a.def.defName != "RR_IncreaseWeightOpinion" && a.def.defName != "RR_DecreaseWeightOpinion") ||
                (Prefs.DevMode && DebugSettings.godMode &&
                a.EffectComps.OfType<CompAbilityEffect_WeightOpinionShift>().Any(c => !c.ShouldHideGizmo))).ToList();
    }

    public sealed class CompProperties_BodyGuidance : CompProperties_AbilityEffect
    {
        public bool larger;
        public CompProperties_BodyGuidance() { compClass = typeof(CompAbilityEffect_BodyGuidance); }
    }

    public sealed class CompAbilityEffect_BodyGuidance : CompAbilityEffect
    {
        private new CompProperties_BodyGuidance Props => (CompProperties_BodyGuidance)props;
        public override bool ShouldHideGizmo => !BodyGuidanceUtility.OwnsRole(parent);
        public override bool GizmoDisabled(out string reason)
        {
            reason = "RR_GuidanceRoleRequired".Translate();
            if (!BodyGuidanceUtility.OwnsRole(parent)) return true;
            reason = "RR_GuidanceNotReady".Translate();
            if (!BodyGuidanceUtility.Ready(parent.pawn)) return true;
            reason = "RR_DiscussionCooldown".Translate();
            if (!parent.pawn.TryGetComp<ThingComp_PawnAttitude>().DiscussionClock.CanParticipate(Find.TickManager.TicksGame, BodyPreferenceSocial.Day)) return true;
            reason = null;
            return false;
        }
        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest) => Valid(target, false);
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (BodyGuidanceUtility.TargetReady(parent, target.Pawn, Props.larger, out string reason)) return true;
            if (throwMessages) Messages.Message(reason, target.Pawn ?? parent.pawn, MessageTypeDefOf.RejectInput, false);
            return false;
        }
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn speaker = parent.pawn, recipient = target.Pawn;
            if (!Valid(target) || !BedsideRoleAbilities.HasLocalContact(speaker, recipient)) return;
            var a = speaker.TryGetComp<ThingComp_PawnAttitude>();
            var b = recipient.TryGetComp<ThingComp_PawnAttitude>();
            int now = Find.TickManager.TicksGame, day = BodyPreferenceSocial.Day;
            string eventId = "rr-guidance/" + parent.GetUniqueLoadID() + "/" + now;
            if (!a.DiscussionClock.CanRecord(now, day, eventId) || !b.DiscussionClock.CanRecord(now, day, eventId)) return;
            float oldConviction = b.PreferenceConviction;
            BodyDiscussionResult result = BodyGuidanceUtility.Calculate(speaker, recipient, Props.larger, Rand.Value);
            if (!result.Completed || !b.TrySetPreference(result.Lower, result.Upper, result.Conviction)) return;
            a.DiscussionClock.Record(now, day, eventId, result.Outcome, true);
            b.DiscussionClock.Record(now, day, eventId, result.Outcome, false);
            b.DiscussionClock.Irritation = result.Irritation;
            speaker.TryGetComp<FullnessAndDietStats_ThingComp>()?.NotifyBodyDiscussionCompleted(now);
            recipient.TryGetComp<FullnessAndDietStats_ThingComp>()?.NotifyBodyDiscussionCompleted(now);
            string key = result.Outcome == BodyDiscussionOutcome.Irritated ? "RR_GuidanceIrritated" :
                result.Outcome == BodyDiscussionOutcome.Breakthrough ? "RR_GuidanceBreakthrough" : "RR_GuidanceReceptive";
            Messages.Message(key.Translate(speaker.LabelShortCap, recipient.LabelShortCap,
                (Props.larger ? "RR_GuidanceLarger" : "RR_GuidanceSmaller").Translate(),
                oldConviction.ToStringPercent(), result.Conviction.ToStringPercent()),
                new LookTargets(speaker, recipient), MessageTypeDefOf.NeutralEvent, false);
        }
    }

    [HarmonyPatch(typeof(CharacterCardUtility), "DoLeftSection")]
    internal static class BodyGuidanceCharacterCardPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            MethodInfo getter = AccessTools.PropertyGetter(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.AllAbilitiesForReading));
            if (code.Count(i => i.Calls(getter)) != 1)
                throw new InvalidOperationException("RimRound character card ability filter requires exactly one ability-list read.");
            foreach (CodeInstruction instruction in code)
                if (instruction.Calls(getter))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(BodyGuidanceUtility), nameof(BodyGuidanceUtility.CharacterCardAbilities)); }
            return code;
        }
    }

    [HarmonyPatch(typeof(BedsideRoleAbilities), "Supported")]
    internal static class BodyGuidanceBedsidePatch
    {
        private static void Postfix(Ability a, ref bool __result)
        {
            if (!__result && ModsConfig.IdeologyActive && BodyGuidanceUtility.IsGuidance(a) &&
                a.GetType() == typeof(Ability) && a.verb?.GetType() == typeof(Verb_CastAbilityTouch)) __result = true;
        }
    }

    [HarmonyPatch(typeof(BedsideRoleAbilities), "BeforeRoleUnassigned")]
    internal static class BodyGuidanceRoleCancellationPatch
    {
        private static void Prefix(Precept_Role role, Pawn oldPawn)
        {
            if (Scribe.mode != LoadSaveMode.Inactive || Current.ProgramState != ProgramState.Playing ||
                !(role is Precept_RoleSingle single) || role.def != PreceptDefOf.IdeoRole_Moralist || oldPawn?.jobs == null) return;
            // Native role transfer reuses the Ability and Verb. Cancel the old
            // owner's mobile jobs before native code rewrites their caster.
            // The core adapter separately owns cancellation of bedside sessions.
            var abilities = (single.AbilitiesForReading ?? new List<Ability>()).Where(a => BodyGuidanceUtility.IsGuidance(a) &&
                a.pawn == oldPawn && a.sourcePrecept == role).ToList();
            if (abilities.Count == 0) return;
            bool Matches(Job job) => job != null && abilities.Any(a => job.ability == a || job.verbToUse == a.verb);
            oldPawn.jobs.jobQueue.RemoveAll(oldPawn, Matches);
            foreach (Ability ability in abilities)
            {
                if (oldPawn.stances?.curStance is Stance_Warmup warmup && warmup.verb == ability.verb)
                    warmup.Interrupt();
                ability.verb?.Reset();
            }
            if (Matches(oldPawn.CurJob)) oldPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }
    }

    [HarmonyPatch(typeof(IdeoDevelopmentUtility), nameof(IdeoDevelopmentUtility.ApplyChangesToIdeo))]
    internal static class BodyGuidanceReformBindingPatch
    {
        private static void Postfix(Ideo ideo)
        {
            if (!ModsConfig.IdeologyActive || ideo == null) return;
            foreach (Precept_RoleSingle role in ideo.PreceptsListForReading.OfType<Precept_RoleSingle>())
            {
                Pawn owner = role.ChosenPawnValue;
                if (role.def != PreceptDefOf.IdeoRole_Moralist || role.chosenPawn == null || owner == null ||
                    owner.Ideo != ideo || role.def.grantedAbilities?.Any(BodyGuidanceUtility.IsGuidance) != true) continue;
                // Native reform copies the role instance and its abilities, but
                // does not run the binding refresh performed on native load.
                // Refresh the surviving live role without replacing abilities
                // or changing their cooldowns or ownership requirements.
                role.chosenPawn.sourceRole = role;
                role.FillOrUpdateAbilities();
                owner.abilities?.Notify_TemporaryAbilitiesChanged();
            }
        }
    }

    [HarmonyPatch(typeof(Precept_Role), "FillOrUpdateAbilityList")]
    internal static class BodyGuidanceRoleUpgradePatch
    {
        private static void Prefix(Precept_Role __instance, Pawn forPawn, List<Ability> abilities)
        {
            if (__instance.def != PreceptDefOf.IdeoRole_Moralist || abilities == null || __instance.def.grantedAbilities == null) return;
            int remaining = abilities.Where(a => BodyGuidanceUtility.IsGuidance(a)).Select(a => a.CooldownTicksRemaining).DefaultIfEmpty(0).Max();
            foreach (AbilityDef def in __instance.def.grantedAbilities.Where(BodyGuidanceUtility.IsGuidance))
                if (!abilities.Any(a => a?.def == def))
                {
                    Ability ability = AbilityUtility.MakeAbility(def, forPawn, __instance);
                    if (remaining > 0) ability.StartCooldown(remaining);
                    abilities.Add(ability);
                }
        }
    }
}
