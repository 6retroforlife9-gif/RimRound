using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using AlienRace;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Presentation
{
    // Both the map overlay and colonist bar use this native label owner. Its
    // measured rectangle can round down at fractional UI scales; names are
    // single-line labels, so never let a final glyph wrap into the world below.
    [HarmonyPatch]
    public static class SingleLinePawnNamePatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var method in typeof(GenMapUI).GetMethods())
                if (method.Name == nameof(GenMapUI.DrawPawnLabel)) yield return method;
        }
        public static void Prefix(out bool __state)
        { __state = Text.WordWrap; Text.WordWrap = false; }
        public static void Finalizer(bool __state) { Text.WordWrap = __state; }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker_Apparel_Body), nameof(PawnRenderNodeWorker_Apparel_Body.CanDrawNow))]
    public static class PortraitMobilityAidPatch
    {
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref bool __result)
        {
            var apparel = (node as PawnRenderNode_Apparel)?.apparel;
            // Keep the node in the tree across recovery and sleep. Native draw
            // parameters invalidate cached requests when posture changes; a
            // construction-time rejection would leave a recovered wearer with
            // no chair until an unrelated graphics rebuild.
            if (__result && (parms.Portrait || parms.posture.Laying()) && apparel != null &&
                (apparel.def.defName == "RR_HoverChair" || apparel.def.defName == "RR_HoverChairArm"))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(AlienRenderTreePatches), nameof(AlienRenderTreePatches.BodyGraphicForPrefix))]
    public static class PreparedBodyGraphicPatch
    {
        // __2 is HAR's ref Graphic parameter; __result is HAR's bool return.
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Pawn pawn, ref Graphic __2, ref bool __result)
        {
            if (!PawnPresentationOwner.IsCustom(pawn)) return true;
            var prepared = PawnPresentationOwner.For(pawn);
            __2 = pawn.Drawer.renderer.CurRotDrawMode == RotDrawMode.Rotting ? prepared.RottenGraphic : prepared.BodyGraphic;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ApparelGraphicRecordGetter), nameof(ApparelGraphicRecordGetter.TryGetGraphicApparel))]
    public static class PreparedApparelGraphicPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Apparel __0, bool __2, ref ApparelGraphicRecord __3, ref bool __result)
        {
            if (!PawnPresentationOwner.IsCustom(__0?.Wearer)) return true;
            Graphic graphic;
            var prepared = PawnPresentationOwner.For(__0.Wearer);
            if (!(__2 ? prepared.StatueApparelGraphics : prepared.ApparelGraphics).TryGetValue(__0, out graphic)) return true;
            __3 = graphic == null ? new ApparelGraphicRecord() : new ApparelGraphicRecord(graphic, __0);
            __result = graphic != null;
            return false;
        }
    }

    [HarmonyPatch]
    public static class PreparedBodyAnchorsPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_StoryTracker, Pawn> StoryPawn =
            AccessTools.FieldRefAccess<Pawn_StoryTracker, Pawn>("pawn");
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PawnRenderer), nameof(PawnRenderer.BaseHeadOffsetAt));
            yield return AccessTools.Method(typeof(PawnRenderer), "GetBodyPos");
            yield return AccessTools.Method(typeof(PawnRenderNodeWorker_Eye), "TryGetWoundAnchor");
            yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(PawnDrawUtility), nameof(PawnDrawUtility.FindAnchors)));
            yield return AccessTools.Method(typeof(PawnWoundDrawer), "WriteCache");
            yield return AccessTools.Method(typeof(PawnWoundDrawer), "DebugDraw");
        }
        public static BodyTypeDef VisualBody(Pawn_StoryTracker story)
        {
            var pawn = StoryPawn(story);
            return PawnPresentationOwner.IsCustom(pawn) ? PawnPresentationOwner.For(pawn).VisualBody ?? story.bodyType : story.bodyType;
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var field = AccessTools.Field(typeof(Pawn_StoryTracker), "bodyType");
            var method = AccessTools.Method(typeof(PreparedBodyAnchorsPatch), nameof(VisualBody));
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
                { instruction.opcode = OpCodes.Call; instruction.operand = method; }
                yield return instruction;
            }
        }
    }

    // Explicit offsets use the pawn image's horizontal/vertical axes (world X/Z).
    // West mirrors horizontal wound/eye offsets; bed X follows the bed's right axis.
    // All changes affect returned draw values, never the shared body/anchor Defs.
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.BaseHeadOffsetAt))]
    public static class PreparedNorthAnchorPatch
    {
        public static void Postfix(Pawn ___pawn, Rot4 rotation, ref Vector3 __result)
        {
            if (rotation != Rot4.North || !PawnPresentationOwner.IsCustom(___pawn)) return;
            var entry = PawnPresentationOwner.For(___pawn).Stage;
            if (entry.AnchorPolicy != "explicit") return;
            float scale = Mathf.Sqrt(___pawn.ageTracker.CurLifeStage.bodySizeFactor);
            __result.x += entry.NorthOffsetX * scale;
            __result.z += entry.NorthOffsetY * scale;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetBodyPos")]
    public static class PreparedBedAnchorPatch
    {
        public static void Postfix(Pawn ___pawn, PawnPosture posture, ref Vector3 __result)
        {
            if (posture == PawnPosture.Standing || !PawnPresentationOwner.IsCustom(___pawn)) return;
            var entry = PawnPresentationOwner.For(___pawn).Stage;
            var bed = ___pawn.CurrentBed();
            if (entry.AnchorPolicy == "explicit" && bed != null)
                __result += new Vector3(entry.BedOffsetX, 0f, 0f).RotatedBy(bed.Rotation.AsAngle);
        }
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker_Eye), "TryGetWoundAnchor")]
    public static class PreparedMissingEyeAnchorPatch
    {
        public static void Postfix(string anchorTag, PawnDrawParms parms,
            ref BodyTypeDef.WoundAnchor anchor, ref bool __result)
        {
            if (__result || (anchorTag != "LeftEye" && anchorTag != "RightEye") ||
                !PawnPresentationOwner.IsCustom(parms.pawn)) return;
            // Older artwork tables omit narrow-head eye variants. Keep any
            // declared art anchor; otherwise use the native head-compatible
            // anchor without changing either shared BodyTypeDef.
            var anchors = parms.pawn.story.bodyType.woundAnchors;
            if (anchors == null) return;
            for (int i = 0; i < anchors.Count; i++)
            {
                var candidate = anchors[i];
                if (candidate.tag != anchorTag || candidate.rotation != parms.facing ||
                    (parms.facing != Rot4.South &&
                     (candidate.narrowCrown == true) != parms.pawn.story.headType.narrow)) continue;
                anchor = candidate;
                __result = true;
                return;
            }
        }
    }

    [HarmonyPatch(typeof(PawnDrawUtility), nameof(PawnDrawUtility.CalcAnchorData))]
    public static class PreparedWoundAnchorPatch
    {
        public static void Postfix(Pawn pawn, BodyTypeDef.WoundAnchor anchor, Rot4 pawnRot, ref Vector3 anchorOffset)
        {
            if (!PawnPresentationOwner.IsCustom(pawn)) return;
            var entry = PawnPresentationOwner.For(pawn).Stage;
            if (entry.AnchorPolicy != "explicit") return;
            bool eye = anchor.tag == "LeftEye" || anchor.tag == "RightEye";
            float x = eye ? entry.EyeOffsetX : entry.WoundOffsetX;
            anchorOffset.x += pawnRot == Rot4.West ? -x : x;
            anchorOffset.z += eye ? entry.EyeOffsetY : entry.WoundOffsetY;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.BodyAngle))]
    public static class AssistedRitualGuestAnglePatch
    {
        public static bool Prefix(Pawn ___pawn, PawnRenderFlags flags, ref float __result)
        {
            if ((flags & (PawnRenderFlags.Portrait | PawnRenderFlags.Statue)) != 0 ||
                !(___pawn.jobs?.curDriver is Ideology.JobDriver_RitualAttendanceStay stay) ||
                stay.HeldGroundAngle < 0f || !___pawn.Spawned || !___pawn.Downed ||
                ___pawn.pather.Moving || ___pawn.Position != stay.job.targetA.Cell || ___pawn.CurrentBed() != null)
                return true;
            // Both the near render tree and the distant atlas use this world angle.
            __result = PawnPresentationOwner.UsesStaticDownedPose(___pawn) ? 0f : stay.HeldGroundAngle;
            return false;
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    public static class PreparedDownedPosePatch
    {
        public static void Prefix(Pawn ___pawn, PawnRenderFlags flags, ref float angle, ref Rot4 bodyFacing)
        {
            if (!PawnPresentationOwner.UsesStaticDownedPose(___pawn)) return;
            // Native world crawling pairs the pawn's travel direction with a body angle and head flip.
            if ((flags & PawnRenderFlags.Portrait) == 0 && ___pawn.Crawling && ___pawn.pather?.Moving == true) return;
            var bed = ___pawn.CurrentBed();
            // An occupied blob-bed guest keeps the facing selected by their
            // Sleeping Position button. Native rendering still aligns the
            // body with its real bed; portraits keep the static front pose.
            if ((flags & PawnRenderFlags.Portrait) == 0 &&
                ___pawn.GetPosture() == PawnPosture.LayingInBed &&
                bed is RimRound.Things.Building_BlobBed blob && blob.IsOperational) return;
            // Rotate body and head together toward the bed's pillow. Portraits
            // and ground-down poses keep their upright front-facing frame.
            angle = bed != null && (flags & PawnRenderFlags.Portrait) == 0
                ? bed.Rotation.Opposite.AsAngle : 0f;
            bodyFacing = Rot4.South;
        }
    }

    [HarmonyPatch]
    public static class PresentationApparelChangedPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelAdded));
            yield return AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelRemoved));
        }
        public static void Postfix(Pawn_ApparelTracker __instance)
        {
            if (Scribe.mode == LoadSaveMode.Inactive) PawnPresentationOwner.NotifyChanged(__instance.pawn);
        }
    }

    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.SetAllGraphicsDirty))]
    public static class PresentationGraphicsChangedPatch
    {
        public static void Postfix(Pawn ___pawn)
        {
            if (___pawn?.TryGetComp<Comps.PawnBodyType_ThingComp>()?.SuppressesAnthrosonaePaletteRefresh == true) return;
            if (Scribe.mode == LoadSaveMode.Inactive)
                PawnPresentationOwner.NotifyChanged(___pawn);
        }
    }

    [HarmonyPatch(typeof(PawnRenderNode_Fur), nameof(PawnRenderNode_Fur.GraphicFor))]
    public static class PreparedAnthrosonaeFurPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PawnRenderNode_Fur __instance, Pawn pawn, ref Graphic __result)
        {
            if (!PawnPresentationOwner.IsCustom(pawn) || PawnPresentationOwner.For(pawn)?.AnthrosonaePilot?.MatchesFur(__instance) != true) return true;
            __result = null;
            return false;
        }
        // VEF can construct a Graphic even from a null original. The exact
        // recognised adapted node stays suppressed after both optional owners.
        [HarmonyAfter("OskarPotocki.VEF", "AnthrosonaeMod")]
        [HarmonyPriority(int.MinValue)]
        public static void Postfix(PawnRenderNode_Fur __instance, Pawn pawn, ref Graphic __result)
        {
            if (PawnPresentationOwner.IsCustom(pawn) && PawnPresentationOwner.For(pawn)?.AnthrosonaePilot?.MatchesFur(__instance) == true) __result = null;
        }
    }

    [HarmonyPatch(typeof(PawnRenderNode), nameof(PawnRenderNode.GetTransform))]
    public static class PreparedAnthrosonaeAttachmentTransformPatch
    {
        public static void Postfix(PawnRenderNode __instance, ref PawnDrawParms parms,
            ref Vector3 offset, ref Quaternion rotation)
        {
            // One hook handles both corrections after native worker transforms.
            // Audited attachment schemas have no transform subworkers; modified
            // schemas already fall back before a pilot can be published.
            if (__instance?.gene == null) return;
            int facing = parms.facing.AsInt;
            if (facing < 0 || facing > 3) return;
            var pilot = Anthrosonae.PreparedAnthrosonaePilot.ForAttachment(__instance, parms);
            if (pilot == null) return;
            bool portrait = (parms.flags & PawnRenderFlags.Portrait) != 0;
            for (int i = 0; i < pilot.Attachments.Count; i++)
            {
                var placement = pilot.Attachments[i];
                if (!placement.Matches(__instance)) continue;
                offset += placement.DeltaFor(facing, portrait);
                if (facing == 2 && __instance.bodyPart == null &&
                    placement.SouthRearPolicy == Anthrosonae.AppearanceRearPosePolicy.NorthTurned)
                {
                    rotation *= Anthrosonae.PreparedAnthrosonaeRearPose.NorthTurn;
                    // Native GetTransform fixes scale.y to one, all rotations
                    // are about Y, and audited attachments have no children.
                    // Move the native clamped North/South altitude difference
                    // here instead of patching LayerFor for every render node.
                    float layer = __instance.Worker.LayerFor(__instance, parms);
                    offset.y += PawnRenderUtility.AltitudeForLayer(layer + placement.SouthRearLayerDelta) -
                        PawnRenderUtility.AltitudeForLayer(layer);
                }
                return;
            }
        }
    }
}
