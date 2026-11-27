using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using RimRound.Comps;
using RimRound.FeedingTube;
using RimRound.Ideology;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    [StaticConstructorOnStartup]
    public static class AbundanceMealJobs
    {
        [ThreadStatic] private static Pawn previewEater;
        [ThreadStatic] private static OrdinaryIntakeBudget? previewBudget;
        [ThreadStatic] private static bool packingForLater;
        [ThreadStatic] internal static Pawn ServedTableEater;
        static AbundanceMealJobs()
        {
            AbundanceRituals.Supplies=FindSupplies;AbundanceRituals.PlanSupplies=PlanSupplies;
            AbundanceRituals.CanShare=AbundanceSocial.CanPlanShare;AbundanceRituals.SocialJob=AbundanceSocial.TryGiveJob;
            AbundanceRituals.SetTable=AbundanceTableSupply.TryGiveJob;AbundanceRituals.Collect=AbundanceCollection.TryGiveJob;
            AbundanceRituals.UpdateCollection=AbundanceCollection.Update;AbundanceRituals.RefreshOfficerSupplies=RefreshOfficerSupplies;
            AbundanceRituals.CheckLaterAppetites=CheckLaterAppetites;
        }
        public static bool Permitted(Pawn eater,Thing serving)
        {
            OrdinaryIntakeBudget? budget=previewEater==eater ? previewBudget : AbundanceRituals.BudgetFor(eater);
            if(!budget.HasValue || !CommunalFeast.IsPreparedMeal(serving))return false;
            // A read-only quote can pack a meal before its owner is hungry.
            // Native acquisition and ingestion never inherit this preview scope.
            if(packingForLater && previewEater==eater)return true;
            return eater.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel!=true ||
                OrdinaryFoodIntake.TryPreview(eater,serving,1,out var portion,budget) &&
                (portion.UsefulFraction>=1d-1e-7d || FeastCompatibilityPolicy.PartialEffectLimitation(serving)==null);
        }
        public static IEnumerable<AbundanceSupply> FindSupplies(Pawn collector,Pawn eater,OrdinaryIntakeBudget budget)
            => FindSupplies(collector,eater,budget,null);
        static IEnumerable<AbundanceSupply> FindSupplies(Pawn collector,Pawn eater,OrdinaryIntakeBudget budget,AbundanceSupplyClaims claims)
        {
            var results=new List<AbundanceSupply>();
            if(collector?.Spawned!=true || eater?.Spawned!=true || collector.Map!=eater.Map)return results;
            var inventory=collector.inventory?.innerContainer.ToList() ?? new List<Thing>();
            Thing carried=collector.carryTracker.CarriedThing;
            if(carried!=null)inventory.Insert(0,carried);
            var sources=inventory.Concat(collector.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
                .Concat(collector.Map.listerBuildings.allBuildingsColonist.OfType<Building_FoodFaucet>()).Distinct()
                .OrderBy(t=>inventory.Contains(t)?0:1).ThenBy(t=>t.PositionHeld.DistanceToSquared(collector.Position));
            foreach(Thing source in sources)
                if(TryDescribeSupply(collector,eater,budget,source,claims,out var supply))results.Add(supply);
            return results;
        }
        internal static bool ClaimPolicyAllows(Pawn eater,Thing source,Pawn getter) => eater!=null && source!=null &&
            CommunalFeast.IsPreparedMeal(source) &&
            FoodUtility.WillEat(eater,source,getter,true,false);

        internal static bool TryDescribeSupply(Pawn collector,Pawn eater,OrdinaryIntakeBudget budget,Thing source,
            AbundanceSupplyClaims claims,out AbundanceSupply supply)
            => DescribeSupply(collector,eater,budget,source,claims,null,out supply);
        static bool DescribeSupply(Pawn collector,Pawn eater,OrdinaryIntakeBudget budget,Thing source,
            AbundanceSupplyClaims claims,FeastPreviewScan scan,out AbundanceSupply supply)
        {
            supply=null;
            if(collector?.Spawned!=true || eater==null || eater.Dead || eater.MapHeld!=collector.Map ||
                source==null || source.Destroyed || source.IsForbidden(collector))return false;
            bool held=AbundanceCollection.HeldBy(collector,source);
            if(scan!=null ? !scan.CanAccess(collector,source) : !held && (!source.Spawned || source.Map!=collector.Map || !collector.CanReach(source,
                source is Building ? PathEndMode.InteractionCell : PathEndMode.ClosestTouch,Danger.Some)))return false;
            Pawn previousEater=previewEater; var previousBudget=previewBudget;
            previewEater=eater; previewBudget=budget;
            Thing preview=source;int count=source.stackCount;bool destroyPreview=false;string providerId=null;
            try
            {
                if(source is Building_FoodFaucet faucet)
                {
                    if(claims!=null && !claims.CanUseMachine(collector,faucet))return false;
                    FaucetMealAcquisition.Selection choice;
                    if(!(scan!=null ? FaucetMealAcquisition.TrySelectForFeastPlan(faucet,collector,eater,out choice) :
                        FaucetMealAcquisition.TrySelectForPurpose(faucet,collector,eater,FaucetMealPurpose.Ritual,out choice)))return false;
                    preview=scan!=null ? FaucetMealAcquisition.FeastPlanningPreview(choice.Batch,choice.Portion) :
                        FoodNetworkV2ServingUtility.MakeServing(choice.Batch,choice.Portion.FoodDef,choice.Portion.FoodDef.defName);
                    destroyPreview=true;count=512;
                }
                else if(FeastSourceProviders.For(source)!=null)
                {
                    if(!(source is Building providerBuilding) || !(claims?.CanUseMachine(collector,providerBuilding) ?? collector.CanReserve(source)) ||
                        !FeastSourceProviders.TryQuote(source,out var quote) || !FeastSourceProviders.Permitted(quote,eater,collector))return false;
                    preview=quote.MakePreview();destroyPreview=true;count=quote.Available;providerId=quote.ProviderId;
                }
                else if(source is Building_NutrientPasteDispenser paste)
                {
                    if(!FeastSourceProviders.IsNativeHopper(paste) || !paste.CanDispenseNow || !(claims?.CanUseMachine(collector,paste) ?? collector.CanReserve(paste)))return false;
                    preview=ThingMaker.MakeThing(paste.DispensableDef);destroyPreview=true;count=512;
                }
                else if(claims!=null)count=claims.AvailableStack(collector,source);
                else if(!held)count=Math.Min(count,collector.Map.reservationManager.CanReserveStack(collector,source,10));
                if(preview==null || count<=0 ||
                    (!destroyPreview && !source.IsSociallyProper(collector)) || !ClaimPolicyAllows(eater,preview,collector))return false;
                double n=FoodUtility.NutritionForEater(eater,preview),v=n*OrdinaryFoodIntake.Density(preview);
                var precise=OrdinaryFoodIntake.PreciseVolume(preview);
                if(precise!=null) { n=precise.PreciseNutritionPerItem;v=precise.PreciseFullnessPerItem; }
                supply=new AbundanceSupply{Source=source,OutputDef=preview.def,Collector=collector,
                    UnitMass=preview.GetStatValue(StatDefOf.Mass),UnitBulk=preview.def.VolumePerUnit,
                    Count=count,Nutrition=n,Volume=v,AllowsPartial=eater.TryGetComp<FullnessAndDietStats_ThingComp>()?.UsesBodyModel==true &&
                        FeastCompatibilityPolicy.PartialEffectLimitation(preview)==null,ProviderId=providerId};
                return true;
            }
            finally
            {
                if(destroyPreview && preview!=null && !preview.Destroyed)preview.Destroy(DestroyMode.Vanish);
                previewEater=previousEater;previewBudget=previousBudget;
            }
        }
        // This scan belongs to one synchronous preview. Nothing survives the
        // call: changed access, policy, inventory and claims are checked again
        // for the next preview and at actual collection.
        sealed class FeastPreviewScan
        {
            readonly Dictionary<Pawn,List<Thing>> sources=new Dictionary<Pawn,List<Thing>>();
            readonly Dictionary<Tuple<Pawn,Thing>,bool> access=new Dictionary<Tuple<Pawn,Thing>,bool>();
            public List<Thing> Sources(Pawn collector)
            {
                if(sources.TryGetValue(collector,out var found))return found;
                var inventory=collector.inventory?.innerContainer.ToList() ?? new List<Thing>();
                if(collector.carryTracker.CarriedThing!=null)inventory.Insert(0,collector.carryTracker.CarriedThing);
                found=inventory.Concat(collector.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
                    .Concat(collector.Map.listerBuildings.allBuildingsColonist.OfType<Building_FoodFaucet>()).Distinct()
                    .OrderBy(t=>inventory.Contains(t)?0:1).ThenBy(t=>t.PositionHeld.DistanceToSquared(collector.Position)).ToList();
                sources[collector]=found;return found;
            }
            public bool CanAccess(Pawn collector,Thing source)
            {
                var key=Tuple.Create(collector,source);if(access.TryGetValue(key,out bool result))return result;
                result=AbundanceCollection.HeldBy(collector,source) || source.Spawned && source.Map==collector.Map && collector.CanReach(source,
                    source is Building ? PathEndMode.InteractionCell : PathEndMode.ClosestTouch,Danger.Some);
                access[key]=result;return result;
            }
        }
        static List<AbundanceSupply> PlanOffers(AbundanceMeal meal,List<Pawn> collectors,List<Thing> sources,
            AbundanceSupplyClaims claims,FeastPreviewScan scan,bool admission)
        {
            var result=new List<AbundanceSupply>();
            // All offers for this diner share the same read-only admission
            // budget. Re-evaluating body capacities per collector/source made
            // large previews quadratic in expensive pawn stat calculations.
            var budget=meal.Budget();
            bool previous=packingForLater;
            packingForLater=meal.AwaitingAppetite;
            try { foreach(var source in sources)
            {
                if(meal.AllowOutsideRoomFood && meal.AwaitingAppetite && source is Building &&
                    !AbundanceBeds.SameRoom(source.Position,meal.Seat,source.Map))continue;
                AbundanceSupply best=null,uncarryable=null;
                // No physical quote can exceed the actual stack. A capable
                // collector reaching that upper bound is already the same
                // stable maximum selected by the full collector enumeration.
                int upper=source is Building ? 512 : source.stackCount;
                foreach(var collector in collectors)
                    if((meal.AllowOutsideRoomFood || AbundanceCollection.RoomSource(source,meal.Venue,collector,admission)) &&
                        DescribeSupply(collector,meal.Eater,budget,source,claims,scan,out var offer))
                    {
                        if(!AbundanceCollection.CanCarryAtLeastOne(collector,offer)) { if(uncarryable==null)uncarryable=offer;continue; }
                        if(best==null || offer.Count>best.Count)best=offer;
                        if(best.Count>=upper)break;
                    }
                if(best!=null || uncarryable!=null)result.Add(best ?? uncarryable);
            } }
            finally { packingForLater=previous; }
            return result;
        }
        static int MealPreference(AbundanceSupply offer)
        {
            if(offer.OutputDef==ThingDefOf.BabyFood || offer.OutputDef==ThingDefOf.Pemmican || offer.Nutrition<.25)return 0;
            return 100+(int)Math.Round(100*CommunalFeast.MealGrade(offer.OutputDef));
        }
        // Plan the entire sitting before the begin button is enabled. Shared
        // hoppers and shared liquid networks are debited in this read-only model
        // once, even when several diners or faucets refer to the same stock.
        private static string PlanSupplies(List<AbundanceMeal> meals,List<Pawn> collectors)
        {
            foreach(var meal in meals)
            {
                meal.AllowOutsideRoomFood=FeedOtherMod.Settings.allowOutsideRitualFood;
                // Legacy direct planner callers supply a dining seat without a ritual target.
                if(!meal.Venue.IsValid)meal.Venue=meal.Seat;
            }
            return PlanSupplies(meals,collectors,false,true);
        }
        private static string PlanSupplies(List<AbundanceMeal> meals,List<Pawn> collectors,bool physicalOnly,bool admission=false)
            => PlanSupplies(meals, collectors, physicalOnly, admission, new Dictionary<AbundanceMeal,List<AbundanceSupply>>());

        private static string PlanSupplies(List<AbundanceMeal> meals, List<Pawn> collectors, bool physicalOnly,
            bool admission, Dictionary<AbundanceMeal,List<AbundanceSupply>> reachable)
        {
            if(!AbundanceSupplyClaims.TryCreate(meals,collectors,out var claims,out string failure))return failure;
            var scan=new FeastPreviewScan();
            // Prefer the hall's dispenser while retaining the same native
            // source quotes, dietary checks and shared finite-stock ledger.
            // Presence in the room alone never makes an unusable source valid.
            IntVec3 venue=meals.Count>0 ? meals[0].Seat : IntVec3.Invalid;
            var sources=collectors.SelectMany(scan.Sources).Distinct()
                .Where(t=>!physicalOnly || !(t is Building))
                .OrderBy(t=>t is Building_FoodFaucet && t.Spawned && venue.IsValid &&
                    AbundanceBeds.SameRoom(t.Position,venue,t.Map) ? 0 : 1).ToList();
            // Raw ingredients still belong to the dispenser stock ledger. Only prepared
            // physical food can become an offer, regardless of diner or collector.
            var servingSources = sources.Where(source => source is Building || CommunalFeast.IsPreparedMeal(source)).ToList();
            foreach (var meal in meals)
                if (!reachable.ContainsKey(meal))
                    reachable.Add(meal, PlanOffers(meal, collectors, servingSources, claims, scan, admission));
            var offers=meals.ToDictionary(m=>m,m=>reachable[m].Where(s=>AbundanceCollection.CanCarryAtLeastOne(s.Collector,s))
                .GroupBy(s=>s.Source).Select(g=>g.OrderByDescending(s=>s.Count).First()).ToList());
            var noFood=meals.FirstOrDefault(m=>offers[m].Count==0);
            if(noFood!=null)
            {
                if(reachable[noFood].Count>0)return "RR_FeastCannotCarry".Translate(noFood.Eater.LabelShort);
                if(noFood.AwaitingAppetite)return (noFood.AllowOutsideRoomFood ? "RR_FeastNoLaterDish" : "RR_FeastNoLaterDishInRoom").Translate(noFood.Eater.LabelShort);
                // Unknown dispenser overrides cannot safely borrow the native
                // hopper accounting. Explain that capability gap without ever
                // calling a dispenser to discover what its output might be.
                var unsupported=collectors.SelectMany(c=>c.Map.listerBuildings.allBuildingsColonist.OfType<Building_NutrientPasteDispenser>()
                    .Where(b=>(noFood.AllowOutsideRoomFood || AbundanceCollection.SourceInRoom(b,noFood.Venue,c.Map)) &&
                        FeastSourceProviders.For(b)==null && !FeastSourceProviders.IsNativeHopper(b) && AbundanceCollection.CanAccess(c,b)))
                    .FirstOrDefault();
                if(unsupported!=null)return "RR_FeastUnsupportedSource".Translate(unsupported.LabelCap);
                return (noFood.AllowOutsideRoomFood ? "RR_FeastNoPreparedFood" : "RR_FeastNoPreparedFoodInRoom").Translate(noFood.Eater.LabelShort);
            }
            var indexed=new List<Dictionary<int,AbundanceSupply>>();
            var diners=new List<FeastAllocationDiner>();int id=0;
            foreach(var meal in meals)
            {
                var body=meal.Eater.TryGetComp<FullnessAndDietStats_ThingComp>();
                float multiplier=body?.UsesBodyModel==true ? body.FullnessGainedMultiplier : 0f;
                var row=new Dictionary<int,AbundanceSupply>();
                foreach(var offer in offers[meal])row[id++]=offer;
                indexed.Add(row);
                // A full attendee needs exactly one physical item or one feasible
                // on-site dispense. Count that unit in the same finite stock search
                // as everyone's meals. It is never entered in their intake ledger.
                bool pack=meal.AwaitingAppetite;
                diners.Add(new FeastAllocationDiner(pack ? 1 : meal.NutritionLeft,pack ? 0 : meal.VolumeLeft,
                    !pack && body?.UsesBodyModel==true && multiplier>0,meal.Ceremonial || pack,
                    row.Select(pair=>new FeastAllocationOffer(pair.Key,pack ? 1 : pair.Value.Nutrition,
                        pack ? 0 : pair.Value.Volume*multiplier,!pack && pair.Value.AllowsPartial,MealPreference(pair.Value))),completionTolerance:.0001));
            }
            var planned=FeastAllocationPlanner.Plan(diners,new NativeFeastStock(meals,indexed,claims.Used,claims.Liquids,sources),new FeastAllocationOptions(12000));
            // Whole-only dishes may be too large for an otherwise healthy diner.
            // Re-run once those individuals need a reserved dish instead. Each
            // retry strictly grows that set; no partial plan is ever published.
            if(admission && !meals.Any(m=>m.Ceremonial) &&
                (planned.Success || planned.Failure==FeastAllocationFailure.NoActualEaters))
            {
                var wait=meals.Where((m,i)=>!m.AwaitingAppetite &&
                    (!planned.Success || planned.Diners[i].Parts.Count==0)).ToList();
                if(wait.Count>0)
                {
                    // Only these diners switch to packed-food eligibility. Reuse the other
                    // quotes within this synchronous validation and allocate all stock afresh.
                    foreach (var meal in wait)
                    {
                        meal.AwaitingAppetite = meal.ReservedForLater = true;
                        reachable.Remove(meal);
                    }
                    return PlanSupplies(meals, collectors, false, true, reachable);
                }
            }
            if(!planned.Success)
            {
                if(Prefs.DevMode)Log.Message("[RimRound Feast] plan refused: "+planned.Failure+" diner="+planned.FailureDinerIndex+" work="+planned.VisitedNodes);
                if(planned.Failure==FeastAllocationFailure.Shortage)
                {
                    var shortMeal=meals[Math.Max(0,planned.FailureDinerIndex)];
                    return (shortMeal.AllowOutsideRoomFood ? "RR_AbundanceNotEnough" : "RR_FeastNotEnoughInRoom").Translate(shortMeal.Eater.LabelShort);
                }
                if(planned.Failure==FeastAllocationFailure.NoActualEaters || planned.Failure==FeastAllocationFailure.RequiredDinerCannotEat)
                    return "RR_AbundanceNoDiners".Translate();
                if(planned.Failure==FeastAllocationFailure.PlanningLimit)return "RR_FeastPlanningLimit".Translate();
                return "RR_AbundanceInvalid".Translate();
            }
            // Publish only a complete joint plan. Numeric search never edits a
            // pawn, source, native reservation or the caller's saved servings.
            foreach(var meal in meals) { meal.Servings.Clear();meal.PlannedNutrition=meal.PlannedGradeNutrition=0; }
            int acquisitionOrder=0;
            foreach(var step in planned.Steps)
            {
                var meal=meals[step.DinerIndex];var chosen=indexed[step.DinerIndex][step.OfferId];
                if(meal.AwaitingAppetite && chosen.Source is Building)
                { meal.LaterDispenser=chosen.Source;continue; }
                if(!meal.AwaitingAppetite)
                {
                    meal.PlannedNutrition+=step.UsefulNutrition;
                    meal.PlannedGradeNutrition+=step.UsefulNutrition*CommunalFeast.MealGrade(chosen.OutputDef);
                }
                var entry=meal.Servings.LastOrDefault();
                if(!(chosen.Source is Building) && entry?.Source==chosen.Source && entry.OutputDef==chosen.OutputDef && entry.SourceProvider==chosen.ProviderId &&
                    entry.SourceNutrition==chosen.Nutrition && entry.SourceVolume==chosen.Volume)entry.Count++;
                else meal.Servings.Add(new AbundanceServing{Source=chosen.Source,Count=1,OutputDef=chosen.OutputDef,
                    BroughtBy=admission && !meal.AllowOutsideRoomFood && AbundanceCollection.HeldBy(chosen.Collector,chosen.Source) ? chosen.Collector : null,
                    UnitMass=chosen.UnitMass,UnitBulk=chosen.UnitBulk,SourceNutrition=chosen.Nutrition,SourceVolume=chosen.Volume,SourceProvider=chosen.ProviderId,
                    AcquisitionOrder=chosen.Source is Building ? acquisitionOrder++ : -1});
            }
            for(int i=0;i<meals.Count;i++)
            {
                if(meals[i].AwaitingAppetite)
                {
                    meals[i].NutritionLeft=meals[i].VolumeLeft=0;meals[i].Finished=true;
                    meals[i].PlanVersion=1;meals[i].Completion=FeastAllocationCompletion.AttendWithoutEating;
                    continue;
                }
                meals[i].NutritionLeft=planned.Diners[i].UsefulNutrition;
                meals[i].VolumeLeft=planned.Diners[i].PhysicalFullness;
                if(meals[i].OfficerCare)
                {
                    // The quoted real units are the finite allowance. Digestion
                    // can create additional room before the last bite, so do not
                    // discard it using the smaller gap measured at planning.
                    // Budget() still clamps every intake to the live 1.1/hard
                    // ceiling; unknown effects still require a whole-item fit.
                    var body=meals[i].Eater.TryGetComp<FullnessAndDietStats_ThingComp>();
                    meals[i].NutritionLeft=meals[i].Servings.Sum(s=>s.Count*s.SourceNutrition);
                    meals[i].VolumeLeft=meals[i].Servings.Sum(s=>s.Count*s.SourceVolume)*(body?.FullnessGainedMultiplier ?? 1f);
                }
                meals[i].PlanVersion=1;meals[i].Completion=planned.Diners[i].Completion;
                meals[i].Finished=planned.Diners[i].Parts.Count==0;
            }
            return null;
        }
        static void CheckLaterAppetites(LordJob_AbundanceMeals ritual)
        {
            int now=Find.TickManager.TicksGame;
            foreach(var meal in ritual.Meals)
            {
                if(!meal.AwaitingAppetite || now<meal.NextAppetiteCheck || !meal.Eater.Spawned)continue;
                meal.NextAppetiteCheck=now+180;
                var eater=meal.Eater;
                var budget=AbundanceRituals.InitialBudget(eater,false);
                var body=eater.TryGetComp<FullnessAndDietStats_ThingComp>();
                float multiplier=body?.UsesBodyModel==true ? body.FullnessGainedMultiplier : 0f;
                double room=multiplier>0 ? Math.Max(0,budget.FullnessTarget-body.OccupiedVolume) : double.MaxValue;
                // Digesting one tick of the original meal is not a new appetite.
                if(budget.Nutrition<.12 || room<Math.Min(.12,budget.FullnessTarget*.1))continue;
                var serving=meal.Servings.FirstOrDefault(s=>s.Placed && s.Count>0);
                if(serving==null && meal.LaterDispenser==null) { ritual.MissedReserve(meal);continue; }
                AbundanceSupply offer=null;
                if(serving!=null)
                {
                    Thing food=serving.Source;
                    if(food?.Spawned!=true || food.IsForbidden(meal.Server) ||
                        !AbundanceBeds.SameRoom(food.Position,ritual.selectedTarget.Cell,ritual.Map) ||
                        !ClaimPolicyAllows(eater,food,meal.Server)) { ritual.MissedReserve(meal);continue; }
                    var precise=OrdinaryFoodIntake.PreciseVolume(food);
                    double n=precise?.PreciseNutritionPerItem ?? FoodUtility.NutritionForEater(eater,food);
                    offer=new AbundanceSupply{Source=food,OutputDef=food.def,Nutrition=n,
                        Volume=precise?.PreciseFullnessPerItem ?? n*OrdinaryFoodIntake.Density(food),
                        AllowsPartial=body?.UsesBodyModel==true && FeastCompatibilityPolicy.PartialEffectLimitation(food)==null};
                }
                else
                {
                    var machine=meal.LaterDispenser;
                    if(machine?.Spawned!=true || !(meal.AllowOutsideRoomFood ? AbundanceBeds.SameRoom(machine.Position,ritual.selectedTarget.Cell,ritual.Map) :
                        AbundanceCollection.SourceInRoom(machine,ritual.selectedTarget.Cell,ritual.Map)))
                    { ritual.MissedReserve(meal);continue; }
                    if(!ritual.InitialSittingFinished || ritual.lord.ownedPawns.Any(p=>p.CurJobDef?.defName==AbundanceRituals.MealJob))
                        continue;
                    // A late machine order must never jump another ritual's
                    // already promised FIFO draws, including shared networks.
                    if(AbundanceCollection.OtherMachineClaims(ritual,machine))continue;
                    var collectors=ritual.lord.ownedPawns.Where(p=>p!=ritual.HostPawn && AbundanceRituals.CapableServer(p) &&
                        p.CurJob?.playerForced!=true && p.carryTracker.CarriedThing==null).ToList();
                    if(collectors.Count==0)continue;
                    // Check this pool's promised stock in every ritual,
                    // including this sitting and physical hopper ingredients.
                    if(!AbundanceSupplyClaims.TryCreateForMachine(machine,collectors,out var claims))
                    { ritual.MissedReserve(meal);continue; }
                    foreach(var collector in collectors)
                        if(TryDescribeSupply(collector,eater,budget,machine,claims,out offer) &&
                            AbundanceCollection.CanCarryAtLeastOne(collector,offer) &&
                            Allocate(offer,claims.Used,claims.Liquids,meal))break;
                        else offer=null;
                    if(offer==null) { ritual.MissedReserve(meal);continue; }
                }
                if(offer.Nutrition<=0) { ritual.MissedReserve(meal);continue; }
                double fraction=Math.Min(1d,budget.Nutrition/offer.Nutrition);
                if(multiplier>0 && offer.Volume>0)fraction=Math.Min(fraction,room/(offer.Volume*multiplier));
                if(fraction<=0 || !offer.AllowsPartial && fraction<1d-.000001)continue;
                meal.NutritionLeft=offer.Nutrition*fraction;
                meal.VolumeLeft=multiplier>0 ? offer.Volume*multiplier*fraction : double.MaxValue;
                meal.FullnessCeiling=budget.FullnessTarget;meal.AwaitingAppetite=false;meal.Finished=false;
                if(serving!=null)continue;
                int order=ritual.Meals.SelectMany(m=>m.Servings).Select(s=>s.AcquisitionOrder).DefaultIfEmpty(-1).Max()+1;
                meal.Servings.Add(new AbundanceServing{Source=offer.Source,OutputDef=offer.OutputDef,Count=1,
                    UnitMass=offer.UnitMass,UnitBulk=offer.UnitBulk,SourceNutrition=offer.Nutrition,
                    SourceVolume=offer.Volume,SourceProvider=offer.ProviderId,AcquisitionOrder=order});
                meal.LaterDispenser=null;ritual.FoodReady=false;ritual.NextCollectionCheck=0;ritual.LastProgressTick=now;
                break;
            }
        }
        static void RefreshOfficerSupplies(LordJob_AbundanceMeals ritual)
        {
            if(!ritual.FoodReady || !ritual.Meals.All(m=>m.Finished))return;
            var originals=ritual.Meals.Where(AbundanceRituals.OfficerNeedsTopUp).ToList();
            if(originals.Count==0)return;
            // Existing chewing/custody must settle first. Never offer the food
            // another live serving job currently holds as a fresh supply.
            if(ritual.lord.ownedPawns.Any(p=>p.CurJobDef?.defName==AbundanceRituals.MealJob) ||
                ritual.Meals.Any(m=>m.Servings.Any(s=>s.Count>0 && !s.Placed)))return;
            var collectors=ritual.lord.ownedPawns.Where(p=>p!=ritual.HostPawn && AbundanceRituals.CapableServer(p) &&
                p.CurJob?.playerForced!=true && p.carryTracker.CarriedThing==null).ToList();
            if(collectors.Count==0 || Find.TickManager.TicksGame-ritual.CreatedTick>=24000)
            { foreach(var meal in originals)meal.OfficerTopUpAttempts=4;return; }
            var drafts=new List<AbundanceMeal>();
            foreach(var meal in originals)
            {
                var body=meal.Eater.TryGetComp<FullnessAndDietStats_ThingComp>();
                var budget=AbundanceRituals.InitialBudget(meal.Eater,false);
                drafts.Add(new AbundanceMeal{Eater=meal.Eater,Server=meal.Server,Seat=meal.Seat,EaterSeat=meal.EaterSeat,
                    Venue=ritual.selectedTarget.Cell,AllowOutsideRoomFood=meal.AllowOutsideRoomFood,
                    NutritionLeft=budget.Nutrition,VolumeLeft=Math.Max(0,budget.FullnessTarget-body.OccupiedVolume),
                    FullnessCeiling=budget.FullnessTarget,OfficerCare=true});
                meal.OfficerTopUpAttempts++;
            }
            // Older rituals execute machine claims first. A new round must not
            // jump ahead of another ritual's already quoted FIFO ingredients.
            bool physicalOnly=ritual.Map.lordManager.lords.Select(l=>l.LordJob).OfType<LordJob_AbundanceMeals>()
                .Any(r=>r!=ritual && !r.cancelled && !r.CancelRequested && r.Meals.Any(m=>
                    m.Servings.Any(s=>s.Count>0 && !s.Collected && s.Source is Building)));
            string failure=PlanSupplies(drafts,collectors,physicalOnly);
            if(failure!=null)
            {
                foreach(var meal in originals)meal.OfficerTopUpAttempts=4;
                if(Prefs.DevMode)Log.Message("[RimRound Feast] Officer service ended without extra stock: "+failure);
                return;
            }
            int offset=ritual.Meals.SelectMany(m=>m.Servings).Select(s=>s.AcquisitionOrder).DefaultIfEmpty(-1).Max()+1;
            bool added=false;
            for(int i=0;i<drafts.Count;i++)
            {
                var meal=originals[i];var draft=drafts[i];
                if(draft.Finished || draft.Servings.Count==0) { meal.OfficerTopUpAttempts=4;continue; }
                foreach(var supply in draft.Servings)
                { if(supply.AcquisitionOrder>=0)supply.AcquisitionOrder+=offset;meal.Servings.Add(supply); }
                meal.NutritionLeft=draft.NutritionLeft;meal.VolumeLeft=draft.VolumeLeft;meal.FullnessCeiling=draft.FullnessCeiling;
                meal.Completion=draft.Completion;meal.PlanVersion=draft.PlanVersion;meal.Finished=false;
                ritual.AdditionalFoodPlanned+=draft.Servings.Sum(s=>s.Count);added=true;
            }
            if(added)
            {
                ritual.FoodReady=false;ritual.NextCollectionCheck=0;ritual.LastProgressTick=Find.TickManager.TicksGame;
                if(Prefs.DevMode)Log.Message("[RimRound Feast] Officer round planned from real stock; additional servings="+ritual.AdditionalFoodPlanned);
            }
        }
        sealed class ReferenceIdentity<T> : IEqualityComparer<T> where T:class
        {
            public static readonly ReferenceIdentity<T> Instance=new ReferenceIdentity<T>();
            public bool Equals(T a,T b)=>ReferenceEquals(a,b);
            public int GetHashCode(T value)=>RuntimeHelpers.GetHashCode(value);
        }
        sealed class NativeFeastStock : IFeastAllocationStock,IFeastAllocationPortionPolicy,IFeastAllocationStateKey
        {
            readonly List<AbundanceMeal> meals;
            readonly List<Dictionary<int,AbundanceSupply>> offers;
            readonly Dictionary<Thing,int> used;
            readonly Dictionary<object,List<FoodAmount>> liquids;
            readonly Dictionary<Thing,int> sourceIds;
            readonly Dictionary<object,int> networkIds;
            public NativeFeastStock(List<AbundanceMeal> meals,List<Dictionary<int,AbundanceSupply>> offers,
                Dictionary<Thing,int> used,Dictionary<object,List<FoodAmount>> liquids,IEnumerable<Thing> sources)
            {
                this.meals=meals;this.offers=offers;this.used=used;this.liquids=liquids;
                // Identity maps belong to this preview, including raw hopper
                // stock. Clones share these immutable maps, never mutable food.
                sourceIds=new Dictionary<Thing,int>(ReferenceIdentity<Thing>.Instance);
                foreach(var source in sources.Concat(used.Keys).Concat(offers.SelectMany(r=>r.Values).Select(o=>o.Source))
                    .Where(s=>s!=null).Distinct(ReferenceIdentity<Thing>.Instance).OrderBy(s=>s.thingIDNumber))sourceIds.Add(source,sourceIds.Count);
                networkIds=new Dictionary<object,int>(ReferenceIdentity<object>.Instance);
                foreach(var network in liquids.Keys.Concat(offers.SelectMany(r=>r.Values).Select(o=>o.Source).OfType<Building_FoodFaucet>()
                    .Select(FaucetMealAcquisition.SourceNetwork)).Where(n=>n!=null).Distinct(ReferenceIdentity<object>.Instance))networkIds.Add(network,networkIds.Count);
            }
            NativeFeastStock(NativeFeastStock original,Dictionary<Thing,int> used,Dictionary<object,List<FoodAmount>> liquids)
            { meals=original.meals;offers=original.offers;sourceIds=original.sourceIds;networkIds=original.networkIds;this.used=used;this.liquids=liquids; }
            public IFeastAllocationStock Clone()=>new NativeFeastStock(this,new Dictionary<Thing,int>(used),
                liquids.ToDictionary(pair=>pair.Key,pair=>new List<FoodAmount>(pair.Value)));
            public string GetAllocationStateKey()
            {
                // An unexpected source is uncacheable, not equivalent to any
                // other state. Exact ordered withdrawals retain FIFO semantics.
                if(used.Keys.Any(k=>!sourceIds.ContainsKey(k)) || liquids.Keys.Any(k=>!networkIds.ContainsKey(k)))return null;
                var key=new StringBuilder();
                foreach(var pair in used.OrderBy(p=>sourceIds[p.Key]))key.Append('P').Append(sourceIds[pair.Key]).Append(':').Append(pair.Value).Append(';');
                foreach(var pair in liquids.OrderBy(p=>networkIds[p.Key]))
                {
                    key.Append('L').Append(networkIds[pair.Key]).Append('[');
                    foreach(var amount in pair.Value)key.Append(BitConverter.DoubleToInt64Bits(amount.Nutrition).ToString("X16"))
                        .Append(':').Append(BitConverter.DoubleToInt64Bits(amount.Volume).ToString("X16")).Append(';');
                    key.Append(']');
                }
                return key.ToString();
            }
            public bool TryTake(int dinerIndex,int offerId)=>offers[dinerIndex].TryGetValue(offerId,out var offer) &&
                Allocate(offer,used,liquids,meals[dinerIndex]);
            public bool AllowsPartial(int dinerIndex,int offerId)
            {
                if(!offers[dinerIndex].TryGetValue(offerId,out var offer))return false;
                if(!(offer.Source is Building_FoodFaucet faucet))return offer.AllowsPartial;
                object network=FaucetMealAcquisition.SourceNetwork(faucet);if(network==null)return false;
                if(!liquids.TryGetValue(network,out var prior))prior=new List<FoodAmount>();
                var next=new List<FoodAmount>(prior){new FoodAmount(offer.Nutrition,offer.Volume)};
                if(!FaucetMealAcquisition.PreviewSources(network,next,out var sources))return false;
                if(Math.Abs(sources.Last().PreciseNutrition-offer.Nutrition)>.000001d)return false;
                var preview=FoodNetworkV2ServingUtility.MakeServing(sources.Last().PortionPrecise(sources.Last().PreciseNutrition,offer.Volume),offer.OutputDef,offer.OutputDef.defName);
                if(preview==null)return false;
                try { return ClaimPolicyAllows(meals[dinerIndex].Eater,preview,offer.Collector) && FeastCompatibilityPolicy.PartialEffectLimitation(preview)==null; }
                finally { preview.Destroy(DestroyMode.Vanish); }
            }
        }
        private static bool Allocate(AbundanceSupply offer,Dictionary<Thing,int> used,Dictionary<object,List<FoodAmount>> liquids,AbundanceMeal meal)
        {
            if(offer.Source is Building_FoodFaucet faucet)
            {
                object network=FaucetMealAcquisition.SourceNetwork(faucet);if(network==null)return false;
                if(!liquids.TryGetValue(network,out var prior))prior=new List<FoodAmount>();
                var next=new List<FoodAmount>(prior){new FoodAmount(offer.Nutrition,offer.Volume)};
                if(!FaucetMealAcquisition.PreviewSources(network,next,out var sources))return false;
                // The real output includes the next batch's ingredients, which
                // can differ from the oldest batch used by the initial preview.
                if(offer.OutputDef==null || NativePasteReserve.OutputDietAllows!=null && !NativePasteReserve.OutputDietAllows(meal.Eater,
                    offer.OutputDef,sources.Last().ingredients,offer.Collector))return false;
                if(Math.Abs(sources.Last().PreciseNutrition-offer.Nutrition)>.000001d)return false;
                var preview=FoodNetworkV2ServingUtility.MakeServing(sources.Last().PortionPrecise(sources.Last().PreciseNutrition,offer.Volume),offer.OutputDef,offer.OutputDef.defName);
                if(preview==null)return false;
                try { if(!ClaimPolicyAllows(meal.Eater,preview,offer.Collector))return false; }
                finally { preview.Destroy(DestroyMode.Vanish); }
                liquids[network]=next;return true;
            }
            if(FeastSourceProviders.For(offer.Source)!=null)
                return FeastSourceProviders.TryDebit(offer.Source,meal.Eater,offer.Collector,used);
            if(offer.Source is Building_NutrientPasteDispenser paste)
                return AbundanceSupplyClaims.TryDebitPaste(paste,meal.Eater,offer.Collector,used);
            used.TryGetValue(offer.Source,out int priorCount);
            if(priorCount>=offer.Count)return false;
            used[offer.Source]=priorCount+1;return true;
        }
    }

    [HarmonyPatch(typeof(MemoryThoughtHandler),nameof(MemoryThoughtHandler.TryGainMemory),new[]{typeof(Thought_Memory),typeof(Pawn)})]
    internal static class AbundanceServedTableThought
    {
        // Only this actual ritual meal's native finalization is a served-table
        // meal. Keep prior memories and every ordinary ingestion rule intact.
        static bool Prefix(MemoryThoughtHandler __instance,Thought_Memory newThought) =>
            newThought?.def!=ThoughtDefOf.AteWithoutTable || __instance.pawn!=AbundanceMealJobs.ServedTableEater;
    }
}
