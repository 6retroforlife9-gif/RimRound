using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using Verse;

namespace RimRound.FeedOther
{
    public static class WeightOpinionShiftSettingsUtility
    {
        public static void ApplyAbilityCooldown()
        {
            Apply(FeedOtherDefOf.RR_IncreaseWeightOpinion);
            Apply(FeedOtherDefOf.RR_DecreaseWeightOpinion);
        }

        private static void Apply(AbilityDef abilityDef)
        {
            if (abilityDef == null)
            {
                return;
            }

            int ticks = FeedOtherMod.Settings.PersuasionCooldownTicks;
            abilityDef.cooldownTicksRange = new IntRange(ticks, ticks);
        }
    }

    public class CompProperties_WeightOpinionShift : CompProperties_AbilityEffect
    {
        public bool increase;

        public CompProperties_WeightOpinionShift()
        {
            compClass = typeof(CompAbilityEffect_WeightOpinionShift);
        }
    }

    public class CompAbilityEffect_WeightOpinionShift : CompAbilityEffect
    {
        private static int SharedCooldownTicks => FeedOtherMod.Settings.PersuasionCooldownTicks;
        private static float MinimumSuccessChance => FeedOtherMod.Settings.PersuasionMinimumChance;
        private static float MaximumSuccessChance => FeedOtherMod.Settings.PersuasionMaximumChance;
        private static float BaseChanceAtSocialOne => FeedOtherMod.Settings.PersuasionBaseChance;
        private static float ChancePerAdditionalSocialLevel => FeedOtherMod.Settings.PersuasionPerSocialLevelChance;
        private static float OpinionChancePerPoint => FeedOtherMod.Settings.PersuasionOpinionChancePerPoint;
        private static float RomanticRelationshipBonus => FeedOtherMod.Settings.PersuasionRomanticBonus;
        private static float CloseFamilyRelationshipBonus => FeedOtherMod.Settings.PersuasionFamilyBonus;

        public new CompProperties_WeightOpinionShift Props
        {
            get { return (CompProperties_WeightOpinionShift)props; }
        }

        public override bool HideTargetPawnTooltip
        {
            get { return true; }
        }

        public override bool ShouldHideGizmo
        {
            get
            {
                Pawn caster = parent?.pawn;
                return !FeedOtherMod.Settings.weightOpinionPersuasionEnabled ||
                    caster == null ||
                    caster.Faction != Faction.OfPlayer ||
                    !caster.RaceProps.Humanlike ||
                    caster.ageTracker == null ||
                    caster.ageTracker.AgeBiologicalYearsFloat < 18f ||
                    caster.TryGetComp<ThingComp_PawnAttitude>() == null;
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!FeedOtherMod.Settings.weightOpinionPersuasionEnabled)
            {
                return false;
            }

            Pawn targetPawn = target.Pawn;
            if (targetPawn == null)
            {
                return false;
            }

            if (targetPawn == parent.pawn)
            {
                return Reject(targetPawn, "RR_WeightOpinionShiftCannotTargetSelf", throwMessages);
            }

            if (!targetPawn.RaceProps.Humanlike ||
                targetPawn.ageTracker == null ||
                targetPawn.ageTracker.AgeBiologicalYearsFloat < 18f)
            {
                return Reject(targetPawn, "RR_WeightOpinionShiftTargetMustBeAdult", throwMessages);
            }

            if (targetPawn.Dead || targetPawn.Downed || !targetPawn.Awake() || targetPawn.MentalState != null)
            {
                return Reject(targetPawn, "RR_WeightOpinionShiftTargetUnavailable", throwMessages);
            }

            ThingComp_PawnAttitude attitude = targetPawn.TryGetComp<ThingComp_PawnAttitude>();
            if (attitude == null || targetPawn.story?.traits == null)
            {
                return Reject(targetPawn, "RR_WeightOpinionShiftTargetUnsupported", throwMessages);
            }

            WeightOpinion current = Normalize(attitude.weightOpinion);
            if (Props.increase && current >= WeightOpinion.Fanatical)
            {
                return Reject(targetPawn, "RR_WeightOpinionShiftAlreadyMaximum", throwMessages);
            }

            if (!Props.increase && current <= WeightOpinion.Hate)
            {
                return Reject(targetPawn, "RR_WeightOpinionShiftAlreadyMinimum", throwMessages);
            }

            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn targetPawn = target.Pawn;
            ThingComp_PawnAttitude attitude = targetPawn?.TryGetComp<ThingComp_PawnAttitude>();
            if (targetPawn == null || attitude == null)
            {
                return;
            }

            int effectiveSocialLevel;
            int targetOpinion;
            float relationshipBonus;
            float successChance = CalculateSuccessChance(
                parent.pawn,
                targetPawn,
                out effectiveSocialLevel,
                out targetOpinion,
                out relationshipBonus);

            // The ability itself starts its own cooldown after casting. Start the
            // other direction here so a failed attempt cannot bypass the shared
            // cooldown by immediately trying the opposite button.
            StartSiblingCooldown();

            if (!Rand.Chance(successChance))
            {
                Messages.Message(
                    "RR_WeightOpinionShiftFailure".Translate(
                        parent.pawn.Named("INITIATOR"),
                        targetPawn.Named("TARGET"),
                        FormatChance(successChance).Named("CHANCE")),
                    targetPawn,
                    MessageTypeDefOf.NeutralEvent,
                    true);
                return;
            }

            WeightOpinion oldOpinion = Normalize(attitude.weightOpinion);
            WeightOpinion newOpinion = Props.increase
                ? (WeightOpinion)Math.Min((int)WeightOpinion.Fanatical, (int)oldOpinion + 1)
                : (WeightOpinion)Math.Max((int)WeightOpinion.Hate, (int)oldOpinion - 1);

            if (newOpinion == oldOpinion)
            {
                return;
            }

            attitude.WeightOpinionFloat = RepresentativeOpinionValue(newOpinion);

            Messages.Message(
                "RR_WeightOpinionShiftSuccess".Translate(
                    parent.pawn.Named("INITIATOR"),
                    targetPawn.Named("TARGET"),
                    OpinionLabel(oldOpinion).Named("OLDOPINION"),
                    OpinionLabel(newOpinion).Named("NEWOPINION")),
                targetPawn,
                MessageTypeDefOf.PositiveEvent,
                true);
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            Pawn targetPawn = target.Pawn;
            ThingComp_PawnAttitude attitude = targetPawn?.TryGetComp<ThingComp_PawnAttitude>();
            if (targetPawn == null || attitude == null || !Valid(target))
            {
                return null;
            }

            WeightOpinion oldOpinion = Normalize(attitude.weightOpinion);
            WeightOpinion newOpinion = Props.increase
                ? (WeightOpinion)Math.Min((int)WeightOpinion.Fanatical, (int)oldOpinion + 1)
                : (WeightOpinion)Math.Max((int)WeightOpinion.Hate, (int)oldOpinion - 1);

            int effectiveSocialLevel;
            int targetOpinion;
            float relationshipBonus;
            float successChance = CalculateSuccessChance(
                parent.pawn,
                targetPawn,
                out effectiveSocialLevel,
                out targetOpinion,
                out relationshipBonus);

            return "RR_WeightOpinionShiftPreview".Translate(
                OpinionLabel(oldOpinion).Named("OLDOPINION"),
                OpinionLabel(newOpinion).Named("NEWOPINION"),
                FormatChance(successChance).Named("CHANCE"),
                effectiveSocialLevel.ToString().Named("SOCIAL"),
                targetOpinion.ToString("+0;-0;0").Named("TARGETOPINION"),
                RelationshipLabel(relationshipBonus).Named("RELATIONSHIP"));
        }

        private static float CalculateSuccessChance(
            Pawn initiator,
            Pawn target,
            out int effectiveSocialLevel,
            out int targetOpinion,
            out float relationshipBonus)
        {
            int actualSocialLevel = initiator?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            effectiveSocialLevel = Math.Max(1, Math.Min(10, actualSocialLevel));

            targetOpinion = target?.relations != null && initiator != null
                ? Math.Max(-100, Math.Min(100, target.relations.OpinionOf(initiator)))
                : 0;

            relationshipBonus = RelationshipBonus(initiator, target);

            float chance = BaseChanceAtSocialOne +
                (effectiveSocialLevel - 1) * ChancePerAdditionalSocialLevel +
                targetOpinion * OpinionChancePerPoint +
                relationshipBonus;

            return Math.Max(MinimumSuccessChance, Math.Min(MaximumSuccessChance, chance));
        }

        private static float RelationshipBonus(Pawn initiator, Pawn target)
        {
            if (HasDirectRelation(initiator, target, PawnRelationDefOf.Spouse) ||
                HasDirectRelation(initiator, target, PawnRelationDefOf.Fiance) ||
                HasDirectRelation(initiator, target, PawnRelationDefOf.Lover))
            {
                return RomanticRelationshipBonus;
            }

            if (HasDirectRelation(initiator, target, PawnRelationDefOf.Parent) ||
                HasDirectRelation(initiator, target, PawnRelationDefOf.Child) ||
                HasDirectRelation(initiator, target, PawnRelationDefOf.Sibling) ||
                HasDirectRelation(initiator, target, PawnRelationDefOf.HalfSibling))
            {
                return CloseFamilyRelationshipBonus;
            }

            return 0f;
        }

        private static bool HasDirectRelation(Pawn first, Pawn second, PawnRelationDef relation)
        {
            if (first == null || second == null || relation == null)
            {
                return false;
            }

            return (first.relations != null && first.relations.DirectRelationExists(relation, second)) ||
                (second.relations != null && second.relations.DirectRelationExists(relation, first));
        }

        private static string RelationshipLabel(float relationshipBonus)
        {
            if (RomanticRelationshipBonus > 0f &&
                relationshipBonus >= RomanticRelationshipBonus)
            {
                return "RR_WeightOpinionShiftRelationshipRomantic".Translate(
                    Math.Round(RomanticRelationshipBonus * 100f)
                        .ToString("F0")
                        .Named("BONUS"));
            }

            if (CloseFamilyRelationshipBonus > 0f &&
                relationshipBonus >= CloseFamilyRelationshipBonus)
            {
                return "RR_WeightOpinionShiftRelationshipFamily".Translate(
                    Math.Round(CloseFamilyRelationshipBonus * 100f)
                        .ToString("F0")
                        .Named("BONUS"));
            }

            return "RR_WeightOpinionShiftRelationshipNone".Translate();
        }

        private static string FormatChance(float chance)
        {
            return (chance * 100f).ToString("F0");
        }

        private void StartSiblingCooldown()
        {
            AbilityDef siblingDef = Props.increase
                ? FeedOtherDefOf.RR_DecreaseWeightOpinion
                : FeedOtherDefOf.RR_IncreaseWeightOpinion;
            Ability sibling = parent.pawn?.abilities?.GetAbility(siblingDef, false);
            if (sibling != null && sibling.CooldownTicksRemaining < SharedCooldownTicks)
            {
                sibling.StartCooldown(SharedCooldownTicks);
            }
        }

        private static bool Reject(Pawn targetPawn, string key, bool throwMessages)
        {
            if (throwMessages)
            {
                Messages.Message(key.Translate(targetPawn.Named("TARGET")), targetPawn, MessageTypeDefOf.RejectInput, false);
            }

            return false;
        }

        private static WeightOpinion Normalize(WeightOpinion opinion)
        {
            if (opinion == WeightOpinion.None)
            {
                return WeightOpinion.Neutral;
            }

            if (opinion > WeightOpinion.Fanatical)
            {
                return WeightOpinion.Fanatical;
            }

            return opinion;
        }

        private static float RepresentativeOpinionValue(WeightOpinion opinion)
        {
            switch (opinion)
            {
                case WeightOpinion.Hate:
                    return 125f;
                case WeightOpinion.Dislike:
                    return 300f;
                case WeightOpinion.NeutralMinus:
                    return 375f;
                case WeightOpinion.Neutral:
                    return 425f;
                case WeightOpinion.NeutralPlus:
                    return 475f;
                case WeightOpinion.Like:
                    return 600f;
                case WeightOpinion.Love:
                    return 850f;
                case WeightOpinion.Fanatical:
                    return 1250f;
                default:
                    return 425f;
            }
        }

        private static string OpinionLabel(WeightOpinion opinion)
        {
            switch (opinion)
            {
                case WeightOpinion.Hate:
                    return "Hate";
                case WeightOpinion.Dislike:
                    return "Dislike";
                case WeightOpinion.NeutralMinus:
                    return "Neutral-";
                case WeightOpinion.Neutral:
                    return "Neutral";
                case WeightOpinion.NeutralPlus:
                    return "Neutral+";
                case WeightOpinion.Like:
                    return "Like";
                case WeightOpinion.Love:
                    return "Love";
                case WeightOpinion.Fanatical:
                    return "Fanatical";
                default:
                    return "Neutral";
            }
        }
    }

    [HarmonyPatch]
    public static class Ability_StartCooldown_WeightOpinionSettingPatch
    {
        [HarmonyTargetMethod]
        public static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Ability),
                nameof(Ability.StartCooldown),
                new[] { typeof(int) });
        }

        [HarmonyPrefix]
        public static void Prefix(Ability __instance, ref int __0)
        {
            if (__instance?.def == FeedOtherDefOf.RR_IncreaseWeightOpinion ||
                __instance?.def == FeedOtherDefOf.RR_DecreaseWeightOpinion)
            {
                __0 = FeedOtherMod.Settings.PersuasionCooldownTicks;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Pawn_SpawnSetup_GrantWeightOpinionShiftAbilities
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (__instance?.abilities == null ||
                !__instance.RaceProps.Humanlike ||
                __instance.TryGetComp<ThingComp_PawnAttitude>() == null)
            {
                return;
            }

            if (FeedOtherDefOf.RR_IncreaseWeightOpinion != null)
            {
                __instance.abilities.GainAbility(FeedOtherDefOf.RR_IncreaseWeightOpinion);
            }

            if (FeedOtherDefOf.RR_DecreaseWeightOpinion != null)
            {
                __instance.abilities.GainAbility(FeedOtherDefOf.RR_DecreaseWeightOpinion);
            }
        }
    }
}
