using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(ThoughtWorker_WantToSleepWithSpouseOrLover))]
    [HarmonyPatch("CurrentStateInternal")]
    public static class ThoughtWorker_WantToSleepWithSpouseOrLover_IncludeBlobBedPartner
    {
        public static void Postfix(ref ThoughtState __result, Pawn p)
        {
            if (__result.Active && BlobBedPartnerUtility.SharesAssignedPartnerBed(p))
                __result = false;
        }
    }
}
