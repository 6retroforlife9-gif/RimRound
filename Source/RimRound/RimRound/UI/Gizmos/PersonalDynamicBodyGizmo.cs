using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimRound.UI
{
    public class PersonalDynamicBodyGizmo : Command_Toggle
    {
        public PersonalDynamicBodyGizmo(PawnBodyType_ThingComp comp) 
        {
            this.comp = comp;
            defaultLabel = "RR_PersonalDynamicBodyGizmoLabel".Translate();
            defaultDesc = "RR_PersonalDynamicBodyGizmoDesc".Translate();
            icon = Utilities.Resources.FILLER_TEXTURE;
            isActive = () => comp.PersonallyExempt;
            toggleAction = () => { ToggleAction(); };
            Order = 401;
        }


        private void ToggleAction()
        {
            Utilities.Resources.gizmoClick.PlayOneShotOnCamera(null);

            if (comp.PersonallyExempt)
                comp.PersonallyExempt = false;
            else
                comp.PersonallyExempt = new ExemptionReason("RR_PersonallyExemptFromGizmo".Translate());
        }       


        PawnBodyType_ThingComp comp;
    }
}
