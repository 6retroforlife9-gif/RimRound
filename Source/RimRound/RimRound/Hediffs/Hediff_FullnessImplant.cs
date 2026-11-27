using RimRound.Utilities;
using Verse;

namespace RimRound.Hediffs
{
    public class Hediff_FullnessImplant : Hediff_Implant
    {
        // An old save can contain an implant on a pawn no longer eligible for
        // adult flesh metabolism. Keep the implant, but suspend its stage.
        public override HediffStage CurStage => BodyModelEligibility.HasAdultBiology(pawn) && ProgressionSources.IsActiveImplant(this) ? base.CurStage : null;

        private static readonly RimRoundStatBonuses Bonus = new RimRoundStatBonuses { softLimitMultiplier = 0.20f };
        private static readonly RimRoundStatBonuses HistoricalBonus = new RimRoundStatBonuses { digestionRateMultiplier = 0.5f, weightGainMultiplier = 0.2f };
        private int appliedBonusVersion;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref appliedBonusVersion, "rrAppliedBonusVersion", 0);
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            appliedBonusVersion = 1;
            StatChangeUtility.ChangeOwnedRimRoundStats(pawn, Bonus);
        }

        public override void PostRemoved()
        {
            StatChangeUtility.ChangeOwnedRimRoundStats(pawn,
                ProgressionSources.Negate(appliedBonusVersion == 0 ? HistoricalBonus : Bonus));
            base.PostRemoved();
        }

        public override string TipStringExtra => base.TipStringExtra + "\nCapacity +20%; hunger +15% (native food need).";
    }
}
