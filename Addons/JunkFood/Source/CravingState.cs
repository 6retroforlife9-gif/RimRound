using System;
using System.Collections.Generic;

namespace Oinky.JunkFood
{
    // Pure saved-state logic. The caller supplies only committed useful nutrition.
    public sealed class CravingState
    {
        public const int TicksPerDay = 60000;
        public const double QualifyingNutrition = 0.45;
        public bool Habit;
        public int LastUsefulTick = -1;
        public int LastQualifyingTick = -1;
        public List<int> Days = new List<int>();
        public List<double> Nutrition = new List<double>();

        public void Clear()
        {
            Habit = false;
            LastUsefulTick = LastQualifyingTick = -1;
            Days = new List<int>();
            Nutrition = new List<double>();
        }

        public void Normalize(int now)
        {
            if (Days == null || Nutrition == null || Days.Count != Nutrition.Count ||
                now < LastUsefulTick || now < LastQualifyingTick)
            { Clear(); return; }
            for (int i = 0; i < Days.Count; i++)
                if (Days[i] < 0 || Days[i] > now / TicksPerDay ||
                    double.IsNaN(Nutrition[i]) || double.IsInfinity(Nutrition[i]) || Nutrition[i] < 0)
                { Clear(); return; }
            if (LastUsefulTick >= 0 && (long)now - LastUsefulTick >= 4L * TicksPerDay)
            { Clear(); return; }
            int oldest = now / TicksPerDay - 4;
            for (int i = Days.Count - 1; i >= 0; i--)
                if (Days[i] < oldest) { Days.RemoveAt(i); Nutrition.RemoveAt(i); }
        }

        public bool Record(int now, double usefulNutrition)
        {
            if (now < 0 || usefulNutrition <= 0 || double.IsNaN(usefulNutrition) ||
                double.IsInfinity(usefulNutrition)) return false;
            Normalize(now);
            int day = now / TicksPerDay;
            int i = Days.IndexOf(day);
            if (i < 0) { i = Days.Count; Days.Add(day); Nutrition.Add(0); }
            double before = Nutrition[i];
            // A small cap bounds a corrupt/modded event without changing normal doses.
            double after = Math.Min(1000000d, before + usefulNutrition);
            Nutrition[i] = after;
            LastUsefulTick = now;
            // Each completed 0.45-N dose satisfies the habit. Tiny extra bites cannot
            // reset its clock indefinitely after the day's first qualifying dose.
            if (Math.Floor((after + 1e-9) / QualifyingNutrition) >
                Math.Floor((before + 1e-9) / QualifyingNutrition)) LastQualifyingTick = now;
            int qualifyingDays = 0;
            for (int j = 0; j < Nutrition.Count; j++)
                if (Nutrition[j] + 1e-9 >= QualifyingNutrition) qualifyingDays++;
            if (qualifyingDays >= 3) Habit = true;
            return true;
        }

        public bool IsCraving(int now)
        {
            Normalize(now);
            return Habit && LastQualifyingTick >= 0 &&
                (long)now - LastQualifyingTick >= 2L * TicksPerDay;
        }
    }
}
