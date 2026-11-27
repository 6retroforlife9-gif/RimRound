using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimRound.Utilities;
using Verse.Sound;

namespace RimRound.Sound
{
    public class SoundParamSource_ZenithOrb : SoundParamSource
    {
        public override string Label => "Zenith Orb volume";

        public override float ValueFor(Sample samp)
        {
            return GlobalSettings.soundZenithOrb.threshold / 100f;
        }
    }
}
