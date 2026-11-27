using HarmonyLib;
using RimRound.Comps;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimRound.Utilities;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(FeedPatientUtility))]
    [HarmonyPatch(nameof(FeedPatientUtility.IsHungry))]
    public static class FeedPatientUtility_IsHungry_CheckForFullness
    {
		public static void Postfix(Pawn __0, ref bool __result)
		{
			__result = BodyFeedingPolicy.ShouldFeedPatient(__0, __result);
		}
    }
}
