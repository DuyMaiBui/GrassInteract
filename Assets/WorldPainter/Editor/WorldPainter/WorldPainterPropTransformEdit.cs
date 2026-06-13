#nullable enable
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Transform-edit mode for unified <see cref="PropLayer"/> layers.
    ///
    /// Handles SceneView input when <see cref="PropTransformModeActive"/> is true:
    ///   - Left-click: pick the nearest <see cref="InstanceRecord"/> within a screen-space
    ///     tolerance of the cursor.
    ///   - Selected instance: renders a <see cref="Handles"/> Position / Rotation / Scale
    ///     gizmo in SceneView.
    ///   - Gizmo drag: writes the updated TRS back to the layer's
    ///     <see cref="AuthoredInstancesData"/> via <see cref="AuthoredInstancesData.TryUpdateRecord"/>.
    ///
    /// Keyboard:
    ///   - T: toggle back to Scatter mode.
    ///   - Escape: deselect the current instance.
    ///
    /// Call <see cref="OnSceneGUI"/> from <see cref="WorldPainterSculptTool.OnToolGUI"/>
    /// when a <see cref="PropLayer"/> is active.
    /// Call <see cref="Reset"/> whenever the active layer or painter changes.
    /// </summary>
    internal sealed class WorldPainterPropTransformEdit
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Keyboard key that toggles Scatter/Transform mode (lower-case).</summary>
        public const char TOGGLE_KEY = 't';

        /// <summary>Max world-space distance from cursor ray for click-pick.</summary>
        private const float PICK_RADIUS_M = 1.5f;

        // ── Static mode flag ──────────────────────────────────────────────────

        /// <summary>
        /// True when the prop layer is in Transform mode (instance gizmo editing).
        /// False = Scatter mode (brush-stamp placement).
        /// Reset to false on domain reload; persists within a session.
        /// </summary>
        public static bool PropTransformModeActive { get; private set; }

        /// <summary>Toggle between Scatter and Transform mode.</summary>
        public static void ToggleMode()
        {
            PropTransformModeActive = !PropTransformModeActive;
        }

        // ── Per-instance selection state ──────────────────────────────────────

        /// <summary>
        /// Index into <see cref="AuthoredInstancesData.WorkingList"/> for the selected instance.
        /// -1 = none selected.
        /// </summary>
        public int SelectedIndex { get; private set; } = -1;

        // ── Shared singleton ──────────────────────────────────────────────────

        /// <summary>Shared instance — created once per domain reload by the sculpt tool.</summary>
        public static readonly WorldPainterPropTransformEdit Instance = new WorldPainterPropTransformEdit();

        private WorldPainterPropTransformEdit() { }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Deselect the current instance and reset internal state.
        /// Call whenever the active layer or painter changes.
        /// </summary>
        public void Reset()
        {
            this.SelectedIndex = -1;
        }

        /// <summary>
        /// Process SceneView GUI events for Transform mode.
        /// Should be called from the sculpt tool's <c>OnToolGUI</c> when
        /// <see cref="PropTransformModeActive"/> is true and a <see cref="PropLayer"/> is active.
        /// </summary>
        public void OnSceneGUI(PropLayer layer, SceneView sceneView)
        {
            var authored = layer.AuthoredInstances;
            if (authored == null) return;

            var currentEvent = Event.current;

            // ── Keyboard shortcuts ────────────────────────────────────────────

            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.T ||
                    currentEvent.character == TOGGLE_KEY)
                {
                    PropTransformModeActive = false;
                    this.SelectedIndex = -1;
                    currentEvent.Use();
                    HandleUtility.Repaint();
                    return;
                }

                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    this.SelectedIndex = -1;
                    currentEvent.Use();
                    HandleUtility.Repaint();
                    return;
                }
            }

            // ── Click-pick ────────────────────────────────────────────────────

            if (currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                !currentEvent.alt)
            {
                int picked = PickInstance(authored, currentEvent.mousePosition);
                if (picked >= 0)
                {
                    this.SelectedIndex = picked;
                    currentEvent.Use();
                }
                else if (!currentEvent.shift)
                {
                    this.SelectedIndex = -1;
                }
            }

            // ── Per-instance selection gizmos (dots) ──────────────────────────

            DrawInstanceGizmos(authored, this.SelectedIndex);

            // ── Transform gizmo for selected instance ─────────────────────────

            if (this.SelectedIndex >= 0)
                this.DrawTransformGizmo(layer, authored, this.SelectedIndex);
        }

        // ── Click-pick ────────────────────────────────────────────────────────

        private static int PickInstance(AuthoredInstancesData authored, Vector2 mousePos)
        {
            var ray      = HandleUtility.GUIPointToWorldRay(mousePos);
            var list     = authored.WorkingList;
            int best     = -1;
            float bestDist = PICK_RADIUS_M;

            for (int i = 0; i < list.Count; i++)
            {
                Vector3 worldPos = list[i].position;
                float dist = Vector3.Cross(ray.direction, worldPos - ray.origin).magnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best     = i;
                }
            }

            return best;
        }

        // ── Selection gizmo dots ──────────────────────────────────────────────

        private static void DrawInstanceGizmos(AuthoredInstancesData authored, int selectedIdx)
        {
            var list = authored.WorkingList;
            if (list.Count == 0) return;

            for (int i = 0; i < list.Count; i++)
            {
                bool isSel = i == selectedIdx;
                Handles.color = isSel
                    ? Color.yellow
                    : new Color(0.3f, 0.8f, 1f, 0.6f);
                float size = HandleUtility.GetHandleSize(list[i].position) * (isSel ? 0.12f : 0.06f);
                Handles.SphereHandleCap(0, list[i].position, Quaternion.identity, size, EventType.Repaint);
            }

            Handles.color = Color.white;
        }

        // ── Transform gizmo ───────────────────────────────────────────────────

        private void DrawTransformGizmo(
            PropLayer             layer,
            AuthoredInstancesData authored,
            int                   idx)
        {
            var list = authored.WorkingList;
            if (idx < 0 || idx >= list.Count) return;

            var rec = list[idx];

            EditorGUI.BeginChangeCheck();

            var newPos   = Handles.PositionHandle(rec.position, rec.rotation);
            var newRot   = Handles.RotationHandle(rec.rotation, rec.position);

            float handleSize = HandleUtility.GetHandleSize(rec.position);
            float newScale   = Handles.ScaleSlider(rec.scale, rec.position,
                rec.rotation * Vector3.up, rec.rotation, handleSize, 0.01f);
            newScale = Mathf.Max(0.01f, newScale);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(authored, "Prop Transform Edit");

                var updated      = rec;
                updated.position = newPos;
                updated.rotation = newRot;
                updated.scale    = newScale;
                authored.TryUpdateRecord(idx, updated);
                authored.PackBlob();
                EditorUtility.SetDirty(authored);
            }
        }
    }
}
