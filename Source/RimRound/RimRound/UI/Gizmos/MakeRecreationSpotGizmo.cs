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
using Resources = RimRound.Utilities.Resources;

namespace RimRound.UI
{
    public class MakeRecreationSpotGizmo : Command_Toggle
    {
        public MakeRecreationSpotGizmo(MakeBlobIntoBed_ThingComp comp) 
        {
            this.comp = comp;
            defaultLabel = "Recreation Spot";
            defaultDesc = "Legacy We Like To Party: become a recreation spot while alive, above the blob threshold and in bed. Turning it off leaves weight unchanged.";
            icon = Resources.RECREATION_SPOT_ICON;
            isActive = () => comp.IsRecSpot;
            toggleAction = () => { ToggleAction(); };
            Order = 402;
        }

        private void ToggleAction() 
        {
            Resources.gizmoClick.PlayOneShotOnCamera(null);
            if(comp.IsRecSpot||comp.recSpot!=null){Reset();return;}
            string reason;if(!comp.TrySpawnRecreation(out reason))Messages.Message(reason,MessageTypeDefOf.RejectInput,false);
        }

        public void Reset()
        {
            comp.ResetRecreation();

        }

        MakeBlobIntoBed_ThingComp comp;
    }
}
