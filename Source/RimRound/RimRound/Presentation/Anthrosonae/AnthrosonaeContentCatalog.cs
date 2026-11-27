using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimRound.Presentation.Anthrosonae
{
    internal static class AnthrosonaeContentCatalog
    {
        internal static AppearanceSilhouetteKey RootKey(string root) =>
            new AppearanceSilhouetteKey("rimround.physical-root", root, "native-coat-v1");

        internal static AppearanceSupportCatalog Create()
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Coat catalog requires the game thread.");
            var availability = new Dictionary<string, bool>(StringComparer.Ordinal);
            Func<string, bool> texture = path => {
                if (!availability.TryGetValue(path, out var exists))
                    availability[path] = exists = !ContentFinder<Texture2D>.Get(path, false).NullOrBad();
                return exists;
            };
            var directions = new[] { "north", "east", "south" };
            var mappings = new List<AppearanceCoatMapping>();
            for (byte i = 0; i < AnthrosonaeSilhouetteRegistry.PhysicalRootCount; i++)
            {
                string original = AnthrosonaeSilhouetteRegistry.ResolveRoot(new PhysicalRootToken(i));
                var fits = AnthrosonaeBuiltInFamilies.Attachments.Select(family => family.FamilyId == "griff-backbone" ?
                    new AppearanceAttachmentFit(family.FamilyId, "anthrosonae.griff-side-rear-joint.v3", true, AnthrosonaeWingFits.ForRoot(original), AnthrosonaeRearFits.ForRoot(original, family.FamilyId), AnthrosonaeRearFits.ForNorthRoot(original, family.FamilyId)) :
                    new AppearanceAttachmentFit(family.FamilyId, "anthrosonae.tail-side-rear-joint.v3", true, AnthrosonaeTailFits.ForRoot(original, family.FamilyId), AnthrosonaeRearFits.ForRoot(original, family.FamilyId), AnthrosonaeRearFits.ForNorthRoot(original, family.FamilyId))).ToArray();
                bool female = original.StartsWith("Things/Pawn/Humanlike/Bodies/Naked_F_", StringComparison.Ordinal);
                bool male = original.StartsWith("Things/Pawn/Humanlike/Bodies/Naked_M_", StringComparison.Ordinal);
                if (!female && !male) throw new InvalidOperationException("Unregistered body anatomy: " + original);
                bool neutralValue = male && (original.EndsWith("New", StringComparison.Ordinal) || original.EndsWith("Old", StringComparison.Ordinal));
                string body = neutralValue ? original : original.Replace("/Bodies/", "/Bodies/BW/");
                bool bodyAvailable = directions.All(direction => texture(body + "_" + direction));
                string reference = female ? "Female" : "Male";
                foreach (var family in AnthrosonaeBuiltInFamilies.Coats)
                {
                    string native = family.NativeBodyTextureRoots[reference];
                    bool assets = bodyAvailable && directions.All(direction => texture(native + "_" + direction) && texture(native + "_" + direction + "m"));
                    if (family.HasPaintedDetail) assets &= directions.All(direction => texture(family.DetailReferenceRoot(reference, direction) + "_" + direction));
                    // All 15 coat/root matrices and factored attachment families,
                    // directions and poses passed manager native visual review.
                    // Exact scope and limits: ANTHROSONAE_COMPATIBILITY_RESULTS.
                    mappings.Add(new AppearanceCoatMapping(family.Coat, RootKey(original), body, null, "mirrorWest",
                        family.HasPaintedDetail ? AnthrosonaeCoatMaterials.NativeMarkedProjectionPolicy : AnthrosonaeCoatMaterials.NativeProjectionPolicy,
                        assets, false, false, fits, reference, neutralValue));
                }
            }
            return new AppearanceSupportCatalog(10, AnthrosonaeBuiltInFamilies.Coats.Select(x => x.Coat),
                AnthrosonaeBuiltInFamilies.ExpectedNodes.Values, mappings);
        }
    }
}
