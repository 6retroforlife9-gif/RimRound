using System.Collections.Generic;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Things;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace RimRound.Utilities
{
    public static class BlobBedPartnerUtility
    {
        internal readonly struct PartnerPair
        {
            internal readonly Building_BlobBed BlobBed;
            internal readonly Building_Bed HostBed;
            internal readonly Pawn Host;
            internal readonly Pawn Guest;

            internal PartnerPair(Building_BlobBed blobBed, Pawn guest)
            {
                BlobBed = blobBed;
                HostBed = blobBed.HostBed;
                Host = blobBed.originatingPawn;
                Guest = guest;
            }

            internal Pawn Other(Pawn pawn) => pawn == Host ? Guest : Host;
            internal Building_Bed BedFor(Pawn pawn) => pawn == Host ? HostBed : BlobBed;
        }

        private static bool IsAdult(Pawn pawn)
            => pawn != null && pawn.RaceProps.Humanlike && !pawn.Dead && !pawn.Destroyed &&
               pawn.ageTracker.AgeBiologicalYearsFloat >= 18f &&
               pawn.ageTracker.CurLifeStage.developmentalStage == DevelopmentalStage.Adult;

        private static Building_BlobBed AssignedBlobBed(Pawn pawn)
        {
            if (pawn?.ownership?.OwnedBed is Building_BlobBed guestBed) return guestBed;
            return pawn?.TryGetComp<MakeBlobIntoBed_ThingComp>()?.blobBed as Building_BlobBed;
        }

        internal static bool TryGetAssignedPair(Pawn pawn, out PartnerPair pair, bool allowMedicalHost = false)
        {
            pair = default(PartnerPair);
            Building_BlobBed bed = AssignedBlobBed(pawn);
            if (bed == null || !bed.IsOperational || bed.HostBed == null ||
                bed.OwnersForReading.Count != 1) return false;
            Pawn host = bed.originatingPawn;
            Pawn guest = bed.OwnersForReading[0];
            if ((pawn != host && pawn != guest) || host == guest ||
                !IsAdult(host) || !IsAdult(guest) || !host.Spawned || !guest.Spawned ||
                host.Map != bed.Map || guest.Map != bed.Map ||
                guest.ownership?.OwnedBed != bed || !bed.IsOwner(guest) ||
                !LovePartnerRelationUtility.LovePartnerRelationExists(host, guest)) return false;
            // A medical bed cannot be assigned, but the host actually resting
            // in it can still share company with their assigned blob-bed guest.
            // Intimacy keeps the separate native nonmedical/owned-bed rules.
            bool hostOwnsBed = host.ownership?.OwnedBed == bed.HostBed && bed.HostBed.IsOwner(host);
            bool medicalHostPresent = allowMedicalHost && bed.HostBed.Medical &&
                                      host.CurrentBed() == bed.HostBed &&
                                      System.Linq.Enumerable.Contains(bed.HostBed.CurOccupants, host);
            if (!hostOwnsBed && !medicalHostPresent) return false;
            pair = new PartnerPair(bed, guest);
            return true;
        }

        private static bool OccupiesOwnedSlot(Pawn pawn, Building_Bed bed)
        {
            if (pawn.CurrentBed() != bed) return false;
            int slot = bed.OwnersForReading.IndexOf(pawn);
            return slot >= 0 && slot < bed.SleepingSlotsCount &&
                   pawn.Position == bed.GetSleepingSlotPos(slot) && bed.GetCurOccupant(slot) == pawn;
        }

        internal static bool TryGetOccupiedPair(Pawn pawn, out PartnerPair pair)
        {
            if (!TryGetAssignedPair(pawn, out pair)) return false;
            return !pair.HostBed.Medical && !pair.BlobBed.Medical &&
                   !pair.Host.Drafted && !pair.Guest.Drafted &&
                   !pair.Host.InMentalState && !pair.Guest.InMentalState &&
                   pair.Host.health.capacities.CanBeAwake && pair.Guest.health.capacities.CanBeAwake &&
                   OccupiesOwnedSlot(pair.Host, pair.HostBed) &&
                   OccupiesOwnedSlot(pair.Guest, pair.BlobBed);
        }

        public static bool SharesAssignedPartnerBed(Pawn pawn)
        {
            if (!TryGetAssignedPair(pawn, out PartnerPair pair, allowMedicalHost: true)) return false;
            Pawn partner = pair.Other(pawn);
            // Match the native thought's qualifying-partner filter. Its shared
            // ownership check precedes the ideology willingness check.
            return partner.IsColonist && !partner.IsWorldPawn() &&
                   partner.relations.everSeenByPlayer && pawn.IsSlaveOfColony == partner.IsSlaveOfColony;
        }

        internal static bool TryGetLinkedJob(JobDriver_Lovin driver, out Building_BlobBed bed,
            out Building_Bed hostBed)
        {
            // Only the guest's job targets the synthetic bed. Guard that job;
            // the native reciprocal driver already fails when its partner stops.
            // This avoids classifying an ordinary host-bed job as a linked one.
            bed = driver.job?.targetB.Thing as Building_BlobBed;
            hostBed = bed?.HostBed;
            return bed != null && bed.originatingPawn != null && driver.job?.def == JobDefOf.Lovin &&
                   driver.pawn != bed.originatingPawn && driver.job.targetA.Thing == bed.originatingPawn;
        }

        internal static bool LinkedJobStillValid(JobDriver_Lovin driver, Building_BlobBed bed,
            Building_Bed hostBed)
        {
            return TryGetOccupiedPair(driver.pawn, out PartnerPair pair) && pair.BlobBed == bed &&
                   pair.HostBed == hostBed && driver.job.targetA.Thing == pair.Other(driver.pawn) &&
                   driver.job.targetB.Thing == pair.BedFor(driver.pawn);
        }
    }

    [HarmonyPatch(typeof(LovePartnerRelationUtility), nameof(LovePartnerRelationUtility.GetPartnerInMyBed))]
    internal static class BoundBlobBedPartnerLookup
    {
        private static void Postfix(Pawn pawn, ref Pawn __result)
        {
            if (__result == null && BlobBedPartnerUtility.TryGetOccupiedPair(pawn, out var pair))
                __result = pair.Other(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class BoundBlobBedReciprocalLovin
    {
        private static void Prefix(Pawn ___pawn, Job newJob)
        {
            if (newJob?.def != JobDefOf.Lovin ||
                !BlobBedPartnerUtility.TryGetOccupiedPair(___pawn, out var pair)) return;
            Pawn partner = pair.Other(___pawn);
            Job partnerJob = partner.CurJob;
            if (newJob.targetA.Thing != partner || partnerJob?.def != JobDefOf.Lovin ||
                partnerJob.targetA.Thing != ___pawn || partnerJob.targetB.Thing != pair.BedFor(partner) ||
                (newJob.targetB.Thing != pair.HostBed && newJob.targetB.Thing != pair.BlobBed)) return;
            // Native Lovin passes its initiator's bed to the reciprocal job.
            // Retarget before StartJob cleans up the recipient's lying posture.
            newJob.SetTarget(TargetIndex.B, pair.BedFor(___pawn));
        }
    }

    [HarmonyPatch(typeof(JobDriver_Lovin), "MakeNewToils")]
    internal static class BoundBlobBedLovinLifetime
    {
        private static void Postfix(JobDriver_Lovin __instance, ref IEnumerable<Toil> __result)
        {
            if (__instance.job?.targetB.Thing is Building_BlobBed)
                __result = Guard(__instance, __result);
        }

        private static IEnumerable<Toil> Guard(JobDriver_Lovin driver, IEnumerable<Toil> nativeToils)
        {
            bool linked = BlobBedPartnerUtility.TryGetLinkedJob(driver, out var bed, out var hostBed);
            foreach (Toil toil in nativeToils)
            {
                if (linked)
                    toil.AddFailCondition(() => !BlobBedPartnerUtility.LinkedJobStillValid(driver, bed, hostBed));
                yield return toil;
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.SetInitialPosture))]
    internal static class BoundBlobBedLovinPosture
    {
        private static bool Prefix(JobDriver __instance)
        {
            if (!(__instance is JobDriver_Lovin) ||
                !BlobBedPartnerUtility.TryGetOccupiedPair(__instance.pawn, out var pair)) return true;
            // Native Lovin resets posture before its instantaneous claim/go-to
            // toils. For an already occupied linked pair that transient Standing
            // state makes the host appear to leave its bound bed. Keep the real
            // lying posture only when both exact native bed/partner targets match.
            return __instance.job.targetA.Thing != pair.Other(__instance.pawn) ||
                   __instance.job.targetB.Thing != pair.BedFor(__instance.pawn);
        }
    }
}
