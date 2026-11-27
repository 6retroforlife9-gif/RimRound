using System;
using System.Collections.Generic;
using HarmonyLib;
using RimRound.Comps;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.Utilities
{
    // Evaluate native health without RimRound's body penalties, walking bonuses or
    // Legacy capacity bonuses. The scope owns its results; pawn capacity caches and the
    // live hediff list are never modified. Dependent capacities use the same scope.
    public static class MedicalCapacityPolicy
    {
        private sealed class Evaluation
        {
            internal Pawn pawn;
            internal bool propelled;
            internal readonly Dictionary<PawnCapacityDef, float> levels = new Dictionary<PawnCapacityDef, float>();
            internal readonly HashSet<PawnCapacityDef> calculating = new HashSet<PawnCapacityDef>();
        }

        [ThreadStatic] private static Evaluation current;

        public static bool IsEvaluating(Pawn pawn) => current != null && current.pawn == pawn;
        public static bool IsPropelledEvaluation(Pawn pawn) => IsEvaluating(pawn) && current.propelled;

        internal static float CurrentLevel(PawnCapacityDef capacity)
        {
            float value;
            if (current.levels.TryGetValue(capacity, out value)) return value;
            // Native capacities form an acyclic dependency graph. A foreign
            // recursive capacity must fail closed rather than poison a pawn cache.
            if (!current.calculating.Add(capacity)) return 0f;
            try
            {
                value = PawnCapacityUtility.CalculateCapacityLevel(current.pawn.health.hediffSet, capacity);
                if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
                current.levels[capacity] = value;
                return value;
            }
            finally { current.calculating.Remove(capacity); }
        }

        private static float WithoutBodyPenalties(Pawn pawn, PawnCapacityDef capacity)
        {
            if (pawn?.health == null || pawn.Dead) return 0f;
            Evaluation previous = current;
            try
            {
                current = new Evaluation { pawn = pawn };
                return CurrentLevel(capacity);
            }
            finally { current = previous; }
        }

        public static bool CanWalkWithoutBodyPenalties(Pawn pawn)
            => WithoutBodyPenalties(pawn, PawnCapacityDefOf.Moving) > PawnCapacityDefOf.Moving.minForCapable;

        public static bool CanOperatePropelledAid(Pawn pawn)
        {
            if (pawn?.health == null || pawn.Dead || !pawn.RaceProps.Humanlike ||
                pawn.ageTracker.CurLifeStage.alwaysDowned ||
                !PawnCapacityUtility.BodyCanEverDoCapacity(pawn.RaceProps.body, PawnCapacityDefOf.Manipulation)) return false;
            Evaluation previous = current;
            try
            {
                current = new Evaluation { pawn = pawn, propelled = true };
                return pawn.health.capacities.CanBeAwake &&
                    CurrentLevel(PawnCapacityDefOf.Manipulation) >= .10f &&
                    CurrentLevel(PawnCapacityDefOf.Moving) > PawnCapacityDefOf.Moving.minForCapable;
            }
            finally { current = previous; }
        }

        public static bool MedicallyCapableOfMovement(Pawn pawn)
            => MobilityChairUtility.IsWearingAMobilityScooter(pawn)
                ? CanOperatePropelledAid(pawn) : CanWalkWithoutBodyPenalties(pawn);

        public static bool MedicallyCapableOfMovement(Pawn pawn, float currentMovement)
        {
            if (MobilityChairUtility.IsWearingAMobilityScooter(pawn)) return CanOperatePropelledAid(pawn);
            // Common unmodified capacities already are the medical answer. This
            // avoids a scope/allocation for animals and ordinary healthy pawns.
            // Any body modifier, walking bonus or Legacy effect takes the full path.
            if (currentMovement > PawnCapacityDefOf.Moving.minForCapable && !ColonyProgression.LegacyPerksActive)
            {
                var comp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
                if (comp == null) return true;
                if (comp.EffectiveStatBonuses.movementFlatBonus == 0f)
                {
                    bool bodyModifier = false;
                    foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                    {
                        if (hediff.def != Defs.HediffDefOf.RimRound_Weight && hediff.def != Defs.HediffDefOf.RimRound_Fullness) continue;
                        List<PawnCapacityModifier> modifiers = hediff.CapMods;
                        if (modifiers == null) continue;
                        foreach (PawnCapacityModifier modifier in modifiers)
                            if (modifier.capacity == PawnCapacityDefOf.Moving) { bodyModifier = true; break; }
                        if (bodyModifier) break;
                    }
                    if (!bodyModifier) return true;
                }
            }
            return CanWalkWithoutBodyPenalties(pawn);
        }

        // A powered aid replaces locomotor limbs/pelvis/spine, not the native
        // systemic and neurological conditions. PawnCapacityUtility still applies
        // every other hediff/gene modifier and its native rounding/limits.
        internal static float PropelledMovementBase(Pawn pawn)
        {
            if (pawn.ageTracker.CurLifeStage.alwaysDowned) return 0f;
            float movement = Mathf.Lerp(1f, CurrentLevel(PawnCapacityDefOf.Breathing), .2f);
            movement = Mathf.Lerp(movement, movement * CurrentLevel(PawnCapacityDefOf.BloodPumping), .2f);
            return movement * Mathf.Min(CurrentLevel(PawnCapacityDefOf.Consciousness), 1f);
        }

        public static bool FilterBodyModifiers(Hediff hediff, ref List<PawnCapacityModifier> modifiers)
        {
            if (hediff == null || modifiers == null || !IsEvaluating(hediff.pawn)) return false;
            bool weight = hediff.def == Defs.HediffDefOf.RimRound_Weight;
            bool fullness = hediff.def == Defs.HediffDefOf.RimRound_Fullness;
            if (!weight && !fullness) return false;
            modifiers = modifiers.FindAll(m => m.capacity != PawnCapacityDefOf.Moving &&
                (!weight || m.capacity != PawnCapacityDefOf.Manipulation));
            return true;
        }

        public static float RespectNativeLimits(HediffSet diffSet, PawnCapacityDef capacity, float native, float modified)
        {
            if (modified <= native) return modified;
            Pawn pawn = diffSet.pawn;
            if (capacity == PawnCapacityDefOf.Consciousness && native < .3f) return native;
            if (capacity == PawnCapacityDefOf.Moving && !MedicallyCapableOfMovement(pawn)) return native;
            if (native <= 0f &&
                (capacity != PawnCapacityDefOf.Moving && capacity != PawnCapacityDefOf.Manipulation ||
                 WithoutBodyPenalties(pawn, capacity) <= 0f)) return native;

            float limit = float.MaxValue;
            foreach (Hediff hediff in diffSet.hediffs)
            {
                if (hediff.def == Defs.HediffDefOf.RimRound_Weight || hediff.def == Defs.HediffDefOf.RimRound_Fullness) continue;
                ApplyLimits(hediff.CapMods, pawn, capacity, ref limit);
            }
            if (ModsConfig.BiotechActive && pawn.genes != null)
                foreach (Gene gene in pawn.genes.GenesListForReading)
                    if (gene.Active) ApplyLimits(gene.def.capMods, pawn, capacity, ref limit);
            return Mathf.Min(modified, limit);
        }

        private static void ApplyLimits(List<PawnCapacityModifier> modifiers, Pawn pawn, PawnCapacityDef capacity, ref float limit)
        {
            if (modifiers == null) return;
            foreach (PawnCapacityModifier modifier in modifiers)
                if (modifier.capacity == capacity) limit = Mathf.Min(limit, modifier.EvaluateSetMax(pawn));
        }
    }

    [HarmonyPatch(typeof(PawnCapacitiesHandler), nameof(PawnCapacitiesHandler.GetLevel))]
    internal static class MedicalCapacity_UncachedDependencies
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn ___pawn, PawnCapacityDef capacity, ref float __result)
        {
            if (!MedicalCapacityPolicy.IsEvaluating(___pawn)) return true;
            __result = MedicalCapacityPolicy.CurrentLevel(capacity);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameComponentUtility), nameof(GameComponentUtility.LoadedGame))]
    internal static class MedicalCapacity_LoadedHealthState
    {
        private static void Postfix()
        {
            Game loadedGame = Current.Game;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (loadedGame == null || Current.Game != loadedGame || Find.Maps == null) return;
                foreach (Map map in Find.Maps)
                {
                    // Native downing can change map registries and current jobs.
                    foreach (Pawn pawn in new List<Pawn>(map.mapPawns.AllPawnsSpawned))
                    {
                        if (!pawn.Spawned || pawn.Destroyed || pawn.Dead || pawn.Downed ||
                            !pawn.RaceProps.Humanlike || pawn.health?.capacities == null ||
                            pawn.kindDef.forceDeathOnDowned ||
                            pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving) ||
                            pawn.health.ShouldBeDead() || !pawn.health.ShouldBeDowned()) continue;
                        // Older saves can retain Mobile despite medical paralysis.
                        // Null damage/hediff does not roll native death-on-downed odds;
                        // lethal and force-death cases above remain native tick work.
                        pawn.health.Notify_HediffChanged(null);
                    }
                }
            });
        }
    }
}
