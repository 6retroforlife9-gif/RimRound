using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.FeedingTube;
using RimRound.FeedingTube.Comps;
using RimRound.Ideology;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.FeedOther
{
    // An allocation always belongs to its eater. A trip only gives a selected
    // attendee custody of a bounded part of that allocation until it is placed.
    public sealed class AbundanceAllocation
    {
        public AbundanceMeal Meal;
        public AbundanceServing Supply;
    }

    public static class AbundanceCollection
    {
        public static bool SharesMachineStock(Thing first,Thing second)
        {
            if(!(first is Building) || !(second is Building) || !first.Spawned || !second.Spawned || first.Map!=second.Map)return false;
            if(first==second)return true;
            if(first is Building_FoodFaucet left && second is Building_FoodFaucet right)
            {
                var pool=ConnectedStock(left);
                return pool!=null && ReferenceEquals(pool,ConnectedStock(right));
            }
            // Registered source providers debit the exact building. Two native
            // paste outlets can instead draw from the same adjacent hopper.
            if(first is Building_NutrientPasteDispenser a && second is Building_NutrientPasteDispenser b &&
                FeastSourceProviders.IsNativeHopper(a) && FeastSourceProviders.IsNativeHopper(b))
                return a.AdjCellsCardinalInBounds.Intersect(b.AdjCellsCardinalInBounds)
                    .Any(c=>c.GetThingList(first.Map).Any(t=>t.IsHopper()));
            return false;
        }
        // A switched-off outlet still owns its place in the shared stock queue.
        // The normal acquisition path separately requires a working, permitted outlet.
        static object ConnectedStock(Building_FoodFaucet outlet) => FeedOtherMod.Settings.foodNetworkV2Enabled
            ? (object)FoodNetworkV2MachineUtility.NetworkFor(outlet) : outlet.GetComp<FoodNetTrader_ThingComp>()?.FoodNet;
        internal static bool OtherMachineClaims(LordJob_AbundanceMeals ritual,Thing machine)
            => ritual.Map.lordManager.lords.Select(l=>l.LordJob).OfType<LordJob_AbundanceMeals>()
                .Any(r=>r!=ritual && !r.cancelled && !r.CancelRequested && Allocations(r)
                    .Any(e=>!e.Supply.Collected && SharesMachineStock(machine,e.Supply.Source)));
        // Machine outputs may contain FIFO ingredients. Execute the planned
        // withdrawal order, including older rituals whose claims come first.
        // Physical stacks can still be collected concurrently.
        public static bool MachineTurn(LordJob_AbundanceMeals ritual,AbundanceServing supply,Pawn collector=null)
        {
            if(!(supply.Source is Building))return true;
            collector=collector ?? supply.Collector;
            foreach(var other in ritual.Map.lordManager.lords.Select(l=>l.LordJob).OfType<LordJob_AbundanceMeals>()
                .Where(r=>!r.cancelled && !r.CancelRequested).OrderBy(r=>r.CreatedTick).ThenBy(r=>r.lord.loadID))
            {
                // One collector may withdraw a contiguous FIFO batch at the
                // same machine. Other collectors and outlets on this stock
                // must preserve the order of the quoted ingredients.
                foreach(var entry in Allocations(other).Where(a=>!a.Supply.Collected && SharesMachineStock(supply.Source,a.Supply.Source))
                    .OrderBy(a=>a.Supply.AcquisitionOrder))
                {
                    var next=entry.Supply;
                    if(other!=ritual || next.Source!=supply.Source || next.Collector!=null && next.Collector!=collector)return false;
                    if(next==supply)return true;
                }
            }
            return false;
        }
        public static IEnumerable<AbundanceAllocation> Allocations(LordJob_AbundanceMeals ritual) =>
            ritual.Meals.SelectMany(m=>m.Servings.Where(s=>s.Count>0).Select(s=>new AbundanceAllocation{Meal=m,Supply=s}));
        internal static void SupplyUnavailable(LordJob_AbundanceMeals ritual,Thing source,AbundanceMeal affected)
        {
            if(ritual==null || affected==null)return;
            if(ritual.Ceremonial) { ritual.RequestCancellation("RR_FeastSupplyChanged");return; }
            // A failed pickup belongs to this diner. Other diners may still be
            // able to use the same stack, and this diner may have another dish.
            foreach(var serving in affected.Servings.Where(s=>s.Count>0 && (s.Source==source || s.Source==null || s.Source.Destroyed)))
            { serving.Count=0;serving.Source=null; }
            if(affected.LaterDispenser==null && !affected.Servings.Any(s=>s.Count>0))
                ritual.StopServing(affected);
        }

        public static bool HeldBy(Pawn pawn,Thing source) => source!=null &&
            (pawn.inventory?.Contains(source)==true || pawn.carryTracker?.CarriedThing==source);

        internal static bool SourceInRoom(Thing source,IntVec3 venue,Map map)
        {
            if(source?.Spawned!=true || source.Map!=map)return false;
            var cell=source is Building && source.def.hasInteractionCell ? source.InteractionCell : source.Position;
            return AbundanceBeds.SameRoom(cell,venue,map);
        }

        internal static bool RoomSource(Thing source,IntVec3 venue,Pawn collector,bool brought)
        {
            if(collector?.Spawned!=true || source==null || source.Destroyed)return false;
            if(source.Spawned)return SourceInRoom(source,venue,collector.Map);
            // A participant can bring their original packed dish from anywhere.
            // Dropping it outside makes it a ground pickup subject to the room rule.
            return HeldBy(collector,source) && (brought || AbundanceBeds.SameRoom(collector.Position,venue,collector.Map));
        }

        internal static bool SourceAllowed(LordJob_AbundanceMeals ritual,AbundanceAllocation entry,Pawn collector) =>
            entry.Meal.AllowOutsideRoomFood || RoomSource(entry.Supply.Source,ritual.selectedTarget.Cell,collector,entry.Supply.BroughtBy==collector);

        static bool CanCollect(LordJob_AbundanceMeals ritual,AbundanceAllocation entry,Pawn collector) =>
            SourceAllowed(ritual,entry,collector) && CanAccess(collector,entry.Supply.Source);

        public static float FreeBulk(Pawn pawn) => Mathf.Max(0,pawn.GetStatValue(StatDefOf.CarryingCapacity)-
            (pawn.inventory?.innerContainer.Sum(t=>t.stackCount*t.def.VolumePerUnit) ?? 0)-
            (pawn.carryTracker.CarriedThing?.stackCount*pawn.carryTracker.CarriedThing?.def.VolumePerUnit ?? 0));

        public static float FreeMass(Pawn pawn) => Mathf.Max(0,MassUtility.FreeSpace(pawn)-
            (pawn.carryTracker.CarriedThing?.stackCount*pawn.carryTracker.CarriedThing?.GetStatValue(StatDefOf.Mass) ?? 0));

        public static bool CanCarryAtLeastOne(Pawn pawn,AbundanceSupply supply) => HeldBy(pawn,supply.Source) ||
            (pawn.carryTracker.MaxStackSpaceEver(supply.OutputDef)>0 &&
             FreeBulk(pawn)+.0001f>=supply.UnitBulk && FreeMass(pawn)+.0001f>=supply.UnitMass);

        public static bool CanAccess(Pawn pawn,Thing source)
        {
            if(source==null || source.Destroyed || source.IsForbidden(pawn))return false;
            if(HeldBy(pawn,source))return true;
            return source.Spawned && source.Map==pawn.Map && pawn.CanReach(source,
                source is Building ? PathEndMode.InteractionCell : PathEndMode.ClosestTouch,Danger.Some);
        }

        public static void Migrate(LordJob_AbundanceMeals ritual)
        {
            if(ritual.CollectionVersion>=2)return;
            // Preserve held/placed object identities and any in-flight pickup.
            // Only unstarted claims enter the new dispatcher.
            foreach(var entry in Allocations(ritual))
            {
                var s=entry.Supply; var owner=entry.Meal.Server;
                if(!s.Placed && (s.Collected || owner?.CurJob?.def.defName==AbundanceRituals.GatherJob &&
                    owner.CurJob.targetA.Thing==s.Source && owner.CurJob.targetB.Pawn==entry.Meal.Eater))
                { s.Collector=owner; s.Trip=ritual.NextTrip++; }
                s.OutputDef=s.Source is Building_NutrientPasteDispenser paste ? paste.DispensableDef : s.Source?.def;
                if(s.Source!=null && !(s.Source is Building))
                { s.UnitBulk=s.Source.def.VolumePerUnit; s.UnitMass=s.Source.GetStatValue(StatDefOf.Mass); }
                else if(s.Source is Building && !s.Collected)
                {
                    var offer=AbundanceMealJobs.FindSupplies(owner,entry.Meal.Eater,entry.Meal.Budget()).FirstOrDefault(a=>a.Source==s.Source);
                    if(offer!=null) { s.OutputDef=offer.OutputDef;s.UnitBulk=offer.UnitBulk;s.UnitMass=offer.UnitMass; }
                }
            }
            ritual.CollectionVersion=2;
        }

        public static void Update(LordJob_AbundanceMeals ritual)
        {
            int now=Find.TickManager.TicksGame;
            if(ritual.FoodReady || now<ritual.NextCollectionCheck)return;
            ritual.NextCollectionCheck=now+60;
            Migrate(ritual);
            var collectors=ritual.lord.ownedPawns.Where(p=>p!=ritual.HostPawn &&
                AbundanceRituals.CapableServer(p) && p.CurJob?.playerForced!=true).ToList();
            foreach(var entry in Allocations(ritual).ToList())
            {
                var s=entry.Supply;
                if(s.Count<=0)continue;
                if(s.Source==null || s.Source.Destroyed) { SupplyUnavailable(ritual,s.Source,entry.Meal);return; }
                if(s.Placed)continue;
                if(s.Collected)
                {
                    // Custody is real inventory. Losing that attendant cancels
                    // this preparation without taking their items remotely.
                    if(s.Collector==null || !collectors.Contains(s.Collector) || !HeldBy(s.Collector,s.Source))
                    { SupplyUnavailable(ritual,s.Source,entry.Meal);return; }
                    continue;
                }
                // A full spectator may have been selected only as a collector.
                // If they leave before pickup, the same allowance is offered to
                // the remaining attendees; neither stock nor budgets change.
                if(s.Collector!=null && (!collectors.Contains(s.Collector) || !CanCollect(ritual,entry,s.Collector)))
                { s.Collector=null;s.Trip=0; }
                if(!collectors.Any(p=>CanCollect(ritual,entry,p))) { SupplyUnavailable(ritual,s.Source,entry.Meal);return; }
            }
        }

        public static IEnumerable<Pawn> AvailableCollectors(LordJob_AbundanceMeals ritual) => ritual.lord.ownedPawns
            .Where(p=>p!=ritual.HostPawn && AbundanceRituals.CapableServer(p) && !RitualAttendance.IsTransportBusy(p) &&
                !(p.carryTracker.CarriedThing is Pawn) && p.CurJob?.playerForced!=true &&
                !Allocations(ritual).Any(a=>!a.Supply.Placed && a.Supply.Collector==p))
            .OrderByDescending(p=>p.GetStatValue(StatDefOf.MoveSpeed))
            .ThenBy(p=>DistanceToSupply(p,ritual)).ThenBy(p=>p.thingIDNumber);

        static float DistanceToSupply(Pawn p,LordJob_AbundanceMeals ritual)
        {
            Thing first=Allocations(ritual).Where(a=>!a.Supply.Collected && a.Supply.Collector==null && CanCollect(ritual,a,p))
                .Select(a=>a.Supply.Source).Distinct().OrderBy(s=>p.Position.DistanceToSquared(s.PositionHeld)).FirstOrDefault();
            return first==null ? float.PositiveInfinity : HeldBy(p,first) ? 0 :
                RitualAttendance.RouteCost(p,first,first is Building ? PathEndMode.InteractionCell : PathEndMode.ClosestTouch);
        }

        public static int CarryCount(Pawn pawn,AbundanceServing supply,float freeBulk,float freeMass,int alreadyOfDef=0)
        {
            if(supply.OutputDef==null)return 0;
            if(HeldBy(pawn,supply.Source))return supply.Count;
            int count=Math.Min(supply.Count,Math.Max(0,pawn.carryTracker.MaxStackSpaceEver(supply.OutputDef)-alreadyOfDef));
            if(supply.UnitBulk>0)count=Math.Min(count,Mathf.FloorToInt((freeBulk+.0001f)/supply.UnitBulk));
            if(supply.UnitMass>0)count=Math.Min(count,Mathf.FloorToInt((freeMass+.0001f)/supply.UnitMass));
            return Math.Max(0,count);
        }

        public static void AssignTrips(LordJob_AbundanceMeals ritual)
        {
            Migrate(ritual);
            foreach(Pawn collector in AvailableCollectors(ritual).ToList())
            {
                var available=Allocations(ritual).Where(a=>!a.Supply.Collected && a.Supply.Collector==null && MachineTurn(ritual,a.Supply,collector) && CanCollect(ritual,a,collector))
                    .OrderBy(a=>a.Supply.Source.PositionHeld.DistanceToSquared(collector.Position)).ThenBy(a=>a.Supply.AcquisitionOrder).ToList();
                float bulk=FreeBulk(collector),mass=FreeMass(collector);
                var byDef=new Dictionary<ThingDef,int>(); IntVec3 anchor=IntVec3.Invalid; int trip=0;
                foreach(var entry in available)
                {
                    var supply=entry.Supply;
                    if(anchor.IsValid && supply.Source.PositionHeld.DistanceToSquared(anchor)>144)continue;
                    int same=0;if(supply.OutputDef!=null)byDef.TryGetValue(supply.OutputDef,out same);
                    int count=CarryCount(collector,supply,bulk,mass,same);
                    if(count==0) { if(supply.Source is Building)break;continue; }
                    bool machineCapacityReached=supply.Source is Building && count<supply.Count;
                    if(!anchor.IsValid) { anchor=supply.Source.PositionHeld; trip=ritual.NextTrip++; }
                    if(count<supply.Count)
                    {
                        supply.Count-=count;
                        supply=new AbundanceServing{Source=supply.Source,BroughtBy=supply.BroughtBy,Count=count,OutputDef=supply.OutputDef,UnitMass=supply.UnitMass,UnitBulk=supply.UnitBulk,
                            SourceNutrition=supply.SourceNutrition,SourceVolume=supply.SourceVolume,SourceProvider=supply.SourceProvider,AcquisitionOrder=supply.AcquisitionOrder};
                        entry.Meal.Servings.Add(supply);
                    }
                    supply.Collector=collector; supply.Trip=trip;
                    if(!HeldBy(collector,supply.Source)) { bulk-=count*supply.UnitBulk; mass-=count*supply.UnitMass; }
                    byDef[supply.OutputDef]=same+count;
                    if(machineCapacityReached)break;
                }
            }
        }

        public static Job TryGiveJob(Pawn pawn)
        {
            var ritual=AbundanceRituals.Session(pawn);
            if(ritual==null || ritual.FoodReady || !RitualAttendance.AllSettled(ritual) || RitualAttendance.IsTransportBusy(pawn))return null;
            Migrate(ritual);
            AssignTrips(ritual);
            var pending=Allocations(ritual).Where(a=>a.Supply.Collector==pawn && !a.Supply.Collected && MachineTurn(ritual,a.Supply)).ToList();
            var next=pending.OrderBy(a=>a.Supply.Source?.PositionHeld.DistanceToSquared(pawn.Position) ?? int.MaxValue).FirstOrDefault();
            if(next==null)return AbundanceTableSupply.TryGiveJob(pawn);
            Thing source=next.Supply.Source;
            if(!CanCollect(ritual,next,pawn)) { SupplyUnavailable(ritual,source,next.Meal);return null; }
            int count=pending.Where(a=>a.Supply.Source==source).Sum(a=>a.Supply.Count);
            if(!HeldBy(pawn,source) && !pawn.CanReserve(source,source is Building ? 1 : 10,source is Building ? -1 : count))return null;
            var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(AbundanceRituals.GatherJob),source,next.Meal.Eater);
            job.count=count;return job;
        }
    }
}
