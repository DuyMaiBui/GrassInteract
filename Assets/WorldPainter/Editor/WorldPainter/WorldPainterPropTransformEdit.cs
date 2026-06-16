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
                int picked = PickInstance(layer, authored, currentEvent.mousePosition);
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
        /// Picks an instance under <paramref name="mousePos"/>. Primary test: cast the cursor ray
        /// against each instance's LOD0 mesh bounds (transformed by its TRS) so clicking ANYWHERE
        /// on the visible mesh selects it — not just near the pivot point. Fallback: nearest pivot
        /// within a screen-space pixel radius (for thin/flat meshes the ray misses, or before a
        /// LOD0 mesh is assigned). The legacy pivot-only test forced the user to click exactly on
        /// the instance origin, which is usually buried inside or below the mesh.
        /// </summary>
        private static int PickInstance(PropLayer layer, AuthoredInstancesData authored, Vector2 mousePos)
        {
            var list   = authored.WorkingList;
            var camera = SceneView.currentDrawingSceneView?.camera ?? Camera.current;
            if (camera == null || list.Count == 0) return -1;

            // ── Primary: ray vs each instance's LOD0 mesh OBB ──────────────────
            Mesh? mesh = ResolveLod0Mesh(layer);
            if (mesh != null)
            {
                Ray     ray     = HandleUtility.GUIPointToWorldRay(mousePos);
                Bounds  local   = mesh.bounds;
                int     bestHit = -1;
                float   bestT   = float.MaxValue;
                for (int i = 0; i < list.Count; i++)
                {
                    var rec = list[i];
                    Matrix4x4 trs = Matrix4x4.TRS(rec.position, rec.rotation, Vector3.one * rec.scale);
                    if (RayHitsLocalBounds(trs, local, ray, out float t) && t < bestT)
                    {
                        bestT   = t;
                        bestHit = i;
                    }
                }
                if (bestHit >= 0) return bestHit;
            }

            // ── Fallback: nearest pivot within PICK_RADIUS_PX screen pixels ────
            int   best   = -1;
            float bestPx = PICK_RADIUS_PX;
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 sp = HandleUtility.WorldToGUIPoint(list[i].position);
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

        /// <summary>Resolves the layer's LOD0 mesh, or null when none is assigned.</summary>
        private static Mesh? ResolveLod0Mesh(PropLayer layer)
        {
            var lods = layer.Render.Lods;
            return (lods != null && lods.Length > 0) ? lods[0].mesh : null;
        }

        /// <summary>
        /// Ray vs an axis-aligned box expressed in an object's LOCAL space (the box is
        /// <paramref name="localBounds"/>, the object is placed by <paramref name="trs"/>).
        /// Transforms the world ray into local space and runs the slab test. Returns the nearest
        /// non-negative hit distance (in the transformed ray's parameter space) via <paramref name="tHit"/>.
        /// </summary>
        private static bool RayHitsLocalBounds(Matrix4x4 trs, Bounds localBounds, Ray worldRay, out float tHit)
        {
            tHit = 0f;
            Matrix4x4 inv = trs.inverse;
            Vector3 o = inv.MultiplyPoint3x4(worldRay.origin);
            Vector3 d = inv.MultiplyVector(worldRay.direction);

            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;
            float tmin = float.NegativeInfinity;
            float tmax = float.PositiveInfinity;
            for (int a = 0; a < 3; a++)
            {
                float oa = o[a], da = d[a];
                if (Mathf.Abs(da) < 1e-8f)
                {
                    if (oa < min[a] || oa > max[a]) return false; // parallel to slab & outside
                }
                else
                {
                    float t1 = (min[a] - oa) / da;
                    float t2 = (max[a] - oa) / da;
                    if (t1 > t2) { (t1, t2) = (t2, t1); }
                    tmin = Mathf.Max(tmin, t1);
                    tmax = Mathf.Min(tmax, t2);
                    if (tmin > tmax) return false;
                }
            }
            if (tmax < 0f) return false;          // box entirely behind the ray
            tHit = tmin >= 0f ? tmin : tmax;       // origin outside vs inside the box
            return true;
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

            // Show ONE handle at a time, selected by the active Unity transform tool
            // (W = Move, E = Rotate, R = Scale). Drawing Position/Rotation/Scale handles all at the
            // same point made them overlap and steal each other's control IDs — the Scale handle's
            // centre cube sat under the Position free-move dot and could never be grabbed (the
            // "scale doesn't work" bug). Gating by Tools.current matches Unity's native multi-tool
            // UX and makes every handle reliably grabbable.
            switch (Tools.current)
            {
                case Tool.Rotate:
                {
                    EditorGUI.BeginChangeCheck();
                    Quaternion newRot = Handles.RotationHandle(rec.rotation, rec.position);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(authored, "Rotate Prop Instance");
                        rec.rotation = newRot;
                        CommitRecord(layer, authored, idx, rec);
                    }
                    break;
                }

                case Tool.Scale:
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 oldScale = Vector3.one * rec.scale;
                    Vector3 newScale = Handles.ScaleHandle(
                        oldScale, rec.position, rec.rotation,
                        HandleUtility.GetHandleSize(rec.position));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(authored, "Scale Prop Instance");
                        // Uniform scale: take whichever axis the user dragged. The centre cube
                        // changes all three equally; an individual axis box changes only that one.
                        // Reading only newScale.x silently ignored Y/Z-axis drags — the other half
                        // of the "scale doesn't work" bug.
                        float newUniform = rec.scale;
                        if      (Mathf.Abs(newScale.x - oldScale.x) > 1e-6f) newUniform = newScale.x;
                        else if (Mathf.Abs(newScale.y - oldScale.y) > 1e-6f) newUniform = newScale.y;
                        else if (Mathf.Abs(newScale.z - oldScale.z) > 1e-6f) newUniform = newScale.z;
                        rec.scale = Mathf.Max(0.0001f, newUniform);
                        CommitRecord(layer, authored, idx, rec);
                    }
                    break;
                }

                default: // Tool.Move and any non-transform tool → position handle
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPos = Handles.PositionHandle(rec.position, rec.rotation);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(authored, "Move Prop Instance");
                        rec.position = newPos;
                        CommitRecord(layer, authored, idx, rec);
                    }
                    break;
                }
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
