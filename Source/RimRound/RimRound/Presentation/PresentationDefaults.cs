using System;
using System.Collections.Generic;
using System.Linq;
using RimRound.Utilities;
using Verse;

namespace RimRound.Presentation
{
    public sealed class PresentationDefaultChoice
    {
        public readonly string Mode;
        public readonly string ProfileId;
        internal PresentationDefaultChoice(string mode, string profileId)
        { Mode = mode; ProfileId = profileId; }
    }

    // Colony artwork defaults belong to this Game. Unknown profile IDs remain saved
    // so removing and restoring a provider does not destroy the player's choice.
    public sealed class PresentationDefaults : GameComponent
    {
        private readonly Game game;
        private Dictionary<string, string> modes = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, string> profileIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private long revision;
        public long Revision => revision;

        public PresentationDefaults(Game game) { this.game = game; }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref modes, "rrPresentationDefaultModes", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref profileIds, "rrPresentationDefaultProfiles", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref revision, "rrPresentationDefaultsRevision", 0L);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                modes = new Dictionary<string, string>(modes ?? new Dictionary<string, string>(), StringComparer.Ordinal);
                profileIds = new Dictionary<string, string>(profileIds ?? new Dictionary<string, string>(), StringComparer.Ordinal);
                foreach (string key in modes.Keys.ToArray())
                {
                    string profile;
                    if (modes[key] != "native" && (modes[key] != "profile" ||
                        !profileIds.TryGetValue(key, out profile) || string.IsNullOrWhiteSpace(profile))) modes.Remove(key);
                }
                foreach (string key in profileIds.Keys.ToArray())
                    if (!modes.ContainsKey(key) || modes[key] != "profile") profileIds.Remove(key);
                revision = Math.Max(0L, revision);
            }
        }

        public PresentationDefaultChoice GetChoice(string package, string race, string gender)
        {
            string key = Key(package, race, gender);
            string mode;
            if (key == null || modes == null || !modes.TryGetValue(key, out mode)) return null;
            string profile;
            profile = null;
            profileIds?.TryGetValue(key, out profile);
            return new PresentationDefaultChoice(mode, profile);
        }

        // Explicit game-thread choice changes never advance a biological stage.
        public void SetChoice(string package, string race, string gender, string mode, string profile)
        {
            string key = Key(package, race, gender);
            if (key == null) throw new ArgumentException("An exact package, race and presentation gender are required.");
            if (mode != null && mode != "native" && mode != "profile") throw new ArgumentException("Unknown presentation default mode.", nameof(mode));
            if (mode == "profile" && string.IsNullOrWhiteSpace(profile)) throw new ArgumentException("A profile choice requires a profile ID.", nameof(profile));
            profile = mode == "profile" ? profile : null;
            var previous = GetChoice(package, race, gender);
            if ((previous == null && mode == null) || (previous != null && previous.Mode == mode && previous.ProfileId == profile)) return;
            if (mode == null) modes.Remove(key); else modes[key] = mode;
            if (profile == null) profileIds.Remove(key); else profileIds[key] = profile;
            revision++;
            foreach (var pawn in BodyTypeUtility.CurrentGamePresentationPawns(game))
                if (string.Equals(pawn.def.modContentPack?.PackageIdPlayerFacing, package, StringComparison.OrdinalIgnoreCase) &&
                    pawn.def.defName == race && string.Equals(pawn.gender.ToString(), gender, StringComparison.OrdinalIgnoreCase))
                    PawnPresentationOwner.NotifyChanged(pawn);
        }

        private static string Key(string package, string race, string gender)
        {
            if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(race) ||
                package.Contains("/") || race.Contains("/")) return null;
            string normalizedGender = gender?.ToLowerInvariant();
            if (normalizedGender != "male" && normalizedGender != "female" && normalizedGender != "none") return null;
            return package.ToLowerInvariant() + "/" + race + "/" + normalizedGender;
        }
    }
}
