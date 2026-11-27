using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimRound.Things
{
    [StaticConstructorOnStartup]
    public class Building_ZenithOrb : Building
    {
        int tickCheckInterval = 120;
        float cellRadius = 20;
        Pawn _currentPawn;
        public Pawn CurrentPawn
        {
            get
            {
                if (_currentPawn is null)
                    _currentPawn = forcedTarget.Pawn;

                return _currentPawn;
            }
            set
            {
                _currentPawn = value;
            }
        }

        FullnessAndDietStats_ThingComp _cachedFNDComp = null;
        CompPowerTrader _cachedPowerTrader = null;

        CompPowerTrader CachedPowerTrader
        {
            get
            {
                if (_cachedPowerTrader is null)
                    _cachedPowerTrader = this.TryGetComp<CompPowerTrader>();

                return _cachedPowerTrader;
            }
            set
            {
                _cachedPowerTrader = value;
            }
        }

        FullnessAndDietStats_ThingComp CachedFNDComp
        {
            get
            {
                if (_cachedFNDComp is null)
                    _cachedFNDComp = forcedTarget.Pawn.TryGetComp<FullnessAndDietStats_ThingComp>();

                return _cachedFNDComp;
            }
            set
            {
                _cachedFNDComp = value;
            }
        }

        LocalTargetInfo forcedTarget = LocalTargetInfo.Invalid;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_TargetInfo.Look(ref forcedTarget, "forcedTarget");
        }


        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (forcedTarget != null && forcedTarget.Pawn != null)
            {
                onSustainer = SpawnOnSustainer();
            }
            else
            {
                offSustainer = SpawnOffSustainer();
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            base.Destroy(mode);
            onSustainer?.End();
            offSustainer?.End();

            onSustainer = null;
            offSustainer = null;
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            base.DeSpawn(mode);
            onSustainer?.End();
            offSustainer?.End();

            onSustainer = null;
            offSustainer = null;
        }

        public override void Discard(bool silentlyRemoveReferences = false)
        {
            base.Discard(silentlyRemoveReferences);
            onSustainer?.End();
            offSustainer?.End();

            onSustainer = null;
            offSustainer = null;
        }

        protected override void Tick()
        {
            base.Tick();
            if (!SpecialistContentPolicy.Allows(def))
            {
                if (forcedTarget.IsValid) ResetForcedTarget();
                return;
            }
            if (CurrentPawn != null && (!CurrentPawn.Spawned || CurrentPawn.Map != Map || CurrentPawn.Dead || CachedFNDComp == null))
                ResetForcedTarget();
            if (CurrentPawn != null && CurrentPawn.IsHashIntervalTick(tickCheckInterval))
            {
                if (CachedPowerTrader.PowerOn && Position.DistanceToSquared(CurrentPawn.Position) <= cellRadius * cellRadius)
                    CachedFNDComp.activeWeightGainRequests.Enqueue(new WeightGainRequest(50, Find.TickManager.TicksGame + 10, 0, false));
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (forcedTarget != LocalTargetInfo.Invalid)
            {
                DrawEffects();
            }
        }


        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            float range = cellRadius;
            GenDraw.DrawRadiusRing(base.Position, range);

            if (forcedTarget != LocalTargetInfo.Invalid)
            {
                Vector3 targetLocation = forcedTarget.Thing.TrueCenter();

                Vector3 zenithOrbCenter = this.TrueCenter();
                targetLocation.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                zenithOrbCenter.y = targetLocation.y;

                GenDraw.DrawLineBetween(zenithOrbCenter, targetLocation);
            }
        }


        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (forcedTarget == LocalTargetInfo.Invalid)
                yield return GetStartActionGizmo();
            else
                yield return GetStopActionGizmo();
        }

        private void DrawEffects()
        {
            //Draw cool effects
        }

        Command_Action GetStartActionGizmo()
        {
            Command_Action command_Action = new Command_Action();
            command_Action.defaultLabel = "ZenithOrb_TargetPawn".Translate();
            command_Action.defaultDesc = "ZenithOrb_TargetPawnDesc".Translate();
            command_Action.icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", true);
            command_Action.action = delegate ()
            {
                this.BeginTargeting();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
            };

            if (!SpecialistContentPolicy.Allows(def)) command_Action.Disable(SpecialistContentPolicy.FantasyReason);

            //command_VerbTarget.Disable("CannotFire".Translate() + ": " + "Roofed".Translate().CapitalizeFirst());

            return command_Action;
        }

        Command_Action GetStopActionGizmo()
        {
            Command_Action command_Action = new Command_Action();
            command_Action.defaultLabel = "ZenithOrb_StopTargetPawn".Translate();
            command_Action.defaultDesc = "ZenithOrb_StopTargetPawnDesc".Translate();
            command_Action.icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt", true);
            command_Action.action = delegate ()
            {
                ResetForcedTarget();
                CachedFNDComp = null;
                SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
            };

            return command_Action;
        }

        private bool ValidateTarget(LocalTargetInfo target)
        {
            if (!SpecialistContentPolicy.Allows(def) || target.Pawn == null || target.Pawn.Dead ||
                target.Pawn.Map != Map || !BodyModelEligibility.HasAdultBiology(target.Pawn) ||
                target.Pawn.TryGetComp<FullnessAndDietStats_ThingComp>() == null)
            {
                return false;
            }
            return true;
        }

        private void BeginTargeting()
        {
            Find.Targeter.BeginTargeting(this.TargetingParams,
                delegate (LocalTargetInfo t) //action
                {
                    if (!this.ValidateTarget(t))
                    {
                        this.BeginTargeting();
                        offSustainer = SpawnOffSustainer();
                        return;
                    }
                    this.CurrentPawn = t.Pawn;
                    this.CachedFNDComp = t.Pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
                    forcedTarget = t;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    onSustainer = SpawnOnSustainer();
                    return;
                },
                delegate (LocalTargetInfo t) //highlight action
                {
                    if (t.Pawn != null)
                    {
                        GenDraw.DrawTargetHighlight(t);
                        return;
                    }
                },
                delegate (LocalTargetInfo t) //validator
                {
                    if (t.Pawn is null || !t.Pawn.RaceProps.Humanlike)
                        return false;

                    return true;
                },
                null, //Caster
                null, // Action when finished
                null,// Icon
                false); //play sound on action
        }

        private TargetingParameters TargetingParams
        {
            get
            {
                return new TargetingParameters
                {
                    canTargetPawns = true,
                    canTargetLocations = false
                };
            }
        }

        private Sustainer SpawnOffSustainer() {
            if (onSustainer != null) {
                onSustainer.End();
                onSustainer = null;
            }

            return offSustainer ?? RimRound.Defs.SoundDefOf.RR_ZenithOrbOffSound.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(this)));
        }

        private Sustainer SpawnOnSustainer() {
            if (offSustainer != null) {
                offSustainer.End();
                offSustainer = null;
            }

            return onSustainer ?? RimRound.Defs.SoundDefOf.RR_ZenithOrbOnSound.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(this)));
        }

        private void ResetForcedTarget()
        {
            this.forcedTarget = LocalTargetInfo.Invalid;
            this.CurrentPawn = null;
            this.CachedFNDComp = null;
            offSustainer = SpawnOffSustainer();
            
        }

        private Sustainer offSustainer;
        private Sustainer onSustainer;
    }
}
