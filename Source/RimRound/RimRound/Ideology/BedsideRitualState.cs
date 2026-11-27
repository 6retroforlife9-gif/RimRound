using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimRound.AI;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimRound.Ideology
{
    public interface IBedsideRitualLord { BedsideRitualState Bedside { get; } }

    public sealed class BedsideRitualState : IExposable
    {
        public Pawn Actor, Target, Escort;
        public Building_Bed Bed;
        public Ideo Ideology, TargetIdeology;
        public Precept_RoleSingle Role;
        public TargetInfo Site;
        public Job ReservationJob;
        public IntVec3 Position, TargetPosition = IntVec3.Invalid;
        public bool UsesTargetPosition;
        public int RestJobId, CreatedTick, LastTick = -1, SpeechTicks;
        public bool Released;
        public bool RestUnchanged => Actor?.Spawned == true && Actor.CurrentBed() == Bed &&
            Actor.Position == Position && Actor.CurJob?.loadID == RestJobId;

        public BedsideRitualState() { }
        public BedsideRitualState(TargetInfo site, Precept_Ritual ritual, RitualRoleAssignments assignments)
        {
            Actor = assignments.FirstAssignedPawn(BedsideRituals.ActorRoleId(ritual));
            Target = assignments.FirstAssignedPawn(ritual.def.defName == "Conversion" ? "convertee" : "convict");
            Escort = assignments.FirstAssignedPawn(BedsideRituals.EscortId);
            Bed = Actor.CurrentBed(); Position = Actor.Position; RestJobId = Actor.CurJob.loadID;
            Role = Actor.Ideo.GetRole(Actor) as Precept_RoleSingle; Ideology = Actor.Ideo; Site = site;
            TargetIdeology = Target?.Ideo;
            UsesTargetPosition = Target != null && ritual.def.defName != "TrialMentalState";
            if (UsesTargetPosition)
                TargetPosition = Bed.OccupiedRect().AdjacentCells.Where(c => c.InBounds(Actor.Map) && c.Standable(Actor.Map) &&
                    c.GetEdifice(Actor.Map) == null && !BedsideRituals.BedAt(c, Actor.Map) && c.GetRoom(Actor.Map) == Actor.GetRoom() &&
                    GenSight.LineOfSight(Position, c, Actor.Map, true) &&
                    (Escort ?? Target).CanReach(c, PathEndMode.OnCell, Danger.Some))
                    .OrderBy(c => c.DistanceToSquared(Position)).ThenBy(c => c.DistanceToSquared(Target.PositionHeld))
                    .DefaultIfEmpty(IntVec3.Invalid).First();
            CreatedTick = Find.TickManager.TicksGame;
            ReservationJob = JobMaker.MakeJob(BedsideBodyDiscussion.SessionJob, Actor);
        }
        public void ExposeData()
        {
            Scribe_References.Look(ref Actor, "actor"); Scribe_References.Look(ref Target, "target");
            Scribe_References.Look(ref Escort, "escort"); Scribe_References.Look(ref Bed, "bed");
            Scribe_References.Look(ref Ideology, "ideology"); Scribe_References.Look(ref Role, "role");
            Scribe_References.Look(ref TargetIdeology, "targetIdeology");
            Scribe_TargetInfo.Look(ref Site, "site"); Scribe_Deep.Look(ref ReservationJob, "reservationJob");
            Scribe_Values.Look(ref Position, "position"); Scribe_Values.Look(ref RestJobId, "restJobId");
            Scribe_Values.Look(ref TargetPosition, "targetPosition", IntVec3.Invalid);
            Scribe_Values.Look(ref UsesTargetPosition, "usesTargetPosition");
            Scribe_Values.Look(ref CreatedTick, "createdTick"); Scribe_Values.Look(ref LastTick, "lastTick", -1);
            Scribe_Values.Look(ref SpeechTicks, "speechTicks"); Scribe_Values.Look(ref Released, "released");
        }
        internal bool Reserve() => (!UsesTargetPosition || TargetPosition.IsValid) && Actor.CanReserve(Actor) &&
            Actor.Map.reservationManager.Reserve(Actor, ReservationJob, Actor, errorOnFailed: false);
        internal void Release(Map map)
        {
            if (Released) return;
            Released = true;
            if (Actor != null && ReservationJob != null) map?.reservationManager.ReleaseClaimedBy(Actor, ReservationJob);
        }
        public bool Valid(LordJob_Ritual ritual)
        {
            int now = Find.TickManager.TicksGame;
            if (Released || !RestUnchanged || !BedsideRituals.Ready(Actor) ||
                Actor.Ideo != Ideology || Role == null || !Role.Active || !Role.IsAssigned(Actor) ||
                !Ideology.PreceptsListForReading.Contains(Role) || !Ideology.PreceptsListForReading.Contains(ritual.Ritual) ||
                ritual.Ritual.ideo != Ideology || ritual.PawnWithRole(BedsideRituals.ActorRoleId(ritual.Ritual)) != Actor ||
                !BedsideRituals.LocalSite(Actor, Site) || !BedsideRoleAbilities.BedReservationsPermitSession(Bed) ||
                !Actor.Map.reservationManager.ReservedBy(Actor, Actor, ReservationJob) ||
                !Actor.Map.reservationManager.OnlyReservationsForJobDef(Actor, BedsideBodyDiscussion.SessionJob, true) ||
                Actor.Map.physicalInteractionReservationManager.IsReserved(Actor) ||
                now < CreatedTick || now - CreatedTick > 30000) return false;
            if (UsesTargetPosition && (!TargetPosition.IsValid || !TargetPosition.Standable(Actor.Map) ||
                TargetPosition.GetEdifice(Actor.Map) != null || BedsideRituals.BedAt(TargetPosition, Actor.Map) ||
                TargetPosition.GetRoom(Actor.Map) != Actor.GetRoom())) return false;
            if (Target != null)
            {
                bool gathering = ritual.StageIndex == 0 && ritual.Ritual.def.defName != "TrialMentalState";
                if (Target.Dead || Target.MapHeld != Actor.Map || Target.Ideo != TargetIdeology ||
                    (!Target.Spawned && !(gathering && Target.CarriedBy == Escort))) return false;
                if (!gathering && !BedsideRituals.Local(Actor, Target)) return false;
                if (gathering && Target.IsPrisonerOfColony && (!BedsideRituals.EscortReady(Escort) ||
                    ritual.PawnWithRole(BedsideRituals.EscortId) != Escort || Escort.GetLord() != ritual.lord)) return false;
            }
            return true;
        }
        internal bool Tick(LordJob_Ritual ritual)
        {
            int now = Find.TickManager.TicksGame;
            if (LastTick == now) return false;
            if (LastTick >= 0 && now != LastTick + 1) { ritual.Cancel(); return false; }
            LastTick = now;
            // A stationary actor has genuinely arrived at the saved bed cell.
            ritual.AddTagForPawn(Actor, StageEndTrigger_RolesArrived.ArrivalTag);
            var behavior = ritual.CurrentStage?.BehaviorForRole(BedsideRituals.ActorRoleId(ritual.Ritual));
            if (behavior?.speakerInteraction == null) return true;
            Actor.skills?.Learn(SkillDefOf.Social, .3f);
            if (++SpeechTicks < 480) return true;
            SpeechTicks = 0;
            MoteMaker.MakeSpeechBubble(Actor, JobDriver_GiveSpeech.moteIcon);
            SocialInteractionUtility.ImitateSocialInteractionWithManyPawns(Actor,
                ritual.lord.ownedPawns.Where(p => p != Actor && BedsideRituals.Local(Actor, p)).ToList(), behavior.speakerInteraction);
            return true;
        }

        private static readonly MethodInfo CloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static T Clone<T>(T obj) where T : class => (T)CloneMethod.Invoke(obj, null);
        internal List<RitualStage> Stages(Precept_Ritual ritual)
        {
            string actorId = BedsideRituals.ActorRoleId(ritual);
            var stages = new List<RitualStage>();
            foreach (RitualStage original in ritual.behavior.def.stages)
            {
                RitualStage stage = Clone(original);
                AccessTools.Field(typeof(RitualStage), "behaviorForRole").SetValue(stage, new Dictionary<string, RitualRoleBehavior>());
                stage.roleBehaviors = original.roleBehaviors?.Select(Clone).ToList() ?? new List<RitualRoleBehavior>();
                stage.failTriggers = original.failTriggers?.Select(Clone).ToList();
                stage.endTriggers = original.endTriggers?.Select(Clone).ToList();
                var actorBehavior = stage.roleBehaviors.FirstOrDefault(b => b.roleId == actorId);
                if (actorBehavior != null)
                {
                    bool delivery = actorBehavior.dutyDef.defName == "DeliverPawnToAltar" || actorBehavior.dutyDef.defName == "DeliverPawnToCell";
                    if (delivery && Escort != null)
                    {
                        var escortBehavior = Clone(actorBehavior); escortBehavior.roleId = BedsideRituals.EscortId;
                        // The bed remains occupied by the speaker. The native
                        // cell-delivery job carries a prisoner beside it without
                        // attempting to reserve or use the speaker's bed.
                        escortBehavior.dutyDef = DefDatabase<DutyDef>.GetNamed("DeliverPawnToCell");
                        AccessTools.Field(typeof(RitualRoleBehavior), "customPositions").SetValue(escortBehavior,
                            new List<RitualPosition> { new RitualPosition_BedsideTarget() });
                        stage.roleBehaviors.Add(escortBehavior);
                        foreach (StageFailTrigger trigger in stage.failTriggers ?? Enumerable.Empty<StageFailTrigger>())
                        {
                            if (trigger is StageFailTrigger_TargetPawnUnreachable reach) reach.takerId = BedsideRituals.EscortId;
                            if (trigger is StageFailTrigger_PawnAsleep asleep) asleep.pawnId = BedsideRituals.EscortId;
                        }
                    }
                    else if (delivery)
                    {
                        // A free recipient walks beside the bed. Remove only
                        // the prisoner-specific first-stage checks.
                        stage = new RitualStage {
                            parent = original.parent, defaultDuty = original.defaultDuty, roleBehaviors = stage.roleBehaviors,
                            showProgressBar = original.showProgressBar, spectatorsRequired = original.spectatorsRequired
                        };
                    }
                    if (delivery)
                        stage.endTriggers = new List<StageEndTrigger> { new StageEndTrigger_BedsideRecipientArrived() };
                    actorBehavior.dutyDef = DefDatabase<DutyDef>.GetNamed("RR_BedsideRitualActor");
                    AccessTools.Field(typeof(RitualRoleBehavior), "customPositions").SetValue(actorBehavior,
                        new List<RitualPosition> { new RitualPosition_BedsideActor() });
                }
                if (UsesTargetPosition)
                {
                    string targetId = ritual.def.defName == "Conversion" ? "convertee" : "convict";
                    var targetBehavior = stage.roleBehaviors.FirstOrDefault(b => b.roleId == targetId);
                    if (targetBehavior != null)
                    {
                        if (targetBehavior.dutyDef.defName == "LayDownAwake")
                            targetBehavior.dutyDef = DefDatabase<DutyDef>.GetNamed("StandOnCell");
                        AccessTools.Field(typeof(RitualRoleBehavior), "customPositions").SetValue(targetBehavior,
                            new List<RitualPosition> { new RitualPosition_BedsideTarget() });
                    }
                }
                stages.Add(stage);
            }
            return stages;
        }
    }

    public sealed class RitualPosition_BedsideActor : RitualPosition
    {
        public override PawnStagePosition GetCell(IntVec3 spot, Pawn p, LordJob_Ritual ritual) =>
            new PawnStagePosition(((IBedsideRitualLord)ritual).Bedside.Position, null, Rot4.Invalid, highlight);
    }

    public sealed class RitualPosition_BedsideTarget : RitualPosition
    {
        public override PawnStagePosition GetCell(IntVec3 spot, Pawn p, LordJob_Ritual ritual) =>
            new PawnStagePosition(((IBedsideRitualLord)ritual).Bedside.TargetPosition, null, Rot4.Invalid, highlight);
    }

    public sealed class StageEndTrigger_BedsideRecipientArrived : StageEndTrigger
    {
        public override Trigger MakeTrigger(LordJob_Ritual ritual, TargetInfo spot, IEnumerable<TargetInfo> foci, RitualStage stage) =>
            new Trigger_TickCondition(() => {
                var state = ((IBedsideRitualLord)ritual).Bedside;
                return state.Target?.Spawned == true && state.Target.Position == state.TargetPosition;
            });
        public override void ExposeData() { }
    }

    public sealed class LordJob_BedsideSpeech : LordJob_Joinable_Speech, IBedsideRitualLord
    {
        private BedsideRitualState bedside;
        public BedsideRitualState Bedside => bedside;
        public LordJob_BedsideSpeech() { }
        public LordJob_BedsideSpeech(TargetInfo target, Precept_Ritual ritual, RitualRoleAssignments assignments, BedsideRitualState state)
            : base(target, state.Actor, ritual, state.Stages(ritual), assignments, false) { bedside = state; spot = state.Position; }
        protected override bool ShouldBeCalledOff() => !bedside.Valid(this) || base.ShouldBeCalledOff();
        public override IEnumerable<Pawn> PawnsToCountTowardsPresence => base.PawnsToCountTowardsPresence.Where(p => p == bedside.Actor || BedsideRituals.Local(bedside.Actor, p));
        public override void LordJobTick()
        {
            if (!bedside.Valid(this)) { Cancel(); return; }
            if (bedside.Tick(this)) base.LordJobTick();
        }
        public override void Cleanup() { bedside.Release(Map); base.Cleanup(); }
        public override void ExposeData() { base.ExposeData(); Scribe_Deep.Look(ref bedside, "rrBedsideRitual"); }
    }
    public sealed class LordJob_BedsideRitual : LordJob_Ritual, IBedsideRitualLord
    {
        private BedsideRitualState bedside;
        public BedsideRitualState Bedside => bedside;
        public LordJob_BedsideRitual() { }
        public LordJob_BedsideRitual(TargetInfo target, Precept_Ritual ritual, RitualObligation obligation,
            RitualRoleAssignments assignments, BedsideRitualState state)
            : base(target, ritual, obligation, state.Stages(ritual), assignments, state.Actor, state.Position) { bedside = state; }
        protected override bool ShouldBeCalledOff() => !bedside.Valid(this) || base.ShouldBeCalledOff();
        protected override bool ShouldCallOffBecausePawnNoLongerOwned(Pawn pawn)
        {
            // Mental-state trial targets retain their own AI. Native forced-role
            // assignments still list them as required, despite addToLord=false.
            if (pawn != null && pawn == bedside?.Target && Ritual.def.defName == "TrialMentalState" &&
                RoleFor(pawn, includeForced: true)?.addToLord == false) return false;
            return base.ShouldCallOffBecausePawnNoLongerOwned(pawn);
        }
        public override IEnumerable<Pawn> PawnsToCountTowardsPresence => base.PawnsToCountTowardsPresence.Where(p => p == bedside.Actor || BedsideRituals.Local(bedside.Actor, p));
        public override void LordJobTick()
        {
            if (!bedside.Valid(this)) { Cancel(); return; }
            if (bedside.Tick(this)) base.LordJobTick();
        }
        public override void Cleanup() { bedside.Release(Map); base.Cleanup(); }
        public override void ExposeData() { base.ExposeData(); Scribe_Deep.Look(ref bedside, "rrBedsideRitual"); }
    }
}
