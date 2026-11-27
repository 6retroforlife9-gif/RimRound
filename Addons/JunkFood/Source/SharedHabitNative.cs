using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Oinky.JunkFood
{
    public sealed class Need_JunkFood : Need_Chemical
    {
        public Need_JunkFood(Pawn pawn) : base(pawn) { }
        public override void SetInitialLevel() { curLevelInt = 0f; }
        // The colony tick owns advancement for map and world pawns. Recovery
        // must not remove this need while the native tracker iterates its list.
        public override void NeedInterval() { }
        public override bool ShowOnNeedList => JunkSharedRules.HabitsEnabled && JunkSharedRules.Active(pawn);
        public override int GUIChangeArrow => JunkSharedRules.Active(pawn) ? -1 : 0;
        internal void RestoreLevel(float value) { CurLevel = value; }
    }

    public sealed class Hediff_JunkFoodHabit : Hediff_Addiction
    {
        public override void PostRemoved()
        {
            base.PostRemoved();
            JunkFoodPolicy.Colony?.GenuineHabitRemoval(pawn);
        }
    }
    public sealed class Hediff_JunkFoodExposure : Hediff
    {
        public override void PostRemoved()
        {
            base.PostRemoved();
            JunkFoodPolicy.Colony?.GenuineExposureRemoval(pawn);
        }
    }
    public sealed class ThoughtWorker_SharedJunkCraving : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            if (!JunkSharedRules.HabitsEnabled || !JunkSharedRules.Active(pawn)) return ThoughtState.Inactive;
            var colony = JunkFoodPolicy.Colony;
            // Native mood evaluation enumerates its live thought collection.
            // Clock/hediff updates and dirty notifications belong to the tick
            // owner, need intervals and intake, never this read-only lookup.
            int stage = colony.SharedRecord(pawn, false)?.State.MoodStage ?? -1;
            return stage < 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(stage);
        }
    }

    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    internal static class SharedJunkNeedEligibility
    {
        static void Postfix(Pawn ___pawn, NeedDef nd, ref bool __result)
        {
            if (nd?.defName == JunkSharedRules.NeedName)
                __result = __result && JunkSharedRules.HabitsEnabled && JunkSharedRules.AdultKind(___pawn) &&
                    !___pawn.Dead && ___pawn.needs?.food != null && BodyModelEligibility.UsesFoodMetabolism(___pawn) &&
                    JunkSharedRules.HasNativeHabit(___pawn);
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class SharedJunkPawnSpawn
    {
        static void Postfix(Pawn __instance) { JunkFoodPolicy.Colony?.ReconcileSharedPawn(__instance); }
    }
    [HarmonyPatch(typeof(ThoughtWorker_TeetotalerVsAddict), "CurrentSocialStateInternal")]
    internal static class SharedJunkSocialExemption
    {
        static void Postfix(Pawn other, ref ThoughtState __result)
        {
            if (JunkSharedRules.HasNativeHabit(other) && !JunkSharedRules.HasRealAddiction(other))
                __result = ThoughtState.Inactive;
        }
    }
    [HarmonyPatch(typeof(AddictionUtility), nameof(AddictionUtility.CheckDrugAddictionTeachOpportunity))]
    internal static class SharedJunkTutorialExemption
    {
        static bool Prefix(Pawn pawn) => !JunkSharedRules.HasNativeHabit(pawn) || JunkSharedRules.HasRealAddiction(pawn);
    }
    [HarmonyPatch(typeof(JobGiver_SatisfyChemicalNeed), "ShouldSatisfy")]
    internal static class SharedJunkNativeNeedSearch
    {
        static void Postfix(Need need, ref bool __result) { if (need is Need_JunkFood) __result = false; }
    }
    [HarmonyPatch(typeof(Caravan_NeedsTracker), "TrySatisfyChemicalNeed")]
    internal static class SharedJunkNativeCaravanNeed
    {
        static bool Prefix(Need_Chemical chemical) => !(chemical is Need_JunkFood);
    }
}
