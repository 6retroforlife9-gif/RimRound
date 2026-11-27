using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace RimRound.FeedOther
{
    internal static class RimRoundAudioLoading
    {
        private static readonly HashSet<string> effectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool installed;

        internal static void Initialize(ModContentPack content)
        {
            // Mod construction precedes the deferred audio-content load. Native
            // streaming shares one reader/seek position per clip; our effects can
            // play on several pawns/machines concurrently. Buffer this pack's
            // effects once, retaining native loading for every other sound pack.
            foreach (var file in ModContentPack.GetAllFilesForMod(content, "Sounds", ModContentLoader<AudioClip>.IsAcceptableExtension).Values)
                effectFiles.Add(Path.GetFullPath(file.FullName));
            if (installed) return;
            var target = AccessTools.Method(typeof(ModContentLoader<AudioClip>), "ShouldStreamAudioClipFromFile");
            if (target == null)
            {
                Log.Warning("[RimRound] Native audio loading policy unavailable; keeping the original loader.");
                return;
            }
            new Harmony("RimRound.ConcurrentEffectAudio").Patch(target,
                postfix: new HarmonyMethod(typeof(RimRoundAudioLoading), nameof(KeepEffectBuffered)));
            installed = true;
        }

        private static void KeepEffectBuffered(VirtualFile file, ref bool __result)
        {
            if (__result && file != null && effectFiles.Contains(Path.GetFullPath(file.FullPath)))
                __result = false;
        }
    }
}
