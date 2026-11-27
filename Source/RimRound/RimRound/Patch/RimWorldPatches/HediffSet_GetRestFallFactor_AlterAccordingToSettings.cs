using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimRound.Patch
{
	[HarmonyPatch(typeof(HediffSet))]
	[HarmonyPatch(nameof(HediffSet.RestFallFactor), MethodType.Getter)]
	internal class HediffSet_GetRestFallFactor_AlterAccordingToSettings
    {
		[HarmonyPriority(Priority.First)]
        public static void Postfix(ref float __result, HediffSet __instance)
        {
            Hediff weight = __instance.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
            if (weight?.CurStage == null) return;

            float configured = GlobalSettings.weightHediffRestRateMult.threshold;
            float activity = ActivityMultiplier(__instance.pawn);
            if (configured == 1f && (activity == 1f || weight.CurStage.restFallFactor == 1f)) return;

            // Preserve the native product of factors, then additive offsets and
            // final zero clamp. Scale only weight's extra cost, not ordinary rest
            // need, other hediffs, genes or the separate RestFallRateFactor stat.
            // Comparing native totals also handles a negative offset that had
            // already clamped the original getter to zero.
            float originalProduct = 1f, adjustedProduct = 1f, offset = 0f;
            foreach (Hediff hediff in __instance.hediffs)
            {
                HediffStage stage = hediff.CurStage;
                if (stage == null) continue;
                float factor = stage.restFallFactor;
                originalProduct *= factor;
                if (hediff.def == Defs.HediffDefOf.RimRound_Weight)
                {
                    float change = (factor - 1f) * configured;
                    factor = 1f + (change > 0f ? change * activity : change);
                }
                adjustedProduct *= factor;
                offset += stage.restFallFactorOffset;
            }
            float original = Mathf.Max(0f, originalProduct + offset);
            float adjusted = Mathf.Max(0f, adjustedProduct + offset);
            __result = Mathf.Max(0f, __result + adjusted - original);
        }

        private static float ActivityMultiplier(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.InBed() ||
                pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
                return 0f;

            bool moving;
            if (pawn.Spawned)
            {
                // MovingNow can remain true while waiting for a door or busy
                // stance. Native pathing updates LastMovedTick each tick it pays
                // movement cost, including slow movement within a cell.
                moving = pawn.pather?.MovingNow == true &&
                    Find.TickManager.TicksGame - pawn.pather.LastMovedTick <= 1;
            }
            else
            {
                // A direct caravan member walks; a vehicle seat, carrier or
                // transport container is a different holder and does not.
                Caravan caravan = pawn.ParentHolder as Caravan;
                moving = caravan?.pather?.MovingNow == true && !caravan.NightResting &&
                    !pawn.CarriedByCaravan();
            }
            if (!moving) return 0f;
            return MobilityChairUtility.IsWearingAMobilityScooter(pawn) ? .25f : 1f;
        }

	}
}
