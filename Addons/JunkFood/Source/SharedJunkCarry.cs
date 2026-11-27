using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace Oinky.JunkFood
{
    public static class JunkCarryUtility
    {
        public const string JobName = "Oinky_CarryJunkSnacks";
        // A persistent keep allowance is separate from permission to fetch now.
        // Drafting, care, incapacitation and return-to-map unloading must not
        // cause stored snacks to be discarded and fetched again later.
        public static int Quota(Pawn pawn) => JunkSharedRules.Enabled && JunkSharedRules.AdultKind(pawn) &&
            pawn.Faction == Faction.OfPlayer && pawn.inventory != null
            ? JunkFoodPolicy.Colony.PolicyFor(pawn)?.Carry ?? 0 : 0;
        private static bool Eligible(Pawn pawn, Thing food) => JunkSnackUtility.Suitable(pawn, food);
        public static double HeldUnits(Pawn pawn) => pawn.inventory.innerContainer
            .Where(t => Eligible(pawn, t)).Sum(t => JunkSnackUtility.Units(t.def) * t.stackCount);
        public static int Wanted(Pawn pawn, Thing food)
        {
            int quota = Quota(pawn);
            if (quota <= 0 || !Eligible(pawn, food)) return 0;
            return Math.Min(food.stackCount, (int)Math.Floor(Math.Max(0d, quota - HeldUnits(pawn)) / JunkSnackUtility.Units(food.def)));
        }
        // Additional group keeps only. Eligible items already retained by the
        // native policy count toward this target, even when they are in a
        // different physical stack or a package larger than the group target.
        public static Dictionary<Thing, int> Allocation(Pawn pawn) => Quota(pawn) > 0
            ? AdditionalAllocation(pawn, NativeRetention(pawn)) : new Dictionary<Thing, int>();

        internal static Dictionary<Thing, int> AdditionalAllocation(Pawn pawn, Dictionary<Thing, int> native)
        {
            var result = new Dictionary<Thing, int>();
            double remaining = Quota(pawn);
            if (remaining <= 0d) return result;
            foreach (var kept in native)
                if (Eligible(pawn, kept.Key)) remaining -= UnitsKept(kept.Key, kept.Value);
            if (remaining <= 0d) return result;
            foreach (var food in pawn.inventory.innerContainer.Where(t => Eligible(pawn, t))
                .OrderBy(t => JunkSnackUtility.Units(t.def)).ThenBy(t => t.thingIDNumber))
            {
                double units = JunkSnackUtility.Units(food.def);
                int alreadyKept; native.TryGetValue(food, out alreadyKept);
                int count = Math.Min(food.stackCount - alreadyKept, (int)Math.Floor(remaining / units));
                if (count <= 0) continue;
                result.Add(food, count); remaining -= count * units;
            }
            return result;
        }

        private static double UnitsKept(Thing food, int count) => JunkSnackUtility.Units(food.def) * count;

        // Preserve native 1.6's calculation in container order. Calculate every
        // stack before assigning the group so native kept food anywhere in the
        // inventory can satisfy the single shared target.
        internal static Dictionary<Thing, int> NativeRetention(Pawn pawn)
        {
            var result = new Dictionary<Thing, int>();
            if (pawn?.inventory == null) return result;
            var keep = new List<ThingDefCount>();
            DrugPolicy policy = pawn.drugs?.CurrentPolicy;
            if (policy != null)
                for (int i = 0; i < policy.Count; i++)
                    if (policy[i].takeToInventory > 0) keep.Add(new ThingDefCount(policy[i].drug, policy[i].takeToInventory));
            if (pawn.inventoryStock?.stockEntries != null)
                foreach (var entry in pawn.inventoryStock.stockEntries.Values) keep.Add(new ThingDefCount(entry.thingDef, entry.count));
            foreach (Thing item in pawn.inventory.innerContainer)
            {
                int index = keep.FindIndex(k => k.ThingDef == item.def);
                if (pawn.IsColonist && item.def.IsNutritionGivingIngestible && !item.def.IsDrug &&
                    JobGiver_PackFood.IsGoodPackableFoodFor(item, pawn, false))
                {
                    float total = JobGiver_PackFood.GetInventoryPackableFoodNutrition(pawn);
                    float nutrition = item.GetStatValue(StatDefOf.Nutrition);
                    float maximum = pawn.needs.food.MaxLevel;
                    if (nutrition > 0f && total - nutrition * item.stackCount <= maximum)
                    {
                        int discard = 0;
                        while (total - nutrition * discard > maximum) discard++;
                        if (item.stackCount - discard > 0)
                        { keep.Add(new ThingDefCount(item.def, item.stackCount - discard)); index = keep.Count - 1; }
                    }
                }
                int native = index >= 0 ? Math.Max(0, keep[index].Count) : 0;
                int retained = Math.Min(item.stackCount, native);
                if (retained > 0) result.Add(item, retained);
                if (index >= 0) keep[index] = new ThingDefCount(keep[index].ThingDef, Math.Max(0, native - item.stackCount));
            }
            return result;
        }
        public static Job TryJob(Pawn pawn)
        {
            if (!JunkSnackUtility.Ready(pawn) || Quota(pawn) <= 0 || pawn.inventory.UnloadEverything) return null;
            var row = JunkFoodPolicy.Colony.SharedRecord(pawn);
            if (JunkSharedRules.Now < row.CarryRetryAfterTick) return null;
            row.CarryRetryAfterTick = JunkSharedRules.Now + 600;
            foreach (Thing food in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree)
                .Where(t => JunkFoodPolicy.IsJunk(t.def)).OrderBy(t => JunkSnackUtility.Units(t.def))
                .ThenBy(t => pawn.Position.DistanceToSquared(t.Position)).ThenBy(t => t.thingIDNumber))
            {
                int count = Wanted(pawn, food);
                if (count <= 0 || !JunkSnackUtility.SourceAllowed(pawn, food, JunkSnackPurpose.Schedule) ||
                    !ColonyFoodReserve.CanMoveToInventory(pawn, food, count)) continue;
                Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(JobName), food); job.count = count;
                return job;
            }
            return null;
        }
    }

    public sealed class JobDriver_CarryJunkSnacks : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, job.count, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !JunkSnackUtility.Ready(pawn) || JunkCarryUtility.Quota(pawn) <= 0 || pawn.inventory.UnloadEverything ||
                !JunkSnackUtility.SourceAllowed(pawn, job.targetA.Thing, JunkSnackPurpose.Schedule, false, job));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            var take = Toils_Haul.TakeToInventory(TargetIndex.A, job.count);
            Action original = take.initAction;
            take.initAction = () =>
            {
                Thing food = job.targetA.Thing;
                if (food == null || JunkCarryUtility.Wanted(pawn, food) < job.count ||
                    !ColonyFoodReserve.CanMoveToInventory(pawn, food, job.count))
                { EndJobWith(JobCondition.Incompletable); return; }
                original();
                ColonyFoodReserve.NotifyFoodChanged(pawn.Map);
            };
            yield return take;
        }
    }

    [HarmonyPatch(typeof(JobGiver_TakeForInventoryStock), "TryGiveJob")]
    internal static class SharedJunkCarryFetch
    { static void Postfix(Pawn pawn, ref Job __result) { if (__result == null) __result = JunkCarryUtility.TryJob(pawn); } }

    [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.FirstUnloadableThing), MethodType.Getter)]
    internal static class SharedJunkCarryRetention
    {
        static bool Prefix(Pawn_InventoryTracker __instance, ref ThingCount __result)
        {
            Pawn pawn = __instance.pawn;
            // Native return-to-map unloading still preserves drug/stock quotas.
            // Explicit player Drop orders act directly on the selected stack.
            if (JunkCarryUtility.Quota(pawn) <= 0) return true;
            var native = JunkCarryUtility.NativeRetention(pawn);
            var allocation = JunkCarryUtility.AdditionalAllocation(pawn, native);
            if (allocation.Count == 0) return true;
            foreach (Thing item in __instance.innerContainer)
            {
                int nativeCount; native.TryGetValue(item, out nativeCount);
                int grouped; allocation.TryGetValue(item, out grouped);
                int retained = nativeCount + grouped;
                if (item.stackCount > retained)
                { __result = new ThingCount(item, item.stackCount - retained); return false; }
            }
            __result = default(ThingCount);
            return false;
        }
    }
}
