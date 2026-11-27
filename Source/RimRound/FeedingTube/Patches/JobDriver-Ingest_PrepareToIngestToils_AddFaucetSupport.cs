using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.FeedingTube.Patches
{
    [HarmonyPatch(typeof(JobDriver_Ingest), "PrepareToIngestToils")]
    public class JobDriver_Ingest_PrepareToIngestToils_AddFaucetSupport
    {
        public static bool Prefix(JobDriver_Ingest __instance,
            ref IEnumerable<Toil> __result)
        {
            if (!FoodNetworkPatchPolicy.LegacyPatchesEnabled) return true;

            if (!(__instance?.job?.GetTarget(TargetIndex.A).Thing is
                Building_FoodFaucet)) return true;
            __result = MakeFaucetToils(__instance);
            return false;
        }

        public static IEnumerable<Toil> MakeFaucetToils(
            JobDriver_Ingest driver)
        {
            yield return Toils_Goto.GotoThing(TargetIndex.A,
                PathEndMode.InteractionCell)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return GrabFoodFromFaucet(driver);
        }

        public static Toil GrabFoodFromFaucet(JobDriver_Ingest driver)
        {
            return GrabFoodFromFaucet(TargetIndex.A, driver?.pawn, driver);
        }

        // Keep the original public entry point for other classic integrations.
        public static Toil GrabFoodFromFaucet(TargetIndex ind, Pawn eater,
            JobDriver_Ingest driver)
        {
            Toil toil = ToilMaker.MakeToil("TakeFoodFromClassicFaucet");
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Building_FoodFaucet faucet = actor.CurJob
                    .GetTarget(ind).Thing as Building_FoodFaucet;
                Thing existing = actor.CurJob
                    .GetTarget(TargetIndex.B).Thing;
                if (existing != null && !existing.Destroyed &&
                    actor.carryTracker.CarriedThing == existing)
                    return; // A loaded delayed toil must not withdraw twice.
                if (faucet == null)
                {
                    driver.EndJobWith(JobCondition.Incompletable);
                    return;
                }
                actor.rotationTracker.FaceTarget(faucet);
                Thing serving = faucet.TryDispenseFood();
                if (serving == null ||
                    !actor.carryTracker.TryStartCarry(serving))
                {
                    if (serving != null && !serving.Destroyed)
                        faucet.TryRecoverServing(serving, actor);
                    driver.EndJobWith(JobCondition.Incompletable);
                    return;
                }
                actor.CurJob.SetTarget(TargetIndex.B,
                    actor.carryTracker.CarriedThing);
                actor.CurJob.count = actor.carryTracker.CarriedThing.stackCount;
            };
            toil.FailOnCannotTouch(ind, PathEndMode.Touch);
            toil.defaultCompleteMode = ToilCompleteMode.Delay;
            toil.defaultDuration = 10;
            return toil;
        }
    }
}
