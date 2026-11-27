using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using HediffUtility = RimRound.Utilities.HediffUtility;

namespace RimRound.FeedOther
{
    // Explicit lines keep obsolete giant meals and internal network fluid out of retail stock.
    public class StockGenerator_RimRoundFood : StockGenerator
    {
        public ThingDef thingDef;
        public float chance = 1f;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            if (thingDef == null || !SpecialistContentPolicy.Allows(thingDef) || !Rand.Chance(chance))
                yield break;
            foreach (Thing item in StockGeneratorUtility.TryMakeForStock(thingDef, RandomCountOf(thingDef), faction))
            {
                FoodTraderUtility.SetImportedIngredients(item);
                yield return item;
            }
        }

        public override bool HandlesThingDef(ThingDef def) => def == thingDef;

        public override IEnumerable<string> ConfigErrors(TraderKindDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (thingDef == null) yield return "RimRound food stock line has no thingDef.";
            else if (!thingDef.tradeability.TraderCanSell())
                yield return thingDef.defName + " does not permit trader sales.";
            if (chance < 0f || chance > 1f) yield return "RimRound food stock chance must be between zero and one.";
        }
    }

    public class StockGenerator_RimRoundBuyFood : StockGenerator
    {
        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        { yield break; }

        public override bool HandlesThingDef(ThingDef def) =>
            def != null && def.IsNutritionGivingIngestible && !def.IsDrug && !def.IsCorpse &&
            def.BaseMarketValue > 0 && (def.tradeability == Tradeability.All || def.tradeability == Tradeability.Sellable) &&
            SpecialistContentPolicy.Allows(def);
    }

    public class FoodTraderPawnExtension : DefModExtension
    {
        public FloatRange startingMassKg;
    }

    public static class FoodTraderUtility
    {
        public const string Caravan = "RR_CaravanFoodSupplier";
        public const string Orbital = "RR_OrbitalFoodSupplier";
        public const string Carrier = "SG_Caloyak";
        public static readonly string[] JunkFoods =
        {
            "Donut", "FatBurger", "RR_CrammedCroissant", "RR_MechaniteMuffin",
            "RR_PackedPizza", "RR_LaBombe"
        };
        static readonly string[] GiftDrugs = { "fatpill", "swellGlowOriginal", "swellGlowPlus", "swellGlowX", "swellGlowMAX" };

        public static void SetImportedIngredients(Thing food)
        {
            if (!food.def.defName.StartsWith("RR_Dense", StringComparison.Ordinal)) return;
            CompIngredients ingredients = food.TryGetComp<CompIngredients>();
            if (ingredients == null) return;
            ingredients.ingredients.Clear();
            ingredients.RegisterIngredient(DefDatabase<ThingDef>.GetNamed("RawRice"));
            if (food.def.defName.StartsWith("RR_DenseFine", StringComparison.Ordinal))
                ingredients.RegisterIngredient(DefDatabase<ThingDef>.GetNamed("Meat_Muffalo"));
        }

        public static List<Thing> GenerateGifts(Map map)
        {
            FloatRange range = DiplomacyTuning.VisitorGiftTotalMarketValueRangeBase *
                DiplomacyTuning.VisitorGiftTotalMarketValueFactorFromPlayerWealthCurve.Evaluate(map.wealthWatcher.WealthTotal);
            float budget = range.RandomInRange;
            List<ThingDef> foods = GiftChoices(JunkFoods, budget);
            List<ThingDef> drugs = GiftChoices(GiftDrugs, budget);
            List<ThingDef> pool = drugs.Count > 0 && Rand.Chance(0.2f) ? drugs : foods;
            if (pool.Count == 0) pool = GiftChoices(new[] { "Chocolate" }, budget);
            if (pool.Count == 0) return new List<Thing>();
            ThingDef choice = pool.RandomElement();
            // The normal gift event owns chance and timing. Only its contents change.
            int count = Mathf.Max(1, Mathf.FloorToInt(budget / choice.BaseMarketValue));
            return StockGeneratorUtility.TryMakeForStock(choice, count, null).ToList();
        }

        static List<ThingDef> GiftChoices(IEnumerable<string> names, float budget) =>
            names.Select(n => DefDatabase<ThingDef>.GetNamedSilentFail(n))
                .Where(d => d != null && SpecialistContentPolicy.Allows(d) &&
                    d.BaseMarketValue > 0f && d.BaseMarketValue <= budget).ToList();
    }

    [HarmonyPatch(typeof(PawnGroupKindWorker_Trader), "GeneratePawns",
        new[] { typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(List<Pawn>), typeof(bool) })]
    internal static class FoodTraderGroup
    {
        static void Prefix(PawnGroupMakerParms parms, ref PawnGroupMaker groupMaker)
        {
            if (parms.traderKind?.defName != FoodTraderUtility.Caravan) return;
            // A per-call copy gives this supplier caloyaks without changing any
            // faction's shared carrier, merchant or guard lists.
            groupMaker = new PawnGroupMaker
            {
                kindDef = groupMaker.kindDef,
                commonality = groupMaker.commonality,
                maxTotalPoints = groupMaker.maxTotalPoints,
                disallowedStrategies = groupMaker.disallowedStrategies,
                options = groupMaker.options,
                carriers = new List<PawnGenOption> { Option(FoodTraderUtility.Carrier, 1f) },
                traders = new List<PawnGenOption> { Option("RR_FoodMerchant", 1f) },
                guards = new List<PawnGenOption>
                {
                    Option("RR_FoodGuard", 4f), Option("RR_FoodGuardElite", 1f)
                }
            };
        }

        static PawnGenOption Option(string name, float weight) =>
            new PawnGenOption { kind = DefDatabase<PawnKindDef>.GetNamed(name), selectionWeight = weight };
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.GetOptions))]
    internal static class FoodTraderGuardOptions
    {
        static void Prefix(PawnGroupMakerParms groupParms, List<PawnGenOption> options, ref float? maxCost)
        {
            if (groupParms?.traderKind?.defName != FoodTraderUtility.Caravan ||
                options == null || options.Count == 0 ||
                options.Any(o => o.kind.defName != "RR_FoodGuard" && o.kind.defName != "RR_FoodGuardElite"))
                return;
            // The outlander raid curve can reject every equipped escort at a
            // normal caravan budget. Admit these kinds, still paying their full
            // combat cost from the unchanged native 550-1000-point budget.
            maxCost = Mathf.Max(maxCost ?? 0f, options.Max(o => o.Cost));
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class FoodTraderGenerationRequest
    {
        static void Prefix(ref PawnGenerationRequest request)
        {
            if (request.KindDef?.GetModExtension<FoodTraderPawnExtension>() == null) return;
            request.MustBeCapableOfViolence = true;
            Predicate<Pawn> original = request.ValidatorPostGear;
            request.ValidatorPostGear = pawn => (original == null || original(pawn)) &&
                !pawn.WorkTagIsDisabled(WorkTags.Violent) &&
                pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness) >= 0.8f &&
                pawn.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation) >= 0.6f &&
                pawn.health.capacities.GetLevel(PawnCapacityDefOf.Sight) >= 0.6f &&
                HoverchairFixUtility.WornChair(pawn) != null &&
                pawn.equipment?.Primary?.def.IsRangedWeapon == true;
        }
    }

    // Initial hediff generation runs for new pawns, not save loading or ordinary world-pawn return.
    [HarmonyPatch(typeof(PawnGenerator), "GenerateInitialHediffs")]
    internal static class FoodTraderStartingBody
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn pawn)
        {
            FoodTraderPawnExtension settings = pawn.kindDef.GetModExtension<FoodTraderPawnExtension>();
            if (settings == null || !BodyModelEligibility.HasAdultBiology(pawn) ||
                BodyTypeUtility.CheckExemptions(pawn)) return;
            Hediff weight = pawn.WeightHediff();
            if (weight == null) return;
            float mass = Mathf.Clamp(settings.startingMassKg.RandomInRange,
                GlobalSettings.minWeight.threshold, GlobalSettings.maxWeight.threshold);
            HediffUtility.SetHediffSeverity(weight.def, pawn, HediffUtility.KilosToSeverityWithBaseWeight(mass));
        }
    }

    [HarmonyPatch(typeof(VisitorGiftForPlayerUtility), nameof(VisitorGiftForPlayerUtility.GiveRandomGift))]
    internal static class FoodTraderDepartureGift
    {
        static bool Prefix(List<Pawn> possibleGivers, Faction faction)
        {
            if (possibleGivers == null || !possibleGivers.Any(p =>
                p?.trader?.traderKind?.defName == FoodTraderUtility.Caravan)) return true;
            Pawn giver = possibleGivers.FirstOrDefault(p => p?.Spawned == true &&
                p.RaceProps.Humanlike && p.Faction == faction);
            if (giver == null) return false;
            VisitorGiftForPlayerUtility.GiveGift(possibleGivers, faction, FoodTraderUtility.GenerateGifts(giver.Map));
            return false;
        }
    }
}
