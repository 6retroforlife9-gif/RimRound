using RimRound.Utilities;
using Verse;

namespace RimRound.Comps.Buildings
{
    public class Comp_RestingWeightGainMod : ThingComp
    {
        public CompProperties_RestingWeightGainMod Props => (CompProperties_RestingWeightGainMod)props;
        public float GetWeightGainAdditiveModifier()
            => EquipmentQuality.BedBonus(Props.additiveRestingWeightGainModifier, parent);
        public override string CompInspectStringExtra()
            => "RR_RestingGainInspect".Translate(EquipmentQuality.Percent(GetWeightGainAdditiveModifier()));
    }
}
