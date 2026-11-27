using HarmonyLib;
using RimRound.Comps;
using RimRound.FeedingTube;
using RimRound.FeedingTube.Comps;
using RimRound.Utilities;
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
    // Native reinstall ghosts receive ThingToInstall.Graphic. A still-spawned
    // compatibility layout deliberately keeps its old graphic, while the new
    // placement always normalizes North. Preview that canonical result too.
    [HarmonyPatch(typeof(GhostUtility), nameof(GhostUtility.GhostGraphicFor))]
    internal static class FixedDistilleryGhostPatch
    {
        private static void Prefix(ref Graphic baseGraphic, ThingDef thingDef)
        {
            if (thingDef?.defName == FoodNetworkV2Constants.DistillerDefName)
                baseGraphic = thingDef.graphic;
        }
    }

    internal static class FoodNetworkV2MachineUtility
    {
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
            // The classic Tick entry point already calls ProcessFood on the
            // global 60-tick cadence. A second thing-hash gate would only
            // coincide for a small fraction of spawned machines.
            if (processor == null || !processor.Spawned ||
                !processor.ReadyForProcessing || !IsOperational(processor))
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

            CompFoodNetworkServing feedstockServing = feedstock.TryGetComp<CompFoodNetworkServing>();
            float nutritionPerItem = feedstockServing?.IsInitialized == true
                ? feedstockServing.RepresentedNutrition
                : feedstock.GetStatValue(StatDefOf.Nutrition, true);
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
                feedstock.TryGetComp<CompFoodNetworkServing>()?.IsInitialized == true
                    ? feedstock.TryGetComp<CompFoodNetworkServing>().SourceCreatedTick
                    : Find.TickManager == null ? 0 : Find.TickManager.TicksGame);
            FoodPreservationStamp sourceStamp =
                FoodPreservationStamp.FromFood(feedstock);
            if (!sourceStamp.Valid ||
                sourceStamp.RemainingRotTicks <= 0f)
                return;
            batch.SetPreservation(sourceStamp);
            if (!network.TryStore(batch))
            {
                return;
            }

            ConsumeExactCount(feedstock, count);
            processor.RecordProcessedNutrition(nutritionPerItem * count,
                FeedOtherMod.Settings.foodProcessorNutritionPerCycle);
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
                thing is Corpse || !OrdinaryFoodIntake.IsProcessableFood(thing) || !SpecialistContentPolicy.Allows(thing) ||
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
            CompFoodNetworkServing serving = food?.TryGetComp<CompFoodNetworkServing>();
            if (serving != null && serving.IsInitialized)
            {
                return serving.FullnessToNutritionRatio;
            }
            ThingComp_FoodItems_NutritionDensity density =
                food == null
                    ? null
                    : food.TryGetComp<ThingComp_FoodItems_NutritionDensity>();
            float ratio = density?.Props == null ? 1f : density.Props.fullnessToNutritionRatio;
            return FoodNetworkV2Constants.PositiveFinite(ratio) ? ratio : 1f;
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
            // The distiller calls ProcessFood every tick; its classic method
            // applies the 60-tick gate internally, so the v2 patch needs one.
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

            input.TryDistillTo(output, FeedOtherMod.Settings.distillerNutritionPerCycle,
                distiller.TargetDensity);
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

        public static IntVec3 DistillerInputCell(Building_NutrientDistillery distiller) =>
            FixedDistilleryGeometry.Input(distiller);
        public static IntVec3 DistillerOutputCell(Building_NutrientDistillery distiller) =>
            FixedDistilleryGeometry.Output(distiller);
        public static float GetDistillerTargetRatio(Building_NutrientDistillery distiller) =>
            distiller?.TargetRatio ?? 1f;
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
            if (tankState != null)
            {
                bool cooling = FoodNetworkV2GameComponent.CoolingActive(tank);
                float temperature = FoodNetworkV2GameComponent.TemperatureFor(tank);
                tankState.AgeTo(Find.TickManager?.TicksGame ?? 0, temperature, cooling);
                result.AppendLine("RR_LiquidTankAmount".Translate(
                    tankState.StoredFullness.ToString("F1"),
                    storage.Capacity.ToString("F1"),
                    tankState.StoredNutrition.ToString("F1")));
                if (tankState.StoredNutrition > 0f)
                {
                    double density = tankState.PreciseStoredNutrition /
                        tankState.PreciseStoredFullness;
                    bool mixed = tankState.batches.Any(batch =>
                        batch != null && !batch.Empty &&
                        Math.Abs(batch.PreciseNutrition / batch.PreciseFullness -
                            density) > 0.0001d);
                    result.AppendLine((mixed
                        ? "RR_LiquidDensityMixed"
                        : "RR_FoodNetworkDensity").Translate(density.ToString("0.##")));
                    float rate = GenTemperature.RotRateAtTemperature(temperature);
                    result.AppendLine(cooling
                        ? "RR_LiquidRefrigerated".Translate().ToString()
                        : rate <= 0f ? "RR_LiquidFrozen".Translate().ToString()
                        : "RR_LiquidNextSpoilage".Translate(
                            tankState.Preservation.TimeUntilSpoilage(temperature)).ToString());
                }
                if (network == null)
                    result.AppendLine("RR_FoodNetworkDisconnected".Translate());
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
                network.StoredNutrition.ToString("F1"));
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

    // A removed/minified tank is no longer discoverable through map listers.
    // Capture classic ownership before removal so reinstalling it cannot
    // resurrect the last v2 snapshot. DeSpawn is not destruction: retain its
    // persistent state for minification and map transfers.
    [HarmonyPatch(typeof(FoodTransmitter_ThingComp), nameof(FoodTransmitter_ThingComp.PostDeSpawn))]
    internal static class FoodNetworkV2StorageLeavingMapPatch
    {
        private static void Prefix(FoodTransmitter_ThingComp __instance)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled &&
                __instance is FoodNetStorage_ThingComp)
            {
                FoodNetworkV2MapComponent.ImportLegacyStorage(__instance.parent as Building);
            }
        }
    }

    // Native destruction also runs for the inner building when a packed
    // MinifiedThing is destroyed. Despawning alone must never dispose food.
    [HarmonyPatch(typeof(Building), nameof(Building.Destroy))]
    internal static class FoodNetworkV2DestroyedBuildingPatch
    {
        private static void Postfix(Building __instance)
        {
            if (__instance.Destroyed &&
                (__instance is Building_AutoFeeder ||
                 __instance.TryGetComp<FoodNetStorage_ThingComp>() != null))
            {
                FoodNetworkV2GameComponent.Instance?.RemoveDestroyedBuilding(__instance);
            }
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
            // Expired batches remove themselves. Manual emptying is only a
            // developer tool; normal storage has no purge action.
            if (!Prefs.DevMode)
                yield break;

            Building tank = storage.parent as Building;
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
            if (distiller == null || !distiller.Spawned)
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
            IntVec3 input = center + FixedDistilleryGeometry.InputOffset;
            IntVec3 output = center + FixedDistilleryGeometry.OutputOffset;
            GenDraw.DrawFieldEdges(
                new List<IntVec3> { input },
                GenTemperature.ColorSpotCold);
            GenDraw.DrawFieldEdges(
                new List<IntVec3> { output },
                GenTemperature.ColorSpotHot);
        }
    }
}
