using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimRound.Comps;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    public enum ColonyPreset { Balanced, Fantasy, Legacy }

    // Colony choices belong to the save, never to the last opened colony's globals.
    public sealed class ColonyProgression : GameComponent
    {
        // Missing GameComponents are constructed after loading older saves and
        // do not necessarily receive ExposeData. Their constructor must be safe.
        private ColonyPreset preset = ColonyPreset.Legacy;
        private int schemaVersion;
        private bool bodyPerkTreeEnabled;
        public bool BodyPerkTreeEnabled => bodyPerkTreeEnabled;
        public static bool BodyPerksActive => Instance?.bodyPerkTreeEnabled == true && !LegacyPerksActive;
        public ColonyProgression(Game game) { }
        public ColonyPreset Preset => preset;
        public static ColonyProgression Instance => Current.Game?.GetComponent<ColonyProgression>();
        public static bool LegacyPerksActive => Instance?.preset == ColonyPreset.Legacy;
        public static bool FantasyActive => Instance?.preset == ColonyPreset.Fantasy;

        public override void StartedNewGame()
        {
            preset = ColonyPreset.Balanced;
            schemaVersion = 1;
            // New colonies opt in by default; old saves retain their stored choice.
            bodyPerkTreeEnabled = true;
        }

        public override void LoadedGame()
        {
            // FillComponents may have added this after deserialization.
            if (schemaVersion == 0) schemaVersion = 1;
            ResearchProjectDef research = Find.ResearchManager?.GetProject();
            if (research != null && !SpecialistContentPolicy.Allows(research))
                Find.ResearchManager.StopProject(research);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref schemaVersion, "rrProgressionSchema", 0);
            Scribe_Values.Look(ref bodyPerkTreeEnabled, "rrBodyPerkTreeEnabled", false);
            // An existing colony with no new keys keeps its purchases active.
            Scribe_Values.Look(ref preset, "rrColonyPreset", ColonyPreset.Legacy);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (schemaVersion == 0 || !Enum.IsDefined(typeof(ColonyPreset), preset))
                    preset = ColonyPreset.Legacy;
                schemaVersion = 1;
            }
        }

        public bool TrySetPreset(ColonyPreset next, out string reason)
        {
            reason = null;
            if (!Enum.IsDefined(typeof(ColonyPreset), next)) return false;
            if (preset == next) return true;
            List<Pawn> pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive.ToList();
            if (preset == ColonyPreset.Legacy)
            {
                Pawn active = pawns.FirstOrDefault(p =>
                {
                    var comp = p.TryGetComp<MakeBlobIntoBed_ThingComp>();
                    return comp != null && comp.HasLegacyOnlyTransformation;
                });
                if (active != null)
                {
                    reason = "RR_ProgressionFinishLegacyTransformation".Translate(active.LabelShort);
                    return false;
                }
            }
            preset = next;
            schemaVersion = 1;
            Ideology.BodyRoleExpectations.Instance?.Reconcile();
            ResearchProjectDef research = Find.ResearchManager?.GetProject();
            if (research != null && !SpecialistContentPolicy.Allows(research))
                Find.ResearchManager.StopProject(research);
            foreach (Pawn pawn in pawns)
            {
                // Preserved perk data is never rewritten or replayed here.
                pawn.health?.hediffSet.DirtyCache();
                pawn.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty();
            }
            return true;
        }

        public void SetBodyPerkTreeEnabled(bool enabled)
        {
            if (bodyPerkTreeEnabled == enabled) return;
            bodyPerkTreeEnabled = enabled;
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                pawn.health?.hediffSet.DirtyCache();
        }

        public static bool IsLegacyPerkHediff(HediffDef def)
        {
            string name = def?.defName;
            return name == "RimRound_HediffHealthyFrame" ||
                name == "RimRound_HediffFoodIsFood" ||
                name == "RimRound_HediffGenevaSuggestion";
        }

        public static void DrawSettings(Rect rect)
        {
            DrawSettingsContent(rect);
        }

        public static float DrawSettingsContent(Rect rect)
        {
            var listing = new Listing_Standard();
            listing.Begin(rect.ContractedBy(16f));
            listing.Label("RR_ProgressionTitle".Translate());
            listing.GapLine();
            if (Instance == null)
                listing.Label("RR_ProgressionLoadColony".Translate());
            else
            {
                listing.Label("RR_ProgressionCurrent".Translate(("RR_Preset" + Instance.preset).Translate()));
                listing.Gap();
                foreach (ColonyPreset choice in Enum.GetValues(typeof(ColonyPreset)))
                {
                    listing.Label(("RR_Preset" + choice + "Description").Translate());
                    if (listing.ButtonText(("RR_Preset" + choice).Translate()))
                    {
                        ColonyPreset selected = choice;
                        if (selected != Instance.preset)
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                                "RR_ProgressionSwitchWarning".Translate(), delegate
                                {
                                    string reason;
                                    if (!Instance.TrySetPreset(selected, out reason))
                                        Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                                }));
                    }
                    listing.Gap();
                }
                listing.GapLine();
                listing.Label("RR_ProgressionRoutes".Translate());
                listing.GapLine();
                bool treeEnabled = Instance.bodyPerkTreeEnabled;
                if (LegacyPerksActive)
                {
                    Rect row = listing.GetRect(Mathf.Max(28f, Text.CalcHeight("RR_TreeEnable".Translate(), rect.width - 60f) + 4f));
                    Widgets.CheckboxLabeled(row, "RR_TreeEnable".Translate(), ref treeEnabled, disabled: true);
                    TooltipHandler.TipRegion(row, "RR_TreeEnableDesc".Translate());
                    listing.Label("RR_SettingsTreeDormant".Translate());
                }
                else listing.CheckboxLabeled("RR_TreeEnable".Translate(), ref treeEnabled, "RR_TreeEnableDesc".Translate());
                Instance.SetBodyPerkTreeEnabled(treeEnabled);
            }
            float height = listing.CurHeight + 32f;
            listing.End();
            return height;
        }
    }

    // Keep the saved hediff identity and severity. Its bonus is dormant outside Legacy.
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.CurStage), MethodType.Getter)]
    internal static class LegacyPerkStageGate
    {
        private static void Postfix(Hediff __instance, ref HediffStage __result)
        {
            if (!ColonyProgression.LegacyPerksActive && ColonyProgression.IsLegacyPerkHediff(__instance.def))
                __result = null;
        }
    }

    [HarmonyPatch(typeof(Hediff), nameof(Hediff.Visible), MethodType.Getter)]
    internal static class LegacyPerkVisibilityGate
    {
        private static void Postfix(Hediff __instance, ref bool __result)
        {
            if (!ColonyProgression.LegacyPerksActive && ColonyProgression.IsLegacyPerkHediff(__instance.def))
                __result = false;
        }
    }

    // Only change the lookup inside thought nullification. Other hediff lookups,
    // other mods' nullifiers and the native probability/ideology checks stay native.
    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.ThoughtNullifyingHediff))]
    internal static class LegacyPerkThoughtGate
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var lookup = AccessTools.Method(typeof(HediffSet), nameof(HediffSet.GetFirstHediffOfDef),
                new[] { typeof(HediffDef), typeof(bool) });
            var replacement = AccessTools.Method(typeof(LegacyPerkThoughtGate), nameof(FindActiveHediff));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(lookup))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
                yield return instruction;
            }
        }
        private static Hediff FindActiveHediff(HediffSet set, HediffDef def, bool visible)
        {
            return !ColonyProgression.LegacyPerksActive && ColonyProgression.IsLegacyPerkHediff(def)
                ? null : set.GetFirstHediffOfDef(def, visible);
        }
    }
}
