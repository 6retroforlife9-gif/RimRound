using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimRound.Utilities;
using Verse.Sound;

namespace RimRound.Sound
{
    public class SoundParamSource_StomachSlosh : SoundParamSource
    {
        public override string Label => "Stomach slosh volume";

        public override float ValueFor(Sample samp)
        {
            return GlobalSettings.soundStomachSlosh.threshold / 100f;
        }
    }
}
