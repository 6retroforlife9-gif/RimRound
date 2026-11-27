using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Simulation;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.Utilities
{
    /// <summary>Read-only native infant access and a conserved maternal milk supply.</summary>
    public static class InfantFoodReserve
    {
        public static bool IsInfant(Pawn pawn) => pawn != null && pawn.DevelopmentalStage.Baby();

        private static bool AvailableHelper(Pawn helper) => helper != null && helper.Spawned && !helper.Dead &&
            !helper.Suspended && !helper.Downed && !helper.Drafted && !helper.InMentalState && helper.RaceProps.Humanlike;

        // Native urgent discovery scans FreeHumanlikesOfFaction. Hosted babies
        // outside that set require actual Childcare work instead of an urgent bypass.
        private static bool UrgentDiscoverable(Pawn helper, Pawn baby) => baby.Faction == helper.Faction &&
            (baby.HostFaction == null || baby.IsSlave) && (!ModsConfig.AnomalyActive || !baby.IsSubhuman);

        public static bool CanDeliver(Pawn helper, Pawn baby)
        {
            if (!ModsConfig.BiotechActive || !AvailableHelper(helper) ||
                !ChildcareUtility.CanFeedBaby(helper, baby, out _) || !ChildcareUtility.CanHaulBaby(helper, baby, out _)) return false;
            // Native bottle work can run with Childcare active even when this
            // helper's urgent-autofeed setting is Never. Urgent needs no work assignment.
            if (!(baby.mindState.AutofeedSetting(helper) == AutofeedMode.Urgent && UrgentDiscoverable(helper, baby)) &&
                helper.workSettings?.WorkIsActive(WorkTypeDefOf.Childcare) != true) return false;
            Pawn carrier = baby.CarriedBy;
            if (carrier != null) return carrier == helper;
            return ChildcareUtility.CanHaulBabyNow(helper, baby, true, out _);
        }

        public static bool CanReceiveStock(Pawn baby, Thing food)
        {
            if (!ModsConfig.BiotechActive || baby?.MapHeld == null || food == null || !food.Spawned || food.Map != baby.MapHeld)
                return false;
            foreach (Pawn helper in food.Map.mapPawns.FreeColonistsSpawned)
                if (CanDeliver(helper, baby) && !food.IsForbidden(helper) && food.IsSociallyProper(helper) &&
                    FoodUtility.WillEat(baby, food, helper, true, false) && food.IngestibleNow &&
                    helper.CanReach(food, PathEndMode.ClosestTouch, Danger.Some)) return true;
            return false;
        }

        private static bool NursingNow(Pawn mother, Pawn baby) => IsInfant(baby) && baby.CarriedBy == mother &&
            mother?.jobs?.curDriver is JobDriver_Breastfeed nursing && nursing.Feeding &&
            mother.CurJob.targetA.Thing == baby && ChildcareUtility.CanBreastfeed(mother, out _);

        private static bool CanNurse(Pawn mother, Pawn baby)
        {
            if (!IsInfant(baby) || mother == null || !mother.Spawned || mother.Dead || mother.Suspended || mother.Drafted ||
                mother.Map != baby.MapHeld || (mother.InMentalState && !mother.Downed) ||
                !ChildcareUtility.CanAutoBreastfeed(mother, baby, false, out _)) return false;
            // Ignore temporary fullness, low charge and other jobs' reservations:
            // this estimates several days, not whether a feeding job starts this tick.
            // A carried baby has no spawned position; retain its real carrier route.
            if (!mother.IsPrisoner && AvailableHelper(mother) &&
                (baby.mindState.AutofeedSetting(mother) != AutofeedMode.Urgent || UrgentDiscoverable(mother, baby)))
            {
                if (baby.CarriedBy == mother) return true;
                if (baby.CarriedBy == null && ChildcareUtility.CanHaulBabyToMomNow(mother, mother, baby, true, out _)) return true;
            }
            if (!mother.Downed && !mother.IsPrisoner) return false;
            foreach (Pawn helper in mother.Map.mapPawns.FreeColonistsSpawned)
            {
                if (helper == mother || !CanDeliver(helper, baby)) continue;
                if (baby.CarriedBy == helper)
                {
                    if ((mother.Downed || mother.IsPrisoner) && ChildcareUtility.CanHaulToMom(helper, mother, out _) &&
                        helper.CanReach(mother, PathEndMode.Touch, Danger.Deadly)) return true;
                }
                else if (ChildcareUtility.CanHaulBabyToMomNow(helper, mother, baby, true, out _)) return true;
            }
            return false;
        }

        internal static void AddNursingSupplies(IReadOnlyList<Pawn> residents, List<FoodReserveSupply> supplies)
        {
            if (!ModsConfig.BiotechActive) return;
            foreach (Pawn mother in residents)
            {
                var lactating = mother.health?.hediffSet.GetFirstHediffOfDef(HediffDefOf.Lactating)?.TryGetComp<HediffComp_Lactating>();
                if (lactating == null) continue;
                double cost = lactating.Props.ticksToFullCharge > 0
                    ? lactating.AddedNutritionPerDay() * BodyResourceUtility.GetMilkMultiplierByWeight(mother) : 0d;
                if (!FoodReserveAllocation.Finite(cost) || cost < 0d || !FoodReserveAllocation.Finite(lactating.Charge) || lactating.Charge < 0f)
                    throw new InvalidOperationException("Invalid lactation reserve rate");
                var recipients = residents.Where(baby => CanNurse(mother, baby)).ToArray();
                // One feeding job per mother. Even weight-amplified production
                // cannot exceed the native transfer rate to her slowest recipient.
                double rate = recipients.Length == 0 ? 0d : Math.Min(cost, recipients.Min(baby => baby.needs.food.MaxLevel * 12d));
                var decay = lactating.parent.TryGetComp<HediffComp_SeverityPerDay>();
                double change = decay?.SeverityChangePerDay() ?? 0d;
                // Do not promise future severity restoration from hypothetical
                // nursing. Reserve one native severity interval for update ordering.
                double lifetime = change < 0d ? Math.Max(0d, lactating.parent.Severity / -change - 200d / 60000d) : double.MaxValue;
                double limit = rate > 0d ? Math.Min(double.MaxValue, rate * lifetime) : 0d;
                bool canActivate = lactating.CanActivate || (rate > 0d && limit >= Math.Max(0d, lactating.Props.minChargeToActivate - lactating.Charge));
                // Native FoodFall already includes its lactation allowance, but
                // TryCharge ALSO debits food 1:1, even when the charge later caps.
                // Reserve that additional fed production cost once for this mother.
                // Current charge is prepaid; expressed Milk has already left this
                // same charge and is counted only as its actual ground/inventory item.
                supplies.Add(new FoodReserveSupply {
                    Key = "breastmilk:" + mother.thingIDNumber, Source = mother, BreastMilk = true,
                    Nutrition = lactating.Charge, ExpiryDays = lifetime,
                    CanServe = baby => (canActivate && recipients.Contains(baby)) || NursingNow(mother, baby)
                });
                // Current nursing may finish after its auto-feed permission is
                // disabled. That grants access to prepaid charge only, never to
                // hypothetical future jobs. Production has its own access mask.
                supplies.Add(new FoodReserveSupply {
                    Key = "breastmilk-production:" + mother.thingIDNumber, Source = mother, BreastMilk = true,
                    Nutrition = 0d, DailyProduction = rate, ProductionLimit = limit, ExpiryDays = lifetime,
                    ProductionConsumer = mother, DailyProductionCost = cost,
                    CanServe = baby => canActivate && recipients.Contains(baby)
                });
            }
        }
    }
}
