using RimWorld;
using Verse;

namespace RimRound.Utilities
{
    /// <summary>Automatic, reversible health benefits of the current weight stage.</summary>
    public static class StageHealthPerks
    {
        public const int HealingStageIndex = 8; // Obese
        public const int FoodImmunityStageIndex = 11; // Lardy
        public const float RecoveryBonus = 1.1f;

        // Existing RimRound arm/leg/torso health values, including the Gelatinous cap.
        private static readonly float[] PartHealthByStage =
        {
            1f, 1f, 1f, 1.13f, 1.23f, 1.33f, 1.50f, 1.70f, 1.93f, 2.23f,
            2.43f, 2.76f, 3.23f, 3.76f, 4.46f, 5.30f, 6.23f, 7.33f, 8.63f, 10f
        };

        private static int EligibleStage(Pawn pawn)
        {
            if (!BodyModelEligibility.IsEligible(pawn)) return -1;
            return pawn.WeightHediff()?.CurStageIndex ?? -1;
        }

        public static bool HasImprovedHealing(Pawn pawn) => EligibleStage(pawn) >= HealingStageIndex;
        public static bool IsFoodPoisoningImmune(Pawn pawn) => EligibleStage(pawn) >= FoodImmunityStageIndex;

        public static float PartHealthMultiplier(Pawn pawn, BodyPartDef part)
        {
            // Preserve the original label-based match used by the health patch.
            if (part == null || (part.label != BodyPartDefOf.Arm.label &&
                part.label != BodyPartDefOf.Leg.label && part.label != BodyPartDefOf.Torso.label))
                return 1f;
            int index = pawn?.WeightHediff()?.CurStageIndex ?? 0;
            if (index < 0) return 1f;
            return PartHealthByStage[System.Math.Min(index, PartHealthByStage.Length - 1)];
        }

        public static float InjuryRecoveryMultiplier(Hediff injury)
        {
            if (!(injury is Hediff_Injury) || !HasImprovedHealing(injury.pawn)) return 1f;
            return RecoveryBonus * PartHealthMultiplier(injury.pawn, injury.Part?.def);
        }

        public static string StatusText(Pawn pawn)
        {
            int stage = EligibleStage(pawn);
            if (stage < 0) return string.Empty;
            return "RR_StageHealth_Header".Translate() + "\n" +
                (stage >= HealingStageIndex ? "RR_StageHealth_HealingActive" : "RR_StageHealth_HealingInactive").Translate() + "\n" +
                (stage >= FoodImmunityStageIndex ? "RR_StageHealth_FoodActive" : "RR_StageHealth_FoodInactive").Translate();
        }
    }
}
