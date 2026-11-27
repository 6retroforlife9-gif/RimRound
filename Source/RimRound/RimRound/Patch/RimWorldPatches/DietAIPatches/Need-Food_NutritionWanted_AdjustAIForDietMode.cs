using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using RimWorld;
using Verse;
using HarmonyLib;
using RimRound.UI;
using RimRound.Comps;
using RimRound.Utilities;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Need_Food))]
    [HarmonyPatch("NutritionWanted", MethodType.Getter)]
    public class RimRound_NeedFood_NutritionWantedPatch
    {
		public static void Postfix(Need_Food __instance , ref float __result, Pawn ___pawn)
		{
			__result = BodyFeedingPolicy.GetRequestedNutrition(___pawn, __result);
		}
    }
}
