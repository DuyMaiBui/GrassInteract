#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Editor-only SSOT debounce for live re-scatter. Tools + inspectors call
    /// <see cref="MarkDirty"/>; the scheduler coalesces marks per (field, layerIdx) and flushes
    /// <see cref="ScatterField.RebuildLayer"/> after a short idle, so dragging a slider re-scatters
    /// once (not 60×/sec). All edit paths call MarkDirty only — never Rebuild()/RebuildLayer() directly.
    /// </summary>
    [InitializeOnLoad]
    internal static class ScatterRebuildScheduler
    {
        private const double DEBOUNCE_SECONDS = 0.15;

        private readonly struct DirtyKey : IEquatable<DirtyKey>
        {
            private readonly int fieldId;
            private readonly int layerIdx;
            public DirtyKey(int fieldId, int layerIdx) { this.fieldId = fieldId; this.layerIdx = layerIdx; }
            public bool Equals(DirtyKey other) => this.fieldId == other.fieldId && this.layerIdx == other.layerIdx;
            public override bool Equals(object? obj) => obj is DirtyKey k && this.Equals(k);
            public override int GetHashCode() => (this.fieldId * 397) ^ this.layerIdx;
        }

        private static readonly Dictionary<DirtyKey, (ScatterField field, int layerIdx, double markTime)> dirty = new();
        private static readonly List<DirtyKey> flushScratch = new();

        static ScatterRebuildScheduler()
        {
            EditorApplication.update += Tick;
        }

        /// <summary>Queues a debounced rebuild of one layer on one field.</summary>
        public static void MarkDirty(ScatterField field, int layerIdx)
        {
            if (field == null) return;
            var key = new DirtyKey(field.GetInstanceID(), layerIdx);
            dirty[key] = (field, layerIdx, EditorApplication.timeSinceStartup);
        }

        /// <summary>Queues a debounced rebuild of every layer on one field.</summary>
        public static void MarkAllLayersDirty(ScatterField field)
        {
            if (field == null || field.Config == null) return;
            int count = field.Layers.Count;
            for (int i = 0; i < count; ++i)
                MarkDirty(field, i);
        }

        private static void Tick()
        {
            if (dirty.Count == 0) return;

            double now = EditorApplication.timeSinceStartup;
            flushScratch.Clear();
            foreach (var kv in dirty)
                if (now - kv.Value.markTime >= DEBOUNCE_SECONDS)
                    flushScratch.Add(kv.Key);

            if (flushScratch.Count == 0) return;

            bool any = false;
            foreach (var key in flushScratch)
            {
                var entry = dirty[key];
                dirty.Remove(key);
                if (entry.field == null) continue;
                entry.field.RebuildLayer(entry.layerIdx);
                any = true;
            }
            if (any) SceneView.RepaintAll();
        }
    }
}
