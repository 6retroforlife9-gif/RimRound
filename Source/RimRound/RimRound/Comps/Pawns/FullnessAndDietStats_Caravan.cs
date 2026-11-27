namespace RimRound.Comps
{
    public partial class FullnessAndDietStats_ThingComp
    {
        // Pawn-owned duplicate-entry guard for native/Vehicle Framework hooks.
        // Restoration uses the existing saved preCaravanDietMode field, so a
        // loaded caravan does not depend on this transient value or a registry.
        internal bool CaravanDietModeSuspended;
    }
}
