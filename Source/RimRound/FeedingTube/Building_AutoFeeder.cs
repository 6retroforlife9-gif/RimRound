using RimRound.Comps;
using RimRound.FeedingTube.Comps;
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

namespace RimRound.FeedingTube
{
    [StaticConstructorOnStartup]
    public class Building_AutoFeeder : Building
    {
        int tickCheckInterval = 200;
        AutoFeederMode currentModeInternal = AutoFeederMode.off;
        public bool LockPawnToBed;
        AutoFeederMode CurrentMode 
        {
            get {
                return currentModeInternal;
            }
            set {
                if (currentModeInternal != value && CurrentPawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.GainMealState.Owner == RimRound.Simulation.GainMealOwner.ConnectedFeeder)
                    GainMealSession.End(CurrentPawn);
                currentModeInternal = value;
                UpdatePawnDrinkingSounds();
            }
        }

        private void UpdatePawnDrinkingSounds() {
            drinkingSustainer?.End();
            drinkingSustainer = null;

            if (CurrentMode == AutoFeederMode.off) 
            {
                return;
            }

            SoundDef sound = SoundUtility.GetSwallowSoundByWeightOpinionAndGender(CurrentPawn, CurrentMode);

            if (sound == null) 
            {
                return;
            }

            drinkingSustainer = sound.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(CurrentPawn)));
        }

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
        FullnessAndDietStats_ThingComp CachedFNDComp 
        {
            get 
            {
                if (_cachedFNDComp is null)
                    _cachedFNDComp = forcedTarget.Pawn?.TryGetComp<FullnessAndDietStats_ThingComp>();

                return _cachedFNDComp;
            }
            set 
            { 
                _cachedFNDComp = value; 
            }
        }
        
        LocalTargetInfo forcedTarget = LocalTargetInfo.Invalid;

        FoodNetTrader_ThingComp _foodTraderComp = null;
        FoodNetTrader_ThingComp FoodNetTrader 
        {
            get 
            {
                if (_foodTraderComp is null) 
                {
                    _foodTraderComp = this.TryGetComp<FoodNetTrader_ThingComp>();
                }

                return _foodTraderComp;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_TargetInfo.Look(ref forcedTarget, "autofeederForcedTarget");
            Scribe_Values.Look<AutoFeederMode>(ref currentModeInternal, "CurrentAutoFeederMode", AutoFeederMode.off);
            Scribe_Values.Look(ref LockPawnToBed, "rrLockPawnToBed", false);
        }


        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            base.Destroy(mode);
            HandleDestruction();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            base.DeSpawn(mode);
            HandleDestruction();
        }

        public override void Discard(bool silentlyRemoveReferences = false)
        {
            base.Discard(silentlyRemoveReferences);
            HandleDestruction();
        }

        private void HandleDestruction() 
        {
            if (CurrentPawn?.TryGetComp<FullnessAndDietStats_ThingComp>()?.GainMealState.Owner == RimRound.Simulation.GainMealOwner.ConnectedFeeder)
                GainMealSession.End(CurrentPawn);
            onSound?.End();
            onSound = null;

            drinkingSustainer?.End();
            drinkingSustainer = null;

            if (CurrentPawn != null) 
            {
                // Disconnection or another lifecycle callback may already have
                // removed this marker. Cleanup must remain safe when repeated.
                Hediff connected = CurrentPawn.health.hediffSet.GetFirstHediffOfDef(
                    Defs.HediffDefOf.RimRound_UsingFeedingTube);
                if (connected != null) CurrentPawn.health.RemoveHediff(connected);

                CurrentPawn = null;
            }

            if (this.CachedFNDComp != null)
            {
                this.CachedFNDComp.SloshDurationSeconds = 60;
                this.CachedFNDComp.SloshStartTick = Find.TickManager.TicksAbs;
                this.CachedFNDComp.IsConnectedToFeedingMachine = false;
            }
            this.CachedFNDComp = null;


            this.forcedTarget = LocalTargetInfo.Invalid;
            
            CurrentMode = AutoFeederMode.off;
        }


        protected override void Tick()
        {
            base.Tick();
            if (CurrentPawn != null && CurrentPawn.IsHashIntervalTick(tickCheckInterval))
            {
                if (!CanContinueFeeding())
                {
                    ResetForcedTarget();
                    return;
                }
                if (CurrentMode == AutoFeederMode.off ||
                    FoodNetTrader == null || !FoodNetTrader.CanBeOn ||
                    FoodNetTrader.FoodNet == null)
                    return;

                if (onSound == null)
                {
                    onSound = RimRound.Defs.SoundDefOf.RR_FeedingTube_On.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(this)));
                }

                if (drinkingSustainer == null)
                {
                    UpdatePawnDrinkingSounds();
                }

                // Ineligible pawns still receive ordinary hunger-based care;
                // they must never be routed into the retired stomach reservoir.
                FeedBodyGoalPawn(CachedFNDComp);
            }
        }

        private bool CanContinueFeeding()
        {
            Pawn pawn = CurrentPawn;
            return Spawned && pawn != null && !pawn.Dead && !pawn.Destroyed &&
                pawn.Spawned && pawn.Map == Map && pawn.InBed() &&
                !pawn.Drafted && !pawn.InMentalState &&
                pawn.needs?.food != null && CachedFNDComp != null;
        }

        private void FeedBodyGoalPawn(FullnessAndDietStats_ThingComp comp)
        {
            bool reserveRefused;
            using (var scope = new ConnectedFeederIntake(comp.parent as Pawn, CurrentMode, reserveExternally: true))
                FeedConnectedPawn(comp, scope.OptionalGainAuthorized, out reserveRefused);
            Pawn pawn = comp.parent as Pawn;
            if (reserveRefused && pawn.needs.food.CurCategory >= HungerCategory.Hungry)
            {
                GainMealSession.End(pawn);
                using (var scope = new ConnectedFeederIntake(pawn, CurrentMode, reserveExternally: true, ordinaryOnly: true))
                    FeedConnectedPawn(comp, false, out reserveRefused);
            }
        }

        private void FeedConnectedPawn(FullnessAndDietStats_ThingComp comp, bool gain, out bool reserveRefused)
        {
            reserveRefused = false;
            Pawn pawn = comp.parent as Pawn;
            if (pawn == null ||
                !pawn.WillEat(Defs.ThingDefOf.RR_FeedingTubeFluid, pawn,
                    true, false))
                return;

            bool ordinaryHunger = pawn.needs.food.CurCategory >=
                HungerCategory.Hungry;

            if (!gain && (!ordinaryHunger || (CurrentMode == AutoFeederMode.lose &&
                !BodyFeedingPolicy.IsUrgentlyHungry(pawn))))
                return;

            float vanillaWanted = Mathf.Max(0f,
                pawn.needs.food.MaxLevel - pawn.needs.food.CurLevel);
            float requested = gain ? float.MaxValue : vanillaWanted;
            FoodNet net = FoodNetTrader.FoodNet;
            float ratio = net.FullnessToNutritionRatio;
            float multiplier = comp.FullnessGainedMultiplier;
            if (requested <= 0f || ratio <= 0f ||
                float.IsNaN(ratio) || float.IsInfinity(ratio) ||
                !FoodNetStorage_ThingComp.Finite(multiplier) || multiplier < 0f ||
                net.Stored <= 0f)
                return;

            float pulse = gain && multiplier > .0001f ? comp.SoftLimit * .1f / (ratio * multiplier) : .1f;
            float requestedPulse = Mathf.Min(pulse, requested, net.Stored / ratio);
            float portionNutrition = requestedPulse;
            if (comp.UsesBodyModel)
            {
                RimRound.Simulation.MealPortionPlan metered;
                if (!OrdinaryFoodIntake.TryPlanPortion(pawn, requestedPulse,
                    (double)requestedPulse * ratio, OrdinaryFoodIntake.LiveBudget(pawn), out metered)) return;
                portionNutrition = (float)metered.UsefulNutrition;
            }
            if (portionNutrition <= 0.0001f) return;

            IDisposable lease = null;
            if (gain && !ColonyFoodReserve.TryReserveOptional(pawn, portionNutrition, this, out lease))
            { reserveRefused = true; return; }
            using (lease)
            using (var transfer = new ClassicFoodNetTransaction(net))
            {
                ClassicFoodPortion portion;
                if (!transfer.TryWithdrawNutrition(net, portionNutrition, out portion) ||
                    portion.Nutrition <= 0.0001f ||
                    !portion.Preservation.Valid ||
                    portion.Preservation.RemainingRotTicks <= 0f ||
                    (portion.Preservation.Ingredients?.Any(d => !OrdinaryFoodIntake.IsProcessableFoodDef(d) ||
                        !SpecialistContentPolicy.Allows(d)) ?? false))
                    return;
                float effectiveRatio = portion.FullnessToNutritionRatio * multiplier;
                float addedFullness = portion.Nutrition * effectiveRatio;
                if (!FoodNetStorage_ThingComp.Finite(effectiveRatio) ||
                    !FoodNetStorage_ThingComp.Finite(addedFullness) ||
                    !FoodNetStorage_ThingComp.Finite(comp.CurrentFullness + addedFullness))
                    return;

                float useful = portion.Nutrition;
                if (comp.UsesBodyModel)
                {
                    RimRound.Simulation.MealPortionPlan plan;
                    if (!OrdinaryFoodIntake.TryPlanPortion(pawn, portion.Nutrition, portion.Volume,
                        OrdinaryFoodIntake.LiveBudget(pawn), out plan)) return;
                    comp.RecordOrdinaryIntake(plan, multiplier);
                    useful = (float)plan.UsefulNutrition;
                }
                pawn.needs.food.CurLevel += useful;
                pawn.records?.AddTo(RecordDefOf.NutritionEaten, useful);
                transfer.Commit();
                ColonyFoodReserve.NotifyFoodChanged(Map);
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (forcedTarget != LocalTargetInfo.Invalid)
            {
                DrawFeedingTubeFromDeviceToPawn();
            }
        }


        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            float range = 9;
            GenDraw.DrawRadiusRing(base.Position, range);

            if (forcedTarget != LocalTargetInfo.Invalid) 
            {
                Vector3 targetLocation = forcedTarget.CenterVector3;

                targetLocation = forcedTarget.Thing.TrueCenter();

                Vector3 autoFeederCenter = this.TrueCenter();
                targetLocation.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                autoFeederCenter.y = targetLocation.y;

                GenDraw.DrawLineBetween(autoFeederCenter, targetLocation, Building_TurretGun.ForcedTargetLineMat, 0.2f);
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

            yield return GetModeSwitchGizmo();
            yield return new Command_Toggle
            {
                defaultLabel = "Lock pawn to bed",
                defaultDesc = "Keep connected occupants in bed until unlocked. Releases for rescue, drafting, danger, medical emergencies or loss of the bed. Feeding does not require the lock.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt", true),
                isActive = () => LockPawnToBed,
                toggleAction = () => LockPawnToBed = !LockPawnToBed
            };
        }

        private void DrawFeedingTubeFromDeviceToPawn()
        {
            Vector3 autoFeederHoseAttachmentPoint = GetAutoFeederLocationVector();
            Vector3 mouthPosition = GetInBedTargetMouthPosition();

            GenDraw.DrawLineBetween(autoFeederHoseAttachmentPoint, mouthPosition, feedingTubeMat, 0.2f);
        }

        private Vector3 GetInBedTargetMouthPosition()
        {
            Vector3 targetLocation = forcedTarget.CenterVector3;

            targetLocation = forcedTarget.Pawn.TrueCenter();

            targetLocation.y = AltitudeLayer.MetaOverlays.AltitudeFor();

            float headOffsetInBed = -0.2f;
            targetLocation.z = targetLocation.z + headOffsetInBed;

            return targetLocation;
        }

        public virtual Vector3 GetAutoFeederLocationVector()
        {
            Vector3 autoFeederHoseAttachPoint = this.TrueCenter();

            autoFeederHoseAttachPoint.y = AltitudeLayer.MetaOverlays.AltitudeFor();

            autoFeederHoseAttachPoint.x = autoFeederHoseAttachPoint.x + -0.11f;
            autoFeederHoseAttachPoint.z = autoFeederHoseAttachPoint.z + 0.36f;

            return autoFeederHoseAttachPoint;
        }


        private void FillPawnStomachTo(FullnessAndDietStats_ThingComp pawnFNDComp, float percentToFillTo) 
        {
            float currentFullness = pawnFNDComp.CurrentFullness;
            float targetFullnessVolume = pawnFNDComp.SoftLimit * percentToFillTo;

            float fullnessToAdd = pawnFNDComp.RemainingFullnessUntil(targetFullnessVolume);
            FoodNet net = FoodNetTrader?.FoodNet;
            float ratio = net?.FullnessToNutritionRatio ?? 0f;
            if (!FoodNetStorage_ThingComp.PositiveFinite(fullnessToAdd) ||
                !FoodNetStorage_ThingComp.PositiveFinite(ratio))
                return;
            using (var transfer = new ClassicFoodNetTransaction(net))
            {
                ClassicFoodPortion portion;
                if (!transfer.TryWithdrawNutrition(net, fullnessToAdd / ratio, out portion) ||
                    !FoodNetStorage_ThingComp.Finite(currentFullness + portion.Volume) ||
                    currentFullness + portion.Volume <= currentFullness)
                    return;
                pawnFNDComp.UpdateRatio(portion.Nutrition, portion.FullnessToNutritionRatio);
                pawnFNDComp.CurrentFullness = currentFullness + portion.Volume;
                transfer.Commit();
            }
        }

        private void FillPawnStomachToHardLimit(FullnessAndDietStats_ThingComp pawnFNDComp)
        {
            float hardlimit = pawnFNDComp.HardLimit;
            float softlimit = pawnFNDComp.SoftLimit;

            FillPawnStomachTo(pawnFNDComp, (hardlimit / softlimit) - Values.MinRQ);
        }

        private void FillPawnNutritionTo(FullnessAndDietStats_ThingComp pawnFNDComp, float percentToFillTo) 
        {
            Pawn pawn = pawnFNDComp.parent.AsPawn();
            float nutritionCurrentPercentage = pawn.needs.food.CurLevelPercentage;

            if (percentToFillTo <= nutritionCurrentPercentage)
                return;

            float currentNutritionBeingDigested = pawnFNDComp.CurrentFullness / pawnFNDComp.CurrentFullnessToNutritionRatio;
            float currentNutritionBeingDigestedAsPercentOfNutrition = currentNutritionBeingDigested / pawn.needs.food.MaxLevel;


            float percentageToAdd = percentToFillTo - (nutritionCurrentPercentage + currentNutritionBeingDigestedAsPercentOfNutrition);

            if (percentageToAdd < 0)
                return;

            float rawNutritionToAdd = percentageToAdd * pawn.needs.food.MaxLevel;
            float currentFoodNetDensity = FoodNetTrader.FoodNet.FullnessToNutritionRatio;

            float fullnessToAdd = rawNutritionToAdd * currentFoodNetDensity;
            float currentFullness = pawnFNDComp.OccupiedVolume;

            float fullnessPercent = (currentFullness + fullnessToAdd) / pawnFNDComp.SoftLimit;

            if (fullnessPercent > 1)
                fullnessPercent = 1;

            FillPawnStomachTo(pawnFNDComp, fullnessPercent);
        }



        Command_Action GetModeSwitchGizmo() 
        {
            Command_Action command_Action = new Command_Action();

            switch (CurrentMode) 
            {
                case AutoFeederMode.off:
                    command_Action.defaultLabel = "FeedingTubeModeLabel_off".Translate();
                    command_Action.defaultDesc = "FeedingTubeModeLabel_offDesc".Translate();
                    command_Action.icon = offIcon;
                    command_Action.action = delegate ()
                    {
                        CurrentMode = AutoFeederMode.lose;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
                    };

                    break;
                case AutoFeederMode.lose:

                    command_Action.defaultLabel = "FeedingTubeModeLabel_lose".Translate();
                    command_Action.defaultDesc = "FeedingTubeModeLabel_loseDesc".Translate();
                    command_Action.icon = loseIcon;
                    command_Action.action = delegate ()
                    {
                        CurrentMode = AutoFeederMode.maintain;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
                    };

                    break;
                case AutoFeederMode.maintain:
                    command_Action.defaultLabel = "FeedingTubeModeLabel_maintain".Translate();
                    command_Action.defaultDesc = "FeedingTubeModeLabel_maintainDesc".Translate();
                    command_Action.icon = maintainIcon;
                    command_Action.action = delegate ()
                    {
                        CurrentMode = AutoFeederMode.gain;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
                    };

                    break;
                case AutoFeederMode.gain:
                    command_Action.defaultLabel = "FeedingTubeModeLabel_gain".Translate();
                    command_Action.defaultDesc = "FeedingTubeModeLabel_gainDesc".Translate();
                    command_Action.icon = gainIcon;
                    command_Action.action = delegate ()
                    {
                        CurrentMode = AutoFeederMode.maxgain;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
                    };

                    break;
                case AutoFeederMode.maxgain:
                    command_Action.defaultLabel = "FeedingTubeModeLabel_maxgain".Translate();
                    command_Action.defaultDesc = "FeedingTubeModeLabel_maxgainDesc".Translate();
                    command_Action.icon = forceGainIcon;
                    command_Action.action = delegate ()
                    {
                        CurrentMode = AutoFeederMode.off;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
                    };

                    break;
            }

            return command_Action;
        }

        Command_Action GetStartActionGizmo() 
        {
            Command_Action command_Action = new Command_Action();
            command_Action.defaultLabel = "FeedingTube_TargetPawn".Translate();
            command_Action.defaultDesc = "FeedingTube_TargetPawnDesc".Translate();
            command_Action.icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", true);
            command_Action.action = delegate ()
            {
                this.BeginTargeting();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
            };

            //command_VerbTarget.Disable("CannotFire".Translate() + ": " + "Roofed".Translate().CapitalizeFirst());

            return command_Action;
        }

        Command_Action GetStopActionGizmo() 
        {
            Command_Action command_Action = new Command_Action();
            command_Action.defaultLabel = "FeedingTube_StopFeedingLabel".Translate();
            command_Action.defaultDesc = "FeedingTube_StopFeedingDesc".Translate();
            command_Action.icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt", true);
            command_Action.action = delegate ()
            {
                ResetForcedTarget();
                
                SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
            };

            return command_Action;
        }

        private bool ValidateTarget(LocalTargetInfo target)
        {
            if (target.Pawn is null)
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
                            return;
                        }
                        this.CurrentPawn = t.Pawn;
                        this.CachedFNDComp = t.Pawn.TryGetComp<FullnessAndDietStats_ThingComp>();
                        this.CurrentPawn.health.AddHediff(Defs.HediffDefOf.RimRound_UsingFeedingTube);
                        forcedTarget = t;
                        
                        if (onSound == null)
                        {
                            onSound = RimRound.Defs.SoundDefOf.RR_FeedingTube_On.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(this)));
                        }
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);

                        this.CachedFNDComp.IsConnectedToFeedingMachine = true;
                        this.CachedFNDComp.SloshDurationSeconds = 0;
                        this.CachedFNDComp.SloshStartTick = 0;

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
                        if (t.Pawn is null || !t.Pawn.RaceProps.Humanlike || !t.Pawn.InBed())
                            return false;

                        return true;
                    }, 
                null, //Caster
                null, // Action when finished
                Defs.ThingDefOf.RR_FeedingTubeFluid.uiIcon, //Icon
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

        private void ResetForcedTarget()
        {
            HandleDestruction();
        }


        private Sustainer onSound;
        private Sustainer drinkingSustainer;

        private static readonly Material feedingTubeMat = MaterialPool.MatFrom("Things/Building/Production/FeedingTubePipeThickMat", ShaderDatabase.Transparent, Color.white);
        private static readonly Texture2D offIcon = ContentFinder<Texture2D>.Get("UI/AutoFeeder/Modes/Off", true);
        private static readonly Texture2D loseIcon = ContentFinder<Texture2D>.Get("UI/AutoFeeder/Modes/Lose", true);
        private static readonly Texture2D maintainIcon = ContentFinder<Texture2D>.Get("UI/AutoFeeder/Modes/Maintain", true);
        private static readonly Texture2D gainIcon = ContentFinder<Texture2D>.Get("UI/AutoFeeder/Modes/Gain", true);
        private static readonly Texture2D forceGainIcon = ContentFinder<Texture2D>.Get("UI/AutoFeeder/Modes/ForceGain", true);

    }



    public enum AutoFeederMode 
    {
        off,
        lose,
        maintain,
        gain,
        maxgain,
    }
}
