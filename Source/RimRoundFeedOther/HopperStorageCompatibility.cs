using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    /// <summary>Separate only saved XL members whose group still has other capabilities.</summary>
    public sealed class HopperStorageCompatibility : MapComponent
    {
        private bool pending;
        public HopperStorageCompatibility(Map map) : base(map) { }
        public void Request() { pending = true; }
        public override void FinalizeInit() { Normalize(map); pending = false; }
        public override void MapComponentTick()
        {
            if (!pending) return;
            pending = false;
            Normalize(map);
        }

        public static int Normalize(Map map)
        {
            ThingDef xl = DefDatabase<ThingDef>.GetNamedSilentFail("RR_Hopper");
            if (map?.storageGroups == null || xl == null) return 0;
            var groups = map.listerThings.ThingsOfDef(xl).OfType<IStorageGroupMember>()
                .Select(m => m.Group).Where(g => g != null).Distinct().ToArray();
            int moved = 0;
            foreach (var group in groups)
            {
                var members = group.members.Where(m => m is Thing t && t.def == xl).ToArray();
                if (members.Length == 0 || members.Length == group.members.Count) continue;
                // Copy BEFORE removing any member: native group acceptance uses
                // the first member's parent. CopyFrom preserves priority, item,
                // quality, freshness and special-filter choices.
                var settings = new StorageSettings();
                settings.CopyFrom(group.GetStoreSettings());
                var separate = map.storageGroups.NewGroup();
                separate.RenamableLabel = group.RenamableLabel;
                separate.GetStoreSettings().CopyFrom(settings);
                foreach (var member in members)
                {
                    member.SetStorageGroup(separate);
                    moved++;
                }
                separate.Notify_SettingsChanged();
                group.Notify_SettingsChanged();
            }
            return moved;
        }
    }

    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.SpawnSetup))]
    internal static class HopperStorageReturned
    {
        private static void Postfix(Building_Storage __instance, Map map)
        {
            if (__instance.def.defName == "RR_Hopper")
                map?.GetComponent<HopperStorageCompatibility>()?.Request();
        }
    }
}
