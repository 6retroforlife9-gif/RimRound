using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(ModSummaryWindow))]
    [HarmonyPatch("DrawContents")]
    public class ModSummaryWindow_DrawContents_RemoveRRFromTheOfficialContentList
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> codeInstructions = new List<CodeInstruction>(instructions);
            List<CodeInstruction> newInstructions = new List<CodeInstruction>();

            Label label = generator.DefineLabel();

            bool insertionPointFound = false;
            int insertionIndex = -1;

            if (ShouldSkipListingExpansionMI is null) 
            {
                Log.Error($"Failed to get method info for ShouldSkipListingExpansion in {nameof(ModSummaryWindow_DrawContents_RemoveRRFromTheOfficialContentList)}");
                return codeInstructions;
            }


            MethodInfo getAllExpansionsMI = typeof(ModLister).GetProperty(nameof(ModLister.AllExpansions), BindingFlags.Public | BindingFlags.Static).GetGetMethod(true);
            if (getAllExpansionsMI == null) 
            {
                Log.Error($"Failed to get method info for Modlister.GetAllExpansions in {nameof(ModSummaryWindow_DrawContents_RemoveRRFromTheOfficialContentList.Transpiler)}");
                return codeInstructions;
            }

            for (int i = 0; i < codeInstructions.Count(); i++) 
            {
                if (codeInstructions[i].opcode == OpCodes.Call && codeInstructions[i].operand != null && codeInstructions[i].operand is MethodInfo mi && mi == getAllExpansionsMI) // This is the body of the upper for loop that generates the expansions in the loading screen
                {
                    insertionPointFound = true;
                    insertionIndex = i;
                    break;
                }
            }


            if (!insertionPointFound) 
            {
                Log.Error($"Failed to find insertion point in {nameof(ModSummaryWindow_DrawContents_RemoveRRFromTheOfficialContentList)}");
                return codeInstructions;
            }

            List<CodeInstruction> getExpansionInstructons = new List<CodeInstruction>();
            getExpansionInstructons.AddRange(codeInstructions.GetRange(insertionIndex, 3)); // This is just the opcodes for: ModLister.AllExpansions[i];



            codeInstructions[insertionIndex + 12].labels.Add(label); // Put the jump label to the index increment part of the for loop
            CodeInstruction loadLocFunnyClass = codeInstructions[insertionIndex - 1]; // This is an instruction that looks like:  ldloc.s   V_11. The decomp shows it as type Verse.ModSummaryWindow/'<>c__DisplayClass17_1' which is some sort of compiler generated class


            newInstructions.Add(new CodeInstruction(OpCodes.Call, ShouldSkipListingExpansionMI)); // This empties the stack and pushes a bool
            newInstructions.Add(new CodeInstruction(OpCodes.Brtrue_S, label)); // Go to just before the for loop increments at the end of the code block
            newInstructions.Add(new CodeInstruction(loadLocFunnyClass)); // If we don't skip, we need to have this object back on the stack
            newInstructions.AddRange(getExpansionInstructons); // Run the previous code to get the ExpansionDef and continue as normal
            codeInstructions.InsertRange(insertionIndex + 3, newInstructions);

            return codeInstructions;
        }


        static MethodInfo ShouldSkipListingExpansionMI = typeof(ModSummaryWindow_DrawContents_RemoveRRFromTheOfficialContentList).GetMethod(nameof(ModSummaryWindow_DrawContents_RemoveRRFromTheOfficialContentList.ShouldSkipListingExpansion), BindingFlags.NonPublic | BindingFlags.Static);
        private static bool ShouldSkipListingExpansion(dynamic _, ExpansionDef exp) // The dynamic here pops off a weird object that we will spawn later with the loadLocFunnyClass instruction
        {
            return exp.defName == "RimRound_Expansion_Royalty";
        }
    }
}
