using System.Collections.Generic;
using RimRound.Comps;

namespace RimRound.Simulation
{
    /// <summary>
    /// Selects one due request without changing the saved order of survivors.
    /// Public queues and their serialized records remain compatible with old saves.
    /// </summary>
    public static class WeightChangeScheduler
    {
        public static bool TryDequeueDue(Queue<WeightGainRequest> requests, int currentTick,
            out WeightGainRequest request)
        {
            request = default(WeightGainRequest);
            int selectedIndex = -1;
            int earliestTick = 0;
            int index = 0;
            foreach (WeightGainRequest candidate in requests)
            {
                // Strict comparison retains arrival order for equal deadlines.
                if (candidate.tickToApplyOn <= currentTick &&
                    (selectedIndex < 0 || candidate.tickToApplyOn < earliestTick))
                {
                    selectedIndex = index;
                    earliestTick = candidate.tickToApplyOn;
                }
                ++index;
            }

            if (selectedIndex < 0)
                return false;

            if (selectedIndex == 0)
            {
                request = requests.Dequeue();
                return true;
            }

            // A bounded pass over the original count removes only the selected
            // request. No allocation, sorting or draining of overdue requests.
            int count = requests.Count;
            for (int i = 0; i < count; ++i)
            {
                WeightGainRequest candidate = requests.Dequeue();
                if (i == selectedIndex)
                    request = candidate;
                else
                    requests.Enqueue(candidate);
            }
            return true;
        }
    }
}
