using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace RimRound.FeedOther
{
    // Native generation creates relations before checking required skills. The
    // 1.6 skill-rejection branch omits the cleanup used by other rejections,
    // leaving an existing pawn related to a parent which will never be saved.
    [HarmonyPatch(typeof(PawnGenerator), "TryGenerateNewPawnInternal")]
    internal static class RejectedPawnReferenceCompatibility
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var discard = AccessTools.Method(typeof(PawnGenerator), "DiscardGeneratedPawn");
            var markers = code.Select((c, i) => new { c, i }).Where(x =>
                x.c.opcode == OpCodes.Ldstr && (string)x.c.operand == "Generated pawn incapable of required skill: ").ToArray();
            int nativeDiscard = code.FindIndex(c => c.Calls(discard));
            if (markers.Length != 1 || nativeDiscard < 2 ||
                code[nativeDiscard - 2].opcode != OpCodes.Ldloc_0 ||
                code[nativeDiscard - 1].opcode != OpCodes.Ldfld ||
                !(code[nativeDiscard - 1].operand is FieldInfo pawnField) || pawnField.FieldType != typeof(Pawn))
                return Unmatched(code);

            int marker = markers[0].i;
            int end = code.FindIndex(marker, c => c.opcode == OpCodes.Stind_Ref);
            if (end < marker || end - marker > 12 || end + 3 >= code.Count ||
                code.Skip(marker).Take(end - marker).Any(c => c.Calls(discard)) ||
                code[end + 1].opcode != OpCodes.Ldnull || !code[end + 2].IsStloc() ||
                (code[end + 3].opcode != OpCodes.Leave && code[end + 3].opcode != OpCodes.Leave_S))
                return Unmatched(code);

            // Use the same pawn local and native discard routine as adjacent
            // rejection branches, before the generation stack's finally runs.
            var load = new CodeInstruction(OpCodes.Ldloc_0);
            load.labels.AddRange(code[end + 1].labels);
            load.blocks.AddRange(code[end + 1].blocks);
            code[end + 1].labels.Clear();
            code[end + 1].blocks.Clear();
            code.InsertRange(end + 1, new[] { load, new CodeInstruction(OpCodes.Ldfld, pawnField), new CodeInstruction(OpCodes.Call, discard) });
            return code;
        }

        private static IEnumerable<CodeInstruction> Unmatched(List<CodeInstruction> code)
        {
            Log.Warning("[RimRound] Rejected-pawn reference compatibility did not match this game's method; original behavior retained.");
            return code;
        }
    }
}
