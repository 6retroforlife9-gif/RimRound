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
        public bool storedBecauseDowned;

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
            int practicalProblems = 0;
            FullnessAndDietStats_ThingComp comp =
                pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();

            if (comp?.perkLevels?.PerkToLevels != null)
                comp.perkLevels.PerkToLevels.TryGetValue(
                    "RR_PracticalProblems_Title", out practicalProblems);

            return Mathf.Clamp(0.5f + 0.25f * practicalProblems, 0f, 1f);
        }

        internal static bool CanOperateChair(Pawn pawn, HediffSet diffSet)
        {
            if (pawn == null || diffSet == null || !pawn.RaceProps.Humanlike)
                return false;

            if (!PawnCapacityUtility.BodyCanEverDoCapacity(
                    pawn.RaceProps.body, PawnCapacityDefOf.Manipulation))
                return false;

            if (!pawn.Awake() || !pawn.health.capacities.CanBeAwake)
                return false;

            // Use the normal manipulation calculation. Passing no impactor list
            // avoids contaminating the Moving explanation with manipulation entries.
            float manipulation = PawnCapacityUtility.CalculateCapacityLevel(
                diffSet, PawnCapacityDefOf.Manipulation, null);
            return manipulation >= 0.10f;
        }

        internal static void StoreChairOnDowning(Pawn pawn)
        {
            Apparel chair = WornChair(pawn);
            if (chair == null || pawn.inventory?.innerContainer == null)
                return;

            CompHoverchairRecovery recovery = chair.TryGetComp<CompHoverchairRecovery>();
            if (recovery == null)
                return;

            recovery.storedBecauseDowned = true;
            if (!pawn.apparel.TryMoveToInventory(chair))
            {
                recovery.storedBecauseDowned = false;
                Log.Warning("[RimRound Feed Other] Could not move " + chair.Label +
                    " into " + pawn.LabelShort + "'s inventory after downing.");
            }
        }

        internal static void TryRestoreChair(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed ||
                pawn.apparel == null || pawn.inventory?.innerContainer == null ||
                WornChair(pawn) != null)
                return;

            ThingOwner<Thing> inventory = pawn.inventory.innerContainer;
            Apparel chair = null;
            CompHoverchairRecovery recovery = null;

            for (int i = 0; i < inventory.Count; i++)
            {
                Apparel candidate = inventory[i] as Apparel;
                if (candidate == null || !IsHoverchair(candidate.def))
                    continue;

                CompHoverchairRecovery candidateRecovery =
                    candidate.TryGetComp<CompHoverchairRecovery>();
                if (candidateRecovery?.storedBecauseDowned == true)
                {
                    chair = candidate;
                    recovery = candidateRecovery;
                    break;
                }
            }

            if (chair == null || recovery == null)
                return;

            if (!pawn.Awake() || !pawn.health.capacities.CanBeAwake ||
                !PawnCapacityUtility.BodyCanEverDoCapacity(
                    pawn.RaceProps.body, PawnCapacityDefOf.Manipulation) ||
                PawnCapacityUtility.CalculateCapacityLevel(
                    pawn.health.hediffSet, PawnCapacityDefOf.Manipulation, null) < 0.10f ||
                !ApparelUtility.HasPartsToWear(pawn, chair.def) ||
                !chair.PawnCanWear(pawn, true) ||
                !pawn.apparel.CanWearWithoutDroppingAnything(chair.def))
                return;

            inventory.Remove(chair);
            pawn.apparel.Wear(chair, false, false);

            if (chair.Wearer == pawn)
            {
                recovery.storedBecauseDowned = false;
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
            else
            {
                // Defensive fallback: do not lose the chair if another mod rejects
                // the direct wear operation after our eligibility checks.
                inventory.TryAdd(chair, false);
            }
        }
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
            if (HoverchairFixUtility.WornChair(pawn) == null)
                return true;

            __result = HoverchairFixUtility.CanOperateChair(pawn, diffSet)
                ? HoverchairFixUtility.ChairSpeed(pawn)
                : 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    internal static class HoverchairDownedInventoryFix
    {
        private static void Postfix(Pawn ___pawn)
        {
            HoverchairFixUtility.StoreChairOnDowning(___pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TickRare))]
    internal static class HoverchairAutoReequipFix
    {
        private static void Postfix(Pawn __instance)
        {
            HoverchairFixUtility.TryRestoreChair(__instance);
        }
    }

    internal static class HoverchairLyingRenderFix
    {
        internal static bool Prefix(Apparel gear, ref bool __result)
        {
            if (gear == null || !HoverchairFixUtility.IsHoverchair(gear.def))
                return true;

            Pawn wearer = gear.Wearer;
            if (wearer == null || !wearer.GetPosture().Laying())
                return true;

            __result = false;
            return false;
        }
    }
}
