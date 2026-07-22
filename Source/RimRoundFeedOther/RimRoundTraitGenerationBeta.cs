using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using UnityEngine;

namespace RimRound.FeedOther
{
    /// <summary>
    /// Beta trait-generation layer. Vanilla, DLC and ordinary mod traits are
    /// generated first. RimRound then adds its normal weight-opinion trait, and
    /// this finalizer guarantees one eating-style marker and one stomach-
    /// elasticity marker without consuming vanilla personality-trait slots.
    /// </summary>
    public static class RimRoundTraitGenerationBeta
    {
        public const string EatingStyleExclusionTag = "RR_Trait_EatingSpeed";
        public const string StomachElasticityExclusionTag = "RR_Trait_StomachElasticity";

        private static readonly string[] EatingStyleDefs =
        {
            "SG_TraitEatSpeedDownPlus",
            "SG_TraitEatSpeedDown",
            "RR_TraitEatSpeedAverage",
            "SG_TraitEatSpeedUp",
            "SG_TraitEatSpeedUpPlus"
        };

        private static readonly string[] StomachElasticityDefs =
        {
            "SG_TraitStomachElasticityDown",
            "RR_TraitStomachElasticityAverage",
            "SG_TraitStomachElasticityUp",
            "SG_TraitStomachElasticityUpPlus"
        };

        private static Dictionary<TraitDef, float> originalWeightOpinionDistribution;

        public static void InitializeBetaRules()
        {
            ApplyConfiguredOpinionDistribution();
        }

        public static void ApplyConfiguredOpinionDistribution()
        {
            if (originalWeightOpinionDistribution == null &&
                WeightOpinionUtility.traitAndCommonalityPair != null)
            {
                originalWeightOpinionDistribution =
                    new Dictionary<TraitDef, float>(
                        WeightOpinionUtility.traitAndCommonalityPair);
            }

            if (FeedOtherMod.Settings.balancedWeightOpinionDistributionEnabled)
            {
                ConfigureWeightOpinionDistribution();
            }
            else if (originalWeightOpinionDistribution != null)
            {
                WeightOpinionUtility.traitAndCommonalityPair =
                    new Dictionary<TraitDef, float>(originalWeightOpinionDistribution);
            }
        }

        public static void ConfigureWeightOpinionDistribution()
        {
            var weights = new Dictionary<TraitDef, float>();
            AddWeight(weights, "RR_WeightOpinion_Hate_Trait", 0.03f);
            AddWeight(weights, "RR_WeightOpinion_Dislike_Trait", 0.07f);
            AddWeight(weights, "RR_WeightOpinion_NeutralMinus_Trait", 0.12f);
            AddWeight(weights, "RR_WeightOpinion_Neutral_Trait", 0.36f);
            AddWeight(weights, "RR_WeightOpinion_NeutralPlus_Trait", 0.20f);
            AddWeight(weights, "RR_WeightOpinion_Like_Trait", 0.12f);
            AddWeight(weights, "RR_WeightOpinion_Love_Trait", 0.07f);
            AddWeight(weights, "RR_WeightOpinion_Fanatical_Trait", 0.03f);

            if (weights.Count == 8)
            {
                WeightOpinionUtility.traitAndCommonalityPair = weights;
            }
            else
            {
                Log.Error("[RimRound Feed Other beta] Could not install the balanced weight-opinion distribution because one or more RimRound TraitDefs were missing.");
            }
        }

        private static void AddWeight(
            IDictionary<TraitDef, float> weights,
            string defName,
            float weight)
        {
            TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
            if (traitDef != null)
            {
                weights.Add(traitDef, weight);
            }
        }

        public static bool EnsureTraitCategories(Pawn pawn)
        {
            if (!IsEligible(pawn))
            {
                return false;
            }

            bool changed = false;
            if (FeedOtherMod.Settings.guaranteedEatingStyleTraitsEnabled)
            {
                changed |= EnsureSingleCategory(
                    pawn,
                    EatingStyleExclusionTag,
                    EatingStyleDefs,
                    "RR_TraitEatSpeedAverage",
                    RollEatingStyleDef);
            }

            if (FeedOtherMod.Settings.guaranteedElasticityTraitsEnabled)
            {
                changed |= EnsureSingleCategory(
                    pawn,
                    StomachElasticityExclusionTag,
                    StomachElasticityDefs,
                    "RR_TraitStomachElasticityAverage",
                    RollStomachElasticityDef);
            }
            return changed;
        }

        private static bool IsEligible(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && pawn.RaceProps != null &&
                pawn.RaceProps.Humanlike && pawn.story?.traits != null &&
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>() != null;
        }

        private static bool EnsureSingleCategory(
            Pawn pawn,
            string exclusionTag,
            IEnumerable<string> knownDefNames,
            string averageDefName,
            System.Func<TraitDef> roller)
        {
            HashSet<string> knownNames = new HashSet<string>(knownDefNames);
            List<Trait> existing = pawn.story.traits.allTraits
                .Where(trait => IsCategoryTrait(trait, exclusionTag, knownNames))
                .ToList();

            if (existing.Count > 0)
            {
                // A pawn editor may add a deliberate specific trait after this
                // beta already generated the neutral marker. Prefer the newest
                // non-average entry; otherwise preserve the newest entry. This
                // keeps player/editor choices while still repairing duplicates.
                Trait keep = existing.LastOrDefault(trait =>
                    trait.def.defName != averageDefName) ?? existing.Last();
                foreach (Trait trait in existing)
                {
                    if (trait != keep)
                    {
                        pawn.story.traits.RemoveTrait(trait);
                    }
                }
                return existing.Count > 1;
            }

            TraitDef selected = roller();
            if (selected == null)
            {
                Log.Warning("[RimRound Feed Other beta] No valid trait could be selected for " +
                    exclusionTag + " on " + pawn.ToStringSafe() + ".");
                return false;
            }

            pawn.story.traits.GainTrait(new Trait(selected, 0, true));
            return true;
        }

        private static bool IsCategoryTrait(
            Trait trait,
            string exclusionTag,
            HashSet<string> knownNames)
        {
            TraitDef def = trait?.def;
            if (def == null)
            {
                return false;
            }

            return knownNames.Contains(def.defName) ||
                (def.exclusionTags != null && def.exclusionTags.Contains(exclusionTag));
        }

        private static TraitDef RollEatingStyleDef()
        {
            // 5% very slow, 20% slow, 50% average, 20% fast, 5% very fast.
            float roll = Rand.Value;
            if (roll < 0.05f)
            {
                return TraitDefNamed("SG_TraitEatSpeedDownPlus");
            }
            if (roll < 0.25f)
            {
                return TraitDefNamed("SG_TraitEatSpeedDown");
            }
            if (roll < 0.75f)
            {
                return TraitDefNamed("RR_TraitEatSpeedAverage");
            }
            if (roll < 0.95f)
            {
                return TraitDefNamed("SG_TraitEatSpeedUp");
            }
            return TraitDefNamed("SG_TraitEatSpeedUpPlus");
        }

        private static TraitDef RollStomachElasticityDef()
        {
            // 20% rigid, 60% average, 15% flexible, 5% elastic.
            float roll = Rand.Value;
            if (roll < 0.20f)
            {
                return TraitDefNamed("SG_TraitStomachElasticityDown");
            }
            if (roll < 0.80f)
            {
                return TraitDefNamed("RR_TraitStomachElasticityAverage");
            }
            if (roll < 0.95f)
            {
                return TraitDefNamed("SG_TraitStomachElasticityUp");
            }
            return TraitDefNamed("SG_TraitStomachElasticityUpPlus");
        }

        private static TraitDef TraitDefNamed(string defName)
        {
            return DefDatabase<TraitDef>.GetNamedSilentFail(defName);
        }

        public static int EnsureAllExistingPawns()
        {
            if (!FeedOtherMod.Settings.repairExistingTraitCategoriesEnabled)
            {
                return 0;
            }

            int changed = 0;
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive.ToList())
            {
                if (EnsureTraitCategories(pawn))
                {
                    changed++;
                }
            }

            if (changed > 0)
            {
                Log.Message("[RimRound Feed Other beta] Added or repaired eating-style/stomach-elasticity categories for " +
                    changed + " existing pawn(s).");
            }
            return changed;
        }
    }

    /// <summary>
    /// RimRound's original initialiser returns WeightOpinion.None when a pawn
    /// editor has already supplied a weight-opinion trait, then indexes
    /// resistance dictionaries with None. Replace that method with an equivalent
    /// safe initialiser which preserves the newest recognised editor selection,
    /// rolls only when no selection exists, and synchronises the component.
    /// </summary>
    [HarmonyPatch(typeof(RimRound.Utilities.PawnGeneratorUtility),
        "InitializeWeightOpinion")]
    public static class PawnGeneratorUtilityInitializeWeightOpinion_SafeBeta
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn)
        {
            ThingComp_PawnAttitude comp =
                pawn?.TryGetComp<ThingComp_PawnAttitude>();
            if (comp == null || pawn.story?.traits == null)
            {
                return false;
            }

            List<Trait> recognised = pawn.story.traits.allTraits
                .Where(trait => trait?.def != null &&
                    WeightOpinionUtility.traitAndCommonalityPair.ContainsKey(
                        trait.def))
                .ToList();

            TraitDef selected;
            if (recognised.Count > 0)
            {
                // The newest recognised trait is most likely the selection made
                // by a pawn editor. Remove any accidental older duplicates.
                Trait keep = recognised.Last();
                selected = keep.def;
                foreach (Trait trait in recognised)
                {
                    if (trait != keep)
                    {
                        pawn.story.traits.RemoveTrait(trait);
                    }
                }
            }
            else
            {
                selected = WeightOpinionUtility.GetWeightedRandWeightOpinionTrait(
                    WeightOpinionUtility.traitAndCommonalityPair);
                if (selected == null)
                {
                    Log.Error("[RimRound Feed Other beta] Could not select a weight-opinion trait for " +
                        pawn.ToStringSafe() + ".");
                    return false;
                }
                pawn.story.traits.GainTrait(new Trait(selected, 0, true));
            }

            WeightOpinion opinion =
                WeightOpinionUtility.TraitDefToWeightOpinion(selected);
            if (opinion == WeightOpinion.None)
            {
                Log.Error("[RimRound Feed Other beta] Could not map weight-opinion trait " +
                    selected.defName + " for " + pawn.ToStringSafe() + ".");
                return false;
            }

            comp.weightOpinion = opinion;

            float gainBase;
            float lossBase;
            if (WeightOpinionUtility.weightOpinionToGainResistance.TryGetValue(
                    opinion, out gainBase) &&
                WeightOpinionUtility.weightOpinionToLossResistance.TryGetValue(
                    opinion, out lossBase))
            {
                comp.WeightOpinionGainResistance = Mathf.Clamp(
                    gainBase + RimRound.Utilities.Values.RandomFloat(-0.4f, 0.4f),
                    0.3f,
                    1.0f);
                comp.WeightOpinionLossResistance = Mathf.Clamp(
                    lossBase + RimRound.Utilities.Values.RandomFloat(-0.4f, 0.4f),
                    0.3f,
                    1.0f);
            }

            // Setting the float through RimRound's own property keeps the visible
            // trait and component enum synchronised and initializes progression.
            comp.WeightOpinionFloat =
                WeightOpinionUtility.GetRandomWeightFloatForOpinion(opinion);
            return false;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), "GenerateTraits")]
    [HarmonyAfter("RRHarmony")]
    public static class PawnGeneratorGenerateTraits_RimRoundCategoryFinalizer
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn pawn)
        {
            RimRoundTraitGenerationBeta.EnsureTraitCategories(pawn);
        }
    }

    public sealed class GameComponent_RimRoundTraitCategoryMigration : GameComponent
    {
        public GameComponent_RimRoundTraitCategoryMigration(Game game)
        {
        }

        public override void StartedNewGame()
        {
            FeedOtherMod.ApplyRuntimeSettings(false);
            RimRoundTraitGenerationBeta.EnsureAllExistingPawns();
        }

        public override void LoadedGame()
        {
            FeedOtherMod.ApplyRuntimeSettings(false);
            RimRoundTraitGenerationBeta.EnsureAllExistingPawns();
        }
    }
}
