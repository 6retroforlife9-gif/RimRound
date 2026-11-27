using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(FloatMenuOptionProvider_Ingest))]
    [HarmonyPatch("GetSingleOptionFor")]
    public class FloatMenuMakerMap_AddHumanlikeOrders_AlterTextForFoodAboveHardLimit
    {
        public static void Postfix(Thing clickedThing, FloatMenuContext context, ref FloatMenuOption __result)
        {
            if (context is null || clickedThing?.def?.ingestible == null || __result?.action == null)
            {
                return;
            }
            
            Pawn selectedPawn = context?.FirstSelectedPawn;

            if (selectedPawn == null || !selectedPawn.RaceProps.Humanlike) 
            {
                return;
            }

            // This provider owns ingest options with native Eat/Drink/Take
            // labels as well as Consume. Do not use translated text as a gate.
            string targetLabel = __result.Label;
            {
                if (!SpecialistContentPolicy.Allows(clickedThing))
                {
                    __result.Label = targetLabel + ": " + SpecialistContentPolicy.FantasyReason;
                    __result.action = null;
                    return;
                }
                if (selectedPawn.TryGetComp<FullnessAndDietStats_ThingComp>() is FullnessAndDietStats_ThingComp FnDStatsComp)
                {
                    // The ordinary-food AI intentionally excludes atomic drugs.
                    // An explicit consume order instead uses the complete-dose
                    // volume gate, independent of hunger and the body goal.
                    // Other native drugs retain their native menu restrictions.
                    bool fits = MetabolicDrugIntake.IsMetabolicDrug(clickedThing.def)
                        ? MetabolicDrugIntake.FitsDose(selectedPawn, clickedThing)
                        : OrdinaryFoodIntake.IsAtomicDef(clickedThing.def) ||
                            (FnDStatsComp.UsesBodyModel && OrdinaryFoodIntake.IsOrdinaryFood(clickedThing)
                                ? OrdinaryFoodIntake.CanOrderServing(selectedPawn, clickedThing)
                                : BodyFeedingPolicy.CanFitFood(selectedPawn, clickedThing,
                                    FoodUtility.NutritionForEater(selectedPawn, clickedThing)));
                    if (fits) return;
                    if (!(FnDStatsComp.fullnessbar?.peaceForeverHeld ?? false))
                    {
                        __result.Label = "FloatMenuCantConsumeTooBig".Translate(clickedThing.LabelShort, clickedThing);
                        __result.action = null;
                    }
                }
            }
        }
    }
}
