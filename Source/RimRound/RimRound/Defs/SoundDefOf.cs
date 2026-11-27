using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Defs
{
    [DefOf]
    public static class SoundDefOf
    {
        // “food machine v2” plays while the pawn is being fed from the feeding machine (hose). 
        public static SoundDef RR_FeedingTube_On;

        // Swallow sound play during use of the feeding machine. Intensity of swallowing depends on the machine setting. No sound plays for lose weight setting
        public static SoundDef RR_FeedingMachine_Swallow_Easy;
        public static SoundDef RR_FeedingMachine_Swallow_Normal;
        public static SoundDef RR_FeedingMachine_Swallow_Labored_Female;
        public static SoundDef RR_FeedingMachine_Swallow_Labored_Female_Pleasure;
        public static SoundDef RR_FeedingMachine_Swallow_Labored_Male;
        public static SoundDef RR_FeedingMachine_Swallow_Labored_Male_Pleasure;

        // Zenith Orb
        public static SoundDef RR_ZenithOrbOnSound;
        public static SoundDef RR_ZenithOrbOffSound;

        // Rapid WG/WL sounds
        public static SoundDef RR_RapidWeightGain; // plays when a pawn is struck with the embiggener weapons, eats the cannot-stop-cake, swallows the fat pills, or  is targeted by the Zenith orb. 
        public static SoundDef RR_RapidWeightLoss;

        public static SoundDef RR_Bwomf_1; // Very thin - chunky
        public static SoundDef RR_Bwomf_2; // Chubby - Obese
        public static SoundDef RR_Bwomf_3; // Obese - Lardy II
        public static SoundDef RR_Bwomf_4; // Enormous - Gigantic II
        public static SoundDef RR_Bwomf_5; // Titanic I - Gelatinous I
        public static SoundDef RR_Bwomf_6; // Gelatinous II+

        // Burp sounds
        public static SoundDef RR_StomachBurp_Light; // Thick - corpulent
        public static SoundDef RR_StomachBurp_Medium; // Fat - Lardy I
        public static SoundDef RR_StomachBurp_Heavy; // Lardy II - Titanic II
        public static SoundDef RR_StomachBurp_SuperHeavy; // Gelatinous+


        // Stomach SFX
        public static SoundDef RR_StomachGurgles_Light; // <= Corpulent // These play all the time, when appropriate
        public static SoundDef RR_StomachGurgles_Medium; // Fat - Lardy I
        public static SoundDef RR_StomachGurgles_Heavy; // Lardy II - Titanic II
        public static SoundDef RR_StomachGurgles_SuperHeavy; // Gelatinous+
        
        public static SoundDef RR_StomachEmpty_Light; // Same as above
        public static SoundDef RR_StomachEmpty_Medium;
        public static SoundDef RR_StomachEmpty_Heavy;
        public static SoundDef RR_StomachEmpty_SuperHeavy;

        public static SoundDef RR_Slosh_Light; // Plays when pawn moves at certain stomach capacities. 1-3L
        public static SoundDef RR_Slosh_Medium; // 3-6L
        public static SoundDef RR_Slosh_Heavy; // 6-10L
        public static SoundDef RR_Slosh_SuperHeavy; // 10+L

        public static SoundDef RR_Stretch_1; // While attached to feeding tube, stretch sounds play based on fullness
        public static SoundDef RR_Stretch_2;
        public static SoundDef RR_Stretch_3;
        public static SoundDef RR_Stretch_4;
        public static SoundDef RR_Stretch_5;

        // Footsteps
        public static SoundDef RR_Footstep_Fat_1; // Each has two sub sounds // These are played while pawn moves // Sounds are labelled by weight stage
        public static SoundDef RR_Footstep_Fat_2;
        public static SoundDef RR_Footstep_Obese_1;
        public static SoundDef RR_Footstep_Obese_2;
        public static SoundDef RR_Footstep_MorbidlyObese_1;
        public static SoundDef RR_Footstep_MorbidlyObese_2;
        public static SoundDef RR_Footstep_Lardy_1;
        public static SoundDef RR_Footstep_Lardy_2;


        // Breath
        public static SoundDef RR_F_BreathLight; // Thick - corpulent // affected by movement
        public static SoundDef RR_F_BreathMedium; // Fat - Lardy I
        public static SoundDef RR_F_BreathHeavy; // Lardy II - Titanic II
        public static SoundDef RR_F_BreathSuperHeavy; // Gelatinous+

        public static SoundDef RR_M_BreathLight; // Thick - corpulent
        public static SoundDef RR_M_BreathMedium; // Fat - Lardy I
        public static SoundDef RR_M_BreathHeavy; // Lardy II - Titanic II
        public static SoundDef RR_M_BreathSuperHeavy; // Gelatinous+

        // Pleasure breath

        /*
            ⦁	Works similarly to breaths. Except these are more tied to a pawn’s weight opinion. 
            ⦁	Pawn with weight opinion “like” - “fanatical” will emit moans of pleasure while their stomach is full and they are gaining weight. Could mix with the regular breaths for variety
            ⦁	A pawn  with a weight option that is less than “fanatical” will eventually stop enjoying gaining weight and will revert to the regular breaths past a certain point. Especially not for the “Gelatinious” stages
            ⦁	A pawn with weight opinion “fantatical” will always enjoy gaining weight and will thus emit more pleasured sounds. 
         */
        public static SoundDef RR_F_PleasureBreathLight; // Thick - corpulent
        public static SoundDef RR_F_PleasureBreathMedium; // Fat - Lardy I
        public static SoundDef RR_F_PleasureBreathHeavy; // Lardy II - Titanic II
        public static SoundDef RR_F_PleasureBreathSuperHeavy; // Gelatinous+

        public static SoundDef RR_M_PleasureBreathLight; // Thick - corpulent
        public static SoundDef RR_M_PleasureBreathMedium; // Fat - Lardy I
        public static SoundDef RR_M_PleasureBreathHeavy; // Lardy II - Titanic II
        public static SoundDef RR_M_PleasureBreathSuperHeavy; // Gelatinous+
    }
}
