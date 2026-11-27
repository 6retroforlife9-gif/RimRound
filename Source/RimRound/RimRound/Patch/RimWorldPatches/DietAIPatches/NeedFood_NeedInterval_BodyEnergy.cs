using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimRound.Comps;
using RimWorld;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Need_Food), nameof(Need_Food.NeedInterval))]
    public static class NeedFood_NeedInterval_BodyEnergy
    {
        private static readonly AccessTools.FieldRef<Need, Pawn> PawnField = AccessTools.FieldRefAccess<Need, Pawn>("pawn");

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Need_Food), nameof(Need_Food.FoodFallPerTick));
            var replacement = AccessTools.Method(typeof(NeedFood_NeedInterval_BodyEnergy), nameof(ObserveFall));
            int matches = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    matches++;
                }
                yield return instruction;
            }
            if (matches != 1) Log.Error("[RimRound] Food interval energy hook expected one native expenditure read; found " + matches);
        }

        public static float ObserveFall(Need_Food need)
        {
            float fall = need.FoodFallPerTick;
            PawnField(need)?.TryGetComp<FullnessAndDietStats_ThingComp>()?.ObserveNativeFoodInterval(fall * 150f, 150);
            return fall;
        }

        public static void Postfix(Pawn ___pawn)
        {
            ___pawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.CompleteNativeFoodInterval();
        }
    }
}
