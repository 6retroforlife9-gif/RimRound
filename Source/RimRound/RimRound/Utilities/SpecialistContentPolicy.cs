using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.Utilities
{
    // Evaluate the saved colony choice at the point of use. Never rewrite shared
    // Defs or saved stock/filter choices when a colony switches its preset.
    public static class SpecialistContentPolicy
    {
        public const string FantasyReason = "Requires the colony's Fantasy preset. Existing items are retained.";
        private static readonly HashSet<string> FantasyItems = new HashSet<string>
        {
            "RR_AlphaBall", "RR_OmegaBall", "RR_GluttoniumMeal", "RR_GluttoniumMealLarge",
            "RR_GluttoniumMealHuge", "RR_GluttoniumMealEnormous", "RR_GluttoniumMealMassive",
            "RR_GluttoniumMealColossal", "RR_LaBombe", "swellGlowX", "swellGlowMAX",
            "RR_DefatteningPistol", "RR_FatteningPistol", "RR_FatteningPistol_Permanent", "RR_FlabGrenade",
            "RR_DefatteningBullet", "RR_FatteningBullet", "RR_FatteningBullet_Permanent", "RR_Proj_GrenadeFlab",
            "RR_ZenithOrb"
        };

        public static bool RequiresFantasy(ThingDef def) => def != null && FantasyItems.Contains(def.defName);
        public static bool Allows(ThingDef def) => def?.defName != "MineableFlesh" && (!RequiresFantasy(def) || ColonyProgression.FantasyActive);
        public static bool Allows(Thing thing)
        {
            if (thing == null) return true;
            if (thing is MinifiedThing minified) thing = minified.InnerThing;
            if (!Allows(thing.def)) return false;
            var ingredients = thing.TryGetComp<CompIngredients>()?.ingredients;
            if (ingredients != null)
                foreach (ThingDef ingredient in ingredients)
                    if (!Allows(ingredient)) return false;
            return true;
        }

        public static bool Allows(ResearchProjectDef research)
        {
            if (research == null || ColonyProgression.FantasyActive) return true;
            switch (research.defName)
            {
                case "RR_Research_GlutoniumMeals": case "RR_Research_ProjectAlpha": case "RR_Research_ProjectOmega":
                case "RR_Research_ExperimentalWeapons": case "RR_Research_ExperimentalGrenades":
                case "RR_Research_AdvancedExperimentalWeapons": case "RR_Research_ZenithOrb":
                case "RR_Research_ExperimentalGlutoniumDrugs": return false;
                default: return true;
            }
        }

        public static bool Allows(RecipeDef recipe)
        {
            if (recipe == null || ColonyProgression.FantasyActive) return true;
            if (recipe.defName == "RR_Replicate_Gluttonium") return false;
            if (recipe.products != null)
                foreach (ThingDefCountClass product in recipe.products)
                    if (!Allows(product.thingDef)) return false;
            // Administer recipes use a fixed required drug rather than products.
            if (recipe.IsSurgery && recipe.ingredients != null)
                foreach (IngredientCount ingredient in recipe.ingredients)
                {
                    bool anyAllowed = false;
                    foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                        if (Allows(def)) { anyAllowed = true; break; }
                    if (!anyAllowed) return false;
                }
            return true;
        }
    }

    [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AvailableNow), MethodType.Getter)]
    internal static class SpecialistRecipeAvailability
    {
        private static void Postfix(RecipeDef __instance, ref bool __result)
        { if (__result && !SpecialistContentPolicy.Allows(__instance)) __result = false; }
    }

    [HarmonyPatch]
    internal static class SpecialistBillAvailability
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Bill_Production), nameof(Bill.ShouldDoNow));
            yield return AccessTools.Method(typeof(Bill_Medical), nameof(Bill.ShouldDoNow));
        }
        private static void Postfix(Bill __instance, ref bool __result)
        { if (__result && !SpecialistContentPolicy.Allows(__instance.recipe)) __result = false; }
    }

    [HarmonyPatch(typeof(JobDriver_DoBill), "MakeNewToils")]
    internal static class SpecialistRunningBillGate
    {
        private static void Postfix(JobDriver_DoBill __instance, ref IEnumerable<Toil> __result)
        { __result = Guard(__result, __instance); }
        private static IEnumerable<Toil> Guard(IEnumerable<Toil> toils, JobDriver_DoBill driver)
        {
            foreach (Toil toil in toils)
            {
                // Abort before ingredient consumption/product creation on a mode
                // change; native job cleanup owns the real unfinished materials.
                toil.AddFailCondition(() => !SpecialistContentPolicy.Allows(driver.job?.bill?.recipe));
                yield return toil;
            }
        }
    }

    [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.CanStartNow), MethodType.Getter)]
    internal static class SpecialistResearchAvailability
    {
        private static void Postfix(ResearchProjectDef __instance, ref bool __result)
        { if (__result && !SpecialistContentPolicy.Allows(__instance)) __result = false; }
    }

    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.PlayerAcquirable), MethodType.Getter)]
    internal static class SpecialistAcquisitionAvailability
    {
        private static void Postfix(ThingDef __instance, ref bool __result)
        { if (Current.Game != null && __result && !SpecialistContentPolicy.Allows(__instance)) __result = false; }
    }

    [HarmonyPatch(typeof(ThingSetMaker), nameof(ThingSetMaker.Generate), new[] { typeof(ThingSetMakerParams) })]
    internal static class SpecialistGeneratedStockGate
    {
        private static void Postfix(List<Thing> __result)
        {
            if (Current.Game == null || ColonyProgression.FantasyActive) return;
            // These are newly generated, unowned stock/rewards, never existing
            // inventory. Retained items are not changed when Fantasy is disabled.
            for (int i = __result.Count - 1; i >= 0; i--)
                if (!SpecialistContentPolicy.Allows(__result[i]))
                {
                    Thing thing = __result[i];
                    __result.RemoveAt(i);
                    thing.Destroy(DestroyMode.Vanish);
                }
        }
    }

    [HarmonyPatch(typeof(PawnWeaponGenerator), nameof(PawnWeaponGenerator.TryGenerateWeaponFor))]
    internal static class SpecialistGeneratedWeaponGate
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var add = AccessTools.Method(typeof(List<ThingStuffPair>), nameof(List<ThingStuffPair>.Add));
            var guarded = AccessTools.Method(typeof(SpecialistGeneratedWeaponGate), nameof(AddAllowed));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(add)) { instruction.opcode = OpCodes.Call; instruction.operand = guarded; }
                yield return instruction;
            }
        }
        private static void AddAllowed(List<ThingStuffPair> choices, ThingStuffPair pair)
        { if (SpecialistContentPolicy.Allows(pair.thing)) choices.Add(pair); }
    }

    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanConstruct), new[] { typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool), typeof(JobDef) })]
    internal static class SpecialistConstructionGate
    {
        private static void Postfix(Thing t, ref bool __result)
        {
            if (__result && t?.def.entityDefToBuild is ThingDef def && !SpecialistContentPolicy.Allows(def))
            { __result = false; JobFailReason.Is(SpecialistContentPolicy.FantasyReason); }
        }
    }

    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.Visible), MethodType.Getter)]
    internal static class SpecialistBuildMenuGate
    {
        private static void Postfix(Designator_Build __instance, ref bool __result)
        { if (__result && __instance.PlacingDef is ThingDef def && !SpecialistContentPolicy.Allows(def)) __result = false; }
    }

    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.CanDesignateCell))]
    internal static class SpecialistBuildPlacementGate
    {
        private static void Postfix(Designator_Build __instance, ref AcceptanceReport __result)
        { if (__instance.PlacingDef is ThingDef def && !SpecialistContentPolicy.Allows(def)) __result = SpecialistContentPolicy.FantasyReason; }
    }

    [HarmonyPatch]
    internal static class SpecialistFoodSelectionGate
    {
        private static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(FoodUtility)).Where(m => m.Name == nameof(FoodUtility.WillEat));
        private static void Postfix(object food, ref bool __result)
        {
            if (!__result) return;
            if ((food is Thing thing && !SpecialistContentPolicy.Allows(thing)) ||
                (food is ThingDef def && !SpecialistContentPolicy.Allows(def))) __result = false;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class SpecialistWeaponUseGate
    {
        private static bool Prefix(Verb __instance, ref bool __result)
        {
            if (SpecialistContentPolicy.Allows(__instance.EquipmentSource?.def)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    internal static class SpecialistWeaponBurstGate
    {
        private static bool Prefix(Verb __instance)
        {
            if (SpecialistContentPolicy.Allows(__instance.EquipmentSource?.def)) return true;
            __instance.Reset();
            return false;
        }
    }

    // Final commit gate covers direct clinical ingestion and caravan callers too.
    // OrdinaryFoodIntake also checks before native meal thoughts are applied.
    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    internal static class SpecialistIngestionGate
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Thing __instance, ref float __result)
        {
            if (SpecialistContentPolicy.Allows(__instance)) return true;
            __result = 0f;
            return false;
        }
    }
}
