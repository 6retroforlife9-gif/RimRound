using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Presentation.Anthrosonae
{
    // This carrier uses already-prepared texture/material references only.
    // It does not put generated paths in ContentFinder or mutate native art.
    public sealed class Graphic_PreparedAnthrosonaeCoat : Graphic
    {
        private readonly Texture2D[] bodies, masks;
        private readonly Material[] materials;
        private readonly AnthrosonaeCoatMaterials.RasterSet rasterOwner;
        private readonly AnthrosonaeMaterialSet ownedMaterials;
        public readonly string ProjectionId;
        internal Graphic_PreparedAnthrosonaeCoat(string root, AnthrosonaeCoatMaterials.RasterSet rasters,
            Shader shader, Color primary, Color secondary)
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Coat materials require the game thread.");
            path = root; ProjectionId = rasters.Projection; color = primary; colorTwo = secondary; drawSize = Vector2.one;
            rasterOwner = rasters; bodies = rasters.Bodies; masks = rasters.Masks; materials = new Material[3];
            ownedMaterials = new AnthrosonaeMaterialSet(rasters.Resources);
            for (int i = 0; i < 3; i++)
                materials[i] = AnthrosonaeRenderResources.Create(bodies[i], shader.SupportsMaskTex() ? masks[i] : null,
                    shader, primary, secondary, ownedMaterials);
        }
        ~Graphic_PreparedAnthrosonaeCoat() { AnthrosonaeRenderResources.RetireMaterials(ownedMaterials); }
        public override Material MatSingle => MatSouth;
        public override Material MatNorth => materials[0];
        public override Material MatEast => materials[1];
        public override Material MatSouth => materials[2];
        public override Material MatWest => materials[1];
        public override bool WestFlipped => true;
        public override bool ShouldDrawRotated => false;
        internal bool MatchesPreparedCoat(string root, AnthrosonaeCoatMaterials.RasterSet rasters,
            Shader shader, Color primary, Color secondary) =>
            path == root && ReferenceEquals(rasterOwner, rasters) && drawSize.Equals(Vector2.one) &&
            ReferenceEquals(MatSouth.shader, shader) && color.Equals(primary) && colorTwo.Equals(secondary);
        public override Graphic GetColoredVersion(Shader shader, Color primary, Color secondary) =>
            shader == MatSouth.shader && ((Color32)primary).Equals((Color32)color) && ((Color32)secondary).Equals((Color32)colorTwo) ? this :
                new Graphic_PreparedAnthrosonaeCoat(path, rasterOwner, shader, primary, secondary) { drawSize = drawSize };
        public override Graphic GetCopy(Vector2 newDrawSize, Shader overrideShader)
        {
            Shader shader = overrideShader ?? MatSouth.shader;
            if (newDrawSize == drawSize && shader == MatSouth.shader) return this;
            return new Graphic_PreparedAnthrosonaeCoat(path, rasterOwner, shader, color, colorTwo) { drawSize = newDrawSize };
        }
        public override void TryInsertIntoAtlas(TextureAtlasGroup groupKey)
        {
            // The static atlas is keyed by main texture alone, so the shared
            // RR main would collide across coat masks. PawnRenderer's dynamic
            // pawn atlas and portraits still bake these exact materials through
            // RenderCache/DrawNow; neither requires static insertion here.
        }
        public override void Print(SectionLayer layer, Thing thing, float extraRotation)
        {
            // Section meshes retain only a raw Material. This pawn-render-node
            // carrier is owned by Graphics, so it cannot lend one indefinitely
            // to a static mesh without a separate lifetime protocol.
            throw new NotSupportedException("Prepared Anthrosonae pawn graphics cannot be printed into static section meshes.");
        }
    }

    public static class AnthrosonaeCoatMaterials
    {
        public const string PandaDiagnosticPolicy = "anthrosonae.panda.female-reference.diagnostic.v2";
        public const string NativeProjectionPolicy = "anthrosonae.native-coat.projection.v1";
        public const string NativeMarkedProjectionPolicy = "anthrosonae.native-coat.marked-projection.v1";
        internal sealed class RasterSet
        {
            internal Texture2D[] Bodies, Masks;
            internal string Projection;
            internal AnthrosonaeRasterResources Resources;
            ~RasterSet()
            {
                AnthrosonaeRenderResources.RetireRasters(Resources);
            }
        }
        // Palette-independent rasters are shared by all three pawn snapshots,
        // fresh/rotten graphics and later pawns. Weak entries do not keep a
        // retired game's masks alive. Managed Graphics retain the set; native
        // draw requests retain nodes and their Graphics. Deferred disposal also
        // waits for every owned material and invisible clone in a retired set.
        private static readonly Dictionary<string, System.WeakReference<RasterSet>> Rasters = new Dictionary<string, System.WeakReference<RasterSet>>();
        public static int PreparedRasterSets => Rasters.Values.Count(x => x.TryGetTarget(out var _));
        public static long GeneratedRasterBytes { get; private set; }
        public static int GeneratedRasterCount { get; private set; }
        private static readonly string[] Directions = { "north", "east", "south" };

        internal static bool TryPrepare(Pawn pawn, AnthrosonaeCapture capture, AppearanceCoatMapping mapping,
            out Graphic fresh, out Graphic rotten, out string reason)
        {
            fresh = rotten = null; reason = null;
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Coat preparation requires the game thread.");
            if (mapping.MaterialPolicyId != PandaDiagnosticPolicy && mapping.MaterialPolicyId != NativeProjectionPolicy &&
                mapping.MaterialPolicyId != NativeMarkedProjectionPolicy)
            { reason = "unsupported-coat-material"; return false; }
            try
            {
                AnthrosonaeBuiltInFamilies.CoatFamily family;
                AnthrosonaeBuiltInFamilies.CoatsByKey.TryGetValue(capture.Descriptor.ActiveCoat.Value, out family);
                string nativeRoot;
                // Reference anatomy belongs to the selected RR silhouette,
                // independently of the pawn's unchanged native body identity.
                if (family == null || !family.NativeBodyTextureRoots.TryGetValue(mapping.NativeReferenceBody, out nativeRoot))
                { reason = "unsupported-native-coat-body"; return false; }
                string key = nativeRoot + "|" + mapping.BodyTextureRoot + "|" + mapping.MaterialPolicyId + "|" + mapping.NeutralizeBodyValue;
                RasterSet set;
                if (!Rasters.TryGetValue(key, out var weak) || !weak.TryGetTarget(out set))
                {
                    set = PrepareRasters(nativeRoot, mapping, family);
                    foreach (var expired in Rasters.Where(x => !x.Value.TryGetTarget(out var _)).Select(x => x.Key).ToArray()) Rasters.Remove(expired);
                    Rasters[key] = new System.WeakReference<RasterSet>(set);
                }
                float factor = capture.CoatProperties.colorRGBPostFactor;
                var post = new AppearanceColor(factor, factor, factor, factor);
                Func<AppearanceColor, AppearanceColor> rot = c => Copy(PawnRenderUtility.GetRottenColor(AnthrosonaeBridge.NativeColor(c)));
                Func<AppearanceColor, AppearanceColor, bool> equal = (a, b) => AnthrosonaeBridge.NativeColor(a).IndistinguishableFrom(AnthrosonaeBridge.NativeColor(b));
                var raw = capture.Descriptor.Palette;
                // A forced gear/health refresh must rebuild the complete prepared
                // state, but an identical coat need not allocate new materials.
                // Reuse only this pawn's still-owned exact raster/palette carrier;
                // no global Graphic cache or early disposal of retained nodes.
                var previous = PawnPresentationOwner.For(pawn);
                fresh = GraphicFor(mapping.BodyTextureRoot, set, new AppearanceMaterialPalette(raw, post, false, rot, equal), capture.CoatProperties.colorType, previous?.BodyGraphic);
                rotten = GraphicFor(mapping.BodyTextureRoot, set, new AppearanceMaterialPalette(raw, post, true, rot, equal), capture.CoatProperties.colorType, previous?.RottenGraphic);
                return true;
            }
            catch (Exception error)
            {
                // A partial fresh/rotten pair is not a published presentation.
                // Release our references; shared rasters retire by their owners.
                fresh = rotten = null;
                reason = "unavailable-coat-projection";
                Log.WarningOnce("[RimRound Anthrosonae] Native coat retained: " + error.Message, (mapping.Coat.ToString() + mapping.BodyTextureRoot).GetHashCode());
                return false;
            }
        }
        private static AppearanceColor Copy(Color c) => new AppearanceColor(c.r, c.g, c.b, c.a);
        private static Graphic GraphicFor(string root, RasterSet set, AppearanceMaterialPalette palette, PawnRenderNodeProperties.AttachmentColorType colorType, Graphic previous)
        {
            bool complex = palette.UseComplexShader;
            Color first = AnthrosonaeBridge.NativeColor(complex || colorType == PawnRenderNodeProperties.AttachmentColorType.Hair ? palette.Primary : palette.Secondary);
            Shader shader = complex ? ShaderDatabase.CutoutComplex : ShaderDatabase.Cutout;
            Color second = AnthrosonaeBridge.NativeColor(palette.Secondary);
            var carrier = previous as Graphic_PreparedAnthrosonaeCoat;
            return carrier != null && carrier.MatchesPreparedCoat(root, set, shader, first, second) ? carrier :
                new Graphic_PreparedAnthrosonaeCoat(root, set, shader, first, second);
        }
        private static RasterSet PrepareRasters(string nativeRoot, AppearanceCoatMapping mapping, AnthrosonaeBuiltInFamilies.CoatFamily family)
        {
            string bodyRoot = mapping.BodyTextureRoot;
            var set = new RasterSet { Bodies = new Texture2D[3], Masks = new Texture2D[3], Projection = mapping.MaterialPolicyId };
            var owned = new List<Texture2D>();
            try
            {
                for (int i = 0; i < Directions.Length; i++)
                {
                    string direction = Directions[i];
                    var main = ContentFinder<Texture2D>.Get(nativeRoot + "_" + direction, false);
                    var mask = ContentFinder<Texture2D>.Get(nativeRoot + "_" + direction + "m", false);
                    var body = ContentFinder<Texture2D>.Get(bodyRoot + "_" + direction, false);
                    if (main.NullOrBad() || mask.NullOrBad() || body.NullOrBad())
                        throw new InvalidOperationException("Missing explicit " + direction + " main/mask/body texture.");
                    // Versioned pilot landmarks: neck cap / chest / lower torso.
                    // These are proposed anatomical fits, not accepted artwork.
                    float[] target = i == 1 ? new[] { 0f, .16f, .53f, 1f } : new[] { 0f, .18f, .49f, 1f };
                    var registration = new CoatProjectionRegistration(mapping.MaterialPolicyId + "/" + direction,
                        new[] { 0f, .20f, .48f, 1f }, target);
                    var bodyPixels = Read(body);
                    var nativePixels = Read(main);
                    var projected = CoatMaskProjection.Project(nativePixels, Read(mask), bodyPixels, registration);
                    var texture = new Texture2D(projected.Width, projected.Height, TextureFormat.RGBA32, false);
                    owned.Add(texture);
                    set.Masks[i] = texture; // Register before any upload/property call can fail.
                    texture.name = "RR Anthro mask " + nativeRoot + " / " + bodyRoot + " / " + direction;
                    texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = body.filterMode;
                    texture.LoadRawTextureData(projected.Rgba); texture.Apply(false, true);
                    set.Bodies[i] = body; set.Masks[i] = texture;
                    bool marked = mapping.MaterialPolicyId == NativeMarkedProjectionPolicy;
                    if (mapping.NeutralizeBodyValue || marked)
                    {
                        var neutral = mapping.NeutralizeBodyValue ? CoatMaskProjection.NeutralValue(bodyPixels) : bodyPixels;
                        if (marked)
                        {
                            if (!family.HasPaintedDetail) throw new InvalidOperationException("Unregistered native painted detail.");
                            var reference = ContentFinder<Texture2D>.Get(family.DetailReferenceRoot(mapping.NativeReferenceBody, direction) + "_" + direction, false);
                            if (reference.NullOrBad()) throw new InvalidOperationException("Missing explicit unmarked " + direction + " reference.");
                            neutral = CoatMaskProjection.ApplyPaintedDetail(nativePixels, Read(reference), neutral, registration);
                        }
                        var valueTexture = new Texture2D(neutral.Width, neutral.Height, TextureFormat.RGBA32, false);
                        owned.Add(valueTexture);
                        valueTexture.name = "RR Anthro " + (marked ? "painted detail " + nativeRoot + " / " : "neutral value ") + bodyRoot + " / " + direction;
                        valueTexture.wrapMode = TextureWrapMode.Clamp; valueTexture.filterMode = body.filterMode;
                        valueTexture.LoadRawTextureData(neutral.Rgba); valueTexture.Apply(false, true);
                        set.Bodies[i] = valueTexture;
                    }
                }
                var sizes = owned.Select(texture => (long)texture.width * texture.height * 4).ToArray();
                foreach (long bytes in sizes) {
                    GeneratedRasterCount++; GeneratedRasterBytes += bytes;
                }
                set.Resources = new AnthrosonaeRasterResources(owned.ToArray(), sizes);
                return set;
            }
            catch
            {
                foreach (var texture in owned) if (texture != null) UnityEngine.Object.Destroy(texture);
                throw;
            }
        }
        private static CoatPixelPlane Read(Texture2D texture)
        {
            RenderTexture previous = RenderTexture.active, target = null;
            Texture2D readable = null;
            try
            {
                Color32[] pixels;
                if (texture.isReadable) pixels = texture.GetPixels32();
                else
                {
                    // RimWorld's active colour space is part of native pilot
                    // evidence. The ordinary Unity blit/readback preserves the
                    // same sampling path as loaded texture/material rendering.
                    target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(texture, target); RenderTexture.active = target;
                    readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                    readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                    readable.Apply(); pixels = readable.GetPixels32();
                }
                var bytes = new byte[pixels.Length * 4];
                for (int i = 0; i < pixels.Length; i++) { int n = i * 4; bytes[n] = pixels[i].r; bytes[n+1] = pixels[i].g; bytes[n+2] = pixels[i].b; bytes[n+3] = pixels[i].a; }
                return new CoatPixelPlane(texture.width, texture.height, bytes);
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }
    }
}
