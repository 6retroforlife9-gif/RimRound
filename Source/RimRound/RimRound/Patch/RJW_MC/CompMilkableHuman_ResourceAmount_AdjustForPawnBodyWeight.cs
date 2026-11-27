using RimRound.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    public class CompMilkableHuman_ResourceAmount_AdjustForPawnBodyWeight
    {
        public static void Postfix(ref float __result, ThingComp __instance) 
        {
            __result *= BodyResourceUtility.GetMilkMultiplierByWeight(__instance.parent.AsPawn());
        }

        public static PatchCollection GetPatchCollection()
        {
            return new PatchCollection 
            {
                postfix = typeof(CompMilkableHuman_ResourceAmount_AdjustForPawnBodyWeight).GetMethod(
                    nameof(CompMilkableHuman_ResourceAmount_AdjustForPawnBodyWeight.Postfix), ModCompatibilityUtility.majorFlags)
            };
        }
    }
}
