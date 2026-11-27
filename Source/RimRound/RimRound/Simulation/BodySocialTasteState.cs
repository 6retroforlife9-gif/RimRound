using System;
using System.Globalization;
using System.Xml;

namespace RimRound.Simulation
{
    /// <summary>
    /// Fixed, pawn-owned social taste. Raw presence and nullable integers are
    /// independent: only two absent nodes permit deterministic initialization.
    /// The native adapter owns lifecycle and saving; reads never initialize.
    /// </summary>
    public sealed class BodySocialTasteState
    {
        public const int CurrentVersion = 1;
        public const string SchemaKey = "rrSocialBodyTasteSchema";
        public const string LevelKey = "rrSocialBodyTasteLevel";

        public int? SchemaVersion { get; private set; }
        public int? Level { get; private set; }
        public bool SchemaPresent { get; private set; }
        public bool LevelPresent { get; private set; }

        // Diagnostics retain malformed text during this load only. Native
        // typed re-saving retains a present-null marker, not the original text.
        public string MalformedSchemaText { get; private set; }
        public string MalformedLevelText { get; private set; }

        public bool IsUsable => SchemaPresent && LevelPresent &&
            SchemaVersion == CurrentVersion && Level >= 0 && Level <= 4;
        public int UsableLevel => IsUsable ? Level.Value : -1;

        public void LoadRawFields(XmlNode schemaNode, XmlNode levelNode)
        {
            SchemaPresent = schemaNode != null;
            LevelPresent = levelNode != null;
            string malformedSchema, malformedLevel;
            SchemaVersion = ReadInteger(schemaNode, out malformedSchema);
            Level = ReadInteger(levelNode, out malformedLevel);
            MalformedSchemaText = malformedSchema;
            MalformedLevelText = malformedLevel;
        }

        public bool TryInitialize(int pawnId, bool hasAdultBiology)
            => TryInitialize(pawnId, hasAdultBiology, false);

        public bool TryInitialize(int pawnId, bool hasAdultBiology, bool embodiedAbundance)
        {
            if (SchemaPresent || LevelPresent || !hasAdultBiology) return false;
            uint hash;
            int bucket, level;
            if (!TryGetSeed(pawnId, out hash, out bucket, out level)) return false;
            SchemaVersion = CurrentVersion;
            // Initial culture raises only the generated floor. Saved presence
            // wins above, and the existing Strongly likes seed stays unchanged.
            Level = embodiedAbundance ? Math.Max(3, level) : level;
            SchemaPresent = LevelPresent = true;
            return true;
        }

        public static bool TryGetSeed(int pawnId, out uint hash, out int bucket, out int level)
        {
            hash = 0u;
            bucket = level = -1;
            if (pawnId <= 0) return false;
            unchecked
            {
                uint x = (uint)pawnId ^ 0x53424F31u;
                x ^= x >> 16; x *= 0x7FEB352Du;
                x ^= x >> 15; x *= 0x846CA68Bu;
                x ^= x >> 16;
                hash = x;
            }
            bucket = (int)(hash % 100u);
            level = bucket < 10 ? 0 : bucket < 30 ? 1 : bucket < 70 ? 2 : bucket < 90 ? 3 : 4;
            return true;
        }

        private static int? ReadInteger(XmlNode node, out string malformedText)
        {
            malformedText = null;
            if (node == null) return null;
            // Match the native nullable Scribe interpretation of IsNull.
            if (string.Equals(node.Attributes?["IsNull"]?.Value, "true",
                StringComparison.InvariantCultureIgnoreCase)) return null;
            int value;
            if (int.TryParse(node.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return value;
            malformedText = node.InnerText;
            return null;
        }
    }

    /// <summary>One capped body component and exactly one native thought stage.</summary>
    public static class BodySocialTastePolicy
    {
        private static readonly int[] MildValues = { -5, -2, 0, 2, 5 };
        private static readonly int[] FullValues = { -10, -5, 0, 5, 10 };

        public static int StageFor(int level, float massKg, float baselineKg,
            bool caresAboutAppearance, bool embodiedAbundance)
        {
            if (level < 0 || level > 4 || !FinitePositive(massKg) || !FinitePositive(baselineKg))
                return -1;
            // Double division avoids overflowing a finite float mass/baseline
            // ratio; every extreme qualifying body still receives the same cap.
            double ratio = (double)massKg / baselineKg;
            if (ratio < 1.5d) return -1;
            int personal = caresAboutAppearance ? (ratio < 2d ? MildValues[level] : FullValues[level]) : 0;
            if (embodiedAbundance && personal < 5) return 6;
            switch (personal)
            {
                case -10: return 0;
                case -5: return 1;
                case -2: return 2;
                case 2: return 3;
                case 5: return 4;
                case 10: return 5;
                default: return -1;
            }
        }

        private static bool FinitePositive(float value) => value > 0f &&
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
