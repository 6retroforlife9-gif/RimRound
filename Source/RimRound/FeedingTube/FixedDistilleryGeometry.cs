using RimRound.Utilities;
using UnityEngine;
using Verse;

namespace RimRound.FeedingTube
{
    public interface IPreciseNetworkFoodServing : INetworkFoodServing, IPreciseBodyFoodVolume
    {
        int SourceCreatedTick { get; }
        bool TryInitializeExactServing(double nutrition, double volume, int sourceCreatedTick);
    }

    public static class FixedDistilleryGeometry
    {
        public static readonly IntVec3 InputOffset = new IntVec3(2, 0, 1);
        public static readonly IntVec3 OutputOffset = new IntVec3(-1, 0, 1);
        public static IntVec3 Input(Building_NutrientDistillery building) =>
            building.Position + (building.LegacyPortLayout
                ? new IntVec3(-1, 0, -1).RotatedBy(building.Rotation) : InputOffset);
        public static IntVec3 Output(Building_NutrientDistillery building) =>
            building.Position + (building.LegacyPortLayout
                ? new IntVec3(2, 0, -1).RotatedBy(building.Rotation) : OutputOffset);
    }

    // Keep the existing sprite. Its South appearance matches the canonical
    // North footprint and upper-side ports, including ghosts and recoloring.
    public sealed class Graphic_FixedNutrientDistillery : Graphic_Single
    {
        public override float DrawRotatedExtraAngleOffset => 180f;
        public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo) =>
            GraphicDatabase.Get<Graphic_FixedNutrientDistillery>(path, newShader, drawSize, newColor, newColorTwo, data);
    }
}
