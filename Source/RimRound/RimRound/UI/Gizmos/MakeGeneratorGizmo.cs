using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.Sound;
using RimRound.Things;
using HediffUtility = RimRound.Utilities.HediffUtility;

namespace RimRound.UI
{
    public class MakeGeneratorGizmo : Command_Toggle
    {
        public MakeGeneratorGizmo(MakeBlobIntoBed_ThingComp comp, FullnessAndDietStats_ThingComp fndComp) 
        {
            this.comp = comp;
            this.fndComp = fndComp;
            defaultLabel = "Make Generator";
            defaultDesc = "Legacy Paunch Power: generate constant power while transformed in bed. Stored calories are not consumed.";
            icon = Resources.MAKE_GENERATOR_ICON;
            isActive = () => comp.IsPowerSpot;
            toggleAction = () => { ToggleAction(); };
            Order = 404;
        }
        

        private void ToggleAction() 
        {
            if (comp == null) return;
            if (comp.IsPowerSpot || comp.generatorSpot != null) { Reset(); return; }
            Pawn pawn = comp.parent as Pawn;
            string reason = Building_PaunchGenerator.EligibilityBlockReason(pawn);
            if (reason != null) { Messages.Message(reason, MessageTypeDefOf.RejectInput, false); return; }
            int level = fndComp?.perkLevels?.EffectLevel("RR_PaunchPower_Title") ?? 0;
            ThingDef generatorDef = level == 1 ? Defs.ThingDefOf.RR_PaunchGenerator_I : level == 2 ? Defs.ThingDefOf.RR_PaunchGenerator_II : null;
            if (generatorDef == null) return;
            // Bounded placement, without wiping the pawn's bed, a wall or stock.
            IntVec3 cell = IntVec3.Invalid;
            foreach (IntVec3 candidate in new[] { pawn.Position + new IntVec3(0, 0, 1) }.Concat(GenAdj.CellsAdjacent8Way(pawn)))
                if (candidate.InBounds(pawn.Map) && GenSpawn.CanSpawnAt(generatorDef, candidate, pawn.Map, Rot4.North, false) &&
                    !candidate.GetThingList(pawn.Map).Any(t => t is Pawn || GenSpawn.SpawningWipes(generatorDef, t.def))) { cell = candidate; break; }
            if (!cell.IsValid) { Messages.Message("No free adjacent space for the pawn's generator.", MessageTypeDefOf.RejectInput, false); return; }
            var station = ThingMaker.MakeThing(generatorDef) as Building_PaunchGenerator;
            if (station == null) { Log.Error("Paunch generator definition requires Building_PaunchGenerator."); return; }
            station.Bind(pawn);
            comp.generatorSpot = station;
            comp.IsPowerSpot = true;
            try
            {
                HediffUtility.AddHediffSeverity(Defs.HediffDefOf.RimRound_BlobBed_III, pawn, 1f, true);
                station.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(station, cell, pawn.Map);
                Resources.gizmoClick.PlayOneShotOnCamera(null);
            }
            catch
            {
                Reset();
                throw;
            }
        }
        public void Reset()
        {
            if (comp == null) return;
            Pawn pawn = comp.parent as Pawn;
            Thing station = comp.generatorSpot;
            comp.generatorSpot = null;
            comp.IsPowerSpot = false;
            HediffUtility.RemoveHediffOfDefFrom(Defs.HediffDefOf.RimRound_BlobBed_III, pawn);
            if (station != null && !station.Destroyed && Building_PaunchGenerator.IsGenerator(station))
            {
                if (station is Building_PaunchGenerator bound && bound.HasBinding && !ReferenceEquals(bound.OriginatingPawn, pawn)) return;
                var power = (station as ThingWithComps)?.GetComp<CompPowerTrader>();
                if (power != null) { power.PowerOutput = 0f; power.PowerOn = false; }
                station.Destroy(DestroyMode.Vanish);
            }
        }
        FullnessAndDietStats_ThingComp fndComp;
        MakeBlobIntoBed_ThingComp comp;
    }
}
