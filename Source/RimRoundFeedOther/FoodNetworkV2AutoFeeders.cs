using HarmonyLib;
using RimRound.Comps;
using RimRound.FeedingTube;
using RimRound.FeedingTube.AI;
using RimRound.FeedingTube.Defs;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using FeedingTubeHediffDefOf = RimRound.FeedingTube.Defs.HediffDefOf;

namespace RimRound.FeedOther
{
    internal static class FoodNetworkV2AutoFeederUtility
    {
        private static readonly FieldInfo ForcedTargetField =
            AccessTools.Field(typeof(Building_AutoFeeder), "forcedTarget");
        private static readonly FieldInfo CurrentModeField =
            AccessTools.Field(typeof(Building_AutoFeeder), "currentModeInternal");
        private static readonly FieldInfo CurrentPawnField =
            AccessTools.Field(typeof(Building_AutoFeeder), "_currentPawn");
        private static readonly FieldInfo CachedFullnessField =
            AccessTools.Field(typeof(Building_AutoFeeder), "_cachedFNDComp");
        private static readonly FieldInfo OnSoundField =
            AccessTools.Field(typeof(Building_AutoFeeder), "onSound");
        private static readonly FieldInfo DrinkingSoundField =
            AccessTools.Field(typeof(Building_AutoFeeder), "drinkingSustainer");

        private static readonly Texture2D OffIcon =
            ContentFinder<Texture2D>.Get("UI/AutoFeeder/Modes/Off", true);
        private static readonly Texture2D LoseIcon =
            ContentFinder<Texture2D>.Get("UI/AutoFeeder/Modes/Lose", true);
        private static readonly Texture2D MaintainIcon =
            ContentFinder<Texture2D>.Get("UI/AutoFeeder/Modes/Maintain", true);
        private static readonly Texture2D GainIcon =
            ContentFinder<Texture2D>.Get("UI/AutoFeeder/Modes/Gain", true);
        private static readonly Texture2D MaximumIcon =
            ContentFinder<Texture2D>.Get("UI/AutoFeeder/Modes/ForceGain", true);
        private static readonly Material TubeMaterial = MaterialPool.MatFrom(
            "Things/Building/Production/FeedingTubePipeThickMat",
            ShaderDatabase.Transparent,
            Color.white);

        public static AutoFeederMode GetMode(Building_AutoFeeder feeder)
        {
            if (feeder == null || CurrentModeField == null)
            {
                return AutoFeederMode.off;
            }
            object value = CurrentModeField.GetValue(feeder);
            return value is AutoFeederMode
                ? (AutoFeederMode)value
                : AutoFeederMode.off;
        }

        public static void SetMode(
            Building_AutoFeeder feeder,
            AutoFeederMode mode)
        {
            if (feeder == null || CurrentModeField == null)
            {
                return;
            }
            CurrentModeField.SetValue(feeder, mode);
            StopSounds(feeder);
        }

        public static LocalTargetInfo GetLegacyTarget(Building_AutoFeeder feeder)
        {
            if (feeder == null || ForcedTargetField == null)
            {
                return LocalTargetInfo.Invalid;
            }
            object value = ForcedTargetField.GetValue(feeder);
            return value is LocalTargetInfo
                ? (LocalTargetInfo)value
                : LocalTargetInfo.Invalid;
        }

        public static void SetLegacyTarget(
            Building_AutoFeeder feeder,
            Pawn pawn)
        {
            if (feeder == null)
            {
                return;
            }
            if (ForcedTargetField != null)
            {
                ForcedTargetField.SetValue(
                    feeder,
                    pawn == null
                        ? LocalTargetInfo.Invalid
                        : new LocalTargetInfo(pawn));
            }
            if (CurrentPawnField != null)
            {
                CurrentPawnField.SetValue(feeder, pawn);
            }
            if (CachedFullnessField != null)
            {
                CachedFullnessField.SetValue(
                    feeder,
                    pawn == null
                        ? null
                        : pawn.TryGetComp<FullnessAndDietStats_ThingComp>());
            }
        }

        public static AutoFeederLinkStateV2 State(
            Building_AutoFeeder feeder,
            bool create)
        {
            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            if (saved == null || feeder == null)
            {
                return null;
            }
            AutoFeederLinkStateV2 state = saved.GetFeederState(feeder, create);
            if (state != null)
            {
                EnsureLegacyTargetMigrated(feeder, state);
            }
            return state;
        }

        public static List<Pawn> LinkedPawns(Building_AutoFeeder feeder)
        {
            AutoFeederLinkStateV2 state = State(feeder, true);
            if (state == null || feeder == null || feeder.Map == null)
            {
                return new List<Pawn>();
            }

            return state.pawnIds
                .Select(delegate(int id) { return ResolvePawn(feeder.Map, id); })
                .Where(delegate(Pawn pawn) { return pawn != null; })
                .ToList();
        }

        public static List<Building_Bed> LinkedBeds(Building_AutoFeeder feeder)
        {
            AutoFeederLinkStateV2 state = State(feeder, true);
            if (state == null || feeder == null || feeder.Map == null)
            {
                return new List<Building_Bed>();
            }

            return state.bedIds
                .Select(delegate(int id) { return ResolveBed(feeder.Map, id); })
                .Where(delegate(Building_Bed bed) { return bed != null; })
                .ToList();
        }

        public static bool LinkBed(
            Building_AutoFeeder feeder,
            Building_Bed bed,
            out string reason)
        {
            reason = null;
            if (!CanLink(feeder, bed, out reason))
            {
                return false;
            }

            AutoFeederLinkStateV2 state = State(feeder, true);
            if (state == null)
            {
                reason = "RR_FoodNetworkLinkUnavailable".Translate();
                return false;
            }

            bool advanced = feeder is Building_AdvancedAutoFeeder;
            if (!advanced)
            {
                UnlinkAll(feeder, false);
                state = State(feeder, true);
            }
            if (!state.bedIds.Contains(bed.thingIDNumber))
            {
                state.bedIds.Add(bed.thingIDNumber);
            }
            RefreshOccupantConnections(feeder, state);
            return true;
        }

        public static void UnlinkAll(Building_AutoFeeder feeder, bool switchOff)
        {
            AutoFeederLinkStateV2 state = State(feeder, true);
            if (state != null)
            {
                foreach (int pawnId in state.pawnIds.ToList())
                {
                    Pawn pawn = ResolvePawnAnywhere(
                        feeder == null ? null : feeder.Map,
                        pawnId);
                    CleanupConnection(pawn);
                }
                state.bedIds.Clear();
                state.pawnIds.Clear();
            }
            SetLegacyTarget(feeder, null);
            StopSounds(feeder);
            if (switchOff)
            {
                SetMode(feeder, AutoFeederMode.off);
            }
        }

        public static void Tick(Building_AutoFeeder feeder)
        {
            if (feeder == null || !feeder.Spawned ||
                !feeder.IsHashIntervalTick(FeedOtherMod.Settings.AutoFeederCheckTicks))
            {
                return;
            }

            AutoFeederLinkStateV2 state = State(feeder, true);
            if (state == null)
            {
                return;
            }

            RemoveInvalidLinks(feeder, state);
            AutoFeederMode mode = GetMode(feeder);
            if (mode == AutoFeederMode.off || state.pawnIds.Count == 0 ||
                !FoodNetworkV2MachineUtility.IsOperational(feeder))
            {
                return;
            }

            if (!(feeder is Building_AdvancedAutoFeeder) &&
                (mode == AutoFeederMode.gain || mode == AutoFeederMode.maxgain))
            {
                mode = AutoFeederMode.maintain;
                SetMode(feeder, mode);
            }

            FoodNetworkV2 network = FoodNetworkV2MachineUtility.NetworkFor(feeder);
            if (network == null || network.StoredNutrition <=
                FoodNetworkV2Constants.Epsilon)
            {
                return;
            }

            foreach (int pawnId in state.pawnIds.ToList())
            {
                Pawn pawn = ResolvePawn(feeder.Map, pawnId);
                if (pawn != null)
                {
                    TryFeedPawn(network, pawn, mode);
                }
            }
        }

        public static string Status(
            Building_AutoFeeder feeder,
            FoodNetworkV2 network)
        {
            AutoFeederLinkStateV2 state = State(feeder, true);
            int count = state == null ? 0 : state.bedIds.Count;
            if (count == 0)
            {
                return "RR_FoodNetworkFeederNoBeds".Translate();
            }
            if (GetMode(feeder) == AutoFeederMode.off)
            {
                return "RR_FoodNetworkFeederOff".Translate(count.ToString());
            }
            if (network == null || network.StoredNutrition <=
                FoodNetworkV2Constants.Epsilon)
            {
                return "RR_FoodNetworkStatusNoFood".Translate();
            }
            return "RR_FoodNetworkFeederReady".Translate(
                count.ToString(),
                ModeLabel(GetMode(feeder)));
        }

        public static void CleanupMapLinks(Map map)
        {
            if (map == null || FoodNetworkV2GameComponent.Instance == null)
            {
                return;
            }

            foreach (Thing thing in map.listerThings.AllThings)
            {
                Building_AutoFeeder feeder = thing as Building_AutoFeeder;
                if (feeder != null && feeder.Spawned)
                {
                    AutoFeederLinkStateV2 state = State(feeder, true);
                    if (state != null)
                    {
                        RemoveInvalidLinks(feeder, state);
                    }
                }
            }
        }

        public static void PrepareForLegacyModeAllMaps()
        {
            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            if (saved == null || Find.Maps == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                foreach (Building_AutoFeeder feeder in map.listerThings.AllThings
                    .OfType<Building_AutoFeeder>())
                {
                    AutoFeederLinkStateV2 state =
                        saved.GetFeederState(feeder, false);
                    if (state == null)
                    {
                        continue;
                    }

                    RemoveInvalidLinks(feeder, state);
                    Pawn first = state.pawnIds
                        .Select(delegate(int id)
                        {
                            return ResolvePawnAnywhere(map, id);
                        })
                        .FirstOrDefault(delegate(Pawn pawn)
                        {
                            return pawn != null && !pawn.Dead && !pawn.Destroyed;
                        });

                    foreach (int pawnId in state.pawnIds.ToList())
                    {
                        Pawn pawn = ResolvePawnAnywhere(map, pawnId);
                        if (pawn != first)
                        {
                            CleanupConnection(pawn);
                        }
                    }

                    Building_Bed firstBed = first == null
                        ? null
                        : first.CurrentBed();
                    state.bedIds.Clear();
                    state.pawnIds.Clear();
                    if (first != null)
                    {
                        if (firstBed != null)
                        {
                            state.bedIds.Add(firstBed.thingIDNumber);
                        }
                        state.pawnIds.Add(first.thingIDNumber);
                        ApplyConnection(first);
                    }
                    state.legacyTargetMigrated = true;
                    SetLegacyTarget(feeder, first);
                }
            }
        }

        public static void ImportLegacyLinksFromAllMaps()
        {
            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            if (saved == null || Find.Maps == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                foreach (Building_AutoFeeder feeder in map.listerThings.AllThings
                    .OfType<Building_AutoFeeder>())
                {
                    LocalTargetInfo legacyTarget = GetLegacyTarget(feeder);
                    Pawn legacyPawn = legacyTarget.IsValid
                        ? legacyTarget.Pawn
                        : null;
                    AutoFeederLinkStateV2 state =
                        saved.GetFeederState(feeder, true);
                    foreach (int pawnId in state.pawnIds.ToList())
                    {
                        CleanupConnection(ResolvePawnAnywhere(map, pawnId));
                    }
                    state.bedIds.Clear();
                    state.pawnIds.Clear();

                    if (legacyPawn != null && !legacyPawn.Dead &&
                        !legacyPawn.Destroyed)
                    {
                        Building_Bed legacyBed = legacyPawn.CurrentBed();
                        if (legacyBed != null)
                        {
                            state.bedIds.Add(legacyBed.thingIDNumber);
                        }
                        state.pawnIds.Add(legacyPawn.thingIDNumber);
                        ApplyConnection(legacyPawn);
                    }
                    state.legacyTargetMigrated = true;
                    // Food Network v2 owns whole-bed links. Keeping the first
                    // imported pawn in RimRound's legacy single-pawn field
                    // makes several old systems treat that pawn as the only
                    // connected occupant, so clear it after migration.
                    SetLegacyTarget(feeder, null);
                }
            }
        }

        public static IEnumerable<Gizmo> ReplaceGizmos(
            Building_AutoFeeder feeder,
            IEnumerable<Gizmo> original)
        {
            HashSet<string> replacedLabels = OriginalCustomLabels();
            if (original != null)
            {
                foreach (Gizmo gizmo in original)
                {
                    Command command = gizmo as Command;
                    string label = command == null || command.defaultLabel == null
                        ? null
                        : command.defaultLabel;
                    if (label == null || !replacedLabels.Contains(label))
                    {
                        yield return gizmo;
                    }
                }
            }

            yield return LinkGizmo(feeder);
            AutoFeederLinkStateV2 state = State(feeder, true);
            if (state != null && state.bedIds.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "RR_FoodNetworkUnlinkBeds".Translate(),
                    defaultDesc = "RR_FoodNetworkUnlinkBedsDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt", true),
                    action = delegate
                    {
                        UnlinkAll(feeder, false);
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    }
                };
            }
            yield return ModeGizmo(feeder);
        }

        public static void DrawTubes(Building_AutoFeeder feeder)
        {
            AutoFeederLinkStateV2 state = State(feeder, true);
            foreach (Building_Bed bed in LinkedBeds(feeder))
            {
                List<Pawn> occupants = new List<Pawn>();
                if (state != null)
                {
                    foreach (int pawnId in state.pawnIds)
                    {
                        Pawn connected = ResolvePawn(feeder.Map, pawnId);
                        Building_Bed connectedBed = FindLinkedBedForPawn(
                            feeder,
                            state,
                            connected);
                        if (connected != null && connectedBed == bed &&
                            !occupants.Contains(connected))
                        {
                            occupants.Add(connected);
                        }
                    }
                }
                foreach (Pawn occupant in bed.CurOccupants)
                {
                    if (occupant != null && !occupants.Contains(occupant))
                    {
                        occupants.Add(occupant);
                    }
                }

                if (occupants.Count == 0)
                {
                    DrawTube(feeder, bed.TrueCenter());
                    continue;
                }
                foreach (Pawn occupant in occupants)
                {
                    DrawTube(feeder, occupant.TrueCenter());
                }
            }
        }

        private static void DrawTube(Building_AutoFeeder feeder, Vector3 target)
        {
            Vector3 from = feeder.GetAutoFeederLocationVector();
            from.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            target.y = from.y;
            target.z -= 0.2f;
            GenDraw.DrawLineBetween(from, target, TubeMaterial, 0.2f);
        }

        public static void DrawSelectionOverlays(Building_AutoFeeder feeder)
        {
            if (feeder == null || feeder.Map == null)
            {
                return;
            }
            float range = feeder is Building_AdvancedAutoFeeder
                ? FeedOtherMod.Settings.advancedAutoFeederRange
                : 1.9f;
            GenDraw.DrawRadiusRing(feeder.Position, range);
            foreach (Building_Bed bed in LinkedBeds(feeder))
            {
                Vector3 from = feeder.TrueCenter();
                Vector3 to = bed.TrueCenter();
                from.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                to.y = from.y;
                GenDraw.DrawLineBetween(
                    from,
                    to,
                    Building_TurretGun.ForcedTargetLineMat,
                    0.2f);
            }
        }

        public static bool IsLinkedToAdvancedFeeder(
            Building_AdvancedAutoFeeder feeder,
            Pawn pawn)
        {
            Building_Bed ignored;
            return TryGetLinkedBed(feeder, pawn, out ignored);
        }

        public static bool TryGetLinkedBed(
            Building_AutoFeeder feeder,
            Pawn pawn,
            out Building_Bed bed)
        {
            AutoFeederLinkStateV2 state = State(feeder, true);
            bed = FindLinkedBedForPawn(feeder, state, pawn);
            return bed != null;
        }

        private static bool TryFeedPawn(
            FoodNetworkV2 network,
            Pawn pawn,
            AutoFeederMode mode)
        {
            if (pawn == null || pawn.needs == null || pawn.needs.food == null)
            {
                return false;
            }

            FullnessAndDietStats_ThingComp fullness =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            float desiredNutrition;
            float maximumStoredFullness = float.MaxValue;
            bool useNutritionPulseLimit = true;
            if (fullness == null || fullness.Disabled ||
                fullness.DietMode == DietMode.Disabled)
            {
                if (mode == AutoFeederMode.lose)
                {
                    if (pawn.needs.food.CurLevelPercentage >
                        pawn.needs.food.PercentageThreshUrgentlyHungry)
                    {
                        return false;
                    }
                    desiredNutrition =
                        pawn.needs.food.PercentageThreshHungry *
                        pawn.needs.food.MaxLevel - pawn.needs.food.CurLevel;
                }
                else
                {
                    if (pawn.needs.food.CurLevelPercentage >
                        pawn.needs.food.PercentageThreshHungry)
                    {
                        return false;
                    }
                    desiredNutrition = 0.8f * pawn.needs.food.MaxLevel -
                        pawn.needs.food.CurLevel;
                }
            }
            else
            {
                float multiplier = Mathf.Max(
                    FoodNetworkV2Constants.Epsilon,
                    fullness.FullnessGainedMultiplier);
                if (mode == AutoFeederMode.gain ||
                    mode == AutoFeederMode.maxgain)
                {
                    float targetFraction = mode == AutoFeederMode.gain
                        ? FeedOtherMod.Settings.FeedingTargetFraction
                        : FeedOtherMod.Settings.autoFeederMaximumTargetPercent / 100f;
                    float fullnessTarget = fullness.HardLimit * targetFraction;
                    float fullnessRemaining = fullnessTarget - fullness.CurrentFullness;
                    if (fullnessRemaining <= FoodNetworkV2Constants.Epsilon)
                    {
                        return false;
                    }
                    float fullnessGainThisPulse = Mathf.Min(
                        fullnessRemaining,
                        fullness.HardLimit *
                            FeedOtherMod.Settings.autoFeederGainFullnessPercentPerPulse /
                            100f);
                    maximumStoredFullness = fullnessGainThisPulse / multiplier;

                    // Gain modes target physical stomach volume. Limiting the
                    // transaction by nutrition made distilled food flow much
                    // more slowly: a 3x-dense batch delivered only one third
                    // as much fullness per pulse. Let the FIFO draw plan size
                    // nutrition from this density-independent volume cap.
                    desiredNutrition = float.MaxValue;
                    useNutritionPulseLimit = false;
                }
                else
                {
                    float trigger = mode == AutoFeederMode.lose
                        ? pawn.needs.food.PercentageThreshUrgentlyHungry
                        : pawn.needs.food.PercentageThreshHungry;
                    if (pawn.needs.food.CurLevelPercentage > trigger)
                    {
                        return false;
                    }
                    float targetPercentage = mode == AutoFeederMode.lose
                        ? pawn.needs.food.PercentageThreshHungry
                        : 0.8f;
                    float digestingNutrition = fullness.CurrentFullness /
                        Mathf.Max(
                            FoodNetworkV2Constants.Epsilon,
                            fullness.CurrentFullnessToNutritionRatio);
                    desiredNutrition = targetPercentage * pawn.needs.food.MaxLevel -
                        pawn.needs.food.CurLevel - digestingNutrition;
                    maximumStoredFullness = Mathf.Max(
                        0f,
                        (fullness.HardLimit * 0.95f - fullness.CurrentFullness) /
                        multiplier);
                }
            }

            if (useNutritionPulseLimit)
            {
                desiredNutrition = Mathf.Min(
                    desiredNutrition,
                    FeedOtherMod.Settings.foodDispenserServingNutrition);
            }
            if (desiredNutrition <= FoodNetworkV2Constants.Epsilon ||
                maximumStoredFullness <= FoodNetworkV2Constants.Epsilon)
            {
                return false;
            }

            FoodBatchV2 batch;
            if (!network.TryDraw(
                    desiredNutrition,
                    maximumStoredFullness,
                    false,
                    out batch))
            {
                return false;
            }

            Thing serving = FoodNetworkV2ServingUtility.MakeServing(batch);
            if (serving == null)
            {
                network.TryStore(batch);
                return false;
            }

            float nutritionIngested = serving.Ingested(pawn, batch.nutrition);
            if (!pawn.Dead && pawn.needs != null && pawn.needs.food != null &&
                nutritionIngested > FoodNetworkV2Constants.Epsilon)
            {
                pawn.needs.food.CurLevel += nutritionIngested;
                if (pawn.records != null)
                {
                    pawn.records.AddTo(
                        RecordDefOf.NutritionEaten,
                        nutritionIngested);
                }
            }
            if (!serving.Destroyed)
            {
                serving.Destroy(DestroyMode.Vanish);
            }
            return true;
        }

        private static Command_Action LinkGizmo(Building_AutoFeeder feeder)
        {
            bool advanced = feeder is Building_AdvancedAutoFeeder;
            AutoFeederLinkStateV2 state = State(feeder, true);
            int currentCount = state == null ? 0 : state.bedIds.Count;
            Command_Action result = new Command_Action
            {
                defaultLabel = (advanced
                    ? "RR_FoodNetworkLinkBedAdvanced"
                    : "RR_FoodNetworkLinkBedBasic").Translate(),
                defaultDesc = (advanced
                    ? "RR_FoodNetworkLinkBedAdvancedDesc"
                    : "RR_FoodNetworkLinkBedBasicDesc").Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", true),
                action = delegate { BeginTargeting(feeder); }
            };
            if (advanced && currentCount >=
                FeedOtherMod.Settings.AdvancedAutoFeederBedLimitRounded)
            {
                result.Disable("RR_FoodNetworkBedLimitReached".Translate());
            }
            return result;
        }

        private static Command_Action ModeGizmo(Building_AutoFeeder feeder)
        {
            AutoFeederMode mode = GetMode(feeder);
            bool advanced = feeder is Building_AdvancedAutoFeeder;
            Command_Action result = new Command_Action();
            result.defaultLabel = ModeLabel(mode);
            result.defaultDesc = ModeDescription(mode);
            result.icon = ModeIcon(mode);
            result.action = delegate
            {
                AutoFeederMode next = NextMode(mode, advanced);
                SetMode(feeder, next);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            };
            return result;
        }

        private static AutoFeederMode NextMode(
            AutoFeederMode mode,
            bool advanced)
        {
            switch (mode)
            {
                case AutoFeederMode.off:
                    return AutoFeederMode.lose;
                case AutoFeederMode.lose:
                    return AutoFeederMode.maintain;
                case AutoFeederMode.maintain:
                    return advanced ? AutoFeederMode.gain : AutoFeederMode.off;
                case AutoFeederMode.gain:
                    return advanced ? AutoFeederMode.maxgain : AutoFeederMode.off;
                default:
                    return AutoFeederMode.off;
            }
        }

        private static string ModeLabel(AutoFeederMode mode)
        {
            switch (mode)
            {
                case AutoFeederMode.lose:
                    return "FeedingTubeModeLabel_lose".Translate();
                case AutoFeederMode.maintain:
                    return "FeedingTubeModeLabel_maintain".Translate();
                case AutoFeederMode.gain:
                    return "FeedingTubeModeLabel_gain".Translate();
                case AutoFeederMode.maxgain:
                    return "FeedingTubeModeLabel_maxgain".Translate();
                default:
                    return "FeedingTubeModeLabel_off".Translate();
            }
        }

        private static string ModeDescription(AutoFeederMode mode)
        {
            switch (mode)
            {
                case AutoFeederMode.lose:
                    return "FeedingTubeModeLabel_loseDesc".Translate();
                case AutoFeederMode.maintain:
                    return "FeedingTubeModeLabel_maintainDesc".Translate();
                case AutoFeederMode.gain:
                    return "RR_FoodNetworkGainModeDesc".Translate(
                        FeedOtherMod.Settings.FeedingTargetPercentRounded.ToString());
                case AutoFeederMode.maxgain:
                    return "RR_FoodNetworkMaximumModeDesc".Translate(
                        FeedOtherMod.Settings.autoFeederMaximumTargetPercent.ToString("F0"));
                default:
                    return "FeedingTubeModeLabel_offDesc".Translate();
            }
        }

        private static Texture2D ModeIcon(AutoFeederMode mode)
        {
            switch (mode)
            {
                case AutoFeederMode.lose:
                    return LoseIcon;
                case AutoFeederMode.maintain:
                    return MaintainIcon;
                case AutoFeederMode.gain:
                    return GainIcon;
                case AutoFeederMode.maxgain:
                    return MaximumIcon;
                default:
                    return OffIcon;
            }
        }

        private static HashSet<string> OriginalCustomLabels()
        {
            return new HashSet<string>
            {
                "FeedingTube_TargetPawn".Translate(),
                "FeedingTube_StopFeedingLabel".Translate(),
                "FeedingTubeModeLabel_off".Translate(),
                "FeedingTubeModeLabel_lose".Translate(),
                "FeedingTubeModeLabel_maintain".Translate(),
                "FeedingTubeModeLabel_gain".Translate(),
                "FeedingTubeModeLabel_maxgain".Translate()
            };
        }

        private static void BeginTargeting(Building_AutoFeeder feeder)
        {
            TargetingParameters parameters = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetLocations = false
            };
            Find.Targeter.BeginTargeting(
                parameters,
                delegate(LocalTargetInfo target)
                {
                    string reason;
                    Building_Bed bed = BedFromTarget(target);
                    if (!LinkBed(feeder, bed, out reason))
                    {
                        Messages.Message(
                            reason ?? "RR_FoodNetworkLinkUnavailable".Translate(),
                            MessageTypeDefOf.RejectInput,
                            false);
                        return;
                    }
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                },
                delegate(LocalTargetInfo target)
                {
                    if (BedFromTarget(target) != null)
                    {
                        GenDraw.DrawTargetHighlight(target);
                    }
                },
                delegate(LocalTargetInfo target)
                {
                    string reason;
                    return CanLink(feeder, BedFromTarget(target), out reason);
                },
                null,
                null,
                RimRound.FeedingTube.Defs.ThingDefOf.RR_FeedingTubeFluid.uiIcon,
                false);
        }

        private static Building_Bed BedFromTarget(LocalTargetInfo target)
        {
            if (target.Pawn != null)
            {
                return target.Pawn.CurrentBed();
            }
            return target.Thing as Building_Bed;
        }

        private static bool CanLink(
            Building_AutoFeeder feeder,
            Building_Bed bed,
            out string reason)
        {
            reason = null;
            if (feeder == null || !feeder.Spawned || bed == null ||
                !bed.Spawned || bed.Map != feeder.Map ||
                bed.def == null || bed.def.building == null ||
                !bed.def.building.bed_humanlike)
            {
                reason = "RR_FoodNetworkTargetMustBeInBed".Translate();
                return false;
            }

            bool advanced = feeder is Building_AdvancedAutoFeeder;
            if (advanced)
            {
                float range = FeedOtherMod.Settings.advancedAutoFeederRange;
                if (feeder.Position.DistanceToSquared(bed.Position) > range * range)
                {
                    reason = "RR_FoodNetworkTargetOutOfRange".Translate(
                        range.ToString("F0"));
                    return false;
                }
                AutoFeederLinkStateV2 state = State(feeder, true);
                if (state != null &&
                    state.bedIds.Count >=
                        FeedOtherMod.Settings.AdvancedAutoFeederBedLimitRounded &&
                    !state.bedIds.Contains(bed.thingIDNumber))
                {
                    reason = "RR_FoodNetworkBedLimitReached".Translate();
                    return false;
                }
            }
            else if (!BedTouchesBasicFeeder(feeder, bed))
            {
                reason = "RR_FoodNetworkBasicNeedsAdjacentBed".Translate();
                return false;
            }

            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            if (saved != null)
            {
                foreach (AutoFeederLinkStateV2 other in saved.FeederStates)
                {
                    if (other.feederThingId != feeder.thingIDNumber &&
                        other.bedIds != null &&
                        other.bedIds.Contains(bed.thingIDNumber))
                    {
                        reason = "RR_FoodNetworkAlreadyLinked".Translate();
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool BedTouchesBasicFeeder(
            Building_AutoFeeder feeder,
            Building_Bed bed)
        {
            if (feeder == null || bed == null)
            {
                return false;
            }
            CellRect allowed = feeder.OccupiedRect().ExpandedBy(1);
            return bed.OccupiedRect().Cells.Any(
                delegate(IntVec3 cell) { return allowed.Contains(cell); });
        }

        private static void EnsureLegacyTargetMigrated(
            Building_AutoFeeder feeder,
            AutoFeederLinkStateV2 state)
        {
            if (state.legacyTargetMigrated)
            {
                return;
            }
            LocalTargetInfo legacy = GetLegacyTarget(feeder);
            Building_Bed legacyBed = legacy.IsValid && legacy.Pawn != null
                ? legacy.Pawn.CurrentBed()
                : null;
            if (legacyBed != null &&
                !state.bedIds.Contains(legacyBed.thingIDNumber))
            {
                state.bedIds.Add(legacyBed.thingIDNumber);
            }
            state.legacyTargetMigrated = true;
            RefreshOccupantConnections(feeder, state);
        }

        private static void RemoveInvalidLinks(
            Building_AutoFeeder feeder,
            AutoFeederLinkStateV2 state)
        {
            List<int> invalid = new List<int>();
            int linkLimit = feeder is Building_AdvancedAutoFeeder
                ? FeedOtherMod.Settings.AdvancedAutoFeederBedLimitRounded
                : 1;
            if (state.bedIds.Count > linkLimit)
            {
                invalid.AddRange(state.bedIds.Skip(linkLimit));
            }
            foreach (int bedId in state.bedIds)
            {
                Building_Bed bed = ResolveBed(feeder.Map, bedId);
                if (!CanRemainLinked(feeder, bed) && !invalid.Contains(bedId))
                {
                    invalid.Add(bedId);
                }
            }
            foreach (int bedId in invalid)
            {
                state.bedIds.Remove(bedId);
            }
            RefreshOccupantConnections(feeder, state);
        }

        private static bool CanRemainLinked(
            Building_AutoFeeder feeder,
            Building_Bed bed)
        {
            if (feeder == null || bed == null || !bed.Spawned ||
                bed.Map != feeder.Map || bed.Destroyed)
            {
                return false;
            }
            if (feeder is Building_AdvancedAutoFeeder)
            {
                float range = FeedOtherMod.Settings.advancedAutoFeederRange;
                return feeder.Position.DistanceToSquared(bed.Position) <= range * range;
            }
            return BedTouchesBasicFeeder(feeder, bed);
        }

        private static void RefreshOccupantConnections(
            Building_AutoFeeder feeder,
            AutoFeederLinkStateV2 state)
        {
            if (state == null)
            {
                return;
            }

            Dictionary<Pawn, Building_Bed> current =
                new Dictionary<Pawn, Building_Bed>();
            if (feeder != null && feeder.Map != null)
            {
                // Preserve an already connected pawn through a brief job/posture
                // transition when they are still physically in a linked bed.
                // The old implementation looked only at CurOccupants/InBed;
                // a failed in-bed joy job could therefore remove the tube,
                // stand the pawn up, and let them escape the bed.
                foreach (int pawnId in state.pawnIds.ToList())
                {
                    Pawn pawn = ResolvePawn(feeder.Map, pawnId);
                    Building_Bed linkedBed = FindLinkedBedForPawn(
                        feeder,
                        state,
                        pawn);
                    if (pawn != null && linkedBed != null &&
                        !current.ContainsKey(pawn))
                    {
                        current.Add(pawn, linkedBed);
                    }
                }

                foreach (int bedId in state.bedIds)
                {
                    Building_Bed bed = ResolveBed(feeder.Map, bedId);
                    if (bed == null)
                    {
                        continue;
                    }
                    foreach (Pawn pawn in bed.CurOccupants)
                    {
                        if (pawn != null && pawn.Spawned && !pawn.Dead &&
                            !pawn.Destroyed && pawn.RaceProps.Humanlike &&
                            pawn.InBed() && !current.ContainsKey(pawn))
                        {
                            current.Add(pawn, bed);
                        }
                    }
                }
            }

            HashSet<int> currentIds = new HashSet<int>(
                current.Keys.Select(delegate(Pawn pawn)
                {
                    return pawn.thingIDNumber;
                }));
            foreach (int oldPawnId in state.pawnIds.ToList())
            {
                if (!currentIds.Contains(oldPawnId))
                {
                    CleanupConnection(ResolvePawnAnywhere(
                        feeder == null ? null : feeder.Map,
                        oldPawnId));
                }
            }

            state.pawnIds.Clear();
            foreach (KeyValuePair<Pawn, Building_Bed> pair in current)
            {
                Pawn pawn = pair.Key;
                state.pawnIds.Add(pawn.thingIDNumber);
                ApplyConnection(pawn);
                EnsurePawnAnchoredInBed(pawn, pair.Value);
            }
            SyncLegacyTarget(feeder, state);
        }

        private static Building_Bed FindLinkedBedForPawn(
            Building_AutoFeeder feeder,
            AutoFeederLinkStateV2 state,
            Pawn pawn)
        {
            if (feeder == null || feeder.Map == null || state == null ||
                pawn == null || !pawn.Spawned || pawn.Map != feeder.Map ||
                pawn.Dead || pawn.Destroyed || pawn.RaceProps == null ||
                !pawn.RaceProps.Humanlike)
            {
                return null;
            }

            Building_Bed currentBed = pawn.CurrentBed();
            if (currentBed != null &&
                state.bedIds.Contains(currentBed.thingIDNumber))
            {
                return currentBed;
            }

            Job job = pawn.CurJob;
            if (job != null)
            {
                Building_Bed targetBed = job.targetA.Thing as Building_Bed ??
                    job.targetC.Thing as Building_Bed;
                if (targetBed != null &&
                    state.bedIds.Contains(targetBed.thingIDNumber) &&
                    targetBed.OccupiedRect().Contains(pawn.Position))
                {
                    return targetBed;
                }
            }

            foreach (int bedId in state.bedIds)
            {
                Building_Bed linkedBed = ResolveBed(feeder.Map, bedId);
                if (linkedBed != null &&
                    linkedBed.OccupiedRect().Contains(pawn.Position))
                {
                    return linkedBed;
                }
            }
            return null;
        }

        private static void EnsurePawnAnchoredInBed(
            Pawn pawn,
            Building_Bed bed)
        {
            if (pawn == null || bed == null || pawn.jobs == null ||
                pawn.jobs.startingNewJob || !pawn.Spawned || pawn.Map != bed.Map ||
                !bed.OccupiedRect().Contains(pawn.Position))
            {
                return;
            }

            pawn.pather?.StopDead();
            if (pawn.GetPosture().InBed() && pawn.CurrentBed() == bed)
            {
                return;
            }

            Job current = pawn.CurJob;
            if (current != null)
            {
                bool layDownForThisBed =
                    current.targetA.Thing == bed &&
                    pawn.jobs.curDriver is JobDriver_LayDown;
                bool inBedRadioForThisBed =
                    current.targetC.Thing == bed &&
                    pawn.jobs.curDriver is JobDriver_ListenToRadio;
                if (layDownForThisBed || inBedRadioForThisBed)
                {
                    // The driver is already restoring the correct posture. Do
                    // not interrupt its setup and create a job-start loop.
                    return;
                }
            }

            bool continueSleeping = pawn.jobs.curDriver != null &&
                pawn.jobs.curDriver.asleep;
            Job layDown = JobMaker.MakeJob(JobDefOf.LayDown, bed);
            pawn.jobs.StartJob(
                layDown,
                JobCondition.InterruptForced,
                resumeCurJobAfterwards: false,
                cancelBusyStances: true,
                continueSleeping: continueSleeping,
                preToilReservationsCanFail: true);
        }

        private static void SyncLegacyTarget(
            Building_AutoFeeder feeder,
            AutoFeederLinkStateV2 state)
        {
            Pawn first = state == null || state.pawnIds.Count == 0
                ? null
                : ResolvePawnAnywhere(feeder == null ? null : feeder.Map,
                    state.pawnIds[0]);
            // While v2 is active, no single pawn is authoritative. The saved
            // bed/pawn collections above drive feeding, locking, drawing and
            // recreation. Restore one legacy pawn only when returning to the
            // original RimRound network.
            SetLegacyTarget(
                feeder,
                FeedOtherMod.Settings.foodNetworkV2Enabled ? null : first);
        }

        private static Pawn ResolvePawn(Map map, int thingId)
        {
            if (map == null || thingId == 0)
            {
                return null;
            }
            return map.mapPawns.AllPawnsSpawned.FirstOrDefault(
                delegate(Pawn pawn) { return pawn.thingIDNumber == thingId; });
        }

        private static Building_Bed ResolveBed(Map map, int thingId)
        {
            if (map == null || thingId == 0)
            {
                return null;
            }
            return map.listerBuildings.allBuildingsColonist
                .Concat(map.listerBuildings.allBuildingsNonColonist)
                .OfType<Building_Bed>()
                .FirstOrDefault(delegate(Building_Bed bed)
                {
                    return bed.thingIDNumber == thingId;
                });
        }

        private static Pawn ResolvePawnAnywhere(Map preferredMap, int thingId)
        {
            if (thingId == 0)
            {
                return null;
            }

            if (preferredMap != null)
            {
                Pawn preferred = preferredMap.mapPawns.AllPawns.FirstOrDefault(
                    delegate(Pawn pawn)
                    {
                        return pawn != null && pawn.thingIDNumber == thingId;
                    });
                if (preferred != null)
                {
                    return preferred;
                }
            }

            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    if (map == preferredMap)
                    {
                        continue;
                    }
                    Pawn pawn = map.mapPawns.AllPawns.FirstOrDefault(
                        delegate(Pawn candidate)
                        {
                            return candidate != null &&
                                candidate.thingIDNumber == thingId;
                        });
                    if (pawn != null)
                    {
                        return pawn;
                    }
                }
            }

            return Find.WorldPawns == null
                ? null
                : Find.WorldPawns.AllPawnsAliveOrDead.FirstOrDefault(
                    delegate(Pawn pawn)
                    {
                        return pawn != null && pawn.thingIDNumber == thingId;
                    });
        }

        private static void ApplyConnection(Pawn pawn)
        {
            if (pawn == null || pawn.health == null)
            {
                return;
            }
            HediffDef tube = FeedingTubeHediffDefOf.RimRound_UsingFeedingTube;
            if (tube != null && !pawn.health.hediffSet.HasHediff(tube))
            {
                pawn.health.AddHediff(tube);
            }
            FullnessAndDietStats_ThingComp fullness =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness != null)
            {
                fullness.IsConnectedToFeedingMachine = true;
                fullness.SloshDurationSeconds = 0;
                fullness.SloshStartTick = 0;
            }
        }

        private static void CleanupConnection(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }
            if (pawn.health != null && FeedingTubeHediffDefOf.RimRound_UsingFeedingTube != null)
            {
                Hediff existing;
                while ((existing = pawn.health.hediffSet.GetFirstHediffOfDef(
                    FeedingTubeHediffDefOf.RimRound_UsingFeedingTube)) != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
            }
            FullnessAndDietStats_ThingComp fullness =
                pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (fullness != null)
            {
                fullness.IsConnectedToFeedingMachine = false;
                fullness.SloshDurationSeconds = 60;
                fullness.SloshStartTick = Find.TickManager == null
                    ? 0
                    : Find.TickManager.TicksAbs;
            }
        }

        private static void StopSounds(Building_AutoFeeder feeder)
        {
            EndSustainer(feeder, OnSoundField);
            EndSustainer(feeder, DrinkingSoundField);
        }

        private static void EndSustainer(
            Building_AutoFeeder feeder,
            FieldInfo field)
        {
            if (feeder == null || field == null)
            {
                return;
            }
            Sustainer sustainer = field.GetValue(feeder) as Sustainer;
            if (sustainer != null)
            {
                sustainer.End();
                field.SetValue(feeder, null);
            }
        }
    }

    internal sealed class AutoFeederTickSuppressionState
    {
        public AutoFeederMode mode;
        public LocalTargetInfo target;
        public Pawn pawn;
        public FullnessAndDietStats_ThingComp fullness;
    }

    [HarmonyPatch(typeof(Building_AutoFeeder), "Tick")]
    internal static class FoodNetworkV2AutoFeederTickPatch
    {
        private static void Prefix(
            Building_AutoFeeder __instance,
            out AutoFeederTickSuppressionState __state)
        {
            __state = null;
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return;
            }
            __state = new AutoFeederTickSuppressionState
            {
                mode = FoodNetworkV2AutoFeederUtility.GetMode(__instance),
                target = FoodNetworkV2AutoFeederUtility.GetLegacyTarget(__instance),
                pawn = __instance.CurrentPawn,
                fullness = __instance.CurrentPawn == null
                    ? null
                    : __instance.CurrentPawn.TryGetComp<FullnessAndDietStats_ThingComp>()
            };
            FoodNetworkV2AutoFeederUtility.SetMode(__instance, AutoFeederMode.off);
            FoodNetworkV2AutoFeederUtility.SetLegacyTarget(__instance, null);
        }

        private static void Postfix(
            Building_AutoFeeder __instance,
            AutoFeederTickSuppressionState __state)
        {
            if (__state == null)
            {
                return;
            }
            FoodNetworkV2AutoFeederUtility.SetMode(__instance, __state.mode);
            FoodNetworkV2AutoFeederUtility.SetLegacyTarget(
                __instance,
                __state.target.IsValid ? __state.target.Pawn : null);
            FoodNetworkV2AutoFeederUtility.Tick(__instance);
        }
    }

    [HarmonyPatch(typeof(Building_AutoFeeder), "HandleDestruction")]
    internal static class FoodNetworkV2AutoFeederCleanupPatch
    {
        private static bool Prefix(Building_AutoFeeder __instance)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            FoodNetworkV2AutoFeederUtility.UnlinkAll(__instance, true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Building_AutoFeeder), nameof(Building_AutoFeeder.GetGizmos))]
    internal static class FoodNetworkV2AutoFeederGizmosPatch
    {
        private static IEnumerable<Gizmo> Postfix(
            IEnumerable<Gizmo> __result,
            Building_AutoFeeder __instance)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return __result;
            }
            return FoodNetworkV2AutoFeederUtility.ReplaceGizmos(
                __instance,
                __result);
        }
    }

    [HarmonyPatch(typeof(Building_AutoFeeder), "DrawAt")]
    internal static class FoodNetworkV2AutoFeederDrawPatch
    {
        private static void Prefix(Building_AutoFeeder __instance)
        {
            if (FeedOtherMod.Settings.foodNetworkV2Enabled &&
                FoodNetworkV2AutoFeederUtility.GetLegacyTarget(__instance).IsValid)
            {
                // Suppress RimRound's one-pawn tube. The postfix draws every
                // occupant of every linked bed without duplicates.
                FoodNetworkV2AutoFeederUtility.SetLegacyTarget(__instance, null);
            }
        }

        private static void Postfix(Building_AutoFeeder __instance)
        {
            if (FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                FoodNetworkV2AutoFeederUtility.DrawTubes(__instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(Building_AutoFeeder),
        nameof(Building_AutoFeeder.DrawExtraSelectionOverlays))]
    internal static class FoodNetworkV2AutoFeederOverlayPatch
    {
        private static bool Prefix(Building_AutoFeeder __instance)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }
            FoodNetworkV2AutoFeederUtility.DrawSelectionOverlays(__instance);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(JoyGiver_ListenToRadio),
        nameof(JoyGiver_ListenToRadio.TryGiveJobWhileInBed))]
    internal static class FoodNetworkV2AdvancedRadioPatch
    {
        private static bool Prefix(
            JoyGiver_ListenToRadio __instance,
            Pawn pawn,
            ref Job __result)
        {
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return true;
            }

            // Do not let the legacy CurrentPawn field choose only the first
            // occupant of a double bed. Resolve the pawn's saved bed link and
            // build the same in-bed watch job independently for every sleeper.
            __result = null;
            if (pawn == null || pawn.Map == null || !pawn.InBed())
            {
                return false;
            }

            Building_Bed linkedBed = null;
            Building_AdvancedAutoFeeder feeder = pawn.Map.listerBuildings
                .allBuildingsColonist
                .OfType<Building_AdvancedAutoFeeder>()
                .FirstOrDefault(delegate(Building_AdvancedAutoFeeder candidate)
                {
                    Building_Bed candidateBed;
                    if (!FoodNetworkV2AutoFeederUtility.TryGetLinkedBed(
                            candidate,
                            pawn,
                            out candidateBed))
                    {
                        return false;
                    }
                    linkedBed = candidateBed;
                    return true;
                });
            if (feeder != null && linkedBed != null)
            {
                __result = JobMaker.MakeJob(
                    __instance.def.jobDef,
                    feeder,
                    pawn.Position,
                    linkedBed);
            }
            return false;
        }
    }

    [HarmonyPatch(
        typeof(JobDriver_WatchBuilding),
        nameof(JobDriver_WatchBuilding.TryMakePreToilReservations))]
    internal static class FoodNetworkV2AdvancedRadioReservationsPatch
    {
        private static bool Prefix(
            JobDriver_WatchBuilding __instance,
            bool errorOnFailed,
            ref bool __result)
        {
            JobDriver_ListenToRadio radio =
                __instance as JobDriver_ListenToRadio;
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled || radio == null)
            {
                return true;
            }

            Pawn pawn = radio.pawn;
            Job job = radio.job;
            Building_Bed bed = job == null
                ? null
                : job.targetC.Thing as Building_Bed;
            if (pawn == null || bed == null)
            {
                __result = false;
                return false;
            }

            // JobDriver_WatchBuilding normally reserves target B as a sitting
            // spot. On a bed cell that becomes a maxPawns=1 reservation on the
            // whole bed, which conflicts with the other sleeper's normal
            // maxPawns=SleepingSlotsCount reservation. Listening is broadcast
            // recreation, so reserve only this pawn's ordinary bed slot and do
            // not reserve the radio or the bed cell as a chair.
            __result = pawn.Reserve(
                bed,
                job,
                bed.SleepingSlotsCount,
                0,
                null,
                errorOnFailed);
            return false;
        }
    }
}
