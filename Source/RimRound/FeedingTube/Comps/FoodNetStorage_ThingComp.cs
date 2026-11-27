using RimRound.FeedingTube.Utilities;
using System;
using RimRound.Utilities;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimRound.FeedingTube.Comps
{
    //Battery bar drawing happens here?
    public class FoodNetStorage_ThingComp : FoodTransmitter_ThingComp
    {
        public float Stored
        {
            get => storedFood;
            set
            {
                if (Finite(value) && value >= 0f)
                {
                    if (!suppressPreservationWrite && !V2PreservationProjection &&
                        value != storedFood)
                    {
                        float beforeAging = storedFood;
                        AgePreservationToNow();
                        // A caller may have computed this assignment from the
                        // old volume. Do not restore food just spoiled during
                        // the availability check.
                        if (beforeAging > 0f && storedFood == 0f && value > 0f)
                            return;
                        if (value > storedFood)
                            MergePreservation(FoodPreservationStamp.LegacyGrace);
                        else if (storedFood > 0f &&
                            PositiveFinite(FullnessToNutritionRatio))
                        {
                            double beforeVolume = PreciseVolume;
                            double removed = Math.Max(0d, beforeVolume - value);
                            purgedFullness += removed;
                            purgedNutrition += beforeVolume > 0d ? PreciseNutrition * (removed / beforeVolume) : 0d;
                        }
                    }
                    storedFood = value;
                    ResetPrecision();
                    if (value == 0f && !suppressPreservationWrite &&
                        !V2PreservationProjection)
                    {
                        preservation = FoodPreservationStamp.Fresh;
                        preservedIngredients.Clear();
                    }
                }
            }
        }

        public float Capacity { get => this.Props.capacity; }

        public float Remaining 
        { 
            get 
            { 
                return PositiveFinite(Capacity) && Finite(Stored) && Stored >= 0f
                    ? Math.Max(0f, Capacity - Stored) : 0f;
            } 
        }


        float _fullnessToNutritionRatio = 1;
        public float FullnessToNutritionRatio
        {
            get
            {
                return _fullnessToNutritionRatio;
            }
            set
            {
                if (!PositiveFinite(value))
                {
                    Log.Warning("Set ftnRatio to illegal value");
                    return;
                }
                if (_fullnessToNutritionRatio != value)
                {
                    _fullnessToNutritionRatio = value;
                    ResetPrecision();
                }
            }
        }


        public new FoodNetStorage_CompProperties Props => (FoodNetStorage_CompProperties)this.props;

        public override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);
            // Breakdown stops cooling; it does not destroy stored food.
        }

        public bool V2PreservationProjection { get; set; }
        public float RemainingRotTicks => ReadPreservation().RemainingRotTicks;
        public float PoisonFraction => ReadPreservation().PoisonFraction;
        public double SpoiledNutrition => spoiledNutrition;
        public double SpoiledFullness => spoiledFullness;
        public double PurgedNutrition => purgedNutrition;
        public double PurgedFullness => purgedFullness;

        // Residuals refine the existing saved volume/ratio; they are not a
        // second stock owner. External legacy writes invalidate both together.
        private double nutritionResidual, volumeResidual;
        private float precisionVolumeProjection, precisionRatioProjection = 1f;
        private int sourceCreatedTick;
        private bool preciseAmountsInitialized;
        private bool HasPreciseProjection => storedFood == precisionVolumeProjection &&
            _fullnessToNutritionRatio == precisionRatioProjection;
        public double PreciseVolume => (double)storedFood + (HasPreciseProjection ? volumeResidual : 0d);
        public double PreciseNutrition => PositiveFinite(_fullnessToNutritionRatio)
            ? (double)storedFood / _fullnessToNutritionRatio + (HasPreciseProjection ? nutritionResidual : 0d) : 0d;
        public int SourceCreatedTick => sourceCreatedTick;
        public bool HasPreciseAmounts => preciseAmountsInitialized && HasPreciseProjection;

        private void ResetPrecision()
        {
            nutritionResidual = volumeResidual = 0d;
            preciseAmountsInitialized = false;
            precisionVolumeProjection = storedFood;
            precisionRatioProjection = _fullnessToNutritionRatio;
            if (storedFood <= 0f) sourceCreatedTick = 0;
        }

        public bool SetPreciseAmounts(double nutrition, double volume, int createdTick)
        {
            if (!FoodServingMath.Finite(nutrition) || !FoodServingMath.Finite(volume) ||
                nutrition < 0d || volume < 0d || ((nutrition == 0d) != (volume == 0d)) ||
                volume > Capacity && !FoodServingMath.Same(volume, Capacity)) return false;
            float ratio = nutrition > 0d ? (float)(volume / nutrition) : 1f;
            if (!PositiveFinite(ratio) || nutrition > float.MaxValue || volume > float.MaxValue) return false;
            storedFood = (float)volume;
            _fullnessToNutritionRatio = ratio;
            precisionVolumeProjection = storedFood;
            precisionRatioProjection = ratio;
            volumeResidual = volume - storedFood;
            nutritionResidual = nutrition - (double)storedFood / ratio;
            sourceCreatedTick = nutrition > 0d ? createdTick : 0;
            preciseAmountsInitialized = true;
            return true;
        }

        public sealed class PreciseState
        {
            internal float Volume, Ratio;
            internal double NutritionResidual, VolumeResidual;
            internal int CreatedTick, PreservationTick;
            internal bool Initialized;
            internal FoodPreservationStamp Preservation;
        }
        public PreciseState CapturePreciseState()
        {
            FoodPreservationStamp stamp = ReadPreservation();
            return new PreciseState { Volume = storedFood, Ratio = _fullnessToNutritionRatio,
                NutritionResidual = HasPreciseProjection ? nutritionResidual : 0d,
                VolumeResidual = HasPreciseProjection ? volumeResidual : 0d,
                CreatedTick = sourceCreatedTick, PreservationTick = preservationLastTick, Preservation = stamp,
                Initialized = HasPreciseAmounts };
        }
        public void RestorePreciseState(PreciseState state)
        {
            storedFood = precisionVolumeProjection = state.Volume;
            _fullnessToNutritionRatio = precisionRatioProjection = state.Ratio;
            nutritionResidual = state.NutritionResidual; volumeResidual = state.VolumeResidual;
            sourceCreatedTick = state.CreatedTick;
            preciseAmountsInitialized = state.Initialized;
            SetPreservation(state.Preservation); preservationLastTick = state.PreservationTick;
        }

        public FoodPreservationStamp ReadPreservation()
        {
            AgePreservationToNow();
            return CurrentPreservation();
        }

        // Gravship buildings are held in a deep-saved dictionary without a
        // ThingOwner. The caller supplies their native world-tile temperature.
        public FoodPreservationStamp ReadPreservationAtTemperature(float temperature)
        {
            AgePreservationToNow(temperature);
            return CurrentPreservation();
        }

        private FoodPreservationStamp CurrentPreservation()
        {
            FoodPreservationStamp result = preservationInitialized && PositiveFinite(Stored)
                ? preservation : FoodPreservationStamp.Fresh;
            result.Ingredients = preservedIngredients == null
                ? new List<ThingDef>() : new List<ThingDef>(preservedIngredients);
            return result.WithoutPoison;
        }

        public void SetPreservation(FoodPreservationStamp stamp)
        {
            if (!stamp.Valid)
                return;
            stamp = stamp.WithoutPoison;
            preservation = stamp;
            preservedIngredients = stamp.Ingredients == null
                ? new List<ThingDef>()
                : stamp.Ingredients.Where(d => d != null).Distinct().ToList();
            preservationInitialized = true;
            preservationLastTick = Find.TickManager?.TicksGame ?? 0;
        }

        public void RestoreTransactionState(float volume, float ratio,
            FoodPreservationStamp stamp)
        {
            SetVolumeForTransaction(volume);
            FullnessToNutritionRatio = ratio;
            SetPreservation(stamp);
        }

        public void SetVolumeForTransaction(float volume)
        {
            suppressPreservationWrite = true;
            try { Stored = volume; }
            finally { suppressPreservationWrite = false; }
        }

        public void MergePreservation(FoodPreservationStamp incoming)
        {
            if (!incoming.Valid)
                return;
            AgePreservationToNow();
            preservation = preservationInitialized && storedFood > 0f
                ? FoodPreservationStamp.Combine(ReadPreservation(), incoming)
                : incoming;
            preservedIngredients = preservation.Ingredients == null
                ? new List<ThingDef>()
                : preservation.Ingredients.Where(d => d != null).Distinct().ToList();
            preservationInitialized = true;
            preservationLastTick = Find.TickManager?.TicksGame ?? 0;
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            AgePreservationToNow();
        }

        public override void CompTick()
        {
            base.CompTick();
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now > 0 && now % 250 == 0)
                AgePreservationToNow();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            AgePreservationToNow();
            base.PostDeSpawn(map, mode);
        }

        private void AgePreservationToNow()
        {
            AgePreservationToNow(null);
        }

        private void AgePreservationToNow(float? temperatureOverride)
        {
            if (V2PreservationProjection)
                return;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!preservationInitialized)
            {
                preservation = FoodPreservationStamp.LegacyGrace;
                preservationInitialized = true;
                preservationLastTick = now;
                return;
            }
            if (preservationLastTick <= 0 || now <= preservationLastTick)
            {
                preservationLastTick = now;
                PurgeExpiredStored();
                return;
            }
            int elapsed = now - preservationLastTick;
            preservationLastTick = now;
            if (!PositiveFinite(storedFood) || !preservation.Valid)
                return;
            if (!CoolingActive())
            {
                // Native Thing.AmbientTemperature resolves held/packed things
                // through their holder or tile where available.
                float temperature = temperatureOverride ??
                    (parent == null ? 21f : parent.AmbientTemperature);
                float rate = GenTemperature.RotRateAtTemperature(temperature);
                if (Finite(rate) && rate > 0f)
                    preservation = preservation.Aged(elapsed * rate);
            }
            PurgeExpiredStored();
        }

        private void PurgeExpiredStored()
        {
            if (!PositiveFinite(storedFood) || !preservation.Valid ||
                preservation.RemainingRotTicks > 0f)
                return;
            spoiledFullness += PreciseVolume;
            if (PositiveFinite(FullnessToNutritionRatio))
                spoiledNutrition += PreciseNutrition;
            suppressPreservationWrite = true;
            try { Stored = 0f; }
            finally { suppressPreservationWrite = false; }
        }

        private bool CoolingActive()
        {
            if (parent == null || !parent.Spawned)
                return false;
            CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
            CompFlickable flick = parent.TryGetComp<CompFlickable>();
            CompBreakdownable breakdown = parent.TryGetComp<CompBreakdownable>();
            return power != null && power.PowerOn && flick != null &&
                flick.SwitchIsOn && breakdown != null &&
                !breakdown.BrokenDown;
        }

        public override string CompInspectStringExtra()
        {
            ReadPreservation();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RR_LiquidTankAmount".Translate(
                Stored.ToString("F1"), Capacity.ToString("F1"),
                PreciseNutrition.ToString("F1")));
            if (PositiveFinite(Stored))
                sb.AppendLine("RR_FoodNetworkDensity".Translate(
                    (PreciseNutrition / PreciseVolume).ToString("0.##")));
            if (!V2PreservationProjection && PositiveFinite(Stored))
            {
                FoodPreservationStamp stamp = ReadPreservation();
                float rate = GenTemperature.RotRateAtTemperature(parent.AmbientTemperature);
                sb.AppendLine(CoolingActive()
                    ? "RR_LiquidRefrigerated".Translate().ToString()
                    : rate <= 0f ? "RR_LiquidFrozen".Translate().ToString()
                    : "RR_LiquidNextSpoilage".Translate(
                          stamp.TimeUntilSpoilage(parent.AmbientTemperature)).ToString());
            }
            return sb.ToString().Trim();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEBUG: Fill",
                    action = delegate ()
                    {
                        this.Stored = this.Capacity;
                    }
                };


                yield return new Command_Action
                {
                    defaultLabel = "DEBUG: Empty",
                    action = delegate ()
                    {
                        this.Stored = 0;
                    }
                };

            }
            yield break;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<float>(ref storedFood, "FoodStored", 0f);
            Scribe_Values.Look<float>(ref _fullnessToNutritionRatio, "fullnessToNutritionRatio", 1f);
            Scribe_Values.Look(ref nutritionResidual, "rrNutritionResidual", 0d);
            Scribe_Values.Look(ref volumeResidual, "rrVolumeResidual", 0d);
            Scribe_Values.Look(ref sourceCreatedTick, "rrSourceCreatedTick", 0);
            Scribe_Values.Look(ref preciseAmountsInitialized, "rrPreciseAmountsInitialized", false);
            Scribe_Values.Look(ref preservationInitialized,
                "preservationInitialized", false);
            Scribe_Values.Look(ref preservation.RemainingRotTicks,
                "preservationRemainingRotTicks", 0f);
            Scribe_Values.Look(ref preservation.PoisonFraction,
                "preservationPoisonFraction", 0f);
            Scribe_Values.Look(ref preservation.PoisonCause,
                "preservationPoisonCause", FoodPoisonCause.Unknown);
            Scribe_Values.Look(ref preservation.AssumedFreshLegacy,
                "preservationLegacyGrace", false);
            Scribe_Values.Look(ref preservationLastTick,
                "preservationLastTick", 0);
            Scribe_Collections.Look(ref preservedIngredients,
                "preservationIngredients", LookMode.Def);
            Scribe_Values.Look(ref spoiledNutrition,
                "preservationSpoiledNutrition", 0d);
            Scribe_Values.Look(ref spoiledFullness,
                "preservationSpoiledFullness", 0d);
            Scribe_Values.Look(ref purgedNutrition,
                "preservationPurgedNutrition", 0d);
            Scribe_Values.Look(ref purgedFullness,
                "preservationPurgedFullness", 0d);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                precisionVolumeProjection = storedFood;
                precisionRatioProjection = _fullnessToNutritionRatio;
                if (!FoodServingMath.Finite(nutritionResidual) || !FoodServingMath.Finite(volumeResidual) ||
                    Math.Abs(volumeResidual) > Math.Abs((double)storedFood) * 0.000001d ||
                    Math.Abs(nutritionResidual) > Math.Abs((double)storedFood / _fullnessToNutritionRatio) * 0.000001d)
                    ResetPrecision();
                if (!preservationInitialized)
                    preservation = FoodPreservationStamp.LegacyGrace;
                else if (!preservation.Valid)
                    preservation = new FoodPreservationStamp
                    {
                        RemainingRotTicks = 0f
                    };
                preservationInitialized = true;
                preservedIngredients = preservedIngredients ??
                    new List<ThingDef>();
                if (preservationLastTick <= 0)
                    preservationLastTick = Find.TickManager?.TicksGame ?? 0;
            }
        }


        /// <param name="amount">A positive number that represents the amount of fluid to subtract</param>
        /// <returns>A positive number that returns amount successfully drawn</returns>
        public float Subtract(float amount)
        {
            AgePreservationToNow();
            if (!PositiveFinite(amount) || !FoodServingMath.Positive(PreciseVolume) ||
                !FoodServingMath.Positive(PreciseNutrition)) return 0f;
            float take = FloorToFloat(Math.Min(amount, PreciseVolume));
            double beforeV = PreciseVolume, beforeN = PreciseNutrition;
            if (take <= 0f || !SetPreciseAmounts(beforeN * (1d - take / beforeV),
                beforeV - take, sourceCreatedTick)) return 0f;
            return take;
        }

        public float Add(float volumeToAdd, float ftnRatio) =>
            Add(volumeToAdd, ftnRatio, FoodPreservationStamp.LegacyGrace);

        public float Add(float volumeToAdd, float ftnRatio, FoodPreservationStamp incoming)
        {
            AgePreservationToNow();
            if (!PositiveFinite(volumeToAdd) || !PositiveFinite(ftnRatio) ||
                !incoming.Valid || incoming.RemainingRotTicks <= 0f) return 0f;
            double beforeV = PreciseVolume, beforeN = PreciseNutrition;
            float accepted = FloorToFloat(Math.Min(volumeToAdd, Math.Max(0d, Capacity - beforeV)));
            if (accepted <= 0f) return 0f;
            FoodPreservationStamp stamp = beforeV > 0d
                ? FoodPreservationStamp.Combine(ReadPreservation(), incoming) : incoming;
            int tick = beforeV > 0d ? sourceCreatedTick : Find.TickManager?.TicksGame ?? 0;
            if (!SetPreciseAmounts(beforeN + (double)accepted / ftnRatio,
                beforeV + accepted, tick)) return 0f;
            SetPreservation(stamp);
            return accepted;
        }
        internal static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool PositiveFinite(float value)
        {
            return value > 0f && Finite(value);
        }

        // Requests below the saved float's ULP transfer no food. Rounding the
        // target quantity toward the old value prevents an over-debit.
        internal static float FloorToFloat(double value)
        {
            if (value <= 0d || double.IsNaN(value))
                return 0f;
            if (value >= float.MaxValue)
                return float.MaxValue;
            float result = (float)value;
            if ((double)result > value)
                result = BitConverter.ToSingle(BitConverter.GetBytes(
                    BitConverter.ToInt32(BitConverter.GetBytes(result), 0) - 1), 0);
            return result;
        }

        internal static float CeilToFloat(double value)
        {
            if (value <= 0d || double.IsNaN(value))
                return 0f;
            if (value >= float.MaxValue)
                return float.MaxValue;
            float result = (float)value;
            if ((double)result < value)
                result = BitConverter.ToSingle(BitConverter.GetBytes(
                    BitConverter.ToInt32(BitConverter.GetBytes(result), 0) + 1), 0);
            return result;
        }

        float storedFood;
        private FoodPreservationStamp preservation;
        private List<ThingDef> preservedIngredients = new List<ThingDef>();
        private bool preservationInitialized;
        private int preservationLastTick;
        private bool suppressPreservationWrite;
        private double spoiledNutrition;
        private double spoiledFullness;
        private double purgedNutrition;
        private double purgedFullness;
    }
}
