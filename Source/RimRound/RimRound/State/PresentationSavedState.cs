using System;
using System.Collections.Generic;

namespace RimRound.State
{
    /// <summary>Pure conversion of the old label/archetype save fields. Unknown values are data.</summary>
    public static class PresentationSavedState
    {
        private static readonly Dictionary<string, string> LegacyProfiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Bamboo's Set (Standard size)", "rimround.bamboo.full" },
            { "ArtOfFire1's Set (Standard size)", "rimround.artoffire.full" },
            { "Bamboo's Set (0.9 size)", "rimround.bamboo.090" },
            { "Bamboo's Set (0.7 size)", "rimround.bamboo.070" },
            { "ArtOfFire1's Set (0.9 size)", "rimround.artoffire.090" },
            { "ArtOfFire1's Set (0.7 size)", "rimround.artoffire.070" },
            { "Bamboo's Set (Ratkin only)", "rimround.bamboo.ratkin" },
            { "Bamboo's Set (Anty only)", "rimround.bamboo.anty" }
        };

        public static string LegacyProfileId(string label)
        {
            if (label == null) return null;
            string id;
            return LegacyProfiles.TryGetValue(label, out id) ? id : label;
        }

        public static string NormalMode(string mode)
        {
            return mode == "default" || mode == "override" || mode == "native" ? mode : null;
        }

        public static void Migrate(ref int schema, ref string profileId, ref string shapeId,
            ref string mode, string legacyLabel, string legacyShape)
        {
            if (schema >= 1) return;
            // A legacy custom label is an explicit choice even when its pack has gone.
            if (profileId == null && legacyLabel != null) profileId = LegacyProfileId(legacyLabel);
            if (shapeId == null && legacyShape != null) shapeId = legacyShape;
            if (mode == null) mode = profileId == null ? "default" : "override";
            schema = 1;
        }

        public static string EffectiveMaleProfileId(string profileId, bool legacySprites, bool alternateSprites)
        {
            if (profileId == null) return null;
            // The old male texture switch only targeted the ArtOfFire M_* sets.
            if (profileId != "rimround.artoffire.full" && profileId != "rimround.artoffire.090" &&
                profileId != "rimround.artoffire.070") return profileId;
            if (legacySprites) return profileId + ".legacy";
            if (alternateSprites) return profileId + ".alternate";
            return profileId;
        }

        public static string EffectiveShapeId(string requestedShapeId, bool standardOnly)
        {
            return standardOnly ? "standard" : requestedShapeId;
        }
    }

    public static class NativeBodySavedState
    {
        public static bool TryCapture(ref string nativeDefName, ref string provenance,
            string candidateDefName, bool isRimRoundBody, bool raceValid, string source)
        {
            if (string.IsNullOrEmpty(candidateDefName) || isRimRoundBody || !raceValid)
                return false;
            if (source != "captured" && source != "observed-native" && source != "legacy-inferred")
                return false;
            if (nativeDefName == candidateDefName && provenance == source) return false;
            nativeDefName = candidateDefName;
            provenance = source;
            return true;
        }
    }
}
