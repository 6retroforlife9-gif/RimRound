using System;

namespace RimRound.Presentation.Anthrosonae
{
    // Plain copied pixels only. Unity resource loading/publication belongs to
    // the main-thread material owner. A projection is an authored registration,
    // not evidence that its anatomical placement has passed visual review.
    public sealed class CoatPixelPlane
    {
        public readonly int Width, Height;
        public readonly byte[] Rgba;
        public CoatPixelPlane(int width, int height, byte[] rgba)
        {
            if (width < 2 || height < 2 || width > 4096 || height > 4096 ||
                rgba == null || rgba.Length != checked(width * height * 4))
                throw new ArgumentException("Invalid coat pixel plane.");
            Width = width; Height = height; Rgba = rgba;
        }
    }

    public sealed class CoatProjectionRegistration
    {
        public readonly string Id;
        private readonly float[] sourceY, targetY;
        public CoatProjectionRegistration(string id, float[] sourceTopDownY, float[] targetTopDownY)
        {
            if (string.IsNullOrWhiteSpace(id) || sourceTopDownY == null || targetTopDownY == null ||
                sourceTopDownY.Length != targetTopDownY.Length || sourceTopDownY.Length < 2)
                throw new ArgumentException("A versioned pair of anatomical landmark sequences is required.");
            Id = id; sourceY = (float[])sourceTopDownY.Clone(); targetY = (float[])targetTopDownY.Clone();
            Validate(sourceY); Validate(targetY);
        }
        private static void Validate(float[] knots)
        {
            if (knots[0] != 0f || knots[knots.Length - 1] != 1f) throw new ArgumentException("Landmarks must cover the whole torso.");
            for (int i = 1; i < knots.Length; i++)
                if (float.IsNaN(knots[i]) || float.IsInfinity(knots[i]) || knots[i] <= knots[i - 1])
                    throw new ArgumentException("Landmarks must be finite and strictly increasing.");
        }
        internal float SourceY(float target)
        {
            int i = 1;
            while (i < targetY.Length - 1 && target > targetY[i]) i++;
            return sourceY[i - 1] + (sourceY[i] - sourceY[i - 1]) *
                (target - targetY[i - 1]) / (targetY[i] - targetY[i - 1]);
        }
    }

    public static class CoatMaskProjection
    {
        // These source pairs share the same authored lighting/anatomy. Dividing
        // out that reference retains the painted dark markings, without laying
        // a second torso shadow or native outline over the RR body. Registration
        // of an unmarked reference is explicit; it is never inferred at runtime.
        public static CoatPixelPlane PaintedDetail(CoatPixelPlane nativeBody, CoatPixelPlane unmarkedReference)
        {
            if (nativeBody == null || unmarkedReference == null) throw new ArgumentNullException();
            if (nativeBody.Width != unmarkedReference.Width || nativeBody.Height != unmarkedReference.Height)
                throw new ArgumentException("Painted detail requires matching authored reference dimensions.");
            var detail = new byte[nativeBody.Rgba.Length];
            for (int i = 0; i < detail.Length; i += 4)
            {
                int value = Math.Max(nativeBody.Rgba[i], Math.Max(nativeBody.Rgba[i + 1], nativeBody.Rgba[i + 2]));
                int basis = Math.Max(unmarkedReference.Rgba[i], Math.Max(unmarkedReference.Rgba[i + 1], unmarkedReference.Rgba[i + 2]));
                // Transparent/outline pixels and small export-rounding changes
                // do not carry coat detail. Do not amplify or brighten RR art.
                byte factor = nativeBody.Rgba[i + 3] >= 128 && unmarkedReference.Rgba[i + 3] >= 128 &&
                    value >= 48 && basis >= 48 && basis - value > 2 ? (byte)Math.Round(255d * value / basis) : (byte)255;
                detail[i] = detail[i + 1] = detail[i + 2] = factor; detail[i + 3] = 255;
            }
            return new CoatPixelPlane(nativeBody.Width, nativeBody.Height, detail);
        }

        public static CoatPixelPlane ApplyPaintedDetail(CoatPixelPlane nativeBody, CoatPixelPlane unmarkedReference,
            CoatPixelPlane rrBody, CoatProjectionRegistration registration)
        {
            if (rrBody == null) throw new ArgumentNullException(nameof(rrBody));
            var projected = Project(nativeBody, PaintedDetail(nativeBody, unmarkedReference), rrBody, registration);
            var pixels = (byte[])rrBody.Rgba.Clone();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] == 0) continue;
                for (int c = 0; c < 3; c++) pixels[i + c] = (byte)Math.Round(pixels[i + c] * projected.Rgba[i] / 255d);
            }
            return new CoatPixelPlane(rrBody.Width, rrBody.Height, pixels);
        }

        // Colourable value plane for the exact New/Old RR source artwork when
        // no authored BW counterpart exists. Keep its alpha, outlines, folds
        // and per-pixel value; remove only chroma before applying the coat.
        // This is a runtime art treatment requiring its own visual acceptance.
        public static CoatPixelPlane NeutralValue(CoatPixelPlane source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var pixels = (byte[])source.Rgba.Clone();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte value = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
                pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            }
            return new CoatPixelPlane(source.Width, source.Height, pixels);
        }
        private sealed class Rows
        {
            internal readonly int[] Left, Right;
            internal readonly int Width, Height;
            internal int Bottom = int.MaxValue, Top = -1;
            internal Rows(CoatPixelPlane image, byte threshold)
            {
                Width = image.Width; Height = image.Height;
                Left = new int[image.Height]; Right = new int[image.Height];
                for (int y = 0; y < image.Height; y++)
                {
                    Left[y] = image.Width; Right[y] = -1;
                    for (int x = 0; x < image.Width; x++)
                        if (image.Rgba[(y * image.Width + x) * 4 + 3] >= threshold)
                        { Left[y] = Math.Min(Left[y], x); Right[y] = x; }
                    if (Right[y] >= Left[y]) { Bottom = Math.Min(Bottom, y); Top = y; }
                }
                if (Top <= Bottom) throw new ArgumentException("Empty or degenerate coat silhouette.");
            }
        }

        // All planes use bottom-up pixel rows, matching Texture2D.GetPixels32.
        // Native body alpha establishes the anatomical sampling domain. Native
        // CutoutComplex reads mask RGB weights, not mask alpha; several audited
        // Thin masks have different bounds from their main texture. Preserve
        // those RGB values, including black/uncoloured regions, verbatim.
        // Original RR alpha/shading remain exclusively in the main texture.
        public static CoatPixelPlane Project(CoatPixelPlane nativeBody, CoatPixelPlane nativeMask,
            CoatPixelPlane rrBody, CoatProjectionRegistration registration)
        {
            if (nativeBody == null || nativeMask == null || rrBody == null || registration == null)
                throw new ArgumentNullException();
            var bodyRows = new Rows(nativeBody, 128);
            var targetRows = new Rows(rrBody, 1);
            var output = new byte[rrBody.Rgba.Length];
            for (int y = targetRows.Bottom; y <= targetRows.Top; y++)
            {
                // Separate shaded sections are legitimate RR geometry. Empty
                // target rows stay empty; they do not redefine torso landmarks.
                if (targetRows.Right[y] < targetRows.Left[y]) continue;
                float down = (targetRows.Top - y) / (float)(targetRows.Top - targetRows.Bottom);
                float sy = bodyRows.Top - registration.SourceY(down) * (bodyRows.Top - bodyRows.Bottom);
                int y0 = Math.Max(bodyRows.Bottom, Math.Min(bodyRows.Top, (int)Math.Floor(sy)));
                int y1 = Math.Min(bodyRows.Top, y0 + 1);
                float fy = Math.Max(0f, Math.Min(1f, sy - y0));
                for (int x = targetRows.Left[y]; x <= targetRows.Right[y]; x++)
                {
                    int dest = (y * rrBody.Width + x) * 4;
                    if (rrBody.Rgba[dest + 3] == 0) continue;
                    float across = targetRows.Right[y] == targetRows.Left[y] ? .5f :
                        (x - targetRows.Left[y]) / (float)(targetRows.Right[y] - targetRows.Left[y]);
                    for (int channel = 0; channel < 3; channel++)
                    {
                        float a = RowSample(nativeMask, bodyRows, y0, across, channel);
                        float b = RowSample(nativeMask, bodyRows, y1, across, channel);
                        output[dest + channel] = (byte)Math.Round(a + (b - a) * fy);
                    }
                    output[dest + 3] = 255;
                }
            }
            return new CoatPixelPlane(rrBody.Width, rrBody.Height, output);
        }

        private static float RowSample(CoatPixelPlane image, Rows rows, int y, float across, int channel)
        {
            if (rows.Right[y] < rows.Left[y])
            {
                int lower = y - 1, upper = y + 1;
                while (lower >= rows.Bottom && rows.Right[lower] < rows.Left[lower]) lower--;
                while (upper <= rows.Top && rows.Right[upper] < rows.Left[upper]) upper++;
                float below = RowSample(image, rows, lower, across, channel);
                float above = RowSample(image, rows, upper, across, channel);
                return below + (above - below) * (y - lower) / (upper - lower);
            }
            float bodyX = rows.Left[y] + across * (rows.Right[y] - rows.Left[y]);
            // Native shaders sample both textures at the same UV, including
            // the audited rabbit's 512px main and 256px mask. Convert texel
            // centres rather than assuming equal source resolutions.
            float x = Math.Max(0f, Math.Min(image.Width - 1f, (bodyX + .5f) * image.Width / rows.Width - .5f));
            float maskY = Math.Max(0f, Math.Min(image.Height - 1f, (y + .5f) * image.Height / rows.Height - .5f));
            int x0 = (int)Math.Floor(x), x1 = Math.Min(image.Width - 1, x0 + 1);
            int y0 = (int)Math.Floor(maskY), y1 = Math.Min(image.Height - 1, y0 + 1);
            float low = image.Rgba[(y0 * image.Width + x0) * 4 + channel] +
                (image.Rgba[(y0 * image.Width + x1) * 4 + channel] - image.Rgba[(y0 * image.Width + x0) * 4 + channel]) * (x - x0);
            float high = image.Rgba[(y1 * image.Width + x0) * 4 + channel] +
                (image.Rgba[(y1 * image.Width + x1) * 4 + channel] - image.Rgba[(y1 * image.Width + x0) * 4 + channel]) * (x - x0);
            return low + (high - low) * (maskY - y0);
        }
    }
}
