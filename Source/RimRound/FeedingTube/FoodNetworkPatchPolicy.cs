namespace RimRound.FeedingTube
{
    // Core owns the classic Harmony hooks for the lifetime of the process.
    // The companion selects which food network is active at runtime.
    public static class FoodNetworkPatchPolicy
    {
        public static bool LegacyPatchesEnabled { get; set; } = true;
    }
}
