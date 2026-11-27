using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Ideology
{
    // This is a supported building choice, not a second demanded building.
    // Precept_ThingStyle supplies the native saved name, ThingDef and icon
    // contracts without Precept_Building's extra presence demand or ownership
    // of the constructed thing's style. Actual ritual use stays on the bed comp.
    public sealed class Precept_RbaRitualBed : Precept_ThingStyle
    {
        public override bool UsesGeneratedName => false;
        public override bool CanRegenerate => false;
        public override string UIInfoFirstLine => string.IsNullOrWhiteSpace(Label) ? RbaBuildings.DefaultName : LabelCap;
        public override string UIInfoSecondLine => RbaBuildings.BedTypeLabel;
        public override string TipLabel => "RR_RbaBuildingTitle".Translate(RbaBuildings.BedTypeLabel);
        public override Texture2D Icon => ThingDef.uiIcon;

        public override void Init(Ideo ideo, FactionDef generatingFor = null)
        {
            // Native generation may initialize precepts twice. Never replace
            // the identity or a name already edited/imported by the player.
            if (Id >= 0 && ThingDef != null) { this.ideo = ideo; return; }
            base.Init(ideo, generatingFor);
            SetName(RbaBuildings.DefaultName);
        }

        // Selecting a supported existing type must not reset an ideology's
        // saved bed style. This card does not create a new style variant.
        protected override void Notify_ThingDefSet() { }

        public override string GenerateNameRaw() => "RR_RbaBuildingNameSuggestion".Translate();

        public override string GetTip()
        {
            var text = new StringBuilder(Description);
            if (!string.IsNullOrWhiteSpace(Label) && Label != RbaBuildings.DefaultName)
                text.AppendLine().AppendLine().Append("RR_RbaCeremonialName".Translate(Label));
            var demand = RbaBuildings.AltarDemand(ideo);
            text.AppendLine().AppendLine().Append("RR_RbaBuildingOptional".Translate());
            if (demand != null)
            {
                text.AppendLine().AppendLine().Append("RR_RbaSharedDemand".Translate(
                    demand.parent.LabelCap, demand.minExpectation.LabelCap));
                if (demand.roomRequirements != null)
                    foreach (RoomRequirement requirement in demand.roomRequirements.Where(r => !AbundanceBuildingDemands.NoBeds(r)))
                        text.AppendLine().Append(" - ").Append(requirement.LabelCap(null));
                text.AppendLine().Append("RR_RbaBedAlternativeRequirements".Translate());
            }
            text.AppendLine().AppendLine().Append("RR_RbaBuildingOpen".Translate());
            return text.ToString();
        }

        public override IEnumerable<FloatMenuOption> EditFloatMenuOptions() => MenuOptions(true);

        private IEnumerable<FloatMenuOption> MenuOptions(bool canRename)
        {
            if (canRename) yield return EditFloatMenuOption();
            yield return new FloatMenuOption("RR_RbaBuildingInformation".Translate(RbaBuildings.BedTypeLabel),
                () => Find.WindowStack.Add(new Dialog_InfoCard(ThingDef, GenStuff.DefaultStuffFor(ThingDef))));
            if (Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null)
            {
                Designator_Build designator = BuildCopyCommandUtility.FindAllowedDesignator(ThingDef);
                if (designator == null)
                    yield return new FloatMenuOption("RR_RbaBuildingUnavailable".Translate(), null);
                else
                    yield return new FloatMenuOption("RR_RbaBuildingConstruct".Translate(RbaBuildings.BedTypeLabel), () =>
                    {
                        // Use the ordinary material chooser and placement rules;
                        // this does not build, claim or designate the bed.
                        Find.MainTabsRoot.EscapeCurrentTab();
                        designator.ProcessInput(Event.current);
                    });
            }
        }

        public override void DrawPreceptBox(Rect preceptBox, IdeoEditMode editMode, bool forceHighlight = false)
        {
            base.DrawPreceptBox(preceptBox, editMode, forceHighlight);
            if (editMode == IdeoEditMode.None && Widgets.ButtonInvisible(preceptBox))
                Find.WindowStack.Add(new FloatMenu(MenuOptions(Current.ProgramState == ProgramState.Playing &&
                    Faction.OfPlayer?.ideos?.Has(ideo) == true).ToList()));
        }
    }

    public sealed class PreceptWorker_RbaRitualBed : PreceptWorker
    {
        public override AcceptanceReport CanUse(ThingDef def, Ideo ideo, FactionDef generatingFor) =>
            def?.defName == RbaBuildings.BedDefName;
    }

    public static class RbaBuildings
    {
        public const string PreceptDefName = "RR_RitualBedBuilding";
        public const string BedDefName = "NotRegalBed";
        public static string DefaultName => "RR_RbaRitualBed".Translate();
        public static string BedTypeLabel => DefDatabase<ThingDef>.GetNamedSilentFail(BedDefName)?.LabelCap.ToString() ?? "RR_RbaBedType".Translate();
        public static Precept_RbaRitualBed Entry(Ideo ideo) =>
            ideo?.PreceptsListForReading.OfType<Precept_RbaRitualBed>().FirstOrDefault();

        public static bool HasActiveRitual(Ideo ideo) => ideo != null && Current.Game != null && Find.World != null && Find.IdeoManager != null &&
            Find.Maps != null && Find.Maps.Any(map => Find.IdeoManager.GetActiveRituals(map).Any(r => r.Ritual?.ideo == ideo));

        public static bool DeferReform(Ideo ideo, Ideo proposed) => HasActiveRitual(ideo) &&
            (AbundanceRoles.Enabled(ideo) || AbundanceRoles.Enabled(proposed) ||
                ideo?.memes?.Any(m => m.defName == "RR_FoodSupremacy") == true ||
                proposed?.memes?.Any(m => m.defName == "RR_FoodSupremacy") == true);

        public static IdeoBuildingPresenceDemand AltarDemand(Ideo ideo) => ideo?.PreceptsListForReading
            .OfType<Precept_Building>().FirstOrDefault(p => p.ThingDef?.isAltar == true)?.presenceDemand;

        public static void Reconcile(Ideo ideo)
        {
            if (!AbundanceRoles.Enabled(ideo) || Scribe.mode != LoadSaveMode.Inactive || HasActiveRitual(ideo)) return;
            var entries = ideo.PreceptsListForReading.OfType<Precept_RbaRitualBed>().ToList();
            if (entries.Count == 0)
            {
                var def = DefDatabase<PreceptDef>.GetNamedSilentFail(PreceptDefName);
                if (def != null) ideo.AddPrecept(PreceptMaker.MakePrecept(def), true);
                return;
            }
            // Keep the named instance if a malformed/imported ideology contains
            // duplicates. No physical thing uses this entry as its style source.
            var keep = entries.OrderByDescending(p => !string.IsNullOrWhiteSpace(p.Label) && p.Label != DefaultName)
                .ThenBy(p => p.Id).First();
            foreach (var duplicate in entries.Where(p => p != keep)) ideo.RemovePrecept(duplicate);
            if (keep.Id < 0 || keep.ThingDef == null) keep.Init(ideo);
            // On removal of EA retain this inert name record, but hide its card.
            // Re-adding EA therefore restores the same edited name and identity.
            // It has no effects, demand, ritual target, or constructed furniture.
        }
    }
}
