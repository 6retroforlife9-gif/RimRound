using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace RimRound.FeedOther
{
    public static class OrbitalMovementPenaltyUtility
    {
        public static float OrbitalMovementPenaltyFactor =>
            FeedOtherMod.Settings.OrbitalMovementPenaltyFactor;

        public static void NotifySettingsChanged()
        {
            if (Find.Maps == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null)
                {
                    continue;
                }

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    pawn?.health?.capacities?.Notify_CapacityLevelsDirty();
                }
            }
        }

        public static bool IsOrbitalMap(Map map)
        {
            if (map == null)
            {
                return false;
            }

            // A claimed asteroid is re-parented to a normal Settlement, so its
            // MapParent type no longer identifies it as orbital. The tile's
            // planet layer remains authoritative for gravships, asteroids,
            // platforms, and settled space colonies alike.
            PlanetTile tile = map.Tile;
            return tile.Valid &&
                (tile.LayerDef == PlanetLayerDefOf.Orbit || tile.LayerDef?.isSpace == true);
        }

        public static bool IsRimRoundMobilityHediff(Hediff hediff)
        {
            string defName = hediff?.def?.defName;
            return defName == "RimRound_Weight" || defName == "RimRound_Fullness";
        }
    }

    [HarmonyPatch(typeof(Hediff), nameof(Hediff.CapMods), MethodType.Getter)]
    [HarmonyAfter("RRHarmony")]
    public static class Hediff_CapMods_ReduceRimRoundMovementPenaltyInOrbitPatch
    {
        public static void Postfix(Hediff __instance, ref List<PawnCapacityModifier> __result)
        {
            if (!FeedOtherMod.Settings.orbitalMovementReliefEnabled ||
                __result == null ||
                !OrbitalMovementPenaltyUtility.IsRimRoundMobilityHediff(__instance) ||
                !OrbitalMovementPenaltyUtility.IsOrbitalMap(__instance?.pawn?.Map))
            {
                return;
            }

            List<PawnCapacityModifier> adjusted = new List<PawnCapacityModifier>(__result.Count);
            for (int index = 0; index < __result.Count; index++)
            {
                PawnCapacityModifier original = __result[index];
                PawnCapacityModifier modifier = new PawnCapacityModifier
                {
                    capacity = original.capacity,
                    offset = original.offset,
                    setMax = original.setMax,
                    postFactor = original.postFactor,
                    statFactorMod = original.statFactorMod,
                    setMaxCurveOverride = original.setMaxCurveOverride,
                    setMaxCurveEvaluateStat = original.setMaxCurveEvaluateStat
                };
                if (modifier.capacity == PawnCapacityDefOf.Moving && modifier.offset < 0f)
                {
                    modifier.offset *= OrbitalMovementPenaltyUtility.OrbitalMovementPenaltyFactor;
                }

                adjusted.Add(modifier);
            }

            __result = adjusted;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Pawn_SpawnSetup_RefreshOrbitalMovementCapacityPatch
    {
        public static void Postfix(Pawn __instance)
        {
            // Capacity levels are cached. Explicitly dirty them whenever a pawn
            // is transferred between a planetary and orbital map so the new
            // map's movement factor is visible immediately.
            __instance?.health?.capacities?.Notify_CapacityLevelsDirty();
        }
    }
}
