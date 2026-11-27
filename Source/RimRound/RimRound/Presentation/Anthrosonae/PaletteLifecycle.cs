using System;
using System.Collections.Generic;

namespace RimRound.Presentation.Anthrosonae
{
    // Pawn-owned, main-thread only. TContext must be an immutable complete
    // non-palette input identity, including forced gear and catalog revision.
    public sealed class BoundedPaletteSnapshotCache<TContext, TPrepared> where TPrepared : class
    {
        private sealed class Entry
        {
            internal readonly AppearancePalette Palette;
            internal readonly TPrepared Prepared;
            internal Entry(AppearancePalette palette, TPrepared prepared) { Palette = palette; Prepared = prepared; }
        }
        private readonly int capacity;
        private readonly Func<bool> isMainThread;
        private readonly List<Entry> entries = new List<Entry>();
        private bool hasContext;
        private TContext context;
        public BoundedPaletteSnapshotCache(int capacity, Func<bool> isMainThread)
        {
            if (capacity < 2 || capacity > 4) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity; this.isMainThread = isMainThread ?? throw new ArgumentNullException(nameof(isMainThread));
        }
        private void RequireGameThread() { if (!isMainThread()) throw new InvalidOperationException("Palette cache is game-thread only."); }
        public int Count { get { RequireGameThread(); return entries.Count; } }
        public TPrepared GetOrBuild(TContext newContext, AppearancePalette palette, Func<TPrepared> build)
        {
            RequireGameThread();
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (!hasContext || !EqualityComparer<TContext>.Default.Equals(context, newContext))
            { entries.Clear(); context = newContext; hasContext = true; }
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].Palette.Equals(palette)) continue;
                var found = entries[i]; entries.RemoveAt(i); entries.Insert(0, found); return found.Prepared;
            }
            var prepared = build() ?? throw new InvalidOperationException("Cannot publish null prepared palette.");
            entries.Insert(0, new Entry(palette, prepared));
            if (entries.Count > capacity) entries.RemoveAt(entries.Count - 1);
            return prepared;
        }
        public void Clear() { RequireGameThread(); entries.Clear(); hasContext = false; context = default(TContext); }
    }

    // Around the exact optional writer. The generic dirty hook checks suppression
    // synchronously and never queues its duplicate forced refresh in this scope.
    public sealed class PaletteMutationCoordinator
    {
        private readonly Func<bool> isMainThread;
        private readonly Action<bool> semanticPrepare;
        private int depth;
        private bool failure;
        public PaletteMutationCoordinator(Func<bool> isMainThread, Action<bool> semanticPrepare)
        { this.isMainThread = isMainThread ?? throw new ArgumentNullException(nameof(isMainThread)); this.semanticPrepare = semanticPrepare ?? throw new ArgumentNullException(nameof(semanticPrepare)); }
        public bool SuppressesGenericForcedRefresh => isMainThread() && depth > 0;
        public Scope Begin()
        {
            if (!isMainThread()) throw new InvalidOperationException("A palette writer scope requires the game thread.");
            depth++; return new Scope(this);
        }
        private void End(bool succeeded)
        {
            if (!isMainThread() || depth <= 0) throw new InvalidOperationException("Unbalanced palette writer scope.");
            failure |= !succeeded;
            depth--;
            if (depth != 0) return;
            bool completed = !failure; failure = false;
            // Clear scope before Prepare; native invalidation must not recurse
            // into the optional writer. Exception after a partial native write
            // still captures/falls back coherently, with original fault retained.
            semanticPrepare(completed);
        }
        public sealed class Scope
        {
            private PaletteMutationCoordinator owner;
            internal Scope(PaletteMutationCoordinator owner) { this.owner = owner; }
            public void Close(bool writerSucceeded)
            { var current = owner; if (current == null) return; owner = null; current.End(writerSucceeded); }
        }
    }
}
