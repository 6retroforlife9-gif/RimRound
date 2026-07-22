using HarmonyLib;
using RimRound.Comps;
using RimRound.FeedingTube;
using RimRound.FeedingTube.Comps;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace RimRound.FeedOther
{
    internal static class FoodNetworkV2MachineUtility
    {
        private static readonly FieldInfo DistillerTargetRatioField =
            AccessTools.Field(typeof(Building_NutrientDistillery), "targetRatio");

        public static FoodNetworkV2 NetworkFor(Thing thing)
        {
            if (thing == null || thing.Map == null)
            {
                return null;
            }

            FoodNetworkV2MapComponent component =
                FoodNetworkV2MapComponent.For(thing.Map);
            return component == null ? null : component.NetworkFor(thing);
        }

        public static bool IsOperational(ThingWithComps machine)
        {
            if (machine == null || !machine.Spawned)
            {
                return false;
            }

            CompPowerTrader power = machine.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
            {
                return false;
            }

            CompFlickable flick = machine.TryGetComp<CompFlickable>();
            if (flick != null && !flick.SwitchIsOn)
            {
                return false;
            }

            CompBreakdownable breakdown = machine.TryGetComp<CompBreakdownable>();
            return breakdown == null || !breakdown.BrokenDown;
        }

        public static void ProcessFood(Building_FoodProcessor processor)
        {
            if (processor == null || !processor.Spawned ||
                !processor.IsHashIntervalTick(60) || !IsOperational(processor))
            {
                return;
            }

            FoodNetworkV2 network = NetworkFor(processor);
            if (network == null || !network.HasStorage ||
                network.RemainingCapacity <= FoodNetworkV2Constants.Epsilon)
            {
                return;
            }

            Thing feedstock = FindFeedstock(processor);
            if (feedstock == null)
            {
                return;
            }

            float nutritionPerItem = feedstock.GetStatValue(
                StatDefOf.Nutrition,
                true);
            if (nutritionPerItem <= FoodNetworkV2Constants.Epsilon)
            {
                return;
            }

            float ratio = FullnessToNutritionRatio(feedstock);
            float fullnessPerItem = nutritionPerItem * ratio;
            int byCapacity = Mathf.FloorToInt(
                (network.RemainingCapacity + FoodNetworkV2Constants.Epsilon) /
                fullnessPerItem);
            int byCycle = Mathf.Max(
                1,
                Mathf.FloorToInt(
                    FeedOtherMod.Settings.foodProcessorNutritionPerCycle /
                    nutritionPerItem));
            int count = Mathf.Min(feedstock.stackCount, byCapacity, byCycle);
            if (count <= 0)
            {
                return;
            }

            FoodBatchV2 batch = new FoodBatchV2(
                nutritionPerItem * count,
                fullnessPerItem * count,
                IngredientsFrom(feedstock),
                Find.TickManager == null ? 0 : Find.TickManager.TicksGame);
            if (!network.TryStore(batch))
            {
                return;
            }

            ConsumeExactCount(feedstock, count);
        }

        public static Thing FindFeedstock(Building_FoodProcessor processor)
        {
            if (processor == null || processor.Map == null)
            {
                return null;
            }

            List<Building_Storage> hoppers = new List<Building_Storage>();
            foreach (IntVec3 cell in GenAdj.CellsAdjacentCardinal(processor))
            {
                foreach (Thing thing in cell.GetThingList(processor.Map))
                {
                    Building_Storage hopper = thing as Building_Storage;
                    if (hopper != null &&
                        (hopper.def == RimWorld.ThingDefOf.Hopper ||
                         hopper.def.defName == "RR_Hopper") &&
                        !hoppers.Contains(hopper))
                    {
                        hoppers.Add(hopper);
                    }
                }
            }

            foreach (Building_Storage hopper in hoppers
                .OrderBy(delegate(Building_Storage storage)
                {
                    return storage.Position.DistanceToSquared(processor.Position);
                }))
            {
                StorageSettings settings = hopper.GetStoreSettings();
                foreach (IntVec3 cell in hopper.OccupiedRect().Cells)
                {
                    foreach (Thing thing in cell.GetThingList(processor.Map))
                    {
                        if (thing != hopper && IsAcceptableFeedstock(thing) &&
                            (settings == null || settings.AllowedToAccept(thing)))
                        {
                            return thing;
                        }
                    }
                }
            }

            return null;
        }

        public static bool IsAcceptableFeedstock(Thing thing)
        {
            if (thing == null || thing.def == null ||
                thing is Corpse || thing.def.IsDrug ||
                !thing.def.IsNutritionGivingIngestible ||
                !thing.IngestibleNow || thing.def.ingestible == null ||
                thing.def.ingestible.preferability == FoodPreferability.Undefined)
            {
                return false;
            }

            bool preparedMeal =
                (int)thing.def.ingestible.preferability >=
                (int)FoodPreferability.MealAwful;
            return FeedOtherMod.Settings.processPreparedMealsInFoodProcessor ||
                !preparedMeal;
        }

        public static float FullnessToNutritionRatio(Thing food)
        {
            ThingComp_FoodItems_NutritionDensity density =
                food == null
                    ? null
                    : food.TryGetComp<ThingComp_FoodItems_NutritionDensity>();
            return density == null
                ? 1f
                : Mathf.Max(
                    FoodNetworkV2Constants.Epsilon,
                    density.Props.fullnessToNutritionRatio);
        }

        public static IEnumerable<ThingDef> IngredientsFrom(Thing food)
        {
            if (food == null)
            {
                return new ThingDef[0];
            }

            CompIngredients ingredients = food.TryGetComp<CompIngredients>();
            if (ingredients != null && !ingredients.ingredients.NullOrEmpty())
            {
                return ingredients.ingredients;
            }
            return new ThingDef[] { food.def };
        }

        public static void ProcessDistiller(Building_NutrientDistillery distiller)
        {
            if (distiller == null || !distiller.Spawned ||
                !distiller.IsHashIntervalTick(60) || !IsOperational(distiller))
            {
                return;
            }

            FoodNetworkV2 input;
            FoodNetworkV2 output;
            GetDistillerNetworks(distiller, out input, out output);
            if (input == null || output == null || input == output)
            {
                return;
            }

            float nutrition = FeedOtherMod.Settings.distillerNutritionPerCycle;
            float targetRatio = GetDistillerTargetRatio(distiller);
            float outputFullness = nutrition * targetRatio;
            if (!input.CanDrawNutrition(nutrition) ||
                output.RemainingCapacity + FoodNetworkV2Constants.Epsilon <
                    outputFullness)
            {
                return;
            }

            FoodBatchV2 source;
            if (!input.TryDrawExactNutrition(nutrition, out source))
            {
                return;
            }

            FoodBatchV2 distilled = new FoodBatchV2(
                source.nutrition,
                source.nutrition * targetRatio,
                source.ingredients,
                source.createdTick);
            if (!output.TryStore(distilled))
            {
                // Capacity was checked first, but another simultaneous consumer
                // may have won the transaction. Restore the exact source batch.
                input.TryStore(source);
            }
        }

        public static void GetDistillerNetworks(
            Building_NutrientDistillery distiller,
            out FoodNetworkV2 input,
            out FoodNetworkV2 output)
        {
            input = null;
            output = null;
            if (distiller == null || distiller.Map == null)
            {
                return;
            }

            FoodNetworkV2MapComponent manager =
                FoodNetworkV2MapComponent.For(distiller.Map);
            if (manager == null)
            {
                return;
            }

            input = manager.NetworkAt(DistillerInputCell(distiller));
            output = manager.NetworkAt(DistillerOutputCell(distiller));
        }

        public static IntVec3 DistillerInputCell(
            Building_NutrientDistillery distiller)
        {
            return distiller.Position +
                (IntVec3.West + IntVec3.South).RotatedBy(distiller.Rotation);
        }

        public static IntVec3 DistillerOutputCell(
            Building_NutrientDistillery distiller)
        {
            return distiller.Position +
                (2 * IntVec3.East + IntVec3.South).RotatedBy(distiller.Rotation);
        }

        public static float GetDistillerTargetRatio(
            Building_NutrientDistillery distiller)
        {
            if (distiller == null || DistillerTargetRatioField == null)
            {
                return 1f;
            }

            object value = DistillerTargetRatioField.GetValue(distiller);
            return value is float
                ? Mathf.Clamp((float)value, 0.1f, 10f)
                : 1f;
        }

        public static string TraderStatus(Thing parent)
        {
            if (parent == null)
            {
                return string.Empty;
            }

            if (parent is Building_NutrientDistillery)
            {
                return DistillerStatus((Building_NutrientDistillery)parent);
            }

            FoodNetworkV2 network = NetworkFor(parent);
            StringBuilder result = new StringBuilder();
            result.Append(NetworkSummary(network));
            string state = MachineState(parent, network);
            if (!string.IsNullOrEmpty(state))
            {
                result.AppendLine();
                result.Append(state);
            }
            return result.ToString();
        }

        public static string StorageStatus(FoodNetStorage_ThingComp storage)
        {
            if (storage == null || storage.parent == null)
            {
                return string.Empty;
            }

            FoodNetworkV2 network = NetworkFor(storage.parent);
            Building tank = storage.parent as Building;
            FoodTankStateV2 tankState = tank == null ||
                FoodNetworkV2GameComponent.Instance == null
                    ? null
                    : FoodNetworkV2GameComponent.Instance.GetTankState(tank, true);
            StringBuilder result = new StringBuilder();
            result.AppendLine(NetworkSummary(network));
            if (tankState != null)
            {
                result.AppendLine("RR_FoodNetworkTankStored".Translate(
                    tankState.StoredNutrition.ToString("F2"),
                    tankState.StoredFullness.ToString("F2"),
                    storage.Capacity.ToString("F1")));
                result.AppendLine("RR_FoodNetworkDensity".Translate(
                    (1f / tankState.AverageFullnessToNutritionRatio)
                        .ToString("F2")));
                if (network != null)
                {
                    result.Append("RR_FoodNetworkIngredients".Translate(
                        network.IngredientSummary(6)));
                }
            }
            return result.ToString().TrimEndNewlines();
        }

        public static string NetworkSummary(FoodNetworkV2 network)
        {
            if (network == null)
            {
                return "RR_FoodNetworkDisconnected".Translate();
            }

            return "RR_FoodNetworkSummary".Translate(
                network.StoredNutrition.ToString("F2"),
                network.StoredFullness.ToString("F2"),
                network.Capacity.ToString("F1"),
                network.BatchCount.ToString());
        }

        private static string MachineState(Thing parent, FoodNetworkV2 network)
        {
            ThingWithComps machine = parent as ThingWithComps;
            if (machine != null && !IsOperational(machine))
            {
                return "RR_FoodNetworkStatusOffline".Translate();
            }
            if (network == null)
            {
                return "RR_FoodNetworkStatusNoConnection".Translate();
            }
            if (parent is Building_FoodProcessor)
            {
                if (!network.HasStorage)
                {
                    return "RR_FoodNetworkStatusNoStorage".Translate();
                }
                if (network.RemainingCapacity <= FoodNetworkV2Constants.Epsilon)
                {
                    return "RR_FoodNetworkStatusOutputFull".Translate();
                }
                return FindFeedstock((Building_FoodProcessor)parent) == null
                    ? "RR_FoodNetworkStatusNoFeedstock".Translate()
                    : "RR_FoodNetworkStatusProcessing".Translate();
            }
            if (parent is Building_FoodFaucet)
            {
                return network.CanDraw(float.MaxValue, float.MaxValue)
                    ? "RR_FoodNetworkStatusReady".Translate()
                    : "RR_FoodNetworkStatusNoFood".Translate();
            }
            if (parent is Building_AutoFeeder)
            {
                return FoodNetworkV2AutoFeederUtility.Status(
                    (Building_AutoFeeder)parent,
                    network);
            }
            return string.Empty;
        }

        private static string DistillerStatus(
            Building_NutrientDistillery distiller)
        {
            FoodNetworkV2 input;
            FoodNetworkV2 output;
            GetDistillerNetworks(distiller, out input, out output);
            StringBuilder result = new StringBuilder();
            string inputText = input == null
                ? "RR_FoodNetworkDisconnected".Translate().ToString()
                : input.StoredNutrition.ToString("F2");
            string outputText = output == null
                ? "RR_FoodNetworkDisconnected".Translate().ToString()
                : output.StoredNutrition.ToString("F2");
            result.AppendLine("RR_FoodNetworkDistillerInput".Translate(inputText));
            result.AppendLine("RR_FoodNetworkDistillerOutput".Translate(outputText));
            if (!IsOperational(distiller))
            {
                result.Append("RR_FoodNetworkStatusOffline".Translate());
            }
            else if (input == null || output == null)
            {
                result.Append("RR_FoodNetworkStatusNeedsTwoNetworks".Translate());
            }
            else if (input == output)
            {
                result.Append("RR_FoodNetworkStatusSameNetwork".Translate());
            }
            else if (!input.CanDrawNutrition(
                FeedOtherMod.Settings.distillerNutritionPerCycle))
            {
                result.Append("RR_FoodNetworkStatusNoFood".Translate());
            }
            else if (output.RemainingCapacity <
                FeedOtherMod.Settings.distillerNutritionPerCycle *
                GetDistillerTargetRatio(distiller))
            {
                result.Append("RR_FoodNetworkStatusOutputFull".Translate());
            }
            else
            {
                result.Append("RR_FoodNetworkStatusDistilling".Translate());
            }
            return result.ToString().TrimEndNewlines();
        }

        private static void ConsumeExactCount(Thing food, int count)
        {
            if (food == null || count <= 0)
            {
                return;
            }

            if (count >= food.stackCount)
            {
                food.Destroy(DestroyMode.Vanish);
                return;
            }

            Thing consumed = food.SplitOff(count);
            if (consumed != null && !consumed.Destroyed)
            {
                consumed.Destroy(DestroyMode.Vanish);
            }
        }
    }

    [HarmonyPatch(typeof(Building_FoodProcessor), "ProcessFood")]
    internal static class FoodNetworkV2ProcessorPatch
    {
        private static bool Prefix(Building_FoodProcessor __instance)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            FoodNetworkV2MachineUtility.ProcessFood(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Building_NutrientDistillery), "ProcessFood")]
    internal static class FoodNetworkV2DistillerPatch
    {
        private static bool Prefix(Building_NutrientDistillery __instance)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            FoodNetworkV2MachineUtility.ProcessDistiller(__instance);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(FoodTransmitter_NetManager),
        nameof(FoodTransmitter_NetManager.MapComponentTick))]
    internal static class DisableLegacyFoodNetworkTickPatch
    {
        private static bool Prefix()
        {
            return !FeedOtherMod.Settings.foodNetworkV2Enabled;
        }
    }

    [HarmonyPatch(
        typeof(FoodTransmitter_NetManager),
        nameof(FoodTransmitter_NetManager.Notify_ConnectorAdded))]
    internal static class FoodNetworkV2ConnectorAddedPatch
    {
        private static bool Prefix(FoodTransmitter_ThingComp comp)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            if (comp != null && comp.parent != null)
            {
                FoodNetworkV2MapComponent manager =
                    FoodNetworkV2MapComponent.For(comp.parent.Map);
                if (manager != null)
                {
                    manager.MarkDirty();
                }
            }
            return false;
        }
    }

    [HarmonyPatch(
        typeof(FoodTransmitter_NetManager),
        nameof(FoodTransmitter_NetManager.Notify_ConnectorRemoved))]
    internal static class FoodNetworkV2ConnectorRemovedPatch
    {
        private static bool Prefix(Map ___map)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            FoodNetworkV2MapComponent manager =
                FoodNetworkV2MapComponent.For(___map);
            if (manager != null)
            {
                manager.MarkDirty();
            }
            return false;
        }
    }

    [HarmonyPatch(
        typeof(FoodValve_ThingComp),
        nameof(FoodValve_ThingComp.ReceiveCompSignal))]
    internal static class FoodNetworkV2ValveSignalPatch
    {
        private static void Postfix(FoodValve_ThingComp __instance)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled ||
                __instance == null || __instance.parent == null ||
                __instance.parent.Map == null)
            {
                return;
            }

            FoodNetworkV2MapComponent manager =
                FoodNetworkV2MapComponent.For(__instance.parent.Map);
            if (manager != null)
            {
                manager.MarkDirty();
            }
            __instance.parent.Map.mapDrawer.MapMeshDirty(
                __instance.parent.Position,
                MapMeshFlagDefOf.Things,
                true,
                false);
        }
    }

    [HarmonyPatch(
        typeof(FoodTransmitter_NetManager),
        nameof(FoodTransmitter_NetManager.Init))]
    internal static class FoodNetworkV2LegacyInitPatch
    {
        private static bool Prefix(Map ___map)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            FoodNetworkV2MapComponent manager =
                FoodNetworkV2MapComponent.For(___map);
            if (manager != null)
            {
                manager.MarkDirty();
            }
            return false;
        }
    }

    [HarmonyPatch(
        typeof(FoodTransmitter_ThingComp),
        nameof(FoodTransmitter_ThingComp.CompInspectStringExtra))]
    internal static class FoodNetworkV2TransmitterInspectPatch
    {
        private static bool Prefix(
            FoodTransmitter_ThingComp __instance,
            ref string __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            __result = FoodNetworkV2MachineUtility.NetworkSummary(
                FoodNetworkV2MachineUtility.NetworkFor(__instance.parent));
            return false;
        }
    }

    [HarmonyPatch(
        typeof(FoodNetTrader_ThingComp),
        nameof(FoodNetTrader_ThingComp.CompInspectStringExtra))]
    internal static class FoodNetworkV2TraderInspectPatch
    {
        private static bool Prefix(
            FoodNetTrader_ThingComp __instance,
            ref string __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            __result = FoodNetworkV2MachineUtility.TraderStatus(__instance.parent);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(FoodNetStorage_ThingComp),
        nameof(FoodNetStorage_ThingComp.CompInspectStringExtra))]
    internal static class FoodNetworkV2StorageInspectPatch
    {
        private static bool Prefix(
            FoodNetStorage_ThingComp __instance,
            ref string __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            __result = FoodNetworkV2MachineUtility.StorageStatus(__instance);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(FoodNetStorage_ThingComp),
        nameof(FoodNetStorage_ThingComp.CompGetGizmosExtra))]
    internal static class FoodNetworkV2StorageGizmosPatch
    {
        private static bool Prefix(
            FoodNetStorage_ThingComp __instance,
            ref IEnumerable<Gizmo> __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            __result = StorageGizmos(__instance);
            return false;
        }

        private static IEnumerable<Gizmo> StorageGizmos(
            FoodNetStorage_ThingComp storage)
        {
            Building tank = storage.parent as Building;
            yield return new Command_Action
            {
                defaultLabel = "RR_FoodNetworkPurgeTank".Translate(),
                defaultDesc = "RR_FoodNetworkPurgeTankDesc".Translate(),
                icon = Widgets.GetIconFor(RimWorld.ThingDefOf.Filth_Water),
                Order = 401,
                action = delegate
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "RR_FoodNetworkPurgeTankConfirm".Translate(),
                        delegate
                        {
                            FoodNetworkV2GameComponent saved =
                                FoodNetworkV2GameComponent.Instance;
                            FoodTankStateV2 state = saved == null || tank == null
                                ? null
                                : saved.GetTankState(tank, true);
                            if (state != null)
                            {
                                state.Purge();
                                FoodNetworkV2 network =
                                    FoodNetworkV2MachineUtility.NetworkFor(tank);
                                if (network != null)
                                {
                                    network.SyncLegacyStorageComps();
                                }
                            }
                        },
                        true));
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "RR_FoodNetworkPurgeNetwork".Translate(),
                defaultDesc = "RR_FoodNetworkPurgeNetworkDesc".Translate(),
                icon = Widgets.GetIconFor(RimWorld.ThingDefOf.Filth_Water),
                Order = 402,
                action = delegate
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "RR_FoodNetworkPurgeNetworkConfirm".Translate(),
                        delegate
                        {
                            FoodNetworkV2 network =
                                FoodNetworkV2MachineUtility.NetworkFor(tank);
                            if (network != null)
                            {
                                network.Purge();
                            }
                        },
                        true));
                }
            };
        }
    }

    [HarmonyPatch(
        typeof(Graphic_LinkedFood),
        nameof(Graphic_LinkedFood.ShouldLinkWith))]
    internal static class FoodNetworkV2LinkedGraphicPatch
    {
        private static bool Prefix(
            IntVec3 c,
            Thing parent,
            ref bool __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            FoodNetworkV2MapComponent manager =
                FoodNetworkV2MapComponent.For(parent.Map);
            __result = manager != null &&
                manager.NetworkFor(parent) != null &&
                manager.NetworkFor(parent) == manager.NetworkAt(c);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(Graphic_LinkedFoodOverlay),
        nameof(Graphic_LinkedFoodOverlay.ShouldLinkWith))]
    internal static class FoodNetworkV2LinkedOverlayPatch
    {
        private static bool Prefix(
            IntVec3 c,
            Thing parent,
            ref bool __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            FoodNetworkV2MapComponent manager =
                FoodNetworkV2MapComponent.For(parent.Map);
            __result = manager != null &&
                manager.NetworkFor(parent) != null &&
                manager.NetworkFor(parent) == manager.NetworkAt(c);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(SectionLayer_ThingsFoodGrid),
        nameof(SectionLayer_ThingsFoodGrid.ShouldDrawFoodGrid),
        MethodType.Getter)]
    internal static class FoodNetworkV2OverlayVisibilityPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return;
            }
            if (FeedOtherMod.Settings.alwaysShowFoodPipeOverlay)
            {
                __result = true;
                return;
            }
            if (!FeedOtherMod.Settings.showFoodPipeOverlayWhenSelected)
            {
                return;
            }

            Thing selected = Find.Selector == null
                ? null
                : Find.Selector.SingleSelectedThing;
            if (selected != null &&
                (FoodNetworkV2Constants.IsFoodNetworkThing(selected) ||
                 FoodNetworkV2Constants.IsDistiller(selected)))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(
        typeof(Building),
        nameof(Building.DrawExtraSelectionOverlays))]
    internal static class FoodNetworkV2DistillerPortOverlayPatch
    {
        private static void Postfix(Building __instance)
        {
            Building_NutrientDistillery distiller =
                __instance as Building_NutrientDistillery;
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled ||
                distiller == null || !distiller.Spawned)
            {
                return;
            }
            GenDraw.DrawFieldEdges(
                new List<IntVec3>
                {
                    FoodNetworkV2MachineUtility.DistillerInputCell(distiller)
                },
                GenTemperature.ColorSpotCold);
            GenDraw.DrawFieldEdges(
                new List<IntVec3>
                {
                    FoodNetworkV2MachineUtility.DistillerOutputCell(distiller)
                },
                GenTemperature.ColorSpotHot);
        }
    }

    public sealed class FoodNetworkV2DistillerPlaceWorker : PlaceWorker
    {
        public override void DrawGhost(
            ThingDef def,
            IntVec3 center,
            Rot4 rot,
            Color ghostCol,
            Thing thing = null)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);
            IntVec3 input = center +
                (IntVec3.West + IntVec3.South).RotatedBy(rot);
            IntVec3 output = center +
                (2 * IntVec3.East + IntVec3.South).RotatedBy(rot);
            GenDraw.DrawFieldEdges(
                new List<IntVec3> { input },
                GenTemperature.ColorSpotCold);
            GenDraw.DrawFieldEdges(
                new List<IntVec3> { output },
                GenTemperature.ColorSpotHot);
        }
    }
}
