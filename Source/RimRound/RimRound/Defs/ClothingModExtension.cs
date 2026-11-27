using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Defs
{
    public class ClothingModExtension : DefModExtension
    {
        public float weightGainMultiplierMultBonus;
        public float digestionSpeedMultBonus;
        public float stomachElasticityMultBonus;
        public float softLimitMultiplier;
        public float fullnessGainedMultiplierBonus;
        public float flatMoveBonus;
        public float flatManipulationBonus;
        public float flatEatingSpeedBonus;

        //All below should be in range [0, 1]. 1 means full mitigation, 0 means none.
        public float movementPenaltyMitigationMultBonus_Weight;
        public float movementPenaltyMitigationMultBonus_Fullness;
        public float manipulationPenaltyMitigationMultBonus_Weight;
        public float painMitigationMultBonus_Fullness;
        public float eatingSpeedReductionMitigationMultBonus_Fullness;
    }

    public class CompProperties_ProgressionApparel : CompProperties
    {
        public CompProperties_ProgressionApparel()
        {
            compClass = typeof(CompProgressionApparel);
        }
    }

    // A missing value on an old worn item means its last callback used the
    // historical profile. Equipping it again records the revised profile.
    public class CompProgressionApparel : ThingComp
    {
        public int appliedProfileVersion;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref appliedProfileVersion, "rrAppliedApparelProfile", 0);
        }
    }
}
