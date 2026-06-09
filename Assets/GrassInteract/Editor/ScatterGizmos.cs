#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// SSOT <see cref="Handles"/> helpers for every GrassInteract scene tool (density brush,
    /// instance placement) plus the <see cref="ScatterField"/> field-bounds gizmo. No per-tool
    /// gizmo copies (requirement #6 + DRY) — all gizmo drawing routes through here.
    /// </summary>
    internal static class ScatterGizmos
    {
        // ── Named colors (no magic literals at call sites) ─────────────────────

        internal static readonly Color BrushColor        = new(0.2f, 0.9f, 0.4f, 1f);
        internal static readonly Color BrushFalloffColor = new(0.2f, 0.9f, 0.4f, 0.45f);
        internal static readonly Color InstanceColor     = new(0.4f, 0.7f, 1f, 1f);
        internal static readonly Color SelectedColor     = new(1f, 0.8f, 0.2f, 1f);
        internal static readonly Color NormalColor       = new(0.6f, 0.9f, 1f, 1f);
        internal static readonly Color EraseColor        = new(1f, 0.35f, 0.3f, 1f);
        internal static readonly Color FieldBoundsColor  = new(1f, 0.5f, 0.1f, 0.8f);

        private const float DEFAULT_DOT_SIZE   = 0.08f;
        private const float DEFAULT_NORMAL_LEN = 0.5f;

        // ── Brush gizmos (Phase 3) ─────────────────────────────────────────────

        public static void BrushDisc(Vector3 center, Vector3 normal, float radius, Color color)
        {
            Color prev = Handles.color;
            Handles.color = color;
            Handles.DrawWireDisc(center, normal, radius);
            Handles.color = prev;
        }

        public static void FalloffRing(Vector3 center, Vector3 normal, float inner, float outer, Color color)
        {
            Color prev = Handles.color;
            Handles.color = color;
            Handles.DrawWireDisc(center, normal, Mathf.Max(0f, inner));
            Handles.DrawWireDisc(center, normal, Mathf.Max(inner, outer));
            Handles.color = prev;
        }

        // ── Instance gizmos (Phase 4) ──────────────────────────────────────────

        public static void InstanceDot(Vector3 pos, float size, Color color)
        {
            Color prev = Handles.color;
            Handles.color = color;
            float s = size <= 0f ? DEFAULT_DOT_SIZE : size;
            Handles.SphereHandleCap(0, pos, Quaternion.identity, s, EventType.Repaint);
            Handles.color = prev;
        }

        public static void Normal(Vector3 pos, Vector3 normal, float length, Color color)
        {
            Color prev = Handles.color;
            Handles.color = color;
            float len = length <= 0f ? DEFAULT_NORMAL_LEN : length;
            Handles.DrawLine(pos, pos + normal.normalized * len);
            Handles.color = prev;
        }

        // ── Bounds (Phase 4 selected instance + field bounds) ──────────────────

        public static void Aabb(Bounds bounds, Color color)
        {
            if (bounds.size == Vector3.zero) return;
            Color prev = Handles.color;
            Handles.color = color;
            Handles.DrawWireCube(bounds.center, bounds.size);
            Handles.color = prev;
        }
    }
}
