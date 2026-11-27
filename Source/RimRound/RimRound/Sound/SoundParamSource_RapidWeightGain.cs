using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimRound.Utilities;
using Verse.Sound;

namespace RimRound.Sound
{
    public class SoundParamSource_RapidWeightGain : SoundParamSource
    {
        public override string Label => "Rapid weight gain volume";

        public override float ValueFor(Sample samp)
        {
            return GlobalSettings.soundRapidWeightGain.threshold / 100f;
        }
    }
}
