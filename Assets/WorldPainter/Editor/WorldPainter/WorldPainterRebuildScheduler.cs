#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Debounced, coalesced rebuild scheduler for per-layer engine rebuilds. Replaces the
    /// previous inline pattern where every IMGUI handle drag tick called
    /// <c>painter.RebuildPropLayer(layer)</c>; that fired 30–60× per second during a single
    /// transform drag, disposing and re-allocating GPU buffers fast enough that the Game-view
    /// camera (which renders continuously in edit mode) caught a partially-torn-down engine
    /// every few frames and showed it as a black / garbage tile flicker.
    ///
    /// The scheduler coalesces rebuild requests by (painter, layer, kind). On every
    /// <see cref="EditorApplication.update"/> tick (editor frame rate, no more than once per
    /// frame) it drains the pending set and issues exactly one rebuild per pair, then triggers
    /// a single <see cref="SceneView.RepaintAll"/>. End-to-end this collapses a continuous
    /// 60-event drag down to one rebuild per frame — flicker disappears.
    /// </summary>
    internal static class WorldPainterRebuildScheduler
    {
        private enum Kind { Grass, Prop }

        private readonly struct Key : System.IEquatable<Key>
        {
            public readonly WorldPainter Painter;
            public readonly Object       Layer;
            public readonly Kind         Kind;

            public Key(WorldPainter painter, Object layer, Kind kind)
            {
                this.Painter = painter; this.Layer = layer; this.Kind = kind;
            }

            public bool Equals(Key other) =>
                ReferenceEquals(this.Painter, other.Painter)
                && ReferenceEquals(this.Layer, other.Layer)
                && this.Kind == other.Kind;

            public override bool Equals(object obj) => obj is Key k && this.Equals(k);
            public override int GetHashCode() =>
                System.HashCode.Combine(
                    this.Painter != null ? this.Painter.GetInstanceID() : 0,
                    this.Layer != null   ? this.Layer.GetInstanceID()   : 0,
                    (int)this.Kind);
        }

        private static readonly HashSet<Key> pending = new();
        private static bool subscribed;

        private static void EnsureSubscribed()
        {
            if (subscribed) return;
            EditorApplication.update += Flush;
            subscribed = true;
        }

        /// <summary>Schedules a coalesced rebuild for every <see cref="WorldPainter"/> in the scene
        /// whose <see cref="WorldPainter.Map"/> contains <paramref name="layer"/>.</summary>
        public static void MarkPropDirty(PropLayer layer)
        {
            if (layer == null) return;
            EnsureSubscribed();

            var painters = Object.FindObjectsByType<WorldPainter>(FindObjectsSortMode.None);
            for (int i = 0; i < painters.Length; ++i)
            {
                var p = painters[i];
                if (p == null || p.Map == null) continue;
                if (!p.Map.SurfaceLayers.Contains(layer)) continue;
                pending.Add(new Key(p, layer, Kind.Prop));
            }
        }

        /// <summary>Schedules a coalesced rebuild for every <see cref="WorldPainter"/> in the scene
        /// whose <see cref="WorldPainter.Map"/> contains <paramref name="layer"/>.</summary>
        public static void MarkGrassDirty(GrassLayer layer)
        {
            if (layer == null) return;
            EnsureSubscribed();

            var painters = Object.FindObjectsByType<WorldPainter>(FindObjectsSortMode.None);
            for (int i = 0; i < painters.Length; ++i)
            {
                var p = painters[i];
                if (p == null || p.Map == null) continue;
                if (!p.Map.SurfaceLayers.Contains(layer)) continue;
                pending.Add(new Key(p, layer, Kind.Grass));
            }
        }

        private static void Flush()
        {
            if (pending.Count == 0) return;

            // Snapshot + clear before iterating so a rebuild that re-dirties (shouldn't happen,
            // but defensively) doesn't re-enter this iteration.
            Key[] snapshot = new Key[pending.Count];
            pending.CopyTo(snapshot);
            pending.Clear();

            for (int i = 0; i < snapshot.Length; ++i)
            {
                Key k = snapshot[i];
                if (k.Painter == null) continue;
                switch (k.Kind)
                {
                    case Kind.Prop  when k.Layer is PropLayer  p: k.Painter.RebuildPropLayer(p);  break;
                    case Kind.Grass when k.Layer is GrassLayer g: k.Painter.RebuildGrassLayer(g); break;
                }
            }

            SceneView.RepaintAll();
        }
    }
}
