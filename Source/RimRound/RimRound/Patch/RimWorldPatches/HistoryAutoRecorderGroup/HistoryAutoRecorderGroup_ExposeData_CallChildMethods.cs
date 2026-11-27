using HarmonyLib;
using RimRound.AI;
using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(HistoryAutoRecorderGroup))]
    [HarmonyPatch(nameof(HistoryAutoRecorderGroup.ExposeData))]
    public class HistoryAutoRecorderGroup_ExposeData_CallChildMethods
    {
        public static bool Prefix(HistoryAutoRecorderGroup __instance)
        {
            var weightDef = Defs.HistoryAutoRecorderGroupDefOf.RR_PawnWeightHistory;
            // Newly constructed groups have no def until LoadingVars. Routing
            // on the uninitialized field lets vanilla deserialize the custom
            // recorder XML as its own incompatible type and loses pawn/samples.
            bool isWeight = Scribe.mode == LoadSaveMode.LoadingVars ?
                Scribe.loader.curXmlParent?["def"]?.InnerText == weightDef.defName : __instance.def == weightDef;
            if (!isWeight)
                return true;

            __instance.def = weightDef;
            HistoryAutoRecorderGroupWeight.Instance().ExposeData();
            return false;
        }
    }
}
