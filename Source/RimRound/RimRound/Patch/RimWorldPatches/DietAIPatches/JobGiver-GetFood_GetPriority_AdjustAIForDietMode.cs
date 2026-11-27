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
	[HarmonyPatch(typeof(JobGiver_GetFood))]
	[HarmonyPatch("GetPriority")]
	//Makes the weight gizmo work. In chanrge of altering when the pawn seeks food.
	public class RimRound_JobGiver_GetFoodPatch
	{
		public static void Postfix(Pawn __0, ref float __result)
		{
			__result = BodyFeedingPolicy.GetFeedingPriority(__0, __result);
		}
	}

}
