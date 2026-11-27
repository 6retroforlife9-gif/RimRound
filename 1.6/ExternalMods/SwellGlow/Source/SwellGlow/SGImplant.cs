using RimRound.Comps;
using RimWorld;
using RimRound.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace SwellGlow
{

    public class Recipe_InstallImplantAllowDuplicates : Recipe_InstallImplant
    {
        // vanilla just filters out parts that already have the hediff;
        // we remove that filter so the bill stays available.
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(
            Pawn pawn, RecipeDef recipe)
        {
            if (!BodyModelEligibility.HasAdultBiology(pawn))
                return Enumerable.Empty<BodyPartRecord>();
            if (!ColonyProgression.LegacyPerksActive)
                return base.GetPartsToApplyOn(pawn, recipe);
            return MedicalRecipesUtility.GetFixedPartsToApplyOn(recipe, pawn, delegate (BodyPartRecord record)
            {
                if (!pawn.health.hediffSet.GetNotMissingParts().Contains(record))
                {
                    return false;
                }

                if (pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(record))
                {
                    return false;
                }

                return true;
            });
        }
    }
    public class SGImplant : HediffWithComps
    {
        public override HediffStage CurStage
        {
            get
            {
                if (ColonyProgression.LegacyPerksActive || !BodyModelEligibility.HasAdultBiology(pawn)) return null;
                // Existing colonies may have several implants. Only the first
                // supplies the new hunger cost and conversion benefit.
                var first = pawn.health?.hediffSet?.hediffs?.FirstOrDefault(h => h.def == def);
                return first == this ? base.CurStage : null;
            }
        }

        public override void Tick()
        {
            base.Tick();
            if (!ColonyProgression.LegacyPerksActive) return;
            if (Find.TickManager.TicksGame % 300 == 0)
            {
                if (pawn != null && pawn.Spawned)
                {
                    FullnessAndDietStats_ThingComp pawnComp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
                    pawnComp?.activeWeightGainRequests.Enqueue(new WeightGainRequest(20 * Severity, Find.TickManager.TicksGame + 10, 0, false));
                }
            }
        }
    }
}
