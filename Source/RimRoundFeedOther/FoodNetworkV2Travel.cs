using HarmonyLib;
using RimRound.FeedingTube.Comps;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    // MapDeiniter still has the complete map ThingOwner graph at entry.
    // Native gravship generation has already removed departing Things, while
    // packed caravan cargo is held by the caravan rather than this map.
    [HarmonyPatch(typeof(MapDeiniter), nameof(MapDeiniter.Deinit))]
    internal static class FoodNetworkV2MapAbandonPatch
    {
        private static void Prefix(Map map)
        {
            FoodNetworkV2GameComponent.Instance?.RemoveAbandonedMap(map);
        }
    }

    // The existing destruction postfix retires the building ID. Capture
    // classic quantities while the native building is still intact, including
    // the inner building of a destroyed minified wrapper.
    [HarmonyPatch(typeof(Building), nameof(Building.Destroy))]
    internal static class FoodNetworkV2ClassicDestroyImportPatch
    {
        private static void Prefix(Building __instance)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled &&
                __instance != null && !__instance.Destroyed &&
                __instance.TryGetComp<FoodNetStorage_ThingComp>() != null)
                FoodNetworkV2MapComponent.ImportLegacyStorage(__instance);
        }
    }

    // Feeding-tube fluid weighs 0.045 kg for 0.10 physical fullness in its
    // ThingDef. Stored liquid therefore adds 0.45 kg per fullness unit to the
    // vat's existing empty Mass. Native Mass requests for minified Things
    // inherit the inner building's stat, so do not add again to the wrapper.
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue),
        new[] { typeof(StatRequest), typeof(bool) })]
    internal static class FoodNetworkV2VatMassPatch
    {
        internal static float StoredLiquidMass(Thing thing)
        {
            Building tank = thing as Building ??
                (thing as MinifiedThing)?.InnerThing as Building;
            FoodNetStorage_ThingComp classic =
                tank?.TryGetComp<FoodNetStorage_ThingComp>();
            if (classic == null) return 0f;
            float fullness = FeedOtherMod.Settings.foodNetworkV2Enabled
                ? FoodNetworkV2GameComponent.Instance?
                    .GetTankState(tank, false)?.StoredFullness ?? classic.Stored
                : classic.Stored;
            return float.IsNaN(fullness) || float.IsInfinity(fullness) ||
                fullness <= 0f ? 0f : fullness * 0.45f;
        }

        private static void Postfix(StatDef ___stat, StatRequest req,
            ref float __result)
        {
            if (___stat != StatDefOf.Mass) return;
            // Native minified-stat inheritance has already queried the inner
            // building and received this contribution on that call.
            if (req.Thing is Building)
                __result += StoredLiquidMass(req.Thing);
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetExplanationFinalizePart))]
    internal static class FoodNetworkV2VatMassExplanationPatch
    {
        private static void Postfix(StatDef ___stat, StatRequest req,
            ref string __result)
        {
            if (___stat != StatDefOf.Mass) return;
            float contribution = FoodNetworkV2VatMassPatch.StoredLiquidMass(req.Thing);
            if (contribution > 0f)
                __result += "\n" + "RR_FoodNetworkStoredMass".Translate(
                    StatDefOf.Mass.ValueToString(contribution));
        }
    }
}
