using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimRound.FeedOther
{
    [HarmonyPatch(typeof(FloatMenuMakerMap), "GetProviderOptions")]
    public static class FeedPartnerFloatMenuPatch
    {
        [HarmonyPostfix]
        public static void AddPartnerFeedingOptions(
            FloatMenuContext __0,
            List<FloatMenuOption> __1)
        {
            FloatMenuContext context = __0;
            List<FloatMenuOption> options = __1;
            if (!FeedOtherMod.Settings.manualPartnerFeedingEnabled ||
                context == null || options == null || context.IsMultiselect)
            {
                return;
            }

            Pawn actor = context.FirstSelectedPawn;
            if (actor == null || actor.Faction != Faction.OfPlayer || actor.Map != context.map)
            {
                return;
            }

            foreach (Pawn partner in context.ClickedPawns
                .Where(candidate => candidate != null && candidate != actor)
                .Distinct())
            {
                if (partner.Map != actor.Map || partner.RaceProps == null ||
                    !partner.RaceProps.Humanlike)
                {
                    continue;
                }

                AddPartnerOption(actor, partner, options);
            }
        }

        private static void AddPartnerOption(
            Pawn actor,
            Pawn partner,
            List<FloatMenuOption> options)
        {
            string targetLabel = FeedOtherUtility.FeedingTargetPercent == 70
                ? "very full"
                : FeedOtherUtility.FeedingTargetPercent + "% fullness";
            string label = "Feed " + partner.LabelShortCap + " until " + targetLabel;
            Job previewJob;
            string unavailableReason;
            if (!TryMakeFeedingJob(actor, partner, out previewJob, out unavailableReason))
            {
                FloatMenuOption disabledOption = new FloatMenuOption(
                    label + ": " + unavailableReason,
                    null);
                disabledOption.Disabled = true;
                disabledOption.revalidateClickTarget = partner;
                options.Add(disabledOption);
                return;
            }

            FloatMenuOption option = new FloatMenuOption(label, delegate
            {
                PreparePawnForManualFeeding(actor);
                PreparePawnForManualFeeding(partner);

                Job job;
                string currentReason;
                if (!TryMakeFeedingJob(actor, partner, out job, out currentReason))
                {
                    Messages.Message(
                        "Cannot feed " + partner.LabelShortCap + ": " + currentReason,
                        partner,
                        MessageTypeDefOf.RejectInput,
                        false);
                    return;
                }

                job.playerForced = true;
                actor.jobs.TryTakeOrderedJob(job, null, false);
            });
            option.revalidateClickTarget = partner;
            options.Add(option);
        }

        private static void PreparePawnForManualFeeding(Pawn pawn)
        {
            // Bed-bound and downed recipients must keep their authoritative
            // LayDown/bed-rest job. The selected feeder can never satisfy this
            // condition because manual feeder eligibility requires Moving and
            // forbids Downed, so this guard affects only the clicked recipient.
            if (pawn == null || pawn.Dead ||
                FeedOtherUtility.ShouldRemainInPlaceForManualFeeding(pawn))
            {
                return;
            }

            // A direct player order is intentionally forceful for ordinary
            // mobile pawns: drafted pawns stand down and current work, recreation,
            // movement or forced orders are interrupted. A sleeping mobile
            // recipient in bed is now preserved here so the feeder-side driver
            // can suspend that LayDown job and keep them in the same bed.
            if (pawn.Drafted && pawn.drafter != null)
            {
                pawn.drafter.Drafted = false;
            }

            // A manual feeding order owns both pawns until the session ends.
            // Healthy sleeping bed recipients return above without clearing or
            // ending LayDown; StartJob will suspend it with its reservation intact.
            pawn.jobs.ClearQueuedJobs();

            if (pawn.CurJob != null)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
            }

            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried != null && pawn.Map != null)
            {
                Thing dropped;
                pawn.carryTracker.TryDropCarriedThing(
                    pawn.Position,
                    ThingPlaceMode.Near,
                    out dropped,
                    null);
            }
        }

        private static bool TryMakeFeedingJob(
            Pawn actor,
            Pawn partner,
            out Job job,
            out string unavailableReason)
        {
            job = null;
            unavailableReason = "unavailable";

            if (actor == null || partner == null || actor.Dead || partner.Dead ||
                !actor.Spawned || !partner.Spawned || actor.Map == null ||
                partner.Map != actor.Map)
            {
                return false;
            }

            if (!FeedOtherUtility.IsManualFeederEligible(actor))
            {
                unavailableReason = actor.LabelShortCap +
                    " cannot move and manipulate well enough to feed someone";
                return false;
            }

            if (!FeedOtherUtility.IsManualRecipientEligible(partner))
            {
                unavailableReason = partner.LabelShortCap +
                    " cannot currently take part in RimRound feeding";
                return false;
            }

            if (!FeedOtherMod.Settings.bedsideFeedingEnabled &&
                FeedOtherUtility.ShouldRemainInPlaceForManualFeeding(partner))
            {
                unavailableReason = "bedside and sleeping-recipient feeding is disabled";
                return false;
            }

            if (FeedOtherUtility.CooldownBlocksManualOrder(actor, partner))
            {
                unavailableReason = "their feeding cooldown is still active";
                return false;
            }

            if (FeedOtherUtility.IsAtOrAboveStartingFullnessCutoff(partner))
            {
                unavailableReason = partner.LabelShortCap + " is already above " +
                    Mathf.RoundToInt(FeedOtherMod.Settings.startingFullnessPercent) + "% full";
                return false;
            }

            Thing meal;
            if (!FeedOtherUtility.TryFindFeedeeAndMeal(actor, partner, out meal))
            {
                unavailableReason = RecipientOrFoodReason(partner);
                return false;
            }

            // A right-click Feed order is always one-way: the selected pawn
            // retrieves and administers meals, while the clicked pawn is the
            // only eater. This makes the command independent of either pawn's
            // relationship, exemption, weight opinion, or the feeder's fullness.
            job = JobMaker.MakeJob(
                FeedOtherDefOf.RR_FeedOtherOneWay,
                meal,
                partner,
                meal.Position);
            job.count = 1;
            return true;
        }

        private static string RecipientOrFoodReason(Pawn partner)
        {
            if (!FeedOtherUtility.IsManualRecipientEligible(partner))
            {
                return partner.LabelShortCap +
                    " cannot currently take part in RimRound feeding";
            }

            return "no suitable stored meal is reachable for " + partner.LabelShortCap;
        }
    }
}
