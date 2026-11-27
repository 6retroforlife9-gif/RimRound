using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Comps;
using RimRound.FeedingTube;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    public static class MealSittingToils
    {
        public static bool CanCollect(Pawn collector, params Pawn[] eaters) =>
            collector?.inventory?.innerContainer != null && collector.carryTracker != null &&
            collector.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
            (MealSittingComponent.Current?.Get(collector) != null ||
             eaters.Any(e => MealSittingComponent.Eligible(e) &&
                 (collector.CurJob?.playerForced == true ||
                  // This chooses the bounded driver, not a food withdrawal.
                  // A source-less debit can consume a restricted pool in the
                  // forecast and incorrectly send an affordable meal through
                  // the unbounded fallback. Real sources and costs are checked
                  // by Reserve when collecting and again at consumption.
                  ColonyFoodReserve.CanSpendOptional(e, 0d))));

        public static bool UseSolo(JobDriver_Ingest driver) => driver?.job?.playerForced != true &&
            driver?.job?.def == JobDefOf.Ingest && CanCollect(driver.pawn, driver.pawn) &&
            (MealSittingComponent.Current?.Get(driver.pawn) != null || GainMealSession.IsActive(driver.pawn)) &&
            (OrdinaryFoodIntake.IsOrdinaryFood(driver.job.targetA.Thing) ||
             driver.job.targetA.Thing is Building_FoodFaucet ||
             driver.job.targetA.Thing is Building_NutrientPasteDispenser);

        public static IEnumerable<Toil> Solo(JobDriver_Ingest driver)
        {
            Pawn pawn = driver.pawn;
            driver.AddFailCondition(() => pawn.Drafted || pawn.InMentalState ||
                !BodyFeedingPolicy.UsesComfortableGainTarget(pawn));
            foreach (Toil toil in Collect(pawn, FaucetMealPurpose.Routine, pawn)) yield return toil;
            yield return Toils_Ingest.CarryIngestibleToChewSpot(pawn, TargetIndex.A);
            yield return Toils_Ingest.FindAdjacentEatSurface(TargetIndex.B, TargetIndex.A);
            Toil chew = FeedOtherUtility.ChewIngestibleWithEatingSpeed(pawn, 1f, TargetIndex.A, TargetIndex.B);
            AccessTools.Field(typeof(JobDriver_Ingest), "chewing").SetValue(driver, chew);
            chew.AddFailCondition(() => !CanEatCarried(pawn, pawn));
            yield return chew;
            yield return Toils_Ingest.FinalizeIngest(pawn, TargetIndex.A);
            yield return Toils_Jump.JumpIf(chew, () => MealSittingComponent.Current.Promote(pawn, pawn));
        }

        public static bool CanEatCarried(Pawn collector, Pawn eater)
        {
            Thing food = collector?.carryTracker?.CarriedThing;
            MealPortionPlan plan;
            return food != null && FoodUtility.WillEat(eater, food, collector, true, false) &&
                OrdinaryFoodIntake.TryPreview(eater, food, 1, out plan,
                    MealSittingComponent.Current?.BudgetFor(eater)) &&
                (MealSittingComponent.EssentialCareMode(collector, eater) || plan.UsefulFraction >= .75d - 1e-7d);
        }

        // Gather every source before the caller's dining/recipient journey. A
        // source may be unavailable by arrival; that creates a bounded partial
        // outcome, never a second storage journey after eating has started.
        public static IEnumerable<Toil> Collect(Pawn collector, FaucetMealPurpose purpose, params Pawn[] eaters)
        {
            // RimWorld resumes the saved toil before GameComponentTick. The
            // per-toil closure is rebuilt on load, so cancel the persisted
            // interrupted sitting before an arrival/pickup tick dereferences it.
            collector.jobs.curDriver.AddFailCondition(() =>
                MealSittingComponent.Current?.Get(collector)?.InterruptedOnLoad == true);
            MealSitting sitting = null;
            SittingRecipient recipient = null;
            Thing source = null, preferred = null;
            int count = 0;
            FaucetMealAcquisition.Selection faucetSelection = null;
            var attempted = new HashSet<string>();
            var reconsideredRaw = new HashSet<int>();
            int phaseStartVisit = 0;
            Toil select = null, arrive = null, pickup = null, finish = null;
            Toil begin = ToilMaker.MakeToil("BeginBoundedMealCollection");
            begin.initAction = delegate
            {
                sitting = MealSittingComponent.Current.Begin(collector, eaters, purpose);
                preferred = collector.CurJob.targetA.Thing;
                if (sitting.InterruptedOnLoad) { collector.jobs.curDriver.EndJobWith(JobCondition.InterruptForced); return; }
                if (sitting.Ready) collector.jobs.curDriver.JumpToToil(finish);
                else if (collector.carryTracker.CarriedThing != null)
                {
                    Thing carried = collector.carryTracker.CarriedThing;
                    if (!collector.carryTracker.innerContainer.TryTransferToContainer(carried,
                        collector.inventory.innerContainer, false)) collector.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                }
            };
            begin.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return begin;

            select = ToilMaker.MakeToil("SelectNextBundleSource");
            select.initAction = delegate
            {
                if (sitting == null || !sitting.Live) { collector.jobs.curDriver.EndJobWith(JobCondition.InterruptForced); return; }
                source = null; recipient = null;
                foreach (SittingRecipient candidate in sitting.Recipients)
                {
                    if ((candidate.EssentialCare ? candidate.EssentialCareCollected : candidate.Budget.CollectedEnough) ||
                        !MealSittingComponent.Permission(sitting, candidate)) continue;
                    Thing[] sources = Sources(collector, preferred).ToArray();
                    // Prefer a prepared sitting even while collection precedes
                    // the first bite and the hunger bar has not risen yet. Once
                    // a meal is collected, a tiny remaining gap is not a reason
                    // to add raw ingredients. Hungry eaters with no collected
                    // meal still retain raw fallback when prepared items cannot fit.
                    bool preparedSitting = !candidate.EssentialCare && (candidate.Pawn.needs.food.CurLevelPercentage >
                        candidate.Pawn.needs.food.PercentageThreshHungry ||
                        sitting.Allocations.Any(a => a.Recipient == candidate.Pawn &&
                            a.Food != null && FeedOtherUtility.IsPreparedMeal(a.Food))) &&
                        sources.Any(food => PreparedFoodAvailable(collector, candidate.Pawn, food));
                    foreach (Thing food in sources)
                    {
                        if (preparedSitting && !FeedOtherUtility.IsPreparedMeal(food)) continue;
                        string key = candidate.Pawn.thingIDNumber + ":" + food.thingIDNumber;
                        if (attempted.Contains(key)) continue;
                        attempted.Add(key);
                        if (!TryCount(sitting, candidate, food, out count, out faucetSelection)) continue;
                        if (food.Spawned && !(food is Building) &&
                            !collector.Reserve(food, collector.CurJob, FeedOtherUtility.FoodReservationMaxPawns, count, null, false)) continue;
                        recipient = candidate; source = food; break;
                    }
                    if (source != null) break;
                }
                if (source == null || sitting.SourcesVisited - phaseStartVisit >= 64)
                {
                    sitting.Outcome = sitting.Recipients.All(r => r.Budget.CollectedEnough) ? null :
                        "partial meal: available food, carrying capacity or portion rounding";
                    collector.jobs.curDriver.JumpToToil(finish); return;
                }
                sitting.SourcesVisited++;
                collector.CurJob.targetA = source;
                collector.CurJob.count = count;
                if (collector.inventory.innerContainer.Contains(source)) collector.jobs.curDriver.JumpToToil(pickup);
            };
            select.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return select;

            arrive = ToilMaker.MakeToil("WalkToBundleSource");
            arrive.initAction = delegate
            {
                if (sitting == null || recipient == null || !sitting.Live)
                { collector.jobs.curDriver.EndJobWith(JobCondition.InterruptForced); return; }
                if (!SourceValid(collector, recipient.Pawn, source)) { collector.jobs.curDriver.JumpToToil(select); return; }
                collector.pather.StartPath(source is Building ? (LocalTargetInfo)source.InteractionCell : source,
                    source is Building ? PathEndMode.OnCell : PathEndMode.ClosestTouch);
            };
            arrive.tickAction = delegate
            {
                if (sitting == null || recipient == null || !sitting.Live)
                { collector.jobs.curDriver.EndJobWith(JobCondition.InterruptForced); return; }
                if (!SourceValid(collector, recipient.Pawn, source)) { collector.jobs.curDriver.JumpToToil(select); return; }
                bool there = source is Building ? collector.Position == source.InteractionCell :
                    collector.CanReachImmediate(source, PathEndMode.ClosestTouch);
                if (there) collector.jobs.curDriver.ReadyForNextToil();
                else if (!collector.pather.Moving) collector.jobs.curDriver.JumpToToil(select);
            };
            arrive.defaultCompleteMode = ToilCompleteMode.Never;
            yield return arrive;

            pickup = ToilMaker.MakeToil("CollectRealMealBundleItems");
            pickup.initAction = delegate
            {
                if (sitting == null || recipient == null || !sitting.Live)
                { collector.jobs.curDriver.EndJobWith(JobCondition.InterruptForced); return; }
                if (!MealSittingComponent.Permission(sitting, recipient)) return;
                // Food may be cooked or an outlet refilled during the walk to
                // raw ingredients. Reconsider once per recipient at pickup;
                // never poll while walking or start another tour after eating.
                if (!recipient.EssentialCare && !FeedOtherUtility.IsPreparedMeal(source) &&
                    !reconsideredRaw.Contains(recipient.Pawn.thingIDNumber))
                {
                    Thing prepared = Sources(collector, preferred).FirstOrDefault(food =>
                        PreparedFoodAvailable(collector, recipient.Pawn, food) &&
                        TryCount(sitting, recipient, food, out int preparedCount, out FaucetMealAcquisition.Selection preparedSelection));
                    if (prepared != null)
                    {
                        reconsideredRaw.Add(recipient.Pawn.thingIDNumber);
                        // Keep a real raw fallback if the replacement disappears,
                        // while permitting exactly one renewed prepared attempt.
                        if (source != null)
                        {
                            attempted.Remove(recipient.Pawn.thingIDNumber + ":" + source.thingIDNumber);
                            if (source.Spawned) collector.Map.reservationManager.Release(source, collector, collector.CurJob);
                        }
                        attempted.Remove(recipient.Pawn.thingIDNumber + ":" + prepared.thingIDNumber);
                        preferred = prepared;
                        collector.jobs.curDriver.JumpToToil(select);
                        return;
                    }
                }
                if (!TryCount(sitting, recipient, source, out count, out faucetSelection)) return;
                if (source is Building_FoodFaucet faucet)
                    CollectFaucet(sitting, recipient, faucet, faucetSelection, count);
                else if (source is Building_NutrientPasteDispenser dispenser)
                    CollectPaste(sitting, recipient, dispenser, count);
                else CollectSolid(sitting, recipient, source, count);
                ColonyFoodReserve.NotifyFoodChanged(collector.Map);
            };
            pickup.defaultCompleteMode = ToilCompleteMode.Delay;
            pickup.defaultDuration = Building_NutrientPasteDispenser.CollectDuration;
            yield return pickup;
            yield return Toils_Jump.Jump(select);

            finish = ToilMaker.MakeToil("FinishMealBundleCollection");
            finish.initAction = delegate
            {
                sitting = MealSittingComponent.Current.Get(collector);
                if (sitting == null || !sitting.Live) { collector.jobs.curDriver.EndJobWith(JobCondition.InterruptForced); return; }
                if (MealSittingComponent.Current.TryBeginEssentialCare(sitting))
                {
                    // One new bounded source pass, now with an ordinary care
                    // request. The recorded mode prevents another transition.
                    attempted.Clear(); reconsideredRaw.Clear();
                    phaseStartVisit = sitting.SourcesVisited;
                    collector.jobs.curDriver.JumpToToil(select);
                    return;
                }
                sitting.Ready = true;
                if (!MealSittingComponent.Current.Promote(collector, eaters[0]))
                    collector.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }

        private static IEnumerable<Thing> Sources(Pawn collector, Thing preferred)
        {
            var inventory = collector.inventory.innerContainer.ToArray();
            var sources = new List<Thing>();
            if (preferred != null && !preferred.Destroyed) sources.Add(preferred);
            sources.AddRange(inventory.Where(OrdinaryFoodIntake.IsOrdinaryFood));
            sources.AddRange(collector.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree)
                .Where(t => t.Position.InHorDistOf(collector.Position, 60f)));
            // V2 faucets are deliberately absent from vanilla's ingestible list.
            // Include them explicitly; TryCount validates output, policy and path.
            sources.AddRange(collector.Map.listerBuildings.allBuildingsColonist.OfType<Building_FoodFaucet>()
                .Where(t => t.Position.InHorDistOf(collector.Position, 60f)));
            return sources.Distinct()
                .OrderBy(t => FeedOtherUtility.IsPreparedMeal(t) ? 0 : 1)
                .ThenBy(t => t == preferred ? 0 : inventory.Contains(t) ? 1 : 2)
                .ThenByDescending(t => t.def.ingestible?.preferability ?? FoodPreferability.Undefined)
                .ThenBy(t => collector.Position.DistanceToSquared(t.Position)).ThenBy(t => t.thingIDNumber);
        }

        private static bool SourceValid(Pawn collector, Pawn eater, Thing source)
        {
            if (source == null || source.Destroyed || source.IsForbidden(collector)) return false;
            if (collector.inventory.innerContainer.Contains(source)) return FoodUtility.WillEat(eater, source, collector, true, false);
            bool care = collector.jobs.curDriver is JobDriver_PrisonerMealDelivery ||
                collector.jobs.curDriver is JobDriver_FoodFeedPatientEatingSpeed ||
                collector.jobs.curDriver is JobDriver_FattenPrisonerDirectFeed;
            return source.Spawned && source.Map == collector.Map &&
                collector.CanReach(source, source is Building ? PathEndMode.InteractionCell : PathEndMode.ClosestTouch, collector.NormalMaxDanger()) &&
                (source is Building || (collector == eater && collector.CurJobDef == JobDefOf.Ingest
                    ? FoodUtility.WillEat(eater, source, collector, true, false) &&
                      SocialProperness.IsSociallyProper(source, eater) && FactionUtility.IsPoliticallyProper(source, eater)
                    : care ? source.IngestibleNow && FoodUtility.WillEat(eater, source, collector, true, false) &&
                        SocialProperness.IsSociallyProper(source, collector) && FactionUtility.IsPoliticallyProper(source, collector)
                    : FeedOtherUtility.IsMealAcceptableForPawn(eater, source)));
        }

        private static bool PreparedFoodAvailable(Pawn collector, Pawn eater, Thing source)
        {
            if (!FeedOtherUtility.IsPreparedMeal(source) || !SourceValid(collector, eater, source)) return false;
            if (source is Building_FoodFaucet faucet)
            {
                var portion = FoodFaucetPortions.Get(FaucetPortionMode.Normal)
                    .WithDensity(FaucetMealAcquisition.ResolveSupplyGrade(faucet));
                if (!FaucetMealAcquisition.TryPreviewServing(faucet, portion, out FoodBatchV2 batch)) return false;
                Thing preview = FoodNetworkV2ServingUtility.MakeServing(batch, portion.FoodDef, portion.FoodDef.defName);
                if (preview == null) return false;
                try { return FoodUtility.WillEat(eater, preview, collector, true, false); }
                finally { preview.Destroy(DestroyMode.Vanish); }
            }
            if (source is Building_NutrientPasteDispenser dispenser)
                return dispenser.CanDispenseNow && FoodUtility.WillEat(eater, ThingDefOf.MealNutrientPaste, collector, true, false);
            return OrdinaryFoodIntake.IsOrdinaryFood(source) && source.IngestibleNow &&
                (!source.Spawned || collector.Map.reservationManager.CanReserveStack(
                    collector, source, FeedOtherUtility.FoodReservationMaxPawns) > 0);
        }

        private static bool TryCount(MealSitting sitting, SittingRecipient recipient, Thing source,
            out int count, out FaucetMealAcquisition.Selection selection)
        {
            count = 0; selection = null;
            Pawn collector = sitting.Collector;
            if (!SourceValid(collector, recipient.Pawn, source)) return false;
            double n, v, mass;
            int available;
            if (source is Building_FoodFaucet faucet)
            {
                if (!FaucetMealAcquisition.TrySelectForPurpose(faucet, collector, recipient.Pawn, recipient.Purpose, out selection)) return false;
                n = selection.Portion.PreciseNutrition;
                v = selection.Portion.Volume * recipient.Pawn.TryGetComp<FullnessAndDietStats_ThingComp>().FullnessGainedMultiplier;
                mass = selection.Portion.FoodDef.GetStatValueAbstract(StatDefOf.Mass);
                available = FaucetMealAcquisition.UsesPreparedServing(recipient.Pawn) ? 256 : 1;
            }
            else if (source is Building_NutrientPasteDispenser dispenser)
            {
                if (!dispenser.CanDispenseNow ||
                    !FoodUtility.WillEat(recipient.Pawn, dispenser.DispensableDef, collector, true, false)) return false;
                n = ThingDefOf.MealNutrientPaste.GetStatValueAbstract(StatDefOf.Nutrition);
                v = n * recipient.Pawn.TryGetComp<FullnessAndDietStats_ThingComp>().FullnessGainedMultiplier;
                mass = ThingDefOf.MealNutrientPaste.GetStatValueAbstract(StatDefOf.Mass); available = 256;
            }
            else
            {
                if (!OrdinaryFoodIntake.IsOrdinaryFood(source) || !source.IngestibleNow ||
                    sitting.Allocations.Any(a => a.Food == source)) return false;
                MealSittingComponent.Amounts(recipient.Pawn, source, out n, out v);
                mass = source.GetStatValue(StatDefOf.Mass);
                available = source.Spawned ? collector.Map.reservationManager.CanReserveStack(
                    collector, source, FeedOtherUtility.FoodReservationMaxPawns) : source.stackCount;
            }
            if (!MealSittingBudget.Valid(mass) || !MealSittingBudget.Valid(n) || !MealSittingBudget.Valid(v)) return false;
            if (!collector.inventory.innerContainer.Contains(source) && mass > 0d)
            {
                int inventorySpace = (int)Math.Min(int.MaxValue, Math.Floor(MassUtility.FreeSpace(collector) / mass));
                // Worn gear can exhaust caravan inventory mass while native
                // hand carrying remains available. Keep that held stack inside
                // the same fixed sitting budget, with one real item owner.
                // Dispensers produce separate serving objects, so an empty
                // hand can receive one; extra servings need inventory space.
                int handSpace = collector.carryTracker.CarriedThing != null ? 0 :
                    source is Building ? 1 : collector.carryTracker.AvailableStackSpace(source.def);
                available = Math.Min(available, Math.Max(inventorySpace, handSpace));
            }
            if (recipient.EssentialCare)
            {
                if (!MealSittingComponent.CanAcquireEssentialCare(collector, recipient.Pawn)) return false;
                var budget = MealSittingComponent.Current.BudgetFor(recipient.Pawn);
                if (!budget.HasValue) return false;
                var body = recipient.Pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
                double room = Math.Max(0d, Math.Min(budget.Value.FullnessTarget, body.HardLimit) - body.OccupiedVolume);
                MealPortionPlan carePlan;
                int maximum = source is Building ? 1 : source.def.ingestible.maxNumToIngestAtOnce;
                // Native item counting permits one ordinary partial-use meal;
                // the optional sitting's 75% rounding rule does not apply.
                if (!MealPortionPlan.TryCreateForItems(n, v, budget.Value.Nutrition, room,
                    source is Building ? Math.Min(1, available) : available, maximum, out count, out carePlan)) return false;
                if (!(source is Building) && !OrdinaryFoodIntake.TryPreview(recipient.Pawn, source, count,
                    out carePlan, budget.Value)) { count = 0; return false; }
            }
            else count = recipient.Budget.CountToCollect(n, v, available);
            return count > 0;
        }

        private static ThingOwner CollectionHolder(Pawn collector, ThingDef foodDef, int count)
        {
            double mass = foodDef.GetStatValueAbstract(StatDefOf.Mass) * (double)count;
            if (mass <= MassUtility.FreeSpace(collector) + .0001d) return collector.inventory.innerContainer;
            return collector.carryTracker.CarriedThing == null && count <= collector.carryTracker.AvailableStackSpace(foodDef)
                ? collector.carryTracker.innerContainer : null;
        }

        private static void Add(MealSitting sitting, SittingRecipient recipient, Thing food)
        {
            sitting.Allocations.Add(new SittingAllocation { Recipient = recipient.Pawn, Food = food });
            MealSittingComponent.Amounts(recipient.Pawn, food, out double n, out double v);
            recipient.Budget.Collected(n * food.stackCount, v * food.stackCount);
            if (recipient.EssentialCare) recipient.EssentialCareCollected = true;
        }

        private static bool Reserve(SittingRecipient recipient, double cost, Thing source, out IDisposable lease)
        {
            lease = null;
            if (recipient.EssentialCare)
            {
                var sitting = MealSittingComponent.Current?.ForRecipient(recipient.Pawn);
                return MealSittingComponent.CanAcquireEssentialCare(sitting?.Collector, recipient.Pawn);
            }
            return !recipient.ProtectReserve || ColonyFoodReserve.TryReserveOptional(recipient.Pawn, cost, source, out lease);
        }

        private static void CollectSolid(MealSitting sitting, SittingRecipient recipient, Thing source, int count)
        {
            if (!Reserve(recipient, ColonyFoodReserve.StockCost(source, count), source, out IDisposable lease)) return;
            try
            {
                Thing item;
                if (sitting.Collector.inventory.innerContainer.Contains(source))
                {
                    item = sitting.Collector.inventory.innerContainer.Take(source, count);
                }
                else
                {
                    if (!sitting.Collector.CanReserve(source, FeedOtherUtility.FoodReservationMaxPawns, count) ||
                        !sitting.Collector.CanReachImmediate(source, PathEndMode.ClosestTouch)) return;
                    item = source.SplitOff(count);
                }
                if (item == null) return;
                if (recipient.EssentialCare) recipient.EssentialCareCollected = true;
                ThingOwner holder = CollectionHolder(sitting.Collector, item.def, item.stackCount);
                if (holder == null || !holder.TryAdd(item, false))
                {
                    MealSittingComponent.Current.RecoverUnownedFood(sitting.Collector, item); return;
                }
                Add(sitting, recipient, item);
            }
            finally { lease?.Dispose(); }
        }

        private static void CollectFaucet(MealSitting sitting, SittingRecipient recipient, Building_FoodFaucet faucet,
            FaucetMealAcquisition.Selection selection, int count)
        {
            // A smaller real batch is useful when stock cannot cover the full sitting.
            for (int take = count; take > 0; take--)
            {
                ThingOwner holder = CollectionHolder(sitting.Collector, selection.Portion.FoodDef, take);
                if (holder == null) continue;
                var requests = new[] { new FaucetMealRequest(recipient.Pawn, recipient.Purpose, selection.Portion, take) };
                if (!FaucetMealAcquisition.TryQuoteBatch(faucet, sitting.Collector, requests,
                    out FaucetBatchQuote quote, out string failure, true)) continue;
                if (!Reserve(recipient, quote.TotalNutrition, faucet, out IDisposable lease)) continue;
                try
                {
                    if (!FaucetMealAcquisition.TryCollectBatch(quote, holder,
                        out CollectedFaucetBatch batch, out failure)) continue;
                    foreach (var item in batch.Servings) Add(sitting, recipient, item.Thing);
                    return;
                }
                finally { lease?.Dispose(); }
            }
        }

        private static void CollectPaste(MealSitting sitting, SittingRecipient recipient, Building_NutrientPasteDispenser dispenser, int count)
        {
            for (int i = 0; i < count && dispenser.CanDispenseNow; i++)
            {
                if (recipient.EssentialCare &&
                    !MealSittingComponent.CanAcquireEssentialCare(sitting.Collector, recipient.Pawn)) break;
                ThingOwner holder = CollectionHolder(sitting.Collector, ThingDefOf.MealNutrientPaste, 1);
                if (holder == null) break;
                IDisposable lease = null;
                if (!recipient.EssentialCare && recipient.ProtectReserve &&
                    !NativePasteReserve.TryReserve(recipient.Pawn, dispenser, out lease)) break;
                try
                {
                    Thing meal = dispenser.TryDispenseFood();
                    if (meal == null) break;
                    // Native paste has already consumed real hopper ingredients.
                    // If placement is rejected, retain the meal and do not draw
                    // another essential serving during this job.
                    if (recipient.EssentialCare) recipient.EssentialCareCollected = true;
                    if (!FoodUtility.WillEat(recipient.Pawn, meal, sitting.Collector, true, false) ||
                        !holder.TryAdd(meal, false))
                    {
                        MealSittingComponent.Current.RecoverUnownedFood(sitting.Collector, meal); break;
                    }
                    Add(sitting, recipient, meal);
                }
                finally { lease?.Dispose(); }
            }
        }

        public static IEnumerable<Toil> Care(JobDriver driver, Pawn eater, FaucetMealPurpose purpose, bool fatten)
        {
            Pawn collector = driver.pawn;
            foreach (Toil toil in Collect(collector, purpose, eater)) yield return toil;
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            Toil chew = FeedOtherUtility.ChewIngestibleWithEatingSpeed(eater,
                FeedOtherUtility.AssistedEatingDurationFactor, TargetIndex.A, TargetIndex.None, 1.5f)
                .FailOnCannotTouch(TargetIndex.B, PathEndMode.Touch);
            chew.AddFailCondition(() => !CanEatCarried(collector, eater));
            yield return chew;
            Toil finalize = Toils_Ingest.FinalizeIngest(eater, TargetIndex.A);
            Action native = finalize.initAction;
            finalize.initAction = delegate
            {
                if (!CanEatCarried(collector, eater) || !FeedOtherUtility.CanFinalizeCareIngestion(eater,
                    collector, collector.carryTracker.CarriedThing, driver.job, fatten))
                { driver.EndJobWith(JobCondition.Incompletable); return; }
                native();
                if (!fatten && ModsConfig.AnomalyActive && Rand.Chance(.3f) && MetalhorrorUtility.IsInfected(collector))
                    MetalhorrorUtility.Infect(eater, collector, "FeedingImplant");
            };
            yield return finalize;
            yield return Toils_Jump.JumpIf(chew, () => MealSittingComponent.Current.Promote(collector, eater));
            if (fatten)
            {
                Toil complete = ToilMaker.MakeToil("CompleteFattenMealBundle");
                complete.initAction = () => PrisonerFatteningFoodPatch.CompleteFattenServing(eater);
                complete.defaultCompleteMode = ToilCompleteMode.Instant; yield return complete;
            }
        }
    }
}
