using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    // Categories are presentation. These presets do not authorize a drug dose
    // or change the goal/ingestion owner, which must still check both policies.
    public static class ItemPolicyDefaults
    {
        [ThreadStatic]
        internal static bool CreatingDrugPolicyFromDef;
        private static readonly HashSet<string> DrugNames = new HashSet<string>
        {
            "fatpill", "cakeFat", "swellGlowOriginal", "swellGlowPlus",
            "swellGlowX", "swellGlowMAX"
        };
        private static readonly HashSet<string> EnrichedNames = new HashSet<string>
        {
            "FatBurger", "Donut", "RR_CrammedCroissant", "RR_MechaniteMuffin",
            "RR_PackedPizza", "RR_LaBombe", "RR_GluttoniumMeal",
            "RR_GluttoniumMealLarge", "RR_GluttoniumMealHuge",
            "RR_GluttoniumMealEnormous", "RR_GluttoniumMealMassive",
            "RR_GluttoniumMealColossal", "RR_AlphaBall", "RR_OmegaBall",
            "SG_Calomilk", "SG_CaloyakMeat"
        };
        private static readonly HashSet<string> ExceptionalNames = new HashSet<string>
        {
            "RR_AlphaBall", "RR_OmegaBall", "RR_LaBombe", "RR_GluttoniumMeal",
            "RR_GluttoniumMealLarge", "RR_GluttoniumMealHuge",
            "RR_GluttoniumMealEnormous", "RR_GluttoniumMealMassive",
            "RR_GluttoniumMealColossal", "swellGlowX", "swellGlowMAX"
        };
        private static readonly FieldInfo NamedIngredients =
            AccessTools.Field(typeof(ThingFilter), "thingDefs");
        private static readonly FieldInfo DeclaredIngredientCategories =
            AccessTools.Field(typeof(ThingFilter), "categories");

        public static bool IsMetabolicDrug(ThingDef def)
        {
            return def != null && DrugNames.Contains(def.defName);
        }

        public static bool IsSpecialistIngredient(ThingDef def)
        {
            return def != null && (DrugNames.Contains(def.defName) ||
                EnrichedNames.Contains(def.defName) || def.defName == "RR_Gluttonium");
        }

        public static void InitializeDiet(FoodPolicy policy, bool allowEnriched)
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.IsDrug || IsMetabolicDrug(def) ||
                    def.defName == "RR_Gluttonium" || ExceptionalNames.Contains(def.defName) ||
                    (!allowEnriched && EnrichedNames.Contains(def.defName)))
                {
                    policy.filter.SetAllow(def, false);
                }
                else if (allowEnriched && EnrichedNames.Contains(def.defName))
                {
                    policy.filter.SetAllow(def, true);
                }
            }
        }

        public static void InitializeDrugs(DrugPolicy policy)
        {
            for (int i = 0; i < policy.Count; i++)
            {
                DrugPolicyEntry entry = policy[i];
                if (!IsMetabolicDrug(entry.drug)) continue;
                entry.allowedForAddiction = false;
                entry.allowedForJoy = false;
                entry.allowScheduled = false;
                entry.takeToInventory = 0;
            }
        }

        public static void InitializeCookingBill(Bill bill)
        {
            RecipeDef recipe = bill.recipe;
            if (recipe == null || recipe.workSkill != SkillDefOf.Cooking ||
                bill.ingredientFilter == null) return;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!(def.IsDrug || IsSpecialistIngredient(def))) continue;
                // A narrow category is not a named ingredient. Use the original
                // XML thingDefs list retained by the native filter, not just the
                // resolved allow-count (which may change when mods are enabled).
                bool explicitlyRequired = recipe.ingredients.Any(ingredient =>
                {
                    var named = NamedIngredients == null ? null :
                        NamedIngredients.GetValue(ingredient.filter) as List<ThingDef>;
                    if (named != null && named.Contains(def)) return true;
                    // The shipped specialist recipes explicitly request this
                    // category after their integration patches. Broad MeatRaw
                    // or AnimalProductRaw inputs do not imply specialist opt-in.
                    var categories = DeclaredIngredientCategories == null ? null :
                        DeclaredIngredientCategories.GetValue(ingredient.filter) as List<string>;
                    return !def.IsDrug && !IsMetabolicDrug(def) &&
                        categories != null && categories.Contains("CaloriteIngredients") &&
                        ingredient.filter.Allows(def);
                });
                if (!explicitlyRequired) bill.ingredientFilter.SetAllow(def, false);
            }
        }
    }

    public sealed class ItemPolicyDefaultsGameComponent : GameComponent
    {
        public ItemPolicyDefaultsGameComponent(Game game) { }

        public override void StartedNewGame()
        {
            FoodRestrictionDatabase database = Current.Game.foodRestrictionDatabase;
            FoodPolicy previousDefault = database.DefaultFoodRestriction();
            FoodPolicy everyday = database.MakeNewFoodRestriction();
            everyday.label = "RimRound everyday (no enriched food or drugs)";
            everyday.CopyFrom(previousDefault);
            ItemPolicyDefaults.InitializeDiet(everyday, false);
            FoodPolicy enriched = database.MakeNewFoodRestriction();
            enriched.label = "RimRound enriched (no drugs or exceptional products)";
            enriched.CopyFrom(previousDefault);
            ItemPolicyDefaults.InitializeDiet(enriched, true);
            database.SetDefault(everyday);

            DrugPolicyDatabase drugs = Current.Game.drugPolicyDatabase;
            DrugPolicy previousDrugDefault = drugs.DefaultDrugPolicy();
            DrugPolicy safeDrugs = drugs.MakeNewDrugPolicy();
            safeDrugs.CopyFrom(previousDrugDefault);
            safeDrugs.sourceDef = null;
            safeDrugs.label = "RimRound metabolic drugs (manual authorization)";
            ItemPolicyDefaults.InitializeDrugs(safeDrugs);
            drugs.SetDefault(safeDrugs);

            // Only starting colonists still using the former default move to
            // the fresh default. Explicit alternative/custom policies survive.
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
            {
                if (!pawn.IsColonist || pawn.Faction != Faction.OfPlayer) continue;
                if (pawn.foodRestriction != null &&
                    pawn.foodRestriction.CurrentFoodPolicy == previousDefault)
                    pawn.foodRestriction.CurrentFoodPolicy = everyday;
                if (pawn.drugs != null && pawn.drugs.CurrentPolicy == previousDrugDefault)
                    pawn.drugs.CurrentPolicy = safeDrugs;
            }
        }
    }

    [HarmonyPatch(typeof(DrugPolicy), MethodType.Constructor,
        new Type[] { typeof(int), typeof(string) })]
    public static class ItemPolicyDefaultsNewDrugPolicyPatch
    {
        public static void Postfix(DrugPolicy __instance)
        {
            if (!ItemPolicyDefaults.CreatingDrugPolicyFromDef)
                ItemPolicyDefaults.InitializeDrugs(__instance);
        }
    }

    [HarmonyPatch(typeof(DrugPolicyDatabase), nameof(DrugPolicyDatabase.NewDrugPolicyFromDef))]
    public static class ItemPolicyDefaultsDrugPolicyDefPatch
    {
        // Preserve every native sourceDef policy, including Unrestricted's
        // constructor addiction defaults. Labels/localization are irrelevant.
        public static void Prefix(out bool __state)
        {
            __state = ItemPolicyDefaults.CreatingDrugPolicyFromDef;
            ItemPolicyDefaults.CreatingDrugPolicyFromDef = true;
        }

        public static Exception Finalizer(Exception __exception, bool __state)
        {
            ItemPolicyDefaults.CreatingDrugPolicyFromDef = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Bill), MethodType.Constructor,
        new Type[] { typeof(RecipeDef), typeof(Precept_ThingStyle) })]
    public static class ItemPolicyDefaultsNewCookingBillPatch
    {
        public static void Postfix(Bill __instance)
        {
            ItemPolicyDefaults.InitializeCookingBill(__instance);
        }
    }
}
