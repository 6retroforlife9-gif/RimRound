using RimRound.Simulation;
using RimRound.Utilities;
using Verse;

namespace RimRound.Comps
{
    public partial class FullnessAndDietStats_ThingComp
    {
        private readonly BodyAdaptationState adaptation = new BodyAdaptationState();
        public float EatingConditioning => adaptation.EatingConditioning;
        public float SizeConditioning => adaptation.SizeConditioning;
        public long ActiveProgressionTicks => adaptation.ActiveTicks;
        private bool CanTrainAdaptation => !ColonyProgression.LegacyPerksActive &&
            parent.Spawned && BodyModelEligibility.IsEligible(parent as Pawn) &&
            BodyModelEligibility.HasColonyManagementScope(parent as Pawn);

        private void ExposeAdaptation()
        {
            Scribe_Values.Look(ref adaptation.SchemaVersion, "rrAdaptationSchema", BodyAdaptationState.CurrentVersion);
            Scribe_Values.Look(ref adaptation.EatingConditioning, "rrEatingConditioning", 0f);
            Scribe_Values.Look(ref adaptation.SizeConditioning, "rrSizeConditioning", 0f);
            Scribe_Values.Look(ref adaptation.WindowTicks, "rrAdaptationWindowTicks", 0);
            Scribe_Values.Look(ref adaptation.ActiveTicks, "rrAdaptationActiveTicks", 0L);
            Scribe_Values.Look(ref adaptation.UsefulNutrition, "rrAdaptationUsefulNutrition", 0d);
            Scribe_Values.Look(ref adaptation.ExpenditureNutrition, "rrAdaptationExpenditure", 0d);
            Scribe_Values.Look(ref adaptation.UsefulCapacityUnits, "rrAdaptationUsefulCapacityUnits", 0d);
        }
        private void RecordAdaptationIntake(float nutrition, float effectiveVolume)
        {
            if (CanTrainAdaptation)
            {
                adaptation.RecordUsefulIntake(nutrition);
                adaptation.RecordUsefulVolume(effectiveVolume, SoftLimit);
            }
        }
        private void AdvanceAdaptation(int ticks, float expenditure)
        {
            if (CanTrainAdaptation && baselineMassKg > 0f)
                adaptation.Advance(ticks, expenditure, ((Pawn)parent).Weight() / baselineMassKg);
        }
        public RimRoundStatBonuses AdaptationBonuses => ColonyProgression.LegacyPerksActive
            ? new RimRoundStatBonuses() : new RimRoundStatBonuses {
                softLimitMultiplier = adaptation.CapacityBonus,
                painMitigationMultBonus_Fullness = adaptation.FullnessPainMitigation
            };
    }
}
