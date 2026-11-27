using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimRound.Utilities;
using Verse.Sound;

namespace RimRound.Sound
{
    public class SoundParamSource_FeedingMachineOn : SoundParamSource
    {
        public override string Label => "Feeding machine volume";

        public override float ValueFor(Sample samp)
        {
            return GlobalSettings.soundFeedingMachine.threshold / 100f;
        }
    }
}
