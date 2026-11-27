using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    [HarmonyPatch(typeof(LordJob_Ritual), nameof(LordJob_Ritual.PostCleanup))]
    internal static class BedsideRitualVisualCleanup
    {
        private static void Postfix(LordJob_Ritual __instance)
        {
            if (!(__instance is IBedsideRitualLord)) return;
            // Preserve the native visual fade without serializing its retired Lord.
            var drawers = __instance.lord?.lordManager?.stencilDrawers;
            if (drawers == null) return;
            foreach (var drawer in drawers)
                if (drawer.sourceLord == __instance.lord) drawer.sourceLord = null;
        }
    }

    internal static class BedsideRitualExecution
    {
        internal static bool Prepare(TargetInfo target, Precept_Ritual ritual, RitualRoleAssignments assignments, out BedsideRitualState state)
        {
            state = null;
            Pawn actor = assignments.FirstAssignedPawn(BedsideRituals.ActorRoleId(ritual));
            if (!AbundanceBedEntry.CanBegin(actor, ritual, target)) return Reject("RR_BedsideRoleNotReady");
            if (target.Thing is Building_Bed && (!AbundanceBedEntry.IsRoleBed(actor, ritual, target) || !BedsideRituals.Ready(actor)))
                return Reject("RR_BedsideRitualSite");
            if (!BedsideRituals.BedCandidate(actor)) return true;
            if (!BedsideRituals.Ready(actor) || !BedsideRituals.LocalSite(actor, target) ||
                !BedsideRoleAbilities.BedReservationsPermitSession(actor.CurrentBed()) ||
                BedsideRoleAbilities.HasSession(actor) || AI.BedsideBodyDiscussion.HasSession(actor)) return Reject("RR_BedsideRoleNotReady");
            Pawn patient = assignments.FirstAssignedPawn(ritual.def.defName == "Conversion" ? "convertee" : "convict");
            if (!AbundanceBedEntry.CanBegin(actor, ritual, target, patient)) return Reject("RR_BedsideRoleNotReady");
            Pawn escort = assignments.FirstAssignedPawn(BedsideRituals.EscortId);
            if (patient?.IsPrisonerOfColony == true && (!BedsideRituals.EscortReady(escort) ||
                !escort.CanReserveAndReach(patient, Verse.AI.PathEndMode.Touch, Danger.Some) ||
                !escort.CanReach(actor, Verse.AI.PathEndMode.Touch, Danger.Some))) return Reject("RR_BedsideRitualEscort");
            if (ritual.def.defName == "TrialMentalState" && !BedsideRituals.Local(actor, patient)) return Reject("RR_BedsideRitualLocalTarget");
            state = new BedsideRitualState(target, ritual, assignments);
            if (state.Reserve()) return true;
            state.Release(actor.Map); state = null;
            return Reject("RR_BedsideRoleBlocked");
        }
        private static bool Reject(string key) { Messages.Message(key.Translate(), MessageTypeDefOf.RejectInput, false); return false; }
        internal static void FinishAttempt(BedsideRitualState state)
        {
            if (state != null && !(state.Actor.GetLord()?.LordJob is IBedsideRitualLord lord && lord.Bedside == state)) state.Release(state.Actor.Map);
        }
        internal static string CanStart(TargetInfo target, Precept_Ritual ritual, Dictionary<string, Pawn> forced)
        {
            Pawn actor = BedsideRituals.Actor(ritual);
            if (!AbundanceBedEntry.CanBegin(actor, ritual, target)) return "RR_BedsideRoleNotReady".Translate();
            if (target.Thing is Building_Bed && (!AbundanceBedEntry.IsRoleBed(actor, ritual, target) || !BedsideRituals.Ready(actor)))
                return "RR_BedsideRitualSite".Translate();
            if (!BedsideRituals.BedCandidate(actor)) return null;
            if (!BedsideRituals.Ready(actor) || !BedsideRituals.LocalSite(actor, target)) return "RR_BedsideRitualSite".Translate();
            if (actor.GetLord() == null && (BedsideRoleAbilities.HasSession(actor) || AI.BedsideBodyDiscussion.HasSession(actor) ||
                !BedsideRoleAbilities.BedReservationsPermitSession(actor.CurrentBed()))) return "RR_BedsideRoleBlocked".Translate();
            Pawn targetPawn = null;
            forced?.TryGetValue(ritual.def.defName == "Conversion" ? "convertee" : "convict", out targetPawn);
            if (targetPawn?.IsPrisonerOfColony == true && BedsideRituals.FindEscort(actor, targetPawn) == null &&
                (forced == null || !forced.TryGetValue(BedsideRituals.EscortId, out var escort) || !BedsideRituals.EscortReady(escort)))
                return "RR_BedsideRitualEscort".Translate();
            if (ritual.def.defName == "TrialMentalState" && targetPawn != null && !BedsideRituals.Local(actor, targetPawn))
                return "RR_BedsideRitualLocalTarget".Translate();
            return null;
        }
        internal static void ReturnPrisoner(LordJob_Ritual ritual, string role)
        {
            Pawn prisoner = ritual.PawnWithRole(role);
            if (prisoner?.IsPrisonerOfColony != true) return;
            var state = ((IBedsideRitualLord)ritual).Bedside;
            Pawn escort = BedsideRituals.EscortReady(state.Escort) ? state.Escort : BedsideRituals.FindEscort(state.Actor, prisoner);
            if (escort != null) WorkGiver_Warden_TakeToBed.TryTakePrisonerToBed(prisoner, escort);
            prisoner.guest.WaitInsteadOfEscapingFor(1250);
        }
    }

    public sealed class RitualBehaviorWorker_BedsideSpeech : RitualBehaviorWorker_Speech
    {
        private BedsideRitualState pending;
        public RitualBehaviorWorker_BedsideSpeech() { }
        public RitualBehaviorWorker_BedsideSpeech(RitualBehaviorDef def) : base(def) { }
        public override string CanStartRitualNow(TargetInfo target, Precept_Ritual ritual, Pawn selectedPawn = null, Dictionary<string, Pawn> forcedForRole = null) =>
            BedsideRitualExecution.CanStart(target, ritual, forcedForRole) ?? base.CanStartRitualNow(target, ritual, selectedPawn, forcedForRole);
        public override void TryExecuteOn(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments, bool playerForced = false)
        {
            if (!BedsideRitualExecution.Prepare(target, ritual, assignments, out pending)) return;
            try { base.TryExecuteOn(target, organizer, ritual, obligation, assignments, playerForced); }
            finally { BedsideRitualExecution.FinishAttempt(pending); pending = null; }
        }
        protected override LordJob CreateLordJob(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments) =>
            pending == null ? base.CreateLordJob(target, organizer, ritual, obligation, assignments) : new LordJob_BedsideSpeech(target, ritual, assignments, pending);
    }
    public sealed class RitualBehaviorWorker_BedsideConversion : RitualBehaviorWorker_Conversion
    {
        private BedsideRitualState pending;
        public RitualBehaviorWorker_BedsideConversion() { }
        public RitualBehaviorWorker_BedsideConversion(RitualBehaviorDef def) : base(def) { }
        public override string CanStartRitualNow(TargetInfo target, Precept_Ritual ritual, Pawn selectedPawn = null, Dictionary<string, Pawn> forcedForRole = null) =>
            BedsideRitualExecution.CanStart(target, ritual, forcedForRole) ?? base.CanStartRitualNow(target, ritual, selectedPawn, forcedForRole);
        public override void TryExecuteOn(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments, bool playerForced = false)
        {
            if (!BedsideRitualExecution.Prepare(target, ritual, assignments, out pending)) return;
            try { base.TryExecuteOn(target, organizer, ritual, obligation, assignments, playerForced); }
            finally { BedsideRitualExecution.FinishAttempt(pending); pending = null; }
        }
        protected override LordJob CreateLordJob(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments) =>
            pending == null ? base.CreateLordJob(target, organizer, ritual, obligation, assignments) : new LordJob_BedsideRitual(target, ritual, obligation, assignments, pending);
        public override void PostCleanup(LordJob_Ritual ritual)
        { if (ritual is IBedsideRitualLord) BedsideRitualExecution.ReturnPrisoner(ritual, "convertee"); else base.PostCleanup(ritual); }
    }
    public sealed class RitualBehaviorWorker_BedsideTrial : RitualBehaviorWorker_Trial
    {
        private BedsideRitualState pending;
        public RitualBehaviorWorker_BedsideTrial() { }
        public RitualBehaviorWorker_BedsideTrial(RitualBehaviorDef def) : base(def) { }
        public override string CanStartRitualNow(TargetInfo target, Precept_Ritual ritual, Pawn selectedPawn = null, Dictionary<string, Pawn> forcedForRole = null) =>
            BedsideRitualExecution.CanStart(target, ritual, forcedForRole) ?? base.CanStartRitualNow(target, ritual, selectedPawn, forcedForRole);
        public override void TryExecuteOn(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments, bool playerForced = false)
        {
            if (!BedsideRitualExecution.Prepare(target, ritual, assignments, out pending)) return;
            try { base.TryExecuteOn(target, organizer, ritual, obligation, assignments, playerForced); }
            finally { BedsideRitualExecution.FinishAttempt(pending); pending = null; }
        }
        protected override LordJob CreateLordJob(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments) =>
            pending == null ? base.CreateLordJob(target, organizer, ritual, obligation, assignments) : new LordJob_BedsideRitual(target, ritual, obligation, assignments, pending);
        public override void PostCleanup(LordJob_Ritual ritual)
        { if (ritual is IBedsideRitualLord) BedsideRitualExecution.ReturnPrisoner(ritual, "convict"); else base.PostCleanup(ritual); }
    }
}
