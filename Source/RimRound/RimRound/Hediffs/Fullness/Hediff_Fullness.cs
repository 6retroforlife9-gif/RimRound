using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimRound.Defs;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using RimRound.Comps;

namespace RimRound.Hediffs
{
    public class Hediff_Fullness : Hediff
    {
        /// <summary>
        /// 1 is no mitigation, 0 is full mitigation
        /// </summary>
        /// 

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);

            if (this.pawn.TryGetComp<FullnessAndDietStats_ThingComp>() is null) 
            {
                this.pawn.health.RemoveHediff(this);
            }
        }

        public override string TipStringExtra
        {
            get
            {
                var body = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
                float passengers = body?.PassengerVolume ?? 0f;
                string original = base.TipStringExtra;
                if (passengers <= 0f) return original;
                return (string.IsNullOrEmpty(original) ? "" : original + "\n") + body.PassengerBreakdown(passengers);
            }
        }

        public float PainMigigationFactor
        {
            get 
            {
                var comp = this?.pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
                if (comp is null)
                {
                    Log.Error("Failed to get comp in PersonalFullnessPainMult!");
                    return 1f;
                }
                float statBonusPainMitigation = Mathf.Clamp01(1 - comp.EffectiveStatBonuses.painMitigationMultBonus_Fullness);
                int noPainStillGainLevel = comp?.perkLevels?.EffectLevel("RR_NoPainStillGain_Title") ?? 0;
                float noPainStillGainPainMitigation = Mathf.Clamp01(1 - (0.1f * noPainStillGainLevel));
                return Mathf.Clamp01(noPainStillGainPainMitigation * statBonusPainMitigation);
            }
        }

        /// <summary>
        /// 1 is no mitigation, 0 is full mitigation
        /// </summary>
        public float MovementPenaltyMitigationFactor
        {
            get
            {
                var comp = this?.pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
                if (comp is null)
                {
                    Log.Error("Failed to get comp in PersonalFullnessPainMult!");
                    return 1f;
                }

                return Mathf.Clamp01(1 - comp.EffectiveStatBonuses.movementPenaltyMitigationMultBonus_Fullness);
            }
        }

        /// <summary>
        /// 1 is no mitigation, 0 is full mitigation
        /// </summary>
        public float EatingSpeedPenaltyMitigationFactor
        {
            get
            {
                var comp = this?.pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
                if (comp is null)
                {
                    Log.Error("Failed to get comp in PersonalFullnessPainMult!");
                    return 1f;
                }
                float statBonusEatingSpeedMitigationFactor = comp.EffectiveStatBonuses.eatingSpeedReductionMitigationMultBonus_Fullness;
                return Mathf.Clamp01(1 - statBonusEatingSpeedMitigationFactor);
            }
        }

        public override float PainOffset
        {
            get
            {
                return (base.PainOffset * GlobalSettings.fullnessHediffPainMult.threshold * PainMigigationFactor);
            }
        }
    }
}
