using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.Ideology
{
    public enum AbundanceBedUse { None, Leader, MoralGuide, Feedee }

    public sealed class CompProperties_AbundanceBed : CompProperties
    { public CompProperties_AbundanceBed() { compClass=typeof(CompAbundanceBed); } }

    public sealed class CompAbundanceBed : ThingComp
    {
        public AbundanceBedUse Use;
        public Ideo Ideology;
        public Building_Bed Bed => parent as Building_Bed;
        public Pawn Owner => Bed?.OwnersForReading.Count==1 ? Bed.OwnersForReading[0] : null;
        public bool Designated => Use!=AbundanceBedUse.None && AbundanceRoles.Enabled(Ideology);
        public bool Matches(Pawn pawn)
        {
            if(!Designated || pawn==null || Owner!=pawn || pawn.Ideo!=Ideology)return false;
            return AbundanceBeds.HoldsRole(pawn,Use);
        }
        public void Designate(AbundanceBedUse use)
        { Use=use; Ideology=use==AbundanceBedUse.None ? null : Owner?.Ideo; }
        public override void PostExposeData()
        { base.PostExposeData(); Scribe_Values.Look(ref Use,"rrRitualBedUse"); Scribe_References.Look(ref Ideology,"rrRitualBedIdeo"); }
        public override string CompInspectStringExtra() => RbaBuildingReadiness.Inspect(this);
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if(!ModsConfig.IdeologyActive || parent.Faction!=Faction.OfPlayer)yield break;
            var designation=new Command_Action {
                defaultLabel="RR_AbundanceBedDesignation".Translate(RbaBuildingReadiness.UseLabel(Use)),
                defaultDesc="RR_AbundanceBedDesignationDesc".Translate(),
                icon=ContentFinder<UnityEngine.Texture2D>.Get("UI/RimRoundIdeology/EmbodiedAbundance"),
                action=()=>Find.WindowStack.Add(new FloatMenu(Enum.GetValues(typeof(AbundanceBedUse)).Cast<AbundanceBedUse>()
                    .Select(use=>new FloatMenuOption(RbaBuildingReadiness.UseLabel(use),()=>Designate(use))).ToList()))
            };
            if(!AbundanceRoles.Enabled(Owner?.Ideo) && Use==AbundanceBedUse.None)
                designation.Disable("RR_AbundanceBedNeedsOwner".Translate());
            yield return designation;
            if(!Designated)yield break;
            foreach(var ritual in Ideology.PreceptsListForReading.OfType<Precept_Ritual>().Where(r=>AbundanceRituals.IsFoodRitual(r)))
                foreach(var gizmo in ritual.GetGizmoFor(parent))yield return gizmo;
            // Reuse the holder's native ability commands, including their cooldowns
            // and targeting, rather than synthesizing free ritual activations.
            if(Matches(Owner))
                foreach(var ability in Owner.abilities?.AllAbilitiesForReading ?? new List<Ability>())
                    if(BedsideRituals.Supported(ability))
                        foreach(var gizmo in ability.GetGizmos())yield return gizmo;
        }
    }

    public static class AbundanceBeds
    {
        public static CompAbundanceBed Focus(Thing thing) => thing?.TryGetComp<CompAbundanceBed>();
        public static Building_Bed For(Pawn pawn) => pawn?.ownership?.OwnedBed is Building_Bed bed && Focus(bed)?.Matches(pawn)==true ? bed : null;
        public static bool HoldsRole(Pawn pawn,AbundanceBedUse use)
        {
            if(!AbundanceRoles.Enabled(pawn?.Ideo) || use==AbundanceBedUse.None)return false;
            Precept_Role role=pawn.Ideo.GetRole(pawn);
            return role?.Active==true && (use==AbundanceBedUse.Leader ? role.def.leaderRole :
                use==AbundanceBedUse.MoralGuide ? role.def.defName=="IdeoRole_Moralist" :
                use==AbundanceBedUse.Feedee && role.def.defName==AbundanceRoles.Feedee);
        }
        public static bool Valid(Building_Bed bed,Ideo ideo) => ideo!=null && bed?.Spawned==true && bed.Faction==Faction.OfPlayer &&
            Focus(bed) is CompAbundanceBed focus && focus.Ideology==ideo && focus.Matches(focus.Owner) &&
            !bed.IsBurning() && !bed.Medical && !bed.ForPrisoners;
        public static bool ReadyForBegin(Pawn pawn) => Ready(pawn) || RitualAttendance.CanWakeForFeast(pawn);
        public static bool Ready(Pawn pawn) => pawn?.Spawned==true && !pawn.Dead && !pawn.Drafted && !pawn.InMentalState &&
            !pawn.IsBurning() && pawn.Awake() && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness) &&
            pawn.needs?.rest?.CurLevelPercentage>0.05f && !pawn.Deathresting && !pawn.health.hediffSet.HasHediff(HediffDefOf.Anesthetic);
        public static bool HasTable(Building_Bed bed) => bed?.Spawned==true && HasTable(bed.Map,bed.Position);
        public static bool HasTable(Map map,IntVec3 focus) => FeastCompatibilityPolicy.Tables(map,focus).Any();
        public static IEnumerable<Building_Bed> Beds(Map map,Ideo ideo) => map==null ? Enumerable.Empty<Building_Bed>() :
            map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>().Where(b=>Valid(b,ideo));
        public static IEnumerable<IntVec3> ApproachCells(Building_Bed bed)
        {
            if(bed?.Spawned!=true)yield break;
            Room room=bed.GetRoom();
            foreach(IntVec3 c in bed.OccupiedRect().ExpandedBy(1).EdgeCells)
                if(c.InBounds(bed.Map) && c.Standable(bed.Map) && c.GetRoom(bed.Map)==room &&
                    c.InHorDistOf(bed.GetSleepingSlotPos(0),2.5f) &&
                    !c.GetThingList(bed.Map).Any(t=>t is Building_Door) &&
                    GenSight.LineOfSight(c,bed.GetSleepingSlotPos(0),bed.Map,true))yield return c;
        }
        public static IntVec3 Approach(Pawn server,Building_Bed bed) => ApproachCells(bed)
            .Where(c=>server.CanReserveAndReach(c,PathEndMode.OnCell,Danger.Some))
            .OrderBy(c=>server.Position.DistanceToSquared(c)).DefaultIfEmpty(IntVec3.Invalid).First();
        public static bool SameRoom(IntVec3 a,IntVec3 b,Map map)
        {
            if(map==null || !a.InBounds(map) || !b.InBounds(map))return false;
            Room room=a.GetRoom(map);
            return room!=null && room==b.GetRoom(map) && (!room.PsychologicallyOutdoors || a.InHorDistOf(b,14f));
        }
        public static IEnumerable<IntVec3> DiningSeats(Map map,IntVec3 focus)
        {
            if(map==null)yield break;
            foreach(var table in FeastCompatibilityPolicy.Tables(map,focus))
                foreach(IntVec3 cell in table.OccupiedRect().AdjacentCellsCardinal)
                    if(cell.InBounds(map) && cell.Standable(map) && SameRoom(cell,focus,map) &&
                        cell.GetThingList(map).Any(t=>FeastCompatibilityPolicy.SeatCells(t).Contains(cell)))yield return cell;
            // Ideology seating is useful throughout the venue, including each
            // native sittable cell of a long pew or a multi-cell kneel sheet.
            // The serving tables are a separate requirement from the seats.
            foreach(var seat in map.listerBuildings.allBuildingsColonist.Where(t=>
                t.def.building?.isSittable==true && t.def.building.buildingTags?.Contains("RitualSeat")==true &&
                !t.IsBurning() && SameRoom(t.Position,focus,map)))
                foreach(IntVec3 cell in FeastCompatibilityPolicy.SeatCells(seat))
                    if(cell.InBounds(map) && cell.Standable(map) && SameRoom(cell,focus,map))yield return cell;
        }
        public static bool DiningSeatRemains(Map map,IntVec3 cell,IntVec3 focus)
        {
            if(map==null || !cell.InBounds(map) || !cell.Standable(map) || !SameRoom(cell,focus,map))return false;
            var seats=cell.GetThingList(map).Where(t=>FeastCompatibilityPolicy.SeatCells(t).Contains(cell)).ToList();
            if(seats.Count==0)return false;
            if(seats.Any(t=>t.def.building?.buildingTags?.Contains("RitualSeat")==true))return true;
            return GenAdj.CardinalDirections.Select(d=>cell+d).Where(c=>c.InBounds(map)).Any(c=>
                c.GetThingList(map).Any(t=>FeastCompatibilityPolicy.IsDiningSurface(t) && SameRoom(t.Position,focus,map)));
        }
    }

    public sealed class RitualTarget_AbundanceFood : RitualObligationTargetWorker_AnyRitualSpotOrAltar
    {
        public RitualTarget_AbundanceFood() { }
        public RitualTarget_AbundanceFood(RitualObligationTargetFilterDef def):base(def) { }
        protected override RitualTargetUseReport CanUseTargetInternal(TargetInfo target,RitualObligation obligation)
        {
            if(!target.IsValid || target.Map==null)return false;
            bool ceremonial=parent.def.defName==AbundanceRituals.Ceremony;
            if(target.Thing is Building_Bed bed)
                return AbundanceBeds.Valid(bed,parent.ideo) ? (RitualTargetUseReport)true : "RR_AbundanceBedOwnerMismatch".Translate().ToString();
            if(ceremonial)return false;
            return FeastCompatibilityPolicy.IsDiningSurface(target.Thing) || base.CanUseTargetInternal(target,obligation).canUse;
        }
        public override IEnumerable<TargetInfo> GetTargets(RitualObligation obligation,Map map)
        {
            foreach(var bed in AbundanceBeds.Beds(map,parent.ideo))yield return bed;
            if(parent.def.defName==AbundanceRituals.Ceremony)yield break;
            foreach(var table in map.listerBuildings.allBuildingsColonist.Where(FeastCompatibilityPolicy.IsDiningSurface))yield return table;
            foreach(var target in base.GetTargets(obligation,map))yield return target;
        }
        public override IEnumerable<string> GetTargetInfos(RitualObligation obligation)
        {
            yield return "RR_AbundanceBedTarget".Translate();
            if(parent.def.defName!=AbundanceRituals.Ceremony)yield return "RR_AbundanceTableTarget".Translate();
        }
    }

    // Saved vanilla altar demands remain intact. A designated bed is an additional
    // valid building for this ideoligion; only its no-beds requirement is waived.
    internal static class AbundanceBuildingDemands
    {
        internal static bool Applies(IdeoBuildingPresenceDemand demand) => demand?.parent?.ThingDef?.isAltar==true && AbundanceRoles.Enabled(demand.parent.ideo);
        internal static bool NoBeds(RoomRequirement requirement) => requirement is RoomRequirement_ForbiddenBuildings forbidden &&
            requirement.labelKey=="RoomRequirementNoBeds" && forbidden.buildingTags?.Count==1 && forbidden.buildingTags[0]=="Bed";
        internal static int Met(IdeoBuildingPresenceDemand demand,Building_Bed bed)
        {
            Room room=demand.GetEffectiveRoom(bed);
            return demand.roomRequirements?.Count(r=>NoBeds(r) || room!=null && r.MetOrDisabled(room)) ?? 0;
        }
        internal static bool BedSatisfied(IdeoBuildingPresenceDemand demand,Building_Bed bed) =>
            demand.GetEffectiveRoom(bed)!=null && AbundanceBeds.HasTable(bed) && AbundanceBeds.ApproachCells(bed).Any() &&
            Met(demand,bed)==(demand.roomRequirements?.Count ?? 0);
        internal static IEnumerable<Thing> NativeBuildings(IdeoBuildingPresenceDemand demand,Map map) =>
            map==null ? Enumerable.Empty<Thing>() : map.listerThings.ThingsOfDef(demand.parent.ThingDef)
                .Where(t=>t is Building && t.Faction==Faction.OfPlayer && t.StyleSourcePrecept==demand.parent);
        [HarmonyPatch(typeof(IdeoBuildingPresenceDemand),nameof(IdeoBuildingPresenceDemand.BuildingPresent))]
        private static class Present
        { private static void Postfix(IdeoBuildingPresenceDemand __instance,Map map,ref bool __result)
            { if(!__result && Applies(__instance))__result=AbundanceBeds.Beds(map,__instance.parent.ideo).Any(); } }
        [HarmonyPatch(typeof(IdeoBuildingPresenceDemand),nameof(IdeoBuildingPresenceDemand.RequirementsSatisfied))]
        private static class Satisfied
        { private static void Postfix(IdeoBuildingPresenceDemand __instance,Map map,ref bool __result)
            {
                if(!Applies(__instance))return;
                // Native returns true immediately for an empty requirement list.
                // A bed-only alternative must still have its table and access.
                __result=NativeBuildings(__instance,map).Any(b=>__instance.roomRequirements.NullOrEmpty() ||
                    __instance.GetEffectiveRoom(b)!=null && __instance.roomRequirements.All(r=>r.MetOrDisabled(__instance.GetEffectiveRoom(b)))) ||
                    AbundanceBeds.Beds(map,__instance.parent.ideo).Any(b=>BedSatisfied(__instance,b));
            } }
        [HarmonyPatch(typeof(IdeoBuildingPresenceDemand),nameof(IdeoBuildingPresenceDemand.BestBuilding))]
        private static class Best
        { private static void Postfix(IdeoBuildingPresenceDemand __instance,Map map,bool ignoreFullyMetRequirements,ref Thing __result)
            {
                if(!Applies(__instance))return;
                var best=AbundanceBeds.Beds(map,__instance.parent.ideo).Where(b=>!ignoreFullyMetRequirements || !BedSatisfied(__instance,b))
                    .OrderByDescending(b=>BedSatisfied(__instance,b)).ThenByDescending(b=>Met(__instance,b)).ThenBy(b=>b.thingIDNumber).FirstOrDefault();
                Thing prior=__result;
                Room priorRoom=__instance.GetEffectiveRoom(prior);
                int priorMet=priorRoom==null ? 0 : (__instance.roomRequirements?.Count(r=>r.MetOrDisabled(priorRoom)) ?? 0);
                if(best!=null && (prior==null || BedSatisfied(__instance,best) && priorMet<(__instance.roomRequirements?.Count ?? 0) ||
                    Met(__instance,best)>priorMet))__result=best;
            } }
        [HarmonyPatch(typeof(IdeoBuildingPresenceDemand),nameof(IdeoBuildingPresenceDemand.RoomRequirementsInfo),MethodType.Getter)]
        private static class Info
        { private static void Postfix(IdeoBuildingPresenceDemand __instance,ref List<string> __result)
            {
                if(Find.CurrentMap==null || !Applies(__instance) || !(__instance.BestBuilding(Find.CurrentMap) is Building_Bed bed))return;
                Room room=__instance.GetEffectiveRoom(bed);
                __result=__instance.roomRequirements?.Where(r=>!NoBeds(r) && (room==null || !r.MetOrDisabled(room)))
                    .Select(r=>r.LabelCap(room).ToString()).ToList() ?? new List<string>();
                if(room==null)__result.Add("RR_RbaBedIndoors".Translate());
                if(!AbundanceBeds.HasTable(bed))__result.Add("RR_FeastRequiresTable".Translate());
                if(!AbundanceBeds.ApproachCells(bed).Any())__result.Add("RR_RbaBedNoAccess".Translate());
            } }
        [HarmonyPatch(typeof(IdeoBuildingPresenceDemand),nameof(IdeoBuildingPresenceDemand.AllBuildings))]
        private static class All
        { private static void Postfix(IdeoBuildingPresenceDemand __instance,Map map,ref IEnumerable<Thing> __result)
            { if(Applies(__instance))__result=__result.Concat(AbundanceBeds.Beds(map,__instance.parent.ideo)).Distinct(); } }
        [HarmonyPatch(typeof(Precept_Building),nameof(Precept_Building.GetAlerts))]
        private static class Alerts
        { private static void Postfix(Precept_Building __instance,ref IEnumerable<Alert> __result)
            {
                var demand=__instance.presenceDemand; Map map=Find.CurrentMap;
                if(map!=null && Applies(demand) && demand.roomRequirements.NullOrEmpty() && demand.AppliesTo(map) &&
                    demand.BuildingPresent(map) && !demand.RequirementsSatisfied(map))
                    __result=__result.Concat(new Alert[]{demand.AlertCachedMissingDisrespected});
            } }
        [HarmonyPatch(typeof(PreceptRequirement_Altar),nameof(PreceptRequirement_Altar.Met))]
        private static class Altar
        { private static void Postfix(List<Precept> precepts,ref bool __result)
            { if(!__result && precepts.Any(p=>AbundanceRoles.Enabled(p.ideo)))__result=true; } }
    }
}
