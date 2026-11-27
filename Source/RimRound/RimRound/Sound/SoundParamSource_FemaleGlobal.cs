using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimRound.Utilities;
using Verse.Sound;

namespace RimRound.Sound
{
    public class SoundParamSource_FemaleGlobal : SoundParamSource
    {
        public override string Label => "Female global volume multiplier";

        public override float ValueFor(Sample samp)
        {
            return GlobalSettings.soundFemalesGlobalMult.threshold / 100f;
        }
    }
}
