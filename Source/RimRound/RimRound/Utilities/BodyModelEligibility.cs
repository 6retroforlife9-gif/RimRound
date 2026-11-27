using RimWorld;
using Verse;

namespace RimRound.Utilities
{
    public interface IBodyFoodVolume
    {
        // Negative means this serving has no custom density and should use its
        // food Def's ordinary volume. Zero is a valid volume-free food.
        float FullnessPerNutrition { get; }
    }

    // Both network implementations use the same saved per-item serving. The
    // core assembly depends on this contract, not on the companion assembly.
    public interface INetworkFoodServing : IBodyFoodVolume
    {
        bool TryInitializeServing(float nutrition, float fullnessPerNutrition);
        bool TryInitializePreservation(FoodPreservationStamp stamp);
        bool TryReadPreservation(out FoodPreservationStamp stamp);
    }

    // Optional quantity contract for returning a serving to its source.
    // Older serving implementations remain edible and can be placed physically.
    public interface INetworkFoodServingQuantity
    {
        float RepresentedNutrition { get; }
    }

    // Races or active genes can opt out of biological food metabolism without
    // a dependency on their mod. Baseline mass declarations belong to races.
    public class BodyMetabolismExtension : DefModExtension
    {
        public bool usesFoodMetabolism = true;
        public float baselineMassKg = 0f;
    }

    public static class BodyModelEligibility
    {
        public static bool IsEligible(Pawn pawn)
        {
            return HasAdultBiology(pawn) && !pawn.Dead && pawn.needs?.food != null;
        }

        // Generation asks before the pawn's needs have been constructed.
        public static bool HasAdultBiology(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null &&
                pawn.RaceProps.Humanlike && pawn.RaceProps.IsFlesh &&
                pawn.ageTracker != null &&
                pawn.DevelopmentalStage == DevelopmentalStage.Adult &&
                pawn.ageTracker.AgeBiologicalYears >= 18 && UsesFoodMetabolism(pawn);
        }

        // Age-independent: reserve demand also protects children, while a
        // synthetic helper's suppressed Food binding must not create demand.
        public static bool UsesFoodMetabolism(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.def.GetModExtension<BodyMetabolismExtension>()?.usesFoodMetabolism == false)
                return false;

            // Needs may not exist during generation. Native Active also handles
            // overridden, age-limited and mutant-disabled genes. Read it afresh
            // so a temporary transition suspends biology without deleting state.
            var genes = pawn.genes?.GenesListForReading;
            if (genes != null)
                for (int i = 0; i < genes.Count; i++)
                    if (genes[i].Active &&
                        genes[i].def.GetModExtension<BodyMetabolismExtension>()?.usesFoodMetabolism == false)
                        return false;
            return true;
        }

        // Ownership persists through mental states and other temporary losses
        // of control. It is not permission to give the pawn an order now.
        public static bool HasColonyManagementScope(Pawn pawn)
        {
            return HasAdultBiology(pawn) && !pawn.IsQuestLodger() &&
                (pawn.IsColonist || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony);
        }

        public static bool CanManage(Pawn pawn)
        {
            return IsEligible(pawn) && HasColonyManagementScope(pawn) && !pawn.InMentalState &&
                (pawn.IsColonistPlayerControlled || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony);
        }

        public static bool CanFollowBodyGoal(Pawn pawn)
        {
            return CanManage(pawn) && !pawn.Drafted;
        }
    }
}
