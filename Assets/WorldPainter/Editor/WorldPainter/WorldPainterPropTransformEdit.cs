#nullable enable
using System.Linq;
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Transform-edit mode for unified <see cref="PropLayer"/> layers.
    ///
    /// Activated by selecting the "Select" tool (<c>instance.select</c>) in the prop tool
    /// palette — the sculpt tool routes SceneView input here instead of through the brush.
    ///
    ///   - Left-click: pick the nearest <see cref="InstanceRecord"/> within a screen-space
    ///     tolerance of the cursor.
    ///   - Selected instance: renders a <see cref="Handles"/> Position / Rotation / Scale
    ///     gizmo in SceneView.
    ///   - Gizmo drag: writes the updated TRS back to the layer's
    ///     <see cref="AuthoredInstancesData"/>.
    ///
    /// Keyboard:
    ///   - T: switch back to the Place tool.
    ///   - Escape: deselect the current instance.
    ///
    /// Call <see cref="Reset"/> whenever the active layer or painter changes.
    /// </summary>
    internal sealed class WorldPainterPropTransformEdit
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Keyboard key that returns from Select to Place tool.</summary>
        public const char TOGGLE_KEY = 't';

        /// <summary>Pixel tolerance for click-pick (screen-space). Independent of camera distance.</summary>
        private const float PICK_RADIUS_PX = 20f;

        // ── Per-instance selection state ──────────────────────────────────────

        /// <summary>
        /// Index into <see cref="AuthoredInstancesData.WorkingList"/> for the selected instance.
        /// -1 = none selected.
        /// </summary>
        public int SelectedIndex { get; private set; } = -1;

        // ── Deferred rebuild state ────────────────────────────────────────────
        // CommitRecord records a "pending Mark" instead of firing the scheduler immediately, so
        // a continuous handle drag doesn't tear down the engine's argsLodN GraphicsBuffers under
        // a still-pending RenderMeshIndirect from the previous frame (the cause of the black-
        // square flicker in Game / Inspector views). OnSceneGUI flushes the pending Mark once
        // the hotControl is released (EventType.MouseUp / hotControl==0).

        private static bool        pendingRebuild;
        private static PropLayer?  pendingLayer;

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
        /// Process SceneView GUI events while the "Select" prop tool is active.
        /// Called by <see cref="WorldPainterSculptTool.OnSceneGui"/> when the active brush tool
        /// id is <c>"instance.select"</c>.
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
                    // T returns to the Place tool — the sculpt tool's OnSceneGui watches the
                    // active brush tool id and switches back into the brush-stamp path.
                    WorldPainterState.SetActiveBrushTool("instance.place");
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

            // ── Draw the transform handle FIRST ────────────────────────────────
            // The PositionHandle / RotationHandle / ScaleSlider each register their own
            // controlIDs and consume MouseDown when the cursor is on a handle axis. If we
            // run the click-pick first and Use() the event, the handles never see the click
            // and the user can't drag them — which is exactly the bug the user reported.

            if (this.SelectedIndex >= 0)
                this.DrawTransformGizmo(layer, authored, this.SelectedIndex);

            // ── Per-instance selection gizmos (dots) ──────────────────────────

            DrawInstanceGizmos(authored, this.SelectedIndex);

            // ── Click-pick — only if no handle consumed the event yet ─────────

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

            // After all event processing — if a deferred rebuild from a handle drag is pending
            // and the mouse has been released, flush it now. This is the single rebuild that
            // refreshes the scatter mesh after the user finishes dragging.
            FlushPendingRebuildIfReleased();
        }

        // ── Click-pick ────────────────────────────────────────────────────────

        /// <summary>
        /// Picks the nearest instance to <paramref name="mousePos"/> using SCREEN-SPACE pixel
        /// distance. The legacy implementation used world-space perpendicular distance, which
        /// shrank to almost nothing on-screen when the camera was far away — making selection
        /// impossible in typical Scene-view viewing distances.
        /// </summary>
        private static int PickInstance(AuthoredInstancesData authored, Vector2 mousePos)
        {
            var list      = authored.WorkingList;
            int best      = -1;
            float bestPx  = PICK_RADIUS_PX;
            var camera    = SceneView.currentDrawingSceneView?.camera ?? Camera.current;
            if (camera == null) return -1;

            for (int i = 0; i < list.Count; i++)
            {
                Vector3 worldPos = list[i].position;
                Vector3 sp       = HandleUtility.WorldToGUIPoint(worldPos);
                float dx = sp.x - mousePos.x;
                float dy = sp.y - mousePos.y;
                float px = Mathf.Sqrt(dx * dx + dy * dy);
                if (px < bestPx)
                {
                    bestPx = px;
                    best   = i;
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

        /// <summary>
        /// Draws the three transform handles each in their OWN BeginChangeCheck block so the
        /// dragged channel writes back without clobbering the un-dragged ones, then rebuilds the
        /// prop engine immediately so the visual mesh tracks the handle in real time.
        ///
        /// Pattern lifted directly from the proven implementation in commit 35e2def's
        /// <c>InstancePlacementTool.DrawSingleSelectHandles</c>. The previous "one big
        /// BeginChangeCheck" caused all three handle reads to be flushed back on any single drag,
        /// AND skipped the engine rebuild — which made the data save but the mesh stay put.
        /// </summary>
        private void DrawTransformGizmo(
            PropLayer             layer,
            AuthoredInstancesData authored,
            int                   idx)
        {
            var list = authored.WorkingList;
            if (idx < 0 || idx >= list.Count) return;

            var rec = list[idx];

            // ── Position handle ────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(rec.position, rec.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(authored, "Move Prop Instance");
                rec.position = newPos;
                CommitRecord(layer, authored, idx, rec);
                return; // one operation per frame is enough
            }

            // ── Rotation handle ────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.RotationHandle(rec.rotation, rec.position);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(authored, "Rotate Prop Instance");
                rec.rotation = newRot;
                CommitRecord(layer, authored, idx, rec);
                return;
            }

            // ── Scale handle ───────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            Vector3 newScaleVec = Handles.ScaleHandle(
                Vector3.one * rec.scale,
                rec.position,
                rec.rotation,
                HandleUtility.GetHandleSize(rec.position));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(authored, "Scale Prop Instance");
                rec.scale = Mathf.Max(0.0001f, newScaleVec.x);
                CommitRecord(layer, authored, idx, rec);
            }
        }

        /// <summary>
        /// Writes <paramref name="rec"/> back to <paramref name="authored"/>, persists, and
        /// schedules a coalesced rebuild via <see cref="WorldPainterRebuildScheduler"/>.
        ///
        /// The previous version called <see cref="WorldPainter.RebuildPropLayer"/> inline on every
        /// EndChangeCheck firing — which during a single handle drag is 30-60 times per second.
        /// That tore down and re-allocated GPU buffers fast enough that the Game-view camera
        /// (which renders continuously in edit mode) caught a partially-disposed engine every few
        /// frames, producing the visible black-tile flicker in Game/Inspector views. Routing
        /// through the scheduler collapses the storm to one rebuild per editor frame.
        /// </summary>
        private static void CommitRecord(PropLayer layer, AuthoredInstancesData authored,
            int idx, InstanceRecord rec)
        {
            authored.TryUpdateRecord(idx, rec);
            authored.PackBlob();
            EditorUtility.SetDirty(authored);

            // Defer engine rebuild until the handle is released — see "Deferred rebuild state"
            // comment at the top of the class. Marking dirty here every EndChangeCheck (30-60×/s
            // during a drag) caused the black-square flicker: each rebuild Disposed argsLodN
            // GraphicsBuffers that a still-pending RenderMeshIndirect from the previous frame
            // referenced. FlushPendingRebuildIfReleased() drains this on MouseUp.
            pendingRebuild = true;
            pendingLayer   = layer;
        }

        /// <summary>
        /// Called from <see cref="OnSceneGUI"/> after the handles have processed the current
        /// event. If a transform commit happened during a drag AND the mouse has now been
        /// released (hotControl == 0 or the event is a MouseUp), schedules the deferred rebuild.
        /// </summary>
        private static void FlushPendingRebuildIfReleased()
        {
            if (!pendingRebuild || pendingLayer == null) return;
            if (GUIUtility.hotControl != 0 && Event.current.type != EventType.MouseUp) return;

            WorldPainterRebuildScheduler.MarkPropDirty(pendingLayer);
            pendingRebuild = false;
            pendingLayer   = null;
        }
    }
}
