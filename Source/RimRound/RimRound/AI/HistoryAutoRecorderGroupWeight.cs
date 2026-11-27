using RimRound.AI;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimRound.AI
{
    public class HistoryAutoRecorderGroupWeight : IExposable
    {
        private static readonly ConditionalWeakTable<Game, HistoryAutoRecorderGroupWeight> Groups =
            new ConditionalWeakTable<Game, HistoryAutoRecorderGroupWeight>();
        public static HistoryAutoRecorderGroupWeight Instance() 
        {
            var game = Current.Game;
            if (game == null) throw new InvalidOperationException("Pawn weight history requires an active game.");
            // Pawn references and samples belong to their Game, even when two
            // successive games reuse the same ThingID strings. Old games must
            // not become owners of references written into a later save.
            return Groups.GetValue(game, ignored => new HistoryAutoRecorderGroupWeight {
                def = Defs.HistoryAutoRecorderGroupDefOf.RR_PawnWeightHistory });
        }

        public float GetMaxDay()
        {
            float num = 0f;
            foreach (HistoryAutoRecorderWeight historyAutoRecorder in this.recorders)
            {
                if (!Usable(historyAutoRecorder)) continue;
                int count = historyAutoRecorder.records.Count;
                if (count != 0)
                {
                    float num2 = (count - 1) * historyAutoRecorder.def.recordTicksFrequency / 60000f;
                    if (num2 > num)
                    {
                        num = num2;
                    }
                }
            }
            return num;
        }

        public void Tick()
        {
            for (int i = 0; i < this.recorders.Count; i++)
            {
                if (Usable(this.recorders[i])) this.recorders[i].Tick();
            }
        }

        public void DrawGraph(Rect graphRect, Rect legendRect, FloatRange section, List<CurveMark> marks)
        {
            int ticksGame = Find.TickManager.TicksGame;
            var selected = Find.Selector.SelectedPawns.ToList();
            if (ticksGame != this.cachedGraphTickCount || !selected.SequenceEqual(cachedSelection))
            {
                this.cachedGraphTickCount = ticksGame;
                cachedSelection = selected;
                this.curves.Clear();
                List<HistoryAutoRecorderWeight> weightCurvesToShow = new List<HistoryAutoRecorderWeight>();

                for (int i = 0; i < this.recorders.Count; i++)
                {
                    if (Usable(this.recorders[i]) && selected.Contains(this.recorders[i].pawnToShowInfoFor))
                        weightCurvesToShow.Add(this.recorders[i]);
                }

                foreach (var recorder in weightCurvesToShow)
                {
                    SimpleCurveDrawInfo simpleCurveDrawInfo = new SimpleCurveDrawInfo
                    {
                        color = recorder.def.graphColor,
                        label = recorder.def.LabelCap,
                        valueFormat = recorder.def.valueFormat,
                        curve = new SimpleCurve()
                    };

                    for (int j = 0; j < recorder.records.Count; j++)
                        if (!float.IsNaN(recorder.records[j]) && !float.IsInfinity(recorder.records[j]))
                            simpleCurveDrawInfo.curve.Add(new CurvePoint((float)j * (float)recorder.def.recordTicksFrequency / 60000f, recorder.records[j]), false);

                    simpleCurveDrawInfo.curve.SortPoints();

                    if (recorder.records.Count == 1 && !float.IsNaN(recorder.records[0]) && !float.IsInfinity(recorder.records[0]))
                        simpleCurveDrawInfo.curve.Add(new CurvePoint(1.6666667E-05f, recorder.records[0]), true);

                    this.curves.Add(simpleCurveDrawInfo);
                }
            }

            if (Mathf.Approximately(section.min, section.max))
                section.max += 1.6666667E-05f;

            SimpleCurveDrawerStyle curveDrawerStyle = Find.History.curveDrawerStyle;
            curveDrawerStyle.FixedSection = section;
            curveDrawerStyle.UseFixedScale = this.def.useFixedScale;
            curveDrawerStyle.FixedScale = this.def.fixedScale;
            curveDrawerStyle.YIntegersOnly = this.def.integersOnly;
            curveDrawerStyle.OnlyPositiveValues = this.def.onlyPositiveValues;
            SimpleCurveDrawer.DrawCurves(graphRect, this.curves, curveDrawerStyle, marks, legendRect);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) NormalizeRecorders();
            Scribe_Defs.Look<HistoryAutoRecorderGroupDef>(ref this.def, "def");
            Scribe_Collections.Look<HistoryAutoRecorderWeight>(ref this.recorders, "recorders", LookMode.Deep, Array.Empty<object>());
            if (Scribe.mode == LoadSaveMode.PostLoadInit) NormalizeRecorders();
        }

        private static bool Usable(HistoryAutoRecorderWeight recorder) => recorder != null &&
            recorder.pawnToShowInfoFor != null && recorder.def != null && recorder.records != null;

        private void NormalizeRecorders()
        {
            // Cross references must have resolved before removing orphaned
            // recorders. Preserve every surviving pawn's original samples.
            if (recorders == null) recorders = new List<HistoryAutoRecorderWeight>();
            int removed = recorders.RemoveAll(r => r == null || r.pawnToShowInfoFor == null);
            foreach (var recorder in recorders)
            {
                if (recorder.def == null) recorder.def = Defs.HistoryAutoRecorderDefOf.RR_Weight_Pawn;
                if (recorder.records == null) recorder.records = new List<float>();
            }
            if (def == null) def = Defs.HistoryAutoRecorderGroupDefOf.RR_PawnWeightHistory;
            cachedGraphTickCount = -1;
            cachedSelection.Clear();
            curves.Clear();
            if (removed > 0) Log.Message("[RimRound] Removed " + removed + " orphaned pawn-weight history recorders; surviving samples preserved.");
        }

        public void AddHistoryRecorders(Pawn pawn)
        {
            if (pawn == null) return;
            if (recorders == null) recorders = new List<HistoryAutoRecorderWeight>();
            if (this.recorders.Any(r => r != null && ReferenceEquals(pawn, r.pawnToShowInfoFor)))
                return;

            var recorder = new HistoryAutoRecorderWeight
            {
                pawnToShowInfoFor = pawn,
                def = Defs.HistoryAutoRecorderDefOf.RR_Weight_Pawn
            };

            this.recorders.Add(recorder);
            cachedGraphTickCount = -1;
        }

        public HistoryAutoRecorderGroupDef def;

        public List<HistoryAutoRecorderWeight> recorders = new List<HistoryAutoRecorderWeight>();

        private List<SimpleCurveDrawInfo> curves = new List<SimpleCurveDrawInfo>();

        private int cachedGraphTickCount = -1;
        private List<Pawn> cachedSelection = new List<Pawn>();
    }
}
