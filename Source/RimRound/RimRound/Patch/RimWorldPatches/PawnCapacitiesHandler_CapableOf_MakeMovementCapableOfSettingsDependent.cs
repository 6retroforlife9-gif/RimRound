using HarmonyLib;
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
    [HarmonyPatch(typeof(PawnCapacitiesHandler))]
    [HarmonyPatch(nameof(PawnCapacitiesHandler.CapableOf))]
    public class PawnCapacitiesHandler_CapableOf_MakeMovementCapableOfSettingsDependent
    {
        public static bool Prefix(PawnCapacitiesHandler __instance, Pawn ___pawn, ref bool __result, PawnCapacityDef __0)
        {
            if (__0.defName != PawnCapacityDefOf.Moving.defName)
                return true;

            // The setting controls RimRound body mobility; it cannot authorize a
            // native medical incapacity, or turn exact zero into movement.
            float level = __instance.GetLevel(__0);
            __result = MedicalCapacityPolicy.IsEvaluating(___pawn)
                ? level > __0.minForCapable
                : level > 0f && level >= GlobalSettings.minForCapableMovement.threshold &&
                    MedicalCapacityPolicy.MedicallyCapableOfMovement(___pawn, level);

            return false;
        }
    }
}
