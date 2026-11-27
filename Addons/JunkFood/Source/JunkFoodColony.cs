using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Oinky.JunkFood
{
    public sealed class CravingPawnRecord : IExposable
    {
        public Pawn pawn;
        public CravingState state = new CravingState();
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            if (state == null) state = new CravingState();
            Scribe_Values.Look(ref state.Habit, "habit", false);
            Scribe_Values.Look(ref state.LastUsefulTick, "lastUsefulTick", -1);
            Scribe_Values.Look(ref state.LastQualifyingTick, "lastQualifyingTick", -1);
            Scribe_Collections.Look(ref state.Days, "days", LookMode.Value);
            Scribe_Collections.Look(ref state.Nutrition, "nutrition", LookMode.Value);
        }
    }

    public sealed partial class JunkFoodGameComponent : GameComponent
    {
        private bool contentEnabled = true;
        private bool cravingsEnabled = true;
        private List<CravingPawnRecord> records = new List<CravingPawnRecord>();
        private Dictionary<Pawn, CravingPawnRecord> index = new Dictionary<Pawn, CravingPawnRecord>();
        public bool ContentEnabled => contentEnabled;
        public bool CravingsEnabled => cravingsEnabled;
        private static int Now => Find.TickManager?.TicksGame ?? 0;

        public JunkFoodGameComponent(Game game) { }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref contentEnabled, "Oinky_JunkFoodEnabled", true);
            Scribe_Values.Look(ref cravingsEnabled, "Oinky_MildCravingsEnabled", true);
            Scribe_Collections.Look(ref records, "Oinky_JunkFoodHabitRecords", LookMode.Deep);
            ExposeSharedData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) RebuildIndex();
        }

        public override void FinalizeInit()
        {
            JunkFoodBootstrap.EnsureSubscribed();
            RebuildIndex();
            if (!contentEnabled || !cravingsEnabled) ClearHistories();
            LegacyJunkFoodMigration.SweepLoadedPawns();
            InitializeSharedMode();
        }

        private void RebuildIndex()
        {
            if (records == null) records = new List<CravingPawnRecord>();
            index = new Dictionary<Pawn, CravingPawnRecord>();
            for (int i = records.Count - 1; i >= 0; i--)
            {
                CravingPawnRecord row = records[i];
                if (row?.pawn == null || row.pawn.Dead || index.ContainsKey(row.pawn))
                { records.RemoveAt(i); continue; }
                if (row.state == null) row.state = new CravingState();
                row.state.Normalize(Now);
                index.Add(row.pawn, row);
            }
        }

        public void SetSettings(bool enableContent, bool enableCravings)
        {
            bool wasActive = contentEnabled && cravingsEnabled;
            contentEnabled = enableContent;
            cravingsEnabled = enableCravings;
            if (!contentEnabled || !cravingsEnabled || !wasActive) ClearHistories();
            ReconcileSharedSettings();
        }

        private void ClearHistories()
        {
            foreach (CravingPawnRecord row in records) DirtyThoughts(row.pawn);
            records.Clear();
            index.Clear();
        }

        public void Record(Pawn pawn, double nutrition)
        {
            if (SharedMode || !contentEnabled || !cravingsEnabled || !JunkFoodPolicy.EligibleAdult(pawn) ||
                nutrition <= 0 || double.IsNaN(nutrition) || double.IsInfinity(nutrition)) return;
            CravingPawnRecord row;
            if (!index.TryGetValue(pawn, out row))
            {
                row = new CravingPawnRecord { pawn = pawn };
                records.Add(row);
                index.Add(pawn, row);
            }
            if (row.state.Record(Now, nutrition)) DirtyThoughts(pawn);
        }

        public bool HasCraving(Pawn pawn)
        {
            CravingPawnRecord row;
            return !SharedMode && contentEnabled && cravingsEnabled && JunkFoodPolicy.EligibleAdult(pawn) &&
                index.TryGetValue(pawn, out row) && row.state.IsCraving(Now);
        }

        public override void GameComponentTick()
        {
            TickShared();
            if (Now % 2500 != 0 || records.Count == 0) return;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                CravingPawnRecord row = records[i];
                row.state.Normalize(Now);
                if (!JunkFoodPolicy.EligibleAdult(row.pawn) || row.state.LastUsefulTick < 0)
                {
                    DirtyThoughts(row.pawn);
                    index.Remove(row.pawn);
                    records.RemoveAt(i);
                }
            }
        }

        internal static void DirtyThoughts(Pawn pawn) =>
            pawn?.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty();
    }

    public class ThoughtWorker_JunkFoodCraving : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn) =>
            JunkFoodPolicy.CravingsEnabled && JunkFoodPolicy.Colony.HasCraving(pawn)
                ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
    }
}
