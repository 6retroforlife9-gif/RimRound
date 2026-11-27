using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

using RimRound.Comps;
using RimWorld;
using RimRound.Utilities;
using Verse.Sound;
using RimRound.Things;

namespace RimRound.UI
{
    public class MakeBlobIntoBedGizmo : Command_Toggle
    {
        public MakeBlobIntoBedGizmo(MakeBlobIntoBed_ThingComp comp, FullnessAndDietStats_ThingComp fndComp) 
        {
            this.comp = comp;
            defaultLabel = "Blob bed";
            defaultDesc = "Become a soft resting place while alive, above the blob threshold and in another bed. No perk is needed for the basic bed; improved tiers require Legacy. Turning it off leaves weight unchanged.";
            icon = Resources.BLOB_BED_ICON;
            isActive = () => comp.IsBed;
            toggleAction = () => { ToggleAction(); };
            Order = 401;
        }


        private void ToggleAction()
        {
            Resources.gizmoClick.PlayOneShotOnCamera(null);
            if(comp.IsBed||comp.blobBed!=null){comp.ResetBed();return;}
            string reason;if(!comp.TrySpawnBed(out reason))Messages.Message(reason,MessageTypeDefOf.RejectInput,false);
        }




        MakeBlobIntoBed_ThingComp comp;
    }
}
