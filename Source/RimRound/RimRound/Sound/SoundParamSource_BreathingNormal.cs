using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimRound.Utilities;
using Verse.Sound;

namespace RimRound.Sound
{
    public class SoundParamSource_BreathingNormal : SoundParamSource
    {
        public override string Label => "Breathing volume";

        public override float ValueFor(Sample samp)
        {
            return GlobalSettings.soundRegularBreath.threshold / 100f;
        }
    }
}
