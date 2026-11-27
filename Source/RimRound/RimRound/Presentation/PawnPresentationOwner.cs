using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using AlienRace;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using RimRound.Presentation.Anthrosonae;

namespace RimRound.Presentation
{
    // A separate GraphicDatabase key keeps the declared mirror policy independent
    // of an optional west file supplied by another texture provider.
    public sealed class Graphic_PresentationMirrorWest : Graphic_Multi
    {
        private List<ShaderParameter> parameters;
        public override Material MatWest => MatEast;
        public override bool WestFlipped => true;
        public override void Init(GraphicRequest request)
        {
            parameters = request.shaderParameters;
            base.Init(request);
        }
        public override Graphic GetColoredVersion(Shader shader, Color first, Color second) =>
            GraphicDatabase.Get(typeof(Graphic_PresentationMirrorWest), path, shader, drawSize,
                first, second, data, parameters, maskPath);
    }

    internal struct PresentationInputs : IEquatable<PresentationInputs>
    {
        internal string Stage;
        internal long StateRevision, RefreshRevision, DefaultsRevision;
        internal int Settings;
        internal BodyTypeDef NativeBody;
        internal Gender Gender;
        internal FurDef Fur;
        internal MutantDef Mutant;
        internal Color Skin;
        internal Color? Statue;
        internal bool Exempt, Eligible, Dead, GeneBodyLayer, TattooLayer;
        internal RotDrawMode Rot;
        internal AnthrosonaeCapture Appearance;
        internal XenotypeArtworkCapture XenotypeArtwork;
        public bool Equals(PresentationInputs other) => Stage == other.Stage && StateRevision == other.StateRevision &&
            RefreshRevision == other.RefreshRevision && DefaultsRevision == other.DefaultsRevision &&
            Settings == other.Settings && NativeBody == other.NativeBody &&
            Gender == other.Gender && Fur == other.Fur && Mutant == other.Mutant && Skin == other.Skin && Statue == other.Statue &&
            Exempt == other.Exempt && Eligible == other.Eligible && Dead == other.Dead && Rot == other.Rot &&
            GeneBodyLayer == other.GeneBodyLayer && TattooLayer == other.TattooLayer && object.Equals(Appearance, other.Appearance) &&
            object.Equals(XenotypeArtwork, other.XenotypeArtwork);
        internal bool PaletteContextEquals(PresentationInputs other)
        {
            if (Appearance == null || other.Appearance == null || !Appearance.ContextEquals(other.Appearance)) return false;
            var left = this; var right = other;
            // Anthrosonae body materials use the captured coat palette. The
            // unrelated native/HAR skin channel is canonicalized at capture;
            // forced gear/health refreshes still have their own revision.
            left.Appearance = null; right.Appearance = null;
            return left.Equals(right);
        }
    }

    // Published once per explicit change. Render consumers only read this object.
    public sealed class PreparedBodyPresentation
    {
        public readonly ResolvedBodyPresentation Resolution;
        public readonly ResolvedStageEntry Stage;
        public readonly BodyTypeDef VisualBody;
        public readonly BodyTypeInfo? LegacyInfo;
        public readonly Graphic BodyGraphic, RottenGraphic;
        public readonly IReadOnlyDictionary<Apparel, Graphic> ApparelGraphics;
        public readonly IReadOnlyDictionary<Apparel, Graphic> StatueApparelGraphics;
        public readonly IReadOnlyDictionary<Apparel, PreparedMobilityAidPresentation> MobilityAids;
        public readonly string NativeReason;
        internal readonly PresentationInputs Inputs;
        public readonly PresentationCandidateContext CandidateContext;
        public bool IsCustom => Stage != null && NativeReason == null;
        public readonly bool UsesCustomBody;
        public readonly PreparedAnthrosonaePilot AnthrosonaePilot;
        internal readonly XenotypeArtworkCapture XenotypeArtwork;
        public bool UsesXenotypeArtworkOverride => IsCustom && XenotypeArtwork != null && XenotypeArtwork.FailureReason == null;
        internal PreparedBodyPresentation(ResolvedBodyPresentation resolution, BodyTypeDef visualBody,
            Graphic graphic, Graphic rotten, IDictionary<Apparel, Graphic> apparel, IDictionary<Apparel, Graphic> statueApparel,
            string nativeReason, PresentationInputs inputs, IDictionary<Apparel, PreparedMobilityAidPresentation> mobilityAids,
            PreparedAnthrosonaePilot anthrosonaePilot = null, PresentationCandidateContext candidateContext = null)
        {
            Resolution = resolution; Stage = resolution?.SourceEntry; VisualBody = visualBody;
            BodyGraphic = graphic; RottenGraphic = rotten; NativeReason = nativeReason;
            Inputs = inputs;
            AnthrosonaePilot = anthrosonaePilot;
            XenotypeArtwork = inputs.Appearance == null && nativeReason == null ? inputs.XenotypeArtwork : null;
            CandidateContext = candidateContext;
            UsesCustomBody = IsCustom && (Stage.AnchorPolicy != "legacyBodyType" || BodyTypeUtility.IsRRBody(Stage.LegacyBodyTypeDefName));
            ApparelGraphics = new ReadOnlyDictionary<Apparel, Graphic>(apparel);
            StatueApparelGraphics = new ReadOnlyDictionary<Apparel, Graphic>(statueApparel);
            MobilityAids = new ReadOnlyDictionary<Apparel, PreparedMobilityAidPresentation>(mobilityAids);
            if (IsCustom) LegacyInfo = new BodyTypeInfo(0f, Stage.MapScale,
                Stage.DownedPosePolicy == "frontStatic" ? 0f : Stage.WiggleSpeed,
                Stage.PortraitZoom, Stage.PortraitPanY, Stage.PortraitPanStyleY);
        }
    }

    public static class PawnPresentationOwner
    {
        private static readonly string[] HumanCapabilities = { "human" };
        private static readonly string[] NoCapabilities = new string[0];
        private static PresentationRegistry definitionFactsRegistry;
        private static PresentationDefinitionFacts definitionFacts;
        public static PreparedBodyPresentation For(Pawn pawn) => pawn?.TryGetComp<PawnBodyType_ThingComp>()?.PreparedPresentation;
        // A menu only evaluates the published capture; it never prepares graphics.
        public static PresentationCandidate Preview(Pawn pawn, PresentationSelection choice)
        {
            var context = For(pawn)?.CandidateContext;
            return context == null ? null : PresentationCandidateEvaluator.Evaluate(context, choice);
        }

        private static PresentationDefinitionFacts CaptureDefinitionFacts(PresentationRegistry registry)
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Definition capture requires game thread.");
            if (ReferenceEquals(definitionFactsRegistry, registry)) return definitionFacts;
            var facts = new Dictionary<string, PresentationAnchorFact>(StringComparer.Ordinal);
            foreach (var profile in registry.Profiles.Values)
            foreach (var entry in profile.Entries.Values)
            {
                if (entry == null || entry.AnchorPolicy != "legacyBodyType" || entry.LegacyBodyTypeDefName == null ||
                    facts.ContainsKey(entry.LegacyBodyTypeDefName)) continue;
                string name = entry.LegacyBodyTypeDefName;
                facts.Add(name, new PresentationAnchorFact(DefDatabase<BodyTypeDef>.GetNamedSilentFail(name) != null,
                    BodyTypeUtility.IsRRBody(name)));
            }
            definitionFacts = new PresentationDefinitionFacts(registry.Revision, facts);
            definitionFactsRegistry = registry;
            return definitionFacts;
        }
        public static bool UsesStaticDownedPose(Pawn pawn) => pawn?.Downed == true && IsCustom(pawn) &&
            For(pawn).Stage.DownedPosePolicy == "frontStatic";
        public static bool IsCustom(Pawn pawn)
        {
            var prepared = For(pawn);
            return prepared?.IsCustom == true && (pawn.Drawer.renderer.CurRotDrawMode != RotDrawMode.Dessicated ||
                prepared.Stage.DessicationPolicy == "body");
        }

        // Called by game-thread lifecycle events, never from a render getter.
        public static void Prepare(Pawn pawn, bool redraw = true)
        {
            var comp = pawn?.TryGetComp<PawnBodyType_ThingComp>();
            if (comp == null) return;
            if (!UnityData.IsInMainThread)
            {
                PresentationRefreshQueue.Enqueue(pawn, comp);
                return;
            }
            if (comp.PresentationGame == null || !ReferenceEquals(comp.PresentationGame, Current.Game) ||
                pawn.story?.bodyType == null || comp.PreparingPresentation) return;
            comp.PreparingPresentation = true;
            try
            {
            comp.RestoreOrObserveNativeBody();
            // This was already an input read. Do it before passive appearance
            // capture so any native lazy base-colour initialization is observed.
            Color observedSkin = pawn.story.SkinColor;
            var appearance = AnthrosonaeBridge.Capture(pawn);
            var biology = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            var weight = pawn.WeightHediff();
            var defaults = Current.Game?.GetComponent<PresentationDefaults>();
            string stageKey = biology?.BodyPresentationStageKey;
            if (stageKey == null && weight != null && biology?.BodyStageStateInitialized == true)
                stageKey = LegacyBodyStagePolicies.Resolve(biology.BodyStagePolicyId).Evaluate(weight.Severity,
                    biology.BodyStageMultiplier, GlobalSettings.varyMinWeightForBodyTypeByBodySize).StageKey;
            var inputs = new PresentationInputs {
                Stage = stageKey, StateRevision = comp.PresentationStateRevision, RefreshRevision = comp.PresentationRefreshRevision,
                DefaultsRevision = defaults?.Revision ?? 0L,
                NativeBody = pawn.story.bodyType, Gender = pawn.gender, Fur = pawn.story.furDef,
                Mutant = pawn.IsMutant ? pawn.mutant.Def : null,
                // HAR's null-override skin getter returns its cached channel,
                // not necessarily story's base skin colour. Adapted materials
                // do not consume either channel, so neither is a palette key.
                Skin = appearance == null ? observedSkin : default(Color), Statue = pawn.Drawer.renderer.StatueColor,
                Exempt = comp.PersonallyExempt || comp.CategoricallyExempt,
                GeneBodyLayer = appearance == null && HasUnsupportedGeneBodyLayer(pawn),
                Appearance = appearance,
                XenotypeArtwork = comp.OverrideXenotypeArtwork && appearance == null ? XenotypeArtworkCapture.Capture(pawn) : null,
                TattooLayer = GlobalSettings.showBodyTatoosForCustomSprites && pawn.style?.BodyTattoo != null &&
                    !pawn.style.BodyTattoo.noGraphic,
                Eligible = BodyModelEligibility.HasAdultBiology(pawn) && weight != null,
                Dead = pawn.Dead, Rot = pawn.Drawer.renderer.CurRotDrawMode,
                Settings = GlobalSettings.maxVisualSizeGelLevel.threshold * 256 +
                    (GlobalSettings.useLegacyMaleSprites ? 1 : 0) + (GlobalSettings.useAltMaleSprites ? 2 : 0) +
                    (GlobalSettings.useOldLardySprite ? 4 : 0) + (GlobalSettings.onlyUseStandardBodyType ? 8 : 0) +
                    (GlobalSettings.preferDefaultOutfitOverNaked ? 16 : 0) + (GlobalSettings.hidePacksForCustomBodies ? 32 : 0) +
                    (GlobalSettings.useZoomPortraitStyle ? 64 : 0) + (GlobalSettings.alternateNorthHeadPositionForRRBodytypes ? 128 : 0)
            };
            if (comp.PreparedPresentation != null && comp.PreparedPresentation.Inputs.Equals(inputs)) return;
            var cachedPalette = comp.FindAnthrosonaePaletteSnapshot(inputs);
            if (cachedPalette != null)
            {
                comp.PublishPresentation(cachedPalette);
                if (redraw) Invalidate(pawn);
                return;
            }
            var variants = new List<string>();
            if (GlobalSettings.useLegacyMaleSprites) variants.Add("legacyMale");
            else if (GlobalSettings.useAltMaleSprites) variants.Add("alternateMale");
            if (!GlobalSettings.useOldLardySprite) variants.Add("alternateLardy");
            var defaultChoice = defaults?.GetChoice(pawn.def.modContentPack.PackageIdPlayerFacing,
                pawn.def.defName, pawn.gender.ToString());
            var request = new PresentationRequest {
                RacePackageId = pawn.def.modContentPack.PackageIdPlayerFacing, RaceDefName = pawn.def.defName,
                AdapterCapabilityIds = pawn.def is ThingDef_AlienRace ? HumanCapabilities : NoCapabilities,
                Gender = pawn.gender.ToString(), RequestedProfileId = comp.RequestedProfileId,
                RequestedShapeId = comp.RequestedShapeId, SelectionMode = comp.PresentationMode,
                DefaultProfileIdOverride = defaultChoice?.Mode == "profile" ? defaultChoice.ProfileId : null,
                NativeDefault = defaultChoice?.Mode == "native",
                StageKey = stageKey ?? "thin", VisualCapStageKey = "gelatinous" + GlobalSettings.maxVisualSizeGelLevel.threshold.ToString("00"),
                StateRevision = comp.PresentationStateRevision, VariantIds = variants
            };
            var choice = new PresentationSelection(comp.PresentationMode, comp.RequestedProfileId, comp.RequestedShapeId);
            var context = new PresentationCandidateContext(PresentationCatalog.Registry, request,
                CaptureDefinitionFacts(PresentationCatalog.Registry), AnthrosonaeBridge.CapturePolicy(appearance),
                GlobalSettings.onlyUseStandardBodyType, inputs.Eligible, inputs.Exempt, inputs.Fur != null,
                inputs.GeneBodyLayer, inputs.TattooLayer, inputs.Mutant != null, inputs.Dead,
                inputs.Statue.HasValue, inputs.Rot == RotDrawMode.Dessicated,
                comp.OverrideXenotypeArtwork, inputs.XenotypeArtwork?.FailureReason);
            var candidate = PresentationCandidateEvaluator.Evaluate(context, choice);
            var resolution = candidate.Resolution;
            string reason = candidate.NativeReason;
            var entry = resolution.SourceEntry;
            var coatDecision = candidate.CoatDecision;
            var apparel = new Dictionary<Apparel, Graphic>();
            var statueApparel = new Dictionary<Apparel, Graphic>();
            var mobilityAids = new Dictionary<Apparel, PreparedMobilityAidPresentation>();
            BodyTypeDef visual = null;
            Graphic body = null, rotten = null;
            PreparedAnthrosonaePilot anthrosonaePilot = null;
            if (reason == null && coatDecision?.Kind == AppearanceDecisionKind.PreparedBaseBody &&
                !PreparedAnthrosonaePilot.TryCreate(appearance, appearance.Attachments, coatDecision, entry, out anthrosonaePilot, out reason))
                anthrosonaePilot = null;
            if (reason == null)
            {
                visual = entry.AnchorPolicy == "legacyBodyType" ? DefDatabase<BodyTypeDef>.GetNamedSilentFail(entry.LegacyBodyTypeDefName) : pawn.story.bodyType;
                if (entry.AnchorPolicy == "explicit")
                    visual = new BodyTypeDef { defName = pawn.story.bodyType.defName,
                        headOffset = new Vector2(entry.HeadOffsetX, entry.HeadOffsetY), bedOffset = entry.BedOffsetY,
                        woundAnchors = pawn.story.bodyType.woundAnchors, woundScale = pawn.story.bodyType.woundScale };
                var race = pawn.def as ThingDef_AlienRace;
                var color = entry.ColorPolicy == "white" ? Color.white : pawn.Drawer.renderer.StatueColor ?? pawn.story.SkinColor;
                var second = entry.ColorPolicy == "white" ? Color.white : race.alienRace.generalSettings.alienPartGenerator.SkinColor(pawn, false);
                Shader shader = entry.ShaderPolicy == "masked" ? ShaderDatabase.CutoutComplex :
                    entry.ColorPolicy == "white" ? ShaderDatabase.Cutout :
                    race.alienRace.graphicPaths.skinShader?.Shader ?? ShaderUtility.GetSkinShader(pawn);
                if (shader == ShaderDatabase.CutoutSkin && pawn.story.SkinColorOverriden) shader = ShaderDatabase.CutoutSkinColorOverride;
                if (pawn.Drawer.renderer.StatueColor.HasValue) shader = ShaderDatabase.Cutout;
                string mask = entry.MaskPolicy == "separate" ? entry.MaskTextureRoot : null;
                var shaderParameters = entry.ColorPolicy == "white" ? null : race.alienRace.graphicPaths.SkinColoringParameter;
                Type graphicType = entry.DirectionPolicy == "mirrorWest" ? typeof(Graphic_PresentationMirrorWest) : typeof(Graphic_Multi);
                string bodyRoot = entry.BodyTextureRoot;
                bool adaptedCoat = coatDecision?.Kind == AppearanceDecisionKind.PreparedBaseBody;
                if (adaptedCoat)
                {
                    bodyRoot = coatDecision.Mapping.BodyTextureRoot;
                    color = appearance.Primary; second = appearance.Secondary ?? Color.white;
                    shader = ShaderDatabase.Cutout; mask = null; shaderParameters = null;
                }
                if (adaptedCoat)
                {
                    if (!AnthrosonaeCoatMaterials.TryPrepare(pawn, appearance, coatDecision.Mapping, out body, out rotten, out reason))
                        visual = null;
                }
                else
                {
                    body = GraphicDatabase.Get(graphicType, bodyRoot, shader, Vector2.one,
                        color, second, null, shaderParameters, mask);
                    rotten = GraphicDatabase.Get(graphicType, bodyRoot, shader, Vector2.one,
                        PawnRenderUtility.GetRottenColor(color), adaptedCoat ? PawnRenderUtility.GetRottenColor(second) : second, null, shaderParameters, mask);
                }
                if (reason == null && pawn.apparel != null)
                    foreach (var item in pawn.apparel.WornApparel)
                    {
                        if (!BodyGarment(item)) continue;
                        bool mobilityAid = PreparedMobilityAidPresentation.IsMobilityAid(item);
                        string path = mobilityAid
                            ? PreparedMobilityAidPresentation.ResolveRoot(item, entry.ApparelSuffix)
                            : item.WornGraphicPath + "_" + entry.ApparelSuffix;
                        if (path == null || !HasTexture(path))
                        {
                            string fallback = !mobilityAid &&
                                (GlobalSettings.preferDefaultOutfitOverNaked || entry.ApparelFallback == "default") ?
                                Values.defaultClothingSetGraphicPath + "_" + entry.ApparelSuffix : null;
                            if (fallback == null || !HasTexture(fallback))
                            {
                                // Missing clothing art must not replace the pawn's
                                // sized body. A present null entry suppresses just
                                // this garment in PreparedApparelGraphicPatch while
                                // retaining its equipment and all supported layers.
                                apparel[item] = null;
                                statueApparel[item] = null;
                                continue;
                            }
                            path = fallback;
                        }
                        Shader apparelShader = item.StyleDef?.graphicData.shaderType?.Shader ??
                            ((item.StyleDef == null ? item.def.apparel.useWornGraphicMask : item.StyleDef.UseWornGraphicMask) ?
                                ShaderDatabase.CutoutComplex : ShaderDatabase.Cutout);
                        var mobility = PreparedMobilityAidPresentation.Prepare(item, entry, path, apparelShader, false);
                        if (mobility != null)
                        {
                            mobilityAids[item] = mobility;
                            apparel[item] = mobility.Graphic;
                            statueApparel[item] = PreparedMobilityAidPresentation.Prepare(item, entry, path, apparelShader, true).Graphic;
                        }
                        else
                        {
                            apparel[item] = GraphicDatabase.Get<Graphic_Multi>(path, apparelShader, item.def.graphicData.drawSize, item.DrawColor);
                            statueApparel[item] = GraphicDatabase.Get<Graphic_Multi>(path, ShaderDatabase.Cutout, item.def.graphicData.drawSize, item.DrawColor);
                        }
                    }
            }
            if (reason != null) anthrosonaePilot = null;
            comp.PublishPresentation(new PreparedBodyPresentation(resolution, visual, body, rotten, apparel, statueApparel,
                reason, inputs, mobilityAids, anthrosonaePilot, context));
            if (redraw) Invalidate(pawn);
            }
            finally { comp.PreparingPresentation = false; }
        }

        // Body-attached gene layers have their own authored silhouette/placement.
        // Until an adapter explicitly fits them, preserve the native composite.
        // Head-only eyes/horns continue through native head render nodes.
        private static bool HasUnsupportedGeneBodyLayer(Pawn pawn)
        {
            var genes = pawn.genes?.GenesListForReading;
            if (genes == null) return false;
            for (int i = 0; i < genes.Count; i++)
            {
                if (!genes[i].Active) continue;
                var nodes = genes[i].def.renderNodeProperties;
                if (nodes == null) continue;
                for (int n = 0; n < nodes.Count; n++)
                    if (nodes[n].parentTagDef?.defName != "Head") return true;
            }
            return false;
        }

        private static bool BodyGarment(Apparel apparel)
        {
            if (apparel.WornGraphicPath.NullOrEmpty()) return false;
            var layer = apparel.def.apparel.LastLayer;
            return layer != ApparelLayerDefOf.Overhead && layer != ApparelLayerDefOf.EyeCover &&
                layer.defName != "OnHead" && layer.defName != "StrappedHead" &&
                !apparel.def.apparel.tags.Exists(t => t.Contains("Warcasket")) &&
                apparel.WornGraphicPath != BaseContent.PlaceholderImagePath && apparel.WornGraphicPath != BaseContent.PlaceholderGearImagePath &&
                (!apparel.RenderAsPack() || GlobalSettings.hidePacksForCustomBodies);
        }
        private static bool HasTexture(string path) => !ContentFinder<Texture2D>.Get(path + "_south", false).NullOrBad() &&
            !ContentFinder<Texture2D>.Get(path + "_north", false).NullOrBad() && !ContentFinder<Texture2D>.Get(path + "_east", false).NullOrBad();
        public static void Invalidate(Pawn pawn)
        {
            if (!UnityData.IsInMainThread) { Prepare(pawn); return; }
            PortraitsCache.SetDirty(pawn);
            GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
            pawn.Drawer?.renderer?.renderTree.SetDirty();
        }
        public static void NotifyChanged(Pawn pawn)
        {
            var comp = pawn?.TryGetComp<PawnBodyType_ThingComp>();
            if (comp == null || (UnityData.IsInMainThread && comp.PreparingPresentation)) return;
            Interlocked.Increment(ref comp.PresentationRefreshRevision);
            Prepare(pawn);
        }
    }
}
