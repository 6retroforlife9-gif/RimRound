using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimRound.Presentation.Anthrosonae
{
    internal sealed class AnthrosonaeRasterResources
    {
        private readonly Texture2D[] textures;
        private readonly long[] bytes;
        private int materials;
        private bool ownerRetired, disposed;
        internal AnthrosonaeRasterResources(Texture2D[] ownedTextures, long[] ownedBytes)
        {
            textures = (Texture2D[])ownedTextures.Clone(); bytes = (long[])ownedBytes.Clone();
            foreach (long count in bytes) AnthrosonaeRenderResources.TrackTexture(count);
        }
        internal void AddMaterial() { if (disposed) throw new InvalidOperationException("Retired raster resource."); materials++; }
        internal void RemoveMaterial() { materials--; TryDispose(); }
        internal void RetireOwner() { ownerRetired = true; TryDispose(); }
        private void TryDispose()
        {
            if (!ownerRetired || materials != 0 || disposed) return;
            disposed = true;
            for (int i = 0; i < textures.Length; i++) {
                UnityEngine.Object.Destroy(textures[i]); AnthrosonaeRenderResources.RecordTextureDisposal(bytes[i]);
            }
        }
    }
    internal sealed class AnthrosonaeMaterialSet
    {
        internal readonly AnthrosonaeRasterResources Rasters;
        internal readonly List<Material> Materials = new List<Material>();
        internal readonly Dictionary<Material, Material> Invisible = new Dictionary<Material, Material>();
        internal AnthrosonaeMaterialSet(AnthrosonaeRasterResources rasters) { Rasters = rasters; }
    }
    // Lifetime follows the managed prepared Graphic, never a Unity Material
    // key: native runtime evidence showed material-keyed leases do not retire.
    // Native draw requests retain their node, which retains its Graphic. Cached
    // portrait/atlas pixels need no material after rendering. Raw borrowed Mat
    // references are valid only while their owning Graphic is retained.
    public static class AnthrosonaeRenderResources
    {
        private sealed class Retirement
        {
            internal readonly AnthrosonaeMaterialSet Materials;
            // Keep textures alive until the material is disposed on the game
            // thread. Finalizer ordering must never choose GPU disposal order.
            internal readonly AnthrosonaeRasterResources Owner;
            internal Retirement(AnthrosonaeMaterialSet materials, AnthrosonaeRasterResources owner)
            { Materials = materials; Owner = owner; }
        }
        // Entries only identify adapter-owned mats for the native invisibility
        // interception. Values do not reference/finalize the owning Graphic.
        private static readonly ConditionalWeakTable<Material, AnthrosonaeMaterialSet> Materials = new ConditionalWeakTable<Material, AnthrosonaeMaterialSet>();
        private static readonly ConcurrentQueue<Retirement> Pending = new ConcurrentQueue<Retirement>();
        private static long createdMaterials, retiredMaterials, createdTextures, retiredTextures, createdBytes, retiredBytes;
        public static long CreatedMaterials => Interlocked.Read(ref createdMaterials);
        public static long RetiredMaterials => Interlocked.Read(ref retiredMaterials);
        public static long LiveMaterials => CreatedMaterials - RetiredMaterials;
        public static long LiveTextures => Interlocked.Read(ref createdTextures) - Interlocked.Read(ref retiredTextures);
        public static long LiveTextureBytes => Interlocked.Read(ref createdBytes) - Interlocked.Read(ref retiredBytes);
        public static int PendingRetirements => Pending.Count;

        internal static Material Create(Texture2D body, Texture2D mask, Shader shader, Color primary, Color secondary, AnthrosonaeMaterialSet owner)
        {
            RequireMainThread();
            // Match native MaterialPool's Color32 precision without registering
            // the resulting object in MaterialPool or MaterialAllocator.
            Material material = null;
            try {
                material = new Material(shader);
                material.name = "RR Anthro " + body.name; material.mainTexture = body; material.color = (Color32)primary;
                if (mask != null && shader.SupportsMaskTex()) material.SetTexture(ShaderPropertyIDs.MaskTex, mask);
                material.SetColor(ShaderPropertyIDs.ColorTwo, (Color32)secondary);
                Track(material, owner); return material;
            } catch { if (material != null) UnityEngine.Object.Destroy(material); throw; }
        }
        private static void Track(Material material, AnthrosonaeMaterialSet owner)
        {
            bool indexed = false, retained = false;
            try {
                Materials.Add(material, owner); indexed = true;
                owner.Materials.Add(material); retained = true;
                owner.Rasters.AddMaterial();
                Interlocked.Increment(ref createdMaterials);
            } catch {
                if (retained) owner.Materials.Remove(material);
                if (indexed) Materials.Remove(material);
                throw;
            }
        }
        internal static bool TryInvisible(Material original, out Material invisible)
        {
            invisible = null;
            if (ReferenceEquals(original, null) || !Materials.TryGetValue(original, out var owner)) return false;
            // EnsureInitialized prepares native node variants on the game
            // thread. ParallelPreDraw also asks for these existing materials:
            // only a cache miss may enter the Unity allocation path.
            if (owner.Invisible.TryGetValue(original, out invisible)) return true;
            RequireMainThread();
            if (!owner.Invisible.TryGetValue(original, out invisible))
            {
                Material material = null;
                try {
                    material = new Material(original);
                    material.shader = ShaderDatabase.Invisible; material.color = new Color(.75f, .93f, .98f, .5f);
                    material.SetTexture("_NoiseTex", TexGame.InvisDistortion);
                    owner.Invisible.Add(original, material);
                    try { Track(material, owner); }
                    catch { owner.Invisible.Remove(original); throw; }
                    invisible = material;
                } catch { if (material != null) UnityEngine.Object.Destroy(material); throw; }
            }
            return true;
        }
        internal static void TrackTexture(long bytes)
        { Interlocked.Increment(ref createdTextures); Interlocked.Add(ref createdBytes, bytes); }
        // Called by a plain managed raster owner finalizer. No Unity API is
        // touched here; only the game thread may destroy a generated texture.
        internal static void RetireRasters(AnthrosonaeRasterResources rasters)
        { if (rasters != null) Pending.Enqueue(new Retirement(null, rasters)); }
        internal static void RetireMaterials(AnthrosonaeMaterialSet materials)
        { if (materials != null) Pending.Enqueue(new Retirement(materials, materials.Rasters)); }
        internal static void RecordTextureDisposal(long bytes)
        { Interlocked.Increment(ref retiredTextures); Interlocked.Add(ref retiredBytes, bytes); }
        internal static void DrainRetired()
        {
            RequireMainThread();
            while (Pending.TryDequeue(out var item))
            {
                if (item.Materials == null) item.Owner.RetireOwner();
                else {
                    foreach (var material in item.Materials.Materials) {
                        Materials.Remove(material); UnityEngine.Object.Destroy(material);
                        Interlocked.Increment(ref retiredMaterials); item.Owner.RemoveMaterial();
                    }
                    item.Materials.Materials.Clear(); item.Materials.Invisible.Clear();
                }
                GC.KeepAlive(item.Owner);
            }
        }
        private static void RequireMainThread()
        { if (!UnityData.IsInMainThread) throw new InvalidOperationException("Anthrosonae GPU resources require the game thread."); }
    }

    internal sealed class AnthrosonaeResourceDrain : MonoBehaviour
    {
        private static AnthrosonaeResourceDrain instance;
        internal static void EnsureRunning()
        {
            if (instance != null) return;
            var owner = new GameObject("RimRound generated coat resources") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(owner);
            instance = owner.AddComponent<AnthrosonaeResourceDrain>();
            instance.StartCoroutine(instance.EndOfFrame());
        }
        private IEnumerator EndOfFrame()
        {
            var boundary = new WaitForEndOfFrame();
            while (true) {
                yield return boundary;
                if (AnthrosonaeRenderResources.PendingRetirements != 0) AnthrosonaeRenderResources.DrainRetired();
            }
        }
    }

    [HarmonyPatch(typeof(InvisibilityMatPool), nameof(InvisibilityMatPool.GetInvisibleMat))]
    internal static class PreparedAnthrosonaeInvisibleMaterialPatch
    {
        private static bool Prefix(Material baseMat, ref Material __result)
        {
            if (!AnthrosonaeRenderResources.TryInvisible(baseMat, out var owned)) return true;
            __result = owned; return false;
        }
    }
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class PreparedAnthrosonaeResourceDrainPatch
    {
        private static void Postfix()
        {
            if (UnityData.IsInMainThread && Time.frameCount % 30 == 0 && AnthrosonaeRenderResources.PendingRetirements != 0)
                AnthrosonaeResourceDrain.EnsureRunning();
        }
    }
}
