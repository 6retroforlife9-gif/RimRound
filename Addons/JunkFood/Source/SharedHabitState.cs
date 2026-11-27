using System;

namespace Oinky.JunkFood
{
    // Pure saved arithmetic. Wall time is converted to active ticks by the colony
    // owner; every timer below therefore has exactly the same suspension mask.
    public sealed class SharedHabitState
    {
        public const double Day = 60000d;
        public bool Habit;
        public double Exposure, EligibleFraction, NeedLevel, DeprivedTicks, AbstinentTicks;
        public double ActiveTicks, ScheduleFraction, LastScheduleUse = -1d;
        public int LastWallTick = -1;

        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static double Unit(double value) => Finite(value) ? Math.Max(0d, Math.Min(1d, value)) : 0d;

        public void Normalize()
        {
            Exposure = Unit(Exposure); NeedLevel = Unit(NeedLevel);
            EligibleFraction = Finite(EligibleFraction) ? Math.Max(0d, EligibleFraction % 1d) : 0d;
            ScheduleFraction = Finite(ScheduleFraction) ? Math.Max(0d, ScheduleFraction % 1d) : 0d;
            if (!Finite(ActiveTicks) || ActiveTicks < 0d) ActiveTicks = 0d;
            if (!Finite(LastScheduleUse) || LastScheduleUse > ActiveTicks) LastScheduleUse = -1d;
            if (!Finite(DeprivedTicks) || DeprivedTicks < 0d) DeprivedTicks = 0d;
            if (!Finite(AbstinentTicks) || AbstinentTicks < 0d) AbstinentTicks = 0d;
            if (!Habit) { NeedLevel = 0d; DeprivedTicks = 0d; }
            if (Exposure == 0d) EligibleFraction = 0d;
        }

        // Mode/settings/cure cleanup deliberately retains the shared schedule.
        public void ClearEffects()
        {
            Habit = false; Exposure = EligibleFraction = NeedLevel = 0d;
            DeprivedTicks = AbstinentTicks = 0d;
        }

        public bool Advance(double ticks, bool effects)
        {
            if (!Finite(ticks) || ticks <= 0d) return false;
            ActiveTicks += ticks;
            if (!effects) return false;
            Exposure = Math.Max(0d, Exposure - .03d * ticks / Day);
            if (Exposure <= 1e-12d) { Exposure = 0d; EligibleFraction = 0d; }
            AbstinentTicks += ticks;
            if (AbstinentTicks >= 4d * Day)
            {
                bool had = Habit || Exposure > 0d || EligibleFraction > 0d;
                ClearEffects();
                return had;
            }
            if (Habit)
            {
                double old = NeedLevel;
                NeedLevel = Math.Max(0d, old - .5d * ticks / Day);
                // Native intervals and a single catch-up chunk must land on
                // the same displayed category at exact threshold boundaries.
                if (Math.Abs(NeedLevel - .1d) < 1e-10d) NeedLevel = .1d;
                if (Math.Abs(NeedLevel - .01d) < 1e-10d) NeedLevel = .01d;
                if (old > .1d) DeprivedTicks = 0d;
                // Integrate only the part of this interval at or below .01.
                double untilDeprived = Math.Max(0d, (old - .01d) * Day / .5d);
                DeprivedTicks += Math.Max(0d, ticks - untilDeprived);
                if (Math.Abs(DeprivedTicks - Day) < 1e-7d) DeprivedTicks = Day;
            }
            return false;
        }

        // Return true only on actual acquisition. The RNG is called once per
        // canonical eligible unit, irrespective of stack/bite partitioning.
        public bool Record(double dose, bool effects, Func<bool> acquisitionTrial)
        {
            if (!Finite(dose) || dose <= 0d) return false;
            double total = ScheduleFraction + dose;
            long full = (long)Math.Min(long.MaxValue, Math.Floor(total + 1e-10d));
            ScheduleFraction = Math.Max(0d, total - full);
            if (full > 0) LastScheduleUse = ActiveTicks;
            if (!effects) return false;
            AbstinentTicks = 0d;
            if (Habit)
            {
                Exposure = Math.Min(1d, Exposure + .02d * dose);
                NeedLevel = Math.Min(1d, NeedLevel + .9d * dose);
                if (NeedLevel > .1d) DeprivedTicks = 0d;
                return false;
            }
            double eligible = Math.Max(0d, dose - Math.Max(0d, (.15d - Exposure) / .02d));
            Exposure = Math.Min(1d, Exposure + .02d * dose);
            double earned = EligibleFraction + eligible;
            long trials = (long)Math.Min(long.MaxValue, Math.Floor(earned + 1e-10d));
            EligibleFraction = Math.Max(0d, earned - trials);
            for (long i = 0; i < trials; i++)
            {
                if (acquisitionTrial == null || !acquisitionTrial()) continue;
                Habit = true;
                NeedLevel = 1d; // Acquisition only, never a need constructor.
                DeprivedTicks = EligibleFraction = 0d;
                return true;
            }
            return false;
        }

        public int MoodStage => !Habit || NeedLevel > .1d ? -1 :
            NeedLevel > .01d ? 0 : DeprivedTicks < Day ? 1 : 2;
        public bool ScheduleDue(double days) => Finite(days) && days > 0d &&
            (LastScheduleUse < 0d || ActiveTicks - LastScheduleUse >= days * Day);
    }
}
