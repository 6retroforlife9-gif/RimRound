using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimRound.Utilities;
using Verse;
using Verse.AI;

namespace RimRound.FeedingTube.Patches
{
    [HarmonyPatch(typeof(JobDriver_Ingest))]
    [HarmonyPatch(nameof(JobDriver_Ingest.TryMakePreToilReservations))]
    public class JobDriver_Ingest_TryMakePreToilReservations_AddExceptionForFaucet
    {
        public static bool Prefix(JobDriver_Ingest __instance, ref bool __result) 
        {
            bool collectedClassic =
                JobDriver_Ingest_MakeNewToils_AddExceptionForFaucet
                    .HasCollectedServing(__instance);
            if (!FoodNetworkPatchPolicy.LegacyPatchesEnabled &&
                !collectedClassic) return true;

            if (collectedClassic ||
                (__instance?.job?.GetTarget(TargetIndex.A).Thing) is
                    Building_FoodFaucet)
            {
                __result = true;
                return false;
            }
            return true;
        } 
    }
}
