using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Presentation.Anthrosonae
{
    public sealed class AnthrosonaeCapture : IEquatable<AnthrosonaeCapture>
    {
        public readonly AppearanceDescriptor Descriptor;
        public readonly Gene CoatGene;
        public readonly PawnRenderNodeProperties CoatProperties;
        public readonly CapturedAnthrosonaeAttachmentSet Attachments;
        internal readonly Color? CachedSkinBase, NativeSkinOverride;
        public readonly string FailureReason;
        public readonly string FailureDetail;
        public readonly float NativeBodyScale, NativeBodyWidth;
        internal AnthrosonaeCapture(AppearanceDescriptor descriptor, Gene coat, PawnRenderNodeProperties coatProps,
            CapturedAnthrosonaeAttachmentSet attachments, string reason, float nativeScale, Color? skinBase, Color? skinOverride, string detail = null, float nativeBodyWidth = 1.5f)
        { Descriptor = descriptor; CoatGene = coat; CoatProperties = coatProps; Attachments = attachments;
            FailureReason = reason; FailureDetail = detail; NativeBodyScale = nativeScale; NativeBodyWidth = nativeBodyWidth; CachedSkinBase = skinBase; NativeSkinOverride = skinOverride; }
        public bool Equals(AnthrosonaeCapture other) => other != null && ContextEquals(other) && Descriptor.Palette.Equals(other.Descriptor.Palette);
        internal bool ContextEquals(AnthrosonaeCapture other) => other != null && CoatGene == other.CoatGene && Attachments.Equals(other.Attachments) &&
            CoatProperties == other.CoatProperties && Nullable.Equals(CachedSkinBase, other.CachedSkinBase) && FailureReason == other.FailureReason && FailureDetail == other.FailureDetail &&
            NativeBodyScale == other.NativeBodyScale && NativeBodyWidth == other.NativeBodyWidth && Descriptor.ApiStatus == other.Descriptor.ApiStatus && Descriptor.ApiVersion == other.Descriptor.ApiVersion &&
            Descriptor.NativeFurDefName == other.Descriptor.NativeFurDefName && Nullable.Equals(Descriptor.ActiveCoat, other.Descriptor.ActiveCoat) && Descriptor.Layers.SequenceEqual(other.Descriptor.Layers);
        public override bool Equals(object obj) => Equals(obj as AnthrosonaeCapture);
        public override int GetHashCode() => Descriptor.GetHashCode();
        internal Color Primary => AnthrosonaeBridge.NativeColor(Descriptor.Palette.Primary);
        internal Color? Secondary => Descriptor.Palette.Secondary.HasValue ? (Color?)AnthrosonaeBridge.NativeColor(Descriptor.Palette.Secondary.Value) : null;
    }

    [StaticConstructorOnStartup]
    public static class AnthrosonaeBridge
    {
        private static bool initialized, present;
        private static OptionalPaletteBinding binding;
        private static FieldInfo allowedColors;
        private static FieldInfo cachedSkinBase;
        private static string apiVersion;
        private static AppearanceSupportCatalog catalog;
        static AnthrosonaeBridge() { LongEventHandler.ExecuteWhenFinished(Initialize); }
        private static void Initialize()
        {
            if (initialized || !UnityData.IsInMainThread) return;
            initialized = true;
            present = LoadedModManager.RunningModsListForReading.Any(x => string.Equals(x.PackageIdPlayerFacing, "ATK.Anthrosonae", StringComparison.OrdinalIgnoreCase));
            if (!present) return;
            try
            {
                var candidates = AppDomain.CurrentDomain.GetAssemblies().Where(x => x.GetName().Name == "Anthrosonae").ToArray();
                if (candidates.Length != 1) throw new InvalidOperationException("Ambiguous/missing Anthrosonae assembly.");
                var type = candidates[0].GetType("Anthrosonae.FurGene", false);
                if (!OptionalPaletteBinding.TryCreate(type, typeof(Gene), typeof(Color), out binding, out var reason)) throw new InvalidOperationException(reason);
                var extension = candidates[0].GetType("Anthrosonae.FurColors", false);
                allowedColors = extension?.GetField("allowedFurColors", BindingFlags.Public | BindingFlags.Instance);
                if (allowedColors == null) throw new InvalidOperationException("Missing FurColors API.");
                cachedSkinBase = AccessTools.Field(typeof(Pawn_StoryTracker), "skinColorBase");
                if (cachedSkinBase?.FieldType != typeof(Color?)) throw new InvalidOperationException("Unsupported native skin identity.");
                apiVersion = candidates[0].GetName().Version.ToString();
                AnthrosonaeNodeSchema.Initialize();
                AnthrosonaeBuiltInFamilies.InitializeExpectedNodes();
                catalog = AnthrosonaeContentCatalog.Create();
                var harmony = new Harmony("RRHarmony.AnthrosonaePresentation");
                harmony.Patch(binding.ApplyColorsMethod,
                    prefix: new HarmonyMethod(typeof(AnthrosonaeColorWriterPatch), nameof(AnthrosonaeColorWriterPatch.Prefix)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(AnthrosonaeColorWriterPatch), nameof(AnthrosonaeColorWriterPatch.Finalizer)) { priority = Priority.Last });
            }
            catch (Exception error)
            {
                binding = null; catalog = null;
                Log.Warning("[RimRound Anthrosonae] Optional presentation API unavailable; native appearance retained: " + error.Message);
            }
        }
        internal static Color NativeColor(AppearanceColor c) => new Color(c.R, c.G, c.B, c.A);
        private static AppearanceColor CopyColor(object value) { var c = (Color)value; return new AppearanceColor(c.r, c.g, c.b, c.a); }
        internal static AnthrosonaeCapture Capture(Pawn pawn)
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Anthrosonae capture requires game thread.");
            Initialize();
            if (!present || pawn.genes == null) return null;
            Gene coat = null;
            var genes = pawn.genes.GenesListForReading;
            foreach (var gene in genes)
                if (gene.Active && (binding?.FurGeneType.IsInstanceOfType(gene) == true ||
                    (string.Equals(gene.def.modContentPack?.PackageIdPlayerFacing, "ATK.Anthrosonae", StringComparison.OrdinalIgnoreCase) && gene.def.fur != null)))
                {
                    if (coat != null) return Failed(pawn, gene, "ambiguous-coat-layer");
                    coat = gene;
                }
            if (coat == null) return null;
            if (binding == null || catalog == null) return Failed(pawn, coat, "unsupported-appearance-api");
            if (!binding.TryCapture(coat, () => UnityData.IsInMainThread, CopyColor, out var palette, out var paletteReason)) return Failed(pawn, coat, paletteReason);
            string failure = null, failureDetail = null;
            if (AnthrosonaeBuiltInFamilies.Coats.Any(x => x.Coat.GeneDefName == coat.def.defName) &&
                !ValidCoatDefinition(coat, pawn.story.furDef)) failure = "modified-coat-definition";
            var layers = new List<AppearanceLayer>();
            var attachmentCaptures = new List<CapturedAnthrosonaeAttachment>();
            PawnRenderNodeProperties coatProps = null;
            try
            {
                foreach (var gene in genes)
                {
                    if (!gene.Active || gene.def.renderNodeProperties == null) continue;
                    for (int i = 0; i < gene.def.renderNodeProperties.Count; i++)
                    {
                        var props = gene.def.renderNodeProperties[i];
                        var key = new AppearanceNodeKey(gene.def.modContentPack?.PackageIdPlayerFacing ?? "unknown", gene.def.defName, i);
                        var location = props?.parentTagDef?.defName == "Head" ? AppearanceLayerLocation.Head :
                            props?.parentTagDef?.defName == "Body" ? AppearanceLayerLocation.Body : AppearanceLayerLocation.Unknown;
                        AppearanceNodeStructure structure;
                        if (location == AppearanceLayerLocation.Head)
                        {
                            // Native head-only extensions are not fitted or
                            // claimed by this adapter. Keep identity in refresh
                            // state without requiring the body structural schema.
                            structure = new AppearanceNodeStructure(new[] {
                                new KeyValuePair<string,string>("owner", "native-head"),
                                new KeyValuePair<string,string>("propsType", props.GetType().AssemblyQualifiedName),
                                new KeyValuePair<string,string>("nodeType", props.nodeClass?.AssemblyQualifiedName ?? "null"),
                                new KeyValuePair<string,string>("workerType", props.workerClass?.AssemblyQualifiedName ?? "null"),
                                new KeyValuePair<string,string>("texture", props.texPath ?? "null") });
                        }
                        else structure = AnthrosonaeNodeSchema.Capture(props);
                        layers.Add(new AppearanceLayer(key, structure, location));
                        if (gene == coat && i == 0) coatProps = props;
                        if (location == AppearanceLayerLocation.Body && AnthrosonaeBuiltInFamilies.AttachmentsByNode.ContainsKey(key))
                        {
                            if (CapturedAnthrosonaeAttachment.TryCapture(pawn, gene, key, props, structure, out var attachment, out var attachmentReason))
                                attachmentCaptures.Add(attachment);
                            else failure = failure ?? attachmentReason;
                        }
                    }
                }
            }
            catch (Exception error) { failure = "unsupported-render-node-structure"; failureDetail = error.Message; }
            if (!CapturedAnthrosonaeAttachmentSet.TryCreate(attachmentCaptures, catalog.Revision, out var attachments, out var captureReason))
            { failure = failure ?? captureReason; attachments = CapturedAnthrosonaeAttachmentSet.Empty; }
            var identity = new AppearanceCoatKey(coat.def.modContentPack?.PackageIdPlayerFacing ?? "unknown", coat.def.defName, coat.def.fur?.defName ?? "missing");
            var descriptor = new AppearanceDescriptor(AppearanceApiStatus.Available, "anthrosonae", apiVersion, pawn.story.furDef?.defName, identity, palette, layers);
            float scale = (pawn.story.bodyType.bodyGraphicScale.x + pawn.story.bodyType.bodyGraphicScale.y) * .5f;
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) failure = "unsupported-native-body-scale";
            return new AnthrosonaeCapture(descriptor, coat, coatProps, attachments, failure, scale,
                (Color?)cachedSkinBase.GetValue(pawn.story), pawn.story.skinColorOverride, failureDetail,
                HumanlikeMeshPoolUtility.HumanlikeBodyWidthForPawn(pawn));
        }
        private static AnthrosonaeCapture Failed(Pawn pawn, Gene coat, string reason)
        {
            var key = new AppearanceCoatKey(coat.def.modContentPack?.PackageIdPlayerFacing ?? "unknown", coat.def.defName, coat.def.fur?.defName ?? "missing");
            return new AnthrosonaeCapture(new AppearanceDescriptor(AppearanceApiStatus.Incompatible, "anthrosonae", apiVersion,
                pawn.story.furDef?.defName, key, default(AppearancePalette), null), coat, null, CapturedAnthrosonaeAttachmentSet.Empty,
                reason, 1f, null, pawn.story.skinColorOverride);
        }
        private static bool ValidCoatDefinition(Gene coat, FurDef fur)
        {
            AnthrosonaeBuiltInFamilies.CoatFamily family;
            if (fur == null || !AnthrosonaeBuiltInFamilies.CoatsByKey.TryGetValue(
                new AppearanceCoatKey("ATK.Anthrosonae", coat.def.defName, fur.defName), out family)) return false;
            if (coat.GetType() != binding.FurGeneType || coat.def.geneClass != binding.FurGeneType ||
                !fur.noGraphic || fur.overrideShaderTypeDef != null ||
                coat.def.modExtensions == null || coat.def.modExtensions.Count != 1) return false;
            var extension = coat.def.modExtensions[0];
            if (extension.GetType() != allowedColors.DeclaringType) return false;
            // Anthrosonae's own Genes.xml adds general colour presets to this
            // list. Preset choice lists do not own render geometry or materials:
            // the captured actual palette is authoritative for this adapter.
            // Keep rejecting extensions that can replace the rendering policy.
            if (fur.bodyTypeGraphicPaths == null || fur.bodyTypeGraphicPaths.Count != 7) return false;
            var required = new HashSet<string>(new[] { "Male", "Female", "Hulk", "Fat", "Thin", "Child", "Baby" }, StringComparer.Ordinal);
            foreach (var path in fur.bodyTypeGraphicPaths)
            {
                string body = path.bodyType?.defName;
                string expectedRoot;
                if (body == null || !required.Remove(body) || !family.NativeBodyTextureRoots.TryGetValue(body, out expectedRoot) || path.texturePath != expectedRoot) return false;
            }
            return required.Count == 0;
        }
        // Copy after main-thread capture. Menus receive only immutable policy
        // values, never native Gene/Props identities or optional API lookups.
        internal static AnthrosonaePolicySnapshot CapturePolicy(AnthrosonaeCapture capture) => capture == null ? null :
            new AnthrosonaePolicySnapshot(capture.Descriptor, capture.FailureReason, catalog);
    }

    internal static class AnthrosonaeColorWriterPatch
    {
        public static void Prefix(object __instance, out PaletteMutationCoordinator.Scope __state)
        {
            __state = null;
            if (!UnityData.IsInMainThread || Scribe.mode != LoadSaveMode.Inactive) return;
            var pawn = (__instance as Gene)?.pawn;
            var comp = pawn?.TryGetComp<PawnBodyType_ThingComp>();
            if (comp != null) __state = comp.GetAnthrosonaePaletteCoordinator().Begin();
        }
        public static Exception Finalizer(Exception __exception, PaletteMutationCoordinator.Scope __state)
        {
            try { __state?.Close(__exception == null); }
            catch (Exception refreshError)
            {
                Log.Warning("[RimRound Anthrosonae] Palette refresh failed: " + refreshError.Message);
                return __exception ?? refreshError;
            }
            return __exception;
        }
    }
}
