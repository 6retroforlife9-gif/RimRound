using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Comps.Buildings;
using RimRound.Defs;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    // Gameplay and information cards share these calculations. Quality bonuses
    // stay live: they are never written into the pawn's saved source ledger.
    public static class EquipmentQuality
    {
        public static string Percent(float value) => (value * 100f).ToString("0.#") + "%";
        public static QualityCategory Quality(Thing item)
            => item != null && item.TryGetQuality(out QualityCategory q) ? q : QualityCategory.Normal;
        public static float BonusFactor(Thing item)
        {
            switch (Quality(item))
            {
                case QualityCategory.Awful: return .7f;
                case QualityCategory.Poor: return .9f;
                case QualityCategory.Good: return 1.1f;
                case QualityCategory.Excellent: return 1.3f;
                case QualityCategory.Masterwork: return 1.5f;
                case QualityCategory.Legendary: return 2f;
                default: return 1f;
            }
        }
        public static float BedBonus(float normalBonus, Thing bed)
            => float.IsNaN(normalBonus) || float.IsInfinity(normalBonus) ? 0f
                : Mathf.Clamp(normalBonus * BonusFactor(bed), 0f, .5f);
        public static float RestingBonus(Pawn pawn)
        {
            if (!BodyModelEligibility.IsEligible(pawn) || !pawn.Spawned || pawn.Dead ||
                pawn.GetPosture() != PawnPosture.LayingInBed) return 0f;
            return pawn.CurrentBed()?.TryGetComp<Comp_RestingWeightGainMod>()?.GetWeightGainAdditiveModifier() ?? 0f;
        }
        public static bool IsNovaTech(ThingDef def)
        {
            switch (def?.defName)
            {
                case "RimRound_NovaTechApparel_Jumpsuit":
                case "RimRound_NovaTechApparel_Belt":
                case "RimRound_NovaTechApparel_ProtoSuit":
                case "RimRound_NovaTechApparel_Gloves":
                case "RimRound_NovaTechApparel_Collar":
                case "RimRound_NovaTechApparel_Hyperbelt": return true;
                default: return false;
            }
        }
        public static RimRoundStatBonuses ApparelBase(ThingDef def)
        {
            var x = def?.GetModExtension<ClothingModExtension>();
            if (x == null) return new RimRoundStatBonuses();
            return new RimRoundStatBonuses {
                weightGainMultBonus = x.weightGainMultiplierMultBonus,
                digestionRateMultiplier = x.digestionSpeedMultBonus,
                stomachElasticityMultiplier = x.stomachElasticityMultBonus,
                softLimitMultiplier = x.softLimitMultiplier,
                fullnessGainedMultBonus = x.fullnessGainedMultiplierBonus,
                movementFlatBonus = x.flatMoveBonus,
                manipulationFlatBonus = x.flatManipulationBonus,
                eatingFlatBonus = x.flatEatingSpeedBonus,
                movementPenaltyMitigationMultBonus_Weight = x.movementPenaltyMitigationMultBonus_Weight,
                movementPenaltyMitigationMultBonus_Fullness = x.movementPenaltyMitigationMultBonus_Fullness,
                manipulationPenaltyMitigationMultBonus_Weight = x.manipulationPenaltyMitigationMultBonus_Weight,
                painMitigationMultBonus_Fullness = x.painMitigationMultBonus_Fullness,
                eatingSpeedReductionMitigationMultBonus_Fullness = x.eatingSpeedReductionMitigationMultBonus_Fullness
            };
        }
        public static RimRoundStatBonuses ApparelBonus(Apparel item)
            => ProgressionSources.Multiply(ApparelBase(item.def), IsNovaTech(item.def) ? BonusFactor(item) : 1f);
        static RimRoundStatBonuses LegacyBase(Apparel item)
            => item.TryGetComp<CompProgressionApparel>()?.appliedProfileVersion == 0
                ? ProgressionSources.HistoricalApparelBonus(item.def.defName) : ApparelBase(item.def);
        public static RimRoundStatBonuses LegacyQualityDelta(Pawn pawn)
        {
            var result = new RimRoundStatBonuses();
            if (!BodyModelEligibility.IsEligible(pawn) || pawn.apparel == null) return result;
            foreach (Apparel item in pawn.apparel.WornApparel)
                if (IsNovaTech(item.def))
                    result = ProgressionSources.Add(result,
                        ProgressionSources.Multiply(LegacyBase(item), BonusFactor(item) - 1f));
            return result;
        }
        public static float SmockBonus(Thing item) => .15f * BonusFactor(item);
        public static float CookingBonus(Pawn pawn)
        {
            if (pawn?.apparel == null) return 0f;
            // Other mods may permit duplicate skin-layer apparel. Use one smock.
            return pawn.apparel.WornApparel.Where(a => a.def.defName == "RR_KitchenWorkSmock")
                .Select(a => SmockBonus(a)).DefaultIfEmpty(0f).Max();
        }
        public static bool IsChair(ThingDef def) => def?.defName == "RR_HoverChair" || def?.defName == "RR_HoverChairArm";
        public static float ChairFactor(Thing item)
        {
            switch (Quality(item))
            {
                case QualityCategory.Awful: return .55f;
                case QualityCategory.Poor: return .60f;
                case QualityCategory.Good: return .68f;
                case QualityCategory.Excellent: return .72f;
                case QualityCategory.Masterwork: return .76f;
                case QualityCategory.Legendary: return .80f;
                default: return .65f;
            }
        }
        public static float ChairCapacity(Pawn pawn, Thing chair)
        {
            if (!ColonyProgression.LegacyPerksActive) return ChairFactor(chair);
            int level = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.perkLevels?.EffectLevel("RR_PracticalProblems_Title") ?? 0;
            return Mathf.Clamp(.5f + .25f * level + ChairFactor(chair) - .65f, 0f, 1f);
        }
        static string Report(Thing item, float normal, float value, string condition)
            => "RR_EquipmentQualityReport".Translate(Percent(normal), Quality(item).GetLabel(),
                Percent(BonusFactor(item)), Percent(value), condition);
        static StatDrawEntry Entry(string key, float normal, float value, Thing item, string condition, int order)
            => new StatDrawEntry(StatCategoryDefOf.BasicsImportant, key.Translate(),
                "+" + Percent(value), Report(item, normal, value, condition), order);
        public static IEnumerable<StatDrawEntry> Info(ThingDef def, Thing item)
        {
            var bed = def.GetCompProperties<CompProperties_RestingWeightGainMod>();
            if (bed != null)
                yield return Entry("RR_RestingGainBonus", bed.additiveRestingWeightGainModifier,
                    BedBonus(bed.additiveRestingWeightGainModifier, item), item, "RR_RestingGainCondition".Translate(), 4200);
            if (def.defName == "RR_KitchenWorkSmock")
                yield return Entry("RR_SmockCookingBonus", .15f, SmockBonus(item), item,
                    "RR_SmockCookingCondition".Translate(), 4200);
            if (IsChair(def))
            {
                Pawn wearer = (item as Apparel)?.Wearer;
                bool legacyChair = wearer != null && ColonyProgression.LegacyPerksActive;
                yield return new StatDrawEntry(StatCategoryDefOf.BasicsImportant,
                    (legacyChair ? "RR_ChairLegacyCapacity" : "RR_ChairPropulsion").Translate(),
                    (legacyChair ? ChairCapacity(wearer, item) : ChairFactor(item)).ToStringPercent(),
                    "RR_ChairQualityReport".Translate(Quality(item).GetLabel(), ChairFactor(item).ToStringPercent()), 4200);
            }
            if (!IsNovaTech(def)) yield break;
            Apparel worn = item as Apparel;
            bool legacy = worn?.Wearer != null && ColonyProgression.LegacyPerksActive;
            RimRoundStatBonuses b = legacy ? LegacyBase(worn) : ApparelBase(def);
            string condition = "RR_NovaQualityCondition".Translate();
            if (legacy) condition += "\n\n" + "RR_LegacyEquipmentCondition".Translate();
            var names = ProgressionSources.FieldNames();
            var values = ProgressionSources.ToList(b, names, null);
            for (int i = 0; i < names.Count; i++)
                if (values[i] != 0f)
                    yield return Entry("RR_Equipment_" + names[i], values[i], values[i] * BonusFactor(item), item,
                        condition, 4199 - i);
        }
    }
    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    static class EquipmentQualityInfo
    {
        static void Postfix(ThingDef __instance, StatRequest req, ref IEnumerable<StatDrawEntry> __result)
        {
            if (__instance.GetCompProperties<CompProperties_RestingWeightGainMod>() != null ||
                EquipmentQuality.IsNovaTech(__instance) || EquipmentQuality.IsChair(__instance) ||
                __instance.defName == "RR_KitchenWorkSmock")
                __result = __result.Concat(EquipmentQuality.Info(__instance, req.Thing));
        }
    }
    [HarmonyPatch(typeof(CompQuality), nameof(CompQuality.SetQuality))]
    static class EquipmentQualityChanged
    {
        static void Postfix(CompQuality __instance)
        {
            if (__instance.parent is Apparel item && item.Wearer != null &&
                (EquipmentQuality.IsNovaTech(item.def) || EquipmentQuality.IsChair(item.def) || item.def.defName == "RR_KitchenWorkSmock"))
                item.Wearer.health?.capacities?.Notify_CapacityLevelsDirty();
        }
    }
}
