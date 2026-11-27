using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    /// <summary>
    /// RimRound's NotRegalBed is physically 3x3, but it is intentionally a
    /// single-pawn bed. Vanilla derives both sleeping-slot count and slot
    /// positions from the footprint width, so the original mod added three
    /// partial patches. Those patches moved every orientation east and left
    /// the 3-deep interaction-cell search unsupported. Keep the large physical
    /// footprint while presenting one complete vanilla-compatible bed slot.
    /// </summary>
    public static class NotRegalBedUtility
    {
        public const string DefName = "NotRegalBed";

        public static bool IsNotRegalBed(ThingDef def)
        {
            return def != null && def.defName == DefName;
        }

        public static bool IsNotRegalBed(Building_Bed bed)
        {
            return bed != null && IsNotRegalBed(bed.def);
        }

        public static IntVec3 GetSlotPosition(
            IntVec3 bedPosition,
            Rot4 rotation,
            IntVec2 size,
            bool head)
        {
            CellRect rect = GenAdj.OccupiedRect(
                bedPosition,
                rotation,
                size);
            int centerX = (rect.minX + rect.maxX) / 2;
            int centerZ = (rect.minZ + rect.maxZ) / 2;

            switch (rotation.AsInt)
            {
                case 2: // South
                    return new IntVec3(
                        centerX,
                        bedPosition.y,
                        head ? rect.maxZ : rect.minZ);
                case 0: // North
                    return new IntVec3(
                        centerX,
                        bedPosition.y,
                        head ? rect.minZ : rect.maxZ);
                case 3: // West
                    return new IntVec3(
                        head ? rect.maxX : rect.minX,
                        bedPosition.y,
                        centerZ);
                case 1: // East
                    return new IntVec3(
                        head ? rect.minX : rect.maxX,
                        bedPosition.y,
                        centerZ);
                default:
                    return bedPosition;
            }
        }

        public static IntVec3 GetSleepingSlotPosition(Building_Bed bed)
        {
            return GetSlotPosition(
                bed.Position,
                bed.Rotation,
                bed.def.size,
                true);
        }

        public static IntVec3 GetFootSlotPosition(Building_Bed bed)
        {
            return GetSlotPosition(
                bed.Position,
                bed.Rotation,
                bed.def.size,
                false);
        }

        /// <summary>
        /// A save made with the old rotation patch can contain a lying pawn on
        /// the wrong tile, including one tile outside a north/west-facing bed.
        /// Re-centre only a pawn whose active job actually targets this bed.
        /// </summary>
        public static void RepairLoadedBeds()
        {
            ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail(DefName);
            if (bedDef == null || Find.Maps == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                if (map == null || map.listerThings == null ||
                    map.mapPawns == null)
                {
                    continue;
                }

                // Copy because repositioning a pawn updates map listers.
                List<Thing> beds = map.listerThings
                    .ThingsOfDef(bedDef)
                    .ToList();
                foreach (Thing thing in beds)
                {
                    RepairLoadedOccupant(thing as Building_Bed, map);
                }
            }
        }

        private static void RepairLoadedOccupant(Building_Bed bed, Map map)
        {
            if (!IsNotRegalBed(bed) || !bed.Spawned || bed.Map != map)
            {
                return;
            }

            IntVec3 target = GetSleepingSlotPosition(bed);
            if (!target.InBounds(map))
            {
                return;
            }

            Pawn targetOccupant = map.thingGrid
                .ThingsListAt(target)
                .OfType<Pawn>()
                .FirstOrDefault();
            if (targetOccupant != null)
            {
                return;
            }

            Pawn candidate = map.mapPawns.AllPawnsSpawned
                .Where(delegate(Pawn pawn)
                {
                    return pawn != null && pawn.Map == map &&
                        pawn.CurJob != null &&
                        pawn.GetPosture().InBed() &&
                        JobTargetsBed(pawn.CurJob, bed);
                })
                .OrderByDescending(delegate(Pawn pawn)
                {
                    return bed.IsOwner(pawn);
                })
                .ThenBy(delegate(Pawn pawn)
                {
                    return pawn.Position.DistanceToSquared(target);
                })
                .FirstOrDefault();

            if (candidate != null && candidate.Position != target)
            {
                candidate.Position = target;
            }
        }

        private static bool JobTargetsBed(Job job, Building_Bed bed)
        {
            return TargetIsBed(job, TargetIndex.A, bed) ||
                TargetIsBed(job, TargetIndex.B, bed) ||
                TargetIsBed(job, TargetIndex.C, bed);
        }

        private static bool TargetIsBed(
            Job job,
            TargetIndex index,
            Building_Bed bed)
        {
            LocalTargetInfo target = job.GetTarget(index);
            return target.IsValid && target.HasThing && target.Thing == bed;
        }
    }

    /// <summary>
    /// Uses the real vanilla bed assignment behaviour while preventing the
    /// generic CompProperties class from deriving three owners from size.x.
    /// </summary>
    public class CompProperties_NotRegalBedAssignable :
        CompProperties_AssignableToPawn
    {
        public CompProperties_NotRegalBedAssignable()
        {
            compClass = typeof(CompAssignableToPawn_NotRegalBed);
            maxAssignedPawnsCount = 1;
            drawAssignmentOverlay = false;
            drawUnownedAssignmentOverlay = false;
        }

        public override void PostLoadSpecial(ThingDef parent)
        {
            // Vanilla normally sets this from bed size.x. This 3x3 bed is one
            // oversized sleeping space, so the resolved value must remain one.
            maxAssignedPawnsCount = 1;
        }
    }

    public class CompAssignableToPawn_NotRegalBed : CompAssignableToPawn_Bed
    {
        public override void PostExposeData()
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MigrateLegacyAssignments();
            }

            base.PostExposeData();
        }

        private void MigrateLegacyAssignments()
        {
            Building_Bed bed = parent as Building_Bed;
            if (!NotRegalBedUtility.IsNotRegalBed(bed))
            {
                return;
            }

            List<Pawn> saved = assignedPawns
                .Where(delegate(Pawn pawn) { return pawn != null; })
                .ToList();
            Pawn retained = saved.FirstOrDefault(delegate(Pawn pawn)
            {
                return pawn.ownership != null &&
                    pawn.ownership.OwnedBed == bed;
            });

            if (retained == null && !bed.Medical)
            {
                retained = saved.FirstOrDefault(delegate(Pawn pawn)
                {
                    return pawn.ownership != null &&
                        pawn.ownership.OwnedBed == null;
                });
            }

            assignedPawns.Clear();
            foreach (Pawn pawn in saved)
            {
                if (pawn != retained && pawn.ownership != null &&
                    pawn.ownership.OwnedBed == bed)
                {
                    pawn.ownership.UnclaimBed();
                }
            }

            if (retained == null)
            {
                return;
            }

            if (retained.ownership.OwnedBed == bed)
            {
                assignedPawns.Add(retained);
                SortAssignedPawns();
            }
            else if (retained.ownership.OwnedBed == null && !bed.Medical)
            {
                retained.ownership.ClaimBedIfNonMedical(bed);
            }
        }
    }

    /// <summary>
    /// Vanilla only defines interaction offsets for 1-deep sleeping spots and
    /// 2-deep beds. This pattern covers the perimeter of a 3-deep, 3-wide bed
    /// from its single centred head position without throwing.
    /// </summary>
    public class NotRegalBedInteractionCellSearchPattern : BedCellSearchPattern
    {
        public static readonly NotRegalBedInteractionCellSearchPattern Instance =
            new NotRegalBedInteractionCellSearchPattern();

        public override void BedCellOffsets(
            List<IntVec3> offsets,
            IntVec2 size,
            int slot)
        {
            // Sides beside the sleeper, middle, and foot of the large bed.
            offsets.Add(2 * IntVec3.West);
            offsets.Add(2 * IntVec3.East);
            offsets.Add(IntVec3.South + 2 * IntVec3.West);
            offsets.Add(IntVec3.South + 2 * IntVec3.East);

            // Head-side cells, matching the preference of an ordinary bed.
            offsets.Add(IntVec3.North);
            offsets.Add(IntVec3.North + IntVec3.West);
            offsets.Add(IntVec3.North + IntVec3.East);
            offsets.Add(IntVec3.North + 2 * IntVec3.West);
            offsets.Add(IntVec3.North + 2 * IntVec3.East);

            offsets.Add(2 * IntVec3.South + 2 * IntVec3.West);
            offsets.Add(2 * IntVec3.South + 2 * IntVec3.East);

            // Foot-side cells are the final external fallback.
            offsets.Add(3 * IntVec3.South);
            offsets.Add(3 * IntVec3.South + IntVec3.West);
            offsets.Add(3 * IntVec3.South + IntVec3.East);
            offsets.Add(3 * IntVec3.South + 2 * IntVec3.West);
            offsets.Add(3 * IntVec3.South + 2 * IntVec3.East);

            // Interior fallbacks are retained for compatibility with a mod
            // that deliberately makes the bed footprint standable again.
            offsets.Add(IntVec3.West);
            offsets.Add(IntVec3.East);
            offsets.Add(IntVec3.South);
            offsets.Add(2 * IntVec3.South);
            offsets.Add(IntVec3.Zero);
        }
    }

    [HarmonyPatch(
        typeof(Building_Bed),
        nameof(Building_Bed.SleepingSlotsCount),
        MethodType.Getter)]
    public static class NotRegalBedSleepingSlotsCountPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("RRHarmony")]
        public static void Postfix(Building_Bed __instance, ref int __result)
        {
            if (NotRegalBedUtility.IsNotRegalBed(__instance))
            {
                __result = 1;
            }
        }
    }

    [HarmonyPatch(
        typeof(Building_Bed),
        nameof(Building_Bed.GetSleepingSlotPos),
        new Type[] { typeof(int) })]
    public static class NotRegalBedSleepingSlotPositionPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("RRHarmony")]
        public static void Postfix(
            Building_Bed __instance,
            int index,
            ref IntVec3 __result)
        {
            if (NotRegalBedUtility.IsNotRegalBed(__instance))
            {
                __result = index == 0
                    ? NotRegalBedUtility.GetSleepingSlotPosition(__instance)
                    : __instance.Position;
            }
        }
    }

    [HarmonyPatch(
        typeof(Building_Bed),
        nameof(Building_Bed.GetFootSlotPos),
        new Type[] { typeof(int) })]
    public static class NotRegalBedFootSlotPositionPatch
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("RRHarmony")]
        public static void Postfix(
            Building_Bed __instance,
            int index,
            ref IntVec3 __result)
        {
            if (NotRegalBedUtility.IsNotRegalBed(__instance))
            {
                __result = index == 0
                    ? NotRegalBedUtility.GetFootSlotPosition(__instance)
                    : __instance.Position;
            }
        }
    }

    [HarmonyPatch(
        typeof(Building_Bed),
        nameof(Building_Bed.FindPreferredInteractionCell),
        new Type[] { typeof(IntVec3), typeof(CellSearchPattern) })]
    public static class NotRegalBedInteractionCellPatch
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(
            Building_Bed __instance,
            ref IntVec3 occupantLocation,
            ref CellSearchPattern customSearchPattern)
        {
            if (!NotRegalBedUtility.IsNotRegalBed(__instance))
            {
                return;
            }

            // Always normalize the focus. Old saves can contain a lying pawn
            // one tile outside the footprint because of RimRound's former
            // east-only offset; passing that stale cell to BedCellSearchPattern
            // would otherwise throw before the load migration can repair it.
            occupantLocation =
                NotRegalBedUtility.GetSleepingSlotPosition(__instance);
            customSearchPattern =
                NotRegalBedInteractionCellSearchPattern.Instance;
        }
    }

    [HarmonyPatch(
        typeof(CompAffectedByFacilities),
        nameof(CompAffectedByFacilities.CanPotentiallyLinkTo_Static),
        new Type[]
        {
            typeof(ThingDef),
            typeof(IntVec3),
            typeof(Rot4),
            typeof(ThingDef),
            typeof(IntVec3),
            typeof(Rot4),
            typeof(Map)
        })]
    public static class NotRegalBedFacilityHeadPatch
    {
        public static void Postfix(
            ThingDef facilityDef,
            IntVec3 facilityPos,
            Rot4 facilityRot,
            ThingDef myDef,
            IntVec3 myPos,
            Rot4 myRot,
            ref bool __result)
        {
            if (!__result || !NotRegalBedUtility.IsNotRegalBed(myDef) ||
                facilityDef == null)
            {
                return;
            }

            CompProperties_Facility props =
                facilityDef.GetCompProperties<CompProperties_Facility>();
            if (props == null ||
                (!props.mustBePlacedAdjacentCardinalToBedHead &&
                 !props.mustBePlacedAdjacentCardinalToAndFacingBedHead))
            {
                return;
            }

            IntVec3 head = NotRegalBedUtility.GetSlotPosition(
                myPos,
                myRot,
                myDef.size,
                true);
            CellRect facilityRect = GenAdj.OccupiedRect(
                facilityPos,
                facilityRot,
                facilityDef.size);

            __result = props.mustBePlacedAdjacentCardinalToAndFacingBedHead
                ? facilityRect.MovedBy(facilityRot.FacingCell).Contains(head)
                : head.IsAdjacentToCardinalOrInside(facilityRect);
        }
    }
}
