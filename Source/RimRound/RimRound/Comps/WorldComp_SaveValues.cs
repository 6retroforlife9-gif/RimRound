using AlienRace;
using RimRound.Utilities;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Comps
{
    public class WorldComp_SaveValues : WorldComponent
    {
        private int defaultsVersion = 1;
        public WorldComp_SaveValues(World world) : base(world) 
        {
            CoreSettingsDefaults.ApplyDefaults();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.LoadingVars) CoreSettingsDefaults.ApplyDefaults();
            SaveAllSettings();
            Scribe_Values.Look(ref defaultsVersion, "rrSettingsDefaultsVersion", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars && defaultsVersion < 1)
            {
                // Retain the saved age range, including old saves using 200.
                // New colonies use the current defaults; loading is not a reset.
                defaultsVersion = 1;
            }
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            // Pawn graphics are invalidated when their body type changes in
            // BodyTypeUtility. Changing another mod's global cache setting here
            // slowed every pawn and persisted beyond RimRound's own render work.
        }

        public void SaveAllSettings()
        {
            IEnumerable<FieldInfo> fieldInfos = typeof(GlobalSettings).GetRuntimeFields();

            foreach (FieldInfo f in fieldInfos)
            {
                if (f.FieldType == typeof(bool))
                {
                    ExposeField<bool>(f);
                }
                else if (f.FieldType == typeof(NumericFieldData<float>))
                {
                    ExposeNumericFieldData<float>(f);
                }
                else if (f.FieldType == typeof(NumericFieldData<int>))
                {
                    ExposeNumericFieldData<int>(f);
                }
            }
        }

        private static void ExposeNumericFieldData<T>(FieldInfo f)
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                T currentVal = (T)((NumericFieldData<T>)f.GetValue(null)).threshold;

                Scribe_Values.Look<T>(ref currentVal, f.Name, default, true);
                return;
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                NumericFieldData<T> dummyNumericFieldData = (NumericFieldData<T>)f.GetValue(null);
                T dummyVal = default;
                Scribe_Values.Look<T>(ref dummyVal, f.Name, dummyNumericFieldData.threshold);

                dummyNumericFieldData.threshold = dummyVal;
                dummyNumericFieldData.stringBuffer = null;

                f.SetValue(null, dummyNumericFieldData);
                return;
            }
        }

        private static void ExposeField<T>(FieldInfo f) where T : struct
        {
            if (typeof(T) == typeof(NumericFieldData<float>) || typeof(T) == typeof(NumericFieldData<int>))
            {
                Log.Error($"Cannot use {nameof(ExposeField)} with {nameof(T)}");
                return;
            }

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                T currentVal = (T)f.GetValue(null);
                Scribe_Values.Look<T>(ref currentVal, f.Name, default, true);
                return;
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                T dummyVal = (T)f.GetValue(null);
                Scribe_Values.Look<T>(ref dummyVal, f.Name, dummyVal);
                f.SetValue(null, dummyVal);
                return;
            }
        }

    }
}
