using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.UI
{
    public enum CoreSettingsPage { Bodies, Appearance, Metabolism, Interface, Health, Sound }

    public static partial class CoreSettingsUI
    {
        private enum Effect { None, Presentation, Health, Movement, WeightRange }
        private sealed class Setting
        {
            public readonly FieldInfo Field;
            public readonly CoreSettingsPage Page;
            public readonly string Label, Tip;
            public readonly Effect ChangeEffect;
            public bool LegacyOnly => Field.Name == "stomachElasticityMultiplier" ||
                Field.Name == "nutritionPerPerkLevel" || Field.Name == "levelsGainedPerLevel" ||
                Field.Name == "showPerkTab" || Field.Name == "showAllPerks";
            public Setting(CoreSettingsPage page, string field, string label, string tip, Effect effect)
            {
                Page = page; Field = typeof(GlobalSettings).GetField(field);
                Label = label; Tip = tip; ChangeEffect = effect;
            }
        }

        public static IEnumerable<string> FieldsFor(CoreSettingsPage page) =>
            Settings.Where(s => s.Page == page).Select(s => s.Field.Name);

        public static float Draw(CoreSettingsPage page, Rect rect, out bool changed)
        {
            changed = false;
            float y = 0f;
            Text.Font = GameFont.Small;
            foreach (Setting setting in Settings.Where(s => s.Page == page))
            {
                string label = setting.Label.Translate();
                if (setting.LegacyOnly) label += "  (" + "RR_SettingsLegacyOnly".Translate() + ")";
                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && (!setting.LegacyOnly || Current.Game == null || ColonyProgression.LegacyPerksActive);
                object old = setting.Field.GetValue(null);
                bool isBoolean = old is bool;
                float labelWidth = rect.width - (isBoolean ? 36f : 116f);
                float height = Mathf.Max(32f, Text.CalcHeight(label, labelWidth) + 8f);
                Rect row = new Rect(rect.x, rect.y + y, rect.width, height);
                object next = old;
                bool fieldChanged;
                if (isBoolean)
                {
                    bool value = (bool)old;
                    Widgets.CheckboxLabeled(row, label, ref value, disabled: !GUI.enabled);
                    next = value; fieldChanged = value != (bool)old;
                }
                else
                {
                    Widgets.Label(new Rect(row.x, row.y + 3f, labelWidth, height), label);
                    Rect number = new Rect(row.xMax - 104f, row.y + 2f, 104f, 28f);
                    if (old is NumericFieldData<int>)
                    {
                        var value = (NumericFieldData<int>)old;
                        int before = value.threshold;
                        Widgets.TextFieldNumeric(number, ref value.threshold, ref value.stringBuffer, value.min, value.max);
                        fieldChanged = before != value.threshold; next = value;
                    }
                    else
                    {
                        var value = (NumericFieldData<float>)old;
                        float before = value.threshold;
                        Widgets.TextFieldNumeric(number, ref value.threshold, ref value.stringBuffer, value.min, value.max);
                        fieldChanged = before != value.threshold; next = value;
                    }
                }
                // Keep editing buffers even when the parsed number has not changed.
                setting.Field.SetValue(null, next);
                TooltipHandler.TipRegion(row, setting.Tip.Translate());
                GUI.enabled = wasEnabled;
                if (fieldChanged) { changed = true; Notify(setting.ChangeEffect); }
                y += height + 8f;
            }
            if (GlobalSettings.minimumAgeForCustomBody.threshold > GlobalSettings.maximumAgeForCustomBody.threshold)
            {
                GlobalSettings.maximumAgeForCustomBody.threshold = GlobalSettings.minimumAgeForCustomBody.threshold;
                GlobalSettings.maximumAgeForCustomBody.stringBuffer = null;
            }
            return y;
        }

        public static void ResetPage(CoreSettingsPage page)
        {
            foreach (Setting setting in Settings.Where(s => s.Page == page))
                setting.Field.SetValue(null, CoreSettingsDefaults.FactoryValue(setting.Field.Name));
            foreach (Effect effect in Settings.Where(s => s.Page == page).Select(s => s.ChangeEffect).Distinct())
                Notify(effect);
        }

        private static void Notify(Effect effect)
        {
            if (effect == Effect.WeightRange && GlobalSettings.minWeight.threshold >= GlobalSettings.maxWeight.threshold)
            {
                GlobalSettings.minWeight = (NumericFieldData<int>)CoreSettingsDefaults.FactoryValue(nameof(GlobalSettings.minWeight));
                GlobalSettings.maxWeight = (NumericFieldData<int>)CoreSettingsDefaults.FactoryValue(nameof(GlobalSettings.maxWeight));
            }
            if (Current.Game == null) return;
            if (effect == Effect.Presentation) BodyTypeUtility.AssignBodyTypeCategoricalExemptions(true);
            if (effect == Effect.Health || effect == Effect.Movement)
                foreach (Pawn pawn in GeneralUtility.GetAllGlobalHumanlikePawns())
                {
                    pawn.health?.hediffSet.DirtyCache();
                    if (effect == Effect.Movement && pawn.WeightHediff() != null)
                        pawn.health.Notify_HediffChanged(pawn.WeightHediff());
                }
        }
    }
}
