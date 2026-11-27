using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimRound.FeedOther
{
    // VEF's raid-group cache outlives native Lords. The map's LordManager is
    // their deep-save owner; once it removes a Lord, that cache must release
    // the reference too. Active raid pawns, factions and Lords stay intact.
    internal static class VefRaidReferenceCompatibility
    {
        internal static readonly Type WatcherType = AccessTools.TypeByName("VEF.Storyteller.StorytellerWatcher");
        private static readonly Type GroupType = AccessTools.TypeByName("VEF.Storyteller.RaidGroup");
        private static readonly FieldInfo Raids = WatcherType == null ? null : AccessTools.Field(WatcherType, "raidGroups");
        private static readonly FieldInfo Reinforcements = WatcherType == null ? null : AccessTools.Field(WatcherType, "reinforcementGroups");
        private static readonly FieldInfo Lords = GroupType == null ? null : AccessTools.Field(GroupType, "lords");
        private static readonly FieldInfo Pawns = GroupType == null ? null : AccessTools.Field(GroupType, "pawns");
        private static readonly FieldInfo GroupFaction = GroupType == null ? null : AccessTools.Field(GroupType, "faction");
        internal static bool Supported => WatcherType != null && Raids != null && Reinforcements != null &&
            Lords?.FieldType == typeof(HashSet<Lord>);

        internal static void ReleaseRetiredLord(Lord retired)
        {
            if (!Supported || retired == null || Current.Game == null) return;
            // Resolve the component from this game, never VEF's static Instance.
            foreach (GameComponent component in Current.Game.components)
                if (WatcherType.IsInstanceOfType(component)) Visit(component, set => set.Remove(retired));
        }

        internal static void BeforeSave(object watcher)
        {
            if (!Supported || Scribe.mode != LoadSaveMode.Saving || Current.Game == null) return;
            // Also repair stale caches in a running game first saved after an
            // upgrade, and Lords whose owning map has been removed. This does
            // not manufacture missing objects or suppress Scribe diagnostics.
            Visit(watcher, set => set.RemoveWhere(lord => lord == null || lord.lordManager == null ||
                !Find.Maps.Contains(lord.lordManager.map) || !lord.lordManager.lords.Contains(lord)));
            // Native temporary factions disappear after their visitors leave.
            // VEF can retain that group's faction after its last Lord retires.
            // Drop only the inactive cache entry; native pawn/faction ownership
            // and groups with an active Lord or living map pawn are untouched.
            if (Pawns?.FieldType != typeof(HashSet<Pawn>) || GroupFaction?.FieldType != typeof(Faction)) return;
            foreach (FieldInfo field in new[] { Raids, Reinforcements })
                if (field.GetValue(watcher) is IList groups)
                    for (int i = groups.Count - 1; i >= 0; i--)
                    {
                        object group = groups[i];
                        if (group == null || !GroupType.IsInstanceOfType(group) ||
                            !(Lords.GetValue(group) is HashSet<Lord> lords) || lords.Count != 0 ||
                            !(GroupFaction.GetValue(group) is Faction faction) ||
                            Find.FactionManager.AllFactionsListForReading.Contains(faction) ||
                            !(Pawns.GetValue(group) is HashSet<Pawn> pawns)) continue;
                        bool activePawn = false;
                        foreach (Pawn pawn in pawns)
                            if (pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Destroyed) { activePawn = true; break; }
                        if (!activePawn) groups.RemoveAt(i);
                    }
        }

        private static void Visit(object watcher, Action<HashSet<Lord>> action)
        {
            foreach (FieldInfo field in new[] { Raids, Reinforcements })
                if (field.GetValue(watcher) is IEnumerable groups)
                    foreach (object group in groups)
                        if (group != null && GroupType.IsInstanceOfType(group) && Lords.GetValue(group) is HashSet<Lord> lords)
                            action(lords);
        }
    }

    [HarmonyPatch(typeof(LordManager), nameof(LordManager.RemoveLord))]
    internal static class VefRaidLordRemovedPatch
    {
        private static bool Prepare() => VefRaidReferenceCompatibility.Supported;
        private static void Postfix(Lord oldLord) => VefRaidReferenceCompatibility.ReleaseRetiredLord(oldLord);
    }

    [HarmonyPatch]
    internal static class VefRaidCacheSavePatch
    {
        private static bool Prepare() => VefRaidReferenceCompatibility.Supported;
        private static MethodBase TargetMethod() => AccessTools.Method(VefRaidReferenceCompatibility.WatcherType, "ExposeData");
        private static void Prefix(object __instance) => VefRaidReferenceCompatibility.BeforeSave(__instance);
    }
}
