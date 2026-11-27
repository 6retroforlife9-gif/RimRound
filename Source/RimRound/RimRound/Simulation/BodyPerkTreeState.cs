using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RimRound.Simulation
{
    /// <summary>Stable single-rank node identifiers and approved catalogue.</summary>
    public static class BodyPerkTreeCatalog
    {
        public const string PractisedAppetite = "practised_appetite";
        public const string ComfortableFullness = "comfortable_fullness";
        public const string SteadyDigestion = "steady_digestion";
        public const string QuickMeals = "quick_meals";
        public const string SupportiveVoice = "supportive_voice";
        public const string PatientListener = "patient_listener";
        public const string DeepCapacity = "deep_capacity";
        public const string RapidProcessing = "rapid_processing";

        private static readonly ReadOnlyCollection<BodyPerkNodeDefinition> nodeList =
            Array.AsReadOnly(new[]
            {
                new BodyPerkNodeDefinition(PractisedAppetite, "Practised appetite", 2,
                    2d, .25f, 0, false, false,
                    null, new[] { SteadyDigestion }, .10f, 0f, 0f, 0f, 0f, 0f, 0f),
                new BodyPerkNodeDefinition(ComfortableFullness, "Comfortable fullness", 2,
                    0d, 0f, 0, false, false,
                    new[] { PractisedAppetite }, null, 0f, 0f, 0f, .10f, 0f, 0f, 0f),
                new BodyPerkNodeDefinition(SteadyDigestion, "Steady digestion", 2,
                    2d, .25f, 0, false, false,
                    null, new[] { PractisedAppetite }, 0f, .15f, 0f, 0f, 0f, 0f, 0f),
                new BodyPerkNodeDefinition(QuickMeals, "Quick meals", 2,
                    0d, 0f, 0, false, false,
                    new[] { SteadyDigestion }, null, 0f, 0f, .10f, 0f, 0f, 0f, 0f),
                new BodyPerkNodeDefinition(SupportiveVoice, "Supportive voice", 2,
                    0d, 0f, 3, false, false,
                    null, new[] { PatientListener }, 0f, 0f, 0f, 0f, .10f, 0f, 0f),
                new BodyPerkNodeDefinition(PatientListener, "Patient listener", 2,
                    0d, 0f, 3, false, false,
                    null, new[] { SupportiveVoice }, 0f, 0f, 0f, 0f, 0f, .20f, 0f),
                new BodyPerkNodeDefinition(DeepCapacity, "Deep capacity", 4,
                    0d, 0f, 0, true, true,
                    new[] { PractisedAppetite }, null, .40f, 0f, 0f, 0f, 0f, 0f, 0f),
                new BodyPerkNodeDefinition(RapidProcessing, "Rapid processing", 4,
                    0d, 0f, 0, true, true,
                    new[] { SteadyDigestion }, null, 0f, .35f, 0f, 0f, 0f, 0f, .10f)
            });

        public static IReadOnlyList<BodyPerkNodeDefinition> All { get { return nodeList; } }

        public static bool TryGet(string id, out BodyPerkNodeDefinition definition)
        {
            for (int i = 0; i < nodeList.Count; i++)
            {
                if (string.Equals(nodeList[i].Id, id, StringComparison.Ordinal))
                {
                    definition = nodeList[i];
                    return true;
                }
            }
            definition = null;
            return false;
        }
    }

    /// <summary>Immutable catalogue data; effects are additive fractions.</summary>
    public sealed class BodyPerkNodeDefinition
    {
        private readonly ReadOnlyCollection<string> prerequisites;
        private readonly ReadOnlyCollection<string> exclusions;

        public readonly string Id;
        public readonly string Name;
        public readonly int Cost;
        public readonly double MinimumActiveDays;
        public readonly float MinimumHabitConditioning;
        public readonly int MinimumCompletedBodyDiscussions;
        public readonly bool RequiresExceptionalAdaptationResearch;
        public readonly bool FantasyOnly;
        public readonly float CapacityBonus;
        public readonly float DigestionBonus;
        public readonly float EatingCapacityBonus;
        public readonly float FullnessPainMitigation;
        public readonly float BodyPersuasionBonus;
        public readonly float IrritationResistanceBonus;
        public readonly float HungerRateMultiplierBonus;

        internal BodyPerkNodeDefinition(string id, string name, int cost,
            double minimumActiveDays, float minimumHabitConditioning,
            int minimumCompletedBodyDiscussions, bool requiresResearch,
            bool fantasyOnly, string[] required, string[] excluded,
            float capacityBonus, float digestionBonus, float eatingCapacityBonus,
            float fullnessPainMitigation, float bodyPersuasionBonus,
            float irritationResistanceBonus, float hungerRateMultiplierBonus)
        {
            Id = id;
            Name = name;
            Cost = cost;
            MinimumActiveDays = minimumActiveDays;
            MinimumHabitConditioning = minimumHabitConditioning;
            MinimumCompletedBodyDiscussions = minimumCompletedBodyDiscussions;
            RequiresExceptionalAdaptationResearch = requiresResearch;
            FantasyOnly = fantasyOnly;
            prerequisites = Array.AsReadOnly(required == null ? new string[0] : (string[])required.Clone());
            exclusions = Array.AsReadOnly(excluded == null ? new string[0] : (string[])excluded.Clone());
            CapacityBonus = capacityBonus;
            DigestionBonus = digestionBonus;
            EatingCapacityBonus = eatingCapacityBonus;
            FullnessPainMitigation = fullnessPainMitigation;
            BodyPersuasionBonus = bodyPersuasionBonus;
            IrritationResistanceBonus = irritationResistanceBonus;
            HungerRateMultiplierBonus = hungerRateMultiplierBonus;
        }

        public IReadOnlyList<string> Prerequisites { get { return prerequisites; } }
        public IReadOnlyList<string> Exclusions { get { return exclusions; } }
    }

    /// <summary>Resolved perk bonuses. The caller applies them to live stats.</summary>
    public struct BodyPerkEffects
    {
        public float CapacityBonus;
        public float DigestionBonus;
        public float EatingCapacityBonus;
        public float FullnessPainMitigation;
        public float BodyPersuasionBonus;
        public float IrritationResistanceBonus;
        public float HungerRateMultiplierBonus;
    }

    /// <summary>
    /// Pure save-state model. Runtime integration Scribes these public primitive
    /// fields and parallel lists. Unknown identifiers and costs remain raw data;
    /// only current, well-formed catalogue entries can produce effects.
    /// </summary>
    public sealed class BodyPerkTreeState
    {
        public const int CurrentVersion = 1;
        public const int ActiveDayTicks = 60000;
        public const int MaximumIntervalTicks = 2500;
        public const long RespecCooldownTicks = 180000L;
        public const double ActiveTimePointsPerDay = .5d;
        public const double FoodPointsPerDay = .4d;
        public const double SocialPointsPerDiscussion = .01d;
        public const double MaximumSocialPointsPerDay = .1d;

        // The manager Scribes these fields by value/reference. No old point field
        // is read or migrated into this new economy.
        public int SchemaVersion = CurrentVersion;
        public double EarnedPoints;
        public List<string> PurchasedNodeIds = new List<string>();
        public List<int> PurchasedRawCosts = new List<int>();
        public int WindowActiveTicks;
        public long LifetimeActiveTicks;
        public double WindowUsefulNutrition;
        public double WindowExpenditureNutrition;
        public int WindowCompletedDiscussions;
        public int LastSocialEventTick = -1;
        public long LastRespecTick = -1L;

        public bool IsUsable { get { return SchemaVersion == CurrentVersion; } }

        public double AvailablePoints
        {
            get
            {
                double spent;
                if (!IsUsable || !FiniteNonnegative(EarnedPoints) ||
                    !TryGetValidPurchaseTotal(out spent)) return 0d;
                return Math.Max(0d, EarnedPoints - spent);
            }
        }

        /// <summary>Records only committed useful nutrition while the tree is active.</summary>
        public bool RecordUsefulCommittedNutrition(double nutrition, bool active)
        {
            if (!active || !CanProgress() || !FinitePositive(nutrition)) return false;
            double next = WindowUsefulNutrition + nutrition;
            if (!FiniteNonnegative(next)) return false;
            WindowUsefulNutrition = next;
            return true;
        }

        public bool RecordResourceExpenditure(double nutrition, bool active)
        {
            if (!active || !CanProgress() || !FinitePositive(nutrition) ||
                !FiniteNonnegative(WindowExpenditureNutrition + nutrition)) return false;
            WindowExpenditureNutrition += nutrition;
            return true;
        }

        /// <summary>
        /// Records a caller-verified completed participant event. The saved tick
        /// guard is advanced while disabled too, so re-enabling cannot replay it.
        /// </summary>
        public bool RecordCompletedBodyDiscussion(int eventTick, bool active)
        {
            if (!IsUsable || eventTick < 0 || LastSocialEventTick < -1 ||
                eventTick <= LastSocialEventTick) return false;
            if (active && !CanProgress()) return false;
            LastSocialEventTick = eventTick;
            if (!active) return false;
            if (WindowCompletedDiscussions < 10) WindowCompletedDiscussions++;
            return true;
        }

        /// <summary>
        /// Adds one short, actual active simulation interval. Expenditure is
        /// apportioned across a day boundary; no wall-clock or save-load catch-up
        /// is inferred. A complete 60000-tick window awards at most one point.
        /// </summary>
        public bool RecordActiveInterval(int ticks, double actualExpenditure, bool active,
            out double pointsAwarded)
        {
            pointsAwarded = 0d;
            if (!active || !CanProgress() || ticks <= 0 || ticks > MaximumIntervalTicks ||
                !FiniteNonnegative(actualExpenditure) || LifetimeActiveTicks > long.MaxValue - ticks)
                return false;

            int windowTicks = WindowActiveTicks;
            double windowFood = WindowUsefulNutrition;
            double windowExpenditure = WindowExpenditureNutrition;
            int windowDiscussions = WindowCompletedDiscussions;
            double earned = EarnedPoints;
            double remainingExpenditure = actualExpenditure;
            int remainingTicks = ticks;
            double awardTotal = 0d;

            while (remainingTicks > 0)
            {
                int pieceTicks = Math.Min(remainingTicks, ActiveDayTicks - windowTicks);
                double pieceExpenditure = pieceTicks == remainingTicks
                    ? remainingExpenditure
                    : remainingExpenditure * pieceTicks / remainingTicks;
                windowExpenditure += pieceExpenditure;
                if (!FiniteNonnegative(windowExpenditure)) return false;
                windowTicks += pieceTicks;
                remainingTicks -= pieceTicks;
                remainingExpenditure -= pieceExpenditure;

                if (windowTicks == ActiveDayTicks)
                {
                    double foodRatio = windowExpenditure > 0d
                        ? Math.Min(1d, windowFood / windowExpenditure) : 0d;
                    double socialPoints = Math.Min(MaximumSocialPointsPerDay,
                        windowDiscussions * SocialPointsPerDiscussion);
                    double award = ActiveTimePointsPerDay + FoodPointsPerDay * foodRatio + socialPoints;
                    double nextEarned = earned + award;
                    if (!FiniteNonnegative(nextEarned)) return false;
                    earned = nextEarned;
                    awardTotal += award;
                    windowTicks = 0;
                    windowFood = 0d;
                    windowExpenditure = 0d;
                    windowDiscussions = 0;
                }
            }

            WindowActiveTicks = windowTicks;
            LifetimeActiveTicks += ticks;
            WindowExpenditureNutrition = windowExpenditure;
            WindowUsefulNutrition = windowFood;
            WindowCompletedDiscussions = windowDiscussions;
            EarnedPoints = earned;
            pointsAwarded = awardTotal;
            return true;
        }

        /// <summary>Purchases one approved node; all requirement inputs are caller-owned.</summary>
        public bool TryPurchase(string nodeId, bool active, double activeDays,
            float habitConditioning, int completedBodyDiscussions,
            bool hasExceptionalAdaptationResearch, bool fantasyMode)
        {
            BodyPerkNodeDefinition definition;
            double spent;
            if (!active || !CanProgress() || !FiniteNonnegative(activeDays) ||
                !FiniteNonnegative(habitConditioning) || habitConditioning > 1f ||
                completedBodyDiscussions < 0 || !TryGetValidPurchaseTotal(out spent) ||
                !BodyPerkTreeCatalog.TryGet(nodeId, out definition) ||
                HasRawPurchase(nodeId) || AvailablePoints < definition.Cost ||
                activeDays < definition.MinimumActiveDays ||
                habitConditioning < definition.MinimumHabitConditioning ||
                completedBodyDiscussions < definition.MinimumCompletedBodyDiscussions ||
                (definition.RequiresExceptionalAdaptationResearch && !hasExceptionalAdaptationResearch) ||
                (definition.FantasyOnly && !fantasyMode)) return false;

            for (int i = 0; i < definition.Prerequisites.Count; i++)
                if (!HasValidKnownPurchase(definition.Prerequisites[i])) return false;
            for (int i = 0; i < definition.Exclusions.Count; i++)
                if (HasRawPurchase(definition.Exclusions[i])) return false;

            PurchasedNodeIds.Add(definition.Id);
            PurchasedRawCosts.Add(definition.Cost);
            return true;
        }

        /// <summary>Respec removes unlocks; earned lifetime points are never edited.</summary>
        public bool TryRespec(bool active, long currentTick, bool activeOwnedSocialOrCareJob,
            out double refundedRecordedCost)
        {
            refundedRecordedCost = 0d;
            double spent;
            if (!active || !CanProgress() || currentTick < 0 ||
                activeOwnedSocialOrCareJob || LastRespecTick < -1L ||
                (LastRespecTick >= 0L &&
                 (double)currentTick - LastRespecTick < RespecCooldownTicks) ||
                !TryGetValidPurchaseTotal(out spent) || spent <= 0d ||
                PurchasedNodeIds.Count == 0) return false;

            PurchasedNodeIds.Clear();
            PurchasedRawCosts.Clear();
            LastRespecTick = currentTick;
            refundedRecordedCost = spent;
            return true;
        }

        /// <summary>Read-only live effects; Balanced suppresses only Fantasy nodes.</summary>
        public BodyPerkEffects GetEffects(bool active, bool fantasyMode)
        {
            BodyPerkEffects effects = new BodyPerkEffects();
            double ignored;
            if (!active || !IsUsable || !TryGetValidPurchaseTotal(out ignored)) return effects;
            for (int i = 0; i < BodyPerkTreeCatalog.All.Count; i++)
            {
                BodyPerkNodeDefinition definition = BodyPerkTreeCatalog.All[i];
                if (!HasActiveNode(definition, fantasyMode)) continue;
                effects.CapacityBonus += definition.CapacityBonus;
                effects.DigestionBonus += definition.DigestionBonus;
                effects.EatingCapacityBonus += definition.EatingCapacityBonus;
                effects.FullnessPainMitigation += definition.FullnessPainMitigation;
                effects.BodyPersuasionBonus += definition.BodyPersuasionBonus;
                effects.IrritationResistanceBonus += definition.IrritationResistanceBonus;
                effects.HungerRateMultiplierBonus += definition.HungerRateMultiplierBonus;
            }
            return effects;
        }

        private bool HasActiveNode(BodyPerkNodeDefinition definition, bool fantasyMode)
        {
            if (definition.FantasyOnly && !fantasyMode) return false;
            if (!HasValidKnownPurchase(definition.Id)) return false;
            for (int i = 0; i < definition.Exclusions.Count; i++)
                if (HasRawPurchase(definition.Exclusions[i])) return false;
            for (int i = 0; i < definition.Prerequisites.Count; i++)
            {
                BodyPerkNodeDefinition prerequisite;
                if (!BodyPerkTreeCatalog.TryGet(definition.Prerequisites[i], out prerequisite) ||
                    !HasActiveNode(prerequisite, fantasyMode)) return false;
            }
            return true;
        }

        private bool HasValidKnownPurchase(string nodeId)
        {
            BodyPerkNodeDefinition definition;
            if (!BodyPerkTreeCatalog.TryGet(nodeId, out definition) ||
                PurchasedNodeIds == null || PurchasedRawCosts == null) return false;
            int found = 0;
            for (int i = 0; i < PurchasedNodeIds.Count && i < PurchasedRawCosts.Count; i++)
            {
                if (!string.Equals(PurchasedNodeIds[i], nodeId, StringComparison.Ordinal)) continue;
                if (PurchasedRawCosts[i] != definition.Cost) return false;
                found++;
            }
            return found == 1;
        }

        private bool HasRawPurchase(string nodeId)
        {
            if (PurchasedNodeIds == null) return false;
            for (int i = 0; i < PurchasedNodeIds.Count; i++)
                if (string.Equals(PurchasedNodeIds[i], nodeId, StringComparison.Ordinal)) return true;
            return false;
        }

        private bool CanProgress()
        {
            return IsUsable && FiniteNonnegative(EarnedPoints) &&
                WindowActiveTicks >= 0 && WindowActiveTicks < ActiveDayTicks &&
                LifetimeActiveTicks >= 0 && FiniteNonnegative(WindowUsefulNutrition) &&
                FiniteNonnegative(WindowExpenditureNutrition) &&
                WindowCompletedDiscussions >= 0 && WindowCompletedDiscussions <= 10 &&
                LastSocialEventTick >= -1;
        }

        private bool TryGetValidPurchaseTotal(out double total)
        {
            total = 0d;
            if (PurchasedNodeIds == null || PurchasedRawCosts == null ||
                PurchasedNodeIds.Count != PurchasedRawCosts.Count ||
                !FiniteNonnegative(EarnedPoints)) return false;

            for (int i = 0; i < PurchasedNodeIds.Count; i++)
            {
                string id = PurchasedNodeIds[i];
                int cost = PurchasedRawCosts[i];
                if (string.IsNullOrWhiteSpace(id) || cost < 0) return false;
                BodyPerkNodeDefinition definition;
                if (BodyPerkTreeCatalog.TryGet(id, out definition))
                {
                    if (cost != definition.Cost) return false;
                    // Stat reads must not allocate a set for eight single-rank
                    // nodes. Preserve unknown raw IDs without interpreting them.
                    for (int previous = 0; previous < i; previous++)
                        if (string.Equals(PurchasedNodeIds[previous], id, StringComparison.Ordinal)) return false;
                }
                total += cost;
                if (double.IsNaN(total) || double.IsInfinity(total)) return false;
            }
            // Valid purchases can never spend more than lifetime earned points.
            return total <= EarnedPoints + 1e-9d;
        }

        private static bool FiniteNonnegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }

        private static bool FiniteNonnegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }

        private static bool FinitePositive(double value)
        {
            return FiniteNonnegative(value) && value > 0d;
        }
    }
}
