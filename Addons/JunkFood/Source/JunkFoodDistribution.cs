using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimRound.FeedOther;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Oinky.JunkFood
{
    public static class JunkFoodDistribution
    {
        private static List<ThingDef> catalogue;
        private static readonly HashSet<string> Drinks = new HashSet<string>
        { "soda", "fruit_soda", "icee", "chocolate_milk", "fruit_milk", "plant_milk", "bubble_tea" };
        private static readonly HashSet<string> Rich = new HashSet<string>
        { "cream_cake", "brownie", "large_chocolate", "freakshake", "frozen_dessert", "pizza_slice", "pizza_pocket", "mac_cheese" };

        public static string Family(ThingDef def) => def.GetModExtension<JunkFoodExtension>().family;
        public static int Group(ThingDef def) => Drinks.Contains(Family(def)) ? 1 : Rich.Contains(Family(def)) ? 2 : 0;

        public static IEnumerable<ThingDef> Available(float budget = float.MaxValue)
        {
            if (!JunkFoodPolicy.Enabled) return Enumerable.Empty<ThingDef>();
            if (catalogue == null)
                catalogue = DefDatabase<ThingDef>.AllDefsListForReading.Where(d =>
                    JunkFoodPolicy.IsJunk(d) && d.GetModExtension<JunkFoodExtension>().portionDef == null &&
                    !string.IsNullOrEmpty(Family(d)) && d.IsNutritionGivingIngestible &&
                    d.tradeability.TraderCanSell() && d.BaseMarketValue > 0 && !d.MadeFromStuff)
                    .OrderBy(d => d.defName, StringComparer.Ordinal).ToList();
            return catalogue.Where(d => d.PlayerAcquirable && d.BaseMarketValue <= budget);
        }

        public static ThingDef Choose(float budget = float.MaxValue) =>
            FamilySelection.Pick(Available(budget), Family, limit => Rand.Range(0, limit));

        public static IEnumerable<Thing> Make(ThingDef def, int count, Faction faction = null)
        {
            if (!JunkFoodPolicy.Enabled || def == null || count <= 0) yield break;
            // StockGeneratorUtility can return an oversized stack. Emit legal
            // stacks ourselves, including when used as resource-pod cargo.
            while (count > 0 && JunkFoodPolicy.Enabled)
            {
                int amount = Math.Min(count, Math.Max(1, def.stackLimit));
                foreach (Thing food in StockGeneratorUtility.TryMakeForStock(def, amount, faction))
                {
                    JunkFoodPolicy.SetImportedIngredients(food);
                    yield return food;
                }
                count -= amount;
            }
        }
    }

    public class StockGenerator_JunkFood : StockGenerator
    {
        public bool orbital;
        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            if (!JunkFoodPolicy.Enabled) yield break;
            List<ThingDef> choices = JunkFoodDistribution.Available().ToList();
            for (int group = 0; group < 3; group++)
            {
                int selectedGroup = group;
                var selected = FamilySelection.DistinctFamilies(
                    choices.Where(d => JunkFoodDistribution.Group(d) == selectedGroup), orbital ? 2 : 1,
                    JunkFoodDistribution.Family, limit => Rand.Range(0, limit));
                foreach (ThingDef def in selected)
                {
                    int count = orbital ? Rand.RangeInclusive(12, 24) : Rand.RangeInclusive(6, 12);
                    foreach (Thing food in JunkFoodDistribution.Make(def, count, faction)) yield return food;
                }
            }
        }

        // Traders may still buy retained food when new content is disabled.
        public override bool HandlesThingDef(ThingDef def) => JunkFoodPolicy.IsJunk(def);
    }

    [HarmonyPatch]
    internal static class JunkFoodGiftPool
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(FoodTraderUtility), "GiftChoices");
        private static void Postfix(IEnumerable<string> names, float budget, ref List<ThingDef> __result)
        {
            if (!JunkFoodPolicy.Enabled || !ReferenceEquals(names, FoodTraderUtility.JunkFoods)) return;
            // Add one grouped option, not 93 branded votes. The original gift
            // method still owns the same budget draw, drug choice, count and timing.
            ThingDef choice = JunkFoodDistribution.Choose(budget);
            if (choice != null && !__result.Contains(choice)) __result.Add(choice);
        }
    }

    [HarmonyPatch(typeof(FoodTraderUtility), nameof(FoodTraderUtility.GenerateGifts))]
    internal static class JunkFoodGiftIngredients
    {
        private static void Postfix(List<Thing> __result)
        {
            if (__result == null) return;
            // Native gift creation may exceed a food's stack limit. Split only
            // this newly generated result, keeping the native count/value budget.
            int originalStacks = __result.Count;
            for (int i = 0; i < originalStacks; i++)
            {
                Thing food = __result[i];
                if (!JunkFoodPolicy.IsJunk(food?.def)) continue;
                JunkFoodPolicy.SetImportedIngredients(food);
                int limit = Math.Max(1, food.def.stackLimit);
                while (food.stackCount > limit) __result.Add(food.SplitOff(limit));
            }
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_ResourcePodCrash), "TryExecuteWorker")]
    internal static class JunkCargoIncidentScope
    {
        [ThreadStatic] internal static bool RollAvailable;
        private static void Prefix(out bool __state)
        { __state = RollAvailable; RollAvailable = true; }
        private static Exception Finalizer(Exception __exception, bool __state)
        { RollAvailable = __state; return __exception; }
    }

    [HarmonyPatch(typeof(ThingSetMaker_ResourcePod), "Generate")]
    internal static class JunkCargoChoice
    {
        private static bool Prefix(List<Thing> outThings)
        {
            // Only the actual cargo-pod incident gets this one roll. Other uses
            // of resource-pod makers and ordinary quest rewards remain native.
            if (!JunkCargoIncidentScope.RollAvailable) return true;
            JunkCargoIncidentScope.RollAvailable = false;
            if (!JunkFoodPolicy.Enabled || !Rand.Chance(0.10f)) return true;
            float budget = Rand.Range(150f, 600f);
            ThingDef choice = JunkFoodDistribution.Choose(budget);
            if (choice == null) return true;
            int count = Math.Min(Math.Max(1, choice.stackLimit) * 7,
                Mathf.FloorToInt(budget / choice.BaseMarketValue));
            if (count <= 0) return true;
            outThings.AddRange(JunkFoodDistribution.Make(choice, count));
            return false;
        }
    }
}
