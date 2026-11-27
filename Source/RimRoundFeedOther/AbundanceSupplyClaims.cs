using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.FeedingTube;
using RimRound.Ideology;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    // A single admission snapshot. It owns only numbers and references, never
    // food, reservations, jobs or edits to the other running rituals.
    public sealed class AbundanceSupplyClaims
    {
        sealed class Claim
        {
            public LordJob_AbundanceMeals Ritual;
            public AbundanceMeal Meal;
            public AbundanceServing Supply;
            public Pawn Getter => Supply.Collector ?? Meal.Server;
        }
        sealed class LiquidClaim
        {
            public Claim Claim;
            public ThingDef Output;
            public FoodAmount Amount;
        }
        readonly Map map;
        readonly HashSet<Pawn> selected;
        readonly List<Claim> claims=new List<Claim>();
        readonly List<ReservationManager.Reservation> external=new List<ReservationManager.Reservation>();
        internal readonly Dictionary<Thing,int> Used=new Dictionary<Thing,int>();
        internal readonly Dictionary<object,List<FoodAmount>> Liquids=new Dictionary<object,List<FoodAmount>>();

        AbundanceSupplyClaims(Map map,IEnumerable<Pawn> selected)
        { this.map=map;this.selected=new HashSet<Pawn>(selected); }

        public static bool TryCreate(List<AbundanceMeal> meals,List<Pawn> collectors,out AbundanceSupplyClaims result,out string failure)
            => TryCreate(meals,collectors,null,out result,out failure);

        internal static bool TryCreateForMachine(Thing machine,List<Pawn> collectors,out AbundanceSupplyClaims result)
            => TryCreate(new List<AbundanceMeal>(),collectors,machine,out result,out _);

        static bool TryCreate(List<AbundanceMeal> meals,List<Pawn> collectors,Thing machine,out AbundanceSupplyClaims result,out string failure)
        {
            result=null;failure=null;
            Map map=collectors?.FirstOrDefault(p=>p?.Spawned==true)?.Map;
            if(map==null || meals==null) { failure="RR_AbundanceInvalid".Translate();return false; }
            var snapshot=new AbundanceSupplyClaims(map,collectors);
            foreach(var ritual in map.lordManager.lords.Select(l=>l.LordJob).OfType<LordJob_AbundanceMeals>()
                .Where(r=>!r.cancelled && !r.CancelRequested && r.Meals!=null && !r.Meals.Any(meals.Contains))
                .OrderBy(r=>r.CreatedTick).ThenBy(r=>r.lord.loadID))
                foreach(var entry in ritual.Meals.SelectMany(meal=>meal.Servings.Where(s=>s.Count>0).Select(supply=>new {meal,supply}))
                    .OrderBy(entry=>entry.supply.AcquisitionOrder))
                    {
                        var meal=entry.meal;var supply=entry.supply;
                        // A late order references one known outlet. A stale
                        // claim in a separate supply pool cannot invalidate it.
                        if(machine!=null && !CompetesForMachine(machine,supply.Source))continue;
                        // Once a machine was collected, only its real output
                        // items remain claims; never debit that machine twice.
                        if(supply.Source is Building && (supply.Collected || supply.Placed))continue;
                        var claim=new Claim{Ritual=ritual,Meal=meal,Supply=supply};
                        if(supply.Source==null || supply.Source.Destroyed || supply.Source.MapHeld!=map ||
                            meal.Eater==null || meal.Eater.Dead || meal.Eater.MapHeld!=map)
                        { failure=Invalid(claim);return false; }
                        snapshot.claims.Add(claim);
                    }
            // Count all existing physical claims before assigning any hopper
            // ingredients. Placed food remains promised to its original diners.
            foreach(var claim in snapshot.claims.Where(c=>!(c.Supply.Source is Building)))
            {
                Thing source=claim.Supply.Source;
                if(!AbundanceMealJobs.ClaimPolicyAllows(claim.Meal.Eater,source,claim.Getter))
                { failure=Invalid(claim);return false; }
                Add(snapshot.Used,source,claim.Supply.Count);
                if(snapshot.Used[source]>source.stackCount) { failure=Invalid(claim);return false; }
            }
            snapshot.AddExternalReservations(collectors[0]);
            var liquids=new Dictionary<object,List<LiquidClaim>>();
            foreach(var claim in snapshot.claims.Where(c=>c.Supply.Source is Building))
            {
                if(FeastSourceProviders.For(claim.Supply.Source)!=null)
                {
                    for(int i=0;i<claim.Supply.Count;i++)
                        if(!FeastSourceProviders.TryDebit(claim.Supply.Source,claim.Meal.Eater,claim.Getter,snapshot.Used))
                        { failure=Invalid(claim);return false; }
                }
                else if(claim.Supply.Source is Building_NutrientPasteDispenser paste)
                {
                    if(!FeastSourceProviders.IsNativeHopper(paste) || !paste.CanDispenseNow || !paste.Spawned)
                    { failure=Invalid(claim);return false; }
                    for(int i=0;i<claim.Supply.Count;i++)
                        if(!TryDebitPaste(paste,claim.Meal.Eater,claim.Getter,snapshot.Used))
                        { failure=Invalid(claim);return false; }
                }
                else if(claim.Supply.Source is Building_FoodFaucet faucet)
                {
                    object network=FaucetMealAcquisition.SourceNetwork(faucet);
                    double nutrition=claim.Supply.SourceNutrition,volume=claim.Supply.SourceVolume;
                    ThingDef output=claim.Supply.OutputDef;
                    if(nutrition==0d && volume==0d)
                    {
                        // Older active saves have no portion metadata. Ask the
                        // existing recipient-aware preview without editing them.
                        AbundanceSupply derived=null;
                        foreach(Pawn getter in new[]{claim.Getter,claim.Meal.Server}.Concat(claim.Ritual.lord.ownedPawns).Distinct())
                            if(AbundanceMealJobs.TryDescribeSupply(getter,claim.Meal.Eater,claim.Meal.Budget(),faucet,null,out derived))break;
                        if(derived==null) { failure=Invalid(claim);return false; }
                        nutrition=derived.Nutrition;volume=derived.Volume;output=derived.OutputDef;
                    }
                    var amount=new FoodAmount(nutrition,volume);
                    if(network==null || output==null || !amount.Valid || nutrition<=0 || volume<=0)
                    { failure=Invalid(claim);return false; }
                    if(!liquids.TryGetValue(network,out var entries))liquids[network]=entries=new List<LiquidClaim>();
                    for(int i=0;i<claim.Supply.Count;i++)entries.Add(new LiquidClaim{Claim=claim,Output=output,Amount=amount});
                }
                else { failure=Invalid(claim);return false; }
            }
            foreach(var group in liquids)
            {
                var amounts=group.Value.Select(c=>c.Amount).ToList();
                if(!FaucetMealAcquisition.PreviewSources(group.Key,amounts,out var sources))
                { failure=Invalid(group.Value[0].Claim);return false; }
                for(int i=0;i<group.Value.Count;i++)
                {
                    var entry=group.Value[i];
                    Thing preview=FoodNetworkV2ServingUtility.MakeServing(
                        sources[i].PortionPrecise(sources[i].PreciseNutrition,entry.Amount.Volume),entry.Output,entry.Output.defName);
                    try
                    {
                        if(preview==null || !AbundanceMealJobs.ClaimPolicyAllows(entry.Claim.Meal.Eater,preview,entry.Claim.Getter) ||
                            NativePasteReserve.OutputDietAllows!=null && !NativePasteReserve.OutputDietAllows(entry.Claim.Meal.Eater,
                                entry.Output,sources[i].ingredients,entry.Claim.Getter))
                        { failure=Invalid(entry.Claim);return false; }
                    }
                    finally { if(preview!=null && !preview.Destroyed)preview.Destroy(DestroyMode.Vanish); }
                }
                snapshot.Liquids[group.Key]=amounts;
            }
            result=snapshot;return true;
        }

        static bool CompetesForMachine(Thing machine,Thing source)
        {
            if(source is Building)return AbundanceCollection.SharesMachineStock(machine,source);
            return machine is Building_NutrientPasteDispenser paste && FeastSourceProviders.IsNativeHopper(paste) &&
                source?.Spawned==true && source.Map==paste.Map && Building_NutrientPasteDispenser.IsAcceptableFeedstock(source.def) &&
                paste.AdjCellsCardinalInBounds.Contains(source.Position) && source.Position.GetThingList(paste.Map).Any(t=>t.IsHopper());
        }

        static string Invalid(Claim claim) => "RR_AbundancePriorClaimInvalid".Translate(claim.Meal.Eater?.LabelShort ?? "?");
        static void Add(Dictionary<Thing,int> counts,Thing source,int count)
        { counts.TryGetValue(source,out int prior);counts[source]=(int)Math.Min(int.MaxValue,(long)prior+Math.Max(0,count)); }

        // Match the exact saved native job, collector and target. Its reservation
        // overlaps the logical claim only up to the quantity already represented.
        int Covered(ReservationManager.Reservation reservation)
        {
            Pawn p=reservation.Claimant;Job job=reservation.Job;Thing source=reservation.Target.Thing;
            if(p==null || job==null || p.CurJob!=job)return 0;
            return claims.Where(c=>c.Supply.Source==source &&
                (job.def.defName==AbundanceRituals.GatherJob && job.targetA.Thing==source && !c.Supply.Collected &&
                    (c.Supply.Collector==p || c.Ritual.CollectionVersion<2 && c.Meal.Server==p) ||
                 job.def.defName==AbundanceRituals.MealJob && (c.Supply.Placed || c.Supply.Serving) && c.Meal.Server==p && job.targetB.Pawn==c.Meal.Eater))
                .Aggregate(0,(n,c)=>(int)Math.Min(int.MaxValue,(long)n+c.Supply.Count));
        }
        void AddExternalReservations(Pawn observer)
        {
            var remainingCoverage=new Dictionary<Tuple<Pawn,Job,Thing>,int>();
            foreach(var reservation in map.reservationManager.ReservationsReadOnly)
            {
                Thing source=reservation.Target.Thing;
                if(reservation.Layer!=null || source==null || source.Destroyed || source is Building ||
                    selected.Contains(reservation.Claimant) || !Respects(observer,reservation.Claimant))continue;
                int count=reservation.StackCount<0 ? source.stackCount : Math.Max(0,reservation.StackCount);
                var key=Tuple.Create(reservation.Claimant,reservation.Job,source);
                if(!remainingCoverage.TryGetValue(key,out int covered))covered=Covered(reservation);
                int overlap=Math.Min(count,covered);remainingCoverage[key]=covered-overlap;count-=overlap;
                if(count==0)continue;
                Add(Used,source,count);external.Add(reservation);
            }
        }
        // Same faction/host rules as native ReservationManager. Enemy reservations
        // must not make a player's accessible stock vanish from its preview.
        static bool Respects(Pawn observer,Pawn claimant)
        {
            if(observer==claimant)return true;
            if(observer?.Faction==null || claimant?.Faction==null)return false;
            return observer.Faction==claimant.Faction || !observer.Faction.HostileTo(claimant.Faction) ||
                claimant.HostFaction!=null && claimant.HostFaction==observer.HostFaction ||
                observer.HostFaction!=null && (claimant.HostFaction!=null || observer.HostFaction==claimant.Faction);
        }
        bool PriorMachineJob(Pawn pawn,Thing source) => pawn?.CurJob?.def.defName==AbundanceRituals.GatherJob &&
            pawn.CurJob.targetA.Thing==source && claims.Any(c=>c.Supply.Source==source && !c.Supply.Collected &&
                (c.Supply.Collector==pawn || c.Ritual.CollectionVersion<2 && c.Meal.Server==pawn));
        bool PhysicalAvailable(LocalTargetInfo target,Thing source)
        {
            var reservers=new HashSet<Pawn>();map.physicalInteractionReservationManager.ReserversOf(target,reservers);
            return reservers.All(p=>selected.Contains(p) || PriorMachineJob(p,source) ||
                claims.Any(c=>c.Supply.Source==source && (c.Supply.Placed || c.Supply.Serving) && c.Meal.Server==p &&
                    p.CurJob?.def.defName==AbundanceRituals.MealJob && p.CurJob.targetB.Pawn==c.Meal.Eater));
        }
        internal int AvailableStack(Pawn collector,Thing source)
        {
            if(!PhysicalAvailable(source,source) || external.Any(r=>r.Target.Thing==source && r.MaxPawns!=10))return 0;
            // Used already contains other rituals plus the nonoverlapping native
            // reservation counts. Return the physical cap, not a second debit.
            return source.stackCount;
        }
        internal bool CanUseMachine(Pawn collector,Building machine)
        {
            var targets=new List<LocalTargetInfo>{machine};
            if(machine.def.hasInteractionCell)
            {
                targets.Add(machine.InteractionCell);
                Building edifice=machine.InteractionCell.GetEdifice(map);if(edifice!=null)targets.Add(edifice);
            }
            return targets.All(t=>PhysicalAvailable(t,machine)) && !map.reservationManager.ReservationsReadOnly.Any(r=>
                r.Layer==null && targets.Contains(r.Target) && Respects(collector,r.Claimant) &&
                !selected.Contains(r.Claimant) && !(r.Claimant?.CurJob==r.Job && PriorMachineJob(r.Claimant,machine)));
        }

        internal static bool TryDebitPaste(Building_NutrientPasteDispenser paste,Pawn eater,Pawn getter,Dictionary<Thing,int> used)
        {
            var ingredients=paste.AdjCellsCardinalInBounds.Where(c=>c.GetThingList(paste.Map).Any(t=>t.IsHopper()))
                .SelectMany(c=>c.GetThingList(paste.Map).Where(t=>Building_NutrientPasteDispenser.IsAcceptableFeedstock(t.def)).Reverse()).ToList();
            if(NativePasteReserve.OutputDietAllows!=null && !NativePasteReserve.OutputDietAllows(eater,paste.DispensableDef,ingredients.Select(t=>t.def),getter))return false;
            var proposed=new Dictionary<Thing,int>(used);
            // Native dispensing rounds each ingredient in float arithmetic.
            float wanted=paste.def.building.nutritionCostPerDispense-.0001f;
            if(float.IsNaN(wanted) || float.IsInfinity(wanted) || wanted<=0)return false;
            foreach(Thing ingredient in ingredients)
            {
                proposed.TryGetValue(ingredient,out int consumed);float n=ingredient.GetStatValue(StatDefOf.Nutrition);
                if(float.IsNaN(n) || float.IsInfinity(n) || n<=0)return false;
                int count=Math.Min(Math.Max(0,ingredient.stackCount-consumed),Mathf.CeilToInt(wanted/n));
                if(count<=0)continue;proposed[ingredient]=consumed+count;wanted-=count*n;if(wanted<=0)break;
            }
            if(wanted>0)return false;
            foreach(var pair in proposed)used[pair.Key]=pair.Value;return true;
        }
    }
}
