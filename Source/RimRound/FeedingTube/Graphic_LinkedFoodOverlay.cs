using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using UnityEngine;

namespace RimRound.FeedingTube
{
    public class Graphic_LinkedFoodOverlay : Graphic_LinkedTransmitterOverlay
    {
		public Graphic_LinkedFoodOverlay(Graphic subGraphic) : base(subGraphic)
		{
		}

		public override bool ShouldLinkWith(IntVec3 c, Thing parent)
		{
			return c.InBounds(parent.Map) && FoodTransmitter_NetManager.For(parent.Map).FoodNetAt(c) != null;
		}

		public override void Print(SectionLayer layer, Thing parent, float extraRotation)
		{
			if (!(parent is Building_NutrientDistillery distillery))
			{
				base.Print(layer, parent, extraRotation);
				return;
			}

			// The machine has two separate ports, not six conduit cells. Join each
			// external port to its nearest socket without drawing a through-pipe.
			PrintPort(layer, distillery, FixedDistilleryGeometry.Input(distillery));
			PrintPort(layer, distillery, FixedDistilleryGeometry.Output(distillery));
		}

		private void PrintPort(SectionLayer layer, Building_NutrientDistillery distillery, IntVec3 port)
		{
			CellRect footprint = distillery.OccupiedRect();
			var socket = new IntVec3(Mathf.Clamp(port.x, footprint.minX, footprint.maxX), 0,
				Mathf.Clamp(port.z, footprint.minZ, footprint.maxZ));
			Vector3 center = (port.ToVector3ShiftedWithAltitude(AltitudeLayer.MapDataOverlay) +
				socket.ToVector3ShiftedWithAltitude(AltitudeLayer.MapDataOverlay)) * 0.5f;
			LinkDirections links = port.x != socket.x
				? LinkDirections.Left | LinkDirections.Right
				: LinkDirections.Up | LinkDirections.Down;
			Material material = MaterialAtlasPool.SubMaterialFromAtlas(subGraphic.MatSingleFor(distillery), links);
			Printer_Plane.PrintPlane(layer, center, Vector2.one, material, 0f);
		}
    }
}
