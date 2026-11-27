using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Oinky.JunkFood
{
    public class JunkFoodExtension : DefModExtension, IOrdinaryFoodServingUnits
    {
        public string family;
        public ThingDef portionDef;
        public int portions;
        public List<ThingDef> importedIngredients;
        public double ServingUnitsPerItem => portionDef != null && portions > 0 ? portions : 1d;
    }

    public static class JunkFoodPolicy
    {
        public static JunkFoodGameComponent Colony => Current.Game?.GetComponent<JunkFoodGameComponent>();
        public static bool Enabled => Colony == null || Colony.ContentEnabled;
        public static bool CravingsEnabled => Enabled && Colony?.CravingsEnabled == true;
        public static bool IsJunk(ThingDef def) => def?.GetModExtension<JunkFoodExtension>() != null;
        public static bool IsJunkRecipe(RecipeDef recipe) =>
            recipe?.products != null && recipe.products.Any(p => IsJunk(p.thingDef));
        public static bool AllowsRecipe(RecipeDef recipe) =>
            recipe?.GetModExtension<LegacyJunkFoodRecipeExtension>() == null && (Enabled || !IsJunkRecipe(recipe));
        public static bool EligibleAdult(Pawn pawn) => pawn != null && !pawn.Dead &&
            pawn.RaceProps.Humanlike && pawn.DevelopmentalStage == DevelopmentalStage.Adult &&
            pawn.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel == true;

        public static void SetImportedIngredients(Thing food)
        {
            var extension = food?.def.GetModExtension<JunkFoodExtension>();
            var ingredients = food?.TryGetComp<CompIngredients>();
            if (extension == null || ingredients == null || ingredients.ingredients.Count > 0) return;
            // Called only for newly generated imported stock. Never overwrite an
            // existing recipe's ingredients or touch age, poison or infection.
            if (extension.importedIngredients == null || extension.importedIngredients.Count == 0)
            {
                Log.ErrorOnce("Junk Food: imported ingredient baseline is missing for " + food.def.defName, 619411);
                return;
            }
            foreach (ThingDef def in extension.importedIngredients)
                if (def != null) ingredients.RegisterIngredient(def);
        }
    }

    public sealed class JunkFoodMod : Mod
    {
        public JunkFoodMod(ModContentPack content) : base(content) { }
        public override string SettingsCategory() => "Junk Food";
        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            JunkFoodGameComponent colony = JunkFoodPolicy.Colony;
            if (colony == null)
                listing.Label("Load a colony to change its Junk Food settings. These choices are saved with that colony.");
            else
            {
                bool enabled = colony.ContentEnabled;
                bool cravings = colony.CravingsEnabled;
                bool shared = colony.SharedMode;
                listing.CheckboxLabeled("Enable Junk Food", ref enabled);
                listing.Label("Turning this off stops new production, trader additions, cargo and junk-food recreation. Existing food and bills remain; existing stock can still be eaten or opened, and eaten calories are retained.");
                listing.Gap();
                bool previousGui = GUI.enabled;
                GUI.enabled = previousGui && enabled;
                listing.CheckboxLabeled("Enable mild cravings", ref cravings);
                GUI.enabled = previousGui;
                listing.CheckboxLabeled("Use shared Junk Food addiction and snack policy", ref shared);
                listing.Label(shared
                    ? "One shared treat habit, mild -1/-2/-3 cravings, a Junk food drug-policy row and occasional junk-only binges. Extra snacks add ordinary calories beyond meals. Turning cravings off clears the habit and binge, while recreation, schedules and modest chemical-interest satisfaction remain. Any useful bite delays the four active days needed to lose a habit."
                    : "Legacy mode: regular adult consumption can create a mild -2 craving. Saved legacy eating history is preserved. Switching modes clears the outgoing habit and starts fresh; food, calories and saved extra-snack policy choices remain.");
                listing.Label("The shared mode is opt-in for this colony. Meals and feeding still follow food restrictions. Turning off extra-use permissions does not ban ordinary junk-food meals.");
                if (shared != colony.SharedMode) colony.SetSharedMode(shared);
                if (enabled != colony.ContentEnabled || cravings != colony.CravingsEnabled)
                    colony.SetSettings(enabled, cravings);
            }
            listing.End();
        }
    }

    [StaticConstructorOnStartup]
    public static class JunkFoodBootstrap
    {
        static JunkFoodBootstrap()
        {
            new Harmony("Oinky.JunkFood.1.6").PatchAll(typeof(JunkFoodBootstrap).Assembly);
            EnsureSubscribed();
        }

        public static void EnsureSubscribed()
        {
            // The accepted companion assigns this delegate during its startup.
            // FinalizeInit reattaches us after all startup constructors, regardless
            // of their order. Remove only our own handler, never other subscribers.
            OrdinaryFoodIntake.UsefulIntakeCommitted -= OnUsefulIntake;
            OrdinaryFoodIntake.UsefulIntakeCommitted += OnUsefulIntake;
            OrdinaryFoodIntake.UsefulIntakeSnapshotCommitted -= OnUsefulSnapshot;
            OrdinaryFoodIntake.UsefulIntakeSnapshotCommitted += OnUsefulSnapshot;
            OrdinaryFoodIntake.SupplementalBudgetProviders -= JunkSnackUtility.BudgetFor;
            OrdinaryFoodIntake.SupplementalBudgetProviders += JunkSnackUtility.BudgetFor;
        }

        private static void OnUsefulSnapshot(OrdinaryIntakeCommit commit)
        { JunkFoodPolicy.Colony?.RecordShared(commit); }

        private static void OnUsefulIntake(Pawn pawn, Thing food, double nutrition, double volume)
        {
            if (JunkSharedRules.Mode || !JunkFoodPolicy.CravingsEnabled || !JunkFoodPolicy.IsJunk(food?.def) ||
                !JunkFoodPolicy.EligibleAdult(pawn)) return;
            try { JunkFoodPolicy.Colony?.Record(pawn, nutrition); }
            catch (Exception error)
            {
                // This observer must not interrupt the food-accounting owner or
                // another subscriber after nutrition has already committed.
                Log.ErrorOnce("Junk Food craving observer failed: " + error, 619412);
            }
        }
    }

    [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AvailableNow), MethodType.Getter)]
    internal static class JunkRecipeAvailability
    {
        private static void Postfix(RecipeDef __instance, ref bool __result)
        { if (__result && !JunkFoodPolicy.AllowsRecipe(__instance)) __result = false; }
    }

    [HarmonyPatch]
    internal static class JunkBillAvailability
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Bill_Production), nameof(Bill.ShouldDoNow));
            yield return AccessTools.Method(typeof(Bill_Medical), nameof(Bill.ShouldDoNow));
        }
        private static void Postfix(Bill __instance, ref bool __result)
        { if (__result && !JunkFoodPolicy.AllowsRecipe(__instance.recipe)) __result = false; }
    }

    [HarmonyPatch(typeof(JobDriver_DoBill), "MakeNewToils")]
    internal static class JunkRunningBillGate
    {
        private static void Postfix(JobDriver_DoBill __instance, ref IEnumerable<Toil> __result)
        {
            RecipeDef recipe = __instance.job?.bill?.recipe;
            if (JunkFoodPolicy.IsJunkRecipe(recipe) || recipe?.GetModExtension<LegacyJunkFoodRecipeExtension>() != null)
                __result = Guard(__result, __instance);
        }
        private static IEnumerable<Toil> Guard(IEnumerable<Toil> source, JobDriver_DoBill driver)
        {
            foreach (Toil toil in source)
            {
                toil.AddFailCondition(() => !JunkFoodPolicy.AllowsRecipe(driver.job?.bill?.recipe));
                Action original = toil.initAction;
                toil.initAction = () =>
                {
                    // In particular, guard a finish toil's init action before it
                    // consumes ingredients. Native job cleanup returns materials.
                    if (!JunkFoodPolicy.AllowsRecipe(driver.job?.bill?.recipe))
                    { driver.pawn.jobs.EndCurrentJob(JobCondition.Incompletable); return; }
                    original?.Invoke();
                };
                yield return toil;
            }
        }
    }

    [HarmonyPatch(typeof(OrdinaryFoodIntake), nameof(OrdinaryFoodIntake.IsProcessableFoodDef))]
    internal static class JunkProcessorExclusion
    {
        private static void Postfix(ThingDef def, ref bool __result)
        { if (__result && JunkFoodPolicy.IsJunk(def)) __result = false; }
    }

    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.PlayerAcquirable), MethodType.Getter)]
    internal static class JunkNewAcquisitionGate
    {
        private static void Postfix(ThingDef __instance, ref bool __result)
        { if (__result && !JunkFoodPolicy.Enabled && JunkFoodPolicy.IsJunk(__instance)) __result = false; }
    }

    [HarmonyPatch(typeof(ThingSetMaker), nameof(ThingSetMaker.Generate), new[] { typeof(ThingSetMakerParams) })]
    internal static class JunkGeneratedStockGate
    {
        private static void Postfix(List<Thing> __result)
        {
            if (__result == null) return;
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                Thing item = __result[i];
                if (!JunkFoodPolicy.IsJunk(item?.def)) continue;
                if (JunkFoodPolicy.Enabled) JunkFoodPolicy.SetImportedIngredients(item);
                else
                {
                    // This callback owns newly generated stock/rewards only.
                    // Never scan or delete existing inventory on a toggle.
                    __result.RemoveAt(i);
                    item.Destroy(DestroyMode.Vanish);
                }
            }
        }
    }

    public class JoyGiver_JunkFood : JoyGiver_Ingest
    {
        public override Job TryGiveJob(Pawn pawn) => JunkSharedRules.Mode
            ? JunkSnackUtility.TryJob(pawn, JunkSnackPurpose.Recreation) : base.TryGiveJob(pawn);
        public override Job TryGiveJobInGatheringArea(Pawn pawn, IntVec3 gatheringSpot, float maxRadius = -1f)
        {
            if (!JunkSharedRules.Mode) return base.TryGiveJobInGatheringArea(pawn, gatheringSpot, maxRadius);
            return JunkSnackUtility.TryJob(pawn, JunkSnackPurpose.Recreation, t => !t.Spawned ||
                (GatheringsUtility.InGatheringArea(t.Position, gatheringSpot, pawn.Map) &&
                 (maxRadius < 0f || t.Position.InHorDistOf(gatheringSpot, maxRadius))));
        }
        public override float GetChance(Pawn pawn)
        {
            if (!JunkSharedRules.Mode) return base.GetChance(pawn);
            int degree = JunkSharedRules.TraitDegree(pawn);
            return def.baseChance * (degree == 2 ? 5f : degree == 1 ? 2f : 1f);
        }
        protected override bool CanIngestForJoy(Pawn pawn, Thing food)
        {
            if (!JunkFoodPolicy.Enabled || !JunkFoodPolicy.IsJunk(food?.def) ||
                !base.CanIngestForJoy(pawn, food)) return false;
            if (!OrdinaryFoodIntake.Applies(pawn, food)) return true;
            MealPortionPlan plan;
            return OrdinaryFoodIntake.TryPreview(pawn, food, 1, out plan) && plan.UsefulNutrition > 0;
        }
    }

    [HarmonyPatch(typeof(JoyGiver_Ingest), "CanIngestForJoy")]
    internal static class JunkOtherRecreationGate
    {
        private static void Postfix(JoyGiver_Ingest __instance, Thing t, ref bool __result)
        {
            if (!__result || !JunkFoodPolicy.IsJunk(t?.def)) return;
            if (!JunkFoodPolicy.Enabled || (JunkSharedRules.Mode && !(__instance is JoyGiver_JunkFood))) __result = false;
        }
    }
}
