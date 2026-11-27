using System;
using System.Collections.Generic;
using RimRound.Comps;
using RimRound.Simulation;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.UI
{
    /// <summary>Colony body-perk catalogue and pawn-specific purchase state.</summary>
    public sealed class Dialog_BodyPerks : Window
    {
        private const float WindowWidth = 660f;
        private const float WindowHeight = 580f;
        private const float OuterMargin = 12f;
        private const float FooterHeight = 70f;
        private const float ScrollbarWidth = 18f;

        private readonly Pawn pawn;
        private Vector2 scrollPosition;

        public Dialog_BodyPerks(Pawn pawn)
        {
            this.pawn = pawn;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(WindowWidth, WindowHeight);

        public override void DoWindowContents(Rect rect)
        {
            FullnessAndDietStats_ThingComp comp = pawn == null
                ? null : pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || comp.PerkTree == null)
            {
                Widgets.Label(rect, "RR_TreeUi_MissingState".Translate());
                return;
            }

            Rect inner = rect.ContractedBy(OuterMargin);
            float viewWidth = Mathf.Max(100f, inner.width - ScrollbarWidth);
            float headerHeight = MeasureHeader(comp, inner.width);
            float footerY = inner.yMax - FooterHeight;

            DrawHeader(new Rect(inner.x, inner.y, inner.width, headerHeight), comp);

            Rect scrollRect = new Rect(inner.x, inner.y + headerHeight + 5f,
                inner.width, Mathf.Max(80f, footerY - (inner.y + headerHeight + 5f) - 5f));
            float contentHeight = MeasureContents(comp, viewWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(contentHeight, scrollRect.height));
            Widgets.BeginScrollView(scrollRect, ref scrollPosition, viewRect, true);
            DrawContents(new Rect(0f, 0f, viewWidth, contentHeight), comp);
            Widgets.EndScrollView();

            DrawFooter(new Rect(inner.x, footerY, inner.width, FooterHeight), comp);
        }

        private void DrawHeader(Rect rect, FullnessAndDietStats_ThingComp comp)
        {
            string title = "RR_TreeUi_Title".Translate(pawn.LabelShortCap).ToString();
            float titleHeight = Mathf.Max(26f, MeasureWrapped(title, rect.width, GameFont.Medium));
            DrawWrappedLabel(new Rect(rect.x, rect.y, rect.width, titleHeight), title, GameFont.Medium);

            float y = rect.y + titleHeight + 5f;
            BodyPerkTreeState state = comp.PerkTree;
            string spent = TryGetSpent(state, out double spentValue)
                ? FormatPoints(spentValue) : "RR_TreeUi_Unknown".Translate().ToString();
            string totals = "RR_TreeUi_PointTotals".Translate(
                FormatPoints(state.EarnedPoints), spent, FormatPoints(state.AvailablePoints)).ToString();
            float totalsHeight = Mathf.Max(20f, MeasureWrapped(totals, rect.width, GameFont.Small));
            DrawWrappedLabel(new Rect(rect.x, y, rect.width, totalsHeight), totals, GameFont.Small);
            y += totalsHeight + 3f;

            string stateText = comp.PerkTreeActive
                ? "RR_TreeUi_Enabled".Translate().ToString()
                : "RR_TreeUi_Disabled".Translate(comp.PerkTreeInactiveReason ?? string.Empty).ToString();
            float stateHeight = Mathf.Max(20f, MeasureWrapped(stateText, rect.width, GameFont.Small));
            DrawWrappedLabel(new Rect(rect.x, y, rect.width, stateHeight), stateText, GameFont.Small);
        }

        private float MeasureHeader(FullnessAndDietStats_ThingComp comp, float width)
        {
            string title = "RR_TreeUi_Title".Translate(pawn.LabelShortCap).ToString();
            float height = Mathf.Max(26f, MeasureWrapped(title, width, GameFont.Medium));
            string spent = TryGetSpent(comp.PerkTree, out double spentValue)
                ? FormatPoints(spentValue) : "RR_TreeUi_Unknown".Translate().ToString();
            string totals = "RR_TreeUi_PointTotals".Translate(
                FormatPoints(comp.PerkTree.EarnedPoints), spent,
                FormatPoints(comp.PerkTree.AvailablePoints)).ToString();
            height += 5f + Mathf.Max(20f, MeasureWrapped(totals, width, GameFont.Small)) + 3f;
            string stateText = comp.PerkTreeActive
                ? "RR_TreeUi_Enabled".Translate().ToString()
                : "RR_TreeUi_Disabled".Translate(comp.PerkTreeInactiveReason ?? string.Empty).ToString();
            return height + Mathf.Max(20f, MeasureWrapped(stateText, width, GameFont.Small));
        }

        private float MeasureContents(FullnessAndDietStats_ThingComp comp, float width)
        {
            float height = 4f;
            height += MeasureBranchHeader("RR_TreeUi_Branch_Physical".Translate().ToString(), width);
            height += MeasureNode(BodyPerkTreeCatalog.All[0], comp, width);
            height += MeasureNode(BodyPerkTreeCatalog.All[1], comp, width);
            height += MeasureNode(BodyPerkTreeCatalog.All[2], comp, width);
            height += MeasureNode(BodyPerkTreeCatalog.All[3], comp, width);
            height += MeasureBranchHeader("RR_TreeUi_Branch_Social".Translate().ToString(), width);
            height += MeasureNode(BodyPerkTreeCatalog.All[4], comp, width);
            height += MeasureNode(BodyPerkTreeCatalog.All[5], comp, width);
            height += MeasureBranchHeader("RR_TreeUi_Branch_Fantasy".Translate().ToString(), width);
            height += MeasureNode(BodyPerkTreeCatalog.All[6], comp, width);
            height += MeasureNode(BodyPerkTreeCatalog.All[7], comp, width);
            return height + 8f;
        }

        private void DrawContents(Rect rect, FullnessAndDietStats_ThingComp comp)
        {
            float y = rect.y + 4f;
            DrawBranchHeader(ref y, "RR_TreeUi_Branch_Physical".Translate().ToString(), rect.width);
            DrawNode(ref y, BodyPerkTreeCatalog.All[0], comp, rect.width);
            DrawNode(ref y, BodyPerkTreeCatalog.All[1], comp, rect.width);
            DrawNode(ref y, BodyPerkTreeCatalog.All[2], comp, rect.width);
            DrawNode(ref y, BodyPerkTreeCatalog.All[3], comp, rect.width);
            DrawBranchHeader(ref y, "RR_TreeUi_Branch_Social".Translate().ToString(), rect.width);
            DrawNode(ref y, BodyPerkTreeCatalog.All[4], comp, rect.width);
            DrawNode(ref y, BodyPerkTreeCatalog.All[5], comp, rect.width);
            DrawBranchHeader(ref y, "RR_TreeUi_Branch_Fantasy".Translate().ToString(), rect.width);
            DrawNode(ref y, BodyPerkTreeCatalog.All[6], comp, rect.width);
            DrawNode(ref y, BodyPerkTreeCatalog.All[7], comp, rect.width);
        }

        private void DrawBranchHeader(ref float y, string label, float width)
        {
            float height = MeasureBranchHeader(label, width);
            Rect header = new Rect(0f, y, width, height - 3f);
            DrawWrappedLabel(header, label, GameFont.Medium);
            Widgets.DrawLineHorizontal(0f, header.yMax - 2f, width);
            y += height;
        }

        private static float MeasureBranchHeader(string label, float width)
        {
            return Mathf.Max(27f, MeasureWrapped(label, width, GameFont.Medium)) + 3f;
        }

        private void DrawNode(ref float y, BodyPerkNodeDefinition node,
            FullnessAndDietStats_ThingComp comp, float width)
        {
            float xPad = 5f;
            float innerWidth = width - xPad * 2f;
            string displayName = TranslateNode(node);
            float titleHeight = MeasureNodeTitle(node, innerWidth);
            Rect titleRect = new Rect(xPad, y, innerWidth - 92f, titleHeight);
            DrawWrappedLabel(titleRect, displayName, GameFont.Medium);
            DrawWrappedLabel(new Rect(titleRect.xMax, y, 92f, titleHeight),
                "RR_TreeUi_Cost".Translate(node.Cost).ToString(), GameFont.Small);
            y += titleHeight + 1f;

            y = DrawWrappedAt(y, xPad, innerWidth, EffectText(node), GameFont.Small);
            List<string> requirements = BuildRequirementLines(node);
            for (int i = 0; i < requirements.Count; i++)
                y = DrawWrappedAt(y, xPad, innerWidth, requirements[i], GameFont.Tiny);

            if (HasPurchased(comp.PerkTree, node.Id))
            {
                string acquired = "RR_TreeUi_Acquired".Translate().ToString();
                y = DrawWrappedAt(y, xPad, innerWidth, acquired, GameFont.Small) + 2f;
            }
            else
            {
                string unavailable = comp.PerkPurchaseUnavailableReason(node.Id);
                Rect buttonRect = new Rect(xPad, y, 185f, 28f);
                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && unavailable == null;
                bool clicked = Widgets.ButtonText(buttonRect,
                    "RR_TreeUi_Purchase".Translate(node.Cost).ToString());
                GUI.enabled = wasEnabled;
                if (clicked)
                {
                    if (!comp.TryPurchaseBodyPerk(node.Id))
                        Messages.Message("RR_TreeUi_PurchaseFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                y += 30f;
                if (!string.IsNullOrEmpty(unavailable))
                    y = DrawWrappedAt(y, xPad, innerWidth, unavailable, GameFont.Tiny) + 2f;
            }

            y += 8f;
            Widgets.DrawLineHorizontal(xPad, y - 3f, innerWidth);
        }

        private float MeasureNode(BodyPerkNodeDefinition node,
            FullnessAndDietStats_ThingComp comp, float width)
        {
            float innerWidth = width - 10f;
            float height = MeasureNodeTitle(node, innerWidth) + 1f;
            height += MeasureWrapped(EffectText(node), innerWidth, GameFont.Small);
            List<string> requirements = BuildRequirementLines(node);
            for (int i = 0; i < requirements.Count; i++)
                height += MeasureWrapped(requirements[i], innerWidth, GameFont.Tiny);
            if (HasPurchased(comp.PerkTree, node.Id))
            {
                height += MeasureWrapped("RR_TreeUi_Acquired".Translate().ToString(),
                    innerWidth, GameFont.Small) + 2f;
            }
            else
            {
                height += 30f;
                string unavailable = comp.PerkPurchaseUnavailableReason(node.Id);
                if (!string.IsNullOrEmpty(unavailable))
                    height += MeasureWrapped(unavailable, innerWidth, GameFont.Tiny) + 2f;
            }
            return height + 8f;
        }

        private static float MeasureNodeTitle(BodyPerkNodeDefinition node, float innerWidth)
        {
            return Mathf.Max(MeasureWrapped(TranslateNode(node), innerWidth - 92f, GameFont.Medium),
                MeasureWrapped("RR_TreeUi_Cost".Translate(node.Cost).ToString(), 92f, GameFont.Small));
        }

        private void DrawFooter(Rect rect, FullnessAndDietStats_ThingComp comp)
        {
            Widgets.DrawLineHorizontal(rect.x, rect.y, rect.width);
            double spent;
            string refund = TryGetSpent(comp.PerkTree, out spent)
                ? FormatPoints(spent) : "RR_TreeUi_Unknown".Translate().ToString();
            Widgets.Label(new Rect(rect.x, rect.y + 5f, rect.width, 20f),
                "RR_TreeUi_RespecSummary".Translate(refund));

            string unavailable = comp.PerkRespecUnavailableReason;
            Rect buttonRect = new Rect(rect.x, rect.y + 29f, 190f, 30f);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && unavailable == null;
            bool clicked = Widgets.ButtonText(buttonRect, "RR_TreeUi_Respec".Translate());
            GUI.enabled = wasEnabled;
            if (clicked && !comp.TryRespecBodyPerks())
                Messages.Message("RR_TreeUi_RespecFailed".Translate(), MessageTypeDefOf.RejectInput, false);

            if (!string.IsNullOrEmpty(unavailable))
                DrawWrappedLabel(new Rect(buttonRect.xMax + 8f, rect.y + 28f,
                    Mathf.Max(0f, rect.width - buttonRect.width - 8f), 38f), unavailable, GameFont.Tiny);
        }

        private List<string> BuildRequirementLines(BodyPerkNodeDefinition node)
        {
            var lines = new List<string>();
            if (node.MinimumActiveDays > 0d)
                lines.Add("RR_TreeUi_ReqActiveDays".Translate(node.MinimumActiveDays).ToString());
            if (node.MinimumHabitConditioning > 0f)
                lines.Add("RR_TreeUi_ReqConditioning".Translate(node.MinimumHabitConditioning * 100f).ToString());
            if (node.MinimumCompletedBodyDiscussions > 0)
                lines.Add("RR_TreeUi_ReqDiscussions".Translate(node.MinimumCompletedBodyDiscussions).ToString());
            if (node.RequiresExceptionalAdaptationResearch)
                lines.Add("RR_TreeUi_ReqResearch".Translate().ToString());
            if (node.FantasyOnly)
                lines.Add("RR_TreeUi_ReqFantasy".Translate().ToString());
            if (node.Prerequisites.Count > 0)
                lines.Add("RR_TreeUi_Prerequisites".Translate(JoinNodeNames(node.Prerequisites)).ToString());
            if (node.Exclusions.Count > 0)
                lines.Add("RR_TreeUi_Exclusions".Translate(JoinNodeNames(node.Exclusions)).ToString());
            return lines;
        }

        private static string JoinNodeNames(IReadOnlyList<string> ids)
        {
            var names = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                BodyPerkNodeDefinition definition;
                if (BodyPerkTreeCatalog.TryGet(ids[i], out definition))
                    names.Add(TranslateNode(definition));
                else
                    names.Add(ids[i]);
            }
            return string.Join(", ", names.ToArray());
        }

        private static string TranslateNode(BodyPerkNodeDefinition node)
        {
            string key = "RR_TreeUi_Node_" + node.Id;
            return key.Translate().ToString();
        }

        private static string EffectText(BodyPerkNodeDefinition node)
        {
            return ("RR_TreeUi_Effect_" + node.Id).Translate().ToString();
        }

        private float DrawWrappedAt(float y, float x, float width, string text, GameFont font)
        {
            float height = MeasureWrapped(text, width, font);
            DrawWrappedLabel(new Rect(x, y, width, height), text, font);
            return y + height;
        }

        private static float MeasureWrapped(string text, float width, GameFont font)
        {
            GameFont oldFont = Text.Font;
            bool oldWrap = Text.WordWrap;
            Text.Font = font;
            Text.WordWrap = true;
            float height = Mathf.Max(Text.LineHeight, Text.CalcHeight(text ?? string.Empty, width));
            Text.WordWrap = oldWrap;
            Text.Font = oldFont;
            return height + 1f;
        }

        private static void DrawWrappedLabel(Rect rect, string text, GameFont font)
        {
            GameFont oldFont = Text.Font;
            bool oldWrap = Text.WordWrap;
            Text.Font = font;
            Text.WordWrap = true;
            Widgets.Label(rect, text ?? string.Empty);
            Text.WordWrap = oldWrap;
            Text.Font = oldFont;
        }

        private static bool HasPurchased(BodyPerkTreeState state, string nodeId)
        {
            if (state == null || state.PurchasedNodeIds == null) return false;
            for (int i = 0; i < state.PurchasedNodeIds.Count; i++)
                if (string.Equals(state.PurchasedNodeIds[i], nodeId, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool TryGetSpent(BodyPerkTreeState state, out double spent)
        {
            spent = 0d;
            if (state == null || state.PurchasedRawCosts == null || state.PurchasedNodeIds == null ||
                state.PurchasedRawCosts.Count != state.PurchasedNodeIds.Count) return false;
            for (int i = 0; i < state.PurchasedRawCosts.Count; i++)
            {
                if (state.PurchasedRawCosts[i] < 0) return false;
                spent += state.PurchasedRawCosts[i];
            }
            return !double.IsNaN(spent) && !double.IsInfinity(spent);
        }

        private static string FormatPoints(double points)
        {
            return double.IsNaN(points) || double.IsInfinity(points)
                ? "RR_TreeUi_Unknown".Translate().ToString()
                : points.ToString("0.##");
        }
    }
}
