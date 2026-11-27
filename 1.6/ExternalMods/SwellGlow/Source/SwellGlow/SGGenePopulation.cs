using Verse;

namespace SwellGlow
{
    // Kept for binary compatibility. SGTraits owns the active Harmony patches.
    public static class SwellGlow_Startup
    {
    }

    // Retain the saved component type for existing colonies. Opinion genes
    // remain available through normal Biotech gene play, but loading a game
    // must not assign a random endogene to every pawn.
    public class SwellGlow_GameComponent : GameComponent
    {
        public SwellGlow_GameComponent(Game game) : base()
        {
        }
    }

    // Older builds exposed this hook even though it was not a Harmony patch.
    // Retain the method name without assigning an opinion gene to new pawns.
    public static class Patch_PawnGenerator_GenerateGenes
    {
        public static void Postfix(Pawn pawn)
        {
        }
    }
}
