using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Presentation.Anthrosonae
{
    // Main-thread capture only. Expected values come from the audited 1.6 XML
    // and constructors, never from the pawn's possibly modified live GeneDefs.
    internal static class AnthrosonaeNodeSchema
    {
        private static FieldInfo[] propertiesFields, drawFields, rotationFields;
        private static bool fieldsValidated, initialized;
        internal static AppearanceNodeStructure ExpectedFur, ExpectedTail;
        private static readonly string[] PropertyNames = {
            "workerClass", "subworkerClasses", "nodeClass", "tagDef", "parentTagDef", "children", "texPath", "texPaths",
            "texPathFemale", "texPathsFemale", "bodyTypeGraphicPaths", "texSeed", "useGraphic", "skipFlag", "rotDrawMode", "pawnType",
            "linkedBodyPartsGroup", "color", "colorRGBPostFactor", "useRottenColor", "useSkinShader", "shaderTypeDef", "colorType",
            "baseLayer", "drawData", "oppositeFacingLayerWhenFlipped", "overrideMeshSize", "drawSize", "anchorTag", "side",
            "narrowCrownHorizontalOffset", "visibleFacing", "flipGraphic", "rotateIndependently", "debugLabel", "overlayLayer", "overlayOverApparel"
        };
        internal static void Initialize()
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Anthrosonae schema requires game thread.");
            if (initialized) return;
            propertiesFields = typeof(PawnRenderNodeProperties).GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
            if (!propertiesFields.Select(x => x.Name).SequenceEqual(PropertyNames.OrderBy(x => x, StringComparer.Ordinal)))
                throw new InvalidOperationException("Unknown PawnRenderNodeProperties schema.");
            drawFields = typeof(DrawData).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
            var expectedDraw = new[] { "defaultData", "dataNorth", "dataEast", "dataSouth", "dataWest", "scaleOffsetByBodySize", "useBodyPartAnchor", "scale", "childScale", "bodyTypeScales" };
            if (!drawFields.Select(x => x.Name).SequenceEqual(expectedDraw.OrderBy(x => x, StringComparer.Ordinal)))
                throw new InvalidOperationException("Unknown DrawData schema.");
            rotationFields = typeof(DrawData.RotationalData).GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
            var expectedRot = new[] { "rotation", "pivot", "offset", "rotationOffset", "flip", "layer" };
            if (!rotationFields.Select(x => x.Name).SequenceEqual(expectedRot.OrderBy(x => x, StringComparer.Ordinal)))
                throw new InvalidOperationException("Unknown rotational draw-data schema.");
            fieldsValidated = true;
            ExpectedFur = BuildExpectedFur();
            ExpectedTail = BuildExpectedAttachment("Things/Pawn/Humanlike/Anthrosonae/BodyAttachments/Tails/AnthrobearTail",
                new Vector2(1.2f, 1.2f), new DrawData.RotationalData { layer = -2f },
                new DrawData.RotationalData { offset = new Vector3(0f, 0f, -.42f), layer = 90f },
                new DrawData.RotationalData { offset = new Vector3(-.65f, 0f, -.29f) },
                new DrawData.RotationalData { offset = new Vector3(-.1f, 0f, -.18f) },
                new DrawData.RotationalData { offset = new Vector3(.65f, 0f, -.29f) });
            initialized = true;
        }
        private static void RequireValidatedFields()
        {
            if (!UnityData.IsInMainThread || !fieldsValidated)
                throw new InvalidOperationException("Expected node construction requires initialized schema on the game thread.");
        }
        internal static AppearanceNodeStructure BuildExpectedFur()
        {
            RequireValidatedFields();
            var fur = AuditedPropertyDefaults();
            fur.nodeClass = typeof(PawnRenderNode_Fur); fur.workerClass = typeof(PawnRenderNodeWorker_Fur);
            fur.shaderTypeDef = ShaderTypeDefOf.CutoutComplex; fur.baseLayer = 5f;
            fur.rotDrawMode = RotDrawMode.Fresh | RotDrawMode.Rotting;
            fur.colorType = PawnRenderNodeProperties.AttachmentColorType.Skin; fur.parentTagDef = PawnRenderNodeTagDefOf.Body;
            return Capture(fur, true);
        }
        // Inputs are static audited records, never fields copied from live defs.
        // Nullable directional records retain raw absence/inheritance semantics.
        internal static AppearanceNodeStructure BuildExpectedAttachment(string texturePath, Vector2 meshSize,
            DrawData.RotationalData defaultData, DrawData.RotationalData? north, DrawData.RotationalData? east,
            DrawData.RotationalData? south, DrawData.RotationalData? west, IEnumerable<Rot4> visibleFacings = null,
            bool scaleOffsetByBodySize = true, bool useBodyPartAnchor = false, float scale = 1f, float childScale = 1f)
        {
            RequireValidatedFields();
            if (string.IsNullOrWhiteSpace(texturePath)) throw new ArgumentException("Audited attachment texture path required.", nameof(texturePath));
            var draw = new DrawData { scaleOffsetByBodySize = scaleOffsetByBodySize,
                useBodyPartAnchor = useBodyPartAnchor, scale = scale, childScale = childScale };
            SetDraw(draw, "defaultData", defaultData); SetDraw(draw, "dataNorth", north);
            SetDraw(draw, "dataEast", east); SetDraw(draw, "dataSouth", south); SetDraw(draw, "dataWest", west);
            SetDraw(draw, "bodyTypeScales", null);
            var attachment = AuditedPropertyDefaults();
            attachment.workerClass = typeof(PawnRenderNodeWorker_AttachmentBody); attachment.texPath = texturePath;
            attachment.shaderTypeDef = ShaderTypeDefOf.CutoutComplex; attachment.colorType = PawnRenderNodeProperties.AttachmentColorType.Hair;
            attachment.overrideMeshSize = meshSize; attachment.parentTagDef = PawnRenderNodeTagDefOf.Body;
            attachment.rotDrawMode = RotDrawMode.Fresh | RotDrawMode.Rotting; attachment.drawData = draw;
            attachment.visibleFacing = visibleFacings == null ? null : visibleFacings.ToList();
            return Capture(attachment, true);
        }
        // Pin every public default from the audited native constructor, rather
        // than implicitly accepting a changed constructor with the same fields.
        private static PawnRenderNodeProperties AuditedPropertyDefaults() => new PawnRenderNodeProperties {
            workerClass = typeof(PawnRenderNodeWorker), subworkerClasses = null, nodeClass = typeof(PawnRenderNode),
            tagDef = null, parentTagDef = null, children = null, texPath = null, texPaths = null,
            texPathFemale = null, texPathsFemale = null, bodyTypeGraphicPaths = null, texSeed = 0, useGraphic = true,
            skipFlag = null, rotDrawMode = RotDrawMode.Fresh | RotDrawMode.Rotting | RotDrawMode.Dessicated,
            pawnType = PawnRenderNodeProperties.RenderNodePawnType.HumanlikeOnly, linkedBodyPartsGroup = null,
            color = null, colorRGBPostFactor = 1f, useRottenColor = false, useSkinShader = false, shaderTypeDef = null,
            colorType = PawnRenderNodeProperties.AttachmentColorType.Custom, baseLayer = 0f, drawData = null,
            oppositeFacingLayerWhenFlipped = false, overrideMeshSize = null, drawSize = Vector2.one, anchorTag = null,
            side = PawnRenderNodeProperties.Side.Center, narrowCrownHorizontalOffset = 0f, visibleFacing = null,
            flipGraphic = false, rotateIndependently = false, debugLabel = null,
            overlayLayer = default(PawnOverlayDrawer.OverlayLayer), overlayOverApparel = null
        };
        private static void SetDraw(DrawData draw, string field, object value) =>
            drawFields.Single(x => x.Name == field).SetValue(draw, value);
        internal static AppearanceNodeStructure Capture(PawnRenderNodeProperties props, bool expected = false)
        {
            if (!UnityData.IsInMainThread || props == null || props.GetType() != typeof(PawnRenderNodeProperties))
                throw new InvalidOperationException("Unknown or off-thread render-node structure.");
            var fields = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("schema", "rimworld1.6.bearpilot.v1") };
            foreach (var field in propertiesFields) fields.Add(new KeyValuePair<string, string>(field.Name, Value(field.GetValue(props), expected, field.Name)));
            return new AppearanceNodeStructure(fields);
        }
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Value(object value, bool expected, string path)
        {
            if (value == null) return "null";
            if (value is Type type) return type.FullName + "," + type.Assembly.GetName().Name;
            if (value is string text) return text;
            if (value is float number) return F(number);
            if (value is bool flag) return flag ? "true" : "false";
            if (value is int integer) return integer.ToString(CultureInfo.InvariantCulture);
            if (value is Enum) return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            if (value is Vector2 v2) return F(v2.x) + "," + F(v2.y);
            if (value is Vector3 v3) return F(v3.x) + "," + F(v3.y) + "," + F(v3.z);
            if (value is Color color) return F(color.r) + "," + F(color.g) + "," + F(color.b) + "," + F(color.a);
            if (value is Rot4 rot) return rot.AsInt.ToString(CultureInfo.InvariantCulture);
            if (value is ShaderTypeDef shader)
                return expected ? typeof(ShaderTypeDef).FullName + "/ludeon.rimworld/CutoutComplex/Map/CutoutComplex/Map/CutoutComplexUI" :
                    shader.GetType().FullName + "/" + (shader.modContentPack?.PackageIdPlayerFacing ?? "").ToLowerInvariant() + "/" + shader.defName + "/" + shader.shaderPath + "/" + shader.uiShaderPath;
            if (value is PawnRenderNodeTagDef tag)
                return expected ? typeof(PawnRenderNodeTagDef).FullName + "/ludeon.rimworld/Body" :
                    tag.GetType().FullName + "/" + (tag.modContentPack?.PackageIdPlayerFacing ?? "").ToLowerInvariant() + "/" + tag.defName;
            if (value is Def def) return def.GetType().FullName + "/" + (def.modContentPack?.PackageIdPlayerFacing ?? "").ToLowerInvariant() + "/" + def.defName;
            if (value is DrawData draw)
            {
                if (draw.GetType() != typeof(DrawData)) throw new InvalidOperationException("Unknown draw-data type.");
                return string.Join(";", drawFields.Select(x => x.Name + "=" + Value(x.GetValue(draw), expected, path + "." + x.Name)));
            }
            if (value is DrawData.RotationalData)
                return string.Join(";", rotationFields.Select(x => x.Name + "=" + Value(x.GetValue(value), expected, path + "." + x.Name)));
            if (value is IEnumerable list)
            {
                var items = new List<string>();
                foreach (var item in list) items.Add(Value(item, expected, path + "[]"));
                return "[" + string.Join("|", items) + "]";
            }
            // Live non-default bodyTypeScales/children or future native objects
            // must not be reduced to an accepting ToString() signature.
            throw new InvalidOperationException("Unregistered structural field " + path + ": " + value.GetType().FullName);
        }
    }
}
