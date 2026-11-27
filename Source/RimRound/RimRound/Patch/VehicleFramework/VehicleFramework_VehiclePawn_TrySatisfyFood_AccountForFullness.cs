using HarmonyLib;
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
using RimRound.Utilities;

namespace RimRound.Patch
{
    public class VehicleFramework_VehiclePawn_TrySatisfyFood_AccountForFullness
    {
        public static void ApplyIfPresent(Harmony harmony)
        {
            if (!ModCompatibilityUtility.CheckModInstalled("Vehicle Framework")) return;
            Type vehicle = ModCompatibilityUtility.GetTypeFromMod("Vehicle Framework", "VehiclePawn");
            if (vehicle == null)
            {
                Log.Warning("[RimRound] Vehicle Framework is active but VehiclePawn is unavailable; passenger compatibility could not be initialized.");
                return;
            }
            MethodInfo method = vehicle.GetMethod("TrySatisfyFood", ModCompatibilityUtility.majorFlags,
                null, new[] { typeof(Pawn) }, null);
            if (method == null)
            {
                // Current VF includes passengers in native caravan needs and
                // inventory selection. Our native caravan and Thing.Ingested
                // hooks already cover that route. Do not patch a missing legacy
                // method or replace the framework's current feeding lifecycle.
                Log.Message("[RimRound] Vehicle Framework uses native caravan feeding; legacy TrySatisfyFood hook is not present.");
                return;
            }
            if (method.IsStatic)
            {
                Log.Warning("[RimRound] Vehicle Framework legacy food hook has an unsupported static signature; native feeding remains active.");
                return;
            }
            try
            {
                harmony.Patch(method, transpiler: new HarmonyMethod(GetPatchCollection().transpiler));
            }
            catch (Exception e)
            {
                Log.Error("[RimRound] Failed to patch Vehicle Framework's legacy passenger food method: " + e);
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codeInstructions = new List<CodeInstruction>(instructions);
            bool hookFound = false;

            MethodInfo curCategoryMI = typeof(Need_Food).GetProperty(nameof(Need_Food.CurCategory)).GetGetMethod(true);
            if (curCategoryMI is null)
                Log.Error($"curCategoryMI was null in {nameof(VehicleFramework_VehiclePawn_TrySatisfyFood_AccountForFullness)}");


            MethodInfo replacementMI = typeof(VehicleFramework_VehiclePawn_TrySatisfyFood_AccountForFullness)
                .GetMethod(nameof(ReplacementMethod), BindingFlags.NonPublic | BindingFlags.Static);
            if (replacementMI is null)
                Log.Error($"replacementMI was null in {nameof(VehicleFramework_VehiclePawn_TrySatisfyFood_AccountForFullness)}");

            for (int i = 0; i < codeInstructions.Count; i++)
            {
                if (!codeInstructions[i].Calls(curCategoryMI))
                    continue;

                hookFound = true;

                codeInstructions[i] = new CodeInstruction(OpCodes.Call, replacementMI);
                codeInstructions.Insert(i, new CodeInstruction(OpCodes.Ldarg_1)); //Pawn

                break;
            }


            if (!hookFound)
                Log.Error($"Failed to find insertion hook in {nameof(VehicleFramework_VehiclePawn_TrySatisfyFood_AccountForFullness)}");

            return codeInstructions;
        }



        /// <summary>
        /// Prevents pawns from eating if they shouldn't due to fullness or nutrition
        /// </summary>
        /// <returns>HungerCategory.Fed if should not eat</returns>
        private static int ReplacementMethod(Need_Food need, Pawn pawn)
        {
            HungerCategory curHunger = need.CurCategory;
            if (curHunger < HungerCategory.Hungry)
                return (int)HungerCategory.Fed;

            var comp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp is null || comp.UsesBodyModel)
                return (int)curHunger;

            float currentNutritionWithFullnessPercentage =
                (need.CurLevel + comp.PendingLegacyNutrition) / need.MaxLevel;


            if (currentNutritionWithFullnessPercentage <= 0f)
            {
                return (int)HungerCategory.Starving;
            }
            if (currentNutritionWithFullnessPercentage < need.PercentageThreshUrgentlyHungry)
            {
                return (int)HungerCategory.UrgentlyHungry;
            }
            if (currentNutritionWithFullnessPercentage < need.PercentageThreshHungry)
            {
                return (int)HungerCategory.Hungry;
            }
            return (int)HungerCategory.Fed;
        }

        public static PatchCollection GetPatchCollection()
        {
            return new PatchCollection
            {
                transpiler = typeof(VehicleFramework_VehiclePawn_TrySatisfyFood_AccountForFullness).GetMethod(
                    nameof(VehicleFramework_VehiclePawn_TrySatisfyFood_AccountForFullness.Transpiler), ModCompatibilityUtility.majorFlags)
            };
        }
    }
}
