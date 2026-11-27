using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using RimWorld;
using Verse;
using HarmonyLib;
using AlienRace;

using RimRound.Hediffs;
using RimRound.Utilities;
using RimRound.UI;
using RimRound.Comps;
using System.Reflection;
using RimWorld.Planet;

namespace RimRound.Patch
{
	public class Thing_Ingested_AddFullness
	{
		public static void Postfix(Thing __instance, Pawn __0, ref float __result, FullnessAndDietStats_ThingComp comp) //,
		{
			AddFullness(__instance, __0, ref __result, comp);
        }


		private static void AddFullness(Thing __instance, Pawn __0, ref float __result, FullnessAndDietStats_ThingComp comp)
		{
			if (comp == null || !comp.UsesBodyModel || __result <= 0f) return;
			ThingComp_FoodItems_NutritionDensity density = __instance.TryGetComp<ThingComp_FoodItems_NutritionDensity>();
			float ratio = density?.Props.fullnessToNutritionRatio ?? FullnessAndDietStats_ThingComp.defaultFullnessToNutritionRatio;
			comp.RecordIngestion(__result, ratio * comp.FullnessGainedMultiplier, FoodEnergyReleasePolicy.DurationTicks(__instance));
			// Vanilla ingest toils/custom feeders apply returned nutrition to Food.
		}
	}
}
