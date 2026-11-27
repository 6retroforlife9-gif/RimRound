using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimRound.Simulation;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimRound.Ideology
{
    // Presentation only: each validation replaces a dialog's text. Neither the
    // planner nor execution reads this cache. Entries die with the dialog.
    public static class FeastDiagnostics
    {
        internal sealed class Preview
        {
            public string Text,Failure;
            public int Diners,Frame;
            public float Grade;
            public bool Feeder;
            public TargetInfo Target;
        }
        static readonly ConditionalWeakTable<RitualRoleAssignments,Preview> previews=new ConditionalWeakTable<RitualRoleAssignments,Preview>();
        public static void Publish(TargetInfo target,RitualRoleAssignments assignments,List<AbundanceMeal> meals,
            List<RitualAttendanceGuest> guests,string failure)
        {
            if(assignments==null)return;
            var projected=previews.GetOrCreateValue(assignments);
            projected.Frame=UnityEngine.Time.frameCount;projected.Target=target;
            projected.Failure=failure;
            projected.Diners=0;projected.Grade=0f;projected.Feeder=false;
            var lines=new List<string>();
            if(failure==null)
            {
                var planned=meals.Where(m=>!m.ReservedForLater && m.PlannedNutrition>0).ToList();
                projected.Diners=planned.Count;
                double nutrition=planned.Sum(m=>m.PlannedNutrition);
                if(nutrition>0)projected.Grade=(float)(planned.Sum(m=>m.PlannedGradeNutrition)/nutrition);
                projected.Feeder=planned.Any(m=>m.Server!=m.Eater && AbundanceRoles.Has(m.Server,AbundanceRoles.Feeder));
                var dishes=meals.SelectMany(m=>m.Servings).Where(s=>s.Count>0 && s.OutputDef!=null)
                    .GroupBy(s=>s.OutputDef).OrderBy(g=>g.Key.label)
                    .Select(g=>g.Sum(s=>s.Count)+" × "+g.Key.LabelCap);
                if(dishes.Any())lines.Add("RR_FeastPlannedFood".Translate(string.Join(", ",dishes)));
                var mobile=meals.Where(m=>m.Eater!=assignments.FirstAssignedPawn(AbundanceRituals.Host) &&
                    guests?.Any(g=>g.Guest==m.Eater)!=true).ToList();
                int available=AbundanceBeds.DiningSeats(target.Map,target.Cell).Distinct().Count(c=>
                    guests?.Any(g=>g.Place==c || g.Approach==c)!=true && mobile.Any(m=>
                    m.Eater.CanReserveSittableOrSpot(c) && m.Eater.CanReach(c,PathEndMode.OnCell,Danger.Some)));
                lines.Add("RR_FeastPlannedSeats".Translate(mobile.Count(m => !m.ReservedForLater), available));
                foreach(var meal in meals)
                {
                    if(meal.ReservedForLater)
                        lines.Add((meal.LaterDispenser==null ? "RR_FeastPackedForLater" : "RR_FeastDispenserForLater").Translate(meal.Eater.LabelShort));
                    else if(meal.Completion==FeastAllocationCompletion.WholePortionLimit)
                        lines.Add("RR_FeastWholePortionLimit".Translate(meal.Eater.LabelShort));
                    else if(meal.Completion==FeastAllocationCompletion.AttendWithoutEating)
                        lines.Add("RR_FeastAttendWithoutEating".Translate(meal.Eater.LabelShort));
                }
            }
            // Blocking issues are already displayed by the native dialog.
            projected.Text=string.Join("\n",lines);
        }
        internal static Preview Projection(Precept_Ritual ritual,TargetInfo target,RitualRoleAssignments assignments)
        {
            if(assignments==null || ritual==null || !target.IsValid)return null;
            if(!previews.TryGetValue(assignments,out var p) || p.Frame!=UnityEngine.Time.frameCount || p.Target!=target)
                AbundanceRituals.Validate(target,ritual,assignments,out _);
            return previews.TryGetValue(assignments,out p) ? p : null;
        }
        internal static void Invalidate(RitualRoleAssignments assignments)
        { if(assignments!=null)previews.Remove(assignments); }
        public static string Explanation(RitualRoleAssignments assignments)
        {
            string text="RR_AbundanceMealExplanation".Translate();
            if(assignments!=null && previews.TryGetValue(assignments,out var preview) && !preview.Text.NullOrEmpty())
                text+="\n\n"+preview.Text;
            return text;
        }
        public static string Waiting(LordJob_AbundanceMeals ritual)
        {
            if(ritual==null || ritual.cancelled || ritual.CancelRequested)return null;
            var guest=RitualAttendance.For(ritual)?.Guests.FirstOrDefault(g=>!g.Finished &&
                g.State!=RitualAttendanceState.Placed && g.State!=RitualAttendanceState.Attending);
            if(guest!=null)return "RR_FeastWaitingArrival".Translate(guest.Guest.LabelShort);
            if(!ritual.HostSettled && ritual.HostPawn!=null)return "RR_FeastWaitingArrival".Translate(ritual.HostPawn.LabelShort);
            var held=ritual.Meals.SelectMany(m=>m.Servings).FirstOrDefault(s=>s.Count>0 && s.Collected && !s.Placed && s.Collector!=null);
            if(held!=null)return "RR_FeastWaitingPlacement".Translate(held.Collector.LabelShort);
            var pending=ritual.Meals.SelectMany(m=>m.Servings.Select(s=>new{Meal=m,Supply=s}))
                .FirstOrDefault(a=>a.Supply.Count>0 && !a.Supply.Collected);
            if(pending!=null)return "RR_FeastWaitingCollection".Translate((pending.Supply.Collector ?? pending.Meal.Server).LabelShort);
            return null;
        }
    }
}
