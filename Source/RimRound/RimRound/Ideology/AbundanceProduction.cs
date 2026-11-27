using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace RimRound.Ideology
{
    public sealed class AbundanceYieldRemainder : IExposable
    {
        public Pawn worker;
        public ThingDef product;
        // Twentieths retain old 20% fractional progress exactly while new food
        // contributes one quarter. A loaded 4/5 item remains 16/20, not 0 or 1.
        public int twentieths;
        public void ExposeData()
        {
            Scribe_References.Look(ref worker,"worker"); Scribe_Defs.Look(ref product,"product");
            Scribe_Values.Look(ref twentieths,"twentieths",-1);
            if(Scribe.mode==LoadSaveMode.LoadingVars && twentieths<0)
            {
                int legacyFifths=0;
                Scribe_Values.Look(ref legacyFifths,"fifths");
                twentieths=legacyFifths>=0 && legacyFifths<5 ? legacyFifths*4 : -1;
            }
        }
    }
    public sealed class AbundanceCaretaker : IExposable
    {
        public Pawn animal, worker;
        public void ExposeData()
        { Scribe_References.Look(ref animal,"animal"); Scribe_References.Look(ref worker,"worker"); }
    }
    public sealed class AbundanceProduction : GameComponent
    {
        private List<AbundanceYieldRemainder> remainders = new List<AbundanceYieldRemainder>();
        private List<AbundanceCaretaker> caretakers = new List<AbundanceCaretaker>();
        public AbundanceProduction(Game game) { }
        public static AbundanceProduction Instance => Current.Game?.GetComponent<AbundanceProduction>();
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref remainders,"rrProvisionerRemainders",LookMode.Deep);
            Scribe_Collections.Look(ref caretakers,"rrProvisionerCaretakers",LookMode.Deep);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)
            {
                if(remainders==null)remainders=new List<AbundanceYieldRemainder>();
                if(caretakers==null)caretakers=new List<AbundanceCaretaker>();
                remainders.RemoveAll(r=>r?.worker==null || r.product==null || r.twentieths<0 || r.twentieths>=20);
                caretakers.RemoveAll(r=>r?.animal==null || r.worker==null);
            }
        }
        public override void GameComponentTick()
        {
            if(Find.TickManager.TicksGame%60000!=0)return;
            remainders.RemoveAll(r=>r.worker==null || r.worker.Destroyed || r.worker.Dead);
            caretakers.RemoveAll(r=>r.animal==null || r.animal.Destroyed || r.animal.Dead || r.worker==null || r.worker.Destroyed || r.worker.Dead);
        }
        public static bool Edible(Thing t) => t?.def?.IsNutritionGivingIngestible==true && !t.def.IsDrug;
        public void Increase(Pawn worker,Thing product)
        {
            if(!AbundanceRoles.Has(worker,AbundanceRoles.Provisioner) || !Edible(product) || product.stackCount<=0)return;
            var record=remainders.FirstOrDefault(r=>r.worker==worker && r.product==product.def);
            if(record==null) { record=new AbundanceYieldRemainder{worker=worker,product=product.def}; remainders.Add(record); }
            long twentieths=(long)product.stackCount*5+record.twentieths;
            int bonus=(int)Math.Min(int.MaxValue-product.stackCount,twentieths/20);
            record.twentieths=(int)(twentieths%20);
            product.stackCount+=bonus;
        }
        public Pawn Caretaker(Pawn animal) => caretakers.FirstOrDefault(r=>r.animal==animal)?.worker;
        public void AssignCaretaker(Pawn animal,Pawn worker)
        {
            caretakers.RemoveAll(r=>r.animal==animal);
            if(worker!=null)caretakers.Add(new AbundanceCaretaker{animal=animal,worker=worker});
        }
        public Pawn ActiveCaretaker(Pawn animal)
        {
            Pawn worker=Caretaker(animal);
            return animal?.Spawned==true && worker?.Spawned==true && animal.Map==worker.Map &&
                animal.Faction==worker.Faction && !worker.Downed && !worker.InMentalState &&
                AbundanceRoles.Has(worker,AbundanceRoles.Provisioner) ? worker : null;
        }
    }

    // Scope exists only while a native harvest/gather completion is placing its
    // new products. Ordinary hauling, cooking and storage never enter this scope.
    internal sealed class AbundanceProductionScope : IDisposable
    {
        [ThreadStatic] internal static AbundanceProductionScope Current;
        internal readonly Pawn Worker;
        internal readonly HashSet<Thing> Products=new HashSet<Thing>();
        private readonly AbundanceProductionScope previous;
        internal AbundanceProductionScope(Pawn worker) { Worker=worker; previous=Current; Current=this; }
        public void Dispose() { Current=previous; }
    }
    [HarmonyPatch(typeof(JobDriver_PlantWork),"MakeNewToils")]
    internal static class AbundanceHarvest
    {
        private static IEnumerable<Toil> Postfix(IEnumerable<Toil> values,JobDriver_PlantWork __instance)
        {
            foreach(Toil toil in values)
            {
                var tick=toil.tickIntervalAction;
                if(tick!=null)toil.tickIntervalAction=delta=>
                { using(new AbundanceProductionScope(__instance.pawn))tick(delta); };
                yield return toil;
            }
        }
    }
    [HarmonyPatch(typeof(CompHasGatherableBodyResource),nameof(CompHasGatherableBodyResource.Gathered))]
    internal static class AbundanceAnimalGather
    {
        private static void Prefix(Pawn doer,out AbundanceProductionScope __state) { __state=new AbundanceProductionScope(doer); }
        private static void Finalizer(AbundanceProductionScope __state) { __state?.Dispose(); }
    }
    [HarmonyPatch]
    internal static class AbundancePlaceHarvest
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(GenPlace).GetMethods().Where(m=>m.Name=="TryPlaceThing" && m.GetParameters()[0].ParameterType==typeof(Thing));
        private static void Prefix(Thing __0)
        {
            var scope=AbundanceProductionScope.Current;
            if(scope!=null && __0!=null && !__0.Spawned && __0.ParentHolder==null && scope.Products.Add(__0))
                AbundanceProduction.Instance?.Increase(scope.Worker,__0);
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.ButcherProducts))]
    internal static class AbundanceButchery
    {
        private static IEnumerable<Thing> Postfix(IEnumerable<Thing> values,Pawn butcher)
        {
            foreach(Thing product in values)
            {
                if(butcher?.CurJob?.def==JobDefOf.DoBill) AbundanceProduction.Instance?.Increase(butcher,product);
                yield return product;
            }
        }
    }
    [HarmonyPatch(typeof(CompEggLayer),nameof(CompEggLayer.ProduceEgg))]
    internal static class AbundanceEggs
    {
        private static void Postfix(CompEggLayer __instance,Thing __result)
        {
            var production=AbundanceProduction.Instance;
            production?.Increase(production.ActiveCaretaker(__instance.parent as Pawn),__result);
        }
    }
    [HarmonyPatch(typeof(JobDriver_Fish),"CompleteFishingToil")]
    internal static class AbundanceFishing
    {
        private static void Postfix(JobDriver_Fish __instance,Toil __result)
        {
            var action=__result?.initAction;
            if(action!=null) __result.initAction=()=>
            { using(new AbundanceProductionScope(__instance.pawn))action(); };
        }
    }
    [HarmonyPatch(typeof(ForagedFoodPerDayCalculator),nameof(ForagedFoodPerDayCalculator.GetBaseForagedNutritionPerDay))]
    internal static class AbundanceForaging
    {
        private static void Postfix(Pawn p,ref float __result)
        { if(__result>0f && AbundanceRoles.Has(p,AbundanceRoles.Provisioner))__result*=1.25f; }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.GetGizmos))]
    internal static class AbundanceCaretakerGizmo
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values,Pawn __instance)
        {
            foreach(Gizmo gizmo in values)yield return gizmo;
            Pawn animal=__instance;
            if(!ModsConfig.IdeologyActive || animal?.Faction!=Faction.OfPlayer || !animal.Spawned || animal.TryGetComp<CompEggLayer>()==null)yield break;
            var manager=AbundanceProduction.Instance;
            if(manager==null)yield break;
            var workers=animal.Map.mapPawns.FreeColonistsSpawned.Where(p=>AbundanceRoles.Has(p,AbundanceRoles.Provisioner)).ToList();
            if(workers.Count==0 && manager.Caretaker(animal)==null)yield break;
            yield return new Command_Action {
                defaultLabel="RR_ProvisionerCaretaker".Translate(manager.Caretaker(animal)?.LabelShort ?? "None".Translate()),
                defaultDesc="RR_ProvisionerCaretakerDesc".Translate(),
                icon=DefDatabase<PreceptDef>.GetNamed(AbundanceRoles.Provisioner).Icon,
                action=()=>{
                    var options=workers.Select(p=>new FloatMenuOption(p.LabelShort,()=>manager.AssignCaretaker(animal,p))).ToList();
                    options.Add(new FloatMenuOption("None".Translate(),()=>manager.AssignCaretaker(animal,null)));
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
        }
    }
}
