using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimRound.Ideology
{
    public static class RbaBuildingReadiness
    {
        public static string UseLabel(AbundanceBedUse use) => ("RR_RbaBedUse" + use).Translate();

        public static IEnumerable<string> Blockers(CompAbundanceBed focus)
        {
            Building_Bed bed = focus?.Bed;
            if (bed?.Spawned != true) { yield return "RR_RbaBedNotSpawned".Translate(); yield break; }
            if (!focus.Matches(focus.Owner)) yield return "RR_AbundanceBedOwnerMismatch".Translate();
            if (bed.Faction != Faction.OfPlayer) yield return "RR_RbaBedNotColony".Translate();
            if (bed.Medical) yield return "RR_RbaBedMedical".Translate();
            if (bed.ForPrisoners) yield return "RR_RbaBedPrisoner".Translate();
            if (bed.IsBurning()) yield return "RR_RbaBedBurning".Translate();
            Room room = bed.GetRoom();
            if (room == null || room.PsychologicallyOutdoors) yield return "RR_RbaBedIndoors".Translate();
            if (!AbundanceBeds.HasTable(bed)) yield return "RR_FeastRequiresTable".Translate();
            if (!AbundanceBeds.ApproachCells(bed).Any()) yield return "RR_RbaBedNoAccess".Translate();
            if (focus.Matches(focus.Owner) && !AbundanceBeds.ReadyForBegin(focus.Owner))
                yield return "RR_RbaBedOwnerNotReady".Translate();
        }

        public static string Inspect(CompAbundanceBed focus)
        {
            if (focus == null || focus.Use == AbundanceBedUse.None) return null;
            var lines = new List<string> {
                "RR_AbundanceBedStatus".Translate(UseLabel(focus.Use), focus.Owner?.LabelShort ?? "None".Translate()).ToString()
            };
            var entry = RbaBuildings.Entry(focus.Ideology);
            if (entry != null && !string.IsNullOrWhiteSpace(entry.Label) && entry.Label != RbaBuildings.DefaultName)
                lines.Add("RR_RbaCeremonialName".Translate(entry.Label));
            if (focus.Bed?.Spawned == true)
            {
                bool table = AbundanceBeds.HasTable(focus.Bed);
                int seats = AbundanceBeds.DiningSeats(focus.Bed.Map, focus.Bed.Position).Distinct().Count();
                lines.Add((table ? "RR_RbaBedHasTable" : "RR_FeastRequiresTable").Translate());
                lines.Add("RR_RbaBedSeats".Translate(seats));
                lines.Add((AbundanceBeds.ApproachCells(focus.Bed).Any() ? "RR_RbaBedHasAccess" : "RR_RbaBedNoAccess").Translate());
            }
            foreach (string blocker in Blockers(focus))
                if (!lines.Contains(blocker)) lines.Add(blocker);
            lines.Add("RR_RbaBedAttendanceCheck".Translate());
            return string.Join("\n", lines);
        }
    }
}
