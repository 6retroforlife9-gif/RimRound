using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimRound.Comps;
using RimRound.Defs;
using RimWorld;
using Verse;

namespace RimRound.Utilities
{
    public static class ProgressionSources
    {
        private static readonly FieldInfo[] SavedFields = typeof(RimRoundStatBonuses)
            .GetFields().OrderBy(f => f.Name).ToArray();
        public static RimRoundStatBonuses Add(RimRoundStatBonuses a, RimRoundStatBonuses b)
        {
            a.weightGainMultiplier += b.weightGainMultiplier;
            a.weightLossMultiplier += b.weightLossMultiplier;
            a.digestionRateMultiplier += b.digestionRateMultiplier;
            a.softLimitMultiplier += b.softLimitMultiplier;
            a.stomachElasticityMultiplier += b.stomachElasticityMultiplier;
            a.hardLimitAdditionalPercentageMultiplier += b.hardLimitAdditionalPercentageMultiplier;
            a.weightGainMultBonus += b.weightGainMultBonus;
            a.weightLossMultBonus += b.weightLossMultBonus;
            a.digestionRateFlatBonus += b.digestionRateFlatBonus;
            a.softLimitFlatBonus += b.softLimitFlatBonus;
            a.stomachElasticityFlatBonus += b.stomachElasticityFlatBonus;
            a.hardLimitAdditionalPercentageMultBonus += b.hardLimitAdditionalPercentageMultBonus;
            a.fullnessGainedMultBonus += b.fullnessGainedMultBonus;
            a.movementFlatBonus += b.movementFlatBonus;
            a.manipulationFlatBonus += b.manipulationFlatBonus;
            a.eatingFlatBonus += b.eatingFlatBonus;
            a.movementPenaltyMitigationMultBonus_Weight += b.movementPenaltyMitigationMultBonus_Weight;
            a.movementPenaltyMitigationMultBonus_Fullness += b.movementPenaltyMitigationMultBonus_Fullness;
            a.eatingSpeedReductionMitigationMultBonus_Fullness += b.eatingSpeedReductionMitigationMultBonus_Fullness;
            a.manipulationPenaltyMitigationMultBonus_Weight += b.manipulationPenaltyMitigationMultBonus_Weight;
            a.painMitigationMultBonus_Fullness += b.painMitigationMultBonus_Fullness;
            return a;
        }

        public static RimRoundStatBonuses Negate(RimRoundStatBonuses b)
        {
            return Multiply(b, -1f);
        }

        public static RimRoundStatBonuses Multiply(RimRoundStatBonuses b, float factor)
        {
            b.weightGainMultiplier *= factor;
            b.weightLossMultiplier *= factor;
            b.digestionRateMultiplier *= factor;
            b.softLimitMultiplier *= factor;
            b.stomachElasticityMultiplier *= factor;
            b.hardLimitAdditionalPercentageMultiplier *= factor;
            b.weightGainMultBonus *= factor;
            b.weightLossMultBonus *= factor;
            b.digestionRateFlatBonus *= factor;
            b.softLimitFlatBonus *= factor;
            b.stomachElasticityFlatBonus *= factor;
            b.hardLimitAdditionalPercentageMultBonus *= factor;
            b.fullnessGainedMultBonus *= factor;
            b.movementFlatBonus *= factor;
            b.manipulationFlatBonus *= factor;
            b.eatingFlatBonus *= factor;
            b.movementPenaltyMitigationMultBonus_Weight *= factor;
            b.movementPenaltyMitigationMultBonus_Fullness *= factor;
            b.eatingSpeedReductionMitigationMultBonus_Fullness *= factor;
            b.manipulationPenaltyMitigationMultBonus_Weight *= factor;
            b.painMitigationMultBonus_Fullness *= factor;
            return b;
        }

        public static List<string> FieldNames()
        {
            return SavedFields.Select(f => f.Name).ToList();
        }

        public static List<float> ToList(RimRoundStatBonuses value, List<string> names, List<float> oldValues)
        {
            var result = new List<float>();
            if (names == null) return result;
            for (int i = 0; i < names.Count; i++)
            {
                FieldInfo field = SavedFields.FirstOrDefault(f => f.Name == names[i]);
                result.Add(field != null ? (float)field.GetValue(value) :
                    oldValues != null && i < oldValues.Count ? oldValues[i] : 0f);
            }
            return result;
        }

        public static RimRoundStatBonuses FromList(List<string> names, List<float> values)
        {
            object boxed = new RimRoundStatBonuses();
            if (names == null || values == null) return (RimRoundStatBonuses)boxed;
            for (int i = 0; i < Math.Min(names.Count, values.Count); i++)
            {
                FieldInfo field = SavedFields.FirstOrDefault(f => f.Name == names[i]);
                if (field != null) field.SetValue(boxed, values[i]);
            }
            return (RimRoundStatBonuses)boxed;
        }

        public static RimRoundStatBonuses Live(Pawn pawn)
        {
            var result = new RimRoundStatBonuses();
            if (pawn == null || !BodyModelEligibility.IsEligible(pawn)) return result;

            if (pawn.apparel?.WornApparel != null)
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    var x = apparel.def.GetModExtension<ClothingModExtension>();
                    if (x == null) continue;
                    result = Add(result, EquipmentQuality.ApparelBonus(apparel));
                }

            bool hasSwellGlowImplant = false;
            if (pawn.health?.hediffSet?.hediffs != null)
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    if (!IsActiveImplant(hediff)) continue;
                    if (hediff.def.defName == "SG_ImplantHediff") hasSwellGlowImplant = true;
                    else result = Add(result, HediffBonus(hediff.def.defName));
                }
            if (hasSwellGlowImplant)
                result = Add(result, new RimRoundStatBonuses { weightGainMultBonus = .10f });

            if (pawn.genes?.GenesListForReading != null)
            {
                foreach (var gene in pawn.genes.GenesListForReading)
                {
                    if (gene.Active) result = Add(result, BiologyBonus(gene.def.defName));
                }
            }
            if (pawn.story?.traits?.allTraits != null)
                foreach (var trait in pawn.story.traits.allTraits)
                    if (!trait.Suppressed && trait.sourceGene == null)
                        result = Add(result, BiologyBonus(trait.def.defName));
            var body = pawn.TryGetComp<RimRound.Comps.FullnessAndDietStats_ThingComp>();
            if (body != null) result = Add(Add(result, body.AdaptationBonuses), body.BodyPerkBonuses);
            return result;
        }

        public static RimRoundStatBonuses HistoricalApparelBonus(string name)
        {
            switch (name)
            {
                case "RimRound_NovaTechApparel_Jumpsuit": return new RimRoundStatBonuses {
                    weightGainMultBonus = .2f, digestionRateMultiplier = .2f, movementFlatBonus = .15f,
                    movementPenaltyMitigationMultBonus_Weight = .05f };
                case "RimRound_NovaTechApparel_Belt": return new RimRoundStatBonuses {
                    digestionRateMultiplier = .5f, stomachElasticityMultiplier = 1f,
                    movementPenaltyMitigationMultBonus_Fullness = .1f, painMitigationMultBonus_Fullness = .1f,
                    eatingSpeedReductionMitigationMultBonus_Fullness = .1f };
                case "RimRound_NovaTechApparel_ProtoSuit": return new RimRoundStatBonuses {
                    weightGainMultBonus = 1f, digestionRateMultiplier = 2f, movementFlatBonus = .3f,
                    manipulationFlatBonus = .15f, movementPenaltyMitigationMultBonus_Weight = .15f,
                    manipulationPenaltyMitigationMultBonus_Weight = .15f };
                case "RimRound_NovaTechApparel_Gloves": return new RimRoundStatBonuses {
                    movementFlatBonus = .15f, manipulationFlatBonus = .15f, eatingFlatBonus = .25f,
                    manipulationPenaltyMitigationMultBonus_Weight = .1f };
                case "RimRound_NovaTechApparel_Collar": return new RimRoundStatBonuses {
                    digestionRateMultiplier = .5f, fullnessGainedMultBonus = .2f, eatingFlatBonus = .25f,
                    manipulationPenaltyMitigationMultBonus_Weight = .05f,
                    eatingSpeedReductionMitigationMultBonus_Fullness = .1f };
                case "RimRound_NovaTechApparel_Hyperbelt": return new RimRoundStatBonuses {
                    weightGainMultBonus = .5f, digestionRateMultiplier = 1f, stomachElasticityMultiplier = 1f,
                    movementPenaltyMitigationMultBonus_Weight = .05f,
                    manipulationPenaltyMitigationMultBonus_Weight = .05f,
                    movementPenaltyMitigationMultBonus_Fullness = .2f,
                    painMitigationMultBonus_Fullness = .2f,
                    eatingSpeedReductionMitigationMultBonus_Fullness = .2f };
                default: return new RimRoundStatBonuses();
            }
        }

        public static bool IsActiveImplant(Hediff hediff)
        {
            if (hediff == null) return false;
            if (ColonyProgression.LegacyPerksActive) return true;
            switch (hediff.def.defName)
            {
                case "RR_BionicMetabolizerHediff": case "RR_HungerImplantHediff":
                case "RR_ArchotechMetabolizerHediff": case "RR_AIEncouragementChipHediff": case "SG_ImplantHediff":
                    var list = hediff.pawn?.health?.hediffSet?.hediffs;
                    if (list != null)
                        foreach (Hediff candidate in list)
                            if (candidate.def == hediff.def) return ReferenceEquals(candidate, hediff);
                    return false;
                default: return true;
            }
        }

        private static RimRoundStatBonuses HediffBonus(string name)
        {
            switch (name)
            {
                case "RR_BionicMetabolizerHediff": return new RimRoundStatBonuses { digestionRateMultiplier = .35f, softLimitMultiplier = -.10f };
                case "RR_HungerImplantHediff": return new RimRoundStatBonuses { softLimitMultiplier = .20f };
                case "RR_ArchotechMetabolizerHediff": return ColonyProgression.FantasyActive
                    ? new RimRoundStatBonuses { digestionRateMultiplier = .75f, softLimitMultiplier = 1.00f }
                    : new RimRoundStatBonuses { digestionRateMultiplier = .60f, softLimitMultiplier = .40f };
                case "RR_AIEncouragementChipHediff": return new RimRoundStatBonuses { digestionRateMultiplier = .25f };
                default: return new RimRoundStatBonuses();
            }
        }

        private static RimRoundStatBonuses BiologyBonus(string name)
        {
            // A forced trait is ignored while its gene is present; the gene's
            // Active flag is the sole authority during suppression.
            switch (name)
            {
                case "SG_GeneDigestDown": case "SG_TraitDigestDown": return new RimRoundStatBonuses { digestionRateMultiplier = -.25f };
                case "SG_GeneDigestUp": case "SG_TraitDigestUp": return new RimRoundStatBonuses { digestionRateMultiplier = .25f };
                case "SG_GeneDigestUpPlus": case "SG_TraitDigestUpPlus": return new RimRoundStatBonuses { digestionRateMultiplier = .50f };
                case "SG_GeneWeightDown": case "SG_TraitWeightGainDown": return new RimRoundStatBonuses { weightGainMultBonus = -.15f };
                case "SG_GeneWeightDownPlus": case "SG_TraitWeightGainDownPlus": return new RimRoundStatBonuses { weightGainMultBonus = -.30f };
                case "SG_GeneWeightUp": case "SG_TraitWeightGainUp": return new RimRoundStatBonuses { weightGainMultBonus = .15f };
                case "SG_GeneWeightUpPlus": case "SG_TraitWeightGainUpPlus": return new RimRoundStatBonuses { weightGainMultBonus = .30f };
                case "SG_GeneElasticDown": case "SG_TraitStomachElasticityDown": return new RimRoundStatBonuses { stomachElasticityMultiplier = -.15f, softLimitMultiplier = -.15f };
                case "SG_GeneElasticUp": case "SG_TraitStomachElasticityUp": return new RimRoundStatBonuses { stomachElasticityMultiplier = .15f, softLimitMultiplier = .15f };
                case "SG_GeneElasticUpPlus": case "SG_TraitStomachElasticityUpPlus": return new RimRoundStatBonuses { stomachElasticityMultiplier = .30f, softLimitMultiplier = .30f };
                case "SG_GeneEatSpeedDown": case "SG_TraitEatSpeedDown": return new RimRoundStatBonuses { eatingFlatBonus = -.15f };
                case "SG_GeneEatSpeedDownPlus": case "SG_TraitEatSpeedDownPlus": return new RimRoundStatBonuses { eatingFlatBonus = -.30f };
                case "SG_GeneEatSpeedUp": case "SG_TraitEatSpeedUp": return new RimRoundStatBonuses { eatingFlatBonus = .15f };
                case "SG_GeneEatSpeedUpPlus": case "SG_TraitEatSpeedUpPlus": return new RimRoundStatBonuses { eatingFlatBonus = .30f };
                default: return new RimRoundStatBonuses();
            }
        }
    }

    public class Recipe_InstallMetabolicImplant : Recipe_InstallImplant
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            if (!BodyModelEligibility.HasAdultBiology(pawn))
                return new List<BodyPartRecord>();
            if (!ColonyProgression.LegacyPerksActive && recipe.addsHediff != null &&
                pawn.health.hediffSet.HasHediff(recipe.addsHediff))
                return new List<BodyPartRecord>();
            return base.GetPartsToApplyOn(pawn, recipe);
        }
    }
}
