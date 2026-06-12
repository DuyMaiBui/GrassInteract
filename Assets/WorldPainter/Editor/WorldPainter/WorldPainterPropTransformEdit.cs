#nullable enable
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Transform-edit mode for Prop layers (P7).
    ///
    /// Handles SceneView input when <see cref="WorldPainterPropLayerCard.Mode"/> ==
    /// <see cref="PropPlacementMode.Transform"/>:
    ///   - Left-click: pick the nearest <see cref="InstanceRecord"/> within a screen-space
    ///     tolerance of the cursor.
    ///   - Selected instance: renders a <see cref="Handles"/> Position / Rotation / Scale
    ///     gizmo (unified free-move handle) in SceneView.
    ///   - Gizmo drag: writes the updated TRS back to the per-tile bucket via
    ///     <see cref="AuthoredInstancesData.TryUpdateRecord"/>.
    ///
    /// Keyboard:
    ///   - <see cref="WorldPainterPropLayerCard.TOGGLE_KEY"/> (T): toggle back to Scatter mode.
    ///   - Escape: deselect the current instance.
    ///
    /// Call <see cref="OnSceneGUI"/> from the owning editor tool's OnToolGUI / DrawHandle.
    /// Call <see cref="Reset"/> whenever the active layer or painter changes.
    /// </summary>
    internal sealed class WorldPainterPropTransformEdit
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Max world-space distance from cursor ray for click-pick.</summary>
        private const float PICK_RADIUS_M = 1.5f;

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Index into <see cref="AuthoredInstancesData.WorkingList"/> for the selected instance.
        /// -1 = none selected.
        /// </summary>
        public int SelectedIndex { get; private set; } = -1;

        private readonly WorldPainterPropLayerCard card;

        // ── Construction ──────────────────────────────────────────────────────

        public WorldPainterPropTransformEdit(WorldPainterPropLayerCard card)
        {
            this.card = card;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Deselect the current instance and reset internal state.
        /// Call whenever the active layer, painter, or mode changes.
        /// </summary>
        public void Reset()
        {
            this.SelectedIndex = -1;
        }

        /// <summary>
        /// Process SceneView GUI events for Transform mode.
        /// Should be called from the active EditorTool's <c>OnToolGUI</c> when
        /// <see cref="WorldPainterPropLayerCard.Mode"/> == <see cref="PropPlacementMode.Transform"/>.
        /// </summary>
        public void OnSceneGUI(InstanceScatterLayer layer, SceneView sceneView)
        {
            var authored = layer.AuthoredInstances;
            if (authored == null) return;

            var currentEvent = Event.current;

            // ── Keyboard shortcuts ────────────────────────────────────────────

            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.T ||
                    currentEvent.character == WorldPainterPropLayerCard.TOGGLE_KEY)
                {
                    this.card.ToggleMode();
                    currentEvent.Use();
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
                int picked = PickInstance(authored, currentEvent.mousePosition, sceneView.camera);
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

        private static int PickInstance(
            AuthoredInstancesData authored,
            Vector2               mousePos,
            Camera                cam)
        {
            var ray       = HandleUtility.GUIPointToWorldRay(mousePos);
            var list      = authored.WorkingList;
            int best      = -1;
            float bestDist = PICK_RADIUS_M;

            for (int i = 0; i < list.Count; i++)
            {
                Vector3 worldPos = list[i].position;
                // Distance from ray to point (for click-pick, closest along ray wins).
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

            // Draw small spheres at each instance position.
            for (int i = 0; i < list.Count; i++)
            {
                bool isSel = i == selectedIdx;
                Handles.color = isSel ? Color.yellow : new Color(0.3f, 0.8f, 1f, 0.6f);
                float size = HandleUtility.GetHandleSize(list[i].position) * (isSel ? 0.12f : 0.06f);
                Handles.SphereHandleCap(0, list[i].position, Quaternion.identity, size, EventType.Repaint);
            }

            Handles.color = Color.white;
        }

        // ── Transform gizmo ───────────────────────────────────────────────────

        private void DrawTransformGizmo(
            InstanceScatterLayer  layer,
            AuthoredInstancesData authored,
            int                   idx)
        {
            var list = authored.WorkingList;
            if (idx < 0 || idx >= list.Count) return;

            var rec = list[idx];

            EditorGUI.BeginChangeCheck();

            // Position handle.
            var newPos = Handles.PositionHandle(rec.position, rec.rotation);

            // Rotation disc handle.
            float handleSize = HandleUtility.GetHandleSize(rec.position);
            var newRot = Handles.RotationHandle(rec.rotation, rec.position);

            // Scale — draw a labeled disc showing current scale; use ScaleSlider for uniform scale.
            float newScale = Handles.ScaleSlider(rec.scale, rec.position,
                rec.rotation * Vector3.up, rec.rotation, handleSize, 0.01f);
            newScale = Mathf.Max(0.01f, newScale);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(authored, "Prop Transform Edit");

                var updated = rec;
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
