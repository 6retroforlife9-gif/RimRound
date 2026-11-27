using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Ideology;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public static class AbundanceTableSupply
    {
        // Real items are placed on table surfaces first. Overflow stays beside
        // those tables in the same room, clear of beds, chairs and doors.
        public static IEnumerable<IntVec3> Cells(LordJob_AbundanceMeals ritual)
        {
            Map map=ritual.Map;
            var tables=FeastCompatibilityPolicy.Tables(map,ritual.selectedTarget.Cell).ToList();
            foreach(var c in tables.SelectMany(t=>t.OccupiedRect().Cells).Distinct()
                .Where(c=>!RitualAttendance.IsReservedCell(map,c))
                .OrderBy(c=>c.GetItemCount(map)>0 ? 1 : 0))yield return c;
            foreach(var c in tables.SelectMany(t=>t.OccupiedRect().ExpandedBy(2).Cells).Distinct()
                .Where(c=>c.InBounds(map) && c.Standable(map) && AbundanceBeds.SameRoom(c,ritual.selectedTarget.Cell,map) &&
                    !c.GetThingList(map).Any(t=>t is Building) && !RitualAttendance.IsReservedCell(map,c) &&
                    !ritual.Meals.Any(m=>m.Seat==c || m.EaterSeat==c) &&
                    !(ritual.Bed!=null && AbundanceBeds.ApproachCells(ritual.Bed).Contains(c)))
                .OrderBy(c=>c.GetFirstPawn(map)==null ? 0 : 1))yield return c;
        }
        public static int Space(Thing food,IntVec3 cell,Map map)
        {
            if(!cell.InBounds(map) || cell.GetThingList(map).OfType<Building_Storage>()
                .Any(storage=>storage.IsForbidden(Faction.OfPlayer) || storage.GetStoreSettings()?.AllowedToAccept(food)==false))return 0;
            var items=cell.GetThingList(map).Where(t=>t.def.category==ThingCategory.Item).ToList();
            int merge=items.Where(t=>t.CanStackWith(food)).Sum(t=>Math.Max(0,t.def.stackLimit-t.stackCount));
            return merge+Math.Max(0,cell.GetMaxItemsAllowedInCell(map)-items.Count)*food.def.stackLimit;
        }
        public static Job TryGiveJob(Pawn pawn)
        {
            var ritual=AbundanceRituals.Session(pawn);
            if(ritual==null || !ritual.Valid())return null;
            AbundanceCollection.Migrate(ritual);
            var meal=ritual.Meals.FirstOrDefault(m=>m.Servings.Any(s=>s.Collector==pawn && s.Count>0 && s.Collected && !s.Placed && !s.Serving));
            var supply=meal?.Servings.FirstOrDefault(s=>s.Collector==pawn && s.Count>0 && s.Collected && !s.Placed && !s.Serving);
            if(supply==null)return null;
            Thing food=supply.Source;
            if(food==null || food.Destroyed || !pawn.inventory.Contains(food)) { AbundanceCollection.SupplyUnavailable(ritual,food,meal);return null; }
            var available=Cells(ritual).Where(c=>Space(food,c,pawn.Map)>0 && pawn.CanReach(c,PathEndMode.ClosestTouch,Danger.Some)).ToList();
            if(available.Count==0) { AbundanceCollection.SupplyUnavailable(ritual,food,meal);return null; }
            var cell=available.Where(c=>pawn.CanReserve(c))
                .DefaultIfEmpty(IntVec3.Invalid).First();
            if(!cell.IsValid)return null;
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(AbundanceRituals.SetTableJob),cell,food,meal.Eater);
        }
    }
    public sealed class JobDriver_AbundanceSetTable : JobDriver
    {
        LordJob_AbundanceMeals Ritual=>AbundanceRituals.Session(pawn);
        AbundanceMeal Meal=>Ritual?.Meals.FirstOrDefault(m=>m.Eater==job.targetC.Pawn);
        public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.Reserve(job.targetA,job,1,-1,null,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(()=>Ritual==null || !Ritual.Valid() || Meal==null);
            AddFinishAction(condition=>
            {
                // An ordinary interruption leaves real food in the collector's
                // inventory. The next job can place it or release its claim.
                if(condition!=JobCondition.Succeeded && (Ritual?.Ceremonial==true ||
                    condition!=JobCondition.InterruptForced && condition!=JobCondition.InterruptOptional))
                    AbundanceCollection.SupplyUnavailable(Ritual,job.targetB.Thing,Meal);
            });
            yield return Toils_Goto.GotoCell(TargetIndex.A,PathEndMode.ClosestTouch);
            var place=ToilMaker.MakeToil("SetOutAbundanceFood");
            place.initAction=()=>
            {
                Thing food=job.targetB.Thing;
                var allocations=AbundanceCollection.Allocations(Ritual).Where(a=>a.Supply.Collector==pawn && a.Supply.Source==food && a.Supply.Collected && !a.Supply.Placed && !a.Supply.Serving).ToList();
                int claimed=allocations.Sum(a=>a.Supply.Count);
                if(allocations.Count==0 || !pawn.inventory.Contains(food) || food.stackCount<claimed ||
                    !AbundanceTableSupply.Cells(Ritual).Contains(job.targetA.Cell))
                { EndJobWith(JobCondition.Incompletable);return; }
                int amount=Math.Min(claimed,AbundanceTableSupply.Space(food,job.targetA.Cell,pawn.Map));
                if(amount<=0) { EndJobWith(JobCondition.Succeeded);return; }
                int placed=0;
                pawn.inventory.innerContainer.TryDrop(food,job.targetA.Cell,pawn.Map,ThingPlaceMode.Direct,amount,out var result,
                    (stack,count)=>
                    {
                        // Track each surviving stack returned by native placement,
                        // including merges and partial placement. Claim only our count.
                        int remaining=count;
                        foreach(var entry in allocations)
                        {
                            int share=Math.Min(remaining,entry.Supply.Count);
                            if(share<=0)continue;
                            entry.Meal.Servings.Add(new AbundanceServing{Source=stack,Count=share,Collected=true,Placed=true,
                                Collector=pawn,Trip=entry.Supply.Trip,OutputDef=stack.def,UnitMass=entry.Supply.UnitMass,UnitBulk=entry.Supply.UnitBulk});
                            entry.Supply.Count-=share;remaining-=share;
                            if(entry.Supply.Count==0)entry.Supply.Source=null;
                        }
                        placed+=count;
                    });
                if(placed==0)EndJobWith(JobCondition.Incompletable);
            };
            place.defaultCompleteMode=ToilCompleteMode.Instant;yield return place;
        }
    }
}
