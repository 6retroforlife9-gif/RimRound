using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Verse;

namespace RimRound.Presentation.Anthrosonae
{
    // Called inside the main-thread active-node capture.
    // Gene/Props are native identity references, not mutable render inputs.
    public sealed class CapturedAnthrosonaeAttachment : IEquatable<CapturedAnthrosonaeAttachment>
    {
        public readonly AppearanceNodeKey Key;
        public readonly Gene Gene;
        public readonly PawnRenderNodeProperties Properties;
        public readonly AnthrosonaeBuiltInFamilies.AttachmentFamily Family;
        public readonly AppearanceNodeStructure Structure;
        private CapturedAnthrosonaeAttachment(AppearanceNodeKey key, Gene gene, PawnRenderNodeProperties properties,
            AnthrosonaeBuiltInFamilies.AttachmentFamily family, AppearanceNodeStructure structure)
        { Key = key; Gene = gene; Properties = properties; Family = family; Structure = structure; }

        public static bool TryCapture(Pawn pawn, Gene gene, AppearanceNodeKey key, PawnRenderNodeProperties properties,
            AppearanceNodeStructure capturedStructure, out CapturedAnthrosonaeAttachment captured, out string reason)
        {
            captured = null; reason = "unsupported-body-layer";
            if (!UnityData.IsInMainThread) { reason = "appearance-capture-off-thread"; return false; }
            if (string.IsNullOrWhiteSpace(key.ProviderPackageId) || string.IsNullOrWhiteSpace(key.GeneDefName) || key.NodeOrdinal < 0) return false;
            if (!AnthrosonaeBuiltInFamilies.ExpectedNodesInitialized) { reason = "unsupported-attachment-api"; return false; }
            if (!AnthrosonaeBuiltInFamilies.AttachmentsByNode.TryGetValue(key, out var family)) return false;
            if (pawn == null || gene == null || !ReferenceEquals(gene.pawn, pawn) || !gene.Active ||
                gene.GetType() != typeof(Gene) || gene.def?.geneClass != typeof(Gene) || gene.def.modExtensions?.Count > 0 ||
                properties == null || properties.GetType() != typeof(PawnRenderNodeProperties))
            { reason = "modified-body-layer"; return false; }
            string provider = gene.def.modContentPack?.PackageIdPlayerFacing;
            var nodes = gene.def.renderNodeProperties;
            if (!string.Equals(provider, key.ProviderPackageId, StringComparison.OrdinalIgnoreCase) ||
                gene.def.defName != key.GeneDefName || nodes == null || key.NodeOrdinal >= nodes.Count ||
                !ReferenceEquals(nodes[key.NodeOrdinal], properties))
            { reason = "attachment-identity-mismatch"; return false; }
            if (capturedStructure == null || !AnthrosonaeBuiltInFamilies.ExpectedNodes.TryGetValue(key, out var expected) ||
                expected.Role != RegisteredAppearanceRole.BodyAttachment || expected.AttachmentFamily != family.FamilyId ||
                !expected.Structure.Equals(capturedStructure))
            { reason = "modified-body-layer"; return false; }
            captured = new CapturedAnthrosonaeAttachment(key, gene, properties, family, capturedStructure);
            reason = null; return true;
        }
        public bool Equals(CapturedAnthrosonaeAttachment other) => other != null && Key.Equals(other.Key) &&
            ReferenceEquals(Gene, other.Gene) && ReferenceEquals(Properties, other.Properties) &&
            ReferenceEquals(Family, other.Family) && Structure.Equals(other.Structure);
        public override bool Equals(object obj) => Equals(obj as CapturedAnthrosonaeAttachment);
        public override int GetHashCode() => ((Key.GetHashCode() * 397 ^ RuntimeHelpers.GetHashCode(Gene)) * 397 ^
            RuntimeHelpers.GetHashCode(Properties)) * 397 ^ Structure.GetHashCode();
    }

    // Empty is the only static instance: no static Pawn/Gene/Props references.
    public sealed class CapturedAnthrosonaeAttachmentSet : IEquatable<CapturedAnthrosonaeAttachmentSet>
    {
        public static readonly CapturedAnthrosonaeAttachmentSet Empty =
            new CapturedAnthrosonaeAttachmentSet(new CapturedAnthrosonaeAttachment[0], 0);
        public readonly IReadOnlyList<CapturedAnthrosonaeAttachment> Entries;
        public readonly long BuiltInRevision = AnthrosonaeBuiltInFamilies.Revision;
        public readonly long SupportRevision;
        public int Count => Entries.Count;
        private CapturedAnthrosonaeAttachmentSet(CapturedAnthrosonaeAttachment[] entries, long supportRevision)
        { Entries = Array.AsReadOnly((CapturedAnthrosonaeAttachment[])entries.Clone()); SupportRevision = supportRevision; }
        public static bool TryCreate(IEnumerable<CapturedAnthrosonaeAttachment> captures, long supportRevision,
            out CapturedAnthrosonaeAttachmentSet result, out string reason)
        {
            result = null; reason = "incomplete-attachment-capture";
            if (!UnityData.IsInMainThread) { reason = "appearance-capture-off-thread"; return false; }
            if (captures == null || supportRevision <= 0) return false;
            var copy = captures.ToArray();
            if (copy.Any(x => x == null)) return false;
            if (copy.Length > AnthrosonaeBuiltInFamilies.Attachments.Count)
            { reason = "ambiguous-attachment-layer"; return false; }
            for (int i = 0; i < copy.Length; i++)
                for (int j = 0; j < i; j++)
                    if (copy[i].Key.Equals(copy[j].Key) || ReferenceEquals(copy[i].Gene, copy[j].Gene) ||
                        ReferenceEquals(copy[i].Properties, copy[j].Properties))
                    { reason = "ambiguous-attachment-layer"; return false; }
            // Different registered genes are valid hybrid inputs. Policy still
            // requires every active family to have a mapping-specific fit.
            result = new CapturedAnthrosonaeAttachmentSet(
                copy.OrderBy(x => x.Key.ToString(), StringComparer.Ordinal).ToArray(), supportRevision);
            reason = null; return true;
        }
        public bool Equals(CapturedAnthrosonaeAttachmentSet other) => other != null && BuiltInRevision == other.BuiltInRevision &&
            SupportRevision == other.SupportRevision && Entries.SequenceEqual(other.Entries);
        public override bool Equals(object obj) => Equals(obj as CapturedAnthrosonaeAttachmentSet);
        public override int GetHashCode() { unchecked { int hash = BuiltInRevision.GetHashCode() * 397 ^ SupportRevision.GetHashCode(); foreach (var entry in Entries) hash = hash * 397 ^ entry.GetHashCode(); return hash; } }
    }
}
