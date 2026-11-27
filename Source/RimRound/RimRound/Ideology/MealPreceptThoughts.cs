using System.Collections.Generic;
using System.Linq;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Ideology
{
    // A recent meal is recorded from successful Thing.Ingested calls, never from a goal or order.
    public sealed class MealPreceptHistory : GameComponent
    {
        private Dictionary<Pawn, int> lastPreparedMealTick = new Dictionary<Pawn, int>();
        private List<Pawn> pawnKeys;
        private List<int> tickValues;

        public MealPreceptHistory(Game game) { }

        public void Record(Pawn pawn)
        {
            if (pawn != null)
                lastPreparedMealTick[pawn] = Find.TickManager.TicksGame;
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 60000 == 0)
                Prune();
        }

        private void Prune()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            foreach (Pawn pawn in lastPreparedMealTick.Keys.Where(pawn =>
                    pawn == null || pawn.Destroyed || pawn.Dead ||
                    now - lastPreparedMealTick[pawn] > 60000).ToList())
                lastPreparedMealTick.Remove(pawn);
        }

        public bool AteRecently(Pawn pawn)
        {
            int tick;
            return pawn != null && lastPreparedMealTick.TryGetValue(pawn, out tick) &&
                Find.TickManager.TicksGame - tick >= 0 &&
                Find.TickManager.TicksGame - tick <= 60000;
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) Prune();
            Scribe_Collections.Look(ref lastPreparedMealTick, "rrLastPreparedMealTick",
                LookMode.Reference, LookMode.Value, ref pawnKeys, ref tickValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && lastPreparedMealTick == null)
                lastPreparedMealTick = new Dictionary<Pawn, int>();
        }
    }

    public class ThoughtWorker_Precept_MealPortion : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn pawn)
        {
            if (!BodyModelEligibility.IsEligible(pawn) ||
                pawn.TryGetComp<PawnBodyType_ThingComp>()?.PersonallyExempt == true ||
                pawn.TryGetComp<PawnBodyType_ThingComp>()?.CategoricallyExempt == true ||
                !(Current.Game?.GetComponent<MealPreceptHistory>()?.AteRecently(pawn) ?? false))
                return ThoughtState.Inactive;
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (body == null || body.SoftLimit <= 0f) return ThoughtState.Inactive;
            float fraction = body.CurrentFullness / body.SoftLimit;
            if (def.defName == "RR_MealPortion_AbundantThought")
                return fraction >= 0.7f && fraction <= 1f;
            if (def.defName == "RR_MealPortion_ModestThought")
                return fraction >= 0.25f && fraction <= 0.7f;
            return ThoughtState.Inactive;
        }
    }

    public class ThoughtWorker_Precept_BodyIdeal : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn pawn)
        {
            if (!BodyModelEligibility.IsEligible(pawn) ||
                pawn.TryGetComp<PawnBodyType_ThingComp>()?.PersonallyExempt == true ||
                pawn.TryGetComp<PawnBodyType_ThingComp>()?.CategoricallyExempt == true)
                return ThoughtState.Inactive;
            var attitude = pawn.TryGetComp<ThingComp_PawnAttitude>();
            var body = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (attitude == null || body == null || body.BaselineBodyMassKg <= 0f ||
                pawn.WeightHediff() == null) return ThoughtState.Inactive;
            float ratio = pawn.Weight() / body.BaselineBodyMassKg;
            if (def.defName == "RR_BodyIdeal_AmpleThought")
                return attitude.weightOpinion >= WeightOpinion.Like &&
                    attitude.weightOpinion <= WeightOpinion.Fanatical && ratio >= 1.1f;
            if (def.defName == "RR_BodyIdeal_LeanThought")
                return attitude.weightOpinion >= WeightOpinion.Hate &&
                    attitude.weightOpinion <= WeightOpinion.Dislike && ratio <= 1.05f;
            return ThoughtState.Inactive;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Thing), "Ingested", new[] { typeof(Pawn), typeof(float) })]
    public static class MealPreceptHistory_Ingested
    {
        [HarmonyLib.HarmonyPrefix]
        public static void Prefix(Thing __instance, out bool __state)
        {
            __state = CommunalFeast.IsPreparedMeal(__instance);
        }

        [HarmonyLib.HarmonyPostfix]
        public static void Postfix(Pawn __0, float __result, bool __state)
        {
            if (__result > 0f && __state && ModsConfig.IdeologyActive &&
                BodyModelEligibility.IsEligible(__0) &&
                __0.TryGetComp<PawnBodyType_ThingComp>()?.PersonallyExempt != true &&
                __0.TryGetComp<PawnBodyType_ThingComp>()?.CategoricallyExempt != true &&
                (__0.Ideo?.HasPrecept(DefDatabase<PreceptDef>.GetNamedSilentFail("RR_MealPortion_Abundant")) == true ||
                 __0.Ideo?.HasPrecept(DefDatabase<PreceptDef>.GetNamedSilentFail("RR_MealPortion_Modest")) == true))
                Current.Game?.GetComponent<MealPreceptHistory>()?.Record(__0);
        }
    }
}
