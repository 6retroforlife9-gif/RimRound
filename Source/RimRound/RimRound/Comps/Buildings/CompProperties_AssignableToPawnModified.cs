using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Comps.Buildings
{
    public class CompProperties_AssignableToPawnModified : CompProperties_AssignableToPawn
    {
        public CompProperties_AssignableToPawnModified()
        {
            this.compClass = typeof(Comp_AssignableToPawnModified);
            base.maxAssignedPawnsCount = this.maxAssignedPawnsCount;
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            return base.ConfigErrors(parentDef);
        }

        public override void PostLoadSpecial(ThingDef parent)
        {
            // This is intentionally left blank
        }

        public new int maxAssignedPawnsCount = 1;
    }
}
