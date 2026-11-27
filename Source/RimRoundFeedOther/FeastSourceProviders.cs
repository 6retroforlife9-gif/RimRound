using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimRound.Ideology;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    // Quotes contain no reservations and are never serialized. Only the source
    // Thing, output Def and provider ID belong to a saved serving.
    public sealed class FeastSourceQuote
    {
        public Thing Source;
        public ThingDef Output;
        public string ProviderId;
        public int Available;
        public float InputPerServing;
        public List<ThingDef> Ingredients;
        public Thing MakePreview()
        {
            Thing preview=ThingMaker.MakeThing(Output);
            var ingredients=preview.TryGetComp<CompIngredients>();
            if(ingredients!=null)
                foreach(var ingredient in Ingredients)ingredients.RegisterIngredient(ingredient);
            return preview;
        }
    }

    public interface IFeastSourceProvider
    {
        string Id { get; }
        bool Handles(Thing source);
        bool TryQuote(Thing source,out FeastSourceQuote quote);
        Thing Acquire(FeastSourceQuote quote);
    }

    public static class FeastSourceProviders
    {
        static readonly List<IFeastSourceProvider> providers=new List<IFeastSourceProvider>{new MedievalPotProvider()};
        static readonly Dictionary<Type,bool> nativeHoppers=new Dictionary<Type,bool>();

        // Explicit adapters have priority over the native hopper fallback.
        // Registrations retain types/providers only, never a map or pawn cache.
        public static void Register(IFeastSourceProvider provider)
        {
            if(provider==null || string.IsNullOrEmpty(provider.Id) || providers.Any(p=>p.Id==provider.Id))
                throw new ArgumentException("A feast provider needs a unique stable ID.");
            providers.Add(provider);
        }
        public static IFeastSourceProvider For(Thing source) => source==null ? null : providers.FirstOrDefault(p=>p.Handles(source));
        public static bool IsNativeHopper(Building_NutrientPasteDispenser source)
        {
            if(source==null || For(source)!=null)return false;
            Type type=source.GetType();
            if(!nativeHoppers.TryGetValue(type,out bool supported))
            {
                supported=new[]{nameof(Building_NutrientPasteDispenser.HasEnoughFeedstockInHoppers),nameof(Building_NutrientPasteDispenser.TryDispenseFood)}
                    .All(name=>AccessTools.Method(type,name)?.DeclaringType==typeof(Building_NutrientPasteDispenser));
                nativeHoppers[type]=supported;
            }
            return supported;
        }
        public static bool TryQuote(Thing source,out FeastSourceQuote quote)
        {
            quote=null;var provider=For(source);
            return provider!=null && provider.TryQuote(source,out quote);
        }
        public static bool Permitted(FeastSourceQuote quote,Pawn eater,Pawn getter)
        {
            if(quote==null || eater==null || getter==null || quote.Output==null || quote.Ingredients==null ||
                NativePasteReserve.OutputDietAllows==null ||
                !NativePasteReserve.OutputDietAllows(eater,quote.Output,quote.Ingredients,getter))return false;
            Thing preview=quote.MakePreview();
            try { return AbundanceMealJobs.ClaimPolicyAllows(eater,preview,getter); }
            finally { preview.Destroy(DestroyMode.Vanish); }
        }
        public static bool TryDebit(Thing source,Pawn eater,Pawn getter,Dictionary<Thing,int> used)
        {
            if(!TryQuote(source,out var quote) || !Permitted(quote,eater,getter))return false;
            used.TryGetValue(source,out int prior);
            if(prior>=quote.Available)return false;
            used[source]=prior+1;return true;
        }
        public static bool CanCollect(LordJob_AbundanceMeals ritual,List<Pawn> collectors)
        {
            if(!AbundanceSupplyClaims.TryCreate(ritual.Meals,collectors,out var snapshot,out var failure))return false;
            foreach(var entry in AbundanceCollection.Allocations(ritual).Where(a=>!a.Supply.Collected && For(a.Supply.Source)!=null))
                for(int i=0;i<entry.Supply.Count;i++)
                    if(!TryDebit(entry.Supply.Source,entry.Meal.Eater,entry.Supply.Collector ?? entry.Meal.Server,snapshot.Used))return false;
            return true;
        }
        public static bool TryAcquire(Thing source,AbundanceServing plan,Pawn eater,Pawn getter,out Thing serving)
        {
            serving=null;var provider=For(source);
            if(provider==null || plan.SourceProvider!=provider.Id || !provider.TryQuote(source,out var quote) ||
                quote.Available<1 || quote.Output!=plan.OutputDef || !Permitted(quote,eater,getter))return false;
            // Output stays real even when a later inventory transfer fails.
            serving=provider.Acquire(quote);
            return serving!=null && !serving.Destroyed && serving.def==plan.OutputDef && serving.stackCount==1;
        }

        sealed class MedievalPotProvider : IFeastSourceProvider
        {
            public string Id=>"MedievalOverhaul.SlopPot.v1";
            readonly Type type=AccessTools.TypeByName("MedievalOverhaul.Building_SlopPot");
            FieldInfo nutrition,slop,ingredients;
            PropertyInfo fuel;
            bool inspected,capable;
            public bool Handles(Thing source)=>type!=null && source.GetType()==type;
            bool Capabilities()
            {
                if(inspected)return capable;inspected=true;
                if(type==null)return false;
                nutrition=AccessTools.Field(type,"nutritionComp");slop=AccessTools.Field(type,"slopComp");
                fuel=nutrition==null ? null : AccessTools.Property(nutrition.FieldType,"Fuel");
                ingredients=slop==null ? null : AccessTools.Field(slop.FieldType,"ingredients");
                capable=typeof(Building_NutrientPasteDispenser).IsAssignableFrom(type) && fuel?.PropertyType==typeof(float) &&
                    ingredients!=null && typeof(IEnumerable<ThingDef>).IsAssignableFrom(ingredients.FieldType) &&
                    AccessTools.Method(type,nameof(Building_NutrientPasteDispenser.TryDispenseFood))?.DeclaringType==type;
                return capable;
            }
            public bool TryQuote(Thing source,out FeastSourceQuote quote)
            {
                quote=null;
                if(!Handles(source) || !Capabilities() || !source.Spawned || source.Destroyed)return false;
                var pot=(Building_NutrientPasteDispenser)source;
                object reservoir=nutrition.GetValue(source), state=slop.GetValue(source);
                if(reservoir==null || state==null)return false;
                float remaining=(float)fuel.GetValue(reservoir,null), cost=source.def.building.nutritionCostPerDispense;
                if(float.IsNaN(remaining) || float.IsInfinity(remaining) || remaining<0 || float.IsNaN(cost) || float.IsInfinity(cost) || cost<=0)return false;
                var contents=(ingredients.GetValue(state) as IEnumerable<ThingDef>)?.ToList();
                // Unknown contents cannot bypass an ingredient food policy.
                if(contents==null || contents.Any(d=>d==null) || (remaining>0 && contents.Count==0))return false;
                int count=0;
                // Mirror the native float debit, including its exact >= boundary.
                while(count<512 && remaining>=cost) { remaining-=cost;count++; }
                if(pot.DispensableDef==null)return false;
                quote=new FeastSourceQuote{Source=source,Output=pot.DispensableDef,ProviderId=Id,Available=count,InputPerServing=cost,Ingredients=contents};
                return true;
            }
            public Thing Acquire(FeastSourceQuote quote)=>((Building_NutrientPasteDispenser)quote.Source).TryDispenseFood();
        }
    }
}
