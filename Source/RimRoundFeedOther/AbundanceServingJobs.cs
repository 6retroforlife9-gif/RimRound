using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Ideology;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    // One physical bundle belongs to one meal. Objects with different native
    // stacking metadata remain separate real inventory objects throughout.
    public sealed class JobDriver_AbundanceMeal : JobDriver
    {
        private const int CurrentLayout = 1;
        private int layout = CurrentLayout;
        private bool collected, collectionFinished;
        private int selectedCount;
        private List<Thing> bundle = new List<Thing>();
        private List<int> attemptedSources = new List<int>();
        private AbundanceMeal cachedMeal;
        public override void ExposeData()
        {
            base.ExposeData();
            if(Scribe.mode==LoadSaveMode.Saving)bundle.RemoveAll(t=>t==null || t.Destroyed);
            Scribe_Values.Look(ref collected,"abundanceCollected");
            Scribe_Values.Look(ref collectionFinished,"abundanceBundleReady");
            Scribe_Values.Look(ref selectedCount,"abundanceBundleSelectedCount");
            Scribe_Values.Look(ref layout,"abundanceBundleLayout",0);
            Scribe_Collections.Look(ref bundle,"abundanceBundleObjects",LookMode.Reference);
            Scribe_Collections.Look(ref attemptedSources,"abundanceBundleAttempted",LookMode.Value);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)
            { bundle=bundle ?? new List<Thing>();attemptedSources=attemptedSources ?? new List<int>(); }
        }
        private Pawn Eater => job.targetB.Pawn;
        private LordJob_AbundanceMeals Ritual => AbundanceRituals.Session(pawn);
        private AbundanceMeal Meal => cachedMeal ?? (cachedMeal=Ritual?.Meals.FirstOrDefault(m=>m.Eater==Eater && m.Server==pawn));
        private bool Ours(Thing food) => food!=null && !food.Destroyed && AbundanceCollection.HeldBy(pawn,food) &&
            Meal?.Servings.Any(s=>s.Source==food && s.Count>0 && s.Collected && !s.Placed && s.Collector==pawn)==true;
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing carried=pawn.carryTracker.CarriedThing;
            if(carried!=null && carried!=job.targetA.Thing && !bundle.Contains(carried) &&
                !pawn.carryTracker.innerContainer.TryTransferToContainer(carried,pawn.inventory.innerContainer,false))return false;
            if(Meal==null || !pawn.ReserveSittableOrSpot(job.targetC.Cell,job,errorOnFailed))return false;
            if(Eater!=pawn && !pawn.Reserve(Eater,job,1,-1,null,errorOnFailed))return false;
            Thing source=job.targetA.Thing;
            return source!=null && (Ours(source) || layout!=CurrentLayout && collected && carried==source ||
                pawn.Reserve(source,job,10,1,null,errorOnFailed));
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            cachedMeal=Meal;
            // Older active saves have a different seven-toil layout. Preserve
            // those indices as inert aborts; never reinterpret a saved chew or
            // finalize index as the new collection/portion state machine.
            if(layout!=CurrentLayout)
            {
                AddFinishAction(FinishBundle);
                this.FailOn(()=>layout!=CurrentLayout);
                for(int i=0;i<7;i++)
                {
                    var abort=ToilMaker.MakeToil("EndLegacyFeastServingSafely");
                    abort.initAction=()=>EndJobWith(JobCondition.Incompletable);
                    abort.defaultCompleteMode=ToilCompleteMode.Instant;yield return abort;
                }
                yield break;
            }
            this.FailOn(()=>layout!=CurrentLayout || Meal==null || Meal.Finished || Ritual==null || !Ritual.Valid() || !Ritual.ServiceParticipantsAvailable(Meal) || !Eater.Spawned ||
                (Eater!=pawn && !Ritual.HostSettled));
            AddFinishAction(FinishBundle);
            var destination=Toils_Goto.GotoCell(TargetIndex.C,PathEndMode.OnCell);
            var select=ToilMaker.MakeToil("SelectBoundedFeastBundleSource");
            var approach=Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);
            select.initAction=()=>
            {
                if(collectionFinished) { JumpToToil(destination);return; }
                var sources=Meal.Servings.Where(s=>s.Placed && s.Count>0 && s.Source?.Spawned==true &&
                    !attemptedSources.Contains(s.Source.thingIDNumber)).Select(s=>s.Source).Distinct()
                    .OrderBy(s=>s==job.targetA.Thing ? 0 : 1).ThenBy(s=>pawn.Position.DistanceToSquared(s.Position)).ToList();
                foreach(Thing source in sources)
                {
                    attemptedSources.Add(source.thingIDNumber);
                    int owned=Meal.Servings.Where(s=>s.Placed && s.Source==source).Sum(s=>s.Count);
                    int count=Capacity(source,owned);
                    if(count<=0 || !AbundanceCollection.CanAccess(pawn,source) ||
                        !AbundanceBeds.SameRoom(source.Position,Ritual.selectedTarget.Cell,pawn.Map) ||
                        !AbundanceMealJobs.Permitted(Eater,source))continue;
                    if(pawn.Map.reservationManager.ReservedBy(source,pawn,job))
                        pawn.Map.reservationManager.Release(source,pawn,job);
                    if(!pawn.Reserve(source,job,10,count,null,false))continue;
                    selectedCount=count;job.targetA=source;return;
                }
                collectionFinished=true;
                if(!bundle.Any(Ours)) { EndJobWith(JobCondition.Incompletable);return; }
                JumpToToil(destination);
            };
            select.defaultCompleteMode=ToilCompleteMode.Instant;yield return select;
            yield return approach;
            var take=ToilMaker.MakeToil("TakeClaimedFeastBundleItems");
            take.initAction=()=>
            {
                Thing source=job.targetA.Thing;
                var allocation=Meal.Servings.FirstOrDefault(s=>s.Placed && s.Count>0 && s.Source==source);
                int claimed=Ritual.Meals.Sum(m=>m.Servings.Where(s=>s.Placed && s.Source==source).Sum(s=>s.Count));
                int own=Meal.Servings.Where(s=>s.Placed && s.Source==source).Sum(s=>s.Count);
                int count=Math.Min(selectedCount,Capacity(source,own));
                if(allocation==null || source==null || !source.Spawned || source.stackCount<claimed || count<=0 ||
                    !AbundanceBeds.SameRoom(source.Position,Ritual.selectedTarget.Cell,pawn.Map) ||
                    source.IsForbidden(pawn) || !AbundanceMealJobs.Permitted(Eater,source))
                { AbundanceCollection.SupplyUnavailable(Ritual,source,Meal);EndJobWith(JobCondition.Incompletable);return; }
                Thing held;
                if(pawn.carryTracker.CarriedThing==null)
                {
                    int moved=pawn.carryTracker.TryStartCarry(source,count);
                    if(moved<=0) { EndJobWith(JobCondition.Incompletable);return; }
                    count=moved;
                    held=pawn.carryTracker.CarriedThing;
                }
                else
                {
                    held=source.SplitOff(count);
                    if(held==null || !pawn.inventory.innerContainer.TryAdd(held,false))
                    {
                        if(held!=null)MealSittingComponent.Current.RecoverUnownedFood(pawn,held);
                        AbundanceCollection.SupplyUnavailable(Ritual,source,Meal);EndJobWith(JobCondition.Incompletable);return;
                    }
                }
                if(held==null || held.stackCount!=count)
                { AbundanceCollection.SupplyUnavailable(Ritual,source,Meal);EndJobWith(JobCondition.Incompletable);return; }
                int debit=count;
                foreach(var entry in Meal.Servings.Where(s=>s.Placed && s.Source==source && s.Count>0).ToList())
                {
                    int amount=Math.Min(debit,entry.Count);entry.Count-=amount;debit-=amount;
                    if(entry.Count==0)entry.Source=null;
                    if(debit==0)break;
                }
                // The native carry split owns its own held-object reservation.
                // Units already extracted must stop reserving the smaller table
                // remainder, or another diner's honest claim can be blocked.
                if(source!=held && source.Spawned && pawn.Map.reservationManager.ReservedBy(source,pawn,job))
                    pawn.Map.reservationManager.Release(source,pawn,job);
                Meal.Servings.Add(new AbundanceServing{Source=held,Count=count,Collected=true,Placed=false,Serving=true,Collector=pawn,
                    OutputDef=held.def,UnitMass=held.GetStatValue(StatDefOf.Mass),UnitBulk=held.def.VolumePerUnit,
                    SourceNutrition=allocation.SourceNutrition,SourceVolume=allocation.SourceVolume,SourceProvider=allocation.SourceProvider});
                bundle.Add(held);collected=true;
            };
            take.defaultCompleteMode=ToilCompleteMode.Instant;yield return take;
            yield return Toils_Jump.Jump(select);
            yield return destination;
            var wait=Toils_General.Wait(30);yield return wait;
            var portion=ToilMaker.MakeToil("SelectOneNativeFeastPortion");
            portion.initAction=()=>
            {
                if(!PromoteOne())EndJobWith(JobCondition.Succeeded);
            };
            portion.defaultCompleteMode=ToilCompleteMode.Instant;yield return portion;
            var chew=FeedOtherUtility.ChewIngestibleWithEatingSpeed(Eater,Eater==pawn ? 1f : FeedOtherUtility.AssistedEatingDurationFactor,TargetIndex.A,TargetIndex.None,1.5f);
            var beginChew=chew.initAction;
            chew.initAction=()=> { FaceTable();beginChew(); };
            var nativeTick=chew.tickIntervalAction;
            chew.tickIntervalAction=delta=> { nativeTick?.Invoke(delta);AbundanceSocial.Converse(pawn,Eater,Meal,Ritual); };
            chew.AddFailCondition(()=>!AbundanceBeds.SameRoom(pawn.Position,Ritual.selectedTarget.Cell,pawn.Map) ||
                (Eater!=pawn && !pawn.Position.InHorDistOf(Eater.Position,2.5f)) ||
                !Ours(job.targetA.Thing) || job.targetA.Thing.stackCount!=1 ||
                !FoodUtility.WillEat(Eater,job.targetA.Thing,pawn,true,false) || !AbundanceMealJobs.Permitted(Eater,job.targetA.Thing));
            yield return chew;
            var finish=Toils_Ingest.FinalizeIngest(Eater,TargetIndex.A);
            Action native=finish.initAction;
            finish.initAction=()=>
            {
                Thing food=job.targetA.Thing;
                if(!Ours(food) || food.stackCount!=1 || !AbundanceMealJobs.Permitted(Eater,food))
                { EndJobWith(JobCondition.Incompletable);return; }
                FaceTable();
                Pawn previous=AbundanceMealJobs.ServedTableEater;
                bool served=Ritual?.FoodReady==true && Eater.Spawned && AbundanceBeds.HasTable(Eater.Map,Ritual.selectedTarget.Cell) &&
                    AbundanceBeds.SameRoom(Eater.Position,Ritual.selectedTarget.Cell,Eater.Map);
                AbundanceMealJobs.ServedTableEater=served ? Eater : null;
                int before=food.stackCount;
                try { native(); }
                finally
                {
                    AbundanceMealJobs.ServedTableEater=previous;
                    int consumed=before-(food.Destroyed ? 0 : food.stackCount);
                    foreach(var claim in Meal.Servings.Where(s=>s.Source==food && s.Collected && !s.Placed && s.Collector==pawn).ToList())
                    {
                        int debit=Math.Min(consumed,claim.Count);claim.Count-=debit;consumed-=debit;
                        if(claim.Count==0)claim.Source=null;
                    }
                    bundle.RemoveAll(t=>t==null || t.Destroyed);
                }
            };
            yield return finish;
            var again=ToilMaker.MakeToil("ContinueFeastBundleAtRecipient");
            again.initAction=()=>
            {
                if(Meal?.Finished!=false || !bundle.Any(t=>Ours(t) && AbundanceMealJobs.Permitted(Eater,t)))
                { EndJobWith(JobCondition.Succeeded);return; }
                JumpToToil(portion);
            };
            again.defaultCompleteMode=ToilCompleteMode.Instant;yield return again;
        }
        private int Capacity(Thing food,int claimed)
        {
            if(food==null || claimed<=0)return 0;
            var supply=new AbundanceServing{Source=food,Count=claimed,OutputDef=food.def,
                UnitMass=food.GetStatValue(StatDefOf.Mass),UnitBulk=food.def.VolumePerUnit};
            int existing=bundle.Where(Ours).Where(t=>t.def==food.def).Sum(t=>t.stackCount);
            return AbundanceCollection.CarryCount(pawn,supply,AbundanceCollection.FreeBulk(pawn),AbundanceCollection.FreeMass(pawn),existing);
        }
        private bool PromoteOne()
        {
            Thing carried=pawn.carryTracker.CarriedThing;
            if(carried!=null && (!Ours(carried) || carried.stackCount!=1))
                if(!pawn.carryTracker.innerContainer.TryTransferToContainer(carried,pawn.inventory.innerContainer,false))return false;
            carried=pawn.carryTracker.CarriedThing;
            if(carried!=null)
            { job.targetA=carried;job.count=1;return AbundanceMealJobs.Permitted(Eater,carried); }
            Thing source=bundle.FirstOrDefault(t=>Ours(t) && pawn.inventory.Contains(t) && AbundanceMealJobs.Permitted(Eater,t));
            if(source==null)return false;
            Thing piece=pawn.inventory.innerContainer.Take(source,1);
            if(piece==null)return false;
            if(piece!=source)
            {
                var original=Meal.Servings.First(s=>s.Source==source && s.Collected && !s.Placed && s.Collector==pawn && s.Count>0);
                original.Count--;if(original.Count==0)original.Source=null;
                Meal.Servings.Add(new AbundanceServing{Source=piece,Count=1,Collected=true,Serving=true,Collector=pawn,OutputDef=piece.def,
                    UnitMass=piece.GetStatValue(StatDefOf.Mass),UnitBulk=piece.def.VolumePerUnit,
                    SourceNutrition=original.SourceNutrition,SourceVolume=original.SourceVolume,SourceProvider=original.SourceProvider});
                bundle.Add(piece);
            }
            if(!pawn.carryTracker.TryStartCarry(piece))
            {
                if(!pawn.inventory.innerContainer.TryAdd(piece,false))MealSittingComponent.Current.RecoverUnownedFood(pawn,piece);
                return false;
            }
            job.targetA=pawn.carryTracker.CarriedThing;job.count=1;return true;
        }
        private void FinishBundle(JobCondition condition)
        {
            var meal=Meal;var ritual=Ritual;
            if(meal!=null)
                foreach(var claim in meal.Servings.Where(s=>s.Collected && !s.Placed && s.Collector==pawn &&
                    (s.Serving || bundle.Contains(s.Source))))claim.Serving=false;
            Thing carried=pawn.carryTracker.CarriedThing;
            if(carried!=null && (bundle.Contains(carried) || layout!=CurrentLayout && collected && job.targetA.Thing==carried))
            {
                if(!pawn.carryTracker.innerContainer.TryTransferToContainer(carried,pawn.inventory.innerContainer,false))
                { ritual?.StopServing(meal); }
                else if(meal!=null && !meal.ReserveUnavailable && !meal.Servings.Any(s=>s.Source==carried && s.Count>0))
                    meal.Servings.Insert(0,new AbundanceServing{Source=carried,Count=carried.stackCount,Collected=true,Collector=pawn,
                        OutputDef=carried.def,UnitMass=carried.GetStatValue(StatDefOf.Mass),UnitBulk=carried.def.VolumePerUnit});
            }
            // Every unused object already has a single saved held allocation and
            // a real owner. The existing set-table dispatcher returns it; cleanup
            // after ritual cancellation still leaves the real inventory intact.
            if(meal!=null && !meal.Finished)
            {
                meal.RetryAfter=Find.TickManager.TicksGame+60;
                if(condition==JobCondition.Incompletable || condition==JobCondition.Errored)
                {
                    if(++meal.FailedAttempts>=3 && ritual!=null)
                        ritual.StopServing(meal);
                }
            }
        }
        private void FaceTable()
        {
            if(Eater.GetPosture()!=PawnPosture.Standing)return;
            IntVec3 cell=GenAdj.CellsAdjacentCardinal(Eater).FirstOrDefault(c=>c.InBounds(Eater.Map) && c.GetThingList(Eater.Map).Any(FeastCompatibilityPolicy.IsDiningSurface));
            if(cell.IsValid && cell!=IntVec3.Zero)Eater.rotationTracker.FaceCell(cell);
        }
    }
}
