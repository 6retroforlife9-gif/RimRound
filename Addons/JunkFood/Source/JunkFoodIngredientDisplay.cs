using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Oinky.JunkFood
{
    // Nutrition remains the recipe's accounting unit. Only presentation uses
    // the same whole-item requirements as the native non-mixing bill search.
    [StaticConstructorOnStartup]
    internal static class JunkFoodIngredientDisplay
    {
        private static readonly Dictionary<IngredientCount, RecipeDef> Owners =
            DefDatabase<RecipeDef>.AllDefsListForReading.Where(Applies)
                .SelectMany(r => r.ingredients.Select(i => new { Ingredient = i, Recipe = r }))
                .GroupBy(p => p.Ingredient).ToDictionary(g => g.Key, g => g.First().Recipe);
        private static readonly Action<IEnumerable<TextureAndColor>, bool, float?, ThingDef> NativeRow =
            AccessTools.MethodDelegate<Action<IEnumerable<TextureAndColor>, bool, float?, ThingDef>>(
                AccessTools.Method(typeof(BillUtility), "DisplayIngredientIconRow"));
        private static readonly FieldInfo LayoutField = AccessTools.Field(typeof(BillUtility), "recipeTooltipLayout");

        internal static bool Applies(RecipeDef recipe) => JunkFoodPolicy.IsJunkRecipe(recipe) &&
            !recipe.allowMixingIngredients && recipe.IngredientValueGetter.GetType() == typeof(IngredientValueGetter_Nutrition);

        private static IEnumerable<ThingDef> Allowed(IngredientCount ingredient, RecipeDef recipe) =>
            ingredient.filter.AllowedThingDefs.Where(d =>
                (ingredient.IsFixedIngredient && ingredient.filter.Allows(d) || recipe.fixedIngredientFilter.AllowedDefCount == 0 ||
                 recipe.fixedIngredientFilter.Allows(d) && !recipe.fixedIngredientFilter.IsAlwaysDisallowedDueToSpecialFilters(d)) &&
                recipe.IngredientValueGetter.ValuePerUnitOf(d) > 0f);

        internal static List<IGrouping<int, ThingDef>> Groups(IngredientCount ingredient, RecipeDef recipe, bool iconsOnly = false) =>
            Allowed(ingredient, recipe).Where(d => !iconsOnly || Widgets.GetIconFor(d) != BaseContent.BadTex)
                .GroupBy(d => ingredient.CountRequiredOfFor(d, recipe)).OrderByDescending(g => g.Key).ToList();

        private static string Names(IEnumerable<ThingDef> defs)
        {
            var list = defs.ToList();
            var labels = new List<string>();
            // Avoid listing every animal's meat in a bill's narrow description.
            if (list.Count(d => d.IsMeat) > 3)
            {
                labels.Add("allowed meats");
                list.RemoveAll(d => d.IsMeat);
            }
            labels.AddRange(list.OrderBy(d => d.label).ThenBy(d => d.defName).Select(d => d.LabelCap.ToString()));
            return string.Join(" / ", labels);
        }

        internal static bool TryDescribe(IngredientCount ingredient, RecipeDef recipe, out string description)
        {
            description = null;
            if (recipe == null && !Owners.TryGetValue(ingredient, out recipe)) return false;
            if (!Applies(recipe)) return false;
            var groups = Groups(ingredient, recipe);
            if (groups.Count == 0) return false;
            description = string.Join(" or ", groups.Select(g => g.Key + "x " + Names(g)));
            return true;
        }

        internal static void DrawRows(IEnumerable<TextureAndColor> icons, bool draw, float? count,
            ThingDef singleIngredient, IngredientCount ingredient, RecipeDef recipe)
        {
            if (!Applies(recipe))
            {
                NativeRow(icons, draw, count, singleIngredient);
                return;
            }
            var groups = Groups(ingredient, recipe, iconsOnly: true);
            if (groups.Count == 0)
            {
                NativeRow(icons, draw, count, singleIngredient);
                return;
            }
            var layout = (RecipeTooltipLayout)LayoutField.GetValue(null);
            for (int i = 0; i < groups.Count; i++)
            {
                if (i > 0)
                {
                    layout.Newline();
                    layout.Gap(8f, 0f);
                    layout.Label("or ", draw);
                }
                var options = groups[i].ToList();
                NativeRow(options.Select(d => new TextureAndColor(Widgets.GetIconFor(d), d.uiIconColor)).Distinct(), draw,
                    groups[i].Key, options.Count == 1 ? options[0] : null);
            }
        }
    }

    [HarmonyPatch(typeof(IngredientCount), nameof(IngredientCount.CountFor))]
    internal static class JunkFoodWholeItemCountDisplay
    {
        private static void Postfix(RecipeDef recipe, ref float __result)
        {
            if (JunkFoodIngredientDisplay.Applies(recipe) && __result > 0f && !float.IsInfinity(__result))
                __result = UnityEngine.Mathf.CeilToInt(__result);
        }
    }

    [HarmonyPatch(typeof(IngredientCount), nameof(IngredientCount.SummaryFor))]
    internal static class JunkFoodIngredientRecipeSummary
    {
        private static bool Prefix(IngredientCount __instance, RecipeDef recipe, ref string __result)
        {
            if (!JunkFoodIngredientDisplay.TryDescribe(__instance, recipe, out string text)) return true;
            __result = text;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class JunkFoodIngredientOwnedSummary
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(IngredientCount), nameof(IngredientCount.Summary));
            yield return AccessTools.PropertyGetter(typeof(IngredientCount), nameof(IngredientCount.SummaryFilterFirst));
        }
        private static bool Prefix(IngredientCount __instance, ref string __result)
        {
            if (!JunkFoodIngredientDisplay.TryDescribe(__instance, null, out string text)) return true;
            __result = text;
            return false;
        }
    }

    [HarmonyPatch(typeof(IngredientValueGetter_Nutrition), nameof(IngredientValueGetter_Nutrition.BillRequirementsDescription))]
    internal static class JunkFoodIngredientBillDescription
    {
        private static bool Prefix(RecipeDef r, IngredientCount ing, ref string __result)
        {
            if (!JunkFoodIngredientDisplay.TryDescribe(ing, r, out string text)) return true;
            __result = text;
            return false;
        }
    }

    [HarmonyPatch(typeof(BillUtility), "LayoutTooltip")]
    internal static class JunkFoodIngredientTooltipRows
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var original = instructions.ToList();
            var countFor = AccessTools.Method(typeof(IngredientCount), nameof(IngredientCount.CountFor));
            var row = AccessTools.Method(typeof(BillUtility), "DisplayIngredientIconRow");
            var replacement = AccessTools.Method(typeof(JunkFoodIngredientDisplay), nameof(JunkFoodIngredientDisplay.DrawRows));
            if (original.Count(c => c.Calls(countFor)) != 1)
            {
                Log.Error("[Junk Food] Recipe ingredient tooltip layout changed; keeping the native layout.");
                return original;
            }
            var ingredient = generator.DeclareLocal(typeof(IngredientCount));
            var recipe = generator.DeclareLocal(typeof(RecipeDef));
            var output = new List<CodeInstruction>();
            bool pending = false;
            int replacements = 0;
            foreach (CodeInstruction instruction in original)
            {
                if (instruction.Calls(countFor))
                {
                    // The native call's stack already contains ingredient, recipe.
                    // Keep those references in locals, without any global UI state.
                    var first = new CodeInstruction(OpCodes.Stloc, recipe);
                    first.labels.AddRange(instruction.labels);
                    first.blocks.AddRange(instruction.blocks);
                    output.Add(first);
                    output.Add(new CodeInstruction(OpCodes.Dup));
                    output.Add(new CodeInstruction(OpCodes.Stloc, ingredient));
                    output.Add(new CodeInstruction(OpCodes.Ldloc, recipe));
                    output.Add(new CodeInstruction(instruction.opcode, instruction.operand));
                    pending = true;
                }
                else if (pending && instruction.Calls(row))
                {
                    var first = new CodeInstruction(OpCodes.Ldloc, ingredient);
                    first.labels.AddRange(instruction.labels);
                    first.blocks.AddRange(instruction.blocks);
                    output.Add(first);
                    output.Add(new CodeInstruction(OpCodes.Ldloc, recipe));
                    output.Add(new CodeInstruction(OpCodes.Call, replacement));
                    pending = false;
                    replacements++;
                }
                else output.Add(instruction);
            }
            if (replacements == 1) return output;
            Log.Error("[Junk Food] Recipe ingredient tooltip row changed; keeping the native layout.");
            return original;
        }
    }
}
