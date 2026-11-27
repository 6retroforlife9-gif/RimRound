using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimRound.FeedOther
{
    // Native random tale/book authors can acquire real pawn relations while
    // being generated. Hand unowned authors to ordinary world-pawn management
    // after their tale data is captured; normal garbage collection still decides
    // which authors matter. Explicit existing authors use GenerateFrom directly.
    [HarmonyPatch(typeof(TaleData_Pawn), nameof(TaleData_Pawn.GenerateRandom))]
    internal static class TalePawnReferenceCompatibility
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var original = AccessTools.Method(typeof(TaleData_Pawn), nameof(TaleData_Pawn.GenerateFrom), new[] { typeof(Pawn) });
            var calls = code.Where(c => c.Calls(original)).ToArray();
            if (calls.Length != 1)
            {
                Log.Warning("[RimRound] Random tale-author reference compatibility did not match this game's method; original behavior retained.");
                return code;
            }
            calls[0].operand = AccessTools.Method(typeof(TalePawnReferenceCompatibility), nameof(CaptureAndRegister));
            return code;
        }

        private static TaleData_Pawn CaptureAndRegister(Pawn pawn)
        {
            var data = TaleData_Pawn.GenerateFrom(pawn);
            var worldPawns = Find.WorldPawns;
            if (pawn != null && !pawn.Destroyed && !pawn.Discarded && !pawn.SpawnedOrAnyParentSpawned &&
                pawn.ParentHolder == null && worldPawns != null && !worldPawns.Contains(pawn))
                worldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
            return data;
        }
    }
}
