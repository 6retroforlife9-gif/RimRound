using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimRound.FeedOther
{
    public sealed class AutoMilkExpressionGameComponent : GameComponent
    {
        private List<Pawn> enabledPawns = new List<Pawn>();

        public AutoMilkExpressionGameComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref enabledPawns,
                "rrFeedOtherAutoMilkExpressionPawns",
                LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (enabledPawns == null)
                {
                    enabledPawns = new List<Pawn>();
                }
                else
                {
                    enabledPawns.RemoveAll(pawn => pawn == null);
                    for (int index = enabledPawns.Count - 1; index >= 0; index--)
                    {
                        if (enabledPawns.IndexOf(enabledPawns[index]) != index)
                        {
                            enabledPawns.RemoveAt(index);
                        }
                    }
                }
            }
        }

        public bool IsEnabled(Pawn pawn)
        {
            return pawn != null && enabledPawns != null && enabledPawns.Contains(pawn);
        }

        public void SetEnabled(Pawn pawn, bool enabled)
        {
            if (pawn == null)
            {
                return;
            }

            if (enabledPawns == null)
            {
                enabledPawns = new List<Pawn>();
            }

            if (enabled)
            {
                if (!enabledPawns.Contains(pawn))
                {
                    enabledPawns.Add(pawn);
                }
            }
            else
            {
                enabledPawns.RemoveAll(candidate => candidate == pawn);
            }
        }
    }

    public static class AutoMilkExpressionUtility
    {
        private const float ChargeEpsilon = 0.00001f;
        private static ThingDef cachedMilkDef;
        private static HediffDef cachedLactatingDef;

        public static AutoMilkExpressionGameComponent Settings
        {
            get
            {
                return Current.Game == null
                    ? null
                    : Current.Game.GetComponent<AutoMilkExpressionGameComponent>();
            }
        }

        public static ThingDef MilkDef
        {
            get
            {
                if (cachedMilkDef == null)
                {
                    cachedMilkDef = DefDatabase<ThingDef>.GetNamedSilentFail("Milk");
                }

                return cachedMilkDef;
            }
        }

        public static HediffComp_Lactating GetLactatingComp(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return null;
            }

            if (cachedLactatingDef == null)
            {
                cachedLactatingDef = DefDatabase<HediffDef>.GetNamedSilentFail("Lactating");
            }

            if (cachedLactatingDef == null)
            {
                return null;
            }

            HediffWithComps lactating = pawn.health.hediffSet
                .GetFirstHediffOfDef(cachedLactatingDef) as HediffWithComps;
            return lactating?.TryGetComp<HediffComp_Lactating>();
        }

        public static bool ShouldShowToggle(Pawn pawn)
        {
            return FeedOtherMod.Settings.automaticMilkExpressionEnabled &&
                pawn != null && !pawn.Dead && pawn.Spawned && pawn.IsPlayerControlled &&
                pawn.RaceProps.Humanlike && pawn.gender == Gender.Female && MilkDef != null &&
                GetLactatingComp(pawn) != null && Settings != null;
        }

        public static bool TryExpressMilk(HediffComp_Lactating lactating)
        {
            Pawn pawn = lactating?.parent?.pawn;
            AutoMilkExpressionGameComponent settings = Settings;
            ThingDef milkDef = MilkDef;
            if (pawn == null || settings == null || !settings.IsEnabled(pawn) ||
                !ShouldShowToggle(pawn) || milkDef == null || pawn.Map == null ||
                lactating.Props == null ||
                lactating.Charge + ChargeEpsilon < lactating.Props.fullChargeAmount)
            {
                return false;
            }

            float nutritionPerMilk = milkDef.GetStatValueAbstract(StatDefOf.Nutrition, null);
            if (nutritionPerMilk <= ChargeEpsilon)
            {
                return false;
            }

            int milkCount = Mathf.FloorToInt(
                (lactating.Charge + ChargeEpsilon) / nutritionPerMilk);
            milkCount = Math.Min(milkCount, milkDef.stackLimit);
            if (milkCount <= 0)
            {
                return false;
            }

            Thing milk = ThingMaker.MakeThing(milkDef, null);
            milk.stackCount = milkCount;
            if (!GenPlace.TryPlaceThing(
                milk,
                pawn.Position,
                pawn.Map,
                ThingPlaceMode.Near))
            {
                if (!milk.Destroyed)
                {
                    milk.Destroy(DestroyMode.Vanish);
                }

                return false;
            }

            lactating.GreedyConsume(milkCount * nutritionPerMilk);
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Pawn_GetGizmos_AutoMilkExpressionPatch
    {
        public static IEnumerable<Gizmo> Postfix(
            IEnumerable<Gizmo> __result,
            Pawn __instance)
        {
            if (__result != null)
            {
                foreach (Gizmo gizmo in __result)
                {
                    yield return gizmo;
                }
            }

            if (!AutoMilkExpressionUtility.ShouldShowToggle(__instance))
            {
                yield break;
            }

            AutoMilkExpressionGameComponent settings = AutoMilkExpressionUtility.Settings;
            HediffComp_Lactating lactating = AutoMilkExpressionUtility.GetLactatingComp(__instance);
            Command_Toggle toggle = new Command_Toggle
            {
                defaultLabel = "RR_AutoExpressMilkLabel".Translate(),
                defaultDesc = "RR_AutoExpressMilkDesc".Translate(),
                icon = AutoMilkExpressionUtility.MilkDef.uiIcon,
                isActive = () => settings.IsEnabled(__instance),
                toggleAction = delegate
                {
                    bool enabled = !settings.IsEnabled(__instance);
                    settings.SetEnabled(__instance, enabled);
                    if (enabled)
                    {
                        AutoMilkExpressionUtility.TryExpressMilk(lactating);
                    }
                }
            };

            yield return toggle;
        }
    }

    [HarmonyPatch(typeof(HediffComp_Lactating), nameof(HediffComp_Lactating.TryCharge))]
    public static class HediffComp_Lactating_TryCharge_AutoMilkExpressionPatch
    {
        public static void Postfix(HediffComp_Lactating __instance)
        {
            AutoMilkExpressionUtility.TryExpressMilk(__instance);
        }
    }
}
