using System;
using System.Collections.Generic;

namespace RimRound.Presentation.Anthrosonae
{
    // Fixed coordinates in original art, not draw-time texture inspection.
    // ANT-VIS-02 audit/tail-side-fit-trial.json records source hashes and points.
    // These side joints do not accept or alter extreme South-pose attachments.
    internal static class AnthrosonaeTailFits
    {
        private struct Point
        {
            internal readonly float X, Z;
            internal Point(float x, float z) { X = x; Z = z; }
        }
        private static readonly Dictionary<string, Point> BodyJoints =
            new Dictionary<string, Point>(StringComparer.Ordinal) {
                { "Things/Pawn/Humanlike/Bodies/Naked_F_005_Thick", new Point(-0.216796875f, -0.255859375f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_006_Chonky", new Point(-0.205078125f, -0.224609375f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_010_Chubby", new Point(-0.236328125f, -0.208984375f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_010a_Chubby", new Point(-0.197265625f, -0.208984375f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_020_Corpulent", new Point(-0.314453125f, -0.181640625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_020a_Corpulent", new Point(-0.259765625f, -0.185546875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_030_Fat", new Point(-0.365234375f, -0.205078125f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_030a_Fat", new Point(-0.318359375f, -0.169921875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_040_Obese", new Point(-0.373046875f, -0.240234375f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_040a_Obese", new Point(-0.314453125f, -0.201171875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_050_MorbidlyObese", new Point(-0.458984375f, -0.248046875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_050a_MorbidlyObese", new Point(-0.408203125f, -0.263671875f) },
                // This silhouette's dropped belly extends below its rear. An
                // alpha-height fraction picks the underside of the belly;
                // the anatomical rear joint is pixel (136,293) in the 512px art.
                { "Things/Pawn/Humanlike/Bodies/Naked_F_060_Lardy", new Point(-0.233398438f, -0.073242188f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_060_LardyAlt", new Point(-0.233398438f, -0.073242188f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_060a_Lardy", new Point(-0.227539062f, -0.071289062f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_070_Enormous", new Point(-0.375976562f, -0.155273438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_070a_Enormous", new Point(-0.305664062f, -0.090820312f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_080_Gigantic", new Point(-0.226074219f, -0.079589844f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_080a_Gigantic", new Point(-0.204589844f, -0.082519531f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_090_Titanic", new Point(-0.279785156f, -0.056152344f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_090a_Titanic", new Point(-0.244628906f, -0.058105469f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_100_Gelatinous", new Point(-0.277832031f, -0.066894531f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_100a_Gelatinous", new Point(-0.354980469f, -0.078613281f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_005_Thick", new Point(-0.216796875f, -0.251953125f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_005_ThickNew", new Point(-0.177734375f, -0.212890625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_005_ThickOld", new Point(-0.216796875f, -0.251953125f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_006_Chonky", new Point(-0.189453125f, -0.228515625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_006_ChonkyNew", new Point(-0.185546875f, -0.224609375f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_006_ChonkyOld", new Point(-0.189453125f, -0.228515625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_010_Chubby", new Point(-0.189453125f, -0.212890625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_010_ChubbyNew", new Point(-0.173828125f, -0.212890625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_010_ChubbyOld", new Point(-0.189453125f, -0.212890625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_020_Corpulent", new Point(-0.318359375f, -0.185546875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_020_CorpulentNew", new Point(-0.255859375f, -0.169921875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_020_CorpulentOld", new Point(-0.318359375f, -0.185546875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_030_Fat", new Point(-0.357421875f, -0.212890625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_030_FatNew", new Point(-0.248046875f, -0.212890625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_030_FatOld", new Point(-0.357421875f, -0.212890625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_040_Obese", new Point(-0.404296875f, -0.263671875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_040_ObeseNew", new Point(-0.345703125f, -0.228515625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_040_ObeseOld", new Point(-0.404296875f, -0.263671875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_050_MorbidlyObese", new Point(-0.396484375f, -0.216796875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_050_MorbidlyObeseNew", new Point(-0.478515625f, -0.259765625f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_050_MorbidlyObeseOld", new Point(-0.396484375f, -0.216796875f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_060_Lardy", new Point(-0.333007812f, -0.094726562f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_060_LardyNew", new Point(-0.284179688f, -0.094726562f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_060_LardyOld", new Point(-0.333007812f, -0.094726562f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_070_Enormous", new Point(-0.340820312f, -0.174804688f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_070_EnormousNew", new Point(-0.364257812f, -0.159179688f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_070_EnormousOld", new Point(-0.340820312f, -0.174804688f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_080_Gigantic", new Point(-0.189941406f, -0.100097656f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_080_GiganticNew", new Point(-0.205566406f, -0.084472656f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_080_GiganticOld", new Point(-0.189941406f, -0.100097656f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_090_Titanic", new Point(-0.259277344f, -0.061035156f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_090_TitanicNew", new Point(-0.234863281f, -0.084472656f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_090_TitanicOld", new Point(-0.259277344f, -0.061035156f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_100_Gelatinous", new Point(-0.211425781f, -0.040527344f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_100_GelatinousNew", new Point(-0.292480469f, -0.089355469f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_100_GelatinousOld", new Point(-0.211425781f, -0.040527344f) },
            };
        private static readonly Dictionary<string, Point> TailJoints =
            new Dictionary<string, Point>(StringComparer.Ordinal) {
                { "bear-tail", new Point(0.385156250f, 0.071093750f) },
                { "cat-tail", new Point(0.385742188f, 0.059570312f) },
                { "cow-tail", new Point(0.385742188f, 0.083007812f) },
                { "deer-tail", new Point(0.391601562f, 0.081054688f) },
                { "drake-tail", new Point(0.403320312f, 0.084960938f) },
                { "fox-tail", new Point(0.395507812f, 0.083007812f) },
                { "goat-tail", new Point(0.391601562f, 0.071289062f) },
                { "griff-tail", new Point(0.393554688f, 0.073242188f) },
                { "horse-tail", new Point(0.391601562f, 0.065429688f) },
                { "hyena-tail", new Point(0.163085938f, 0.143554688f) },
                { "panda-tail", new Point(0.385156250f, 0.071093750f) },
                { "rabbit-tail", new Point(0.381835938f, 0.069335938f) },
                { "shark-tail", new Point(0.379882812f, 0.065429688f) },
                { "wah-tail", new Point(0.411132812f, 0.079101562f) },
                { "wolf-tail", new Point(0.395507812f, 0.071289062f) },
            };
        internal static AppearanceSideJoint ForRoot(string root, string family)
        {
            Point body = BodyJoints[root], tail = TailJoints[family];
            return new AppearanceSideJoint(body.X, body.Z, tail.X, tail.Z);
        }
    }
}
