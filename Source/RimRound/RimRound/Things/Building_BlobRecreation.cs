using System;
using System.Collections.Generic;
using HarmonyLib;
using RimRound.AI;
using RimRound.Comps;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.Things
{
    public sealed class Building_BlobRecreation : Building
    {
        private Pawn originatingPawn;
        private bool releasing;
        public Pawn OriginatingPawn=>originatingPawn;
        public bool IsOperational=>OperationalBlockReason==null;
        public static bool CanUse(Thing station)=>station is Building_BlobRecreation bound&&bound.IsOperational;
        public string OperationalBlockReason
        {
            get
            {
                if(originatingPawn==null)return "No saved originating pawn; this recreation station is inactive.";
                var comp=originatingPawn.TryGetComp<MakeBlobIntoBed_ThingComp>();if(comp==null)return "The originating pawn has no transformation controller.";
                string reason=comp.RecreationBlockReason;if(reason!=null)return reason;
                if(!Spawned||originatingPawn.Map!=Map)return "The station and its originating pawn must remain on the same map.";
                if(!comp.IsRecSpot||!ReferenceEquals(comp.recSpot,this))return "The originating pawn no longer owns this recreation station.";
                if(!originatingPawn.health.hediffSet.HasHediff(Defs.HediffDefOf.RimRound_BlobBed_II))return "The originating pawn's recreation transformation has ended.";
                return null;
            }
        }
        public void Bind(Pawn pawn){if(pawn==null||originatingPawn!=null||Spawned||Destroyed)throw new InvalidOperationException("A new unspawned recreation station can be bound once.");originatingPawn=pawn;}
        public override void ExposeData(){base.ExposeData();Scribe_References.Look(ref originatingPawn,"rrBlobRecOriginatingPawn");}
        protected override void Tick(){base.Tick();if(this.IsHashIntervalTick(15)&&!IsOperational)EndTransformation();}
        public void EndTransformation(){ReleaseOwner();if(!Destroyed)Destroy(DestroyMode.Vanish);}
        private void ReleaseOwner()
        {
            if(releasing)return;releasing=true;Pawn owner=originatingPawn;originatingPawn=null;
            try
            {
                var comp=owner?.TryGetComp<MakeBlobIntoBed_ThingComp>();
                bool replacement=comp?.recSpot is Building_BlobRecreation station&&!ReferenceEquals(station,this)&&ReferenceEquals(station.OriginatingPawn,owner)&&station.IsOperational;
                if(comp!=null&&!replacement){comp.recSpot=null;comp.IsRecSpot=false;RimRound.Utilities.HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RimRound_BlobBed_II,owner);}
            }
            finally{releasing=false;}
        }
        public override void DeSpawn(DestroyMode mode=DestroyMode.Vanish){ReleaseOwner();base.DeSpawn(mode);}
        public override void Destroy(DestroyMode mode=DestroyMode.Vanish){ReleaseOwner();base.Destroy(mode);}
    }
    [HarmonyPatch(typeof(JoyGiver_PlayWithBlob),"TryGivePlayJob")]
    internal static class BoundBlobRecreationSelection
    {
        private static bool Prefix(Thing t,ref Job __result){if(Building_BlobRecreation.CanUse(t))return true;__result=null;return false;}
    }
    [HarmonyPatch(typeof(JobDriver_PlayWithBlob),nameof(JobDriver_PlayWithBlob.TryMakePreToilReservations))]
    internal static class BoundBlobRecreationReservation
    {
        private static bool Prefix(JobDriver_PlayWithBlob __instance,ref bool __result){if(Building_BlobRecreation.CanUse(__instance.job?.targetA.Thing))return true;__result=false;return false;}
    }
    [HarmonyPatch(typeof(JobDriver_PlayWithBlob),"MakeNewToils")]
    internal static class BoundBlobRecreationRunningJob
    {
        private static void Postfix(JobDriver_PlayWithBlob __instance,ref IEnumerable<Toil> __result){__result=Guard(__instance,__result);}
        private static IEnumerable<Toil> Guard(JobDriver_PlayWithBlob driver,IEnumerable<Toil> toils)
        {
            foreach(Toil toil in toils){toil.AddFailCondition(()=>!Building_BlobRecreation.CanUse(driver.job?.targetA.Thing));yield return toil;}
        }
    }
    // Older saves may retain the old serialized Verse.Building class. They have
    // no owner and remain inert; never infer a binding or delete foreign stock.
    [HarmonyPatch(typeof(ThingWithComps),nameof(ThingWithComps.GetInspectString))]
    internal static class BlobRecreationInspect
    {
        private static void Postfix(ThingWithComps __instance,ref string __result)
        {
            if(__instance.def.defName!="WLTP_Building")return;
            string reason=__instance is Building_BlobRecreation station?station.OperationalBlockReason:"Historical station has no saved originating pawn; inactive. End and retoggle the pawn's transformation.";
            if(reason!=null)__result+=(string.IsNullOrEmpty(__result)?"":"\n")+"Inactive: "+reason;
        }
    }
}
