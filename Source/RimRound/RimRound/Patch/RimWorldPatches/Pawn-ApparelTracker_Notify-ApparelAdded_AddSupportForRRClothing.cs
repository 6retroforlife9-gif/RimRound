using HarmonyLib;
using RimRound.Comps;
using RimRound.Defs;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Patch
{
    [HarmonyPatch(typeof(Pawn_ApparelTracker))]
    [HarmonyPatch(nameof(Pawn_ApparelTracker.Notify_ApparelAdded))]
    public class PawnApparelTracker_Notify_ApparelAdded_AddSupportForRRClothing
    {
        public static void Postfix(Apparel apparel, Pawn_ApparelTracker __instance)
        {
            Pawn pawn = __instance.pawn;
            if (pawn is null || !pawn.RaceProps.Humanlike)
                return;

            ClothingModExtension clothingStats = apparel.def.GetModExtension<ClothingModExtension>();
            if (clothingStats is null)
                return;
            

            StatChangeUtility.ChangeOwnedRimRoundStats(pawn, new RimRoundStatBonuses()
            {
                weightGainMultBonus = clothingStats.weightGainMultiplierMultBonus,
                digestionRateMultiplier = clothingStats.digestionSpeedMultBonus,
                stomachElasticityMultiplier = clothingStats.stomachElasticityMultBonus,
                softLimitMultiplier = clothingStats.softLimitMultiplier,
                fullnessGainedMultBonus = clothingStats.fullnessGainedMultiplierBonus,
                movementFlatBonus = clothingStats.flatMoveBonus,
                manipulationFlatBonus = clothingStats.flatManipulationBonus,
                eatingFlatBonus = clothingStats.flatEatingSpeedBonus,
                movementPenaltyMitigationMultBonus_Weight = clothingStats.movementPenaltyMitigationMultBonus_Weight,
                movementPenaltyMitigationMultBonus_Fullness = clothingStats.movementPenaltyMitigationMultBonus_Fullness,
                manipulationPenaltyMitigationMultBonus_Weight = clothingStats.manipulationPenaltyMitigationMultBonus_Weight,
                painMitigationMultBonus_Fullness = clothingStats.painMitigationMultBonus_Fullness,
                eatingSpeedReductionMitigationMultBonus_Fullness = clothingStats.eatingSpeedReductionMitigationMultBonus_Fullness
            });

            // Native apparel invalidates capacities only for equippedStatOffsets.
            // RimRound's mod-extension bonuses also change capacity workers and
            // hediff mitigation; clear their cached levels after the source update.
            pawn.health?.capacities?.Notify_CapacityLevelsDirty();

            if (Scribe.mode == LoadSaveMode.Inactive)
            {
                var profile = apparel.TryGetComp<CompProgressionApparel>();
                if (profile != null) profile.appliedProfileVersion = 1;
            }


            return;
        }
    }
}
