using System;
using Verse;
using RimRound.FeedingTube;

namespace RimRound.FeedOther
{
    internal static class FoodNetworkServingRecovery
    {
        // True means the serving was returned and consumed by the network, or
        // retained as a physical item. False leaves it intact for the caller
        // when neither a map nor an inventory can hold it.
        internal static bool TryReturnOrPlace(Thing source, Thing serving,
            Pawn carrier)
        {
            if (serving == null || serving.Destroyed)
                return false;
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled && source is Building_FoodFaucet classicFaucet)
                return classicFaucet.TryRecoverServing(serving, carrier);
            if (FoodNetworkV2ServingUtility.TryReturnToNetwork(source, serving))
            {
                serving.Destroy(DestroyMode.Vanish);
                return true;
            }

            if (serving.Spawned || carrier?.carryTracker?.CarriedThing == serving)
                return true;

            if (carrier != null && carrier.Spawned &&
                TryPlace(serving, carrier.Position, carrier.Map))
                return true;
            if (source != null && source.Spawned &&
                TryPlace(serving, source.Position, source.Map))
                return true;

            // A despawned pawn can still own inventory; this also protects
            // a serving if map placement is temporarily unavailable.
            if (carrier?.inventory?.innerContainer != null &&
                carrier.inventory.innerContainer.TryAdd(serving))
                return true;

            // Last map fallback searches a wider area for a cell with an item
            // slot. Direct spawning into a full cell could move or destroy a
            // different item, so only use a cell with explicit spare room.
            Map map = carrier != null && carrier.Spawned
                ? carrier.Map : source != null && source.Spawned
                    ? source.Map : null;
            IntVec3 cell = carrier != null && carrier.Spawned
                ? carrier.Position : source != null && source.Spawned
                    ? source.Position : IntVec3.Invalid;
            if (map != null && cell.IsValid && cell.InBounds(map))
            {
                for (int radius = 0; radius <= 8; radius++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                                continue;
                            IntVec3 candidate = cell + new IntVec3(dx, 0, dz);
                            if (!candidate.InBounds(map) ||
                                !candidate.Standable(map) ||
                                candidate.GetItemCount(map) >=
                                    candidate.GetMaxItemsAllowedInCell(map))
                                continue;
                            try
                            {
                                Thing placed = GenSpawn.Spawn(serving,
                                    candidate, map, WipeMode.Vanish);
                                if (placed != null && placed.Spawned)
                                    return true;
                            }
                            catch (Exception exception)
                            {
                                Log.Error("[RimRound Feed Other] Could not " +
                                    "place a refunded serving: " + exception);
                            }
                        }
                    }
                }
            }

            Log.Error("[RimRound Feed Other] No map or inventory could hold " +
                "a food network serving after refund failed; serving was not destroyed.");
            return false;
        }

        private static bool TryPlace(Thing serving, IntVec3 cell, Map map)
        {
            if (map == null || !cell.IsValid || !cell.InBounds(map))
                return false;
            try
            {
                Thing placed;
                return GenPlace.TryPlaceThing(serving, cell, map,
                    ThingPlaceMode.Near, out placed) &&
                    placed != null && placed.Spawned;
            }
            catch (Exception exception)
            {
                Log.Error("[RimRound Feed Other] Nearby serving placement " +
                    "failed: " + exception);
                return serving.Spawned;
            }
        }
    }
}
