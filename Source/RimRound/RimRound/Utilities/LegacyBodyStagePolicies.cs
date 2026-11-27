using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RimRound.Comps;
using RimRound.Simulation;
using RimWorld;
using Verse;

namespace RimRound.Utilities
{
    /// <summary>
    /// One-time compatibility bridge from the legacy tables to independent
    /// biological policies. Later presentation choices never select a policy.
    /// Legacy tables remain available for old saves until rendering conversion.
    /// </summary>
    public static class LegacyBodyStagePolicies
    {
        static LegacyBodyStagePolicies() { }
        private sealed class Source
        {
            internal readonly string Id;
            internal readonly Dictionary<BodyTypeDef, BodyTypeInfo> Table;
            internal Source(string name, Dictionary<BodyTypeDef, BodyTypeInfo> table)
            { Id = "rimround.legacy." + name.ToLowerInvariant(); Table = table; }
        }

        private static readonly Source[] Sources = {
            new Source("defaultFemaleSet", RacialBodyTypeInfoUtility.defaultFemaleSet),
            new Source("appleFemaleSet", RacialBodyTypeInfoUtility.appleFemaleSet),
            new Source("defaultMaleSet", RacialBodyTypeInfoUtility.defaultMaleSet),
            new Source("standardFemaleSetNoThin", RacialBodyTypeInfoUtility.standardFemaleSetNoThin),
            new Source("appleFemaleSetNoThin", RacialBodyTypeInfoUtility.appleFemaleSetNoThin),
            new Source("defaultFemaleSetNoFemale", RacialBodyTypeInfoUtility.defaultFemaleSetNoFemale),
            new Source("appleFemaleSetNoFemale", RacialBodyTypeInfoUtility.appleFemaleSetNoFemale),
            new Source("set090Female", RacialBodyTypeInfoUtility.set090Female),
            new Source("set090FemaleApple", RacialBodyTypeInfoUtility.set090FemaleApple),
            new Source("set090Male", RacialBodyTypeInfoUtility.set090Male),
            new Source("set070Female", RacialBodyTypeInfoUtility.set070Female),
            new Source("set070Male", RacialBodyTypeInfoUtility.set070Male),
            new Source("set090FemaleNoFemaleSprite", RacialBodyTypeInfoUtility.set090FemaleNoFemaleSprite),
            new Source("set090FemaleAppleNoFemaleSprite", RacialBodyTypeInfoUtility.set090FemaleAppleNoFemaleSprite),
            new Source("set090FemaleNoFemaleStandardThin", RacialBodyTypeInfoUtility.set090FemaleNoFemaleStandardThin),
            new Source("set090FemaleAppleNoFemaleSpriteStandardThin", RacialBodyTypeInfoUtility.set090FemaleAppleNoFemaleSpriteStandardThin)
        };

        private static readonly int[] Codes = {
            5, 6, 10, 20, 30, 40, 50, 60, 70, 80, 90,
            100, 150, 200, 250, 300, 350, 400, 450, 500,
            900, 910, 920, 930, 940, 950, 960, 970, 980, 990, 995
        };
        private static readonly string[] StageKeys = {
            "thick", "chonky", "chubby", "corpulent", "fat", "obese", "morbid", "lardy",
            "enormous", "gigantic", "titanic", "gelatinous01", "gelatinous02", "gelatinous03",
            "gelatinous04", "gelatinous05", "gelatinous06", "gelatinous07", "gelatinous08",
            "gelatinous09", "gelatinous10", "gelatinous11", "gelatinous12", "gelatinous13",
            "gelatinous14", "gelatinous15", "gelatinous16", "gelatinous17", "gelatinous18",
            "gelatinous19", "gelatinous20"
        };
        private static readonly IReadOnlyDictionary<string, BodyStagePolicy> policies = Build();
        public static IReadOnlyDictionary<string, BodyStagePolicy> Policies { get { return policies; } }
        public static BodyStagePolicy DefaultPolicy { get { return policies["rimround.legacy.defaultfemaleset"]; } }

        /// <summary>Explicit lifecycle migration, before the first new projection.</summary>
        public static void InitializeForPawn(Pawn pawn)
        {
            var state = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (state == null || state.BodyStageStateInitialized || pawn.story == null) return;
            string id = Identify(RacialBodyTypeInfoUtility.GetRacialDictionary(pawn));
            state.InitializeBodyStageState(id ?? "rimround.legacy.unmapped",
                LegacyMultiplier(pawn.story.bodyType?.defName));
        }

        /// <summary>
        /// Called after choosing the current projection, at the old explicit
        /// sprite-update cadence. An art request alone never calls this method.
        /// </summary>
        public static BodyStageTransition? AdvanceForPawn(Pawn pawn, bool nativeCompatibility)
        {
            var state = pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();
            var weight = HediffUtility.WeightHediff(pawn);
            if (state == null || !state.BodyStageStateInitialized || weight == null) return null;
            BodyStagePolicy policy = nativeCompatibility ? DefaultPolicy : Resolve(state.BodyStagePolicyId);
            return state.AdvanceBodyStageState(policy, weight.Severity,
                GlobalSettings.varyMinWeightForBodyTypeByBodySize);
        }

        private static IReadOnlyDictionary<string, BodyStagePolicy> Build()
        {
            var result = new Dictionary<string, BodyStagePolicy>(StringComparer.Ordinal);
            foreach (Source source in Sources)
            {
                var rows = new List<BodyStageRow>();
                foreach (var entry in source.Table)
                {
                    // Negative rows supply native graphic metadata but cannot
                    // be selected by nonnegative biological severity.
                    if (entry.Value.maxSeverity <= 0f) continue;
                    string key;
                    if (!TryGetStageKey(entry.Key.defName, out key))
                        throw new InvalidOperationException("Unmapped biological stage in " + source.Id + ": " + entry.Key.defName);
                    rows.Add(new BodyStageRow(entry.Value.maxSeverity, key, LegacyMultiplier(entry.Key.defName)));
                }
                result.Add(source.Id, new BodyStagePolicy(source.Id, rows));
            }
            return new ReadOnlyDictionary<string, BodyStagePolicy>(result);
        }

        /// <summary>Migration/generation only; never call from a render getter.</summary>
        public static string Identify(Dictionary<BodyTypeDef, BodyTypeInfo> legacyTable)
        {
            foreach (Source source in Sources)
                if (ReferenceEquals(source.Table, legacyTable)) return source.Id;
            return null;
        }

        /// <summary>The caller retains an unknown saved ID; fallback does not rewrite it.</summary>
        public static BodyStagePolicy Resolve(string savedPolicyId)
        {
            BodyStagePolicy policy;
            return savedPolicyId != null && policies.TryGetValue(savedPolicyId, out policy) ? policy : DefaultPolicy;
        }

        public static bool TryGetStageKey(string legacyDefName, out string stageKey)
        {
            stageKey = null;
            if (legacyDefName == "Thin") { stageKey = "thin"; return true; }
            if (legacyDefName == "Male" || legacyDefName == "Female") { stageKey = "average"; return true; }
            if (string.IsNullOrEmpty(legacyDefName)) return false;
            string[] parts = legacyDefName.Split('_');
            if (parts.Length < 3 || (parts[0] != "F" && parts[0] != "M")) return false;
            string codePart = parts[1];
            if (codePart.Length == 4 && codePart[3] == 'a') codePart = codePart.Substring(0, 3);
            int code;
            if (codePart.Length != 3 || !int.TryParse(codePart, out code)) return false;
            int index = Array.IndexOf(Codes, code);
            if (index < 0) return false;
            stageKey = StageKeys[index];
            return true;
        }

        /// <summary>Known legacy suffix conversion, independent of the current art settings.</summary>
        public static float LegacyMultiplier(string legacyDefName)
        {
            string stage;
            if (!TryGetStageKey(legacyDefName, out stage)) return 1f;
            string suffix = legacyDefName.Split('_').Last();
            if (suffix == "070") return .35f;
            if (suffix == "090" || suffix == "Ratkin" || suffix == "Anty") return .6f;
            return 1f;
        }
    }
}
