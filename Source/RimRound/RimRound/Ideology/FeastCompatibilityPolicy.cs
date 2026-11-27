using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Ideology
{
    // Explicit metadata can identify unusual meals or furniture. Hard food
    // exclusions and native ownership/safety checks still apply.
    public sealed class FeastCompatibilityExtension : DefModExtension
    {
        public bool? preparedMeal;
        public bool? diningSurface;
        public bool? diningSeat;
    }

    public static class FeastCompatibilityPolicy
    {
        static readonly ConcurrentDictionary<Type,bool> supportedNativeHooks=new ConcurrentDictionary<Type,bool>();
        public static bool IsMeal(Thing food)
        {
            if(food==null || !food.IngestibleNow || !OrdinaryFoodIntake.IsOrdinaryFood(food) ||
                FoodEnergyReleasePolicy.IsTimedProduct(food.def) || !SpecialistContentPolicy.Allows(food))return false;
            var ingredients=food.TryGetComp<CompIngredients>()?.ingredients;
            if(ingredients!=null && ingredients.Any(d=>OrdinaryFoodIntake.IsAtomicDef(d) || FoodEnergyReleasePolicy.IsTimedProduct(d)))return false;
            bool? explicitMeal=food.def.GetModExtension<FeastCompatibilityExtension>()?.preparedMeal;
            if(!(explicitMeal ?? food.def.ingestible.preferability>=FoodPreferability.MealTerrible))return false;
            // An overridden amount hook can bypass the ordinary intake ledger.
            // Stacking-only subclasses still inherit the supported native hook.
            return supportedNativeHooks.GetOrAdd(food.GetType(),SupportsNativeHooks);
        }
        static bool SupportsNativeHooks(Type type)
        {
            var amountMethod=AccessTools.Method(type,"IngestedCalculateAmounts");
            var ingestMethod=AccessTools.Method(type,"Ingested",new[]{typeof(Pawn),typeof(float)});
            return amountMethod!=null && (amountMethod.DeclaringType==typeof(Thing) || amountMethod.DeclaringType==typeof(ThingWithComps)) &&
                ingestMethod!=null && (ingestMethod.DeclaringType==typeof(Thing) || ingestMethod.DeclaringType==typeof(ThingWithComps));
        }
        public static string PartialEffectLimitation(Thing food)
        {
            string limitation=OrdinaryFoodIntake.PartialEffectLimitation(food);
            if(limitation!=null || food?.def.ingestible==null)return limitation;
            // Some cooking mods apply buffs through a food thought's MoodOffset
            // or PostAdd, not through an ingestion callback. Those effects still
            // require a whole serving, including when supplied by an ingredient.
            var thoughts=new[]{food.def.ingestible.tasteThought,food.def.ingestible.specialThoughtDirect}
                .Concat(food.TryGetComp<CompIngredients>()?.ingredients.Select(d=>d.ingestible?.specialThoughtAsIngredient) ?? Enumerable.Empty<ThoughtDef>());
            foreach(var thought in thoughts.Where(t=>t!=null))
                if(thought.hediff!=null || thought.ThoughtClass.Assembly!=typeof(Thought_Memory).Assembly)
                    return "unknown-food-thought:"+thought.defName+":"+thought.ThoughtClass.FullName;
            return null;
        }
        public static bool IsDiningSurface(Thing thing) => thing is Building && thing.Spawned &&
            thing.Faction==Faction.OfPlayer && !thing.IsBurning() && !thing.IsForbidden(Faction.OfPlayer) &&
            (thing.def.GetModExtension<FeastCompatibilityExtension>()?.diningSurface ?? thing.def.IsTable);
        public static IEnumerable<Building> Tables(Map map,IntVec3 focus) => map==null ? Enumerable.Empty<Building>() :
            map.listerBuildings.allBuildingsColonist.Where(t=>IsDiningSurface(t) && AbundanceBeds.SameRoom(t.Position,focus,map));
        public static IEnumerable<IntVec3> SeatCells(Thing seat)
        {
            if(!(seat is Building) || !seat.Spawned || seat.Faction!=Faction.OfPlayer || seat.IsBurning() || seat.IsForbidden(Faction.OfPlayer) ||
                !(seat.def.GetModExtension<FeastCompatibilityExtension>()?.diningSeat ?? seat.def.building?.isSittable==true))yield break;
            if(seat.def.building?.multiSittable==true)
            { foreach(var cell in seat.OccupiedRect().Cells)yield return cell; }
            else if(seat.def.hasInteractionCell)yield return seat.InteractionCell;
            else yield return seat.Position;
        }
    }
}
