using System;
using System.Collections.Generic;
using HarmonyLib;
using RimRound.Comps;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.FeedOther
{
    public sealed class CompProperties_HoverchairRecovery : CompProperties
    {
        public CompProperties_HoverchairRecovery()
        {
            compClass = typeof(CompHoverchairRecovery);
        }
    }

    public sealed class CompHoverchairRecovery : ThingComp
    {
        // Retained for old-save compatibility; no automatic container transfers use it.
        public bool storedBecauseDowned;

        public override IEnumerable<Gizmo> CompGetGizmosExtra() => MobilityAidController.Gizmos(parent as Apparel);
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra() => MobilityAidController.Gizmos(parent as Apparel);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref storedBecauseDowned, "storedBecauseDowned", false);
        }
    }

    internal static class HoverchairFixUtility
    {
        internal static bool IsHoverchair(ThingDef def)
        {
            return def != null &&
                (def.defName == "RR_HoverChair" || def.defName == "RR_HoverChairArm");
        }

        internal static Apparel WornChair(Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null)
                return null;

            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (IsHoverchair(worn[i]?.def))
                    return worn[i];
            }

            return null;
        }

        internal static float ChairSpeed(Pawn pawn)
        {
            return RimRound.Utilities.EquipmentQuality.ChairCapacity(pawn, WornChair(pawn));
        }

        internal static bool CanOperateChair(Pawn pawn, HediffSet diffSet)
            => MobilityAidController.CanPotentiallyControl(pawn, diffSet);
    }

    [HarmonyPatch(typeof(PawnCapacityUtility),
        nameof(PawnCapacityUtility.CalculateCapacityLevel))]
    internal static class HoverchairCapacityFix
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref float __result, HediffSet diffSet,
            PawnCapacityDef capacity)
        {
            if (capacity != PawnCapacityDefOf.Moving)
                return true;

            Pawn pawn = diffSet?.pawn;
            if (RimRound.Utilities.MedicalCapacityPolicy.IsEvaluating(pawn)) return true;
            Apparel chair = HoverchairFixUtility.WornChair(pawn);
            if (chair == null)
                return true;

            string reason;
            __result = MobilityAidController.PhysicallyCompatible(pawn, chair, out reason) &&
                HoverchairFixUtility.CanOperateChair(pawn, diffSet)
                ? HoverchairFixUtility.ChairSpeed(pawn)
                : 0f;
            return false;
        }
    }

    // Native downing retains the same worn item. There is no automatic refit.

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    internal static class HoverchairPropulsionSpeed
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(StatDef ___stat, StatRequest req, ref float __result)
        {
            Pawn pawn;
            if (!Applies(___stat, req, out pawn)) return;
            // This is aid propulsion, not the wearer's walking-bonus stack.
            // FinalizeValue and native TicksPerMove/path costs still own glow,
            // scenario/weather, restraint/carry, terrain, doors and urgency.
            __result = Nominal(pawn) * HoverchairFixUtility.ChairSpeed(pawn);
        }

        internal static bool Applies(StatDef stat, StatRequest req, out Pawn pawn)
        {
            pawn = req.Thing as Pawn;
            if (stat != StatDefOf.MoveSpeed || RimRound.Utilities.ColonyProgression.LegacyPerksActive) return false;
            Apparel chair = HoverchairFixUtility.WornChair(pawn);
            string reason;
            return chair != null && MobilityAidController.PhysicallyCompatible(pawn, chair, out reason) &&
                HoverchairFixUtility.CanOperateChair(pawn, pawn.health.hediffSet);
        }

        internal static float Nominal(Pawn pawn)
        {
            float nominal = StatDefOf.MoveSpeed.Worker.GetBaseValueFor(StatRequest.For(pawn.def, null));
            nominal += pawn.ageTracker.CurLifeStage.statOffsets.GetStatOffsetFromList(StatDefOf.MoveSpeed);
            nominal *= pawn.ageTracker.CurLifeStage.statFactors.GetStatFactorFromList(StatDefOf.MoveSpeed);
            return nominal;
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetExplanationUnfinalized))]
    internal static class HoverchairPropulsionExplanation
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(StatDef ___stat, StatRequest req, ref string __result)
        {
            Pawn pawn;
            if (!HoverchairPropulsionSpeed.Applies(___stat, req, out pawn)) return;
            float nominal = HoverchairPropulsionSpeed.Nominal(pawn);
            __result = "Hoverchair propulsion\n\nNative race and life-stage nominal speed: " +
                nominal.ToString("0.##") + " cells/s\nPropulsion factor: " + HoverchairFixUtility.ChairSpeed(pawn).ToStringPercent() + "\nPropulsion speed: " +
                (nominal * HoverchairFixUtility.ChairSpeed(pawn)).ToString("0.##") + " cells/s\n\n" +
                "Walking bonuses from traits, genes, drugs and equipment do not increase propulsion. " +
                "Native final stat modifiers and movement rules still apply, including lighting, weather, " +
                "terrain, doors, carrying, restraints, allowed areas and job urgency. " +
                "Native health determines whether the wearer can move.";
        }
    }

}
