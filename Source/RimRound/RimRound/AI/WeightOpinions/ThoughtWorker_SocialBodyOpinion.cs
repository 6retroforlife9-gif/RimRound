using RimRound.Comps;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.AI
{
    public sealed class ThoughtWorker_SocialBodyOpinion : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn observer, Pawn target)
        {
            // Creation-time native filters do not remove every cached thought
            // after a transformation. Recheck both current participants here.
            if (observer == null || target == null || observer == target ||
                observer.Dead || target.Dead || observer.Destroyed || target.Destroyed ||
                observer.IsSubhuman || target.IsSubhuman ||
                !CanObserveBodyOpinion(observer) || !BodyModelEligibility.IsEligible(target) ||
                !RelationsUtility.PawnsKnowEachOther(observer, target))
                return ThoughtState.Inactive;

            var attitude = observer.TryGetComp<ThingComp_PawnAttitude>();
            var body = target.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (attitude?.HasSocialBodyTaste != true || body?.UsesBodyModel != true ||
                target.WeightHediff() == null)
                return ThoughtState.Inactive;

            // These are saved biological inputs. An opinion read must never
            // create a baseline, weight hediff or preference state.
            float massKg = target.Weight();
            float baselineKg = body.BaselineBodyMassKg;
            if (!BodyEnergyState.IsFinite(massKg) || massKg <= 0f ||
                !BodyEnergyState.IsFinite(baselineKg) || baselineKg <= 0f)
                return ThoughtState.Inactive;
            int stage = BodySocialTastePolicy.StageFor(attitude.SocialBodyTasteLevel,
                massKg, baselineKg,
                observer.story.CaresAboutOthersAppearance, HasEmbodiedAbundance(observer));
            return stage < 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(stage);
        }

        internal static bool CanObserveBodyOpinion(Pawn observer)
        {
            return observer != null && !observer.Dead && !observer.Destroyed &&
                !observer.IsSubhuman && PawnHelperEligibility.HasSocialIdentity(observer) &&
                observer.needs?.mood != null && observer.story?.traits != null;
        }

        internal static bool HasEmbodiedAbundance(Pawn observer)
        {
            if (!ModsConfig.IdeologyActive || observer?.Ideo == null) return false;
            MemeDef abundance = DefDatabase<MemeDef>.GetNamedSilentFail("RR_EmbodiedAbundance");
            return abundance != null && observer.Ideo.HasMeme(abundance);
        }
    }
}
