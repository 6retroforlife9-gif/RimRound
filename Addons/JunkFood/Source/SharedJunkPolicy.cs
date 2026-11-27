using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Oinky.JunkFood
{
    public sealed class JunkPolicyRecord : IExposable
    {
        public DrugPolicy Policy;
        public bool Recreation = true, Addiction = true, Scheduled;
        public int Carry;
        public float Days = 1f, MoodBelow = 1f, JoyBelow = 1f;
        internal string CarryBuffer;
        public void ExposeData()
        {
            Scribe_References.Look(ref Policy, "policy");
            Scribe_Values.Look(ref Recreation, "recreation", true);
            Scribe_Values.Look(ref Addiction, "addiction", true);
            Scribe_Values.Look(ref Scheduled, "scheduled");
            Scribe_Values.Look(ref Carry, "carry");
            Scribe_Values.Look(ref Days, "days", 1f);
            Scribe_Values.Look(ref MoodBelow, "moodBelow", 1f);
            Scribe_Values.Look(ref JoyBelow, "joyBelow", 1f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Normalize();
        }
        internal void Normalize()
        {
            Carry = Math.Max(0, Math.Min(100, Carry));
            Days = SharedHabitState.Finite(Days) ? Mathf.Clamp(Days, .1f, 25f) : 1f;
            MoodBelow = SharedHabitState.Finite(MoodBelow) ? Mathf.Clamp01(MoodBelow) : 1f;
            JoyBelow = SharedHabitState.Finite(JoyBelow) ? Mathf.Clamp01(JoyBelow) : 1f;
        }
        internal void CopyFrom(JunkPolicyRecord other)
        {
            Recreation = other.Recreation; Addiction = other.Addiction; Scheduled = other.Scheduled;
            Carry = other.Carry; Days = other.Days; MoodBelow = other.MoodBelow; JoyBelow = other.JoyBelow;
            CarryBuffer = null;
        }
    }

    public sealed partial class JunkFoodGameComponent
    {
        public JunkPolicyRecord PolicyFor(Pawn pawn) => PolicyFor(pawn?.drugs?.CurrentPolicy);
        public JunkPolicyRecord PolicyFor(DrugPolicy policy, bool create = true)
        {
            if (policy == null) return null;
            var row = sharedPolicies.FirstOrDefault(r => ReferenceEquals(r?.Policy, policy));
            if (row == null && create)
            {
                row = new JunkPolicyRecord { Policy = policy };
                sharedPolicies.Add(row);
            }
            return row;
        }
        private void PrunePolicies()
        {
            var policies = Current.Game?.drugPolicyDatabase?.AllPolicies;
            if (policies == null) return;
            sharedPolicies.RemoveAll(r => r?.Policy == null || !policies.Any(p => ReferenceEquals(p, r.Policy)));
            caravanReserves.RemoveAll(r => r?.Caravan == null || r.Caravan.Destroyed);
        }
    }

    [HarmonyPatch(typeof(DrugPolicy), nameof(DrugPolicy.CopyFrom))]
    internal static class SharedJunkPolicyCopy
    {
        static void Postfix(DrugPolicy __instance, Policy other)
        {
            var colony = JunkFoodPolicy.Colony;
            if (colony == null) return;
            // Copy also works while the opt-in mode is off. Native IDs are never
            // used as our key, so deleting and reusing an ID cannot leak settings.
            colony.PolicyFor(__instance).CopyFrom(colony.PolicyFor(other as DrugPolicy) ?? new JunkPolicyRecord());
        }
    }

    [HarmonyPatch(typeof(Dialog_ManageDrugPolicies), "DoPolicyConfigArea")]
    internal static class SharedJunkPolicyRow
    {
        static void Prefix(ref Rect rect, out Rect __state)
        {
            __state = default(Rect);
            if (!JunkSharedRules.Mode) return;
            float height = Mathf.Min(106f, rect.height / 3f);
            __state = new Rect(rect.x, rect.yMax - height, rect.width, height);
            rect.height -= height + 4f;
        }
        static void Postfix(Dialog_ManageDrugPolicies __instance, Rect __state)
        {
            if (__state.height <= 0f) return;
            var property = AccessTools.Property(typeof(Dialog_ManageDrugPolicies), "SelectedPolicy");
            var policy = property?.GetValue(__instance, null) as DrugPolicy;
            var row = JunkFoodPolicy.Colony?.PolicyFor(policy);
            if (row == null) return;
            Widgets.DrawMenuSection(__state);
            Rect area = __state.ContractedBy(8f);
            var oldFont = Text.Font;
            Text.Font = GameFont.Small;
            float col = area.width / 5f;
            Widgets.Label(new Rect(area.x, area.y, col, 25f), "Junk food (all treats)");
            Widgets.CheckboxLabeled(new Rect(area.x + col, area.y, col - 8f, 25f), "Recreation", ref row.Recreation);
            Widgets.CheckboxLabeled(new Rect(area.x + 2f * col, area.y, col - 8f, 25f), "Habit relief", ref row.Addiction);
            Widgets.CheckboxLabeled(new Rect(area.x + 3f * col, area.y, col - 8f, 25f), "Scheduled", ref row.Scheduled);
            Widgets.Label(new Rect(area.x + 4f * col, area.y, col * .55f, 25f), "Carry units");
            Widgets.TextFieldNumeric(new Rect(area.x + 4.55f * col, area.y, col * .45f, 25f), ref row.Carry, ref row.CarryBuffer, 0f, 100f);
            float y = area.y + 30f;
            if (row.Scheduled)
            {
                string days = row.Days.ToString("0.#");
                Widgets.Label(new Rect(area.x, y, col, 25f), "Every " + days + " active day" + (days == "1" ? "" : "s"));
                row.Days = Widgets.HorizontalSlider(new Rect(area.x + col, y, col, 25f), row.Days, .1f, 25f, true);
                row.MoodBelow = Widgets.HorizontalSlider(new Rect(area.x + 2f * col + 8f, y, col * 1.45f - 8f, 25f), row.MoodBelow, .01f, 1f, true, "Mood below " + row.MoodBelow.ToStringPercent());
                row.JoyBelow = Widgets.HorizontalSlider(new Rect(area.x + 3.5f * col + 8f, y, col * 1.5f - 8f, 25f), row.JoyBelow, .01f, 1f, true, "Recreation below " + row.JoyBelow.ToStringPercent());
            }
            Widgets.Label(new Rect(area.x, area.yMax - 23f, area.width, 23f), "Extra snacks only. Meals follow food restrictions; 1 serving = 1 unit. Packages contain several units.");
            TooltipHandler.TipRegion(__state, "Shared permissions cover every junk food. Chemical interest and fascination may seek recreation or relief regardless of those two checkboxes. Food restrictions, fullness and colony food reserves still apply. A useful full unit from meals also restarts the shared schedule.");
            Text.Font = oldFont;
        }
    }
}
