using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimRound.Comps;
using RimRound.Utilities;
using RimWorld;
using Verse;

namespace RimRound.Ideology
{
    public sealed class BodyRoleIdealExtension : DefModExtension
    {
        public float baselineRatio;
        public bool fantasyOnly;
    }

    public sealed class BodyRoleExpectationRecord : IExposable
    {
        public Precept_Role role;
        public Pawn holder;
        public PreceptDef ideal;
        public int appointedAt;
        public float baselineRatio;
        public void ExposeData()
        {
            Scribe_References.Look(ref role, "role");
            Scribe_References.Look(ref holder, "holder");
            Scribe_Defs.Look(ref ideal, "ideal");
            Scribe_Values.Look(ref appointedAt, "appointedAt", -1);
            Scribe_Values.Look(ref baselineRatio, "baselineRatio", 0f);
        }
    }

    // Saved records refer only to currently occupied, relevant native roles.
    // Reconciliation releases former holders; there is no static pawn registry.
    public sealed class BodyRoleExpectations : GameComponent
    {
        public const int GraceTicks = 5 * 60000;
        private int schema = 1;
        private List<BodyRoleExpectationRecord> records = new List<BodyRoleExpectationRecord>();
        public BodyRoleExpectations(Game game) { }
        public static BodyRoleExpectations Instance => Current.Game?.GetComponent<BodyRoleExpectations>();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref schema, "rrRoleExpectationSchema", 1);
            Scribe_Collections.Look(ref records, "rrRoleExpectations", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && schema == 1 && records == null)
                records = new List<BodyRoleExpectationRecord>();
        }
        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 2500 == 0) Reconcile();
        }
        public override void StartedNewGame() { Reconcile(); }
        public static bool IsRelevantRole(Precept_Role role) => ModsConfig.IdeologyActive && role != null &&
            (role.def.leaderRole || role.def == PreceptDefOf.IdeoRole_Moralist);

        public static PreceptDef IdealFor(Ideo ideo)
        {
            if (!ModsConfig.IdeologyActive || ideo == null) return null;
            PreceptDef found = null;
            float largest = 0f;
            foreach (Precept precept in ideo.PreceptsListForReading)
            {
                var ideal = precept.def.GetModExtension<BodyRoleIdealExtension>();
                if (ideal != null && !float.IsNaN(ideal.baselineRatio) && !float.IsInfinity(ideal.baselineRatio) &&
                    ideal.baselineRatio > largest && ideal.baselineRatio <= 10000f)
                { found = precept.def; largest = ideal.baselineRatio; }
            }
            return found;
        }
        public static bool IdealActive(PreceptDef ideal) => ideal != null &&
            (ideal.GetModExtension<BodyRoleIdealExtension>()?.fantasyOnly != true || ColonyProgression.FantasyActive);

        public static bool AppointmentAllowed(Pawn pawn, Precept_Role role)
        {
            if (!IsRelevantRole(role) || !IdealActive(IdealFor(role.ideo)) || pawn == null || role.IsAssigned(pawn)) return true;
            var attitude = pawn.TryGetComp<ThingComp_PawnAttitude>();
            return BodyModelEligibility.HasAdultBiology(pawn) && attitude?.HasPreference == true &&
                attitude.weightOpinion >= WeightOpinion.Like && attitude.weightOpinion <= WeightOpinion.Extreme;
        }

        public void Reconcile()
        {
            if (schema != 1 || !ModsConfig.IdeologyActive || Find.IdeoManager == null || records == null) return;
            var occupied = new HashSet<Precept_Role>();
            foreach (Ideo ideo in Find.IdeoManager.IdeosListForReading)
            {
                PreceptDef ideal = IdealFor(ideo);
                if (!IdealActive(ideal)) continue;
                foreach (Precept precept in ideo.PreceptsListForReading)
                {
                    var role = precept as Precept_Role;
                    if (!IsRelevantRole(role)) continue;
                    Pawn holder = role.ChosenPawnSingle();
                    if (holder == null || holder.Dead || holder.Destroyed || holder.Ideo != ideo || !role.Active) continue;
                    occupied.Add(role);
                    float ratio = ideal.GetModExtension<BodyRoleIdealExtension>().baselineRatio;
                    BodyRoleExpectationRecord record = records.Find(r => r?.role == role);
                    if (record == null)
                    {
                        record = new BodyRoleExpectationRecord { role = role };
                        records.Add(record);
                    }
                    if (record.holder != holder || record.ideal != ideal || record.baselineRatio != ratio)
                    {
                        record.holder = holder;
                        record.ideal = ideal;
                        record.baselineRatio = ratio;
                        record.appointedAt = Find.TickManager.TicksGame;
                    }
                }
            }
            records.RemoveAll(r => r == null || !occupied.Contains(r.role));
        }

        public BodyRoleExpectationRecord For(Pawn pawn)
        {
            if (schema != 1 || pawn == null || records == null) return null;
            return records.Find(r => r?.holder == pawn && StillApplies(r));
        }
        private static bool StillApplies(BodyRoleExpectationRecord record) => record?.holder != null &&
            !record.holder.Dead && !record.holder.Destroyed && BodyModelEligibility.HasAdultBiology(record.holder) &&
            IsRelevantRole(record.role) && record.role.Active &&
            record.role.ideo.PreceptsListForReading.Contains(record.role) &&
            record.role.IsAssigned(record.holder) && record.holder.Ideo == record.role.ideo &&
            record.ideal == IdealFor(record.role.ideo) && IdealActive(record.ideal);

        public static float ExpectedMass(BodyRoleExpectationRecord record)
        {
            float baseline = record?.holder?.TryGetComp<FullnessAndDietStats_ThingComp>()?.BaselineBodyMassKg ?? 0f;
            return baseline > 0f && record.baselineRatio > 0f ? baseline * record.baselineRatio : 0f;
        }
        public int UnmetRoleCount(Pawn follower)
        {
            if (schema != 1 || !ModsConfig.IdeologyActive || records == null || follower?.Ideo == null ||
                !BodyModelEligibility.HasColonyManagementScope(follower)) return 0;
            bool leader = false, guide = false;
            int now = Find.TickManager.TicksGame;
            foreach (var record in records)
            {
                if (!StillApplies(record) || record.role.ideo != follower.Ideo || record.appointedAt < 0 ||
                    (long)now - record.appointedAt < GraceTicks) continue;
                float expected = ExpectedMass(record);
                if (expected <= 0f || record.holder.WeightHediff() == null || record.holder.Weight() + .001f >= expected) continue;
                if (record.role.def.leaderRole) leader = true; else guide = true;
            }
            return (leader ? 1 : 0) + (guide ? 1 : 0);
        }

        public static string Description(Pawn pawn)
        {
            var record = Instance?.For(pawn);
            if (record == null) return null;
            float days = Math.Max(0f, (GraceTicks - ((Find.TickManager?.TicksGame ?? 0) - (long)record.appointedAt)) / 60000f);
            return "RR_BodyRoleExpectation".Translate(record.role.LabelForPawn(pawn),
                ExpectedMass(record).ToString("0.#"), days.ToString("0.0"));
        }
    }

    public sealed class RoleRequirement_BodyPreference : RoleRequirement
    {
        public static readonly RoleRequirement_BodyPreference Instance = new RoleRequirement_BodyPreference();
        public override bool Met(Pawn p, Precept_Role role) => BodyRoleExpectations.AppointmentAllowed(p, role);
        public override string GetLabel(Precept_Role role) => "RR_BodyRoleRequiresPreference".Translate();
    }

    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.RequirementsMet))]
    internal static class BodyRoleRequirements
    {
        private static void Postfix(Precept_Role __instance, Pawn p, ref bool __result)
        { if (__result) __result = BodyRoleExpectations.AppointmentAllowed(p, __instance); }
    }
    [HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.GetFirstUnmetRequirement))]
    internal static class BodyRoleRequirementReason
    {
        private static void Postfix(Precept_Role __instance, Pawn p, ref RoleRequirement __result)
        {
            if (__result == null && !BodyRoleExpectations.AppointmentAllowed(p, __instance))
                __result = RoleRequirement_BodyPreference.Instance;
        }
    }
    [HarmonyPatch(typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.Assign))]
    internal static class BodyRoleAssignment
    {
        private static bool Prefix(Precept_RoleSingle __instance, Pawn p)
        {
            if (BodyRoleExpectations.AppointmentAllowed(p, __instance)) return true;
            Messages.Message("RR_BodyRoleRequiresPreference".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }
        private static void Postfix() { BodyRoleExpectations.Instance?.Reconcile(); }
    }
    public sealed class ThoughtWorker_BodyRoleExpectation : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            int missing = BodyRoleExpectations.Instance?.UnmetRoleCount(p) ?? 0;
            return missing > 0 ? ThoughtState.ActiveAtStage(Math.Min(2, missing) - 1) : ThoughtState.Inactive;
        }
    }
}
