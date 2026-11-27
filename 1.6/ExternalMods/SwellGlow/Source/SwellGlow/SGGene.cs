using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using RimRound;
using RimRound.Utilities;
using RimRound.Comps;
using HarmonyLib;

namespace SwellGlow
{
    public class SGTraitExtension : DefModExtension
    {
        public WeightOpinion opinionTrait;
    }

    // Opinion genes retain their DefNames and xenogerm data. They do not
    // overwrite a pawn-owned social preference during PostAdd or reload.
    public class SGGene : Gene
    {
    }
}
