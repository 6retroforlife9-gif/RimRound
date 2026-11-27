using System;
using System.Collections.Concurrent;
using HarmonyLib;
using RimRound.Comps;
using Verse;

namespace RimRound.Presentation
{
    // LongEventHandler.ExecuteWhenFinished is not a main-thread dispatcher:
    // outside a long event it executes on its caller. Keep worker notifications
    // here until Root.Update can safely prepare Unity graphics.
    internal sealed class PresentationRefreshWork
    {
        internal readonly Pawn Pawn;
        internal readonly Game Game;
        internal readonly PawnBodyType_ThingComp Comp;
        internal readonly long Generation;
        internal PresentationRefreshWork(Pawn pawn, Game game, PawnBodyType_ThingComp comp, long generation)
        { Pawn = pawn; Game = game; Comp = comp; Generation = generation; }
    }

    internal static class PresentationRefreshQueue
    {
        private static readonly ConcurrentQueue<PresentationRefreshWork> pending = new ConcurrentQueue<PresentationRefreshWork>();
        internal static void Enqueue(Pawn pawn, PawnBodyType_ThingComp comp)
        {
            lock (comp.PresentationQueueLock)
            {
                if (comp.PendingPresentationRefresh != null) return;
                var work = new PresentationRefreshWork(pawn, comp.PresentationGame, comp, comp.PresentationLoadGeneration);
                comp.PendingPresentationRefresh = work;
                pending.Enqueue(work);
            }
        }

        internal static void BindGame(PawnBodyType_ThingComp comp)
        {
            lock (comp.PresentationQueueLock)
            {
                comp.PresentationGame = Current.Game;
                comp.PresentationLoadGeneration++;
                comp.PendingPresentationRefresh = null;
            }
        }

        internal static void Drain()
        {
            if (!UnityData.IsInMainThread || Scribe.mode != LoadSaveMode.Inactive || LongEventHandler.AnyEventNowOrWaiting) return;
            // Bound each frame; new notifications are handled on a later frame.
            int count = Math.Min(256, pending.Count);
            for (int i = 0; i < count && pending.TryDequeue(out var work); i++)
            {
                // A load reset or replacement may have canceled this exact work.
                // Never clear a newer ticket belonging to the same component.
                lock (work.Comp.PresentationQueueLock)
                {
                    if (!ReferenceEquals(work.Comp.PendingPresentationRefresh, work)) continue;
                    work.Comp.PendingPresentationRefresh = null;
                    if (work.Generation != work.Comp.PresentationLoadGeneration || !ReferenceEquals(work.Game, work.Comp.PresentationGame)) continue;
                }
                if (work.Game == null || !ReferenceEquals(work.Game, Current.Game) || work.Pawn.Destroyed) continue;
                try { PawnPresentationOwner.Prepare(work.Pawn); }
                catch (Exception error) { Log.Error("[RimRound] Deferred body presentation failed: " + error); }
            }
        }
    }

    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class PresentationRefreshDrainPatch
    {
        private static void Postfix() => PresentationRefreshQueue.Drain();
    }
}
