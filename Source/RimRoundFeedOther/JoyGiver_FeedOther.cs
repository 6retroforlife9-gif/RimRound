using RimWorld;
using Verse;
using Verse.AI;
namespace RimRound.FeedOther
{
    // Retain the XML/save type; the map coordinator owns all automatic invitations.
    public class JoyGiver_FeedOther : JoyGiver
    {
        public override float GetChance(Pawn pawn) => 0f;
        public override Job TryGiveJob(Pawn pawn) => null;
    }
}
