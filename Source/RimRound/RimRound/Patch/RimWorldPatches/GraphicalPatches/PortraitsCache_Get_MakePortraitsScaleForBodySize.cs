using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(PortraitsCache))]
    [HarmonyPatch(nameof(PortraitsCache.Get))]
    public class PortraitsCache_Get_MakePortraitsScaleForBodySize
    {
        public static bool Prefix(Pawn __0, ref Vector2 __1, ref Vector3 __3, ref float __4) 
        {
            //3 moves the portrait around
            //1 is the resolution?
            //4 is the zoom of everything
            var prepared = Presentation.PawnPresentationOwner.For(__0);
            if (ColonistPortraitViewport.Pawn == __0)
                __1 = new Vector2(ColonistPortraitViewport.Bounds.width, ColonistPortraitViewport.Bounds.height);
            if (!Presentation.PawnPresentationOwner.IsCustom(__0))
                return true;


            // Gelatinous artwork uses a body-sized mesh whose head moves far from
            // the pawn origin as the stage grows. Keep the fitted camera values
            // for every Gelatinous stage, including race-specific equivalents.
            __3.x = prepared.Stage.PortraitPanX;
            if (!GlobalSettings.useZoomPortraitStyle || prepared.Stage.ForceFittedPortrait)
            {
                __3.z = prepared.Stage.PortraitPanY;
                __4 = prepared.Stage.PortraitZoom;
                //__3.z = Values.debugPos2;
                //__4 = Values.debugPos;


                return true;
            }
            else 
            {
                __3.z = prepared.Stage.PortraitPanStyleY;
                return true;
            }
            
        }
    }

    // Native portraits use a tall canvas extending above the bar entry. Fitted
    // and rotated bodies need the same viewport as the square we draw them in.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
    public static class ColonistPortraitViewport
    {
        [ThreadStatic] public static Pawn Pawn;
        [ThreadStatic] public static Rect Bounds;
        public struct Scope { public Pawn Pawn; public Rect Bounds; }
        public static bool Applies(Pawn pawn) => pawn?.RaceProps?.Humanlike == true &&
            pawn.TryGetComp<Comps.PawnBodyType_ThingComp>() != null;
        public static void Prefix(Rect rect, Pawn colonist, out Scope __state)
        {
            __state = new Scope { Pawn = Pawn, Bounds = Bounds };
            Pawn = Applies(colonist) ? colonist : null;
            Bounds = rect.ContractedBy(1f);
        }
        public static void Finalizer(Scope __state) { Pawn = __state.Pawn; Bounds = __state.Bounds; }
        [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.GetPawnTextureRect))]
        public static class TextureRect
        {
            public static void Postfix(ref Rect __result) { __result = FitTextureRect(__result); }
        }
        public static Rect FitTextureRect(Rect nativeRect) => Pawn != null ? Bounds : nativeRect;
    }

    // CM replaces DrawColonist and uses its own cached portrait rectangle.
    // Fit that draw site too, without changing its mood, weapon or status UI.
    [HarmonyPatch]
    public static class ColorCodedMoodBarPortraitViewport
    {
        static Type Drawer => AccessTools.TypeByName("ColoredMoodBar13.MoodPatch");
        static bool Prepare() => Drawer != null;
        static MethodBase TargetMethod() => AccessTools.Method(Drawer, "DrawColonist");
        public static void Prefix(Rect rect, Pawn colonist, out ColonistPortraitViewport.Scope __state)
            => ColonistPortraitViewport.Prefix(rect, colonist, out __state);
        public static void Finalizer(ColonistPortraitViewport.Scope __state) => ColonistPortraitViewport.Finalizer(__state);
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var textureRect = AccessTools.Field(AccessTools.TypeByName("ColoredMoodBar13.MoodCache"), "PawnTexturePosition");
            var fitRect = AccessTools.Method(typeof(ColonistPortraitViewport), nameof(ColonistPortraitViewport.FitTextureRect));
            int matches = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (!instruction.LoadsField(textureRect)) continue;
                yield return new CodeInstruction(OpCodes.Call, fitRect);
                matches++;
            }
            if (matches != 1) throw new InvalidOperationException("RimRound could not identify CM's portrait draw rectangle.");
        }
    }

    [HarmonyPatch(typeof(PawnCacheRenderer), nameof(PawnCacheRenderer.RenderPawn))]
    public static class PreparedPortraitPosePatch
    {
        [ThreadStatic] private static Pawn portraitPawn;
        [ThreadStatic] private static float anchorScale;
        private static readonly Func<PawnRenderer, PawnRenderFlags> NativeFlags =
            AccessTools.MethodDelegate<Func<PawnRenderer, PawnRenderFlags>>(AccessTools.PropertyGetter(typeof(PawnRenderer), "DefaultRenderFlagsNow"));
        public struct Scope { public Pawn Pawn; public float Scale; }
        public static float AnchorScaleFor(Pawn pawn) => portraitPawn == pawn ? anchorScale : 1f;
        public static void Finalizer(Scope __state) { portraitPawn = __state.Pawn; anchorScale = __state.Scale; }
        // Pose changes affect only the portrait draw request. Health, mobility and
        // the world camera remain native. Other downed poses use their actual angle.
        public static void Prefix(Pawn pawn, RenderTexture renderTexture, ref Vector3 cameraOffset, ref float cameraZoom,
            ref float angle, ref Rot4 rotation, bool portrait, ref Vector3 positionOffset, out Scope __state)
        {
            __state = new Scope { Pawn = portraitPawn, Scale = anchorScale };
            if (!portrait || !ColonistPortraitViewport.Applies(pawn)) return;
            // The first colonist-bar render can precede HAR mesh preparation.
            // Frame the same prepared mesh that RenderCache will actually use.
            pawn.Drawer.renderer.renderTree.EnsureInitialized(NativeFlags(pawn.Drawer.renderer));
            var prepared = Presentation.PawnPresentationOwner.For(pawn);
            portraitPawn = pawn;
            bool custom = Presentation.PawnPresentationOwner.IsCustom(pawn);
            anchorScale = custom ? Mathf.Min(1f, 2.5f / prepared.Stage.PortraitScale) : 1f;
            bool fixedDowned = Presentation.PawnPresentationOwner.UsesStaticDownedPose(pawn);
            if (fixedDowned)
            {
                angle = 0f;
                rotation = Rot4.South;
                positionOffset = Vector3.zero;
            }
            // Portraits show an identifiable face and upper body even when the
            // complete world sprite is many cells wide.
            if (angle == 0f && !fixedDowned && !(custom && prepared.Stage.ForceFittedPortrait) && anchorScale == 1f) return;
            Vector3 origin = Find.PawnCacheCamera.transform.position;
            var alien = pawn.TryGetComp<AlienRace.AlienPartGenerator.AlienComp>();
            Vector2 headSize = alien?.customPortraitHeadDrawSize ?? Vector2.one;
            Vector3 head = pawn.Drawer.renderer.BaseHeadOffsetAt(rotation);
            float headScale = Mathf.Max(.1f, headSize.x, headSize.y);
            // Head meshes contain generous transparent padding. Using their
            // full rectangle to fit the camera made faces unnecessarily tiny.
            Vector3 focus = new Vector3(head.x, 0f, head.z - .3f * headScale).RotatedBy(angle) + positionOffset;
            cameraOffset.x = focus.x - origin.x;
            cameraOffset.z = focus.z - origin.z;
            cameraZoom = 1.28205f / headScale;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.BaseHeadOffsetAt))]
    public static class PortraitHeadAnchorScale
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn ___pawn, ref Vector3 __result)
        {
            float scale = PreparedPortraitPosePatch.AnchorScaleFor(___pawn);
            __result.x *= scale; __result.z *= scale;
        }
    }
}
