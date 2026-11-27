using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using HarmonyLib;
using RimRound.Comps;

namespace RimRound.Things
{
    public class Building_BlobBed : Building_Bed
    {
        public Pawn originatingPawn;
        private Building_Bed hostBed;
        private bool releasing;
        // Legacy saved beds acquire their supporting bed on their first tick,
        // after cross references and native pawn jobs have finished loading.
        public Building_Bed HostBed=>hostBed??originatingPawn?.CurrentBed();
        public bool IsOperational=>OperationalBlockReason==null;
        public string OperationalBlockReason
        {
            get
            {
                if(originatingPawn==null)return "No saved originating pawn; this synthetic bed is inactive.";
                var comp=originatingPawn.TryGetComp<MakeBlobIntoBed_ThingComp>();
                if(comp==null)return "The originating pawn has no transformation controller.";
                string reason=comp.EligibilityBlockReason;if(reason!=null)return reason;
                if(!Spawned||originatingPawn.Map!=Map)return "The bed and its originating pawn must remain on the same map.";
                Building_Bed support=HostBed;
                if(support==null||support is Building_BlobBed||!support.Spawned||support.Map!=Map||
                    !ReferenceEquals(originatingPawn.CurrentBed(),support)||
                    !PlacementTouchesHost(def,Position,Rotation,support))
                    return "The bed and its originating pawn must remain on the same map.";
                if(!comp.IsBed||!ReferenceEquals(comp.blobBed,this))return "The originating pawn no longer owns this blob bed.";
                string expected=comp.BedLevel==0?"BlobBed_FoldsOfHeaven_z":comp.BedLevel==1?"BlobBed_FoldsOfHeaven_I":comp.BedLevel==2?"BlobBed_FoldsOfHeaven_II":comp.BedLevel==3?"BlobBed_FoldsOfHeaven_III":null;
                if(def.defName!=expected)return "The bed tier no longer matches the active mode and purchase. Retoggle the transformation.";
                if(!originatingPawn.health.hediffSet.HasHediff(Defs.HediffDefOf.RimRound_BlobBed))return "The originating pawn's blob-bed transformation has ended.";
                return null;
            }
        }
        public void Bind(Pawn pawn)
        {
            if(pawn==null||originatingPawn!=null||Spawned||Destroyed)throw new InvalidOperationException("A new unspawned blob bed can be bound once.");
            originatingPawn=pawn;
            hostBed=pawn.CurrentBed();
        }

        public static bool PlacementTouchesHost(ThingDef bedDef,IntVec3 cell,Rot4 rotation,Building_Bed support)
        {
            if(bedDef==null||support==null)return false;
            CellRect host=support.OccupiedRect(),adjacent=host.ExpandedBy(1);
            CellRect guest=GenAdj.OccupiedRect(cell,rotation,bedDef.size);
            return guest.Cells.All(c=>!host.Contains(c))&&guest.Cells.Any(c=>adjacent.Contains(c));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look<Pawn>(ref originatingPawn, "originatingPawn");
            Scribe_References.Look(ref hostBed,"rrBlobHostBed");
        }
        protected override void Tick()
        {
            base.Tick();
            if(hostBed==null&&originatingPawn!=null&&Scribe.mode==LoadSaveMode.Inactive)
                hostBed=originatingPawn.CurrentBed();
            if(this.IsHashIntervalTick(15)&&!IsOperational)EndTransformation();
        }
        public void EndTransformation(){ReleaseOwner();if(!Destroyed)Destroy(DestroyMode.Vanish);}
        private void ReleaseOwner()
        {
            if(releasing)return;releasing=true;Pawn owner=originatingPawn;originatingPawn=null;
            try
            {
                var comp=owner?.TryGetComp<MakeBlobIntoBed_ThingComp>();
                bool replacement=comp?.blobBed is Building_BlobBed bed&&!ReferenceEquals(bed,this)&&ReferenceEquals(bed.originatingPawn,owner)&&bed.IsOperational;
                if(comp!=null&&!replacement){comp.blobBed=null;comp.IsBed=false;RimRound.Utilities.HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RimRound_BlobBed,owner);}
            }
            finally{releasing=false;}
        }
        public override void DeSpawn(DestroyMode mode=DestroyMode.Vanish){ReleaseOwner();base.DeSpawn(mode);}
        public override void Destroy(DestroyMode mode=DestroyMode.Vanish){ReleaseOwner();base.Destroy(mode);}
        public override string GetInspectString(){string native=base.GetInspectString(),reason=OperationalBlockReason;return reason==null?native:native+(string.IsNullOrEmpty(native)?"":"\n")+"Inactive: "+reason;}
    }

    [HarmonyPatch(typeof(RestUtility),nameof(RestUtility.CanUseBedNow))]
    internal static class BoundBlobBedSelection
    {
        private static void Postfix(Thing bedThing,ref bool __result){if(__result&&bedThing is Building_BlobBed bed&&!bed.IsOperational)__result=false;}
    }
    [HarmonyPatch(typeof(JobDriver_LayDown),nameof(JobDriver_LayDown.TryMakePreToilReservations))]
    internal static class BoundBlobBedReservation
    {
        private static bool Prefix(JobDriver_LayDown __instance,ref bool __result){if(!(__instance.Bed is Building_BlobBed bed)||bed.IsOperational)return true;__result=false;return false;}
    }
    [HarmonyPatch(typeof(JobDriver_LayDown),"MakeNewToils")]
    internal static class BoundBlobBedRunningJob
    {
        private static void Postfix(JobDriver_LayDown __instance,ref IEnumerable<Toil> __result){__result=Guard(__instance,__result);}
        private static IEnumerable<Toil> Guard(JobDriver_LayDown driver,IEnumerable<Toil> toils)
        {
            foreach(Toil toil in toils){toil.AddFailCondition(()=>driver.Bed is Building_BlobBed bed&&!bed.IsOperational);yield return toil;}
        }
    }
}
