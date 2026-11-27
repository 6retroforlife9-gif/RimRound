using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    internal static class RitualAttendanceAdapters
    {
        [ThreadStatic] internal static Precept_Ritual EligibilityRitual;
        internal static bool AssignmentPatched, GatheringPatched;
        internal static bool SafeSettledGuest(Pawn pawn)
        {
            var e=RitualAttendance.For(pawn);
            if(e?.Settled(pawn)!=true || e.Lord==null || e.Ritual==null || e.Ritual.cancelled ||
                !e.Map.lordManager.lords.Contains(e.Lord) || pawn.GetLord()!=e.Lord ||
                e.Ritual.assignments?.PawnParticipating(pawn)!=true || RitualAttendance.SafetyReason(pawn)!=null)return false;
            var guest=e.GuestFor(pawn);
            return RitualAttendance.PlaceSafe(pawn,guest.Place,pawn.Map,guest.DestinationBed,guest.Chair,allowPassingPawns:true);
        }
        internal static bool AdaptDowned(Pawn pawn,RitualRole role,Precept_Ritual ritual,TargetInfo target) =>
            AssignmentPatched && GatheringPatched && pawn?.Downed==true && RitualAttendance.AdmissionReason(pawn,role,ritual,target)==null;
        internal static bool DownedForAssignment(Pawn pawn,RitualRole role,Precept_Ritual ritual,TargetInfo target) =>
            pawn.Downed && !AdaptDowned(pawn,role,ritual,target);
        internal static bool DownedForGathering(Pawn pawn)
        {
            if(!pawn.Downed)return false;
            if(!AssignmentPatched || !GatheringPatched || !RitualAttendance.Supported(EligibilityRitual) ||
                RitualAttendance.SafetyReason(pawn,AbundanceRituals.IsFoodRitual(EligibilityRitual))!=null)return true;
            // A preview pawn is still spawned. A carried one needs the exact
            // current coordinator/carrier/job pair, never just MapHeld.
            return !pawn.Spawned && RitualAttendance.For(pawn)?.OwnedTransport(pawn)!=true;
        }
        internal static string Preview(TargetInfo target,Precept_Ritual ritual,RitualRoleAssignments assignments,Pawn only=null)
        {
            var selected=assignments?.Participants.Where(RitualAttendance.NeedsCarrying).Where(p=>only==null || p==only).ToList();
            if(selected==null || selected.Count==0)
            {
                if(only!=null && RitualAttendance.NeedsCarrying(only))
                    return "RR_AttendanceNeedsCarrying".Translate(only.LabelShort)+": "+
                        (RitualAttendance.AdmissionReason(only,assignments?.RoleForPawn(only),ritual,target) ?? "RR_AttendanceSelectToPlan".Translate().ToString());
                return null;
            }
            string failure=RitualAttendance.Validate(target,ritual,assignments,out var guests);
            var lines=new List<string>();
            foreach(Pawn pawn in selected)
            {
                var g=guests.FirstOrDefault(x=>x.Guest==pawn);
                if(g==null) { lines.Add("RR_AttendanceNeedsCarrying".Translate(pawn.LabelShort)+": "+(failure ?? "RR_AttendanceExistingBedside".Translate().ToString()));continue; }
                string place=g.DestinationBed!=null ? g.DestinationBed.LabelShort :
                    g.FloorSeat ? "RR_AttendanceFloorSeat".Translate().ToString() :
                    g.Chair ? "RR_AttendanceChair".Translate().ToString() : "RR_AttendanceGround".Translate().ToString();
                lines.Add(g.NeedsTransport ? "RR_AttendancePreview".Translate(pawn.LabelShort,place,g.Place.ToString(),g.Helper?.LabelShort ?? "?",g.Approach.ToString()).ToString() :
                    "RR_AttendanceAlreadySettled".Translate(pawn.LabelShort,place).ToString());
            }
            return string.Join("\n",lines);
        }
    }

    [HarmonyPatch]
    internal static class RitualAttendanceAssignmentEligibility
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(RitualRoleAssignments),nameof(RitualRoleAssignments.PawnNotAssignableReason),
            new[]{typeof(Pawn),typeof(RitualRole),typeof(Precept_Ritual),typeof(RitualRoleAssignments),typeof(TargetInfo),typeof(bool).MakeByRefType()});
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.ToList();var getter=AccessTools.PropertyGetter(typeof(Pawn),nameof(Pawn.Downed));
            var matches=code.Select((c,i)=>new{c,i}).Where(x=>x.c.Calls(getter)).ToList();
            if(matches.Count!=1) { Log.Error("[RimRound] Assisted attendance assignment adapter unavailable; native eligibility remains unchanged.");return code; }
            int index=matches[0].i;
            var replacement=new List<CodeInstruction> { new CodeInstruction(OpCodes.Ldarg_1),new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_S,4),new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(RitualAttendanceAdapters),nameof(RitualAttendanceAdapters.DownedForAssignment))) };
            replacement[0].labels.AddRange(code[index].labels);replacement[0].blocks.AddRange(code[index].blocks);
            code.RemoveAt(index);code.InsertRange(index,replacement);RitualAttendanceAdapters.AssignmentPatched=true;return code;
        }
        static void Postfix(Pawn p,RitualRole role,Precept_Ritual ritual,TargetInfo ritualTarget,ref string __result)
        {
            if(!RitualAttendance.NeedsCarrying(p) || __result==null)return;
            if(__result=="MessageRitualPawnDowned".Translate(p).ToString())
                __result=RitualAttendance.AdmissionReason(p,role,ritual,ritualTarget) ?? __result;
        }
    }
    [HarmonyPatch(typeof(GatheringsUtility),nameof(GatheringsUtility.ShouldPawnKeepAttendingRitual))]
    internal static class RitualAttendanceGatheringContext
    {
        static void Prefix(Precept_Ritual ritual,out Precept_Ritual __state) { __state=RitualAttendanceAdapters.EligibilityRitual;RitualAttendanceAdapters.EligibilityRitual=ritual; }
        static Exception Finalizer(Exception __exception,Precept_Ritual __state) { RitualAttendanceAdapters.EligibilityRitual=__state;return __exception; }
    }
    [HarmonyPatch(typeof(GatheringsUtility),nameof(GatheringsUtility.ShouldGuestKeepAttendingRitual))]
    internal static class RitualAttendanceGatheringEligibility
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.ToList();var getter=AccessTools.PropertyGetter(typeof(Pawn),nameof(Pawn.Downed));
            var matches=code.Where(c=>c.Calls(getter)).ToList();
            if(matches.Count!=1) { Log.Error("[RimRound] Assisted attendance gathering adapter unavailable; native eligibility remains unchanged.");return code; }
            matches[0].opcode=OpCodes.Call;matches[0].operand=AccessTools.Method(typeof(RitualAttendanceAdapters),nameof(RitualAttendanceAdapters.DownedForGathering));
            RitualAttendanceAdapters.GatheringPatched=true;return code;
        }
    }
    [HarmonyPatch(typeof(RitualBehaviorWorker),nameof(RitualBehaviorWorker.TryExecuteOn))]
    internal static class RitualAttendanceValidateExecution
    {
        static bool Prefix(TargetInfo target,Precept_Ritual ritual,RitualRoleAssignments assignments)
        {
            if(assignments==null || !assignments.Participants.Any(RitualAttendance.NeedsCarrying))return true;
            string reason=RitualAttendance.Validate(target,ritual,assignments,out _);
            if(reason==null)return true;
            Messages.Message(reason,target,MessageTypeDefOf.RejectInput,false);return false;
        }
    }
    [HarmonyPatch(typeof(Lord),nameof(Lord.SetJob))]
    internal static class RitualAttendanceAttachBeforeDuties
    {
        static void Postfix(Lord __instance,bool loading)
        {
            // MakeNewLord assigns duties inside GotoToil, before returning. Those
            // duties immediately call CheckForJobOverride and may start work.
            // Register after SetJob has attached the Lord/map/graph, but before
            // its first GotoToil can allocate a food or spectator job.
            if(!loading && __instance.LordJob is LordJob_Ritual ritual)
                RitualAttendance.Ensure(ritual,(ritual as LordJob_AbundanceMeals)?.PlannedGuests);
        }
    }
    [HarmonyPatch(typeof(LordMaker),nameof(LordMaker.MakeNewLord))]
    internal static class RitualAttendanceAttach
    {
        static void Postfix(Lord __result)
        {
            if(__result?.LordJob is LordJob_Ritual ritual && RitualAttendance.CompleteStart(ritual))
                RitualAttendance.Ensure(ritual,(ritual as LordJob_AbundanceMeals)?.PlannedGuests);
        }
    }
    [HarmonyPatch(typeof(Lord),nameof(Lord.LordTick))]
    internal static class RitualAttendancePreparation
    {
        static bool Prefix(Lord __instance)
        {
            if(!(__instance.LordJob is LordJob_Ritual ritual))return true;
            var e=RitualAttendance.For(ritual);
            // Duty jobs continue on the pawns. Freezing the Lord tick also freezes
            // stage actions, progress, presence and tick-triggered transitions.
            // A saved return request may await cancellation on the next map
            // tick. Do not advance the still-live ritual during that interval.
            return e==null || e.AllSettled;
        }
    }
    [HarmonyPatch(typeof(LordJob_Ritual),nameof(LordJob_Ritual.ShouldRemovePawn))]
    internal static class RitualAttendanceExactPawnLoss
    {
        static void Postfix(LordJob_Ritual __instance,Pawn p,PawnLostCondition reason,ref bool __result)
        {
            if(reason!=PawnLostCondition.Vanished)return;
            var e=RitualAttendance.For(__instance);
            if(e!=null && (e.OwnedTransport(p) || e.PickupScope(p)))__result=false;
        }
    }
    [HarmonyPatch(typeof(LordJob_Ritual),nameof(LordJob_Ritual.Notify_PawnLost))]
    internal static class RitualAttendanceActualPawnLoss
    {
        static void Postfix(LordJob_Ritual __instance,Pawn p,PawnLostCondition condition)
        {
            var e=RitualAttendance.For(__instance);
            if(e?.GuestFor(p)==null)return;
            // Death, map departure, another Lord and explicit player actions are
            // real losses. Native handling has already run; never undo it.
            if(condition==PawnLostCondition.Killed || condition==PawnLostCondition.ExitedMap || condition==PawnLostCondition.ForcedToJoinOtherLord ||
                condition==PawnLostCondition.ChangedFaction || condition==PawnLostCondition.MadePrisoner || condition==PawnLostCondition.MadeSlave)
            { e.GuestFor(p).State=RitualAttendanceState.Interrupted;return; }
            e.BeginReturn("RR_AttendanceParticipantLeft".Translate(p.LabelShort));
        }
    }
    [HarmonyPatch(typeof(LordToil_Gathering),nameof(LordToil_Gathering.LordToilTick))]
    internal static class RitualAttendancePresence
    {
        static bool Prefix(LordToil_Gathering __instance)
        {
            if(!(__instance is LordToil_Ritual toil))return true;
            var e=RitualAttendance.For(toil.ritual);if(e==null)return true;
            foreach(Pawn p in toil.lord.ownedPawns)
            {
                if(p.Spawned && (e.GuestFor(p)==null || e.Settled(p)) && GatheringsUtility.InGatheringArea(p.Position,toil.ritual.Spot,toil.Map))
                {
                    if(!toil.Data.presentForTicks.ContainsKey(p))toil.Data.presentForTicks[p]=0;
                    toil.Data.presentForTicks[p]++;
                }
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(ThinkNode_Duty),nameof(ThinkNode_Duty.TryIssueJobPackage))]
    internal static class RitualAttendanceTransportPriority
    {
        static bool Prefix(ThinkNode_Duty __instance,Pawn pawn,ref ThinkResult __result)
        {
            Job job=RitualAttendance.TryGiveTransportJob(pawn);
            if(job==null)return true;
            __result=new ThinkResult(job,__instance);return false;
        }
    }
    [HarmonyPatch(typeof(ThinkNode_Subtree),nameof(ThinkNode_Subtree.TryIssueJobPackage))]
    internal static class RitualAttendanceStationaryDownedDuty
    {
        static bool Prefix(ThinkNode_Subtree __instance,ThinkTreeDef ___treeDef,Pawn pawn,ref ThinkResult __result)
        {
            if(___treeDef?.defName!="Downed")return true;
            var e=RitualAttendance.For(pawn);
            if(e?.Settled(pawn)!=true || RitualAttendance.SafetyReason(pawn)!=null || e.GuestFor(pawn).DestinationBed!=null)return true;
            Job job=pawn.CurJob?.def.defName==RitualAttendance.StayJob ? pawn.CurJob :
                JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(RitualAttendance.StayJob),e.GuestFor(pawn).Place);
            job.count=e.EventId;__result=new ThinkResult(job,__instance);return false;
        }
    }
    [HarmonyPatch(typeof(Pawn_JobTracker),nameof(Pawn_JobTracker.CheckForJobOverride))]
    internal static class RitualAttendanceKeepStationary
    {
        static bool Prefix(Pawn ___pawn)
        {
            if(___pawn.GetLord()?.LordJob is LordJob_Ritual starting && RitualAttendance.StartBlocked(starting))return false;
            var e=RitualAttendance.For(___pawn);
            if(e==null || e.ReturnRequested || RitualAttendance.SafetyReason(___pawn)!=null || !RitualAttendance.NeedsCarrying(___pawn))return true;
            var g=e.GuestFor(___pawn);
            return !(e.Settled(___pawn) || (g?.State==RitualAttendanceState.WaitingForPickup && ___pawn.CurrentBed()!=null));
        }
    }
    [HarmonyPatch(typeof(JobGiver_PatientGoToBed),"TryGiveJob")]
    internal static class RitualAttendancePendingReturnBedSeeking
    {
        static bool Prefix(Pawn pawn,ref Job __result)
        {
            // Only replace routine bed-seeking for this exact safe return guest.
            // Native FleeDanger precedes this node and remains untouched; the
            // predicate also yields to medical needs, direct orders and care jobs.
            Job waiting=RitualAttendance.For(pawn)?.ReturnWaitingJob(pawn);
            if(waiting==null)return true;
            __result=waiting;return false;
        }
    }
    [HarmonyPatch(typeof(WorkGiver_RescueDowned),nameof(WorkGiver_RescueDowned.HasJobOnThing))]
    internal static class RitualAttendanceRescueReservation
    {
        static bool Prefix(Thing t,bool forced,ref bool __result)
        {
            if(forced || !(t is Pawn pawn))return true;
            if(!RitualAttendanceAdapters.SafeSettledGuest(pawn))return true;
            __result=false;return false;
        }
    }
    [HarmonyPatch(typeof(Alert_ColonistNeedsRescuing),nameof(Alert_ColonistNeedsRescuing.NeedsRescue))]
    internal static class RitualAttendanceSettledRescueAlert
    {
        static void Postfix(Pawn p,ref bool __result)
        {
            // Keep the native decision for waiting, carried, returning and unsafe
            // pawns. Only the same live safe place used by unforced rescue work
            // removes an already settled active attendee from this alert.
            if(__result && RitualAttendanceAdapters.SafeSettledGuest(p))__result=false;
        }
    }
    [HarmonyPatch(typeof(RitualUtility),nameof(RitualUtility.GoodSpectateCellForRitual))]
    internal static class RitualAttendanceKeepAudiencePlacesClear
    {
        static void Postfix(IntVec3 spot,Pawn p,Map map,ref bool __result)
        {
            if(__result && RitualAttendance.IsReservedCell(map,spot) && RitualAttendance.For(p)?.GuestFor(p)?.Place!=spot)__result=false;
        }
    }
    [HarmonyPatch(typeof(LordJob_Ritual),nameof(LordJob_Ritual.Cleanup))]
    internal static class RitualAttendanceCleanup
    { static void Prefix(LordJob_Ritual __instance) { RitualAttendance.BeginReturn(__instance); } }
    [HarmonyPatch(typeof(LordJob_Ritual),nameof(LordJob_Ritual.PostCleanup))]
    internal static class RitualAttendancePostCleanup
    { static void Postfix(LordJob_Ritual __instance) { RitualAttendance.For(__instance)?.DetachLord(); } }

    [HarmonyPatch(typeof(Dialog_BeginRitual),"get_ExtraExplanationLabel")]
    internal static class RitualAttendanceDialogDetails
    {
        static void Postfix(TargetInfo ___target,Precept_Ritual ___ritual,RitualRoleAssignments ___assignments,ref TaggedString __result)
        {
            // Food rituals show the full guest plan in the scrollable details column.
            if (AbundanceRituals.IsFoodRitual(___ritual)) return;
            string text=RitualAttendanceAdapters.Preview(___target,___ritual,___assignments);
            if(!text.NullOrEmpty())__result=__result.ToString()+"\n\n"+text;
        }
    }
    [HarmonyPatch(typeof(PawnRitualRoleSelectionWidget),"ExtraTipContents")]
    internal static class RitualAttendancePawnTooltip
    {
        static void Postfix(Pawn pawn,TargetInfo ___target,Precept_Ritual ___ritual,RitualRoleAssignments ___ritualAssignments,ref string __result)
        {
            if(!RitualAttendance.NeedsCarrying(pawn))return;
            string text=RitualAttendanceAdapters.Preview(___target,___ritual,___ritualAssignments,pawn);
            if(!text.NullOrEmpty())__result=(__result.NullOrEmpty() ? "" : __result+"\n")+text;
        }
    }
    [HarmonyPatch(typeof(Dialog_BeginRitual),"BlockingIssues")]
    internal static class RitualAttendanceDialogBlockers
    {
        static void Postfix(TargetInfo ___target,Precept_Ritual ___ritual,RitualRoleAssignments ___assignments,ref IEnumerable<string> __result)
        {
            if(___assignments==null || !___assignments.Participants.Any(RitualAttendance.NeedsCarrying))return;
            string reason=RitualAttendance.Validate(___target,___ritual,___assignments,out _);
            if(reason!=null)__result=__result.Concat(new[]{reason});
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.GetInspectString))]
    internal static class RitualAttendanceReturnInspection
    {
        static void Postfix(Pawn __instance,ref string __result)
        {
            var e=RitualAttendance.For(__instance);var g=e?.GuestFor(__instance);if(g==null)return;
            string line=e.ReturnRequested ? "RR_AttendancePendingInspection".Translate(g.Reason ?? e.Reason ?? "RR_AttendanceWaitingHelper".Translate()).ToString() :
                "RR_AttendanceInspection".Translate(g.State.ToString()).ToString();
            __result=(__result.NullOrEmpty()?"":__result+"\n")+line;
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.GetGizmos))]
    internal static class RitualAttendanceRetryReturn
    {
        static void Postfix(Pawn __instance,ref IEnumerable<Gizmo> __result)
        {
            var e=RitualAttendance.For(__instance);
            if(e?.ReturnRequested!=true || !__instance.IsColonistPlayerControlled)return;
            __result=__result.Concat(new Gizmo[]{new Command_Action { defaultLabel="RR_AttendanceRetryReturn".Translate(),
                defaultDesc="RR_AttendanceRetryReturnDesc".Translate(),action=e.RetryReturns }});
        }
    }
}
