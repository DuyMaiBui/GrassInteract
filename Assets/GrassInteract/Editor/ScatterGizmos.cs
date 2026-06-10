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
        internal static readonly Color InstanceColor     = new(0.4f, 0.7f, 1f, 1f);
        internal static readonly Color SelectedColor     = new(1f, 0.8f, 0.2f, 1f);
        internal static readonly Color EraseColor        = new(1f, 0.35f, 0.3f, 1f);
        internal static readonly Color FieldBoundsColor  = new(1f, 0.5f, 0.1f, 0.8f);

        private const float DEFAULT_DOT_SIZE   = 0.08f;

        // ── Brush gizmos (Phase 3) ─────────────────────────────────────────────

        public static void BrushDisc(Vector3 center, Vector3 normal, float radius, Color color)
        {
            Color prev = Handles.color;
            Handles.color = color;
            Handles.DrawWireDisc(center, normal, radius);
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
    }
}
