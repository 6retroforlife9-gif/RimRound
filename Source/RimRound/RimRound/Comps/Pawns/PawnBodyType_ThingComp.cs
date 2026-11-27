using AlienRace;
using RimRound.UI;
using RimRound.Utilities;
using RimRound.State;
using RimRound.Presentation;
using RimRound.Presentation.Anthrosonae;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.Comps
{
    public class PawnBodyType_ThingComp : ThingComp
    {
        public PawnBodyType_ThingComp() 
        {
            PresentationGame = Current.Game;
            dynamicBodyExcemptionGizmo = new PersonalDynamicBodyGizmo(this);
            customBodyTypeGizmo = new CustomBodyTypeGizmo(this);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) RestoreOrObserveNativeBody();
            Scribe_Values.Look<BodyArchetype>(ref _bodyarchetype, "_bodyarchetype", BodyArchetype.none, false);
            Scribe_Values.Look<string>(ref personallyExempt.reason, "personallyExempt", null);
            Scribe_Values.Look<string>(ref bodyTypeDictNameString, "bodyTypeDictNameString", null, false);
            Scribe_Values.Look(ref rrPresentationSchema, "rrPresentationSchema", 0);
            Scribe_Values.Look(ref rrRequestedProfileId, "rrRequestedProfileId", null);
            Scribe_Values.Look(ref rrRequestedShapeId, "rrRequestedShapeId", null);
            Scribe_Values.Look(ref rrPresentationMode, "rrPresentationMode", null);
            Scribe_Values.Look(ref rrOverrideXenotypeArtwork, "rrOverrideXenotypeArtwork", false);
            Scribe_Values.Look(ref rrNativeBodyDefName, "rrNativeBodyDefName", null);
            Scribe_Values.Look(ref rrNativeBodyProvenance, "rrNativeBodyProvenance", null);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                PresentationRefreshQueue.BindGame(this);
                preparedPresentation = null;
                PreparingPresentation = false;
                anthrosonaePaletteSnapshots.Clear();
                anthrosonaePaletteCoordinator = null;
                EnsurePresentationState();
                ObserveCurrentNativeBody("observed-native");
                // Human's explicit legacy fallback preserves compatibility; it
                // is an inference, never a recovered historical native body.
                var loadedPawn = parent as Pawn;
                if (rrNativeBodyDefName == null && loadedPawn?.def == RimWorld.ThingDefOf.Human &&
                    loadedPawn.story?.bodyType != null && BodyTypeUtility.IsRRBody(loadedPawn.story.bodyType))
                    TryInferLegacyNativeBody(RimWorld.BodyTypeDefOf.Thin, true);
                LegacyBodyStagePolicies.InitializeForPawn(parent as Pawn);
                PresentationRefreshQueue.Enqueue(parent as Pawn, this);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (GlobalSettings.showExemptionGizmo)
                yield return dynamicBodyExcemptionGizmo;

            yield return customBodyTypeGizmo;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (((Pawn)parent).RaceProps.Humanlike)
            {
                PresentationRefreshQueue.BindGame(this);
                EnsurePresentationState();
                ObserveCurrentNativeBody(respawningAfterLoad ? "observed-native" : "captured");
                BodyTypeUtility.AssignPersonalCategoricalExemptions(this);
                var pawn = (Pawn)parent;
                LegacyBodyStagePolicies.InitializeForPawn(pawn);
                RestoreOrObserveNativeBody();
                BodyTypeUtility.UpdatePawnSprite(pawn, PersonallyExempt, categoricallyExempt, true, !respawningAfterLoad);
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();

            if (((Pawn)parent).RaceProps.Humanlike)
            {
                ticksSinceLastBodyChange += 250;
                if (ticksSinceLastBodyChange >= numberOfTicksCooldownPerChange)
                {
                    if (UnityData.IsInMainThread && Current.Game != null && ReferenceEquals(PresentationGame, Current.Game))
                        BodyTypeUtility.AssignPersonalCategoricalExemptions(this);
                    BodyTypeUtility.UpdatePawnSprite((Pawn)parent, PersonallyExempt, categoricallyExempt, false, true);
                }
            }
        }



        public Dictionary<BodyArchetype, Dictionary<BodyTypeDef, BodyTypeInfo>> CustomBodyTypeDict 
        {
            get 
            {
                if (bodyTypeDictNameString is null)
                    return null;

                Dictionary<BodyArchetype, Dictionary<BodyTypeDef, BodyTypeInfo>> returnValue = null;
                RacialBodyTypeInfoUtility.genderedSets.TryGetValue(bodyTypeDictNameString, out returnValue);
                return returnValue;
            } 
        }


        public void RegisterListener(IWeightStageChangedListener listener) 
        {
            _weightStageListeners.Add(listener);
        }

        public void RemoveListener(IWeightStageChangedListener listener) 
        {
            bool success = _weightStageListeners.Remove(listener);

            if (!success) 
            {
                Log.Warning($"Tried to remove listener from {this.parent.AsPawn().Name.ToStringFull} but it wasn't registered!");
            }
        }


        public void NotifyWeightStageListeners() 
        {
            foreach (var listener in _weightStageListeners) 
            {
                listener.OnWeightStageChanged();
            }
        }

        private List<IWeightStageChangedListener> _weightStageListeners = new List<IWeightStageChangedListener>();

        public string bodyTypeDictNameString = null;

        private int rrPresentationSchema;
        private string rrRequestedProfileId;
        private string rrRequestedShapeId;
        private string rrPresentationMode;
        private bool rrOverrideXenotypeArtwork;
        private string rrNativeBodyDefName;
        private string rrNativeBodyProvenance;
        private long presentationStateRevision;
        private volatile PreparedBodyPresentation preparedPresentation;
        internal bool PreparingPresentation;
        internal readonly object PresentationQueueLock = new object();
        internal Game PresentationGame;
        internal long PresentationLoadGeneration;
        internal PresentationRefreshWork PendingPresentationRefresh;
        internal long PresentationRefreshRevision;
        private readonly List<PreparedBodyPresentation> anthrosonaePaletteSnapshots = new List<PreparedBodyPresentation>();
        private PaletteMutationCoordinator anthrosonaePaletteCoordinator;
        internal bool SuppressesAnthrosonaePaletteRefresh => anthrosonaePaletteCoordinator?.SuppressesGenericForcedRefresh == true;
        internal PaletteMutationCoordinator GetAnthrosonaePaletteCoordinator()
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Anthrosonae palette coordinator requires game thread.");
            return anthrosonaePaletteCoordinator ?? (anthrosonaePaletteCoordinator = new PaletteMutationCoordinator(
                () => UnityData.IsInMainThread, succeeded => {
                    var pawn = parent as Pawn;
                    if (pawn != null && !pawn.Destroyed) PawnPresentationOwner.Prepare(pawn);
                }));
        }
        internal PreparedBodyPresentation FindAnthrosonaePaletteSnapshot(PresentationInputs inputs)
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Anthrosonae palette cache requires game thread.");
            if (inputs.Appearance == null || (preparedPresentation != null && !preparedPresentation.Inputs.PaletteContextEquals(inputs)))
            { anthrosonaePaletteSnapshots.Clear(); return null; }
            foreach (var snapshot in anthrosonaePaletteSnapshots)
                if (snapshot.Inputs.Equals(inputs)) return snapshot;
            return null;
        }
        public PreparedBodyPresentation PreparedPresentation => preparedPresentation;
        internal void PublishPresentation(PreparedBodyPresentation value)
        {
            if (value.Inputs.Appearance != null)
            {
                anthrosonaePaletteSnapshots.Remove(value);
                anthrosonaePaletteSnapshots.Insert(0, value);
                if (anthrosonaePaletteSnapshots.Count > 3) anthrosonaePaletteSnapshots.RemoveAt(3);
            }
            else anthrosonaePaletteSnapshots.Clear();
            preparedPresentation = value;
        }

        internal void RestoreOrObserveNativeBody()
        {
            var pawn = parent as Pawn;
            if (pawn?.story?.bodyType == null) return;
            if (BodyTypeUtility.IsRRBody(pawn.story.bodyType))
            {
                // Only restore a known RimRound projection. Never undo an external
                // native-body change; it becomes the new captured identity.
                var native = rrNativeBodyDefName == null ? null : DefDatabase<BodyTypeDef>.GetNamedSilentFail(rrNativeBodyDefName);
                if (native != null && !BodyTypeUtility.IsRRBody(native)) pawn.story.bodyType = native;
            }
            else if (pawn.story.bodyType.defName != rrNativeBodyDefName)
                ObserveExternalNativeBody(pawn.story.bodyType);
        }

        public string RequestedProfileId => rrRequestedProfileId;
        public string RequestedShapeId => rrRequestedShapeId;
        public string PresentationMode => rrPresentationMode ?? "default";
        public bool OverrideXenotypeArtwork => rrOverrideXenotypeArtwork;
        public string NativeBodyDefName => rrNativeBodyDefName;
        public string NativeBodyProvenance => rrNativeBodyProvenance;
        public long PresentationStateRevision => presentationStateRevision;

        /// <summary>Call at pawn generation before stage selection; safe to repeat at spawn.</summary>
        public void PrepareForGeneration()
        {
            EnsurePresentationState();
            ObserveCurrentNativeBody("captured");
        }

        private void EnsurePresentationState()
        {
            EnsureBodyArchetypeAssigned();
            PresentationSavedState.Migrate(ref rrPresentationSchema, ref rrRequestedProfileId,
                ref rrRequestedShapeId, ref rrPresentationMode, bodyTypeDictNameString,
                _bodyarchetype.ToString());
        }

        public void SetPresentationRequest(string mode, string profileId, string shapeId)
        {
            if (PresentationSavedState.NormalMode(mode) == null)
                throw new ArgumentException("Unknown presentation mode.", nameof(mode));
            rrPresentationSchema = 1;
            rrPresentationMode = mode;
            rrRequestedProfileId = profileId;
            rrRequestedShapeId = shapeId;
            presentationStateRevision++;
            PawnPresentationOwner.Prepare(parent as Pawn);
        }

        public void SetOverrideXenotypeArtwork(bool enabled)
        {
            if (rrOverrideXenotypeArtwork == enabled) return;
            rrOverrideXenotypeArtwork = enabled;
            presentationStateRevision++;
            PawnPresentationOwner.Prepare(parent as Pawn);
        }

        public string EffectiveRequestedProfileId(bool male)
        {
            return male ? PresentationSavedState.EffectiveMaleProfileId(rrRequestedProfileId,
                GlobalSettings.useLegacyMaleSprites, GlobalSettings.useAltMaleSprites) : rrRequestedProfileId;
        }

        public string EffectiveRequestedShapeId => PresentationSavedState.EffectiveShapeId(
            rrRequestedShapeId, GlobalSettings.onlyUseStandardBodyType);

        public bool CaptureNativeBodyBeforeProjection(BodyTypeDef candidate)
        {
            if (rrNativeBodyDefName != null) return false;
            return StoreNativeBody(candidate, true, "captured");
        }

        public bool ObserveExternalNativeBody(BodyTypeDef candidate)
        {
            return StoreNativeBody(candidate, true, "observed-native");
        }

        public bool TryInferLegacyNativeBody(BodyTypeDef candidate, bool raceValid)
        {
            if (rrNativeBodyDefName != null) return false;
            return StoreNativeBody(candidate, raceValid, "legacy-inferred");
        }

        private bool StoreNativeBody(BodyTypeDef candidate, bool raceValid, string source)
        {
            if (candidate == null || !NativeBodySavedState.TryCapture(ref rrNativeBodyDefName,
                ref rrNativeBodyProvenance, candidate.defName, BodyTypeUtility.IsRRBody(candidate), raceValid, source))
                return false;
            presentationStateRevision++;
            return true;
        }

        private void ObserveCurrentNativeBody(string source)
        {
            var candidate = (parent as Pawn)?.story?.bodyType;
            if (candidate == null || BodyTypeUtility.IsRRBody(candidate) ||
                rrNativeBodyDefName != null) return;
            StoreNativeBody(candidate, true, source);
        }

        public int ticksSinceLastBodyChange = 0;
        public readonly int numberOfTicksCooldownPerChange = 250;

        ExemptionReason categoricallyExempt = false;
        public ExemptionReason CategoricallyExempt
        {
            get => categoricallyExempt;
            set
            {
                categoricallyExempt = value;
            }
        }



        ExemptionReason personallyExempt = new ExemptionReason();
        public ExemptionReason PersonallyExempt 
        { 
            get => personallyExempt; 
            set 
            {
                personallyExempt = value;
                BodyTypeUtility.UpdatePawnSprite((Pawn)parent, PersonallyExempt, categoricallyExempt, true, true);
            } 
        }

        readonly PersonalDynamicBodyGizmo dynamicBodyExcemptionGizmo;

        readonly CustomBodyTypeGizmo customBodyTypeGizmo;

        public bool usingCustomBodyMeshSize = false;

        BodyArchetype _bodyarchetype = BodyArchetype.none;
        private void EnsureBodyArchetypeAssigned()
        {
            if (_bodyarchetype != BodyArchetype.none) return;
            if (rrRequestedShapeId == "standard") _bodyarchetype = BodyArchetype.standard;
            else if (rrRequestedShapeId == "apple") _bodyarchetype = BodyArchetype.apple;
            else _bodyarchetype = Values.RandomChanceAtOrBelow(0.5f)
                ? BodyArchetype.standard : BodyArchetype.apple;
        }

        public BodyArchetype BodyArchetype 
        {
            get 
            {
                // Render and utility getters may run off the game thread.
                return _bodyarchetype == BodyArchetype.none ? BodyArchetype.standard : _bodyarchetype;
            }
            set 
            {
                _bodyarchetype = value;
                if (value == BodyArchetype.standard || value == BodyArchetype.apple)
                {
                    rrRequestedShapeId = value.ToString();
                    presentationStateRevision++;
                }
            }
        }
    }

    // Converts to true if pawn is exempt. This is inddicated by there being a given reason that they are exempt.
    public class ExemptionReason
    {
        public ExemptionReason() 
        {
            this.reason = null;
        }

        public ExemptionReason(string reason) 
        {
            this.reason = reason;
        }

        public static implicit operator ExemptionReason(bool b) => b ? new ExemptionReason("other reason") : new ExemptionReason();
        public static implicit operator bool(ExemptionReason e) => !(e.reason is null);
        
        public string reason;
    }




    public enum BodyArchetype 
    {
        none,
        standard,
        apple,
    }
}
