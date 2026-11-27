using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimRound.FeedOther
{
    public enum MobilityAidState { Unassigned, AwaitingFit, HeldByOther, Incompatible, Fitted, Stowed, Incapacitated }
    public enum MobilityAidRequest { None, Fit, Remove, Replace }
    public sealed class MobilityAidAssignment : IExposable
    {
        public Pawn pawn;
        public Apparel aid;
        // Retain D01 save fields. Generations keep cancel/requeue from retargeting
        // an already running helper job.
        public bool pendingFit;
        public Apparel replacementAid;
        public MobilityAidRequest request;
        public int revision, failedAttempts, nextAttemptTick;
        public bool automaticCare, suppressRefitting, allowCustodialAssistance;
        public string blockedReason;
        public Apparel RequestedAid => request == MobilityAidRequest.Replace ? replacementAid : aid;
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn"); Scribe_References.Look(ref aid, "aid");
            Scribe_Values.Look(ref pendingFit, "pendingFit", false);
            Scribe_References.Look(ref replacementAid, "replacementAid");
            Scribe_Values.Look(ref request, "request", MobilityAidRequest.None);
            Scribe_Values.Look(ref revision, "revision", 0);
            Scribe_Values.Look(ref failedAttempts, "failedAttempts", 0);
            Scribe_Values.Look(ref nextAttemptTick, "nextAttemptTick", 0);
            Scribe_Values.Look(ref automaticCare, "automaticCare", false);
            Scribe_Values.Look(ref suppressRefitting, "suppressRefitting", false);
            Scribe_Values.Look(ref allowCustodialAssistance, "allowCustodialAssistance", false);
            Scribe_Values.Look(ref blockedReason, "blockedReason", null);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && request == MobilityAidRequest.None && pendingFit) request = MobilityAidRequest.Fit;
        }
    }
    // References and intent only. Physical ownership stays in native containers.
    public sealed class MobilityAidController : GameComponent
    {
        public const int MaximumAutomaticAttempts = 3;
        private List<MobilityAidAssignment> assignments = new List<MobilityAidAssignment>();
        private readonly HashSet<Pawn> transfers = new HashSet<Pawn>();
        // Native apparel toils continue using their Job after notifying removal.
        // A synchronous downing notification can end and pool that same Job.
        // Weak references coalesce the final physical state without retaining pawns.
        private sealed class PendingHealthChange
        {
            internal System.WeakReference<Pawn> pawn;
            internal int dueTick;
        }
        private readonly Dictionary<int, PendingHealthChange> pendingHealthChanges = new Dictionary<int, PendingHealthChange>();
        private readonly List<int> pendingHealthKeys = new List<int>();
        // Exists only while writing a save or resolving its reference list.
        // Runtime pending work retains weak references exclusively.
        private List<Pawn> pendingHealthSaveReferences;
        private int scanIndex;
        public MobilityAidController(Game game) { }
        public static MobilityAidController CurrentController => Current.Game?.GetComponent<MobilityAidController>();
        public IReadOnlyList<MobilityAidAssignment> Assignments => assignments;
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) Prune();
            Scribe_Collections.Look(ref assignments, "mobilityAidAssignments", LookMode.Deep);
            ExposePendingPhysicalChanges();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Prune();
        }
        private void ExposePendingPhysicalChanges()
        {
            LoadSaveMode mode = Scribe.mode;
            if (mode == LoadSaveMode.Saving)
            {
                pendingHealthSaveReferences = new List<Pawn>();
                foreach (PendingHealthChange pending in pendingHealthChanges.Values)
                {
                    Pawn p;
                    if (pending.pawn.TryGetTarget(out p) && p?.health != null && !p.Dead && !p.Destroyed && !p.Discarded)
                        pendingHealthSaveReferences.Add(p);
                }
            }
            try
            {
                // Native Scribe registers load IDs in LoadingVars and returns
                // the resolved objects only in ResolvingCrossRefs.
                Scribe_Collections.Look(ref pendingHealthSaveReferences, "pendingMobilityPhysicalChanges", LookMode.Reference);
                if (mode == LoadSaveMode.ResolvingCrossRefs && pendingHealthSaveReferences != null)
                    foreach (Pawn p in pendingHealthSaveReferences)
                        if (p?.health != null && !p.Dead && !p.Destroyed && !p.Discarded) DeferPhysicalChange(p);
            }
            finally
            {
                if (mode == LoadSaveMode.Saving || mode == LoadSaveMode.ResolvingCrossRefs)
                { pendingHealthSaveReferences?.Clear(); pendingHealthSaveReferences = null; }
            }
        }
        public override void LoadedGame()
        {
            base.LoadedGame();
            // Native loading invalidates HediffSet caches but restores healthState
            // verbatim. Reevaluate assigned pawns after all saved holders load,
            // including a save made between a physical change and its next tick.
            foreach (MobilityAidAssignment a in assignments)
                if (a.pawn?.health != null && !a.pawn.Dead && !a.pawn.Destroyed && !a.pawn.Discarded)
                { a.pawn.health.hediffSet.DirtyCache(); DeferPhysicalChange(a.pawn); }
        }
        public override void GameComponentTick()
        {
            FlushPhysicalChanges();
            if (Find.TickManager.TicksGame % 250 != 0) return;
            Prune();
            // Bound readiness scans. Only native Doctor work issues automatic jobs.
            for (int count = 0; count < Math.Min(8, assignments.Count); count++)
            {
                if (scanIndex >= assignments.Count) scanIndex = 0;
                MobilityAidAssignment a = assignments[scanIndex++]; string permission;
                if (!CanReceiveAssistance(a, out permission))
                { if (a.request != MobilityAidRequest.None) CancelRequest(a.pawn); a.blockedReason = permission; continue; }
                if (!a.automaticCare || !a.pawn.Spawned) continue;
                if (a.request == MobilityAidRequest.None && !a.suppressRefitting && a.aid.Wearer != a.pawn)
                { string reason; if (!RequestFit(a.pawn, out reason)) { a.blockedReason = reason; continue; } }
                if (!CanTryAutomatically(a) || MobilityAidJobs.HasActiveJob(a.pawn)) continue;
                string blocked = "No available doctor can provide mobility assistance."; bool ready = false;
                foreach (Pawn helper in a.pawn.Map.mapPawns.FreeColonistsSpawned.OrderBy(p => p.thingIDNumber))
                {
                    if (helper.workSettings?.WorkIsActive(WorkTypeDefOf.Doctor) != true) continue;
                    JobPlan plan;
                    if (MobilityAidJobs.TryPlan(helper, a.pawn, false, out plan, out blocked)) { ready = true; break; }
                }
                if (ready) a.blockedReason = null; else RecordFailure(a.pawn, a.revision, blocked);
            }
        }
        public void Prune()
        {
            if (assignments == null) assignments = new List<MobilityAidAssignment>();
            assignments.RemoveAll(a => a == null || !CanOwnAssignment(a.pawn) || a.aid == null || a.aid.Destroyed || !HoverchairFixUtility.IsHoverchair(a.aid.def));
            var pawns = new HashSet<Pawn>(); var aids = new HashSet<Apparel>();
            assignments.RemoveAll(a => !pawns.Add(a.pawn) || !aids.Add(a.aid));
            foreach (MobilityAidAssignment a in assignments)
                if (a.request == MobilityAidRequest.Replace && (a.replacementAid == null || a.replacementAid.Destroyed || !aids.Add(a.replacementAid)))
                { CancelRequest(a.pawn); a.blockedReason = "The requested replacement is no longer available."; }
        }
        public static bool CanOwnAssignment(Pawn p) => p != null && !p.Dead && !p.Destroyed && !p.Discarded && p.RaceProps.Humanlike && !p.IsQuestLodger() &&
            ((p.Faction == Faction.OfPlayer && p.IsColonist && !p.IsPrisoner && !p.IsSlave) ||
             p.IsPrisonerOfColony || p.IsSlaveOfColony);
        public static bool CanReceiveAssistance(MobilityAidAssignment a, out string reason)
        {
            reason = null;
            if (a == null || !CanOwnAssignment(a.pawn)) reason = "Recipient is outside colony mobility care.";
            else if ((a.pawn.IsPrisoner || a.pawn.IsSlave) && !a.allowCustodialAssistance) reason = "Assisted mobility in custody is disabled for this pawn.";
            return reason == null;
        }
        public MobilityAidAssignment ForPawn(Pawn p) => assignments.FirstOrDefault(a => a.pawn == p);
        public MobilityAidAssignment ForAid(Apparel aid) => aid == null ? null : assignments.FirstOrDefault(a => a.aid == aid || a.replacementAid == aid);
        public bool CanTryAutomatically(MobilityAidAssignment a) => a.automaticCare && a.request != MobilityAidRequest.None && a.failedAttempts < MaximumAutomaticAttempts && Find.TickManager.TicksGame >= a.nextAttemptTick;
        public void RecordFailure(Pawn p, int revision, string reason)
        {
            MobilityAidAssignment a = ForPawn(p);
            if (a == null || a.revision != revision || a.request == MobilityAidRequest.None) return;
            a.blockedReason = reason ?? "Mobility assistance was interrupted."; a.failedAttempts++;
            a.nextAttemptTick = Find.TickManager.TicksGame + 600 * Math.Min(a.failedAttempts, MaximumAutomaticAttempts);
        }
        public string Status(Pawn p)
        {
            MobilityAidAssignment a = ForPawn(p); if (a == null) return "No assigned mobility aid.";
            string s = a.request == MobilityAidRequest.None ? "No assistance requested." : a.request + " requested.";
            if (!string.IsNullOrEmpty(a.blockedReason)) s += " " + a.blockedReason;
            if (a.failedAttempts >= MaximumAutomaticAttempts) s += " Automatic retries paused. Request assistance again to retry.";
            else if (a.request != MobilityAidRequest.None && a.nextAttemptTick > Find.TickManager.TicksGame) s += " Waiting before the next automatic attempt.";
            return s;
        }
        public static bool CanPotentiallyControl(Pawn p, HediffSet diffSet)
        {
            return p != null && diffSet != null && ReferenceEquals(diffSet.pawn, p) &&
                RimRound.Utilities.MedicalCapacityPolicy.CanOperatePropelledAid(p);
        }
        public static bool PhysicallyCompatible(Pawn p, Apparel aid, out string reason) => PhysicallyCompatible(p, aid, null, out reason);
        public static bool PhysicallyCompatible(Pawn p, Apparel aid, Apparel replaced, out string reason)
        {
            reason = null;
            if (p == null || p.Dead || p.Destroyed || p.apparel == null || !p.RaceProps.Humanlike || aid == null || aid.Destroyed || !HoverchairFixUtility.IsHoverchair(aid.def))
                reason = "A living humanlike pawn and a real mobility aid are required.";
            else if (!ApparelUtility.HasPartsToWear(p, aid.def) || !aid.PawnCanWear(p, true)) reason = "This pawn cannot physically wear this mobility aid.";
            else if (aid.Wearer != p && p.apparel.WornApparel.Any(w => w != replaced && !ApparelUtility.CanWearTogether(aid.def, w.def, p.RaceProps.body)))
                reason = "The pawn already wears incompatible equipment or another mobility aid.";
            return reason == null;
        }
        public bool Assign(Pawn p, Apparel aid, out string reason)
        {
            Prune(); if (!PhysicallyCompatible(p, aid, out reason)) return false;
            if (!CanOwnAssignment(p)) { reason = "Only colony-managed pawns can be assigned an aid."; return false; }
            MobilityAidAssignment other = ForAid(aid), own = ForPawn(p);
            if (other != null && other.pawn != p) { reason = "Unassign this aid from its current owner first."; return false; }
            if (own != null && own.aid != aid) { reason = "Unassign the pawn's current aid first."; return false; }
            if (own == null) assignments.Add(new MobilityAidAssignment { pawn = p, aid = aid }); return true;
        }
        public void Unassign(Pawn p) => assignments.RemoveAll(a => a.pawn == p);
        public void CancelFit(Pawn p) => CancelRequest(p);
        public void CancelRequest(Pawn p)
        {
            MobilityAidAssignment a = ForPawn(p); if (a == null) return;
            a.revision++; a.request = MobilityAidRequest.None; a.pendingFit = false; a.replacementAid = null; a.suppressRefitting = true; a.blockedReason = null;
        }
        private void Queue(MobilityAidAssignment a, MobilityAidRequest request, Apparel replacement = null)
        {
            a.revision++; a.request = request; a.pendingFit = request != MobilityAidRequest.Remove; a.replacementAid = replacement;
            a.failedAttempts = 0; a.nextAttemptTick = 0; a.blockedReason = null; a.suppressRefitting = false;
        }
        public bool RequestFit(Pawn p, out string reason)
        {
            Prune(); MobilityAidAssignment a = ForPawn(p); if (!CanReceiveAssistance(a, out reason)) return false;
            if (!PhysicallyCompatible(p, a.aid, out reason)) return false;
            if (a.aid.Wearer == p) { reason = "The assigned aid is already fitted."; return false; }
            Queue(a, MobilityAidRequest.Fit); return true;
        }
        public bool RequestRemoval(Pawn p, out string reason)
        {
            Prune(); MobilityAidAssignment a = ForPawn(p); if (!CanReceiveAssistance(a, out reason)) return false;
            if (a.aid.Wearer != p) { reason = "The assigned aid is not fitted."; return false; }
            Queue(a, MobilityAidRequest.Remove); return true;
        }
        public bool RequestReplacement(Pawn p, Apparel replacement, out string reason)
        {
            Prune(); MobilityAidAssignment a = ForPawn(p); if (!CanReceiveAssistance(a, out reason)) return false;
            if (a.aid.Wearer != p) { reason = "Fit the currently assigned aid before requesting replacement."; return false; }
            if (replacement == a.aid || (replacement != null && ForAid(replacement) != null)) { reason = "Choose a different unassigned aid."; return false; }
            if (!PhysicallyCompatible(p, replacement, a.aid, out reason)) return false;
            Queue(a, MobilityAidRequest.Replace, replacement); return true;
        }
        public void SetAutomaticCare(Pawn p, bool enabled)
        {
            MobilityAidAssignment a = ForPawn(p); if (a == null) return;
            bool was = a.automaticCare; a.automaticCare = enabled;
            if (enabled && !was)
            {
                a.suppressRefitting = false; a.failedAttempts = 0; a.nextAttemptTick = 0;
                if (a.request == MobilityAidRequest.None && a.aid.Wearer != p) { string reason; RequestFit(p, out reason); }
            }
        }
        public void SetCustodialAssistance(Pawn p, bool enabled)
        { MobilityAidAssignment a = ForPawn(p); if (a == null) return; a.allowCustodialAssistance = enabled; if (!enabled) CancelRequest(p); }
        public void CompleteRequest(Pawn p, int revision, MobilityAidRequest request, Apparel fitted)
        {
            MobilityAidAssignment a = ForPawn(p); if (a == null || a.revision != revision) return;
            if (request == MobilityAidRequest.Replace) a.aid = fitted;
            a.request = MobilityAidRequest.None; a.pendingFit = false; a.replacementAid = null; a.blockedReason = null;
            a.failedAttempts = 0; a.nextAttemptTick = 0; a.suppressRefitting = request == MobilityAidRequest.Remove;
        }
        internal void BeginTransfer(Pawn p) => transfers.Add(p);
        internal void EndTransfer(Pawn p) => transfers.Remove(p);
        private void DeferPhysicalChange(Pawn p)
        {
            int tick = Find.TickManager.TicksGame + 1;
            PendingHealthChange pending;
            if (pendingHealthChanges.TryGetValue(p.thingIDNumber, out pending))
            { pending.pawn.SetTarget(p); pending.dueTick = tick; }
            else pendingHealthChanges.Add(p.thingIDNumber, new PendingHealthChange { pawn = new System.WeakReference<Pawn>(p), dueTick = tick });
        }
        private void FlushPhysicalChanges()
        {
            if (pendingHealthChanges.Count == 0) return;
            int tick = Find.TickManager.TicksGame;
            pendingHealthKeys.Clear();
            foreach (var entry in pendingHealthChanges) if (entry.Value.dueTick <= tick) pendingHealthKeys.Add(entry.Key);
            foreach (int key in pendingHealthKeys)
            {
                PendingHealthChange pending = pendingHealthChanges[key];
                Pawn p;
                if (pending.pawn.TryGetTarget(out p) && p != null && !p.Dead && !p.Destroyed && !p.Discarded && transfers.Contains(p))
                { pending.dueTick = tick + 1; continue; }
                // Remove first: callbacks may queue a new physical change.
                pendingHealthChanges.Remove(key);
                if (p != null && !p.Dead && !p.Destroyed && !p.Discarded) p.health?.Notify_HediffChanged(null);
            }
            pendingHealthKeys.Clear();
        }
        public MobilityAidState State(Pawn p)
        {
            Apparel worn = HoverchairFixUtility.WornChair(p), aid = worn ?? ForPawn(p)?.aid;
            if (aid == null) return MobilityAidState.Unassigned; string reason;
            if (!PhysicallyCompatible(p, aid, out reason)) return MobilityAidState.Incompatible;
            if (aid.Wearer != p) return aid.Wearer == null ? MobilityAidState.AwaitingFit : MobilityAidState.HeldByOther;
            if (!CanPotentiallyControl(p, p.health.hediffSet) || p.Downed || p.health.InPainShock) return MobilityAidState.Incapacitated;
            if (!p.Awake() || p.GetPosture().Laying()) return MobilityAidState.Stowed; return MobilityAidState.Fitted;
        }
        public static void PhysicalChanged(Pawn p)
        {
            MobilityAidController c = CurrentController;
            if (c != null && !c.transfers.Contains(p)) c.CancelRequest(p);
            if (p?.health == null || p.Dead || p.Destroyed || p.Discarded) return;
            // This is the cache invalidation half of native Notify_HediffChanged.
            // Moving reflects the current real apparel immediately. Native health
            // changes state after the apparel callback and its caller have returned.
            p.health.hediffSet.DirtyCache();
            c?.DeferPhysicalChange(p);
        }
        public static IEnumerable<Gizmo> Gizmos(Apparel aid)
        {
            MobilityAidController c = CurrentController; if (c == null || aid == null || aid.Destroyed) yield break;
            c.Prune(); MobilityAidAssignment a = c.ForAid(aid);
            if (a == null)
            {
                yield return new Command_Action { defaultLabel = "Assign mobility aid", defaultDesc = "Assign this aid to the pawn. A helper must still fit it.",
                    action = () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetPawns = true, canTargetBuildings = false, canTargetLocations = false },
                        target => { string reason; if (!c.Assign(target.Pawn, aid, out reason)) Messages.Message(reason, MessageTypeDefOf.RejectInput, false); }) }; yield break;
            }
            yield return new Command_Action { defaultLabel = "Unassign aid: " + a.pawn.LabelShort, defaultDesc = "Cancel the request and unassign the aid. Leave the item where it is.", action = () => c.Unassign(a.pawn) };
            yield return new Command_Toggle { defaultLabel = "Automatic mobility assistance", defaultDesc = "Allow low-priority Doctor work for this assigned pawn. Three failed attempts pause retries. " + c.Status(a.pawn),
                isActive = () => a.automaticCare, toggleAction = () => c.SetAutomaticCare(a.pawn, !a.automaticCare) };
            if (a.pawn.IsPrisoner || a.pawn.IsSlave)
                yield return new Command_Toggle { defaultLabel = "Allow assisted mobility in custody", defaultDesc = "Allow this pawn to receive assistance. A fitted aid can restore movement and enable escape. Automatic assistance is a separate toggle.",
                    isActive = () => a.allowCustodialAssistance, toggleAction = () => c.SetCustodialAssistance(a.pawn, !a.allowCustodialAssistance) };
            if (a.request != MobilityAidRequest.None)
            {
                yield return MobilityAidJobs.HelperCommand(a);
                yield return new Command_Action { defaultLabel = "Cancel mobility assistance", defaultDesc = c.Status(a.pawn), action = () => c.CancelRequest(a.pawn) };
                if (a.failedAttempts >= MaximumAutomaticAttempts)
                    yield return new Command_Action { defaultLabel = "Retry mobility assistance", defaultDesc = "Allow another set of attempts for this assistance request.", action = () => { a.revision++; a.failedAttempts = 0; a.nextAttemptTick = 0; a.blockedReason = null; } };
                yield break;
            }
            if (a.aid.Wearer == a.pawn)
            {
                yield return new Command_Action { defaultLabel = "Request aid removal", defaultDesc = "Have a helper remove the aid at the bedside. It will not be refitted until you request it.",
                    action = () => { string reason; if (!c.RequestRemoval(a.pawn, out reason)) Messages.Message(reason, MessageTypeDefOf.RejectInput, false); } };
                yield return new Command_Action { defaultLabel = "Request aid replacement", defaultDesc = "Choose a replacement aid. Keep wearing the current one until a helper swaps it at the bedside.",
                    action = () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetItems = true, canTargetPawns = false, canTargetBuildings = false, canTargetLocations = false },
                        target => { string reason; if (!c.RequestReplacement(a.pawn, target.Thing as Apparel, out reason)) Messages.Message(reason, MessageTypeDefOf.RejectInput, false); }) };
            }
            else yield return new Command_Action { defaultLabel = "Request aid fitting", defaultDesc = "Use the assigned real item from storage or recipient/helper inventory. Choose a helper or enable automatic assistance. " + c.Status(a.pawn),
                action = () => { string reason; if (!c.RequestFit(a.pawn, out reason)) Messages.Message(reason, MessageTypeDefOf.RejectInput, false); } };
        }
    }
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelAdded))]
    internal static class MobilityAidAdded
    {
        private static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        { if (HoverchairFixUtility.IsHoverchair(apparel?.def)) MobilityAidController.PhysicalChanged(__instance.pawn); }
    }
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelRemoved))]
    internal static class MobilityAidRemoved
    {
        private static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        { if (HoverchairFixUtility.IsHoverchair(apparel?.def)) MobilityAidController.PhysicalChanged(__instance.pawn); }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class MobilityAidRecipientGizmos
    {
        private static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            MobilityAidAssignment a = MobilityAidController.CurrentController?.ForPawn(__instance);
            // Worn comp gizmos already cover a fitted aid. Also expose controls
            // on the recipient while the assigned item is stored or in inventory.
            if (a != null && a.aid != null && !a.aid.Destroyed && a.aid.Wearer != __instance)
                __result = __result.Concat(MobilityAidController.Gizmos(a.aid));
        }
    }
}
