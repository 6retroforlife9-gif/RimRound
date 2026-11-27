using System.Linq;
using System.Collections.Generic;
using RimRound.Things;
using RimRound.UI;
using RimRound.Utilities;
using RimWorld;
using Verse;
using HediffUtility=RimRound.Utilities.HediffUtility;

namespace RimRound.Comps
{
    public class MakeBlobIntoBed_ThingComp : ThingComp
    {
        private bool _isBed,_isRecSpot,_isPowerSpot;
        private FullnessAndDietStats_ThingComp fndComp;
        public bool IsBed { get=>_isBed;set=>_isBed=value; }
        public bool IsRecSpot { get=>_isRecSpot;set=>_isRecSpot=value; }
        public bool IsPowerSpot { get=>_isPowerSpot;set=>_isPowerSpot=value; }
        public MakeBlobIntoBedGizmo gizmo;
        public MakeRecreationSpotGizmo gizmoRec;
        public MakeGeneratorGizmo generatorGizmo;
        public Thing recSpot,generatorSpot,blobBed;
        // Physical tier, not a stat read or a mutable perk lookup.
        public bool HasLegacyOnlyTransformation=>IsRecSpot||IsPowerSpot||IsBed&&blobBed?.def.defName!="BlobBed_FoldsOfHeaven_z";
        public int BedLevel=>ColonyProgression.LegacyPerksActive?(parent.TryGetComp<FullnessAndDietStats_ThingComp>()?.perkLevels?.EffectLevel("RR_FoldsOfHeaven_Title")??0):0;
        public string EligibilityBlockReason
        {
            get
            {
                Pawn pawn=parent as Pawn;
                if(pawn==null||pawn.Destroyed||pawn.Dead||!pawn.Spawned)return "The originating pawn must be alive and spawned.";
                if(!pawn.InBed()||pawn.CurrentBed() is Building_BlobBed)return "The originating pawn must remain in another actual bed.";
                Hediff weight=pawn.health?.hediffSet?.GetFirstHediffOfDef(Defs.HediffDefOf.RimRound_Weight);
                if(weight==null||!(weight.Severity>=HediffUtility.KilosToSeverityWithBaseWeight(GlobalSettings.weightToBeBed.threshold)))return "The originating pawn no longer meets the existing blob transformation threshold.";
                return null;
            }
        }
        public string RecreationBlockReason=>EligibilityBlockReason??(!ColonyProgression.LegacyPerksActive?"Blob recreation requires the colony's Legacy preset.":(parent.TryGetComp<FullnessAndDietStats_ThingComp>()?.perkLevels?.EffectLevel("RR_WeLikeToParty_Title")??0)<=0?"The originating pawn needs an active We Like To Party purchase.":null);
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);fndComp=parent.TryGetComp<FullnessAndDietStats_ThingComp>();
            gizmo=new MakeBlobIntoBedGizmo(this,fndComp);gizmoRec=new MakeRecreationSpotGizmo(this);generatorGizmo=new MakeGeneratorGizmo(this,fndComp);
        }
        public override void PostExposeData()
        {
            base.PostExposeData();Scribe_Values.Look(ref _isBed,"blobIsBed",false);Scribe_Values.Look(ref _isRecSpot,"isRecSpot",false);Scribe_Values.Look(ref _isPowerSpot,"isPowerSpot",false);
            Scribe_References.Look(ref recSpot,"recSpot");Scribe_References.Look(ref generatorSpot,"generatorSpot");Scribe_References.Look(ref blobBed,"blobBed");
        }
        public override void CompTick()
        {
            base.CompTick();if(!parent.IsHashIntervalTick(15))return;
            Pawn pawn=parent as Pawn;
            if((IsBed||blobBed!=null)&&(!(blobBed is Building_BlobBed bed)||!ReferenceEquals(bed.originatingPawn,pawn)||!bed.IsOperational))ResetBed();
            if((IsRecSpot||recSpot!=null)&&(!(recSpot is Building_BlobRecreation rec)||!ReferenceEquals(rec.OriginatingPawn,pawn)||!rec.IsOperational))ResetRecreation();
        }
        public override void PostDeSpawn(Map map,DestroyMode mode=DestroyMode.Vanish){ResetBed();ResetRecreation();base.PostDeSpawn(map,mode);}
        public override void PostDestroy(DestroyMode mode,Map previousMap){ResetBed();ResetRecreation();base.PostDestroy(mode,previousMap);}
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if(gizmo==null||gizmoRec==null||generatorGizmo==null)yield break;
            // Enumeration only prepares UI state; it never repairs gameplay.
            if(IsBed||blobBed!=null){gizmo.Disabled=false;yield return gizmo;}
            else if(GlobalSettings.showBlobIntobedGizmo&&EligibilityBlockReason==null){gizmo.Disabled=false;yield return gizmo;}
            if(IsRecSpot||recSpot!=null){gizmoRec.Disabled=false;yield return gizmoRec;}
            else if(GlobalSettings.showBlobIntobedGizmo&&ColonyProgression.LegacyPerksActive&&(fndComp?.perkLevels?.EffectLevel("RR_WeLikeToParty_Title")??0)>0){gizmoRec.Disabled=false;string reason=RecreationBlockReason;if(reason!=null)gizmoRec.Disable(reason);yield return gizmoRec;}
            if(IsPowerSpot||generatorSpot!=null){generatorGizmo.Disabled=false;yield return generatorGizmo;}
            else if(GlobalSettings.showBlobIntobedGizmo&&ColonyProgression.LegacyPerksActive&&(fndComp?.perkLevels?.EffectLevel("RR_PaunchPower_Title")??0)>0){generatorGizmo.Disabled=false;string reason=Building_PaunchGenerator.EligibilityBlockReason(parent as Pawn);if(reason!=null)generatorGizmo.Disable(reason);yield return generatorGizmo;}
        }
        private bool TryPlacement(ThingDef def,out IntVec3 cell)
        {
            cell=IntVec3.Invalid;Pawn pawn=parent as Pawn;if(def==null||pawn?.Map==null)return false;
            foreach(IntVec3 candidate in new[]{pawn.Position+new IntVec3(0,0,-2)}.Concat(GenAdj.CellsAdjacent8Way(pawn)))
                if(candidate.InBounds(pawn.Map)&&GenSpawn.CanSpawnAt(def,candidate,pawn.Map,Rot4.South,false)&&GenAdj.OccupiedRect(candidate,Rot4.South,def.size).Cells.All(c=>c.InBounds(pawn.Map)&&!c.GetThingList(pawn.Map).Any(t=>t is Pawn||t is Building||GenSpawn.SpawningWipes(def,t.def)))){cell=candidate;return true;}
            return false;
        }
        private bool TryBedPlacement(ThingDef def,out IntVec3 cell,out Rot4 rotation)
        {
            cell=IntVec3.Invalid;rotation=Rot4.South;
            Pawn pawn=parent as Pawn;
            Building_Bed hostBed=pawn?.CurrentBed();
            if(def==null||hostBed==null||pawn.Map==null)return false;
            // Search around the entire supporting bed. Searching only around
            // the pawn's pillow cell leaves every candidate inside a large bed.
            var candidates=hostBed.OccupiedRect().ExpandedBy(2).Cells
                .OrderBy(c=>c.DistanceToSquared(pawn.Position)).ThenBy(c=>c.z).ThenBy(c=>c.x);
            foreach(IntVec3 candidate in candidates)
                for(int turn=0;turn<4;turn++)
                {
                    Rot4 facing=new Rot4((hostBed.Rotation.AsInt+turn)%4);
                    if(!Building_BlobBed.PlacementTouchesHost(def,candidate,facing,hostBed))continue;
                    var footprint=GenAdj.OccupiedRect(candidate,facing,def.size);
                    if(!footprint.Cells.All(c=>c.InBounds(pawn.Map)&&c.Standable(pawn.Map)&&
                        !c.GetThingList(pawn.Map).Any(t=>t is Pawn||GenSpawn.SpawningWipes(def,t.def)||
                            t is Building&&t.def.altitudeLayer>=AltitudeLayer.FloorEmplacement)))continue;
                    if(!GenSpawn.CanSpawnAt(def,candidate,pawn.Map,facing,false))continue;
                    cell=candidate;rotation=facing;return true;
                }
            return false;
        }
        public bool TrySpawnBed(out string reason)
        {
            reason=EligibilityBlockReason;if(reason!=null)return false;
            if(IsBed||blobBed!=null){reason="End the existing blob-bed transformation before starting another.";return false;}
            ThingDef def=BedLevel==0?Defs.ThingDefOf.BlobBed_FoldsOfHeaven_z:BedLevel==1?Defs.ThingDefOf.BlobBed_FoldsOfHeaven_I:BedLevel==2?Defs.ThingDefOf.BlobBed_FoldsOfHeaven_II:BedLevel==3?Defs.ThingDefOf.BlobBed_FoldsOfHeaven_III:null;
            IntVec3 cell;Rot4 rotation;if(def==null||!TryBedPlacement(def,out cell,out rotation)){reason="No free adjacent space or supported tier for this blob bed.";return false;}
            var bed=ThingMaker.MakeThing(def) as Building_BlobBed;if(bed==null){reason="The blob-bed definition has no bound bed owner.";return false;}
            bed.Bind(parent as Pawn);blobBed=bed;IsBed=true;
            try{HediffUtility.AddHediffSeverity(Defs.HediffDefOf.RimRound_BlobBed,parent as Pawn,1f,true);bed.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(bed,cell,parent.Map,rotation);return true;}
            catch{ResetBed();throw;}
        }
        public void SpawnBed(){string reason;if(!TrySpawnBed(out reason))Messages.Message(reason,MessageTypeDefOf.RejectInput,false);}
        public bool TrySpawnRecreation(out string reason)
        {
            reason=RecreationBlockReason;if(reason!=null)return false;
            if(IsRecSpot||recSpot!=null){reason="End the existing recreation transformation before starting another.";return false;}
            ThingDef def=Defs.ThingDefOf.WLTP_Building;IntVec3 cell;if(!TryPlacement(def,out cell)){reason="No free adjacent space for this recreation station.";return false;}
            var station=ThingMaker.MakeThing(def) as Building_BlobRecreation;if(station==null){reason="The recreation definition has no bound station owner.";return false;}
            station.Bind(parent as Pawn);recSpot=station;IsRecSpot=true;
            try{HediffUtility.AddHediffSeverity(Defs.HediffDefOf.RimRound_BlobBed_II,parent as Pawn,1f,true);station.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(station,cell,parent.Map);return true;}
            catch{ResetRecreation();throw;}
        }
        public void ResetBed()
        {
            Thing previous=blobBed;blobBed=null;IsBed=false;HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RimRound_BlobBed,parent as Pawn);
            if(previous is Building_BlobBed bed&&!bed.Destroyed&&ReferenceEquals(bed.originatingPawn,parent))bed.EndTransformation();
        }
        public void ResetRecreation()
        {
            Thing previous=recSpot;recSpot=null;IsRecSpot=false;HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RimRound_BlobBed_II,parent as Pawn);
            if(previous is Building_BlobRecreation station&&!station.Destroyed&&ReferenceEquals(station.OriginatingPawn,parent))station.EndTransformation();
        }
    }
}
