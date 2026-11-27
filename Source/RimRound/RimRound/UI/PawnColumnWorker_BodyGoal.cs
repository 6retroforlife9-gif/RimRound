using System;
using System.Collections.Generic;
using RimRound.Comps;
using RimRound.Simulation;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimRound.UI
{
    public class PawnColumnWorker_BodyGoal : PawnColumnWorker
    {
        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            var comp = pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
            if (comp == null || !BodyModelEligibility.IsEligible(pawn)) return;
            if (!BodyModelEligibility.CanManage(pawn))
            {
                Widgets.Label(rect.ContractedBy(0f, 2f), comp.BodyGoalLabel);
                TooltipHandler.TipRegion(rect, comp.BodyStatusDescription);
                return;
            }
            Widgets.Dropdown(rect.ContractedBy(0f, 2f), pawn,
                p => p.TryGetComp<FullnessAndDietStats_ThingComp>().AssignedGoal,
                Menu, comp.BodyGoalLabel, null, comp.BodyStatusDescription +
                    (GainMealSession.Reason(pawn) is string reason ? "\n" + reason : ""), null, null, paintable: true);
        }

        private IEnumerable<Widgets.DropdownMenuElement<BodyGoal>> Menu(Pawn pawn)
        {
            foreach (BodyGoal goal in Enum.GetValues(typeof(BodyGoal)))
            {
                BodyGoal captured = goal;
                yield return new Widgets.DropdownMenuElement<BodyGoal>
                {
                    payload = goal,
                    option = new FloatMenuOption(("RR_BodyGoal_" + goal).Translate(),
                        () => pawn.TryGetComp<FullnessAndDietStats_ThingComp>().SetBodyGoal(captured))
                };
            }
        }

        public override int GetMinWidth(PawnTable table) => 115;
        public override int GetOptimalWidth(PawnTable table) => 135;
        public override int Compare(Pawn a, Pawn b) =>
            (a.TryGetComp<FullnessAndDietStats_ThingComp>()?.AssignedGoal ?? BodyGoal.Legacy).CompareTo(
                b.TryGetComp<FullnessAndDietStats_ThingComp>()?.AssignedGoal ?? BodyGoal.Legacy);
    }
}
