using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Ideology;
using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace Oinky.JunkFood
{
    // Preparation owns no intake. The existing food jobs remain responsible for
    // selecting, collecting and eating the real child stack after preparation.
    public static class AutomaticPackageServing
    {
        public const string JobName = "Oinky_AutoOpenPackage";
        public const int RetryTicks = 2500;
        private const float StorageRadius = 8f;
        private static int Now => Find.TickManager?.TicksGame ?? 0;

        public static bool IsPackage(Thing food) =>
            food?.def.GetModExtension<JunkFoodExtension>()?.portionDef != null &&
            food.TryGetComp<CompJunkFoodPackage>() != null;

        public static bool ActorReady(Pawn pawn) => pawn?.Spawned == true &&
            pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Humanlike && !pawn.Dead &&
            !pawn.Downed && !pawn.Drafted && !pawn.InMentalState &&
            pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
            pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving);

        public static bool HasFeastClaim(Thing food) => food?.Map?.lordManager != null &&
            food.Map.lordManager.lords.Select(l => l.LordJob).OfType<LordJob_AbundanceMeals>()
                .Any(r => !r.cancelled && !r.CancelRequested && r.Meals != null &&
                    r.Meals.Any(m => m.Servings != null &&
                        m.Servings.Any(s => s.Source == food && s.Count > 0)));

        private static bool Unclaimed(Pawn actor, Thing food, Job ownJob = null)
        {
            if (food?.Spawned != true || HasFeastClaim(food)) return false;
            // A logical ritual claim can precede a native reservation. Neither
            // may be replaced with child units behind its owner's back.
            if (food.Map.reservationManager.ReservationsReadOnly.Any(r =>
                r.Target.Thing == food && (r.Claimant != actor || r.Job != ownJob))) return false;
            var physical = new HashSet<Pawn>();
            food.Map.physicalInteractionReservationManager.ReserversOf(food, physical);
            return physical.All(p => ownJob != null && p == actor);
        }

        private static bool SourceReady(Pawn actor, Thing package, Job ownJob)
        {
            if (!ActorReady(actor) || !IsPackage(package) || !PackageServing.CanOpen(package) ||
                package.Map != actor.Map || package.IsForbidden(actor) || package.IsBurning() ||
                !package.IsSociallyProper(actor) || !FactionUtility.IsPoliticallyProper(package, actor) ||
                !Unclaimed(actor, package, ownJob)) return false;
            var comp = package.TryGetComp<CompJunkFoodPackage>();
            return (ownJob != null || Now >= comp.AutoOpenRetryAfterTick) &&
                actor.CanReserveAndReach(package, PathEndMode.Touch, actor.NormalMaxDanger(), 1, 1);
        }

        public static void Defer(Thing package)
        {
            var comp = package?.TryGetComp<CompJunkFoodPackage>();
            if (comp != null) comp.AutoOpenRetryAfterTick = Now + RetryTicks;
        }

        private static bool ChildAllowed(Pawn actor, Pawn eater, Thing child)
        {
            if (eater?.Spawned != true || eater.Map != actor.Map || eater.Dead ||
                eater.needs?.food == null || !BodyModelEligibility.UsesFoodMetabolism(eater) ||
                !child.IngestibleNow ||
                !FoodUtility.WillEat(eater, child, actor, true, false)) return false;
            return !OrdinaryFoodIntake.Applies(eater, child) ||
                OrdinaryFoodIntake.CanOrderServing(eater, child);
        }

        private static bool StorageAccepts(Pawn actor, IntVec3 cell, Thing portions)
        {
            Map map = actor.Map;
            return cell.InBounds(map) && !cell.Fogged(map) &&
                !cell.GetThingList(map).Any(t => t.def == portions.def) &&
                cell.IsValidStorageFor(map, portions) &&
                StoreUtility.IsGoodStoreCell(cell, map, portions, actor, actor.Faction) &&
                actor.CanReach(cell, PathEndMode.ClosestTouch, actor.NormalMaxDanger());
        }

        private static bool FindOutputCell(Pawn actor, Thing package, Thing portions, out IntVec3 cell)
        {
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(package.Position, StorageRadius, true))
                if (StorageAccepts(actor, candidate, portions)) { cell = candidate; return true; }
            cell = IntVec3.Invalid;
            return false;
        }

        public static bool CanPrepare(Pawn actor, Thing package, Pawn eater,
            out IntVec3 destination, Job ownJob = null)
        {
            destination = IntVec3.Invalid;
            if (!SourceReady(actor, package, ownJob) ||
                !PackageServing.TryMakePortions(package, out Thing preview)) return false;
            try
            {
                return ChildAllowed(actor, eater, preview) &&
                    FindOutputCell(actor, package, preview, out destination);
            }
            finally { preview.Destroy(DestroyMode.Vanish); }
        }

        public static bool TryOpenFor(Pawn actor, Thing package, Pawn eater, Job ownJob, out Thing servings)
        {
            servings = null;
            if (!CanPrepare(actor, package, eater, out IntVec3 destination, ownJob)) return false;
            // Another opener may have completed during our walk/work. Do not
            // turn several simultaneous requests into a pantry-wide unpacking.
            if (AvailableChild(actor, eater, package.def.GetModExtension<JunkFoodExtension>().portionDef) != null)
                return false;
            return PackageServing.TryOpenAt(package, destination,
                (cell, portions) => ChildAllowed(actor, eater, portions) && StorageAccepts(actor, cell, portions),
                out servings);
        }

        // Shared snacks have their own live purpose and, for a mental binge,
        // native restriction exceptions. Ordinary preparation keeps its original
        // ActorReady/ChildAllowed contract above.
        internal static bool CanPrepareShared(Pawn actor, Thing package, JunkSnackPurpose purpose,
            bool prospectiveBinge, out IntVec3 destination, Job ownJob = null)
        {
            destination = IntVec3.Invalid;
            if (!PackageServing.CanOpen(package) ||
                !JunkSnackUtility.SourceAllowed(actor, package, purpose, prospectiveBinge, ownJob) ||
                !PackageServing.TryMakePortions(package, out Thing preview)) return false;
            try
            {
                RimRound.Simulation.MealPortionPlan plan;
                return JunkSnackUtility.Preview(actor, preview, purpose, out plan, false, prospectiveBinge) &&
                    FindOutputCell(actor, package, preview, out destination);
            }
            finally { preview.Destroy(); }
        }

        internal static bool TryOpenShared(Pawn actor, Thing package, JunkSnackPurpose purpose,
            Job ownJob, out Thing servings)
        {
            servings = null;
            if (!CanPrepareShared(actor, package, purpose, false, out IntVec3 destination, ownJob)) return false;
            return PackageServing.TryOpenAt(package, destination, (cell, portions) =>
            {
                RimRound.Simulation.MealPortionPlan plan;
                return JunkSnackUtility.Preview(actor, portions, purpose, out plan) && StorageAccepts(actor, cell, portions);
            }, out servings);
        }

        private static Thing AvailableChild(Pawn actor, Pawn eater, ThingDef childDef)
        {
            if (actor?.Map == null) return null;
            return actor.Map.listerThings.ThingsOfDef(childDef)
                .Where(t => t.Spawned && t.stackCount > 0 && t.Position.InHorDistOf(actor.Position, 60f) &&
                    StoreUtility.IsInValidStorage(t) && !t.IsForbidden(actor) && Unclaimed(actor, t) &&
                    t.IsSociallyProper(actor) && FactionUtility.IsPoliticallyProper(t, actor) &&
                    ChildAllowed(actor, eater, t) &&
                    actor.CanReserveAndReach(t, PathEndMode.ClosestTouch, actor.NormalMaxDanger(), 1, 1))
                .OrderBy(t => actor.Position.DistanceToSquared(t.Position)).ThenBy(t => t.thingIDNumber)
                .FirstOrDefault();
        }

        private static Job OpeningJob(Thing package, Pawn eater)
        {
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(JobName), package, eater);
            job.count = 1;
            return job;
        }

        // Called only after the ordinary food giver has selected an Ingest job.
        // Existing servings are preferred to opening another package; all other
        // native decisions and the ninety ordinary foods pass through unchanged.
        public static Job ForSelfFoodJob(Pawn pawn, Job original)
        {
            Thing package = original?.targetA.Thing;
            if (original?.def != JobDefOf.Ingest || original.playerForced ||
                !ActorReady(pawn) || !IsPackage(package) || package.Spawned != true ||
                package.Map != pawn.Map) return original;
            Thing child = AvailableChild(pawn, pawn, package.def.GetModExtension<JunkFoodExtension>().portionDef);
            if (child != null)
            {
                int count = Math.Min(child.stackCount, FoodUtility.WillIngestStackCountOf(pawn,
                    child.def, FoodUtility.NutritionForEater(pawn, child)));
                if (count <= 0) return original;
                original.targetA = child;
                original.count = count;
                return original;
            }
            return CanPrepare(pawn, package, pawn, out _) ? OpeningJob(package, pawn) : original;
        }

        public static Pawn PreparationRecipient(Pawn actor, Thing package)
        {
            if (!SourceReady(actor, package, null) || !StoreUtility.IsInValidStorage(package)) return null;
            ThingDef childDef = package.def.GetModExtension<JunkFoodExtension>().portionDef;
            // One open stack suffices. Hungry residents (including patients and
            // prisoners) provide actual demand, without an endless pantry sweep.
            foreach (Pawn eater in actor.Map.mapPawns.AllPawnsSpawned)
            {
                if (eater.Dead || (eater.Faction != Faction.OfPlayer && eater.HostFaction != Faction.OfPlayer) ||
                    eater.RaceProps?.Humanlike != true || eater.needs?.food == null ||
                    eater.needs.food.CurLevelPercentage > eater.RaceProps.FoodLevelPercentageWantEat ||
                    AvailableChild(actor, eater, childDef) != null) continue;
                if (CanPrepare(actor, package, eater, out _)) return eater;
            }
            return null;
        }

        public static Job ForPreparation(Pawn actor, Thing package)
        {
            Pawn eater = PreparationRecipient(actor, package);
            return eater == null ? null : OpeningJob(package, eater);
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    internal static class AutomaticPackageHungerJob
    {
        // Let the companion finish its source adapters, then run before the
        // parent's final Gain binding/count pass. An opener is not an Ingest job.
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("RimRound.FeedOther")]
        [HarmonyBefore("RRHarmony")]
        private static void Postfix(Pawn pawn, ref Job __result)
        { __result = AutomaticPackageServing.ForSelfFoodJob(pawn, __result); }
    }

    public sealed class WorkGiver_OpenJunkFoodPackage : WorkGiver_Scanner
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            !AutomaticPackageServing.ActorReady(pawn) ? Enumerable.Empty<Thing>() :
                pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree)
                    .Where(AutomaticPackageServing.IsPackage);

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            AutomaticPackageServing.PreparationRecipient(pawn, t) != null;

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            AutomaticPackageServing.ForPreparation(pawn, t);
    }

    public sealed class JobDriver_AutoOpenPackage : JobDriver
    {
        private bool opened;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref opened, "junkFoodPackageOpened");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (opened) return true;
            bool ready = AutomaticPackageServing.CanPrepare(pawn, job.targetA.Thing, job.targetB.Pawn,
                out _, job) && pawn.Reserve(job.targetA, job, 1, 1, null, errorOnFailed);
            if (!ready) AutomaticPackageServing.Defer(job.targetA.Thing);
            return ready;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !opened && (job.targetA.Thing?.Spawned != true ||
                job.targetA.Thing.IsForbidden(pawn) || !AutomaticPackageServing.ActorReady(pawn)));
            AddFinishAction(condition =>
            {
                if (!opened) AutomaticPackageServing.Defer(job.targetA.Thing);
            });
            Toil commit = Toils_General.Do(() =>
            {
                if (opened) return;
                if (!AutomaticPackageServing.TryOpenFor(pawn, job.targetA.Thing, job.targetB.Pawn,
                    job, out Thing servings))
                {
                    AutomaticPackageServing.Defer(job.targetA.Thing);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                opened = true;
                job.targetC = servings;
                // AvailableChild suppresses redundant preparation. A successful
                // opening must not delay a genuinely needed second package once
                // these servings have all been consumed or claimed.
            });
            yield return Toils_Jump.JumpIf(commit, () => opened);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(60).WithProgressBarToilDelay(TargetIndex.A);
            yield return commit;
        }
    }
}
