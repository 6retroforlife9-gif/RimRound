using System;
using System.Linq;
using System.Xml;
using HarmonyLib;
using Verse;

namespace RimRound.FeedOther
{
    internal static class ExportAgencyCompatibility
    {
        internal static void Initialize()
        {
            // Export Agency reads its archive from its startup constructor.
            // Install during Mod construction so its subsequent settings load
            // cannot register a recipe-less Bill.GetUniqueLoadID().
            Type settings = AccessTools.TypeByName("ExportAgency.ExportSettings");
            if (settings == null) return;
            var expose = AccessTools.DeclaredMethod(settings, "ExposeData");
            if (expose == null) return;
            new Harmony("RimRound.ExportAgencyArchiveCompatibility").Patch(expose,
                prefix: new HarmonyMethod(typeof(ExportAgencyCompatibility), nameof(BeforeArchiveLoad)));
        }

        private static void BeforeArchiveLoad()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            int skipped = RemoveEmptyArchivedBills(Scribe.loader?.curXmlParent);
            if (skipped > 0)
                Log.Message("[RimRound] Skipped " + skipped +
                    " empty Export Agency bill entries with no recipe while reading its archive; valid templates preserved.");
        }

        internal static int RemoveEmptyArchivedBills(XmlNode settings)
        {
            if (settings == null) return 0;
            var bills = settings.SelectNodes("exposeDictionary/values/li/internalList/li/exposable/internalList/li/exposable");
            if (bills == null) return 0;
            int removed = 0;
            foreach (XmlNode bill in bills.Cast<XmlNode>().ToArray())
            {
                // Only the known empty exported placeholder shape. Named but
                // unavailable recipes and real colony bills retain native load
                // diagnostics. This edits the temporary Scribe DOM, not disk.
                if (bill.Attributes?["Class"]?.Value != "Bill_ProductionWithUft" ||
                    bill["loadID"]?.InnerText != int.MinValue.ToString() ||
                    !string.IsNullOrWhiteSpace(bill["recipe"]?.InnerText) ||
                    bill.ChildNodes.Cast<XmlNode>().Any(n => n.NodeType == XmlNodeType.Element &&
                        n.Name != "loadID" && n.Name != "recipe" && n.Name != "allowedSkillRange" &&
                        n.Name != "pawnRestriction" && n.Name != "precept" && n.Name != "xenogerm")) continue;
                XmlNode entry = bill.ParentNode;
                if (entry?.Name != "li" || entry.ParentNode?.Name != "internalList") continue;
                entry.ParentNode.RemoveChild(entry);
                removed++;
            }
            return removed;
        }
    }
}
