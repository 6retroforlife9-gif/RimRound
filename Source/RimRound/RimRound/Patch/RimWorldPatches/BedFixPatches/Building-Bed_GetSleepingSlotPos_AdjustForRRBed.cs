using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch.RimWorldPatches
{
    public class Building_Bed_GetSleepingSlotPos_AdjustForRRBed
    {
        public static void Postfix(Building_Bed __instance, ref IntVec3 __result)
        {
            //Log.Warning($"Old Pos: {__result}");
            if (__instance.def.defName == "NotRegalBed")
            {
                __result.x += 1;
            }
            //Log.Warning($"New Pos: {__result}");
            return;
        }
    }
}
