using RimRound.Comps;
using RimRound.Utilities;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.AI
{
    internal class StatPart_CookSpeed : StatPart
    {
        public override string ExplanationPart(StatRequest req)
        {
            Pawn pawn = req.Thing as Pawn;
            float bonus = EquipmentQuality.CookingBonus(pawn);
            string equipment = bonus > 0f
                ? "RR_KitchenSmockStatExplanation".Translate(EquipmentQuality.Percent(1f + bonus)).ToString() : "";
            if (!ColonyProgression.LegacyPerksActive) return equipment;
            FullnessAndDietStats_ThingComp comp = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            int culinaryConniseurLevel = comp?.perkLevels?.EffectLevel("RR_Culinary_Connisseur_Title") ?? 0;
            return equipment + (equipment.Length > 0 ? "\n" : "") +
                $"Culinary Connoisseur: +{culinaryConniseurLevel * 1}";
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.Thing is Pawn pawn)
            {
                if (pawn.RaceProps.Humanlike)
                {
                    FullnessAndDietStats_ThingComp comp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
                    if (comp is null)
                        return;

                    int culinaryConniseurLevel = comp.perkLevels.EffectLevel("RR_Culinary_Connisseur_Title") ?? 0;

                    val += culinaryConniseurLevel * 1;
                }
            }
        }
    }

    // CookSpeed's native gear offsets are applied before its skill curve. A
    // final factor gives the smock the same quality-dependent percentage at every skill level.
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.FinalizeValue))]
    internal static class StatWorker_FinalizeValue_KitchenSmock
    {
        public static void Postfix(StatRequest req, ref float val, bool applyPostProcess, StatDef ___stat)
        {
            if (!applyPostProcess || ___stat?.defName != "CookSpeed" || !(req.Thing is Pawn pawn)) return;
            val *= 1f + EquipmentQuality.CookingBonus(pawn);
        }
    }
}
