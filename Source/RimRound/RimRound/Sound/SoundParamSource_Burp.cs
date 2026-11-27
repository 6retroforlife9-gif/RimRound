using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimRound.Utilities;
using Verse.Sound;

namespace RimRound.Sound
{
    public class SoundParamSource_Burp : SoundParamSource
    {
        public override string Label => "Burp volume";

        public override float ValueFor(Sample samp)
        {
            return GlobalSettings.soundBurp.threshold / 100f;
        }
    }
}
