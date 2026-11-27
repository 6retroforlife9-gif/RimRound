using RimRound.FeedingTube.Comps;
using RimRound.FeedingTube.Utilities;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimRound.FeedingTube
{
    public class Building_NutrientDistillery : Building
    {
        private FoodNetTrader_ThingComp trader;
        private int targetDensity = 1;
        private float targetRatio = 1f; // saved projection for old versions
        private int geometrySchema = 1;
        private bool legacyPortLayout;
        private Graphic legacyGraphic;
        public int TargetDensity => FoodServingMath.ClampGrade(targetDensity);
        public bool LegacyPortLayout => legacyPortLayout;
        public float TargetRatio => 1f / TargetDensity;

        public override Graphic Graphic
        {
            get
            {
                if (!legacyPortLayout || !Spawned) return base.Graphic;
                if (legacyGraphic == null)
                {
                    Graphic graphic = base.Graphic;
                    legacyGraphic = GraphicDatabase.Get<Graphic_Single>(graphic.path,
                        graphic.Shader, graphic.drawSize, graphic.color, graphic.colorTwo, graphic.data);
                }
                return legacyGraphic;
            }
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            if (!respawningAfterLoad)
            {
                Rotation = Rot4.North;
                legacyPortLayout = false;
            }
            else if (geometrySchema < 1)
            {
                if (Rotation == Rot4.South)
                {
                    // Native South anchoring of an even-width footprint is one
                    // cell west. Preserve all six cells and both existing ports.
                    Position += IntVec3.West;
                    Rotation = Rot4.North;
                    legacyPortLayout = false;
                }
                else legacyPortLayout = true;
            }
            geometrySchema = 1;
            legacyGraphic = null;
            base.SpawnSetup(map, respawningAfterLoad);
            trader = GetComp<FoodNetTrader_ThingComp>();
        }
        protected override void Tick() { base.Tick(); ProcessFood(); }
        public bool SetTargetDensity(int grade)
        {
            if (!FoodServingMath.GradeValid(grade)) return false;
            targetDensity = grade; targetRatio = 1f / grade; return true;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) targetRatio = TargetRatio;
            Scribe_Values.Look(ref targetRatio, "targetRatio", 1f);
            Scribe_Values.Look(ref targetDensity, "rrTargetDensity", 0);
            Scribe_Values.Look(ref geometrySchema, "rrFixedGeometrySchema", 0);
            Scribe_Values.Look(ref legacyPortLayout, "rrLegacyPortLayout", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // This migrates only the requested setting. Existing food N/V
                // is never rounded, relabelled, or rewritten with this grade.
                if (!FoodServingMath.GradeValid(targetDensity))
                    targetDensity = FoodServingMath.ClampGrade(FoodServingMath.Positive(targetRatio)
                        ? (int)Math.Round(1d / targetRatio, MidpointRounding.AwayFromZero) : 1);
                targetRatio = TargetRatio;
            }
        }
        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            text += "\n" + "RR_DistilleryDensityInspect".Translate(TargetDensity);
            if (legacyPortLayout) text += "\n" + "RR_DistilleryLegacyLayout".Translate();
            return text.Trim();
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "RR_DistilleryDensityCommand".Translate(TargetDensity),
                defaultDesc = "RR_DistilleryDensityDesc".Translate(),
                icon = Widgets.GetIconFor(ThingDefOf.MealNutrientPaste),
                action = delegate
                {
                    var options = new List<FloatMenuOption>();
                    for (int grade = 1; grade <= 5; grade++)
                    {
                        int captured = grade;
                        options.Add(new FloatMenuOption("RR_FoodDensityGrade".Translate(grade),
                            () => SetTargetDensity(captured)));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
        }
        private Pair<FoodNet, FoodNet> GetFoodNets()
        {
            FoodTransmitter_NetManager manager = FoodTransmitter_NetManager.For(Map);
            return new Pair<FoodNet, FoodNet>(manager?.FoodNetAt(FixedDistilleryGeometry.Input(this)),
                manager?.FoodNetAt(FixedDistilleryGeometry.Output(this)));
        }
        private void ProcessFood()
        {
            if (!Utilities.FeedingTubeUtility.IsHashIntervalTick(60) || trader == null ||
                !trader.IsOn || !trader.CanBeOn || !trader.TransmitsFoodNow) return;
            Pair<FoodNet, FoodNet> nets = GetFoodNets();
            if (nets.First == null || nets.Second == null || ReferenceEquals(nets.First, nets.Second)) return;
            using (var transfer = new ClassicFoodNetTransaction(nets.First, nets.Second))
            {
                ClassicFoodPortion source;
                if (!transfer.IsValid || !transfer.TryWithdrawNutrition(nets.First, 0.1f, out source) ||
                    !transfer.TryDepositExact(nets.Second, source.PreciseNutrition,
                        source.PreciseNutrition / TargetDensity, source.Preservation, source.SourceCreatedTick)) return;
                transfer.Commit();
            }
        }
    }
}
