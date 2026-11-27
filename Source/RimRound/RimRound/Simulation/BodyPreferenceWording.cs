using System;

namespace RimRound.Simulation
{
    [Flags]
    internal enum BodyPreferenceWishRegions
    {
        None = 0, Hips = 1, Legs = 2, Belly = 4, Chest = 8, Arms = 16,
        All = Hips | Legs | Belly | Chest | Arms
    }

    // A display choice computed from the saved pawn and game time. Nothing is
    // rolled or stored when the player opens a thought.
    internal readonly struct BodyPreferenceWording
    {
        internal const int RotationTicks = 3 * BodyEnergyState.TicksPerDay;
        internal const int StageCount = 7;
        private static readonly string[] StageNames = {
            "FarBelow", "Below", "SlightlyBelow", "Preferred", "SlightlyAbove", "Above", "FarAbove"
        };
        private static readonly string[] RegionWishGroups = { "HipWish", "LegWish", "BellyWish", "ChestWish", "ArmWish" };

        private readonly int turn;
        private readonly int stage;
        private readonly BodyPreferenceDescriptionFamily family;

        private BodyPreferenceWording(int turn, int stage, BodyPreferenceDescriptionFamily family)
        {
            this.turn = turn;
            this.stage = stage;
            this.family = family;
        }

        internal static bool TryCreate(int pawnId, int tick, int cachedStage, int liveStage,
            BodyPreferenceDescriptionFamily family, out BodyPreferenceWording choice)
        {
            choice = default;
            if (pawnId <= 0 || tick < 0 || cachedStage < 0 || cachedStage >= StageCount || cachedStage != liveStage ||
                (uint)family > (uint)BodyPreferenceDescriptionFamily.Fat)
                return false;
            uint pawnPhase = unchecked((uint)pawnId * 2654435761u);
            long epoch = ((long)tick + pawnPhase % RotationTicks) / RotationTicks;
            int firstTurn = (int)(pawnPhase >> 16) + cachedStage + StageCount * (int)family;
            choice = new BodyPreferenceWording((int)epoch + firstTurn, cachedStage, family);
            return true;
        }

        internal bool CanUseRegionWish => turn % 3 == 2 &&
            (stage > 3 || (stage < 3 && family == BodyPreferenceDescriptionFamily.Fat));
        internal bool CanUseWalkingDifficulty => turn % 3 == 2 && stage >= 5;

        internal string Resolve(string cachedGeneral, float preferredLowerRatio, BodyPreferenceWishRegions presentRegions,
            bool weightLimitsWalking, Func<string, string> translation)
        {
            string contextKey = ContextKey(preferredLowerRatio, presentRegions, weightLimitsWalking);
            if (contextKey.Length > 0)
            {
                string context = translation(contextKey);
                if (!string.IsNullOrWhiteSpace(context)) return context;
            }

            string primaryKey = "RR_BodyPreference_" + family + "_" + StageNames[stage];
            // On a context turn without a usable line, alternate between both
            // ordinary choices on successive trips through the three slots.
            if (turn % 3 == 1 || (turn % 3 == 2 && (turn / 3) % 2 != 0))
            {
                string alternate = translation(primaryKey + "_Alternate");
                if (!string.IsNullOrWhiteSpace(alternate)) return alternate;
            }
            if (family != BodyPreferenceDescriptionFamily.General)
            {
                string primary = translation(primaryKey);
                if (!string.IsNullOrWhiteSpace(primary)) return primary;
            }
            return cachedGeneral;
        }

        private string ContextKey(float preferredLowerRatio, BodyPreferenceWishRegions presentRegions, bool weightLimitsWalking)
        {
            if (!CanUseRegionWish) return string.Empty;
            bool gaining = stage < 3;
            int eligible = (int)(presentRegions & BodyPreferenceWishRegions.All);
            bool extra = gaining ? preferredLowerRatio >= 16f && preferredLowerRatio <= BodyPreferenceState.MaximumRatio :
                CanUseWalkingDifficulty && weightLimitsWalking;
            int count = extra ? 2 : 0;
            for (int bit = 1; bit <= (int)BodyPreferenceWishRegions.Arms; bit <<= 1)
                if ((eligible & bit) != 0) count++;
            if (count == 0) return string.Empty;
            int selected = (turn / 3) % count;
            for (int i = 0; i < RegionWishGroups.Length; i++)
            {
                if ((eligible & (1 << i)) == 0) continue;
                if (selected-- == 0)
                    return "RR_BodyPreference_" + RegionWishGroups[i] + (gaining ? "_Gain" : "_Loss");
            }
            return "RR_BodyPreference_" + (gaining ? "ExtremeWish" : "WeightStruggle") + (selected == 0 ? "_1" : "_2");
        }
    }
}
