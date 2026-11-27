using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace RimRound.Utilities
{
    // Configure after Def loading: GlobalSettings includes Def-dependent defaults.
    // The companion owns the config file; this assembly owns colony settings.
    public static class CoreSettingsDefaults
    {
        private static Dictionary<string, object> factory;
        private static Dictionary<string, object> defaults;
        private static FieldInfo[] fields;
        private static bool menuApplied;

        private static void Initialize()
        {
            if (factory != null) return;
            fields = typeof(GlobalSettings).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(bool) || f.FieldType == typeof(NumericFieldData<int>) ||
                    f.FieldType == typeof(NumericFieldData<float>)).ToArray();
            factory = fields.ToDictionary(f => f.Name, f => f.GetValue(null));
            defaults = new Dictionary<string, object>(factory);
        }

        public static object FactoryValue(string name)
        {
            Initialize();
            return factory[name];
        }

        public static void Configure(Dictionary<string, bool> booleans,
            Dictionary<string, int> integers, Dictionary<string, float> numbers)
        {
            Initialize();
            defaults = new Dictionary<string, object>(factory);
            foreach (FieldInfo field in fields)
            {
                bool b; int i; float n;
                if (field.FieldType == typeof(bool) && booleans != null && booleans.TryGetValue(field.Name, out b))
                    defaults[field.Name] = b;
                else if (field.FieldType == typeof(NumericFieldData<int>) && integers != null && integers.TryGetValue(field.Name, out i))
                {
                    var value = (NumericFieldData<int>)factory[field.Name];
                    value.threshold = (int)Math.Max(value.min, Math.Min((double)value.max, i));
                    defaults[field.Name] = value;
                }
                else if (field.FieldType == typeof(NumericFieldData<float>) && numbers != null && numbers.TryGetValue(field.Name, out n) &&
                    !float.IsNaN(n) && !float.IsInfinity(n))
                {
                    var value = (NumericFieldData<float>)factory[field.Name];
                    value.threshold = Math.Max(value.min, Math.Min(value.max, n));
                    defaults[field.Name] = value;
                }
            }
            menuApplied = false;
        }

        public static void ApplyDefaults()
        {
            Initialize();
            foreach (FieldInfo field in fields) field.SetValue(null, defaults[field.Name]);
            menuApplied = false;
        }

        public static void EnsureMenuContext()
        {
            if (Current.Game != null) { menuApplied = false; return; }
            if (menuApplied) return;
            ApplyDefaults();
            menuApplied = true;
        }

        public static void CaptureDefaults(out Dictionary<string, bool> booleans,
            out Dictionary<string, int> integers, out Dictionary<string, float> numbers)
        {
            Initialize();
            booleans = new Dictionary<string, bool>();
            integers = new Dictionary<string, int>();
            numbers = new Dictionary<string, float>();
            foreach (FieldInfo field in fields)
            {
                object value = field.GetValue(null);
                if (value is bool) booleans[field.Name] = (bool)value;
                else if (value is NumericFieldData<int>) integers[field.Name] = ((NumericFieldData<int>)value).threshold;
                else if (value is NumericFieldData<float>) numbers[field.Name] = ((NumericFieldData<float>)value).threshold;
            }
            Configure(booleans, integers, numbers);
            menuApplied = true;
        }
    }
}
