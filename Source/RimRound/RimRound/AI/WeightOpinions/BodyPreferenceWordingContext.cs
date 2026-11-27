using System.Collections.Generic;
using System.Linq;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.AI
{
    internal static class BodyPreferenceWordingContext
    {
        internal static BodyPreferenceWishRegions PresentWishRegions(Pawn pawn)
        {
            var body = pawn?.RaceProps?.body;
            var health = pawn?.health?.hediffSet;
            var regions = BodyPreferenceWishRegions.None;
            if (body?.corePart == null || health == null) return regions;

            bool torso = body.corePart.def == BodyPartDefOf.Torso && IsNaturalPart(health, body.corePart);
            int arms = 0, legs = 0;
            foreach (var part in body.AllParts)
            {
                var def = part?.def;
                if (def == null) continue;
                BodyPreferenceWishRegions region;
                if (def == BodyPartDefOf.Arm) region = BodyPreferenceWishRegions.Arms;
                else if (def == BodyPartDefOf.Leg) region = BodyPreferenceWishRegions.Legs;
                else if (BodyPartTagDefOf.Pelvis != null && def.tags?.Contains(BodyPartTagDefOf.Pelvis) == true)
                    region = BodyPreferenceWishRegions.Hips;
                else if (torso && def.defName == "Stomach") region = BodyPreferenceWishRegions.Belly;
                else if (torso && (def.defName == "Ribcage" || def.defName == "Sternum"))
                    region = BodyPreferenceWishRegions.Chest;
                else continue;

                if (!IsNaturalPart(health, part)) continue;
                if (region == BodyPreferenceWishRegions.Arms) arms++;
                else if (region == BodyPreferenceWishRegions.Legs) legs++;
                else regions |= region;
            }
            // The limb lines are plural. A single remaining natural limb does not qualify.
            if (arms >= 2) regions |= BodyPreferenceWishRegions.Arms;
            if (legs >= 2) regions |= BodyPreferenceWishRegions.Legs;
            return regions;
        }

        private static bool IsNaturalPart(HediffSet health, BodyPartRecord part)
        {
            for (var current = part; current != null; current = current.parent)
                if (health.PartIsMissing(current) || health.HasDirectlyAddedPartFor(current)) return false;
            return true;
        }

        internal static bool HasWeightCausedWalkingDifficulty(Pawn pawn)
        {
            if (pawn?.Spawned != true || pawn.Dead || pawn.Downed || pawn.CarriedBy != null ||
                pawn.Flying || pawn.Swimming || pawn.apparel?.WornApparel.Any(a => EquipmentQuality.IsChair(a.def)) == true ||
                pawn.GetPosture() != PawnPosture.Standing || pawn.health?.capacities == null)
                return false;

            var moving = PawnCapacityDefOf.Moving;
            float level = pawn.health.capacities.GetLevel(moving);
            if (!(level > 0f && level <= .75f) || !pawn.health.capacities.CapableOf(moving)) return false;

            var weight = pawn.WeightHediff();
            // CapMods includes the player's penalty setting and this pawn's mitigation.
            var modifier = weight?.CapMods?.Find(m => m.capacity == moving);
            if (modifier == null || !(modifier.offset <= -.25f) || modifier.postFactor != 1f || modifier.SetMaxDefined)
                return false;

            // A healthy walker's unit baseline plus this offset must account for the observed level.
            // Native impactors also identify limb damage, dependent capacities, genes and other hediffs.
            if (level != GenMath.RoundedHundredth(1f + modifier.offset)) return false;
            var impactors = new List<PawnCapacityUtility.CapacityImpactor>();
            float evaluated = PawnCapacityUtility.CalculateCapacityLevel(pawn.health.hediffSet, moving, impactors);
            return evaluated == level && impactors.Count == 1 &&
                impactors[0] is PawnCapacityUtility.CapacityImpactorHediff cause && cause.hediff == weight;
        }
    }
}
