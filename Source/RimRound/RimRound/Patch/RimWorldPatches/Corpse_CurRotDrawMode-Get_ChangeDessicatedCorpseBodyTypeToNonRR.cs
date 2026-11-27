namespace RimRound.Patch
{
    // Kept as the cache-reset compatibility entry point. Corpse render getters
    // must not change biological state or native body identity.
    public static class Corpse_CurRotDrawMode_Get_ChangeDessicatedCorpseBodyTypeToNonRR
    {
        public static void InvalidateCache() { }
    }
}
