using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using SwellGlow;
using Verse;

namespace RimRound.FeedOther
{
    // Installed-only compatibility change. Keep the original gene Def names so
    // xenotypes, gene packs and existing saves keep their identity.
    public static class HeritableEatingBiology
    {
        private sealed class Family
        {
            internal readonly string Tag;
            internal readonly string[] Traits, Genes;
            internal readonly float[] Chances;
            internal Family(string tag, string[] traits, string[] genes, float[] chances)
            { Tag = tag; Traits = traits; Genes = genes; Chances = chances; }
        }

        private static readonly Family[] Families = {
            new Family("EatSpeedGene",
                new[] { "SG_TraitEatSpeedDownPlus", "SG_TraitEatSpeedDown", "RR_TraitEatSpeedAverage", "SG_TraitEatSpeedUp", "SG_TraitEatSpeedUpPlus" },
                new[] { "SG_GeneEatSpeedDownPlus", "SG_GeneEatSpeedDown", null, "SG_GeneEatSpeedUp", "SG_GeneEatSpeedUpPlus" },
                new[] { .05f, .20f, .50f, .20f, .05f }),
            new Family("ElasticGene",
                new[] { "SG_TraitStomachElasticityDown", "RR_TraitStomachElasticityAverage", "SG_TraitStomachElasticityUp", "SG_TraitStomachElasticityUpPlus" },
                new[] { "SG_GeneElasticDown", null, "SG_GeneElasticUp", "SG_GeneElasticUpPlus" },
                new[] { .20f, .60f, .15f, .05f }),
            new Family("DigestionGene",
                new[] { "SG_TraitDigestDown", null, "SG_TraitDigestUp", "SG_TraitDigestUpPlus" },
                new[] { "SG_GeneDigestDown", null, "SG_GeneDigestUp", "SG_GeneDigestUpPlus" },
                new[] { .20f, .60f, .15f, .05f }),
            new Family("WeightGene",
                new[] { "SG_TraitWeightGainDownPlus", "SG_TraitWeightGainDown", null, "SG_TraitWeightGainUp", "SG_TraitWeightGainUpPlus" },
                new[] { "SG_GeneWeightDownPlus", "SG_GeneWeightDown", null, "SG_GeneWeightUp", "SG_GeneWeightUpPlus" },
                new[] { .05f, .20f, .50f, .20f, .05f })
        };

        public static bool Eligible(Pawn pawn)
        {
            return ModsConfig.BiotechActive && pawn != null && !pawn.Destroyed &&
                pawn.RaceProps?.Humanlike == true && pawn.RaceProps.IsFlesh &&
                pawn.genes != null && pawn.story?.traits != null &&
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>() != null &&
                pawn.def.GetModExtension<BodyMetabolismExtension>()?.usesFoodMetabolism != false &&
                !pawn.genes.GenesListForReading.Any(g => g.Active &&
                    g.def.GetModExtension<BodyMetabolismExtension>()?.usesFoodMetabolism == false);
        }

        private static bool HasFamily(Pawn pawn, Family family)
        {
            // Include dormant genes and xenogenes: an editor or xenotype's
            // choices must not be replaced just because a gene is overridden.
            return pawn.genes.GenesListForReading.Any(g => family.Genes.Contains(g.def.defName) ||
                g.def.exclusionTags?.Contains(family.Tag) == true);
        }

        public static bool ConvertExisting(Pawn pawn)
        {
            if (!Eligible(pawn)) return false;
            bool changed = false;
            foreach (Family family in Families) changed |= ConvertFamily(pawn, family);
            return changed;
        }

        internal static bool IsPhysicalTrait(Trait trait)
        { return trait != null && Families.Any(f => f.Traits.Contains(trait.def.defName)); }

        private static bool ConvertFamily(Pawn pawn, Family family)
        {
            List<Trait> traits = pawn.story.traits.allTraits
                .Where(t => family.Traits.Contains(t.def.defName)).ToList();
            if (traits.Count == 0) return false;

            // A forced trait is only a display/effect proxy, not evidence that
            // an implanted xenogene should become inheritable.
            Trait natural = traits.LastOrDefault(t => t.sourceGene == null &&
                family.Genes[Array.IndexOf(family.Traits, t.def.defName)] != null);
            if (natural != null && !HasFamily(pawn, family))
            {
                string name = family.Genes[Array.IndexOf(family.Traits, natural.def.defName)];
                GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(name);
                if (def == null || pawn.genes.AddGene(def, false) == null)
                    return false; // Race restriction or missing Def: preserve the old effect.
            }
            foreach (Trait trait in traits)
            {
                // Native RemoveTrait also removes sourceGene. This migration
                // removes only the obsolete proxy, keeping the original gene.
                trait.sourceGene = null;
                trait.suppressedByTrait = false;
                trait.suppressedByGene = null;
                pawn.story.traits.RemoveTrait(trait, true);
            }
            return true;
        }

        public static void InitializeGenerated(Pawn pawn, PawnGenerationRequest request)
        {
            if (!Eligible(pawn)) return;
            var priorTraits = new HashSet<string>(pawn.story.traits.allTraits.Select(t => t.def.defName));
            ConvertExisting(pawn);

            // Pregnancy supplies the inherited set explicitly. Never fill gaps
            // in a newborn's inheritance, or reroll an explicit custom set.
            if (request.AllowedDevelopmentalStages.Newborn() ||
                request.ForcedEndogenes != null || request.ForcedCustomXenotype != null) return;

            for (int i = 0; i < Families.Length; i++)
            {
                Family family = Families[i];
                if (!GenerationEnabled(i) || family.Traits.Any(t => t != null && priorTraits.Contains(t)) ||
                    HasFamily(pawn, family)) continue;
                float roll = Rand.Value;
                int index = 0;
                for (; index < family.Chances.Length - 1; index++)
                {
                    if (roll < family.Chances[index]) break;
                    roll -= family.Chances[index];
                }
                string name = family.Genes[index];
                if (name == null) continue; // Normal physiology needs no marker gene.
                GeneDef def = DefDatabase<GeneDef>.GetNamedSilentFail(name);
                if (def == null) continue;
                int metabolism = pawn.genes.GenesListForReading.Where(g => g.Active).Sum(g => g.def.biostatMet);
                int combined = metabolism + def.biostatMet;
                if (combined >= GeneTuning.BiostatRange.min && combined <= GeneTuning.BiostatRange.max)
                    pawn.genes.AddGene(def, false);
            }
        }

        private static bool GenerationEnabled(int family)
        {
            switch (family)
            {
                case 0: return FeedOtherMod.Settings.guaranteedEatingStyleTraitsEnabled;
                case 1: return FeedOtherMod.Settings.guaranteedElasticityTraitsEnabled;
                case 2: return FeedOtherMod.Settings.heritableDigestionVariationEnabled;
                case 3: return FeedOtherMod.Settings.heritableMetabolismVariationEnabled;
                default: return false;
            }
        }

        internal static RimRoundStatBonuses LegacyGeneBonuses(Pawn pawn)
        {
            var result = new RimRoundStatBonuses();
            if (!Eligible(pawn)) return result;
            foreach (Gene gene in pawn.genes.GenesListForReading)
            {
                if (!gene.Active) continue;
                foreach (Family family in Families)
                {
                    int index = Array.IndexOf(family.Genes, gene.def.defName);
                    if (index < 0 || family.Traits[index] == null) continue;
                    // Before the load migration, the historical raw ledger may
                    // still contain the old proxy's contribution.
                    if (pawn.story.traits.allTraits.Any(t => t.def.defName == family.Traits[index])) continue;
                    SGTraitStatExtension x = DefDatabase<TraitDef>.GetNamedSilentFail(family.Traits[index])
                        ?.GetModExtension<SGTraitStatExtension>();
                    if (x == null) continue;
                    result = ProgressionSources.Add(result, new RimRoundStatBonuses {
                        weightGainMultBonus = x.weightGainMultiplierMultBonus,
                        weightLossMultBonus = x.weightLossMultiplierMultBonus,
                        digestionRateMultiplier = x.digestionSpeedMultBonus,
                        stomachElasticityMultiplier = x.stomachElasticityMultBonus,
                        fullnessGainedMultBonus = x.fullnessGainedMultiplierBonus,
                        eatingSpeedReductionMitigationMultBonus_Fullness = x.eatingSpeedReductionMitigationMultBonus_Fullness
                    });
                }
            }
            return result;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), "GenerateGenes")]
    [HarmonyAfter("Galactase.SwellGlow", "RRHarmony")]
    public static class PawnGenerator_HeritableEatingBiology
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn pawn, PawnGenerationRequest request)
        { HeritableEatingBiology.InitializeGenerated(pawn, request); }
    }

    [HarmonyPatch(typeof(TraitSet), nameof(TraitSet.ExposeData))]
    public static class HeritableEatingSavedProxies
    {
        // Native PostLoadInit silently drops trait proxies whose Def no longer
        // appears in forcedTraits. Defer only our proxies to the normal loaded-
        // game migration so RemoveTrait can also reconcile the saved ledger.
        [HarmonyPrefix]
        public static void Prefix(TraitSet __instance, out Dictionary<Trait, Gene> __state)
        {
            __state = null;
            if (!ModsConfig.BiotechActive || Scribe.mode != LoadSaveMode.PostLoadInit) return;
            foreach (Trait trait in __instance.allTraits)
            {
                if (trait.sourceGene == null || !HeritableEatingBiology.IsPhysicalTrait(trait)) continue;
                if (__state == null) __state = new Dictionary<Trait, Gene>();
                __state.Add(trait, trait.sourceGene);
                trait.sourceGene = null;
            }
        }
        [HarmonyPostfix]
        public static void Postfix(Dictionary<Trait, Gene> __state)
        {
            if (__state == null) return;
            foreach (var pair in __state) pair.Key.sourceGene = pair.Value;
        }
    }

    [HarmonyPatch(typeof(FullnessAndDietStats_ThingComp), nameof(FullnessAndDietStats_ThingComp.EffectiveStatBonuses), MethodType.Getter)]
    public static class HeritableEatingLegacyBonuses
    {
        [HarmonyPostfix]
        public static void Postfix(FullnessAndDietStats_ThingComp __instance, ref RimRoundStatBonuses __result)
        {
            // Balanced/Fantasy already read active genes directly. The Legacy
            // preset formerly got these effects through the removed trait proxy.
            if (ColonyProgression.LegacyPerksActive)
                __result = ProgressionSources.Add(__result, HeritableEatingBiology.LegacyGeneBonuses(__instance.parent as Pawn));
        }
    }
}
