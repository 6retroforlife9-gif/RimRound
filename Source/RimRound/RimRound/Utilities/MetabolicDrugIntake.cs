using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimRound.Comps;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace RimRound.Utilities
{
    // Owned metabolic products are indivisible doses. Body goals and hunger are
    // not drug authorization; native drug jobs/bills retain their own effects.
    public static class MetabolicDrugIntake
    {
        [ThreadStatic] internal static Pawn ClinicalPawn;
        [ThreadStatic] internal static Thing ClinicalDrug;
        [ThreadStatic] internal static Pawn CaravanPawn;
        [ThreadStatic] internal static Thing CaravanDrug;

        public static bool IsMetabolicDrug(ThingDef def)
        {
            switch (def?.defName)
            {
                case "swellGlowOriginal": case "swellGlowPlus": case "swellGlowX":
                case "swellGlowMAX": case "cakeFat": case "fatpill": return true;
                default: return false;
            }
        }

        private static bool Fits(Pawn pawn, ThingDef def, float nutrition, float density)
        {
            var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body?.UsesBodyModel != true || pawn.Dead || !SpecialistContentPolicy.Allows(def) ||
                !Finite(nutrition) || nutrition <= 0f || !Finite(density) || density < 0f ||
                !Finite(body.FullnessGainedMultiplier) || body.FullnessGainedMultiplier < 0f) return false;
            double volume = (double)nutrition * density * body.FullnessGainedMultiplier;
            double room = Math.Max(0d, Math.Min(body.SoftLimit, body.HardLimit) - body.OccupiedVolume);
            return volume <= room;
        }

        public static bool FitsDose(Pawn pawn, Thing drug)
            => drug != null && !drug.Destroyed && drug.stackCount > 0 && drug.IngestibleNow &&
               SpecialistContentPolicy.Allows(drug) && Fits(pawn, drug.def,
                   FoodUtility.NutritionForEater(pawn, drug), OrdinaryFoodIntake.Density(drug));

        public static bool FitsDose(Pawn pawn, ThingDef drug)
            => drug != null && Fits(pawn, drug, drug.GetStatValueAbstract(StatDefOf.Nutrition),
                drug.GetCompProperties<CompProperties_FoodItems_NutritionDensity>()?.fullnessToNutritionRatio ??
                FullnessAndDietStats_ThingComp.defaultFullnessToNutritionRatio);

        internal static bool AddictionAllowed(Pawn pawn, Thing drug)
        {
            if (pawn?.drugs?.CurrentPolicy?[drug.def]?.allowedForAddiction != true) return false;
            ChemicalDef chemical = drug.TryGetComp<CompDrug>()?.Props.chemical;
            return chemical != null && ((chemical.addictionHediff != null &&
                pawn.health.hediffSet.HasHediff(chemical.addictionHediff)) || AddictionUtility.HasChemicalDependency(pawn, drug.def));
        }

        internal static bool ClinicalBillFits(Bill bill, Pawn patient)
        {
            if (!(bill?.recipe?.Worker is Recipe_AdministerIngestible) || patient == null) return true;
            foreach (IngredientCount ingredient in bill.recipe.ingredients)
                foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                    if (IsMetabolicDrug(def) && !FitsDose(patient, def)) return false;
            return true;
        }

        public static bool AuthorizedNow(Pawn pawn, Thing drug)
        {
            if (!IsMetabolicDrug(drug?.def)) return true;
            if (!FitsDose(pawn, drug)) return false;
            if (ClinicalPawn == pawn && ClinicalDrug == drug) return true;
            if (CaravanPawn == pawn && CaravanDrug == drug) return AddictionAllowed(pawn, drug);
            Job job = pawn.CurJob;
            if (job?.def != JobDefOf.Ingest || job.targetA.Thing != drug) return false;
            if (job.playerForced) return true;
            if (job.jobGiver is JobGiver_TakeDrugsForDrugPolicy)
                return pawn.drugs?.AllowedToTakeScheduledNow(drug.def) == true;
            if (job.jobGiver is JobGiver_SatisfyChemicalNeed || job.jobGiver is JobGiver_SatifyChemicalDependency)
                return AddictionAllowed(pawn, drug);
            return job.jobGiver is JobGiver_GetJoy && pawn.drugs?.CurrentPolicy?[drug.def]?.allowedForJoy == true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    internal static class MetabolicDoseCommitGate
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Thing __instance, Pawn ingester, ref float __result)
        {
            if (MetabolicDrugIntake.AuthorizedNow(ingester, __instance)) return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.BestFoodInInventory))]
    internal static class MetabolicInventoryNutritionGate
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.IsNutritionGivingIngestible));
            var guarded = AccessTools.Method(typeof(MetabolicInventoryNutritionGate), nameof(CanSupplyNutrition));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = guarded; }
                yield return instruction;
            }
        }
        private static bool CanSupplyNutrition(ThingDef def)
            => def.IsNutritionGivingIngestible && !MetabolicDrugIntake.IsMetabolicDrug(def);
    }

    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.FinalizeIngest))]
    internal static class MetabolicDoseFinalizeGate
    {
        private static void Postfix(Pawn ingester, TargetIndex ingestibleInd, Toil __result)
        {
            var original = __result.initAction;
            __result.initAction = delegate
            {
                Pawn actor = __result.actor;
                Thing food = actor.CurJob.GetTarget(ingestibleInd).Thing;
                if (!MetabolicDrugIntake.AuthorizedNow(ingester, food))
                { actor.jobs.curDriver.EndJobWith(JobCondition.Incompletable); return; }
                original();
            };
        }
    }

    [HarmonyPatch(typeof(Pawn_DrugPolicyTracker), nameof(Pawn_DrugPolicyTracker.ShouldTryToTakeScheduledNow))]
    internal static class MetabolicScheduledDoseGate
    {
        private static void Postfix(Pawn_DrugPolicyTracker __instance, ThingDef ingestible, ref bool __result)
        { if (__result && MetabolicDrugIntake.IsMetabolicDrug(ingestible)) __result = MetabolicDrugIntake.FitsDose(__instance.pawn, ingestible); }
    }

    [HarmonyPatch(typeof(JoyGiver_Ingest), "CanIngestForJoy")]
    internal static class MetabolicRecreationDoseGate
    {
        private static void Postfix(Pawn pawn, Thing t, ref bool __result)
        {
            if (__result && MetabolicDrugIntake.IsMetabolicDrug(t.def))
                __result = pawn.drugs?.CurrentPolicy?[t.def]?.allowedForJoy == true && MetabolicDrugIntake.FitsDose(pawn, t);
        }
    }

    [HarmonyPatch(typeof(JobGiver_SatisfyChemicalNeed), "DrugValidator")]
    internal static class MetabolicAddictionDoseGate
    {
        private static void Postfix(Pawn pawn, Thing drug, ref bool __result)
        {
            if (__result && MetabolicDrugIntake.IsMetabolicDrug(drug.def))
                __result = MetabolicDrugIntake.AddictionAllowed(pawn, drug) && MetabolicDrugIntake.FitsDose(pawn, drug);
        }
    }

    [HarmonyPatch(typeof(JobGiver_SatifyChemicalDependency), "DrugValidator")]
    internal static class MetabolicDependencyDoseGate
    {
        private static void Postfix(Pawn pawn, Thing drug, ref bool __result)
        {
            if (__result && MetabolicDrugIntake.IsMetabolicDrug(drug.def))
                __result = MetabolicDrugIntake.AddictionAllowed(pawn, drug) && MetabolicDrugIntake.FitsDose(pawn, drug);
        }
    }

    [HarmonyPatch(typeof(CaravanInventoryUtility), nameof(CaravanInventoryUtility.TryGetDrugToSatisfyChemicalNeed))]
    internal static class MetabolicCaravanDrugSelectionGate
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.IngestibleNow));
            var guarded = AccessTools.Method(typeof(MetabolicCaravanDrugSelectionGate), nameof(CanSelect));
            bool found = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(native))
                {
                    // Keep the native inventory order and chemical checks; skip
                    // an unauthorized/oversized dose before it blocks another.
                    yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(instruction);
                    instruction.opcode = OpCodes.Call; instruction.operand = guarded; found = true;
                }
                yield return instruction;
            }
            if (!found) throw new InvalidOperationException("Native caravan drug-selection boundary was not found.");
        }
        private static bool CanSelect(Thing drug, Pawn pawn) => drug.IngestibleNow &&
            (!MetabolicDrugIntake.IsMetabolicDrug(drug.def) ||
             (MetabolicDrugIntake.AddictionAllowed(pawn, drug) && MetabolicDrugIntake.FitsDose(pawn, drug)));
    }

    [HarmonyPatch(typeof(Bill_Medical), nameof(Bill.ShouldDoNow))]
    internal static class MetabolicClinicalBillSelectionGate
    {
        private static void Postfix(Bill_Medical __instance, ref bool __result)
        {
            if (__result) __result = MetabolicDrugIntake.ClinicalBillFits(__instance, __instance.billStack?.billGiver as Pawn);
        }
    }

    [HarmonyPatch(typeof(JobDriver_DoBill), "MakeNewToils")]
    internal static class MetabolicClinicalJobGate
    {
        private static void Postfix(JobDriver_DoBill __instance, ref IEnumerable<Toil> __result)
        { __result = Guard(__result, __instance); }
        private static IEnumerable<Toil> Guard(IEnumerable<Toil> toils, JobDriver_DoBill driver)
        {
            foreach (Toil toil in toils)
            {
                // Check before native ingredient splitting and bill completion.
                // Cleanup retains the original placed/carried dose on failure.
                toil.AddFailCondition(() => !MetabolicDrugIntake.ClinicalBillFits(driver.job?.bill, driver.BillGiver as Pawn));
                yield return toil;
            }
        }
    }

    [HarmonyPatch(typeof(Recipe_AdministerIngestible), nameof(Recipe_AdministerIngestible.ApplyOnPawn))]
    internal static class MetabolicClinicalDoseGate
    {
        private static bool Prefix(Recipe_AdministerIngestible __instance, Pawn pawn, List<Thing> ingredients, Bill bill,
            out Tuple<Pawn, Thing> __state)
        {
            __state = Tuple.Create(MetabolicDrugIntake.ClinicalPawn, MetabolicDrugIntake.ClinicalDrug);
            Thing drug = ingredients != null && ingredients.Count > 0 ? ingredients[0] : null;
            if (!MetabolicDrugIntake.IsMetabolicDrug(drug?.def)) return true;
            if (!(bill is Bill_Medical) || bill.billStack?.billGiver != pawn || bill.recipe != __instance.recipe || !bill.ShouldDoNow() ||
                !MetabolicDrugIntake.FitsDose(pawn, drug)) return false;
            MetabolicDrugIntake.ClinicalPawn = pawn;
            MetabolicDrugIntake.ClinicalDrug = drug;
            return true;
        }
        private static Exception Finalizer(Exception __exception, Tuple<Pawn, Thing> __state)
        {
            if (__state != null)
            { MetabolicDrugIntake.ClinicalPawn = __state.Item1; MetabolicDrugIntake.ClinicalDrug = __state.Item2; }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Caravan_NeedsTracker), nameof(Caravan_NeedsTracker.IngestDrug))]
    internal static class MetabolicCaravanDoseGate
    {
        private static bool Prefix(Pawn pawn, Thing drug, out Tuple<Pawn, Thing> __state)
        {
            __state = Tuple.Create(MetabolicDrugIntake.CaravanPawn, MetabolicDrugIntake.CaravanDrug);
            if (!MetabolicDrugIntake.IsMetabolicDrug(drug?.def)) return true;
            if (!MetabolicDrugIntake.AddictionAllowed(pawn, drug) || !MetabolicDrugIntake.FitsDose(pawn, drug)) return false;
            MetabolicDrugIntake.CaravanPawn = pawn; MetabolicDrugIntake.CaravanDrug = drug;
            return true;
        }
        private static Exception Finalizer(Exception __exception, Tuple<Pawn, Thing> __state)
        {
            if (__state != null)
            { MetabolicDrugIntake.CaravanPawn = __state.Item1; MetabolicDrugIntake.CaravanDrug = __state.Item2; }
            return __exception;
        }
    }
}
