using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimRound.Comps;

namespace RimRound.Patch
{
    public static class VENutrientPaste_Building_Dripper_FixForRR
    {
        private static MethodInfo methodInfo_GetCurLevelPercentage = typeof(Need).GetProperty(nameof(Need.CurLevelPercentage)).GetGetMethod();
        private static MethodInfo methodInfo_ShouldntEat = typeof(VENutrientPaste_Building_Dripper_FixForRR).GetMethod(nameof(ShouldntEat), BindingFlags.NonPublic | BindingFlags.Static);

        private static bool ShouldntEat(Pawn pawn)
        {
            if (pawn?.needs?.food == null)
                return true;
            bool vanillaDecision =
                pawn.needs.food.CurLevelPercentage <= 0.4f;
            return !BodyFeedingPolicy.ShouldSeekFood(pawn, vanillaDecision);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> codeInstructions = new List<CodeInstruction>(instructions);
            List<CodeInstruction> newInstructions = new List<CodeInstruction>();

            if (methodInfo_GetCurLevelPercentage == null) {
                Log.Error("Failed to patch VENutrientPaste_Building_Dripper_FixForRR - Null MethodInfo");
                return codeInstructions;
            }


            int startJndex = -1;
            int endJndex = -1;

            for (int i = 0; i < codeInstructions.Count; ++i) {
                if (codeInstructions[i].opcode == OpCodes.Callvirt && (MethodInfo)codeInstructions[i].operand == methodInfo_GetCurLevelPercentage) {
                    Log.Message("Found Insertion Point");
                    startJndex = i - 2; // Just after pawn is loaded on stack
                    endJndex = i + 3; // Just before 0 is loaded on stack
                    break;
                }
            }

            newInstructions.Add(new CodeInstruction(OpCodes.Call, methodInfo_ShouldntEat));

            if (startJndex != -1 && endJndex != -1)
            {
                codeInstructions.RemoveRange(startJndex, endJndex - startJndex + 1);
                codeInstructions.InsertRange(startJndex, newInstructions);
            }

            return codeInstructions;
        }


        public static PatchCollection GetPatchCollection()
        {
            return new PatchCollection
            {
                transpiler = typeof(VENutrientPaste_Building_Dripper_FixForRR).GetMethod(
                    nameof(VENutrientPaste_Building_Dripper_FixForRR.Transpiler), ModCompatibilityUtility.majorFlags)
            };
        }
    }
}
