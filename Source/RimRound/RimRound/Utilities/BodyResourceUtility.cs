using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    public static class BodyResourceUtility
    {
        public static float GetMilkMultiplierByWeight(Pawn p) 
        {
            if (!p.RaceProps.Humanlike) 
            {
                return 1f;
            }

            float weightSeverity = Utilities.HediffUtility.KilosToSeverityWithBaseWeight(p.Weight());
            float multiplier = 1;
            if (weightSeverity > milkMultiplier.Last().First)
            {
                multiplier = 1 + ((milkMultiplier.Last().Second - 1) * GlobalSettings.milkMultiplierForWeight.threshold);
                multiplier = Mathf.Clamp(multiplier, 0, GlobalSettings.maxMilkMultiplier.threshold);
                return multiplier;
            }

            for (int i = 1; i < milkMultiplier.Count - 1; ++i)
            {
                if (weightSeverity < milkMultiplier[i].First)
                {
                    multiplier = 1 + ((milkMultiplier[i - 1].Second - 1) * GlobalSettings.milkMultiplierForWeight.threshold);
                    break;
                }
            }

            multiplier = Mathf.Clamp(multiplier, 0, GlobalSettings.maxMilkMultiplier.threshold);
            return multiplier;
        }

        static List<Pair<float, float>> milkMultiplier = new List<Pair<float, float>>()
        {
            new Pair<float, float>( 0.000f, 0.90f  ),
            new Pair<float, float>( 0.005f, 0.95f  ),
            new Pair<float, float>( 0.015f, 1.00f  ),
            new Pair<float, float>( 0.035f, 1.05f  ),
            new Pair<float, float>( 0.050f, 1.05f  ),
            new Pair<float, float>( 0.065f, 1.10f  ),
            new Pair<float, float>( 0.090f, 1.15f  ),
            new Pair<float, float>( 0.120f, 1.20f  ),
            new Pair<float, float>( 0.155f, 1.25f  ),
            new Pair<float, float>( 0.200f, 1.35f  ),
            new Pair<float, float>( 0.230f, 1.40f  ),
            new Pair<float, float>( 0.280f, 1.45f  ),
            new Pair<float, float>( 0.350f, 1.60f  ),
            new Pair<float, float>( 0.430f, 1.75f  ),
            new Pair<float, float>( 0.535f, 1.90f  ),
            new Pair<float, float>( 0.660f, 2.15f  ),
            new Pair<float, float>( 0.800f, 2.40f  ),
            new Pair<float, float>( 0.965f, 2.70f  ),
            new Pair<float, float>( 1.160f, 3.05f  ),
            new Pair<float, float>( 1.410f, 3.50f  ),

            new Pair<float, float>( 1.860f, 4.00f  ),
            new Pair<float, float>( 2.460f, 4.50f  ),
            new Pair<float, float>( 2.960f, 5.00f  ),
            new Pair<float, float>( 3.960f, 5.50f  ),
            new Pair<float, float>( 4.960f, 6.00f  ),
            new Pair<float, float>( 6.460f, 6.75f  ),
            new Pair<float, float>( 7.960f, 7.50f  ),
            new Pair<float, float>( 9.960f, 8.50f  ),
            new Pair<float, float>( 14.46f, 10.0f  ),
        }; // Perhaps add more after GEL I
    }
}
