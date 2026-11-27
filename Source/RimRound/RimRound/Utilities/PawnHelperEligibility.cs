using RimWorld;
using Verse;

namespace RimRound.Utilities
{
    public sealed class HelperIdentityExtension : DefModExtension
    {
        // Constructed adults can be newly manufactured. Native developmental
        // stage must still be Adult; this never grants biological eligibility.
        public bool constructedAdult;
    }

    // Acting for another pawn does not require the actor to metabolize food.
    // Eating, body goals and personal preferences keep BodyModelEligibility.
    public static class PawnHelperEligibility
    {
        public static bool IsAdultHumanlike(Pawn pawn) => pawn != null && !pawn.Dead &&
            !pawn.Destroyed && pawn.RaceProps?.Humanlike == true && !pawn.IsSubhuman &&
            pawn.ageTracker != null && pawn.DevelopmentalStage == DevelopmentalStage.Adult &&
            (pawn.ageTracker.AgeBiologicalYears >= 18 || IsConstructedAdult(pawn));

        private static bool IsConstructedAdult(Pawn pawn)
        {
            if (pawn.def.GetModExtension<HelperIdentityExtension>()?.constructedAdult == true) return true;
            var genes = pawn.genes?.GenesListForReading;
            if (genes != null)
                for (int i = 0; i < genes.Count; i++)
                    if (genes[i].Active && genes[i].def.GetModExtension<HelperIdentityExtension>()?.constructedAdult == true) return true;
            return false;
        }

        // Pawn creates its interaction tracker after comp PostSpawnSetup. Taste
        // initialization needs stable identity; operational discussions check
        // that live tracker separately when starting and committing the job.
        public static bool HasSocialIdentity(Pawn pawn) => IsAdultHumanlike(pawn) &&
            pawn.story?.traits != null && pawn.needs?.mood != null;

        public static bool OrdinaryColonist(Pawn pawn) => IsAdultHumanlike(pawn) &&
            pawn.Faction == Faction.OfPlayer && pawn.IsColonist && !pawn.IsPrisoner &&
            !pawn.IsSlave && !pawn.IsQuestLodger() && AllowsDirectedHelp(pawn);

        public static bool AllowsDirectedHelp(Pawn pawn)
        {
            if (pawn == null) return false;
            // VRE Androids disables ordered control in its float-menu patch,
            // rather than IsColonistPlayerControlled. Our gizmos/jobs must also
            // honor that active gene; no dependency on its assembly is needed.
            var genes = pawn.genes?.GenesListForReading;
            if (genes != null)
                for (int i = 0; i < genes.Count; i++)
                    if (genes[i].Active && genes[i].def.defName == "VREA_Uncontrollable") return false;
            return true;
        }

        public static bool NotHungry(Pawn pawn) => pawn != null &&
            (pawn.needs?.food == null || pawn.needs.food.CurLevelPercentage > pawn.needs.food.PercentageThreshHungry);
    }
}
