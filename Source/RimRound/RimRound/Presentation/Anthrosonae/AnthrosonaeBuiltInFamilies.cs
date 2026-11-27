using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimRound.Presentation.Anthrosonae
{
    // Audited identity/structure data only. This does not register silhouette art,
    // material policies or accepted attachment fits, and has no optional CLR refs.
    public static class AnthrosonaeBuiltInFamilies
    {
        public const string ProviderPackageId = "ATK.Anthrosonae";
        public const long Revision = 1;
        public const string GeneXmlSha256 = "b3731fe7a4e0a3e8168e0c3911df684e8879a4d1e4a075a8adeb55c3c8682735";
        public const string FurXmlSha256 = "02d08a6acc369346fe659853ac11583485b13458e54d034ab770db9b9f4d8963";
        public const string NativeAssemblySha256 = "5cf1b5be399d5b1c9c56ca72c9d35b4ecf307feacf5859d04ac5a1aa5926356a";

        public sealed class CoatFamily
        {
            public readonly AppearanceCoatKey Coat;
            public readonly AppearanceNodeKey BodyNode;
            public readonly string ExpectedGeneClassName = "Anthrosonae.FurGene";
            public readonly IReadOnlyList<string> ExpectedModExtensionTypeNames = Array.AsReadOnly(new[] { "Anthrosonae.FurColors" });
            public readonly bool NativeFurNoGraphic = true;
            public readonly string NativeOverrideShaderTypeDefName = null;
            public readonly IReadOnlyDictionary<string, string> NativeBodyTextureRoots;
            private readonly string detailNorth, detailOther;
            public bool HasPaintedDetail => detailNorth != null;
            public string DetailReferenceRoot(string body, string direction) => !HasPaintedDetail ? null :
                "Things/Pawn/Humanlike/Anthrosonae/Bodies/" + (direction == "north" ? detailNorth : detailOther) + "_" + body;
            internal CoatFamily(string gene, string fur, string textureFamily, string detailNorth = null, string detailOther = null)
            {
                this.detailNorth = detailNorth; this.detailOther = detailOther;
                Coat = new AppearanceCoatKey(ProviderPackageId, gene, fur);
                BodyNode = new AppearanceNodeKey(ProviderPackageId, gene, 0);
                string root = "Things/Pawn/Humanlike/Anthrosonae/Bodies/" + textureFamily;
                // All seven entries are explicit in each audited FurDef. Baby
                // intentionally reuses Child; this is not adult support inference.
                NativeBodyTextureRoots = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal) {
                    { "Male", root + "_Male" }, { "Female", root + "_Female" }, { "Hulk", root + "_Hulk" },
                    { "Fat", root + "_Fat" }, { "Thin", root + "_Thin" }, { "Child", root + "_Child" }, { "Baby", root + "_Child" }
                });
            }
        }

        public sealed class AttachmentFamily
        {
            public readonly string FamilyId, TexturePath;
            public readonly AppearanceNodeKey BodyNode;
            public readonly string ExpectedGeneClassName = "Verse.Gene";
            public readonly IReadOnlyList<string> ExpectedModExtensionTypeNames = Array.AsReadOnly(new string[0]);
            public readonly Vector2 MeshSize;
            public readonly DrawData.RotationalData DefaultData, NorthData, EastData, SouthData, WestData;
            public readonly IReadOnlyList<Rot4> VisibleFacings;
            public readonly bool ScaleOffsetByBodySize = true, UseBodyPartAnchor = false;
            public readonly float Scale = 1f, ChildScale = 1f;
            internal AttachmentFamily(string family, string gene, string texture, float mesh,
                Vector3 north, Vector3 east, Vector3 south, Vector3 west, bool excludeSouth)
            {
                FamilyId = family; BodyNode = new AppearanceNodeKey(ProviderPackageId, gene, 0); TexturePath = texture;
                MeshSize = new Vector2(mesh, mesh);
                // Unspecified nullable members stay absent, as in the XML. The
                // native constructor leaves bodyTypeScales null and scales at 1.
                DefaultData = new DrawData.RotationalData { layer = -2f };
                NorthData = new DrawData.RotationalData { offset = north, layer = 90f };
                EastData = new DrawData.RotationalData { offset = east };
                SouthData = new DrawData.RotationalData { offset = south };
                WestData = new DrawData.RotationalData { offset = west };
                VisibleFacings = excludeSouth ? Array.AsReadOnly(new[] { Rot4.East, Rot4.North, Rot4.West }) : null;
            }
        }

        public static readonly IReadOnlyList<CoatFamily> Coats = Array.AsReadOnly(new[] {
            new CoatFamily("ATK_AnthrobearFur", "ATK_Anthrobear", "Anthrobear"),
            new CoatFamily("ATK_AnthrocatFur", "ATK_Anthrocat", "Anthrocat", "Anthrogriff", "Anthrobear"),
            new CoatFamily("ATK_AnthrocowFur", "ATK_Anthrocow", "Anthrocow"),
            new CoatFamily("ATK_AnthrodeerFur", "ATK_Anthrodeer", "Anthrodeer"),
            new CoatFamily("ATK_AnthrodrakeSkin", "ATK_Anthrodrake", "Anthrodrake"),
            new CoatFamily("ATK_AnthrofoxFur", "ATK_Anthrofox", "Anthrofox"),
            new CoatFamily("ATK_AnthrogoatFur", "ATK_Anthrogoat", "Anthrogoat"),
            new CoatFamily("ATK_AnthrogriffFur", "ATK_Anthrogriff", "Anthrogriff"),
            new CoatFamily("ATK_AnthrohorseFur", "ATK_Anthrohorse", "Anthrohorse"),
            new CoatFamily("ATK_AnthrohyenaFur", "ATK_Anthrohyena", "Anthrohyena", "Anthrogriff", "Anthrorabbit"),
            new CoatFamily("ATK_AnthropandaFur", "ATK_Anthropanda", "Anthropanda"),
            new CoatFamily("ATK_AnthrorabbitFur", "ATK_Anthrorabbit", "Anthrorabbit"),
            new CoatFamily("ATK_AnthrosharkSkin", "ATK_Anthroshark", "Anthroshark", "Anthrohorse", "Anthrohorse"),
            new CoatFamily("ATK_AnthrowahFur", "ATK_Anthrowah", "Anthrowah"),
            new CoatFamily("ATK_AnthrowolfFur", "ATK_Anthrowolf", "Anthrowolf")
        });

        private const string TailRoot = "Things/Pawn/Humanlike/Anthrosonae/BodyAttachments/Tails/";
        public static readonly IReadOnlyList<AttachmentFamily> Attachments = Array.AsReadOnly(new[] {
            Tail("bear-tail", "ATK_AnthrobearTail", "AnthrobearTail", 1.2f, 0f, -.42f, -.65f, -.29f, -.1f, -.18f),
            Tail("cat-tail", "ATK_AnthrocatTail", "AnthrocatTail", 1.2f, .04f, -.5f, -.55f, -.25f, -.14f, -.25f),
            Tail("cow-tail", "ATK_AnthrocowTail", "AnthrocowTail", 1.2f, .03f, -.5f, -.65f, -.3f, -.03f, -.31f),
            Tail("deer-tail", "ATK_AnthrodeerTail", "AnthrodeerTail", 1.2f, 0f, -.53f, -.6f, -.29f, -.1f, -.28f, true),
            Tail("drake-tail", "ATK_AnthrodrakeTail", "AnthrodrakeTail", 1.2f, .03f, -.5f, -.58f, -.28f, -.13f, -.28f),
            Tail("fox-tail", "ATK_AnthrofoxTail", "AnthrofoxTail", 1.2f, .03f, -.5f, -.61f, -.28f, -.13f, -.28f),
            Tail("goat-tail", "ATK_AnthrogoatTail", "AnthrogoatTail", 1.2f, -.01f, -.45f, -.55f, -.25f, -.09f, -.22f, true),
            Tail("griff-tail", "ATK_AnthrogriffTail", "AnthrogriffTail", 1f, .03f, -.45f, -.54f, -.25f, -.11f, -.2f),
            Tail("horse-tail", "ATK_AnthrohorseTail", "AnthrohorseTail", .9f, .03f, -.6f, -.54f, -.3f, -.11f, -.25f),
            Tail("hyena-tail", "ATK_AnthrohyenaTail", "AnthrohyenaTail", 1.2f, .04f, -.53f, -.34f, -.39f, -.44f, -.18f),
            Tail("panda-tail", "ATK_AnthropandaTail", "AnthropandaTail", 1.2f, 0f, -.42f, -.65f, -.29f, -.1f, -.18f),
            Tail("rabbit-tail", "ATK_AnthrorabbitTail", "AnthrorabbitTail", 1.2f, 0f, -.5f, -.6f, -.28f, -.1f, -.28f, true),
            Tail("shark-tail", "ATK_AnthrosharkTail", "AnthrosharkTail", .9f, 0f, -.55f, -.53f, -.25f, -.25f, -.36f),
            Tail("wah-tail", "ATK_AnthrowahTail", "AnthrowahTail", 1.2f, .05f, -.53f, -.61f, -.3f, -.15f, -.28f),
            Tail("wolf-tail", "ATK_AnthrowolfTail", "AnthrowolfTail", 1.2f, .03f, -.5f, -.6f, -.28f, -.13f, -.28f),
            // Native gene ID says Backbone; its player-facing label says wings.
            new AttachmentFamily("griff-backbone", "ATK_AnthrogriffBackbone",
                "Things/Pawn/Humanlike/Anthrosonae/BodyAttachments/Backbones/AnthrogriffBackbone", 1.4f,
                Vector3.zero, new Vector3(-.05f, 0f, 0f), Vector3.zero, new Vector3(.05f, 0f, 0f), false)
        });

        private static AttachmentFamily Tail(string family, string gene, string texture, float mesh,
            float northX, float northZ, float eastX, float eastZ, float southX, float southZ, bool excludeSouth = false) =>
            new AttachmentFamily(family, gene, TailRoot + texture, mesh, new Vector3(northX, 0f, northZ),
                new Vector3(eastX, 0f, eastZ), new Vector3(southX, 0f, southZ), new Vector3(-eastX, 0f, eastZ), excludeSouth);

        public static readonly IReadOnlyDictionary<AppearanceCoatKey, CoatFamily> CoatsByKey =
            new ReadOnlyDictionary<AppearanceCoatKey, CoatFamily>(Coats.ToDictionary(x => x.Coat));
        public static readonly IReadOnlyDictionary<AppearanceNodeKey, AttachmentFamily> AttachmentsByNode =
            new ReadOnlyDictionary<AppearanceNodeKey, AttachmentFamily>(Attachments.ToDictionary(x => x.BodyNode));
        public static readonly IReadOnlyList<AppearanceNodeKey> BodyNodeKeys =
            Array.AsReadOnly(Coats.Select(x => x.BodyNode).Concat(Attachments.Select(x => x.BodyNode)).ToArray());

        private static IReadOnlyDictionary<AppearanceNodeKey, AppearanceLayerRegistration> expectedNodes;
        public static bool ExpectedNodesInitialized => expectedNodes != null;
        public static IReadOnlyDictionary<AppearanceNodeKey, AppearanceLayerRegistration> ExpectedNodes =>
            expectedNodes ?? throw new InvalidOperationException("Initialize built-in expected nodes on the game thread before reading them.");

        // Explicit optional-bridge startup work. Merely reading family metadata
        // does not resolve optional APIs, scan live defs or activate support.
        public static void InitializeExpectedNodes()
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Built-in expected nodes require the game thread.");
            if (expectedNodes != null) return;
            AnthrosonaeNodeSchema.Initialize();
            var registrations = new Dictionary<AppearanceNodeKey, AppearanceLayerRegistration>();
            foreach (var coat in Coats)
                registrations.Add(coat.BodyNode, new AppearanceLayerRegistration(coat.BodyNode,
                    AnthrosonaeNodeSchema.ExpectedFur, RegisteredAppearanceRole.CoatSurface));
            foreach (var attachment in Attachments)
                registrations.Add(attachment.BodyNode, new AppearanceLayerRegistration(attachment.BodyNode,
                    AnthrosonaeNodeSchema.BuildExpectedAttachment(attachment.TexturePath, attachment.MeshSize,
                        attachment.DefaultData, attachment.NorthData, attachment.EastData, attachment.SouthData, attachment.WestData,
                        attachment.VisibleFacings, attachment.ScaleOffsetByBodySize, attachment.UseBodyPartAnchor, attachment.Scale, attachment.ChildScale),
                    RegisteredAppearanceRole.BodyAttachment, attachment.FamilyId));
            expectedNodes = new ReadOnlyDictionary<AppearanceNodeKey, AppearanceLayerRegistration>(registrations);
        }
    }
}
