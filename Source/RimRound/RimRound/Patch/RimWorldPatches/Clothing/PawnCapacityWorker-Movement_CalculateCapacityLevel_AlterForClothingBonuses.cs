using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(PawnCapacityWorker_Moving))]
    [HarmonyPatch(nameof(PawnCapacityWorker_Moving.CalculateCapacityLevel))]
    public class PawnCapacityWorker_Movement_CalculateCapacityLevel_AlterForClothingBonuses
    {
        public static bool Prefix(HediffSet diffSet, ref float __result)
        {
            if (!MedicalCapacityPolicy.IsPropelledEvaluation(diffSet.pawn)) return true;
            __result = MedicalCapacityPolicy.PropelledMovementBase(diffSet.pawn);
            return false;
        }

        public static void Postfix(HediffSet diffSet, ref float __result)
        {
            if (MedicalCapacityPolicy.IsEvaluating(diffSet.pawn)) return;
            if (__result <= 0)
                return;

            Pawn pawn = diffSet.pawn;
            if (!pawn.RaceProps.Humanlike)
                return;

            var comp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp is null)
                return;

            __result += comp.EffectiveStatBonuses.movementFlatBonus;
            return;
        }
    }
}
