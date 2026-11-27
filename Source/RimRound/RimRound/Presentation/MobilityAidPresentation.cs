using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Presentation
{
    public sealed class PreparedMobilityAidPresentation
    {
        public readonly Apparel Aid;
        public readonly string Appearance, ProfileId, StageKey, SourceRoot, RequestedSouthFallbackRoot, SouthFallbackRoot, FallbackReason;
        public readonly float MapScale, PortraitScale;
        public readonly Graphic Graphic;
        internal PreparedMobilityAidPresentation(Apparel aid, ResolvedStageEntry stage, string root, Graphic graphic)
        {
            Aid = aid; Appearance = aid.def.defName; ProfileId = stage.ProfileId; StageKey = stage.StageKey;
            SourceRoot = root; RequestedSouthFallbackRoot = Graphic_MobilityAidSouthFallback.FallbackRoot(root);
            SouthFallbackRoot = (graphic as Graphic_MobilityAidSouthFallback)?.SelectedSouthRoot;
            FallbackReason = (graphic as Graphic_MobilityAidSouthFallback)?.SelectionReason;
            MapScale = stage.MapScale; PortraitScale = stage.PortraitScale; Graphic = graphic;
        }
        internal static PreparedMobilityAidPresentation Prepare(Apparel aid, ResolvedStageEntry stage,
            string root, Shader shader, bool statue)
        {
            if (aid.Wearer == null || (aid.def.defName != "RR_HoverChair" && aid.def.defName != "RR_HoverChairArm")) return null;
            string fallback = Graphic_MobilityAidSouthFallback.FallbackRoot(root);
            Type type = fallback == null ? typeof(Graphic_Multi) : typeof(Graphic_MobilityAidSouthFallback);
            Graphic graphic = GraphicDatabase.Get(type, root, statue ? ShaderDatabase.Cutout : shader,
                aid.def.graphicData.drawSize, aid.DrawColor, Color.white);
            return new PreparedMobilityAidPresentation(aid, stage, root, graphic);
        }

        internal static bool IsMobilityAid(Apparel aid) =>
            aid.def.defName == "RR_HoverChair" || aid.def.defName == "RR_HoverChairArm";

        internal static string ResolveRoot(Apparel aid, string suffix)
        {
            string requested = aid.WornGraphicPath + "_" + suffix;
            if (HasDirections(requested)) return requested;
            // The original chair set has female-sized frames but no male or
            // several alternate-stage variants. Reuse the corresponding frame,
            // retaining the wearer's own body and palette. A chair must never
            // fall through to the generic shirt used for missing clothing art.
            string standard = Regex.Replace(suffix, @"^([MF]_\d+)[a-z](_)", "$1$2");
            foreach (string candidate in new[] {
                standard, Regex.Replace(standard, "^M_", "F_"),
                standard == "Male" ? "Female" : standard })
            {
                string root = aid.WornGraphicPath + "_" + candidate;
                if (HasDirections(root)) return root;
            }
            return null;
        }

        static bool HasDirections(string root) =>
            !ContentFinder<Texture2D>.Get(root + "_south", false).NullOrBad() &&
            !ContentFinder<Texture2D>.Get(root + "_north", false).NullOrBad() &&
            !ContentFinder<Texture2D>.Get(root + "_east", false).NullOrBad();
    }

    // A prepared material substitution in the accepted apparel Graphic consumer.
    // This trial does not supply a seated body pose or claim geometry acceptance.
    public sealed class Graphic_MobilityAidSouthFallback : Graphic_Multi
    {
        private Graphic south;
        private List<ShaderParameter> parameters;
        public string SelectedSouthRoot { get; private set; }
        public string SelectionReason { get; private set; }
        private static readonly Dictionary<Texture2D, string> SouthAlpha = new Dictionary<Texture2D, string>();
        public override Material MatSouth => south?.MatSouth ?? base.MatSouth;
        internal static string FallbackRoot(string path)
        {
            const string prefix = "Things/Pawn/Humanlike/Apparel/Hoverchair/";
            if (path == prefix + "hoverchair_F_100_Gelatinous") return prefix + "hoverchair_F_090_Titanic";
            if (path == prefix + "hoverchairArm_F_100_Gelatinous") return prefix + "hoverchairArm_F_090_Titanic";
            return null;
        }
        public override void Init(GraphicRequest request)
        {
            parameters = request.shaderParameters;
            base.Init(request);
            string fallback = FallbackRoot(request.path);
            // Honour visible South art supplied by an actual texture provider.
            // Only the two known roots are inspected, once per loaded texture,
            // during game-thread preparation; render getters only read a material.
            SelectionReason = fallback == null ? "no-approved-fallback-root" : InspectSouth(base.MatSouth.mainTexture as Texture2D);
            if (SelectionReason != "blank-source-south") return;
            if (fallback != null && !ContentFinder<Texture2D>.Get(fallback + "_south", false).NullOrBad() &&
                !ContentFinder<Texture2D>.Get(fallback + "_north", false).NullOrBad() &&
                !ContentFinder<Texture2D>.Get(fallback + "_east", false).NullOrBad())
            {
                south = GraphicDatabase.Get(typeof(Graphic_Multi), fallback, request.shader, request.drawSize,
                    request.color, request.colorTwo, request.graphicData, request.shaderParameters, request.maskPath);
                SelectedSouthRoot = fallback;
                SelectionReason = null;
            }
            else SelectionReason = "missing-approved-south-fallback";
        }
        private static string InspectSouth(Texture2D texture)
        {
            if (texture == null) return "missing-source-south-texture";
            string result;
            if (SouthAlpha.TryGetValue(texture, out result)) return result;
            RenderTexture old = RenderTexture.active, target = null;
            Texture2D readable = null;
            try
            {
                Color32[] pixels;
                if (texture.isReadable) pixels = texture.GetPixels32();
                else
                {
                    target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(texture, target); RenderTexture.active = target;
                    readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                    readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); readable.Apply(); pixels = readable.GetPixels32();
                }
                result = "blank-source-south";
                for (int i = 0; i < pixels.Length; i++) if (pixels[i].a != 0) { result = "visible-source-south-retained"; break; }
            }
            catch (Exception error) { result = "source-south-alpha-unverified-" + error.GetType().Name; }
            finally
            {
                RenderTexture.active = old;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
            SouthAlpha[texture] = result;
            return result;
        }
        public override Graphic GetColoredVersion(Shader shader, Color first, Color second) =>
            GraphicDatabase.Get(typeof(Graphic_MobilityAidSouthFallback), path, shader, drawSize,
                first, second, data, parameters, maskPath);
    }
}
