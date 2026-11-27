using RimRound.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.Sound;

namespace RimRound.Comps
{
    public partial class ThingComp_PawnAttitude : ThingComp
    {
        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Values.Look<WeightOpinion>(ref weightOpinion, "weightOpinion");
            Scribe_Values.Look<float?>(ref gainingResistance, "gainingResistance");
            ExposePreference();
            ExposeSocialBodyTaste();
            ExposeSocialState();
            ExposeRoleAssistance();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in GetBodyDiscussionGizmos()) yield return gizmo;
            if (HasPreference && BodyModelEligibility.HasColonyManagementScope(parent as Pawn))
                yield return new Command_Action {
                    defaultLabel = "RR_PreferenceTitle".Translate(),
                    defaultDesc = PreferenceDescription,
                    icon = Resources.INCREASE_WEIGHT_OPINION_ICON,
                    action = () => Find.WindowStack.Add(new RimRound.UI.Dialog_BodyPreference((Pawn)parent))
                };
            if (Prefs.DevMode && DebugSettings.godMode && HasPreference)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Increase Weight Opinion",
                    icon = Resources.INCREASE_WEIGHT_OPINION_ICON,
                    action = delegate ()
                    {
                        Resources.gizmoClick.PlayOneShotOnCamera(null);
                        Debug_IncreaseOpinion();
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "Decrease Weight Opinion",
                    icon = Resources.DECREASE_WEIGHT_OPINION_ICON,
                    action = delegate ()
                    {
                        Resources.gizmoClick.PlayOneShotOnCamera(null);
                        Debug_LowerOpinion();
                    }
                };

            }
        }

        public WeightOpinion Debug_IncreaseOpinion()
        {
            SetWeightOpinion((WeightOpinion)Math.Min(9, (int)weightOpinion + 1));
            return weightOpinion;
        }

        public WeightOpinion Debug_LowerOpinion()
        {
            SetWeightOpinion((WeightOpinion)Math.Max(1, (int)weightOpinion - 1));
            return weightOpinion;
        }

        public void SetWeightOpinion(WeightOpinion weightOpinion) 
        {
            InitializePreference();
            if (preference.TrySetOpinion((int)weightOpinion)) ProjectPreference();
        }

        public WeightOpinion weightOpinion = WeightOpinion.None;


        private float? gainingResistance = null;

        public float GainingResistance 
        {
            get 
            {
                if (gainingResistance is null)
                {
                    gainingResistance = GetAlteredResistanceBasedOnWeightOpinion(this.parent.AsPawn()?.guest?.resistance ?? 999);
                }

                return gainingResistance.Value;
            }
            set 
            {
                gainingResistance = value;
            }
        }

        float GetAlteredResistanceBasedOnWeightOpinion(float gainingResistance) 
        {
            switch (weightOpinion) 
            {
                case WeightOpinion.None:
                    return gainingResistance;
                case WeightOpinion.Hate:
                    return gainingResistance * 3.5f;
                case WeightOpinion.Dislike:
                    return gainingResistance * 2.25f;
                case WeightOpinion.NeutralMinus:
                    return gainingResistance * 1.5f;
                case WeightOpinion.Neutral:
                    return gainingResistance * 1.1f;
                case WeightOpinion.NeutralPlus:
                    return gainingResistance * 0.7f;
                case WeightOpinion.Like:
                    return gainingResistance * 0.4f;
                case WeightOpinion.Love:
                    return gainingResistance * 0.1f;
                case WeightOpinion.Fanatical:
                    return gainingResistance * 0.0f;
                default:
                    return gainingResistance;
            }
        }

        //----------------



        void UpdateWeightOpinion() 
        {
            WeightOpinion weightOpinion = WeightOpinion.None;

            foreach (var p in WeightOpinionUtility.reluctanceGap) 
            {
                if (WeightOpinionFloat < p.Second)
                {
                    weightOpinion = p.First;
                    break;
                }
            }

            SetWeightOpinion(weightOpinion);
        }

        float _weightOpinionFloatInt;

        public float WeightOpinionFloat 
        {
            get
            {
                return _weightOpinionFloatInt;
            }
            set 
            {
                _weightOpinionFloatInt = value;

                UpdateWeightOpinion();
            } 
        }

        public float WeightOpinionGainResistance { get; set; }
        public float WeightOpinionLossResistance { get; set; }

    }
}
