using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RimRound.Patch.RimWorldPatches
{
    public class Building_Bed_SleepingSlotsCount_AdjustForRRBed
    {
        public static bool Prefix(Building_Bed __instance, ref int __result) {
            if (__instance.def.defName == "NotRegalBed") {
                __result = 1;
                return false;
            }

            return true;
        }
    }
}
