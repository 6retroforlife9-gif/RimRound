using System;
using System.Collections.Generic;

namespace RimRound.Presentation.Anthrosonae
{
    // ANT-VIS-01: explicit physical artwork, never a weight-size threshold.
    // North attachment pixels are turned 180 degrees into the South image axes.
    // Source hashes/pixel anchors: resume-20261015/rear-pose-manager01.
    // Initial manager-authored fit; native visual acceptance is separate.
    internal static class AnthrosonaeRearFits
    {
        private struct Point
        {
            internal readonly float X, Z;
            internal Point(float x, float z) { X = x; Z = z; }
        }
        private static readonly AppearanceRearFit Hidden = new AppearanceRearFit(AppearanceRearPosePolicy.Hidden);
        private static readonly HashSet<string> CoveredRoots = new HashSet<string>(StringComparer.Ordinal) {
            "Things/Pawn/Humanlike/Bodies/Naked_F_060_Lardy",
            "Things/Pawn/Humanlike/Bodies/Naked_F_060_LardyAlt",
            "Things/Pawn/Humanlike/Bodies/Naked_F_060a_Lardy",
            "Things/Pawn/Humanlike/Bodies/Naked_F_070_Enormous",
            "Things/Pawn/Humanlike/Bodies/Naked_F_070a_Enormous",
            "Things/Pawn/Humanlike/Bodies/Naked_F_080_Gigantic",
            "Things/Pawn/Humanlike/Bodies/Naked_F_080a_Gigantic",
            "Things/Pawn/Humanlike/Bodies/Naked_F_090_Titanic",
            "Things/Pawn/Humanlike/Bodies/Naked_F_090a_Titanic",
            "Things/Pawn/Humanlike/Bodies/Naked_M_060_Lardy",
            "Things/Pawn/Humanlike/Bodies/Naked_M_060_LardyNew",
            "Things/Pawn/Humanlike/Bodies/Naked_M_060_LardyOld",
            "Things/Pawn/Humanlike/Bodies/Naked_M_070_Enormous",
            "Things/Pawn/Humanlike/Bodies/Naked_M_070_EnormousNew",
            "Things/Pawn/Humanlike/Bodies/Naked_M_070_EnormousOld",
            "Things/Pawn/Humanlike/Bodies/Naked_M_080_Gigantic",
            "Things/Pawn/Humanlike/Bodies/Naked_M_080_GiganticNew",
            "Things/Pawn/Humanlike/Bodies/Naked_M_080_GiganticOld",
            "Things/Pawn/Humanlike/Bodies/Naked_M_090_Titanic",
            "Things/Pawn/Humanlike/Bodies/Naked_M_090_TitanicNew",
            "Things/Pawn/Humanlike/Bodies/Naked_M_090_TitanicOld",
        };
        // Tail and wing centres differ on each exposed torso artwork.
        private static readonly Dictionary<string, Point> ExposedJoints = new Dictionary<string, Point>(StringComparer.Ordinal) {
            { "Things/Pawn/Humanlike/Bodies/Naked_F_100_Gelatinous", new Point(0.1987304688f, 0.1674804688f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_100a_Gelatinous", new Point(0.1909179688f, 0.1528320312f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_100_Gelatinous", new Point(0.2094726562f, 0.1723632812f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_100_GelatinousNew", new Point(0.2133789062f, 0.1772460938f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_100_GelatinousOld", new Point(0.2055664062f, 0.1723632812f) },
        };
        private static readonly Dictionary<string, Point> NativeNorthJoints = new Dictionary<string, Point>(StringComparer.Ordinal) {
            { "bear-tail", new Point(0f, 0.19453125f) },
            { "cat-tail", new Point(-0.0341796875f, 0.2021484375f) },
            { "cow-tail", new Point(-0.02734375f, 0.2138671875f) },
            { "deer-tail", new Point(0.0146484375f, 0.3798828125f) },
            { "drake-tail", new Point(-0.0244140625f, 0.2021484375f) },
            { "fox-tail", new Point(-0.025390625f, 0.2060546875f) },
            { "goat-tail", new Point(0.0068359375f, 0.1669921875f) },
            { "griff-tail", new Point(-0.029296875f, 0.2158203125f) },
            { "horse-tail", new Point(-0.015625f, 0.3896484375f) },
            { "hyena-tail", new Point(-0.0341796875f, 0.2021484375f) },
            { "panda-tail", new Point(0f, 0.19453125f) },
            { "rabbit-tail", new Point(-0.0068359375f, 0.3173828125f) },
            { "shark-tail", new Point(0.0068359375f, 0.3935546875f) },
            { "wah-tail", new Point(-0.0244140625f, 0.2041015625f) },
            { "wolf-tail", new Point(0f, 0.2177734375f) },
            { "griff-backbone", new Point(0f, 0.2373046875f) },
        };
        // ANT-VIS-03: authored North joints for the 26 giant roots only.
        // Point.X holds tail Z; Point.Z holds wing Z, as in ExposedJoints.
        // Independent original-pixel evidence: north-fit-manager01/north-body-joints.tsv.
        private static readonly Dictionary<string, Point> NorthBodyJoints = new Dictionary<string, Point>(StringComparer.Ordinal) {
            { "Things/Pawn/Humanlike/Bodies/Naked_F_060_Lardy", new Point(-0.0322265625f, 0.1279296875f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_060_LardyAlt", new Point(-0.0322265625f, 0.1279296875f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_060a_Lardy", new Point(-0.0224609375f, 0.1181640625f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_070_Enormous", new Point(-0.0595703125f, 0.0888671875f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_070a_Enormous", new Point(-0.0595703125f, 0.0927734375f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_080_Gigantic", new Point(-0.0571289062f, 0.0454101562f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_080a_Gigantic", new Point(-0.0581054688f, 0.0444335938f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_090_Titanic", new Point(-0.0229492188f, 0.0747070312f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_090a_Titanic", new Point(-0.0493164062f, 0.0747070312f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_100_Gelatinous", new Point(-0.0639648438f, 0.0454101562f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_F_100a_Gelatinous", new Point(-0.1108398438f, 0.0356445312f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_060_Lardy", new Point(-0.0087890625f, 0.1318359375f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_060_LardyNew", new Point(-0.0283203125f, 0.1181640625f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_060_LardyOld", new Point(-0.0087890625f, 0.1318359375f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_070_Enormous", new Point(-0.0830078125f, 0.0986328125f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_070_EnormousNew", new Point(-0.0751953125f, 0.0888671875f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_070_EnormousOld", new Point(-0.0830078125f, 0.0986328125f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_080_Gigantic", new Point(-0.0600585938f, 0.0483398438f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_080_GiganticNew", new Point(-0.0532226562f, 0.0454101562f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_080_GiganticOld", new Point(-0.0600585938f, 0.0483398438f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_090_Titanic", new Point(-0.0229492188f, 0.0717773438f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_090_TitanicNew", new Point(-0.0493164062f, 0.0717773438f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_090_TitanicOld", new Point(-0.0229492188f, 0.0717773438f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_100_Gelatinous", new Point(-0.0893554688f, 0.0405273438f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_100_GelatinousNew", new Point(-0.1010742188f, 0.0346679688f) },
            { "Things/Pawn/Humanlike/Bodies/Naked_M_100_GelatinousOld", new Point(-0.0893554688f, 0.0405273438f) },
        };
        internal static AppearanceSideJoint ForNorthRoot(string root, string family)
        {
            if (!NorthBodyJoints.TryGetValue(root, out var body)) return null;
            Point attachment = NativeNorthJoints[family];
            return new AppearanceSideJoint(0f, family == "griff-backbone" ? body.Z : body.X, attachment.X, attachment.Z);
        }
        internal static AppearanceRearFit ForRoot(string root, string family)
        {
            if (CoveredRoots.Contains(root)) return Hidden;
            if (!ExposedJoints.TryGetValue(root, out var body)) return null;
            Point attachment = NativeNorthJoints[family];
            return new AppearanceRearFit(AppearanceRearPosePolicy.NorthTurned,
                new AppearanceSideJoint(0f, family == "griff-backbone" ? body.Z : body.X, -attachment.X, -attachment.Z));
        }
    }
}
