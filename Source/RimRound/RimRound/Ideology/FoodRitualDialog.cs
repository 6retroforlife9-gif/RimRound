using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Ideology
{
    [HarmonyPatch(typeof(RitualRoleAssignments), nameof(RitualRoleAssignments.RoleGroups))]
    internal static class TableFeastRoleGroups
    {
        private static void Postfix(RitualRoleAssignments __instance, TargetInfo ___ritualTarget,
            ref IEnumerable<IGrouping<string, RitualRole>> __result)
        {
            if (__instance.Ritual?.def.defName != CommunalFeast.RitualDefName || ___ritualTarget.Thing is Building_Bed) return;
            // Keep the underlying role definitions for saves and integrations.
            // Only hide unused bedside rows for a normal table gathering.
            __result = __result.Where(group => group.Any(role =>
                !(role is RitualRole_AbundanceHost || role is RitualRole_AbundanceAttendant) || __instance.AnyPawnAssigned(role)));
        }
    }

    [HarmonyPatch(typeof(Dialog_BeginLordJob), nameof(Dialog_BeginLordJob.DoButtonRow))]
    internal static class FoodRitualRequirements
    {
        private sealed class ScrollState
        {
            public Vector2 Position;
        }

        private static readonly ConditionalWeakTable<Dialog_BeginRitual, ScrollState> scroll =
            new ConditionalWeakTable<Dialog_BeginRitual, ScrollState>();
        private static readonly Func<Dialog_BeginRitual, IEnumerable<string>> blockingIssues =
            AccessTools.MethodDelegate<Func<Dialog_BeginRitual, IEnumerable<string>>>(
                AccessTools.DeclaredMethod(typeof(Dialog_BeginRitual), "BlockingIssues"));

        private static bool Prefix(Dialog_BeginLordJob __instance, ref RectDivider layout, bool canBegin)
        {
            if (canBegin || !(__instance is Dialog_BeginRitual dialog) ||
                !AbundanceRituals.IsFoodRitual((Precept_Ritual)AbundanceRoleAppointment.DialogRitual.GetValue(dialog)))
                return true;

            // The native footer centers its warning between two 200-wide buttons.
            // Reserve a separate full-width row so wrapped requirements stay visible.
            RectDivider buttons = layout.NewRow(40f, VerticalJustification.Bottom, 5f);
            float buttonWidth = Mathf.Min(200f, (buttons.Rect.width - 10f) / 2f);
            Rect begin = buttons.NewCol(buttonWidth, HorizontalJustification.Right, 10f);
            Rect cancel = buttons.NewCol(buttonWidth);
            using (new TextBlock(Color.gray))
                Widgets.ButtonText(begin, dialog.OkButtonLabel, active: false);
            if (Widgets.ButtonText(cancel, dialog.CancelButtonLabel))
                dialog.Close();

            string message = string.Join("\n", blockingIssues(dialog).Where(issue => !issue.NullOrEmpty()).Distinct());
            float textWidth = layout.Rect.width - 24f;
            float textHeight = Text.CalcHeight(message, textWidth);
            float visibleHeight = Mathf.Min(textHeight, Text.LineHeight * 3f);
            Rect panel = layout.NewRow(visibleHeight + 10f, VerticalJustification.Bottom, 5f);
            Widgets.DrawBoxSolid(panel, new Color(0.19f, 0.15f, 0.12f));
            Widgets.DrawBoxSolid(new Rect(panel.x, panel.y, 3f, panel.height), new Color(0.93f, 0.69f, 0.36f));
            Rect content = new Rect(panel.x + 12f, panel.y + 5f, textWidth, visibleHeight);
            using (new TextBlock(new Color(1f, 0.91f, 0.76f)))
            {
                if (textHeight <= visibleHeight)
                    Widgets.Label(content, message);
                else
                {
                    float width = content.width - 16f;
                    Rect view = new Rect(0f, 0f, width, Text.CalcHeight(message, width));
                    ScrollState state = scroll.GetOrCreateValue(dialog);
                    Widgets.BeginScrollView(content, ref state.Position, view);
                    Widgets.Label(view, message);
                    Widgets.EndScrollView();
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.DrawExtraOutcomeDescriptions))]
    internal static class FoodRitualPlanDetails
    {
        private static void Postfix(Rect viewRect, ref float curY, ref float totalInfoHeight,
            TargetInfo ___target, Precept_Ritual ___ritual, RitualRoleAssignments ___assignments)
        {
            if (!AbundanceRituals.IsFoodRitual(___ritual)) return;
            string details = FeastDiagnostics.Explanation(___assignments);
            string transport = RitualAttendanceAdapters.Preview(___target, ___ritual, ___assignments);
            if (!transport.NullOrEmpty()) details += "\n\n" + transport;
            float height = Text.CalcHeight(details, viewRect.width);
            Widgets.Label(new Rect(viewRect.x, curY + 8f, viewRect.width, height), details);
            curY += height + 8f;
            totalInfoHeight += height + 8f;
        }
    }
}
