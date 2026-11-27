using System;
using System.Collections.Generic;

namespace RimRound.FeedOther
{
    // Game-wide duty cadence survives map changes and is independent of the
    // recreation cooldown. Native serialization owns only this dictionary.
    public sealed class OfficerServiceCadence
    {
        public const int IntervalTicks = 8 * 2500;
        public Dictionary<int, int> Until = new Dictionary<int, int>();

        public bool IsBlocked(int pawnId, int now) => Until.TryGetValue(pawnId, out int until) && now < until;

        public bool TryClaim(int helperId, int recipientId, int now)
        {
            if (now < 0 || helperId == recipientId || IsBlocked(helperId, now) || IsBlocked(recipientId, now)) return false;
            int until = (int)Math.Min(int.MaxValue, (long)now + IntervalTicks);
            Until[helperId] = Until[recipientId] = until;
            return true;
        }

        public void Prune(int now)
        {
            var expired = new List<int>();
            foreach (var pair in Until) if (pair.Value <= now) expired.Add(pair.Key);
            foreach (int id in expired) Until.Remove(id);
        }
    }

    public static class OfficerServiceCollectionBudget
    {
        // Extra *collection* covers digestion during a bounded visit. The live
        // intake ceiling and personal goal energy budget never increase here.
        public static double ExtraVolume(double softLimit, double fullnessMultiplier) =>
            softLimit > 0d && !double.IsNaN(softLimit) && !double.IsInfinity(softLimit) &&
            fullnessMultiplier > .0001d && !double.IsNaN(fullnessMultiplier) && !double.IsInfinity(fullnessMultiplier)
                ? softLimit * .5d : 0d;
    }
}
