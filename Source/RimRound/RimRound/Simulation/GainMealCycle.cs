using System;

namespace RimRound.Simulation
{
    public enum GainMealOwner { None, OrdinaryJob, ConnectedFeeder }

    // Permission state only: this neither creates calories nor owns food stock.
    public sealed class GainMealCycle
    {
        public const float StartFoodFraction = .25f;
        public const int MaximumSessionTicks = 10000;
        public const int MaximumIdleTicks = 6000;
        public const int RetryDelayTicks = 1200;
        public const int FollowupWindowTicks = 300;
        public bool Active;
        public GainMealOwner Owner;
        public int StartedTick;
        public int ActivityTick;
        public int RetryAfterTick;
        public bool RearmRequired;
        public bool AwaitingFollowup;
        public int ProgressVersion;
        public bool HungerStarted;
        public bool SocialFollowup;
        public int SocialRetryAfterTick;

        // Timetable permission is supplied by the native adapter. Connected feeders
        // retain the older hunger cycle below and do not inherit a pawn timetable.
        public bool CanStartPermitted(int now, float foodFraction, bool eligible, bool hasRoom, bool permitted)
        {
            Observe(now, foodFraction, eligible, hasRoom);
            return !Active && eligible && hasRoom && permitted && Finite(foodFraction) && now >= RetryAfterTick;
        }

        public bool BeginPermitted(int now, float foodFraction, bool eligible, bool hasRoom, bool permitted, bool hungry)
        {
            if (!CanStartPermitted(now, foodFraction, eligible, hasRoom, permitted)) return false;
            Active = true; Owner = GainMealOwner.OrdinaryJob; StartedTick = ActivityTick = now;
            HungerStarted = hungry; AwaitingFollowup = false; RearmRequired = false;
            return true;
        }

        public void Observe(int now, float foodFraction, bool eligible, bool hasRoom)
        {
            if (Finite(foodFraction) && foodFraction > StartFoodFraction) RearmRequired = false;
            if (!Active) return;
            if (!eligible || !Finite(foodFraction) || now < StartedTick ||
                now - StartedTick >= MaximumSessionTicks || now - ActivityTick >= MaximumIdleTicks ||
                (AwaitingFollowup && !SocialFollowup && now - ActivityTick > FollowupWindowTicks))
                End(now, false);
            else if (!hasRoom) End(now, true);
        }

        public bool CanStart(int now, float foodFraction, bool eligible, bool hasRoom)
        {
            Observe(now, foodFraction, eligible, hasRoom);
            return !Active && eligible && hasRoom && Finite(foodFraction) &&
                foodFraction <= StartFoodFraction && !RearmRequired && now >= RetryAfterTick;
        }

        public bool Begin(int now, float foodFraction, bool eligible, bool hasRoom, GainMealOwner owner)
        {
            if (owner == GainMealOwner.None || !CanStart(now, foodFraction, eligible, hasRoom)) return false;
            Active = true; Owner = owner; StartedTick = ActivityTick = now; AwaitingFollowup = false;
            return true;
        }

        public void Progress(int now)
        {
            if (!Active) return;
            ActivityTick = now; ProgressVersion++;
        }

        public void WaitForFollowup(int now)
        {
            if (!Active) return;
            ActivityTick = now; AwaitingFollowup = true;
        }

        public void End(int now, bool completed)
        {
            Active = false; Owner = GainMealOwner.None; AwaitingFollowup = false;
            HungerStarted = false;
            SocialFollowup = false;
            if (completed) RearmRequired = true;
            else RetryAfterTick = now + RetryDelayTicks;
        }

        public void EndAfterReload(int now)
        {
            if (!Active) return;
            End(now, false);
            RearmRequired = true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
