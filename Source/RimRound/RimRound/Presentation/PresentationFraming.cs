using System;

namespace RimRound.Presentation
{
    public struct FittedPortraitFrame
    {
        public readonly float X, Y, Zoom;
        public FittedPortraitFrame(float x, float y, float zoom) { X = x; Y = y; Zoom = zoom; }
    }

    public static class PresentationFraming
    {
        // Fit the complete body/head rectangles, including transparent mesh area.
        // This intentionally leaves a small margin instead of loading/reading pixels.
        public static FittedPortraitFrame Fit(float bodyHalfX, float bodyHalfY,
            float headX, float headY, float headHalfX, float headHalfY,
            float angleDegrees, float aspect, float maximumZoom)
        {
            float minX = Math.Min(-bodyHalfX, headX - headHalfX);
            float maxX = Math.Max(bodyHalfX, headX + headHalfX);
            float minY = Math.Min(-bodyHalfY, headY - headHalfY);
            float maxY = Math.Max(bodyHalfY, headY + headHalfY);
            double angle = angleDegrees * Math.PI / 180.0;
            float sin = (float)Math.Sin(angle), cos = (float)Math.Cos(angle);
            float x = (minX + maxX) * .5f, y = (minY + maxY) * .5f;
            float halfX = (maxX - minX) * .5f, halfY = (maxY - minY) * .5f;
            float rotatedX = Math.Abs(cos) * halfX + Math.Abs(sin) * halfY;
            float rotatedY = Math.Abs(sin) * halfX + Math.Abs(cos) * halfY;
            float extent = Math.Max(rotatedY, rotatedX / Math.Max(.01f, aspect));
            return new FittedPortraitFrame(cos * x + sin * y, cos * y - sin * x,
                Math.Min(maximumZoom, 1f / Math.Max(.01f, extent * 1.04f)));
        }
    }
}
