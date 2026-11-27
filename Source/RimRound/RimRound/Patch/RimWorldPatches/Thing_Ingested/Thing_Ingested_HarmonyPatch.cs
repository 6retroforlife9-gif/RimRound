using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("Ingested", new Type[] { typeof(Pawn), typeof(float) })]
    public class Thing_Ingested_HarmonyPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Thing __instance, Pawn __0, float __1,
            ref float __result, out OrdinaryFoodIntake.Transaction __state)
        {
            bool allowed = OrdinaryFoodIntake.Begin(__instance, __0, __1, out __state);
            if (!allowed) __result = 0f;
            return allowed;
        }

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Thing __instance, Pawn __0,
            ref float __result, OrdinaryFoodIntake.Transaction __state)
        {
            if (__state != null)
            {
                OrdinaryFoodIntake.Complete(__state);
                return;
            }
            // A rejected ordinary meal ran no native effects and earns no ledger.
            if (OrdinaryFoodIntake.Applies(__0, __instance)) return;
            FullnessAndDietStats_ThingComp comp = __0?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp != null && comp.UsesBodyModel)
            {
                PlaySloshIfBeer(__instance, comp);
                Thing_Ingested_AddFullness.Postfix(__instance, __0, ref __result, comp);
                Thing_Ingested_StomachBurstCheck.Postfix(comp);
            }

        }

        public static Exception Finalizer(Exception __exception, OrdinaryFoodIntake.Transaction __state)
        {
            OrdinaryFoodIntake.End(__state);
            return __exception;
        }

        private static void PlaySloshIfBeer(Thing ingestedItem, FullnessAndDietStats_ThingComp comp) 
        {
            if (ingestedItem.def.defName == ThingDefOf.Beer.defName)
            {
                const float SECONDS_OF_SLOSHING_PER_BEER = 15;
                comp.SloshDurationSeconds += SECONDS_OF_SLOSHING_PER_BEER;
                if (comp.SloshStartTick == 0) 
                {
                    comp.SloshStartTick = Find.TickManager.TicksAbs;
                }
            }
        }
    }
}
