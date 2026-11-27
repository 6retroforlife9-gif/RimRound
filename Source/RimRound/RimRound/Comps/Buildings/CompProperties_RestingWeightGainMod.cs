using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Comps.Buildings
{
    public class CompProperties_RestingWeightGainMod : CompProperties
    {
        public CompProperties_RestingWeightGainMod()
        {
            this.compClass = typeof(Comp_RestingWeightGainMod);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            return base.ConfigErrors(parentDef);
        }

        public float additiveRestingWeightGainModifier = 0;
    }
}
