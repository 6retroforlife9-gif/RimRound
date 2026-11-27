using RimRound.Defs;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using static RimRound.Utilities.HungerDroneUtility;

namespace RimRound.AI
{
    class GameCondition_PsychicHungerIncrease : GameCondition
    {
        public override string Label 
        {
            get 
            {
                return $"Hunger driver ({level.ToString().ToLowerInvariant()}, {gender.GetLabel(false)})";
            }
        }

        public override string LetterText 
        {
            get 
            {
                return Description;
            }
        }

        public override string Description 
        {
            get 
            {
                return this.def.description.Formatted(gender.GetLabel(false), MultiplierForLevel(level).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        public override void PostMake()
        {
            base.PostMake();
            this.level = 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<Gender>(ref this.gender, "gender", Gender.None, false);
            Scribe_Values.Look<HungerDroneLevel>(ref this.level, "level", HungerDroneLevel.None, false);
        }

        public Gender gender;
        public HungerDroneLevel level;
    }


}
