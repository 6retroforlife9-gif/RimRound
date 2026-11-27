using System;
using System.Reflection;

namespace RimRound.Presentation.Anthrosonae
{
    // Binding is deliberately explicit and has no static reference to any
    // Anthrosonae type. The caller first verifies package/assembly/API identity.
    public sealed class OptionalPaletteBinding
    {
        public readonly Type FurGeneType;
        public readonly MethodInfo ApplyColorsMethod;
        private readonly FieldInfo primaryField, secondaryField;
        private OptionalPaletteBinding(Type type, FieldInfo primary, FieldInfo secondary, MethodInfo writer)
        { FurGeneType = type; primaryField = primary; secondaryField = secondary; ApplyColorsMethod = writer; }
        public static bool TryCreate(Type furGeneType, Type nativeGeneType, Type nativeColorType, out OptionalPaletteBinding result, out string reason)
        {
            result = null; reason = "unsupported-appearance-api";
            if (furGeneType == null || nativeGeneType == null || nativeColorType == null ||
                furGeneType.FullName != "Anthrosonae.FurGene" || !nativeGeneType.IsAssignableFrom(furGeneType) || !nativeColorType.IsValueType) return false;
            var first = furGeneType.GetField("colorOne", BindingFlags.Public | BindingFlags.Instance);
            var second = furGeneType.GetField("colorTwo", BindingFlags.Public | BindingFlags.Instance);
            var writer = furGeneType.GetMethod("ApplyColors", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (first?.FieldType != nativeColorType || second == null || Nullable.GetUnderlyingType(second.FieldType) != nativeColorType ||
                writer == null || writer.ReturnType != typeof(void)) return false;
            result = new OptionalPaletteBinding(furGeneType, first, second, writer); reason = null; return true;
        }
        public bool TryCapture(object activeGene, Func<bool> isMainThread, Func<object, AppearanceColor> copyColor,
            out AppearancePalette palette, out string reason)
        {
            palette = default(AppearancePalette); reason = "unsupported-appearance-api";
            if (isMainThread == null || !isMainThread()) { reason = "appearance-capture-off-thread"; return false; }
            if (activeGene == null || !FurGeneType.IsInstanceOfType(activeGene) || copyColor == null) return false;
            try
            {
                var first = copyColor(primaryField.GetValue(activeGene));
                var rawSecond = secondaryField.GetValue(activeGene);
                AppearanceColor? second = rawSecond == null ? (AppearanceColor?)null : copyColor(rawSecond);
                palette = new AppearancePalette(first, second); reason = null; return true;
            }
            catch (Exception) { reason = "appearance-palette-read-failed"; return false; }
        }
    }
}
