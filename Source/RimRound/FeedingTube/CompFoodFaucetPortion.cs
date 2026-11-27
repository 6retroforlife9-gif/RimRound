using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimRound.FeedingTube
{
    public enum FaucetPortionMode
    {
        Auto = 0,
        Normal = 1,
        Large = 2,
        Huge = 3,
        Enormous = 4,
        Massive = 5,
        Colossal = 6
    }

    // A snapshot of one catalog entry. Recreate it when requested so both
    // DefDatabase and the current game's research state are read at use time.
    public sealed class FaucetPortion
    {
        public FaucetPortionMode Mode { get; }
        public ThingDef FoodDef { get; }
        public double Volume { get; }
        public int Density { get; }
        public double PreciseNutrition => Volume * Density;
        public float Nutrition => (float)PreciseNutrition;
        public bool Unlocked { get; }
        public string Label { get; }
        public string LockedReason { get; }

        internal FaucetPortion(FaucetPortionMode mode, string foodDefName,
            float nutrition, string researchDefName)
        {
            Mode = mode;
            Volume = mode == FaucetPortionMode.Normal ? FoodServingMath.StandardVolume :
                mode == FaucetPortionMode.Large ? FoodServingMath.LargeVolume :
                mode == FaucetPortionMode.Auto ? 0d : FoodServingMath.FeastVolume;
            Density = 1;
            Label = ("RR_FaucetPortion_" + mode).Translate().ToString();
            FoodDef = foodDefName == null ? null :
                DefDatabase<ThingDef>.GetNamedSilentFail(foodDefName);
            ResearchProjectDef research = researchDefName == null ? null :
                DefDatabase<ResearchProjectDef>.GetNamedSilentFail(researchDefName);
            bool researchReady = researchDefName == null ||
                (research != null && Current.Game != null &&
                    Find.ResearchManager != null && research.IsFinished);
            Unlocked = researchReady && (foodDefName == null || FoodDef != null);
            if (!Unlocked)
            {
                LockedReason = FoodDef == null && foodDefName != null
                    ? "RR_FaucetPortionMissingDef".Translate().ToString()
                    : research == null
                        ? "RR_FaucetPortionMissingResearch".Translate().ToString()
                        : "RR_FaucetPortionRequiresResearch".Translate(
                            research.LabelCap).ToString();
            }
        }
        private FaucetPortion(FaucetPortion source, int density)
        {
            Mode = source.Mode; FoodDef = source.FoodDef; Volume = source.Volume; Density = density;
            Unlocked = source.Unlocked; Label = source.Label; LockedReason = source.LockedReason;
        }
        public FaucetPortion WithDensity(int density) => FoodServingMath.GradeValid(density)
            ? new FaucetPortion(this, density) : null;
    }

    public static class FoodFaucetPortions
    {
        // This intentionally constructs descriptors on every access. Static
        // Def resolution is too early during RimWorld startup, and research
        // unlocks can change while a faucet is already spawned.
        public static IEnumerable<FaucetPortion> All
        {
            get
            {
                yield return Get(FaucetPortionMode.Auto);
                yield return Get(FaucetPortionMode.Normal);
                yield return Get(FaucetPortionMode.Large);
                yield return Get(FaucetPortionMode.Huge);
            }
        }

        public static FaucetPortion Get(FaucetPortionMode mode)
        {
            const string dense = "RR_Research_NutritionallyDenseFoods";
            mode = Normalize(mode);
            switch (mode)
            {
                case FaucetPortionMode.Auto:
                    return new FaucetPortion(mode, null, 0f, null);
                case FaucetPortionMode.Normal:
                    return new FaucetPortion(mode, "MealNutrientPaste", 0.9f, null);
                case FaucetPortionMode.Large:
                    return new FaucetPortion(mode, "RR_NetworkMealLarge", 1.8f, dense);
                case FaucetPortionMode.Huge:
                    return new FaucetPortion(mode, "RR_NetworkMealHuge", 3.6f, dense);
                default:
                    return null;
            }
        }
        public static FaucetPortionMode Normalize(FaucetPortionMode mode) =>
            mode >= FaucetPortionMode.Huge && mode <= FaucetPortionMode.Colossal ? FaucetPortionMode.Huge : mode;
    }

    public sealed class CompProperties_FoodFaucetPortion : CompProperties
    {
        public CompProperties_FoodFaucetPortion()
        {
            compClass = typeof(CompFoodFaucetPortion);
        }
    }

    public sealed class CompFoodFaucetPortion : ThingComp
    {
        private FaucetPortionMode mode = FaucetPortionMode.Auto;
        private FaucetPortionMode autoMaximum = FaucetPortionMode.Huge;
        private int densityOverride;
        public int DensityOverride => densityOverride;
        public static Func<Building_FoodFaucet, string> DensityStatusProvider;
        public bool SetDensityOverride(int value)
        {
            if (value < 0 || value > 5) return false;
            densityOverride = value; return true;
        }

        public FaucetPortionMode Mode => mode;
        public FaucetPortionMode AutoMaximum => autoMaximum;

        // A locked fixed selection is retained. Callers receive no candidate
        // until its research is complete; they must not fall back to Normal.
        public IEnumerable<FaucetPortion> Candidates
        {
            get
            {
                if (mode == FaucetPortionMode.Auto)
                    return FoodFaucetPortions.All.Where(portion =>
                        portion.Mode != FaucetPortionMode.Auto && portion.Unlocked &&
                        portion.Mode <= autoMaximum)
                        .OrderBy(portion => portion.Nutrition);
                FaucetPortion selected = FoodFaucetPortions.Get(mode);
                return selected != null && selected.Unlocked
                    ? new[] { selected } : Enumerable.Empty<FaucetPortion>();
            }
        }

        public bool SetMode(FaucetPortionMode value)
        {
            if (!Enum.IsDefined(typeof(FaucetPortionMode), value)) return false;
            mode = FoodFaucetPortions.Normalize(value);
            return true;
        }

        public bool SetAutoMaximum(FaucetPortionMode value)
        {
            if (value == FaucetPortionMode.Auto ||
                !Enum.IsDefined(typeof(FaucetPortionMode), value)) return false;
            autoMaximum = FoodFaucetPortions.Normalize(value);
            return true;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref mode, "rrFaucetPortionMode", FaucetPortionMode.Auto);
            Scribe_Values.Look(ref autoMaximum, "rrFaucetAutoMaximum", FaucetPortionMode.Colossal);
            Scribe_Values.Look(ref densityOverride, "rrFaucetDensityOverride", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                mode = FoodFaucetPortions.Normalize(mode);
                autoMaximum = FoodFaucetPortions.Normalize(autoMaximum);
                if (densityOverride < 0 || densityOverride > 5) densityOverride = 0;
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit &&
                !Enum.IsDefined(typeof(FaucetPortionMode), mode))
                mode = FaucetPortionMode.Auto;
            if (Scribe.mode == LoadSaveMode.PostLoadInit &&
                (autoMaximum == FaucetPortionMode.Auto ||
                 !Enum.IsDefined(typeof(FaucetPortionMode), autoMaximum)))
                autoMaximum = FaucetPortionMode.Huge;
        }

        public override string CompInspectStringExtra()
        {
            FaucetPortion selected = FoodFaucetPortions.Get(mode);
            if (selected == null) return null;
            string heading = "RR_FaucetPortionInspect".Translate(selected.Label).ToString() + "\n" +
                (DensityStatusProvider?.Invoke(parent as Building_FoodFaucet) ??
                    (densityOverride == 0 ? "RR_FaucetDensityUseSupply".Translate().ToString() :
                        "RR_FoodDensityGrade".Translate(densityOverride).ToString()));
            if (mode == FaucetPortionMode.Auto)
                return heading + "\n" +
                    "RR_FaucetAutoMaximumInspect".Translate(
                        FoodFaucetPortions.Get(autoMaximum).Label).ToString() + "\n" +
                    "RR_FaucetPortionAutoStatus".Translate().ToString();
            if (!selected.Unlocked)
                return heading + "\n" +
                    "RR_FaucetPortionFixedLocked".Translate(
                        selected.LockedReason).ToString();
            return heading + "\n" +
                "RR_FaucetPortionFixedStatus".Translate(
                    selected.Volume.ToString("0.##")).ToString();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = densityOverride == 0 ? "RR_FaucetDensityUseSupply".Translate() :
                    "RR_FaucetDensityOverride".Translate(densityOverride),
                defaultDesc = "RR_FaucetDensityDesc".Translate(),
                icon = Widgets.GetIconFor(RimWorld.ThingDefOf.MealNutrientPaste),
                action = delegate
                {
                    var options = new List<FloatMenuOption> { new FloatMenuOption("RR_FaucetDensityUseSupply".Translate(), () => SetDensityOverride(0)) };
                    for (int grade = 1; grade <= 5; grade++)
                    {
                        int captured = grade;
                        options.Add(new FloatMenuOption("RR_FoodDensityGrade".Translate(grade), () => SetDensityOverride(captured)));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
            FaucetPortion selected = FoodFaucetPortions.Get(mode);
            yield return new Command_Action
            {
                defaultLabel = "RR_FaucetPortionCommand".Translate(
                    selected?.Label ?? mode.ToString()),
                defaultDesc = CompInspectStringExtra(),
                icon = Widgets.GetIconFor(RimWorld.ThingDefOf.MealNutrientPaste),
                action = OpenMenu
            };
            if (mode == FaucetPortionMode.Auto)
                yield return new Command_Action
                {
                    defaultLabel = "RR_FaucetAutoMaximumCommand".Translate(
                        FoodFaucetPortions.Get(autoMaximum).Label),
                    defaultDesc = "RR_FaucetAutoMaximumDesc".Translate(),
                    icon = Widgets.GetIconFor(RimWorld.ThingDefOf.MealNutrientPaste),
                    action = OpenMaximumMenu
                };
        }

        private void OpenMaximumMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (FaucetPortion portion in FoodFaucetPortions.All)
            {
                if (portion.Mode == FaucetPortionMode.Auto) continue;
                FaucetPortionMode captured = portion.Mode;
                string label = portion.Label;
                if (captured == autoMaximum)
                    label += " " + "RR_FaucetPortionSelected".Translate().ToString();
                if (!portion.Unlocked)
                    label += " (" + portion.LockedReason + ")";
                // A ceiling may include a future unlock; selection still
                // requires the portion's actual research before collection.
                options.Add(new FloatMenuOption(label, () => SetAutoMaximum(captured)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (FaucetPortion portion in FoodFaucetPortions.All)
            {
                FaucetPortionMode captured = portion.Mode;
                string label = portion.Label;
                if (captured == mode)
                    label += " " + "RR_FaucetPortionSelected".Translate().ToString();
                if (!portion.Unlocked)
                    label += " (" + portion.LockedReason + ")";
                options.Add(new FloatMenuOption(label,
                    portion.Unlocked ? (Action)(() => SetMode(captured)) : null));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
