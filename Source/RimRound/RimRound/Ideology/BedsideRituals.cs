using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimRound.AI;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    public static class BedsideRituals
    {
        internal const string EscortId = "RR_escort";
        internal static bool OrganizerPredicatePatched;
        internal static bool Supported(Precept_Ritual r) => r != null && (r.def.defName == "LeaderSpeech" ||
            r.def.defName == "Conversion" || r.def.defName == "Trial" || r.def.defName == "TrialPrisoner" || r.def.defName == "TrialMentalState");
        internal static string ActorRoleId(Precept_Ritual r) => r.def.defName == "LeaderSpeech" ? "speaker" : r.def.defName == "Conversion" ? "moralist" : "leader";
        internal static Pawn Actor(Precept_Ritual r) => r?.ideo?.RolesListForReading.FirstOrDefault(p =>
            r.def.defName == "Conversion" ? p.def.defName == "IdeoRole_Moralist" : p.def.leaderRole)?.ChosenPawnSingle();
        internal static bool BedCandidate(Pawn pawn) => pawn?.CurrentBed() != null &&
            (BedsideRoleAbilities.Immobile(pawn) || AbundanceBeds.For(pawn)==pawn.CurrentBed());
        internal static bool Ready(Pawn p) => OrganizerPredicatePatched && BedsideRoleAbilities.IsReadyBedOccupant(p) &&
            (BedsideRoleAbilities.Immobile(p) || AbundanceBeds.For(p)==p.CurrentBed()) && !p.WorkTagIsDisabled(WorkTags.Social) &&
            p.skills?.GetSkill(SkillDefOf.Social).TotallyDisabled == false;
        internal static bool Local(Pawn actor, Pawn other) => actor != null && other != null && other.Spawned &&
            actor.Map == other.Map && actor.Position.InHorDistOf(other.Position, 6f) &&
            actor.GetRoom() == other.GetRoom() && GenSight.LineOfSight(actor.Position, other.Position, actor.Map, true);
        internal static bool LocalSite(Pawn actor, TargetInfo target) => actor?.Spawned == true && target.IsValid &&
            (!target.HasThing || (target.Thing.Spawned && !target.Thing.Destroyed)) &&
            target.Map == actor.Map && actor.Position.InHorDistOf(target.CenterCell, 4f) &&
            actor.CurrentBed()?.GetRoom() == target.Cell.GetRoom(target.Map) &&
            GenSight.LineOfSight(actor.Position, target.CenterCell, actor.Map, true);
        // Blob beds are not edifices. Use the actual thing grid when choosing
        // a visitor's cell so a bed's empty foot cell is still protected.
        internal static bool BedAt(IntVec3 cell, Map map) => map != null && cell.InBounds(map) &&
            cell.GetThingList(map).Any(t => t is Building_Bed);
        internal static bool EscortReady(Pawn p) => BedsideRoleAbilities.OrdinaryColonist(p) &&
            !p.Downed && !p.Drafted && !p.InMentalState && p.Awake() && !p.IsBurning() &&
            !p.WorkTagIsDisabled(WorkTags.Social) && !p.WorkTypeIsDisabled(WorkTypeDefOf.Warden) &&
            p.health.capacities.CapableOf(PawnCapacityDefOf.Moving) && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
        internal static Pawn FindEscort(Pawn actor, Pawn prisoner) => actor.Map.mapPawns.FreeColonistsSpawned
            .Where(p => p != actor && EscortReady(p) && p.GetLord() == null && p.CurJob?.playerForced != true &&
                p.CanReserveAndReach(prisoner, PathEndMode.Touch, Danger.Some) && p.CanReach(actor, PathEndMode.Touch, Danger.Some))
            .OrderBy(p => p.Position.DistanceToSquared(prisoner.Position)).FirstOrDefault();
        public static bool HasSession(Pawn pawn) => pawn?.GetLord()?.LordJob is IBedsideRitualLord;
        internal static bool Supported(Ability a) => ModsConfig.IdeologyActive && a != null && a.GetType() == typeof(Ability) &&
            a.verb?.GetType() == typeof(Verb_CastAbility) &&
            (a.def.defName == "LeaderSpeech" || a.def.defName == "ConversionRitual" || a.def.defName == "Trial");
        internal static bool OwnsAbility(Ability a) => Supported(a) && a.sourcePrecept is Precept_RoleSingle role &&
            role.Active && role.IsAssigned(a.pawn) && role.ideo == a.pawn.Ideo && role.ideo.PreceptsListForReading.Contains(role) &&
            role.AbilitiesForReading.Contains(a) && a.pawn.abilities.AllAbilitiesForReading.Contains(a);
        internal static bool CasterReady(Ability a) => OwnsAbility(a) && Ready(a.pawn);

        // Called on the main game update, never from a UI getter or Scribe.
        internal static void InstallAdapters()
        {
            if (!ModsConfig.IdeologyActive || Find.IdeoManager == null) return;
            foreach (var ideo in Find.IdeoManager.IdeosListForReading)
                foreach (var r in ideo.PreceptsListForReading.OfType<Precept_Ritual>().Where(Supported))
                {
                    if (r.behavior == null) continue;
                    if (Find.Maps.Any(m => Find.IdeoManager.GetActiveRituals(m).Any(j => j.Ritual == r))) continue;
                    if (!r.behavior.def.defName.StartsWith("RR_Bedside", StringComparison.Ordinal))
                    {
                        Type type = r.behavior.GetType();
                        if (type != typeof(RitualBehaviorWorker_Speech) && type != typeof(RitualBehaviorWorker_Conversion) && type != typeof(RitualBehaviorWorker_Trial)) continue;
                        var adapted = DefDatabase<RitualBehaviorDef>.GetNamedSilentFail("RR_Bedside" + r.def.defName);
                        if (adapted == null || !(r.targetFilter is RitualTargetFilter_Altar || r.targetFilter is RitualTargetFilter_IdeoBuildingOrRitualSpot)) continue;
                        r.behavior = adapted.GetInstance();
                        r.targetFilter = new RitualTargetFilter_Bedside { Original = r.targetFilter, def = r.targetFilter.def };
                    }
                    if (r.targetFilter is RitualTargetFilter_Bedside && r.obligationTargetFilter != null &&
                        !(r.obligationTargetFilter is RitualObligationTargetFilter_Bedside))
                        r.obligationTargetFilter = new RitualObligationTargetFilter_Bedside {
                            Original = r.obligationTargetFilter, parent = r, def = r.obligationTargetFilter.def
                        };
                }
        }

        public static bool TryOrder(Ability a, LocalTargetInfo target, out string reason)
        {
            if (AbundanceBedEntry.Candidate(a)) return AbundanceBedEntry.TryOrder(a, target, out reason);
            return TryOpenDialog(a, target, out reason, out _);
        }

        internal static bool TryResolveRitual(Ability a, LocalTargetInfo target, out Precept_Ritual ritual,
            out Dictionary<string, Pawn> forced)
        {
            ritual = null; forced = new Dictionary<string, Pawn>();
            var comp = a?.EffectComps.OfType<CompAbilityEffect_StartRitual>().SingleOrDefault();
            if (comp == null) return false;
            ritual = comp.Ritual;
            if (comp is CompAbilityEffect_StartRitualOnPawn onPawn)
            {
                if (target.Pawn == null || !a.verb.ValidateTarget(target, false)) return false;
                ritual = (Precept_Ritual)AccessTools.Method(onPawn.GetType(), "RitualForTarget").Invoke(onPawn, new object[] { target.Pawn });
                forced[onPawn.Props.targetRoleId] = target.Pawn;
            }
            return Supported(ritual) && ritual.targetFilter is RitualTargetFilter_Bedside && Actor(ritual) == a.pawn;
        }

        internal static bool TryOpenDialog(Ability a, LocalTargetInfo target, out string reason, out Window opened)
        {
            opened = null;
            reason = "RR_BedsideRoleNotReady".Translate();
            if (!CasterReady(a) || !a.CanQueueCast || !a.CanApplyOn(target) || a.GizmoDisabled(out reason) ||
                a.pawn.GetLord() != null ||
                BedsideRoleAbilities.HasSession(a.pawn) || BedsideBodyDiscussion.HasSession(a.pawn) ||
                !BedsideRoleAbilities.BedReservationsPermitSession(a.pawn.CurrentBed()) || !a.pawn.CanReserve(a.pawn)) return false;
            if (!TryResolveRitual(a, target, out var ritual, out var forced)) return false;
            TargetInfo site = ritual.targetFilter.BestTarget(a.pawn, TargetInfo.Invalid);
            if (!LocalSite(a.pawn, site)) { reason = "RR_BedsideRitualSite".Translate(); return false; }
            if (target.Pawn?.IsPrisonerOfColony == true)
            {
                Pawn escort = FindEscort(a.pawn, target.Pawn);
                if (escort == null) { reason = "RR_BedsideRitualEscort".Translate(); return false; }
                forced[EscortId] = escort;
            }
            if (ritual.def.defName == "TrialMentalState" && !Local(a.pawn, target.Pawn))
            { reason = "RR_BedsideRitualLocalTarget".Translate(); return false; }
            reason = ritual.behavior.CanStartRitualNow(site, ritual, null, forced);
            if (!reason.NullOrEmpty()) return false;
            Pawn actor = a.pawn;
            int restJob = actor.CurJob.loadID;
            bool activated = false;
            Window window = ritual.GetRitualBeginWindow(site, null, () => {
                // The native dialog starts the ritual first. Activate the
                // original zero-warmup SpeechBase ability once afterwards,
                // preserving native group overrides (notably conversion).
                if (activated || a.pawn != actor || !OwnsAbility(a) || !(actor.GetLord()?.LordJob is IBedsideRitualLord bedLord) ||
                    ((LordJob_Ritual)bedLord).Ritual != ritual || actor.CurJob?.loadID != restJob) return;
                activated = true;
                a.Activate(target, LocalTargetInfo.Invalid);
            }, actor, forced);
            if (window == null) return false;
            Find.WindowStack.Add(window);
            opened = window;
            reason = null; return true;
        }
    }

    public sealed class GameComponent_BedsideRitualAdapters : GameComponent
    {
        private int lastCheck = -1;
        public GameComponent_BedsideRitualAdapters(Game game) { }
        public override void GameComponentUpdate()
        {
            if (Scribe.mode != LoadSaveMode.Inactive || Current.ProgramState != ProgramState.Playing ||
                LongEventHandler.AnyEventNowOrWaiting || !Current.Game.PlayerHasControl) return;
            int now = Find.TickManager.TicksGame;
            if (lastCheck >= 0 && now >= lastCheck && now - lastCheck < 2500) return;
            lastCheck = now; BedsideRituals.InstallAdapters();
        }
    }

    public sealed class RitualRole_BedsideActor : RitualRoleTag
    {
        public bool requiresReachWhenMobile;
        public override bool AppliesToPawn(Pawn p, out string reason, TargetInfo selectedTarget, LordJob_Ritual ritual = null,
            RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            if (!base.AppliesToPawn(p, out reason, selectedTarget, ritual, assignments, precept, skipReason)) return false;
            if (BedsideRituals.BedCandidate(p))
            {
                bool valid = BedsideRituals.Ready(p) && (!selectedTarget.IsValid || BedsideRituals.LocalSite(p, selectedTarget));
                if (!valid && !skipReason) reason = "RR_BedsideRitualSite".Translate();
                return valid;
            }
            if (p.Downed || (requiresReachWhenMobile && selectedTarget.IsValid && !p.CanReach((LocalTargetInfo)selectedTarget, PathEndMode.Touch, Danger.Some)))
            { if (!skipReason) reason = "RR_BedsideRoleNotReady".Translate(); return false; }
            return true;
        }
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref requiresReachWhenMobile, "requiresReachWhenMobile"); }
    }
    public sealed class RitualRole_BedsideEscort : RitualRole
    {
        public override bool AppliesToPawn(Pawn p, out string reason, TargetInfo selectedTarget, LordJob_Ritual ritual = null,
            RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            Precept_Ritual selected = ritual?.Ritual ?? assignments?.Ritual ?? precept;
            string targetRole = selected?.def.defName == "Conversion" ? "convertee" : "convict";
            Pawn patient = ritual?.PawnWithRole(targetRole) ?? assignments?.FirstAssignedPawn(targetRole);
            if (patient?.IsPrisonerOfColony != true)
            { reason = skipReason ? null : "RR_BedsideRitualEscortNotNeeded".Translate().ToString(); return false; }
            return base.AppliesToPawn(p, out reason, selectedTarget, ritual, assignments, precept, skipReason);
        }
        public override bool AppliesToRole(Precept_Role role, out string reason, Precept_Ritual ritual = null, Pawn pawn = null, bool skipReason = false)
        {
            reason = null;
            bool valid = ritual != null && BedsideRituals.Ready(BedsideRituals.Actor(ritual)) && BedsideRituals.EscortReady(pawn) &&
                pawn != BedsideRituals.Actor(ritual);
            if (!valid && !skipReason) reason = "RR_BedsideRitualEscort".Translate();
            return valid;
        }
    }

    // Designated mobile holders can select their bed before walking there.
    // Unassigned mobile holders retain the saved native target selection.
    public sealed class RitualTargetFilter_Bedside : RitualTargetFilter
    {
        public RitualTargetFilter Original;
        public override bool CanStart(TargetInfo initiator, TargetInfo selectedTarget, out string rejectionReason)
        {
            if (initiator.Thing is Pawn owner && AbundanceBeds.For(owner) is Building_Bed owned &&
                (!BedsideRoleAbilities.Immobile(owner) || owner.CurrentBed() == owned))
            {
                bool valid = BestTarget(initiator, selectedTarget).IsValid;
                rejectionReason = valid ? null : "RR_BedsideRitualSite".Translate().ToString();
                return valid;
            }
            if (!(initiator.Thing is Pawn actor) || !BedsideRituals.BedCandidate(actor))
                return Original.CanStart(initiator, selectedTarget, out rejectionReason);
            rejectionReason = "RR_BedsideRitualSite".Translate();
            return BestTarget(initiator, selectedTarget).IsValid;
        }
        public override TargetInfo BestTarget(TargetInfo initiator, TargetInfo selectedTarget)
        {
            if (initiator.Thing is Pawn owner && AbundanceBeds.For(owner) is Building_Bed owned &&
                (!BedsideRoleAbilities.Immobile(owner) || owner.CurrentBed() == owned))
                return AbundanceBedEntry.CanUseBed(owner, owned) ? new TargetInfo(owned) : TargetInfo.Invalid;
            if (!(initiator.Thing is Pawn actor) || !BedsideRituals.BedCandidate(actor)) return Original.BestTarget(initiator, selectedTarget);
            if (!BedsideRituals.Ready(actor)) return TargetInfo.Invalid;
            Building_Bed designated=AbundanceBeds.For(actor);
            if(designated==actor.CurrentBed() && AbundanceBeds.Valid(designated,actor.Ideo))return designated;
            bool altar = Original is RitualTargetFilter_Altar;
            IEnumerable<ThingDef> types = actor.Ideo.PreceptsListForReading.OfType<Precept_Building>()
                .Select(p => p.ThingDef).Where(d => !altar || d.isAltar);
            var buildings = actor.Map.listerBuildings.allBuildingsColonist.Where(b => types.Contains(b.def)).Cast<Thing>()
                .Where(t => BedsideRituals.LocalSite(actor, t)).ToList();
            if ((!altar || buildings.Count == 0) && (!altar || def.fallbackToRitualSpot))
                buildings.AddRange(actor.Map.listerThings.ThingsOfDef(ThingDefOf.RitualSpot).Where(t => BedsideRituals.LocalSite(actor, t)));
            if (!altar && buildings.Count == 0 && def.fallBackToGatherSpot)
                buildings.AddRange(actor.Map.gatherSpotLister.activeSpots.Select(s => s.parent).Where(t => BedsideRituals.LocalSite(actor, t)));
            return buildings.OrderBy(t => t.Position.DistanceToSquared(actor.Position)).FirstOrDefault();
        }
        public override IEnumerable<string> GetTargetInfos(TargetInfo initiator)
        {
            if (initiator.Thing is Pawn actor && AbundanceBeds.For(actor) is Building_Bed bed && AbundanceBedEntry.CanUseBed(actor, bed))
                yield return bed.LabelCap;
            foreach (string info in Original.GetTargetInfos(initiator)) yield return info;
        }
        public override void ExposeData() { base.ExposeData(); Scribe_Deep.Look(ref Original, "rrOriginalTargetFilter"); }
    }

    internal static class BedsideRitualPatches
    {
        [HarmonyPatch(typeof(JobGiver_SpectateDutySpectateRect), "TryFindSpot")]
        private static class KeepSpectatorsOffBeds
        {
            private static void Postfix(Pawn pawn, PawnDuty duty, ref IntVec3 spot, ref bool __result)
            {
                if (!__result || !(pawn?.GetLord()?.LordJob is IBedsideRitualLord) ||
                    !BedsideRituals.BedAt(spot, pawn.Map)) return;

                Map map = pawn.Map;
                Func<IntVec3, Pawn, Map, bool> safeCell = (cell, spectator, candidateMap) =>
                    !BedsideRituals.BedAt(cell, candidateMap) &&
                    RitualUtility.GoodSpectateCellForRitual(cell, spectator, candidateMap);
                // Preserve every valid native choice, including ritual seats.
                // An unsafe ritual-seat fast path ignores extraValidator, so
                // this repair omits only that shortcut and uses the native
                // preferred-side/allowed-side search for an alternative.
                bool found = duty.spectateRectPreferredSide != SpectateRectSide.None &&
                    SpectatorCellFinder.TryFindSpectatorCellFor(pawn, duty.spectateRect, map, out spot,
                        duty.spectateRectPreferredSide, 1, null, null, safeCell);
                if (!found)
                    found = SpectatorCellFinder.TryFindSpectatorCellFor(pawn, duty.spectateRect, map, out spot,
                        duty.spectateRectAllowedSides, 1, null, null, safeCell);
                if (!found)
                {
                    IntVec3 target = duty.spectateRect.CenterCell;
                    found = CellFinder.TryFindRandomReachableNearbyCell(target, map, 5f, TraverseParms.For(pawn),
                        cell => cell.GetRoom(map) == target.GetRoom(map) && pawn.CanReserveSittableOrSpot(cell) &&
                            !duty.spectateRect.Contains(cell) && !BedsideRituals.BedAt(cell, map), null, out spot);
                }
                __result = found && !BedsideRituals.BedAt(spot, map);
                if (!__result) spot = IntVec3.Invalid;
            }
        }
        [HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord))]
        private static class RespectNonMemberRoles
        {
            private static void Prefix(LordJob lordJob, ref IEnumerable<Pawn> startingPawns)
            {
                // Native creation queries RoleFor without forced roles. Both
                // ordinary and bedside mental-state trial recipients must honour
                // addToLord=false without losing their forced outcome role.
                if (lordJob is LordJob_Ritual ritual && startingPawns != null &&
                    (lordJob is IBedsideRitualLord || ritual.Ritual?.def?.defName == "TrialMentalState"))
                    startingPawns = startingPawns.Where(p => ritual.RoleFor(p, includeForced: true)?.addToLord ?? true).ToList();
            }
        }
        [HarmonyPatch(typeof(LordJob_Ritual), nameof(LordJob_Ritual.VoluntaryJoinPriorityFor))]
        private static class RespectNonMemberRejoining
        {
            private static void Postfix(LordJob_Ritual __instance, Pawn p, ref float __result)
            {
                // Keep the same nonmember role contract if a target's native
                // mental state later recovers and its think tree reaches joining.
                if (__result > 0f && (__instance is IBedsideRitualLord || __instance.Ritual?.def?.defName == "TrialMentalState") &&
                    __instance.RoleFor(p, includeForced: true)?.addToLord == false) __result = 0f;
            }
        }
        [HarmonyPatch(typeof(LordJob_Ritual), "ShouldCallOffBecausePawnNoLongerOwned")]
        private static class RespectNativeNonMemberRequiredRole
        {
            private static void Postfix(LordJob_Ritual __instance, Pawn p, ref bool __result)
            {
                // The ordinary mental trial keeps its forced target outside the
                // Lord, just as the bedside variant does. This one membership
                // check must agree; all target, medical and outcome gates remain.
                if (__result && !(__instance is IBedsideRitualLord) &&
                    __instance.Ritual?.def?.defName == "TrialMentalState" && p != null &&
                    __instance.RoleFor(p, includeForced: true)?.addToLord == false) __result = false;
            }
        }
        [HarmonyPatch(typeof(Ability), nameof(Ability.QueueCastingJob), new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })]
        private static class Queue
        {
            private static bool Prefix(Ability __instance, LocalTargetInfo target)
            {
                if (!BedsideRituals.Supported(__instance) ||
                    (!BedsideRituals.BedCandidate(__instance.pawn) && !AbundanceBedEntry.Candidate(__instance))) return true;
                if (!BedsideRituals.TryOrder(__instance, target, out string reason))
                    Messages.Message(reason ?? "RR_BedsideRoleNotReady".Translate(), __instance.pawn, MessageTypeDefOf.RejectInput, false);
                return false;
            }
        }
        [HarmonyPatch(typeof(LordJob_Ritual), "ShouldBeCalledOff")]
        private static class DownedOrganizer
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                MethodInfo downed = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Downed));
                BedsideRituals.OrganizerPredicatePatched = code.Count(i => i.Calls(downed)) == 1;
                if (!BedsideRituals.OrganizerPredicatePatched)
                { Log.Error("[RimRound] Bedside rituals disabled: native organizer-downing predicate changed."); return code; }
                int index = code.FindIndex(i => i.Calls(downed));
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(code[index].labels); load.blocks.AddRange(code[index].blocks);
                code[index] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DownedOrganizer), nameof(Blocks)));
                code.Insert(index, load); return code;
            }
            private static bool Blocks(Pawn p, LordJob_Ritual ritual) => p.Downed &&
                (!(ritual is IBedsideRitualLord bedside) || !bedside.Bedside.Valid(ritual));
        }
        [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.CheckForJobOverride))]
        private static class KeepRestJob
        {
            private static bool Prefix(Pawn ___pawn) => !AbundanceBedEntry.KeepRestJob(___pawn) &&
                (!(___pawn.GetLord()?.LordJob is IBedsideRitualLord bedside) ||
                bedside.Bedside.Actor != ___pawn || !bedside.Bedside.RestUnchanged);
        }
        [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.CanFallAsleep))]
        private static class ActiveSpeakerStaysAwake
        {
            private static void Postfix(Pawn pawn, ref bool __result)
            {
                // The retained LayDown job normally starts sleep at 75% rest.
                // Speaking is active work even though its owner remains in bed.
                // Do not wake an existing sleeper or suppress exhausted/forced
                // sleep, deathrest, anaesthesia or any other readiness failure.
                if (!__result || pawn?.jobs?.curDriver?.asleep != false ||
                    pawn.CurJob?.forceSleep == true || pawn.Deathresting ||
                    pawn.needs?.rest?.CurCategory == RestCategory.Exhausted) return;
                var ritual = pawn.GetLord()?.LordJob as LordJob_Ritual;
                if (AbundanceBedEntry.KeepRestJob(pawn) ||
                    (ritual is IBedsideRitualLord bedside && bedside.Bedside.Actor == pawn && bedside.Bedside.Valid(ritual)))
                    __result = false;
            }
        }
    }
}
