using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.FeedingTube;
using RimRound.Ideology;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public sealed class JobDriver_AbundanceGather : JobDriver
    {
        LordJob_AbundanceMeals Ritual=>AbundanceRituals.Session(pawn);
        List<AbundanceAllocation> Pending
        {
            get
            {
                if(Ritual==null)return new List<AbundanceAllocation>();
                AbundanceCollection.Migrate(Ritual);
                return AbundanceCollection.Allocations(Ritual).Where(a=>a.Supply.Collector==pawn && !a.Supply.Collected && a.Supply.Source==job.targetA.Thing &&
                    AbundanceCollection.SourceAllowed(Ritual,a,pawn) && AbundanceCollection.MachineTurn(Ritual,a.Supply)).OrderBy(a=>a.Supply.AcquisitionOrder).ToList();
            }
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing source=job.targetA.Thing; var claims=Pending;
            if(claims.Count==0 || !AbundanceCollection.CanAccess(pawn,source))
            { Unavailable();return false; }
            return AbundanceCollection.HeldBy(pawn,source) || source is Building_FoodFaucet ||
                pawn.Reserve(source,job,source is Building ? 1 : 10,source is Building ? -1 : claims.Sum(a=>a.Supply.Count),null,false);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(()=>Ritual==null || !Ritual.Valid() || Ritual.FoodReady || pawn.carryTracker.CarriedThing is Pawn);
            AddFinishAction(condition=>
            {
                if(condition==JobCondition.Succeeded)return;
                var ritual=Ritual;
                if(ritual==null)return;
                if(!ritual.Ceremonial && (condition==JobCondition.InterruptForced || condition==JobCondition.InterruptOptional))
                {
                    foreach(var entry in AbundanceCollection.Allocations(ritual).Where(a=>a.Supply.Collector==pawn &&
                        !a.Supply.Collected && a.Supply.Source==job.targetA.Thing))
                    { entry.Supply.Collector=null;entry.Supply.Trip=0; }
                }
                else Unavailable();
            });
            var collect=ToilMaker.MakeToil("CollectFeastSupply");
            collect.initAction=()=>
            {
                var claims=Pending;
                if(claims.Count==0) { EndJobWith(JobCondition.Incompletable);return; }
                Thing source=job.targetA.Thing;
                // Recheck capacity at pickup. A changed stat/old in-flight save
                // may reduce this load; retain the uncollected allowance exactly.
                float bulk=AbundanceCollection.FreeBulk(pawn),mass=AbundanceCollection.FreeMass(pawn);
                var byDef=new Dictionary<ThingDef,int>();
                bool machineCapacityReached=false;
                foreach(var entry in claims.ToArray())
                {
                    var s=entry.Supply;
                    if(machineCapacityReached)
                    { s.Collector=null;s.Trip=0;claims.Remove(entry);continue; }
                    if(source is Building_FoodFaucet f &&
                        FaucetMealAcquisition.TrySelectForFeastPlan(f,pawn,entry.Meal.Eater,out var portion))
                    {
                        if(!MatchesPlan(s,portion)) { FailCollection();return; }
                        s.OutputDef=portion.Portion.FoodDef;
                        s.UnitBulk=s.OutputDef.VolumePerUnit;
                        s.UnitMass=s.OutputDef.GetStatValueAbstract(StatDefOf.Mass);
                    }
                    int same=0;if(s.OutputDef!=null)byDef.TryGetValue(s.OutputDef,out same);
                    int count=AbundanceCollection.CarryCount(pawn,s,bulk,mass,same);
                    machineCapacityReached=source is Building && count<s.Count;
                    if(count<s.Count)
                    {
                        entry.Meal.Servings.Add(new AbundanceServing{Source=s.Source,BroughtBy=s.BroughtBy,Count=s.Count-count,OutputDef=s.OutputDef,UnitMass=s.UnitMass,UnitBulk=s.UnitBulk,
                            SourceNutrition=s.SourceNutrition,SourceVolume=s.SourceVolume,SourceProvider=s.SourceProvider,AcquisitionOrder=s.AcquisitionOrder});
                        s.Count=count;
                    }
                    if(count==0) { s.Source=null;claims.Remove(entry);continue; }
                    if(!AbundanceCollection.HeldBy(pawn,source)) { bulk-=count*s.UnitBulk;mass-=count*s.UnitMass; }
                    byDef[s.OutputDef]=same+count;
                }
                if(claims.Count==0) { EndJobWith(JobCondition.Succeeded);return; }
                if(source is Building_FoodFaucet faucet)
                {
                    var requests=new List<FaucetMealRequest>();
                    foreach(var entry in claims)
                    {
                        // A later diner may accept only a later FIFO batch. Do
                        // not test every request against the still-undrawn front
                        // batch; the ordered quote below checks each real output.
                        if(!FaucetMealAcquisition.TrySelectForFeastPlan(faucet,pawn,entry.Meal.Eater,out var choice))
                        { FailCollection();return; }
                        if(!MatchesPlan(entry.Supply,choice)) { FailCollection();return; }
                        requests.Add(new FaucetMealRequest(entry.Meal.Eater,FaucetMealPurpose.Ritual,choice.Portion,entry.Supply.Count));
                    }
                    if(!FaucetMealAcquisition.TryQuoteBatch(faucet,pawn,requests,out var quote,out var failure) ||
                        !FaucetMealAcquisition.TryCollectBatch(quote,pawn.inventory.innerContainer,out var batch,out failure))
                    { FailCollection();return; }
                    foreach(var entry in claims) { entry.Supply.Count=0;entry.Supply.Source=null; }
                    foreach(var serving in batch.Servings)
                    {
                        var claim=claims.First(a=>a.Meal.Eater==serving.Recipient);
                        claim.Meal.Servings.Add(Held(serving.Thing,1,claim.Supply.Trip));
                    }
                }
                else if(FeastSourceProviders.For(source)!=null)
                {
                    var collectors=Ritual.lord.ownedPawns.Where(AbundanceRituals.CapableServer).ToList();
                    if(!FeastSourceProviders.CanCollect(Ritual,collectors)) { FailCollection();return; }
                    foreach(var entry in claims)
                    {
                        while(entry.Supply.Count>0)
                        {
                            bool valid=FeastSourceProviders.TryAcquire(source,entry.Supply,entry.Meal.Eater,pawn,out var serving);
                            if(serving!=null && !serving.Destroyed)
                            {
                                // A real dispense consumed its source, even if
                                // custody fails. Never refund the reservoir.
                                entry.Supply.Count--;
                                if(pawn.inventory.innerContainer.TryAdd(serving,false))
                                    entry.Meal.Servings.Add(Held(serving,serving.stackCount,entry.Supply.Trip));
                                else
                                {
                                    MealSittingComponent.Current.RecoverUnownedFood(pawn,serving);
                                    FailCollection();return;
                                }
                            }
                            if(!valid) { FailCollection();return; }
                        }
                        entry.Supply.Source=null;
                    }
                }
                else if(source is Building_NutrientPasteDispenser paste)
                {
                    if(!FeastSourceProviders.IsNativeHopper(paste)) { FailCollection();return; }
                    foreach(var entry in claims)
                    {
                        while(entry.Supply.Count>0)
                        {
                            Thing serving=paste.TryDispenseFood();
                            if(serving==null || !pawn.inventory.innerContainer.TryAdd(serving,false))
                            {
                                if(serving!=null)MealSittingComponent.Current.RecoverUnownedFood(pawn,serving);
                                FailCollection();return;
                            }
                            entry.Supply.Count--;
                            entry.Meal.Servings.Add(Held(serving,1,entry.Supply.Trip));
                        }
                        entry.Supply.Source=null;
                    }
                }
                else
                {
                    int allClaimed=AbundanceCollection.Allocations(Ritual).Where(a=>a.Supply.Source==source).Sum(a=>a.Supply.Count);
                    if(source.stackCount<allClaimed) { FailCollection();return; }
                    int count=claims.Sum(a=>a.Supply.Count);
                    Thing held;
                    if(pawn.inventory.Contains(source))held=source;
                    else
                    {
                        held=source.SplitOff(count);
                        if(!pawn.inventory.innerContainer.TryAdd(held,false))
                        { MealSittingComponent.Current.RecoverUnownedFood(pawn,held);FailCollection();return; }
                    }
                    foreach(var entry in claims) { entry.Supply.Source=held;entry.Supply.Collected=true; }
                }
            };
            collect.defaultCompleteMode=ToilCompleteMode.Instant;
            yield return Toils_Jump.JumpIf(collect,()=>AbundanceCollection.HeldBy(pawn,job.targetA.Thing));
            yield return Toils_Goto.GotoThing(TargetIndex.A,job.targetA.Thing is Building ? PathEndMode.InteractionCell : PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return collect;
        }
        AbundanceServing Held(Thing thing,int count,int trip) => new AbundanceServing
        {
            Source=thing,Count=count,Collector=pawn,Trip=trip,Collected=true,OutputDef=thing.def,
            UnitBulk=thing.def.VolumePerUnit,UnitMass=thing.GetStatValue(StatDefOf.Mass)
        };
        static bool MatchesPlan(AbundanceServing supply,FaucetMealAcquisition.Selection choice) =>
            supply.SourceNutrition<=0 && supply.SourceVolume<=0 ||
            supply.OutputDef==choice.Portion.FoodDef && Math.Abs(supply.SourceNutrition-choice.Portion.PreciseNutrition)<.000001d &&
            Math.Abs(supply.SourceVolume-choice.Portion.Volume)<.000001d;
        void Unavailable() => AbundanceCollection.SupplyUnavailable(Ritual,job.targetA.Thing,Ritual?.Meals.FirstOrDefault(m=>m.Eater==job.targetB.Pawn));
        void FailCollection() { EndJobWith(JobCondition.Incompletable); }
    }
}
