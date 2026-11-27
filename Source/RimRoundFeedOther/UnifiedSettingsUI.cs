using System;
using HarmonyLib;
using RimRound.UI;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.FeedOther
{
    public static class UnifiedSettingsUI
    {
        private sealed class Page
        {
            public string Label;
            public CoreSettingsPage? Core;
            public FeedOtherSettingsPage? Feeding;
            public bool Progression, Races;
            public Vector2 Scroll;
            public float Height = 900f;
        }

        private static readonly Page[] Pages =
        {
            new Page { Label = "RR_SettingsBodies", Core = CoreSettingsPage.Bodies },
            new Page { Label = "RR_SettingsAppearance", Core = CoreSettingsPage.Appearance },
            new Page { Label = "RR_SettingsMetabolism", Core = CoreSettingsPage.Metabolism },
            new Page { Label = "RR_SettingsInterface", Core = CoreSettingsPage.Interface },
            new Page { Label = "RR_SettingsHealth", Core = CoreSettingsPage.Health },
            new Page { Label = "RR_SettingsSound", Core = CoreSettingsPage.Sound },
            new Page { Label = "RR_SettingsProgression", Progression = true },
            new Page { Label = "RR_SettingsRaces", Races = true },
            new Page { Label = "RR_FeedOtherSettingsTabFeeding", Feeding = FeedOtherSettingsPage.Feeding },
            new Page { Label = "RR_FeedOtherSettingsTabCooldowns", Feeding = FeedOtherSettingsPage.Cooldowns },
            new Page { Label = "RR_FeedOtherSettingsTabSocial", Feeding = FeedOtherSettingsPage.Social },
            new Page { Label = "RR_FeedOtherSettingsTabPawnBehaviour", Feeding = FeedOtherSettingsPage.PawnBehaviour },
            new Page { Label = "RR_FeedOtherSettingsTabPrisoners", Feeding = FeedOtherSettingsPage.Prisoners },
            new Page { Label = "RR_FeedOtherSettingsTabWorld", Feeding = FeedOtherSettingsPage.WorldGeneration },
            new Page { Label = "RR_FeedOtherSettingsTabFoodNetwork", Feeding = FeedOtherSettingsPage.FoodNetwork }
        };
        private static int currentPage;
        private static Vector2 navigationScroll;

        public static bool Draw(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            try
            {
                CoreSettingsDefaults.EnsureMenuContext();
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                float navigationWidth = Mathf.Clamp(inRect.width * .24f, 144f, 190f);
                Rect navigation = new Rect(inRect.x, inRect.y, navigationWidth, inRect.height);
                Widgets.DrawMenuSection(navigation);
                Rect navInner = navigation.ContractedBy(6f);
                float navWidth = navInner.width - 16f;
                float[] rowHeights = new float[Pages.Length];
                float navHeight = 0f;
                for (int i = 0; i < Pages.Length; i++)
                {
                    rowHeights[i] = Mathf.Max(32f, Text.CalcHeight(Pages[i].Label.Translate(), navWidth - 8f) + 10f);
                    navHeight += rowHeights[i] + 4f;
                }
                Rect navView = new Rect(0, 0, navWidth, navHeight);
                Widgets.BeginScrollView(navInner, ref navigationScroll, navView);
                float navY = 0f;
                for (int i = 0; i < Pages.Length; i++)
                {
                    Rect button = new Rect(0, navY, navView.width, rowHeights[i]);
                    navY += rowHeights[i] + 4f;
                    if (i == currentPage) Widgets.DrawHighlightSelected(button);
                    if (Widgets.ButtonText(button, Pages[i].Label.Translate(), drawBackground: false))
                    {
                        currentPage = i;
                        GUI.FocusControl(null);
                    }
                }
                Widgets.EndScrollView();

                Page page = Pages[currentPage];
                Rect panel = new Rect(navigation.xMax + 12f, inRect.y,
                    inRect.width - navigationWidth - 12f, inRect.height);
                Widgets.DrawMenuSection(panel);
                Rect content = panel.ContractedBy(12f);
                Text.Font = GameFont.Medium;
                string heading = page.Label.Translate();
                float headingHeight = Mathf.Max(32f, Text.CalcHeight(heading, content.width));
                Widgets.Label(new Rect(content.x, content.y, content.width, headingHeight), heading);
                Text.Font = GameFont.Small;
                string scope = (page.Feeding.HasValue ? "RR_SettingsGlobalScope" :
                    Current.Game != null ? "RR_SettingsColonyScope" :
                    page.Core.HasValue ? "RR_SettingsDefaultScope" : "RR_SettingsLoadColony").Translate();
                float scopeHeight = Text.CalcHeight(scope, content.width) + 12f;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(content.x, content.y + headingHeight, content.width, scopeHeight), scope);
                GUI.color = oldColor;
                bool canReset = page.Core.HasValue || page.Feeding.HasValue;
                float top = content.y + headingHeight + scopeHeight;
                Rect scroll = new Rect(content.x, top, content.width,
                    Mathf.Max(40f, content.yMax - top - (canReset ? 44f : 0f)));
                Rect view = new Rect(0, 0, scroll.width - 18f, Mathf.Max(scroll.height, page.Height));
                Widgets.BeginScrollView(scroll, ref page.Scroll, view);
                bool changed = false;
                float height = 0f;
                Rect body = new Rect(0, 0, view.width, Mathf.Max(view.height, 3000f));
                if (page.Core.HasValue) height = CoreSettingsUI.Draw(page.Core.Value, body, out changed);
                else if (page.Feeding.HasValue) height = FeedOtherSettingsUI.DrawPageContents(body, page.Feeding.Value, out changed);
                else if (Current.Game != null && page.Races) height = MainTabWindow_RRGlobalSettings.DrawRaceSettings(body);
                else if (Current.Game != null && page.Progression)
                {
                    height = ColonyProgression.DrawSettingsContent(body);
                }
                Widgets.EndScrollView();
                page.Height = height + 8f;

                bool saveGlobal = changed && page.Feeding.HasValue;
                if (changed && page.Core.HasValue && Current.Game == null)
                {
                    CaptureBodyDefaults();
                    saveGlobal = true;
                }
                if (canReset && Widgets.ButtonText(new Rect(content.x, content.yMax - 34f,
                    Mathf.Min(210f, content.width), 30f), "RR_SettingsResetPage".Translate()))
                {
                    Page selected = page;
                    Game selectedGame = Current.Game;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "RR_SettingsResetConfirm".Translate(page.Label.Translate()), () =>
                        {
                            if (!ReferenceEquals(selectedGame, Current.Game)) return;
                            if (selected.Core.HasValue)
                            {
                                CoreSettingsUI.ResetPage(selected.Core.Value);
                                if (Current.Game == null) { CaptureBodyDefaults(); FeedOtherMod.SaveSettings(); }
                            }
                            else
                            {
                                FeedOtherMod.Settings.ResetPage(selected.Feeding.Value);
                                FeedOtherMod.SaveSettings();
                            }
                        }));
                }
                return saveGlobal;
            }
            finally
            {
                Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; GUI.enabled = oldEnabled;
            }
        }

        private static void CaptureBodyDefaults()
        {
            CoreSettingsDefaults.CaptureDefaults(out FeedOtherMod.Settings.coreBoolDefaults,
                out FeedOtherMod.Settings.coreIntDefaults, out FeedOtherMod.Settings.coreFloatDefaults);
        }
    }

    // Vanilla's fixed 900x700 dialog can exceed the logical screen at larger
    // UI scales. Limit only our dialog; both panes already scroll independently.
    [HarmonyPatch(typeof(Dialog_ModSettings), nameof(Dialog_ModSettings.InitialSize), MethodType.Getter)]
    internal static class RimRoundSettingsWindowSize
    {
        private static void Postfix(Mod ___mod, ref Vector2 __result)
        {
            if (!(___mod is FeedOtherMod)) return;
            __result.x = Mathf.Min(__result.x, Verse.UI.screenWidth - 16f);
            __result.y = Mathf.Min(__result.y, Verse.UI.screenHeight - 16f);
        }
    }
}
