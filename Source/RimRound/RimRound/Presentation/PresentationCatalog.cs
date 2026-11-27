using System.Collections.Generic;
using System.Linq;
using AlienRace;
using UnityEngine;
using Verse;

namespace RimRound.Presentation
{
    // New packs register an exact package/race pair. Priority 0 is reserved for
    // the shipped legacy bridge; explicit registrations normally use 100.
    public sealed class RacePresentationDef : Def
    {
        public string racePackageId, raceDefName, adapterId = "human";
        public int priority = 100;
        public string defaultMaleProfileId, defaultFemaleProfileId, defaultNoneProfileId, fallbackProfileId;
        public List<string> requiredCapabilityIds = new List<string>();
        public RacePresentationData ToData() => new RacePresentationData {
            ProviderPackageId = modContentPack.PackageIdPlayerFacing, RacePackageId = racePackageId,
            RaceDefName = raceDefName, AdapterId = adapterId, Priority = priority,
            DefaultMaleProfileId = defaultMaleProfileId, DefaultFemaleProfileId = defaultFemaleProfileId,
            DefaultNoneProfileId = defaultNoneProfileId, FallbackProfileId = fallbackProfileId,
            RequiredCapabilityIds = requiredCapabilityIds
        };
    }

    // Read-only compatibility crosswalk for the old, unqualified race-name list.
    // It qualifies only an actually loaded Def and never registers an unknown race.
    public sealed class LegacyRacePresentationDef : Def
    {
        public string raceDefName, defaultMaleProfileId, defaultFemaleProfileId, defaultNoneProfileId;
    }

    [StaticConstructorOnStartup]
    public static class PresentationCatalog
    {
        public static readonly PresentationRegistry Registry;
        static PresentationCatalog()
        {
            var races = new List<RacePresentationData>();
            foreach (var source in DefDatabase<LegacyRacePresentationDef>.AllDefsListForReading)
            {
                var race = DefDatabase<ThingDef>.GetNamedSilentFail(source.raceDefName);
                if (!(race is ThingDef_AlienRace) || race.race?.Humanlike != true) continue;
                // The old BW conversion has separate race masks that have not
                // passed the new adapter's alignment gate. Explicit pack registrations
                // can supply their own validated masked profile in the meantime.
                if (Utilities.RacialBodyTypeInfoUtility.specialRacialTextureSuffixes.ContainsKey(race.defName)) continue;
                races.Add(new RacePresentationData {
                    ProviderPackageId = source.modContentPack.PackageIdPlayerFacing,
                    RacePackageId = race.modContentPack.PackageIdPlayerFacing, RaceDefName = race.defName,
                    AdapterId = "human", Priority = 0,
                    DefaultMaleProfileId = source.defaultMaleProfileId, DefaultFemaleProfileId = source.defaultFemaleProfileId,
                    DefaultNoneProfileId = source.defaultNoneProfileId
                });
            }
            races.AddRange(DefDatabase<RacePresentationDef>.AllDefsListForReading.Select(d => d.ToData()));
            Registry = PresentationRegistry.Build(
                DefDatabase<BodyPresentationProfileDef>.AllDefsListForReading.Select(d => d.ToData(d.modContentPack.PackageIdPlayerFacing)),
                races, LoadedModManager.RunningModsListForReading.Select(m => m.PackageIdPlayerFacing),
                path => !ContentFinder<Texture2D>.Get(path, false).NullOrBad(), 1);
            foreach (var diagnostic in Registry.Diagnostics)
                Log.Warning("[RimRound presentation] " + diagnostic.Code + ": " + diagnostic.Subject + "; " + diagnostic.Detail);
        }
    }
}
