using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimRound.Utilities;
using Verse.Sound;

namespace RimRound.Sound
{
    public class SoundParamSource_MaleGlobal : SoundParamSource
    {
        public override string Label => "Male global volume multiplier";

        public override float ValueFor(Sample samp)
        {
            return GlobalSettings.soundMalesGlobalMult.threshold / 100f;
        }
    }
}
