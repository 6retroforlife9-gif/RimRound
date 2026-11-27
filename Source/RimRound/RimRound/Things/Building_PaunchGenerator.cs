using System;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using Verse;
using HediffUtility = RimRound.Utilities.HediffUtility;

namespace RimRound.Things
{
    // A synthetic station belongs to one real pawn. References do not protect
    // that pawn from native world GC, or recreate a lost pawn/station.
    public sealed class Building_PaunchGenerator : Building
    {
        private Pawn originatingPawn;
        private bool bindingEstablished;
        private bool releasing;
        public Pawn OriginatingPawn => originatingPawn;
        public bool HasBinding => bindingEstablished;
        public bool CanGeneratePower => OperationalBlockReason == null;
        public static bool IsGenerator(Thing thing) => thing?.def?.defName == "RR_PaunchGenerator_I" || thing?.def?.defName == "RR_PaunchGenerator_II";

        public static string EligibilityBlockReason(Pawn pawn)
        {
            if (!ColonyProgression.LegacyPerksActive) return "Paunch generation requires the colony's Legacy preset.";
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) return "The originating pawn must be alive and spawned.";
            if (!pawn.InBed()) return "The originating pawn must remain in a bed.";
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            int level = body?.perkLevels?.EffectLevel("RR_PaunchPower_Title") ?? 0;
            if (level != 1 && level != 2) return "The originating pawn needs an active Paunch Power purchase.";
            var weight = pawn.health?.hediffSet?.GetFirstHediffOfDef(RimRound.Defs.HediffDefOf.RimRound_Weight);
            if (weight == null || weight.Severity < HediffUtility.KilosToSeverityWithBaseWeight(GlobalSettings.weightToBeBed.threshold))
                return "The originating pawn no longer meets the existing blob transformation threshold.";
            if (pawn.TryGetComp<MakeBlobIntoBed_ThingComp>() == null) return "This pawn has no blob transformation controller.";
            return null;
        }

        public string OperationalBlockReason
        {
            get
            {
                if (!bindingEstablished || originatingPawn == null) return "No originating pawn binding. Toggle the eligible pawn's generator off, then on.";
                string reason = EligibilityBlockReason(originatingPawn);
                if (reason != null) return reason;
                if (!Spawned || originatingPawn.Map != Map) return "The generator and its originating pawn must remain on the same map.";
                var controller = originatingPawn.TryGetComp<MakeBlobIntoBed_ThingComp>();
                if (!controller.IsPowerSpot || !ReferenceEquals(controller.generatorSpot, this)) return "The originating pawn no longer owns this generator transformation.";
                if (!originatingPawn.health.hediffSet.HasHediff(RimRound.Defs.HediffDefOf.RimRound_BlobBed_III)) return "The originating pawn's generator transformation has ended.";
                int level = originatingPawn.TryGetComp<FullnessAndDietStats_ThingComp>().perkLevels.EffectLevel("RR_PaunchPower_Title") ?? 0;
                if ((level == 1 && def.defName != "RR_PaunchGenerator_I") || (level == 2 && def.defName != "RR_PaunchGenerator_II")) return "The generator tier no longer matches the active purchase. Retoggle the transformation.";
                return null;
            }
        }

        public void Bind(Pawn pawn)
        {
            if (pawn == null || bindingEstablished || Spawned || Destroyed) throw new InvalidOperationException("A new unspawned Paunch generator may be bound exactly once.");
            originatingPawn = pawn;
            bindingEstablished = true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref originatingPawn, "rrPaunchOriginatingPawn");
            Scribe_Values.Look(ref bindingEstablished, "rrPaunchBindingEstablished", false);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            // Loading references may not be ready yet. Do not infer a binding
            // or destroy a station during load; the first real tick validates it.
            if (!CanGeneratePower && GetComp<CompPowerTrader>() is CompPowerTrader power) power.PowerOutput = 0f;
        }

        protected override void Tick()
        {
            // The native power comp's DesiredPowerOutput also guards every read.
            base.Tick();
            if (bindingEstablished && !CanGeneratePower) EndTransformation();
        }

        public void EndTransformation()
        {
            ReleaseOwner();
            if (!Destroyed) Destroy(DestroyMode.Vanish);
        }

        private void StopPower()
        {
            var power = GetComp<CompPowerTrader>();
            if (power == null) return;
            power.PowerOutput = 0f;
            power.PowerOn = false;
        }

        private void ReleaseOwner()
        {
            if (releasing) return;
            releasing = true;
            Pawn owner = originatingPawn;
            originatingPawn = null;
            try
            {
                StopPower();
                var controller = owner?.TryGetComp<MakeBlobIntoBed_ThingComp>();
                // A genuine replacement bound to this owner keeps its state.
                // A stale foreign pointer is cleared on this owner's comp only;
                // the foreign station and its owner's state are never touched.
                bool hasOwnedReplacement = controller?.generatorSpot is Building_PaunchGenerator replacement &&
                    !ReferenceEquals(replacement, this) && replacement.HasBinding && !replacement.Destroyed &&
                    ReferenceEquals(replacement.OriginatingPawn, owner) && replacement.CanGeneratePower;
                if (controller != null && !hasOwnedReplacement)
                {
                    controller.generatorSpot = null;
                    controller.IsPowerSpot = false;
                    HediffUtility.RemoveHediffOfDefFrom(RimRound.Defs.HediffDefOf.RimRound_BlobBed_III, owner);
                }
            }
            finally { releasing = false; }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            ReleaseOwner();
            base.DeSpawn(mode);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            ReleaseOwner();
            base.Destroy(mode);
        }
    }

    // Old saves may deserialize the old Verse.Building class. A power-comp
    // guard also leaves these unbound historical objects inert, without migration.
    public sealed class CompPowerPlant_PaunchGenerator : CompPowerPlant
    {
        protected override float DesiredPowerOutput =>
            parent is Building_PaunchGenerator station && station.CanGeneratePower ? base.DesiredPowerOutput : 0f;

        public override string CompInspectStringExtra()
        {
            string reason = parent is Building_PaunchGenerator station ? station.OperationalBlockReason :
                "No originating pawn binding. Toggle the eligible pawn's generator off, then on.";
            string native = base.CompInspectStringExtra();
            return reason == null ? native : native + (string.IsNullOrEmpty(native) ? "" : "\n") + "Inactive: " + reason;
        }
    }
}
