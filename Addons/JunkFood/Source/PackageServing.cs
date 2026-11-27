using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Oinky.JunkFood
{
    public sealed class CompProperties_JunkFoodPackage : CompProperties
    {
        public CompProperties_JunkFoodPackage() { compClass = typeof(CompJunkFoodPackage); }
    }

    // No ingestion callbacks: ordinary fractional food accounting remains owned
    // by RimRound. Opening a package is a one-way inventory operation, not cooking.
    public sealed class CompJunkFoodPackage : ThingComp
    {
        public int AutoOpenRetryAfterTick;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref AutoOpenRetryAfterTick, "junkFoodAutoOpenRetryAfterTick");
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (var option in base.CompFloatMenuOptions(selPawn)) yield return option;
            var ext = parent.def.GetModExtension<JunkFoodExtension>();
            if (selPawn?.Faction != Faction.OfPlayer || selPawn.RaceProps?.Humanlike != true ||
                ext?.portionDef == null || ext.portions < 1 || !parent.Spawned) yield break;
            string label = "Open one " + parent.LabelNoCount + " (" + ext.portions + " servings)";
            if (!selPawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                yield return new FloatMenuOption(label + ": incapable of manipulation", null);
                yield break;
            }
            if (!selPawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some, 1, 1))
            {
                yield return new FloatMenuOption(label + ": cannot reach or reserve", null);
                yield break;
            }
            yield return new FloatMenuOption(label, () =>
            {
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Oinky_OpenPackage"), parent);
                job.count = 1;
                selPawn.jobs.TryTakeOrderedJob(job);
            });
        }

        public override string CompInspectStringExtra()
        {
            var ext = parent.def.GetModExtension<JunkFoodExtension>();
            return ext?.portionDef == null ? null : "Contains " + ext.portions + " × " + ext.portionDef.label +
                ". Select a colonist and right-click to open.";
        }
    }

    public sealed class JobDriver_OpenPackage : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        { return pawn.Reserve(job.targetA, job, 1, 1, null, errorOnFailed); }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !PackageServing.CanOpen(job.targetA.Thing));
            this.FailOn(() => !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(60).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() =>
            {
                if (!PackageServing.TryOpen(job.targetA.Thing))
                    Messages.Message("There is no room to put the opened servings nearby.", pawn,
                        MessageTypeDefOf.RejectInput, false);
            });
        }
    }

    public static class PackageServing
    {
        public static bool CanOpen(Thing package)
        { return CanOpenPackage(package, false); }

        private static bool CanOpenPackage(Thing package, bool allowHeld)
        {
            var ext = package?.def.GetModExtension<JunkFoodExtension>();
            return package != null && (package.Spawned || (allowHeld && package.ParentHolder is Pawn_InventoryTracker)) &&
                !package.Destroyed && package.stackCount > 0 &&
                package.IngestibleNow && ext?.portionDef != null && ext.portions > 0 &&
                ext.portions <= ext.portionDef.stackLimit && PairConserves(package.def, ext);
        }

        // A malformed third-party patch cannot turn opening into a calorie copier.
        public static bool PairConserves(ThingDef whole, JunkFoodExtension ext)
        {
            if (whole == null || ext?.portionDef == null || ext.portions <= 0) return false;
            double energy = whole.GetStatValueAbstract(StatDefOf.Nutrition);
            double portionsEnergy = ext.portionDef.GetStatValueAbstract(StatDefOf.Nutrition) * ext.portions;
            var a = whole.GetCompProperties<RimRound.Comps.CompProperties_FoodItems_NutritionDensity>();
            var b = ext.portionDef.GetCompProperties<RimRound.Comps.CompProperties_FoodItems_NutritionDensity>();
            return a != null && b != null && Close(ext.ServingUnitsPerItem,
                    (ext.portionDef.GetModExtension<JunkFoodExtension>()?.ServingUnitsPerItem ?? 0d) * ext.portions) &&
                Close(energy, portionsEnergy) &&
                Close(energy * a.fullnessToNutritionRatio, portionsEnergy * b.fullnessToNutritionRatio) &&
                Close(whole.GetStatValueAbstract(StatDefOf.Mass),
                    ext.portionDef.GetStatValueAbstract(StatDefOf.Mass) * ext.portions);
        }

        static bool Close(double a, double b) => !double.IsNaN(a) && !double.IsInfinity(a) &&
            !double.IsNaN(b) && !double.IsInfinity(b) && a > 0d && b > 0d &&
            Math.Abs(a - b) <= Math.Max(a, b) * 0.00001d;

        public static bool TryOpen(Thing package)
        { return TryOpen(package, out _); }

        public static bool TryOpen(Thing package, out Thing servings)
        { return TryOpen(package, package?.Position ?? IntVec3.Invalid, ThingPlaceMode.Near, null, out servings); }

        // Automatic callers choose a real accepting storage cell first. Keep
        // the manual Near placement API and its existing behavior unchanged.
        internal static bool TryOpenAt(Thing package, IntVec3 cell,
            Func<IntVec3, Thing, bool> accepts, out Thing servings)
        { return TryOpen(package, cell, ThingPlaceMode.Direct, accepts, out servings); }

        internal static bool TryMakePortions(Thing package, out Thing portion, bool allowHeld = false)
        {
            portion = null;
            if (!CanOpenPackage(package, allowHeld)) return false;
            var ext = package.def.GetModExtension<JunkFoodExtension>();
            portion = ThingMaker.MakeThing(ext.portionDef);
            portion.stackCount = ext.portions;
            var sourceRot = package.TryGetComp<CompRottable>();
            var targetRot = portion.TryGetComp<CompRottable>();
            if (sourceRot == null || targetRot == null ||
                package.TryGetComp<CompFoodPoisonable>() == null || portion.TryGetComp<CompFoodPoisonable>() == null ||
                package.TryGetComp<CompIngredients>() == null || portion.TryGetComp<CompIngredients>() == null ||
                (package.TryGetComp<CompMetalhorrorInfectible>() != null && portion.TryGetComp<CompMetalhorrorInfectible>() == null))
            { portion.Destroy(); portion = null; return false; }

            // These copies do not mutate the input. Initialize them before Spawn
            // so any native food-kind/cache query sees the actual ingredients.
            double remaining = sourceRot.PropsRot.TicksToRotStart - sourceRot.RotProgress;
            targetRot.RotProgress = Mathf.Max(0f, targetRot.PropsRot.TicksToRotStart - (float)remaining);
            package.TryGetComp<CompFoodPoisonable>().PostSplitOff(portion);
            package.TryGetComp<CompIngredients>().PostSplitOff(portion);
            portion.HitPoints = Mathf.Clamp(Mathf.RoundToInt(
                package.HitPoints / (float)package.MaxHitPoints * portion.MaxHitPoints), 1, portion.MaxHitPoints);
            return true;
        }

        // The holder has to accept the real unmerged children before the source
        // is debited. This works in a caravan without materializing map stock.
        internal static bool TryOpenHeld(Thing package, out Thing servings)
        {
            servings = null;
            var inventory = package?.ParentHolder as Pawn_InventoryTracker;
            if (inventory == null || !TryMakePortions(package, out Thing portion, true)) return false;
            if (!inventory.innerContainer.TryAdd(portion, false))
            { if (!portion.Destroyed) portion.Destroy(); return false; }
            if (!inventory.innerContainer.Contains(portion) || package.ParentHolder != inventory || package.Destroyed)
            { if (!portion.Destroyed) portion.Destroy(); return false; }
            Thing opened = package.SplitOff(1);
            portion.TryGetComp<CompMetalhorrorInfectible>()?.PreAbsorbStack(opened, 1);
            opened.Destroy(DestroyMode.Vanish);
            servings = portion;
            if (inventory.pawn.Map != null) ColonyFoodReserve.NotifyFoodChanged(inventory.pawn.Map);
            return true;
        }

        private static bool TryOpen(Thing package, IntVec3 location, ThingPlaceMode mode,
            Func<IntVec3, Thing, bool> accepts, out Thing servings)
        {
            servings = null;
            // Master-off deliberately permits opening existing stock, just as it
            // permits eating it. No recipe/stock/reward generation occurs here.
            if (!TryMakePortions(package, out Thing portion)) return false;
            Map map = package.Map;

            // Native Direct placement does not call extraValidator. Validate
            // its actual metadata-bearing output explicitly before spawning.
            if (mode == ThingPlaceMode.Direct && (!location.InBounds(map) ||
                location.GetThingList(map).Any(t => t.def == portion.def) ||
                (accepts != null && !accepts(location, portion))))
            { portion.Destroy(DestroyMode.Vanish); return false; }

            // Do not merge with another stack: native merging can average age and
            // poisoning. Reserve a fresh output stack before debiting the package.
            Thing placed;
            if (!GenPlace.TryPlaceThing(portion, location, map, mode, out placed,
                extraValidator: cell => !cell.GetThingList(map).Any(t => t.def == portion.def) &&
                    (accepts == null || accepts(cell, portion))))
            { if (!portion.Destroyed) portion.Destroy(); return false; }

            // Automatic placement must be usable storage before any source is
            // debited. A failed placement leaves the original package edible.
            if (accepts != null && (!ReferenceEquals(placed, portion) ||
                !StoreUtility.IsInValidStorage(portion)))
            { if (!portion.Destroyed) portion.Destroy(); return false; }

            // This entire commit is synchronous. SplitOff transfers at most the
            // selected package's native infection ownership out of a larger stack.
            Thing opened = package.SplitOff(1);
            // Transfer the infector list once to the single output stack. Do not
            // clone it into each serving and do not run Notify_RecipeProduced.
            var infection = portion.TryGetComp<CompMetalhorrorInfectible>();
            if (infection != null) infection.PreAbsorbStack(opened, 1);
            opened.Destroy(DestroyMode.Vanish);
            servings = placed;
            ColonyFoodReserve.NotifyFoodChanged(map);
            return true;
        }
    }
}
