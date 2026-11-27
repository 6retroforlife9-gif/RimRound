using System;
using System.Collections.Generic;

namespace RimRound.Presentation.Anthrosonae
{
    // Diagnostic v1 shoulder placements extracted from each exact RR side image.
    // Source hashes and authored rule: ANT task audit/wing-fit-trial.json.
    // No placement is validated merely by presence in this table.
    internal static class AnthrosonaeWingFits
    {
        private static readonly Dictionary<string, AppearanceSideJoint> SideJoints =
            new Dictionary<string, AppearanceSideJoint>(StringComparer.Ordinal) {
                { "Things/Pawn/Humanlike/Bodies/Naked_F_005_Thick", new AppearanceSideJoint(-0.103515625f, 0.037109375f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_006_Chonky", new AppearanceSideJoint(-0.119140625f, 0.052734375f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_010_Chubby", new AppearanceSideJoint(-0.142578125f, 0.076171875f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_010a_Chubby", new AppearanceSideJoint(-0.158203125f, 0.076171875f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_020_Corpulent", new AppearanceSideJoint(-0.185546875f, 0.119140625f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_020a_Corpulent", new AppearanceSideJoint(-0.201171875f, 0.119140625f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_030_Fat", new AppearanceSideJoint(-0.177734375f, 0.123046875f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_030a_Fat", new AppearanceSideJoint(-0.185546875f, 0.126953125f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_040_Obese", new AppearanceSideJoint(-0.232421875f, 0.115234375f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_040a_Obese", new AppearanceSideJoint(-0.232421875f, 0.103515625f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_050_MorbidlyObese", new AppearanceSideJoint(-0.326171875f, 0.134765625f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_050a_MorbidlyObese", new AppearanceSideJoint(-0.353515625f, 0.115234375f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_060_Lardy", new AppearanceSideJoint(-0.137695312f, 0.081054688f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_060_LardyAlt", new AppearanceSideJoint(-0.122070312f, 0.112304688f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_060a_Lardy", new AppearanceSideJoint(-0.155273438f, 0.112304688f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_070_Enormous", new AppearanceSideJoint(-0.155273438f, 0.086914062f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_070a_Enormous", new AppearanceSideJoint(-0.254882812f, 0.090820312f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_080_Gigantic", new AppearanceSideJoint(-0.087402344f, 0.047363281f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_080a_Gigantic", new AppearanceSideJoint(-0.128417969f, 0.044433594f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_090_Titanic", new AppearanceSideJoint(-0.075683594f, 0.060058594f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_090a_Titanic", new AppearanceSideJoint(-0.159667969f, 0.065917969f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_100_Gelatinous", new AppearanceSideJoint(-0.200683594f, 0.051269531f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_F_100a_Gelatinous", new AppearanceSideJoint(-0.225097656f, 0.058105469f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_005_Thick", new AppearanceSideJoint(-0.150390625f, 0.037109375f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_005_ThickNew", new AppearanceSideJoint(-0.173828125f, 0.037109375f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_005_ThickOld", new AppearanceSideJoint(-0.150390625f, 0.037109375f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_006_Chonky", new AppearanceSideJoint(-0.134765625f, 0.056640625f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_006_ChonkyNew", new AppearanceSideJoint(-0.166015625f, 0.048828125f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_006_ChonkyOld", new AppearanceSideJoint(-0.134765625f, 0.056640625f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_010_Chubby", new AppearanceSideJoint(-0.107421875f, 0.060546875f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_010_ChubbyNew", new AppearanceSideJoint(-0.142578125f, 0.064453125f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_010_ChubbyOld", new AppearanceSideJoint(-0.107421875f, 0.060546875f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_020_Corpulent", new AppearanceSideJoint(-0.162109375f, 0.107421875f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_020_CorpulentNew", new AppearanceSideJoint(-0.212890625f, 0.115234375f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_020_CorpulentOld", new AppearanceSideJoint(-0.162109375f, 0.107421875f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_030_Fat", new AppearanceSideJoint(-0.146484375f, 0.103515625f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_030_FatNew", new AppearanceSideJoint(-0.193359375f, 0.095703125f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_030_FatOld", new AppearanceSideJoint(-0.146484375f, 0.103515625f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_040_Obese", new AppearanceSideJoint(-0.236328125f, 0.076171875f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_040_ObeseNew", new AppearanceSideJoint(-0.267578125f, 0.099609375f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_040_ObeseOld", new AppearanceSideJoint(-0.236328125f, 0.076171875f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_050_MorbidlyObese", new AppearanceSideJoint(-0.271484375f, 0.126953125f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_050_MorbidlyObeseNew", new AppearanceSideJoint(-0.376953125f, 0.099609375f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_050_MorbidlyObeseOld", new AppearanceSideJoint(-0.271484375f, 0.126953125f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_060_Lardy", new AppearanceSideJoint(-0.133789062f, 0.106445312f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_060_LardyNew", new AppearanceSideJoint(-0.204101562f, 0.104492188f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_060_LardyOld", new AppearanceSideJoint(-0.133789062f, 0.106445312f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_070_Enormous", new AppearanceSideJoint(-0.129882812f, 0.069335938f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_070_EnormousNew", new AppearanceSideJoint(-0.278320312f, 0.065429688f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_070_EnormousOld", new AppearanceSideJoint(-0.129882812f, 0.069335938f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_080_Gigantic", new AppearanceSideJoint(-0.076660156f, 0.030761719f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_080_GiganticNew", new AppearanceSideJoint(-0.159667969f, 0.044433594f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_080_GiganticOld", new AppearanceSideJoint(-0.076660156f, 0.030761719f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_090_Titanic", new AppearanceSideJoint(-0.091308594f, 0.056152344f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_090_TitanicNew", new AppearanceSideJoint(-0.174316406f, 0.046386719f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_090_TitanicOld", new AppearanceSideJoint(-0.091308594f, 0.056152344f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_100_Gelatinous", new AppearanceSideJoint(-0.150878906f, 0.057128906f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_100_GelatinousNew", new AppearanceSideJoint(-0.226074219f, 0.048339844f, -0.084960938f, 0.108398438f) },
                { "Things/Pawn/Humanlike/Bodies/Naked_M_100_GelatinousOld", new AppearanceSideJoint(-0.150878906f, 0.057128906f, -0.084960938f, 0.108398438f) },
            };
        internal static AppearanceSideJoint ForRoot(string root) => SideJoints[root];
    }
}
