using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.Presentation;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.UI
{
    public class CustomBodyTypeGizmo : Command_Action
    {
        private readonly PawnBodyType_ThingComp comp;

        public CustomBodyTypeGizmo(PawnBodyType_ThingComp comp)
        {
            this.comp = comp;
            defaultLabel = "Body appearance";
            defaultDesc = "Choose this pawn's body artwork and shape. Saved choices remain visible when unavailable.";
            icon = Utilities.Resources.FILLER_TEXTURE;
            action = () => Find.WindowStack.Add(new PresentationSelectionWindow(comp));
            Order = 404;
        }

        public override bool GroupsWith(Gizmo other) => false;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            defaultDesc = "Choose this pawn's body artwork and shape.\n\nSaved choice: " +
                PresentationSelectionWindow.RequestLabel(comp) + "\nShown now: " +
                PresentationSelectionWindow.EffectiveLabel(comp) + "\n" +
                PresentationSelectionWindow.Reason(comp);
            return base.GizmoOnGUI(topLeft, maxWidth, parms);
        }
    }

    // Selection is a game-thread request. Drawing only reads the published presentation.
    public sealed class PresentationSelectionWindow : Window
    {
        private readonly PawnBodyType_ThingComp comp;
        private Pawn Pawn => comp.parent as Pawn;
        private Vector2 scrollPosition;
        public override Vector2 InitialSize => new Vector2(Math.Min(560f, Verse.UI.screenWidth - 32f),
            Math.Min(520f, Verse.UI.screenHeight - 32f));

        public PresentationSelectionWindow(PawnBodyType_ThingComp comp)
        {
            this.comp = comp;
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            draggable = true;
        }

        private bool CanEdit
        {
            get
            {
                var pawn = Pawn;
                var game = Current.Game;
                // Adult/metabolism eligibility belongs to the published capture;
                // temporary control and needs remain live without scanning genes.
                return comp.PreparedPresentation?.CandidateContext?.Eligible == true &&
                    game != null && ReferenceEquals(comp.PresentationGame, game) &&
                    pawn != null && !pawn.Dead && pawn.needs?.food != null && !pawn.IsQuestLodger() &&
                    (pawn.IsColonist || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony) && !pawn.InMentalState &&
                    (pawn.IsColonistPlayerControlled || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony) &&
                    game.PlayerHasControl;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont priorFont = Text.Font;
            TextAnchor priorAnchor = Text.Anchor;
            bool priorWrap = Text.WordWrap;
            bool scrolling = false;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                string instructions = "Choose the artwork and body shape shown for this pawn. Race default keeps this pawn's saved shape.";
                string reason = Reason(comp);
                string footer = CanEdit ?
                    (GlobalSettings.onlyUseStandardBodyType ? "Standard-only is enabled. Your saved shape will return when that setting is disabled." :
                    "Changes apply immediately to this pawn's appearance.") :
                    "Read only: appearance changes require an eligible adult colonist, prisoner or slave under colony control.";
                float width = Math.Max(1f, inRect.width - 16f);
                float instructionsHeight = Math.Max(42f, Text.CalcHeight(instructions, width));
                float rowsY = 44f + instructionsHeight;
                float reasonY = rowsY + 112f;
                float reasonHeight = Math.Max(44f, Text.CalcHeight(reason, width));
                float dividerY = reasonY + reasonHeight + 8f;
                float artY = dividerY + 16f;
                float shapeY = artY + 42f;
                bool showXenotypeOverride = ModsConfig.BiotechActive || comp.OverrideXenotypeArtwork;
                string overrideLabel = "RR_XenotypeArtwork_Override".Translate();
                string overrideHelp = "RR_XenotypeArtwork_Help".Translate();
                if (comp.PresentationMode == "native") overrideHelp += " " + "RR_XenotypeArtwork_NativeSelected".Translate();
                float overrideY = shapeY + 44f;
                float overrideLabelHeight = Math.Max(30f, Text.CalcHeight(overrideLabel, Math.Max(1f, width - 32f)));
                Text.Font = GameFont.Tiny;
                float overrideHelpHeight = Text.CalcHeight(overrideHelp, width);
                float footerY = overrideY + (showXenotypeOverride ? overrideLabelHeight + overrideHelpHeight + 12f : 0f);
                float footerHeight = Math.Max(40f, Text.CalcHeight(footer, width));
                var content = new Rect(0f, 0f, width, footerY + footerHeight + 4f);
                var viewport = new Rect(inRect.x, inRect.y, inRect.width, Math.Max(1f, inRect.height - 40f));
                Widgets.BeginScrollView(viewport, ref scrollPosition, content);
                scrolling = true;
                Text.Font = GameFont.Medium;
                Label(new Rect(0f, 0f, content.width - 24f, 32f), "Body appearance: " + Pawn.LabelShortCap);
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(0f, 40f, content.width, instructionsHeight), instructions);
                Row(content, rowsY, "Saved choice", RequestLabel(comp));
                Row(content, rowsY + 26f, "Saved shape", ShapeLabel(comp.RequestedShapeId));
                Row(content, rowsY + 52f, "Shown now", EffectiveLabel(comp));
                var resolution = comp.PreparedPresentation?.Resolution;
                string shownShape = comp.PreparedPresentation?.IsCustom == true ? ShapeLabel(resolution?.EffectiveShapeId) : "Native";
                if (comp.PreparedPresentation?.IsCustom == true && GlobalSettings.onlyUseStandardBodyType &&
                    comp.RequestedShapeId != null && comp.RequestedShapeId != "standard")
                    shownShape += " (standard-only setting)";
                Row(content, rowsY + 78f, "Shown shape", shownShape);
                var reasonRect = new Rect(0f, reasonY, content.width, reasonHeight);
                Widgets.Label(reasonRect, reason);
                TooltipHandler.TipRegion(reasonRect, reason);
                Widgets.DrawLineHorizontal(0f, dividerY, content.width);

                Widgets.Label(new Rect(0f, artY + 4f, 112f, 30f), "Artwork");
                Rect artButton = new Rect(120f, artY, content.width - 120f, 32f);
                if (Widgets.ButtonText(artButton, Fit(RequestLabel(comp), artButton.width - 20f), active: CanEdit)) OpenArtMenu();
                TooltipHandler.TipRegion(artButton, RequestLabel(comp) +
                    (comp.RequestedProfileId == null ? "" : "\nSaved art set: " + comp.RequestedProfileId));

                Widgets.Label(new Rect(0f, shapeY + 4f, 112f, 30f), "Body shape");
                Rect shapeButton = new Rect(120f, shapeY, content.width - 120f, 32f);
                bool hasSelection = ShapeProfile() != null;
                if (Widgets.ButtonText(shapeButton, Fit(ShapeLabel(comp.RequestedShapeId), shapeButton.width - 20f),
                    active: CanEdit && hasSelection)) OpenShapeMenu();
                TooltipHandler.TipRegion(shapeButton, comp.PresentationMode != "native" ?
                    "Your saved shape is retained while the standard-only setting is enabled." :
                    "Choose race default or an art set to select a body shape.");

                if (showXenotypeOverride)
                {
                    bool enabled = comp.OverrideXenotypeArtwork;
                    var overrideRect = new Rect(0f, overrideY, content.width, overrideLabelHeight);
                    Widgets.CheckboxLabeled(overrideRect, overrideLabel, ref enabled, disabled: !CanEdit);
                    TooltipHandler.TipRegion(overrideRect, overrideHelp);
                    if (CanEdit && enabled != comp.OverrideXenotypeArtwork) comp.SetOverrideXenotypeArtwork(enabled);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(0f, overrideY + overrideLabelHeight, content.width, overrideHelpHeight), overrideHelp);
                }

                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(0f, footerY, content.width, footerHeight), footer);
            }
            finally
            {
                if (scrolling) Widgets.EndScrollView();
                Text.Font = priorFont; Text.Anchor = priorAnchor; Text.WordWrap = priorWrap;
            }
        }

        private PresentationSelection SavedSelection => new PresentationSelection(
            comp.PresentationMode, comp.RequestedProfileId, comp.RequestedShapeId);

        private void OpenArtMenu()
        {
            if (!CanEdit) return;
            var options = new List<FloatMenuOption>();
            AddSelection(options, "Race default", new PresentationSelection("default", null, comp.RequestedShapeId));
            AddSelection(options, "Native body", new PresentationSelection("native", comp.RequestedProfileId, comp.RequestedShapeId));
            foreach (var profile in CompatibleProfiles().OrderBy(p => ProfileLabel(p), StringComparer.CurrentCulture)
                .ThenBy(p => p.ProfileId, StringComparer.Ordinal))
            {
                string shape = profile.ShapeIds.Contains(comp.RequestedShapeId) ? comp.RequestedShapeId : profile.DefaultShapeId;
                AddSelection(options, ProfileLabel(profile), new PresentationSelection("override", profile.ProfileId, shape));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private IEnumerable<PresentationProfile> CompatibleProfiles()
        {
            var context = comp.PreparedPresentation?.CandidateContext;
            if (context == null) yield break;
            foreach (var profile in context.Registry.Profiles.Values)
            {
                if (context.CanChooseProfile(profile.ProfileId)) yield return profile;
            }
        }

        private void AddSelection(List<FloatMenuOption> options, string label, PresentationSelection choice)
        {
            var prediction = PawnPresentationOwner.Preview(Pawn, choice);
            bool current = choice.Equals(SavedSelection) && comp.PreparedPresentation != null;
            bool nativeNow = current ? !comp.PreparedPresentation.IsCustom : prediction?.IsNativeNow == true;
            string status = nativeNow ? " " + "RR_Presentation_NativeNowSuffix".Translate().ToString() : "";
            var option = new FloatMenuOption(Fit(label, Math.Max(0f, 340f - Text.CalcSize(status).x)) + status,
                () => Apply(choice.Mode, choice.ProfileId, choice.ShapeId));
            option.tooltip = new TipSignal(label + "\n" + SelectionExplanation(choice, prediction));
            options.Add(option);
        }

        private string SelectionExplanation(PresentationSelection choice, PresentationCandidate prediction)
        {
            // The actual chosen result includes material/attachment/apparel
            // failures that hypothetical structural predictions cannot promise.
            if (choice.Equals(SavedSelection) && comp.PreparedPresentation != null) return Reason(comp);
            if (prediction == null) return "RR_Presentation_ApplyToUpdate".Translate();
            if (prediction.IsNativeNow) return ReasonForCode(prediction.NativeReason);
            string detail = "RR_Presentation_ArtworkAvailable".Translate();
            return prediction.Resolution.FallbackReason == null ? detail :
                ReasonForCode(prediction.Resolution.FallbackReason) + "\n" + detail;
        }

        private void OpenShapeMenu()
        {
            if (!CanEdit) return;
            PresentationProfile profile = ShapeProfile();
            if (profile == null) return;
            var options = new List<FloatMenuOption>();
            foreach (string shape in profile.ShapeIds)
            {
                AddSelection(options, ShapeLabel(shape), new PresentationSelection(
                    comp.PresentationMode, comp.RequestedProfileId, shape));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private PresentationProfile ShapeProfile() => PresentationCandidateEvaluator.ShapeProfile(
            comp.PreparedPresentation?.CandidateContext, PresentationCatalog.Registry, SavedSelection);

        private void Apply(string mode, string profile, string shape)
        {
            if (!CanEdit) return;
            if (!PresentationCandidateEvaluator.IsDeclaredSelection(comp.PreparedPresentation?.CandidateContext,
                new PresentationSelection(mode, profile, shape))) return;
            comp.SetPresentationRequest(mode, profile, shape);
        }

        private static void Row(Rect rect, float y, string caption, string value)
        {
            Widgets.Label(new Rect(0f, y, 112f, 24f), caption);
            Label(new Rect(120f, y, rect.width - 120f, 24f), value);
        }

        private static void Label(Rect rect, string value)
        {
            bool previous = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(rect, Fit(value, rect.width));
            Text.WordWrap = previous;
            TooltipHandler.TipRegion(rect, value);
        }

        private static string Fit(string value, float width)
        {
            value = value ?? "";
            if (Text.CalcSize(value).x <= width) return value;
            int length = value.Length;
            while (length > 0 && Text.CalcSize(value.Substring(0, length) + "…").x > width) length--;
            return value.Substring(0, length) + "…";
        }

        private static string ShapeLabel(string id) => id == "standard" ? "Standard" : id == "apple" ? "Apple" :
            string.IsNullOrEmpty(id) ? "Art set default" : id;
        private static string ProfileLabel(PresentationProfile profile) => string.IsNullOrWhiteSpace(profile.Label) ? profile.ProfileId : profile.Label;
        private static string ProfileName(string id)
        {
            PresentationProfile profile;
            return id != null && PresentationCatalog.Registry.Profiles.TryGetValue(id, out profile) ? ProfileLabel(profile) :
                string.IsNullOrEmpty(id) ? "Unavailable art set" : "Unavailable: " + id;
        }
        internal static string RequestLabel(PawnBodyType_ThingComp comp) => comp.PresentationMode == "native" ? "Native body" :
            comp.PresentationMode == "default" ? "Race default" : ProfileName(comp.RequestedProfileId);
        internal static string EffectiveLabel(PawnBodyType_ThingComp comp) => comp.PreparedPresentation == null ? "Preparing appearance" :
            comp.PreparedPresentation.IsCustom ? ProfileName(comp.PreparedPresentation.Resolution.EffectiveProfileId) : "Native body";
        internal static string Reason(PawnBodyType_ThingComp comp)
        {
            if (comp.PreparedPresentation == null) return "Preparing this pawn's appearance.";
            string reason = comp.PreparedPresentation?.NativeReason ?? comp.PreparedPresentation?.Resolution?.FallbackReason;
            return ReasonForCode(reason);
        }

        internal static string ReasonForCode(string reason)
        {
            switch (reason)
            {
                case null: return "Your selected artwork is available.";
                case "selected-native": return "Using the pawn's native body artwork.";
                case "requested-unavailable": return "The saved art set or shape is unavailable; a compatible race default is shown.";
                case "requested-shape-unavailable": return "The saved body shape is unavailable for this art set; its default shape is shown.";
                case "default-unavailable": return "The race default is unavailable; compatible fallback artwork is shown.";
                case "race-profile-fallback": case "requested-profile-fallback": case "default-profile-fallback":
                    return "Compatible fallback artwork is shown because the preferred art set is unavailable.";
                case "body-exempt": return "This pawn is exempt from custom body artwork; the saved choice is retained.";
                case "ineligible-body": return "This pawn currently uses native artwork because its body is ineligible.";
                case "native-fur-layer": return "This pawn's fur uses native body artwork; the saved choice is retained.";
                case "native-gene-body-layer": return "This pawn's body gene artwork uses native appearance; the saved choice is retained.";
                case "xenotype-base-renderer-unsupported": case "xenotype-fur-renderer-unsupported": case "xenotype-layer-unsupported":
                    return "RR_XenotypeArtwork_Unsupported".Translate();
                case "native-tattoo-layer": return "Showing this pawn's body tattoo uses native appearance; the saved art choice is retained.";
                case "native-mutant-body": return "This pawn's transformation uses native body artwork; the saved choice returns when the transformation ends.";
                case "native-apparel-fallback": return "A worn garment has no compatible texture; native appearance is shown and the saved choice is retained.";
                case "missing-anchor-definition": return "This art set's body placement data is unavailable; native appearance is shown.";
                case "native-global-default": return "The colony preference for this race uses native appearance.";
                case "global-default-unavailable": return "The colony art set is unavailable; the race's declared fallback is shown.";
                case "native-desiccation": case "native-corpse": return "The pawn's current condition uses native body artwork.";
                case "unknown-race": case "unsupported-adapter": case "no-compatible-profile":
                    return "No compatible body artwork is available for this race; native artwork is shown.";
                case "native-small-stage":
                    return "RR_Anthrosonae_NativeSmallStage".Translate();
                case "unsupported-coat": case "missing-coat-art":
                    return "RR_Anthrosonae_CoatArtworkUnavailable".Translate();
                case "missing-attachment-fit": case "invalid-attachment-stage-scale":
                case "invalid-attachment-offset": case "diagnostic-attachment-placement-required":
                    return "RR_Anthrosonae_AttachmentPlacementUnavailable".Translate();
                case "unsupported-body-layer": case "modified-body-layer": case "modified-coat-definition":
                case "unsupported-render-node-structure": case "modified-appearance-silhouette":
                case "attachment-identity-mismatch": case "incomplete-attachment-capture": case "ambiguous-attachment-layer":
                    return "RR_Anthrosonae_BodyFeaturesIncompatible".Translate();
                case "ambiguous-coat-layer": case "ambiguous-appearance-layer":
                case "incomplete-coat-layer": case "appearance-coat-mismatch":
                    return "RR_Anthrosonae_CoatFeaturesIncompatible".Translate();
                case "unsupported-appearance-api": case "appearance-palette-read-failed":
                case "unsupported-native-body-scale": case "appearance-capture-off-thread": case "unsupported-attachment-api":
                    return "RR_Anthrosonae_IntegrationUnavailable".Translate();
                case "unsupported-coat-material": case "unsupported-native-coat-body": case "unavailable-coat-projection":
                    return "RR_Anthrosonae_ColouringUnavailable".Translate();
                case "diagnostic-equal-palette-required": case "diagnostic-art-unavailable": case "anthrosonae-pilot-statue-unavailable":
                    return "RR_Anthrosonae_PreviewUnavailable".Translate();
                default: return "The saved choice is unavailable; native body artwork is shown.";
            }
        }
    }
}
