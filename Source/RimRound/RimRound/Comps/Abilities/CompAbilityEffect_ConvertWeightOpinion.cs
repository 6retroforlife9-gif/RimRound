using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimRound.Comps
{
    // Retain the old comp identity for saved/external ability references. The
    // prototype was never implemented; current body-guidance abilities are separate.
    public class CompAbilityEffect_ConvertWeightOpinion : CompAbilityEffect
    {
        public new CompProperties_ConvertWeightOpinion Props =>
            (CompProperties_ConvertWeightOpinion)props;

        public override bool HideTargetPawnTooltip => true;
        public override bool ShouldHideGizmo => true;
        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest) => false;
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false) => false;
        public override bool Valid(GlobalTargetInfo target, bool throwMessages = false) => false;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest) { }
        public override void Apply(GlobalTargetInfo target) { }
    }
}
