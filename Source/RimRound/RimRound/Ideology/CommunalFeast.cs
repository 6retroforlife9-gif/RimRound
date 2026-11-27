using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    /// <summary>Food and participation rules for the Ideology ritual. Eating remains a vanilla Ingest job.</summary>
    public static class CommunalFeast
    {
        public const string RitualDefName = "RR_CommunalFeast";
        public const float MaximumMealGradeOffset=.15f;
        public static float MealGrade(ThingDef dish)
        {
            if(dish?.ingestible==null || dish==ThingDefOf.Pemmican || dish==ThingDefOf.BabyFood)return 0f;
            var preference=dish.ingestible.preferability;
            if(preference>=FoodPreferability.MealLavish)return 1f;
            if(preference>=FoodPreferability.MealFine)return .75f;
            if(preference>=FoodPreferability.MealSimple)return .4f;
            return .1f;
        }

        public static bool IsFeast(LordJob_Ritual ritual)
        {
            return AbundanceRituals.IsFoodRitual(ritual?.Ritual);
        }

        public static bool IsPreparedMeal(Thing thing)
        {
            return FeastCompatibilityPolicy.IsMeal(thing);
        }

        public static Thing FindPermittedMeal(Pawn pawn, IntVec3 spot)
        {
            if (pawn?.Map == null || pawn.needs?.food == null ||
                pawn.needs.food.CurLevelPercentage > 0.9f)
                return null;

            Predicate<Thing> allowed = meal => CanUseMeal(pawn, meal, spot);

            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                allowed, null, 0, 12);
        }

        public static bool HasPermittedMeal(Pawn pawn, IntVec3 spot)
        {
            return FindPermittedMeal(pawn, spot) != null;
        }

        private static bool CanUseMeal(Pawn pawn, Thing meal, IntVec3 spot)
        {
            return IsPreparedMeal(meal) &&
                pawn.WillEat(meal) && !meal.IsForbidden(pawn) &&
                meal.IsSociallyProper(pawn) && pawn.CanReserve(meal) &&
                BodyFeedingPolicy.CanFitFood(pawn, meal,
                    FoodUtility.NutritionForEater(pawn, meal));
        }

        public static bool HasEnoughMeals(IEnumerable<Pawn> participants, Map map, IntVec3 spot)
        {
            if (participants == null || map == null || !HasTable(map, spot)) return false;
            var diners = participants.Where(p=>CanParticipate(p) && p.needs.food.CurLevelPercentage<=.9f).Distinct().ToList();
            if (diners.Count == 0 || diners.Any(pawn => pawn.Map != map)) return false;
            var stock = map.listerThings.AllThings
                .Where(IsPreparedMeal)
                .ToDictionary(meal => meal, meal => meal.stackCount);
            // Match portions to diners instead of greedily assigning the first
            // stack: a flexible diner must not take the only vegetarian meal
            // from a diner whose policy forbids the other available dishes.
            var portions = stock.SelectMany(pair => Enumerable.Repeat(pair.Key,
                Math.Min(pair.Value, diners.Count))).ToList();
            var allowed = diners.Select(pawn => portions.Select(meal =>
                CanUseMeal(pawn, meal, spot) && pawn.CanReach(meal,
                    PathEndMode.ClosestTouch, Danger.Some)).ToArray()).ToArray();
            int[] owners = Enumerable.Repeat(-1, portions.Count).ToArray();
            for (int diner = 0; diner < diners.Count; diner++)
            {
                if (!AssignPortion(diner, allowed, owners, new bool[portions.Count])) return false;
            }
            return true;
        }

        private static bool AssignPortion(int diner, bool[][] allowed, int[] owners, bool[] visited)
        {
            for (int portion = 0; portion < owners.Length; portion++)
            {
                if (visited[portion] || !allowed[diner][portion]) continue;
                visited[portion] = true;
                if (owners[portion] >= 0 && !AssignPortion(owners[portion], allowed, owners, visited)) continue;
                owners[portion] = diner;
                return true;
            }
            return false;
        }

        public static bool HasTable(Map map, IntVec3 spot)
        {
            return AbundanceBeds.HasTable(map,spot);
        }

        public static bool CanParticipate(Pawn pawn)
        {
            return pawn?.Spawned==true && pawn.RaceProps.Humanlike && !pawn.Dead && pawn.needs?.food!=null;
        }

        public static RitualOutcomeCompData_CommunalMeals MealData(LordJob_Ritual ritual)
        {
            if (!IsFeast(ritual) || ritual.Ritual.outcomeEffect?.def?.comps == null)
                return null;
            var comp = ritual.Ritual.outcomeEffect.def.comps
                .OfType<RitualOutcomeComp_CommunalMeals>().FirstOrDefault();
            return comp == null ? null : ritual.Ritual.outcomeEffect.DataForComp(comp)
                as RitualOutcomeCompData_CommunalMeals;
        }

        public static int SuccessfulEaters(LordJob_Ritual ritual)
        {
            return MealData(ritual)?.NutritionByPawn.Count ?? 0;
        }
    }

    public class RitualBehaviorWorker_CommunalFeast : RitualBehaviorWorker
    {
        private const int FeastCooldownTicks = 180000;
        private List<AbundanceMeal> pendingMeals;
        private List<RitualAttendanceGuest> pendingGuests;
        private bool confirmedBegin;
        [ThreadStatic] private static List<RitualAttendanceGuest> startingGuests;

        internal static bool KeepOfficerRestAtBegin(Pawn pawn) => startingGuests?.Any(g=>
            g.Guest==pawn && !g.NeedsTransport && g.DestinationBed!=null && AbundanceRituals.IsOfficer(pawn) &&
            pawn.CurJob?.loadID==g.InitialJobId && pawn.CurJob.def==JobDefOf.LayDown &&
            pawn.CurrentBed()==g.DestinationBed && pawn.ownership?.OwnedBed==g.DestinationBed &&
            RitualAttendance.SafetyReason(pawn,true)==null)==true;

        public RitualBehaviorWorker_CommunalFeast() { }
        public RitualBehaviorWorker_CommunalFeast(RitualBehaviorDef def) : base(def) { }

        public override string CanStartRitualNow(TargetInfo target, Precept_Ritual ritual,
            Pawn selectedPawn = null, Dictionary<string, Pawn> forcedForRole = null)
        {
            string reason = base.CanStartRitualNow(target, ritual, selectedPawn, forcedForRole);
            if (reason != null) return reason;
            if (!target.IsValid || target.Map == null)
                return "RR_FeastInvalidTarget".Translate();
            if (ritual.lastFinishedTick >= 0 &&
                Find.TickManager.TicksGame - ritual.lastFinishedTick < FeastCooldownTicks)
                return "RR_FeastCooldown".Translate(
                    (FeastCooldownTicks - (Find.TickManager.TicksGame - ritual.lastFinishedTick))
                        .ToStringTicksToPeriod());
            bool ceremonial=ritual.def.defName==AbundanceRituals.Ceremony;
            if (ceremonial && !(target.Thing is Building_Bed))return "RR_AbundanceHostRequired".Translate();
            if (target.Thing is Building_Bed bed && !AbundanceBeds.Valid(bed,ritual.ideo))return "RR_AbundanceBedOwnerMismatch".Translate();
            if (!ceremonial && !CommunalFeast.HasTable(target.Map, target.Cell))
                return "RR_FeastRequiresTable".Translate();
            return null;
        }

        public override bool PawnCanFillRole(Pawn pawn, RitualRole role, out string reason,
            TargetInfo ritualTarget)
        {
            if (!CommunalFeast.CanParticipate(pawn))
            {
                reason = "RR_FeastParticipantIneligible".Translate();
                return false;
            }
            reason=FeastStartRisk(pawn);
            if(reason!=null)return false;
            return base.PawnCanFillRole(pawn, role, out reason, ritualTarget);
        }

        internal static string FeastStartRisk(Pawn pawn)
        {
            if(pawn==null || pawn.Dead)return "RR_AttendanceInvalid".Translate();
            if(pawn?.carryTracker?.CarriedThing is Pawn || pawn?.CurJobDef==JobDefOf.TendPatient ||
                pawn?.CurJobDef==JobDefOf.Rescue || pawn?.CurJobDef==JobDefOf.BeatFire)
                return "RR_FeastEmergencyDuty".Translate(pawn.LabelShort);
            if(RitualAttendance.NeedsCarrying(pawn))return RitualAttendance.SafetyReason(pawn,true);
            if(pawn.Drafted || pawn.InMentalState || pawn.Suspended || pawn.Deathresting || pawn.IsBurning() ||
                !pawn.health.capacities.CanBeAwake || pawn.health.hediffSet.HasHediff(HediffDefOf.Anesthetic))
                return "RR_AttendanceUnsafe".Translate(pawn.LabelShort);
            if(pawn.health.hediffSet.BleedRateTotal>0f || pawn.health.hediffSet.InLabor() ||
                pawn.health.hediffSet.HasTemperatureInjury(TemperatureInjuryStage.Serious) ||
                HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn))return "RR_AttendanceMedical".Translate(pawn.LabelShort);
            if(pawn.needs?.rest?.CurLevelPercentage<.1f || pawn.needs?.food?.Starving==true ||
                !pawn.Awake() && !RitualAttendance.CanWakeForFeast(pawn))return "RR_AttendanceNeeds".Translate(pawn.LabelShort);
            return null;
        }

        public override void TryExecuteOn(TargetInfo target, Pawn organizer,
            Precept_Ritual ritual, RitualObligation obligation,
            RitualRoleAssignments assignments, bool playerForced = false)
        {
            string reason=AbundanceRituals.Validate(target,ritual,assignments,out pendingMeals,out pendingGuests);
            if (reason!=null)
            {
                Messages.Message(reason,
                    target, MessageTypeDefOf.RejectInput);
                return;
            }
            var previous=startingGuests;
            startingGuests=pendingGuests;
            confirmedBegin=playerForced;
            try { base.TryExecuteOn(target, organizer, ritual, obligation,assignments, playerForced); }
            finally { confirmedBegin=false;startingGuests=previous; pendingMeals=null; pendingGuests=null; }
        }
        protected override LordJob CreateLordJob(TargetInfo target,Pawn organizer,Precept_Ritual ritual,RitualObligation obligation,RitualRoleAssignments assignments)
        {
            // Native Begin has interrupted ordinary work and rest by this point.
            // A bedside role keeps its exact LayDown job and reservations.
            if(confirmedBegin)
                foreach(var pawn in assignments.Participants)
                    if(!pawn.Awake() && RitualAttendance.CanWakeForFeast(pawn) && pawn.jobs.curDriver!=null)
                        pawn.jobs.curDriver.asleep=false;
            return new LordJob_AbundanceMeals(target,ritual,obligation,assignments,pendingMeals,pendingGuests);
        }
        public override string GetExplanation(Precept_Ritual ritual,RitualRoleAssignments assignments,float quality) =>
            (ritual.def.defName == AbundanceRituals.Ceremony ? "RR_CeremonyStartSummary" : "RR_FeastStartSummary").Translate();

        public override void PostCleanup(LordJob_Ritual ritual)
        {
            base.PostCleanup(ritual);
            // Ordinary work or a player order can resume a queued feast job on
            // cleanup, even when the gathering had no transported guests.
            var meals = ritual as LordJob_AbundanceMeals;
            if (meals?.lord != null)
                RitualAttendanceEvent.DetachJobs(meals.lord,meals.Meals.Select(m=>m.Eater)
                    .Concat(meals.Meals.Select(m=>m.Server)).Concat(meals.assignments.Participants).Concat(meals.lord.ownedPawns));
            // Native ritual visuals fade for 60 frames after their lord is
            // removed. Keep the fade, but detach this feast's retired lord so
            // a save during those frames cannot serialize an orphan reference.
            var drawers = ritual?.lord?.lordManager?.stencilDrawers;
            if (drawers == null) return;
            foreach (var drawer in drawers)
                if (drawer.sourceLord == ritual.lord) drawer.sourceLord = null;
        }
    }

    // Native player-started rituals end every spectator job before making the
    // Lord, so the normal attendance duty guard is too late for a resting
    // officer. Preserve only the exact validated rest job during this one
    // synchronous Begin call; later player orders and care interruptions run.
    [HarmonyPatch(typeof(Pawn_JobTracker),nameof(Pawn_JobTracker.EndCurrentJob))]
    internal static class CommunalFeastKeepOfficerRestOnBegin
    {
        static bool Prefix(Pawn ___pawn,JobCondition condition,bool startNewJob) =>
            condition!=JobCondition.InterruptForced || startNewJob ||
            !RitualBehaviorWorker_CommunalFeast.KeepOfficerRestAtBegin(___pawn);
    }

    public class JobGiver_CommunalFeastMeal : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var ritual = pawn?.GetLord()?.LordJob as LordJob_Ritual;
            if (!CommunalFeast.IsFeast(ritual)) return null;
            Thing meal = CommunalFeast.FindPermittedMeal(pawn, ritual.Spot);
            if (meal == null) return null;
            Job job = JobMaker.MakeJob(JobDefOf.Ingest, meal);
            job.count = FoodUtility.WillIngestStackCountOf(pawn, meal.def,
                FoodUtility.NutritionForEater(pawn, meal));
            return job;
        }
    }

    public sealed class RitualOutcomeCompData_CommunalMeals : RitualOutcomeComp_Data
    {
        private Dictionary<Pawn, float> nutritionByPawn = new Dictionary<Pawn, float>();
        private List<Pawn> pawnKeys;
        private List<float> nutritionValues;
        private float totalGradeNutrition;
        private float totalGradedNutrition;
        public float AverageGrade => totalGradedNutrition>0f ? Mathf.Clamp01(totalGradeNutrition/totalGradedNutrition) : 0f;

        public IReadOnlyDictionary<Pawn, float> NutritionByPawn => nutritionByPawn;

        public void Record(Pawn pawn, float nutrition)
        { Record(pawn,nutrition,0f); }
        public void Record(Pawn pawn,float nutrition,float grade)
        {
            if (pawn == null || float.IsNaN(nutrition) || float.IsInfinity(nutrition) || nutrition <= 0f)
                return;
            float prior;
            nutritionByPawn.TryGetValue(pawn, out prior);
            nutritionByPawn[pawn] = prior + nutrition;
            totalGradedNutrition+=nutrition;
            totalGradeNutrition+=nutrition*Mathf.Clamp01(grade);
        }

        public override void Reset()
        {
            nutritionByPawn.Clear();
            totalGradeNutrition=totalGradedNutrition=0f;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref nutritionByPawn, "rrFeastNutritionByPawn",
                LookMode.Reference, LookMode.Value, ref pawnKeys, ref nutritionValues);
            Scribe_Values.Look(ref totalGradedNutrition,"rrFeastGradedNutrition");
            Scribe_Values.Look(ref totalGradeNutrition,"rrFeastGradeNutrition");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && nutritionByPawn == null)
                nutritionByPawn = new Dictionary<Pawn, float>();
        }
    }

    public class RitualOutcomeComp_CommunalMeals : RitualOutcomeComp_Quality
    {
        public override RitualOutcomeComp_Data MakeData()
        {
            return new RitualOutcomeCompData_CommunalMeals();
        }

        public override float Count(LordJob_Ritual ritual, RitualOutcomeComp_Data data)
        {
            var meals = data as RitualOutcomeCompData_CommunalMeals;
            return meals?.NutritionByPawn.Count ?? 0;
        }
        public override float QualityOffset(LordJob_Ritual ritual,RitualOutcomeComp_Data data)
            => base.QualityOffset(ritual,data)+CommunalFeast.MaximumMealGradeOffset*((data as RitualOutcomeCompData_CommunalMeals)?.AverageGrade ?? 0f);
        public override string GetDesc(LordJob_Ritual ritual=null,RitualOutcomeComp_Data data=null)
        {
            if(data==null)return "RR_FeastMaximumQuality".Translate(curve.Points[curve.PointsCount-1].y.ToStringPercent(),CommunalFeast.MaximumMealGradeOffset.ToStringPercent());
            var meals=(RitualOutcomeCompData_CommunalMeals)data;
            return "RR_FeastActualQuality".Translate(meals.NutritionByPawn.Count,curve.Evaluate(meals.NutritionByPawn.Count).ToStringPercent(),
                (CommunalFeast.MaximumMealGradeOffset*meals.AverageGrade).ToStringPercent());
        }
        public override QualityFactor GetQualityFactor(Precept_Ritual ritual,TargetInfo target,RitualObligation obligation,
            RitualRoleAssignments assignments,RitualOutcomeComp_Data data)
        {
            var plan=FeastDiagnostics.Projection(ritual,target,assignments);
            int diners=plan?.Diners ?? 0;
            float intake=curve.Evaluate(diners), grade=CommunalFeast.MaximumMealGradeOffset*(plan?.Grade ?? 0f);
            return new QualityFactor{label=label.CapitalizeFirst(),count=diners.ToString(),
                quality=intake+grade,qualityChange=(intake+grade).ToStringWithSign("0.#%"),
                toolTip="RR_FeastExpectedQuality".Translate(diners,intake.ToStringPercent(),grade.ToStringPercent()),
                positive=intake+grade>0f,priority=3};
        }

        public override IEnumerable<string> BlockingIssues(Precept_Ritual ritual,
            TargetInfo target, RitualRoleAssignments assignments)
        {
            string reason=FeastDiagnostics.Projection(ritual,target,assignments)?.Failure;
            if(reason!=null)yield return reason;
        }
        public override void Notify_AssignmentsChanged(RitualRoleAssignments assignments,RitualOutcomeComp_Data data)
            => FeastDiagnostics.Invalidate(assignments);
    }

    public class RitualOutcomeEffectWorker_CommunalFeast : RitualOutcomeEffectWorker_FromQuality
    {
        public RitualOutcomeEffectWorker_CommunalFeast() { }
        public RitualOutcomeEffectWorker_CommunalFeast(RitualOutcomeEffectDef def) : base(def) { }

        public override RitualOutcomePossibility GetOutcome(float quality, LordJob_Ritual ritual)
        {
            return ritual is LordJob_AbundanceMeals feast && feast.Ceremonial && CommunalFeast.SuccessfulEaters(ritual) == 0
                ? def.outcomeChances[0] : base.GetOutcome(quality, ritual);
        }

        public override void Apply(float progress, Dictionary<Pawn, int> totalPresence,
            LordJob_Ritual jobRitual)
        {
            var meals = CommunalFeast.MealData(jobRitual);
            var eaters = new Dictionary<Pawn, int>();
            if (meals != null && totalPresence != null)
                foreach (var presence in totalPresence)
                    if (meals.NutritionByPawn.ContainsKey(presence.Key))
                        eaters.Add(presence.Key, presence.Value);
            base.Apply(progress,jobRitual is LordJob_AbundanceMeals feast && !feast.Ceremonial ? totalPresence : eaters,jobRitual);
            if (Find.HistoryEventsManager == null) return;
            var eventDef = DefDatabase<HistoryEventDef>.GetNamedSilentFail("RR_AteAtCommunalFeast");
            if (eventDef == null) return;
            foreach (Pawn pawn in eaters.Keys)
                Find.HistoryEventsManager.RecordEvent(new HistoryEvent(eventDef,
                    pawn.Named(HistoryEventArgsNames.Doer)));
        }
    }

    [HarmonyPatch(typeof(Thing), "Ingested", new[] { typeof(Pawn), typeof(float) })]
    public static class CommunalFeast_Ingested
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance, out bool __state)
        {
            __state = CommunalFeast.IsPreparedMeal(__instance);
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __0, float __result, bool __state)
        {
            Pawn pawn = __0;
            var ritual = pawn?.GetLord()?.LordJob as LordJob_Ritual;
            if(ritual is LordJob_AbundanceMeals)
            {
                if(__state && __result>0 && pawn.TryGetComp<RimRound.Comps.FullnessAndDietStats_ThingComp>()?.UsesBodyModel!=true)
                    AbundanceRituals.Committed(pawn,__result,0);
                return;
            }
            if (__result <= 0f || !CommunalFeast.IsFeast(ritual) ||
                pawn.CurJob?.def != JobDefOf.Ingest ||
                !__state) return;
            var data = CommunalFeast.MealData(ritual);
            if (data == null) return;
            data.Record(pawn,__result,CommunalFeast.MealGrade(pawn.CurJob.targetA.Thing?.def));
        }
    }
}
