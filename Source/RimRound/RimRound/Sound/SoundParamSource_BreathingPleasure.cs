using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimRound.Utilities;
using Verse.Sound;

namespace RimRound.Sound
{
    public class SoundParamSource_BreathingPleasure : SoundParamSource
    {
        public override string Label => "Pleasure breath volume";

        public override float ValueFor(Sample samp)
        {
            return GlobalSettings.soundPleasureBreath.threshold / 100f;
        }
    }
}
