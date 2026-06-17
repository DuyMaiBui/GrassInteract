#nullable enable
using System.Linq;
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>Which transform handle the Select tool currently shows for the picked instance.</summary>
    internal enum PropTransformMode { Move, Rotate, Scale }

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
    /// Keyboard (handled directly here — the Select tool runs through
    /// <see cref="SceneView.duringSceneGui"/>, so Unity's global W/E/R tool hotkeys do NOT
    /// reach it and <c>Tools.current</c> can never be relied on to pick the handle):
    ///   - W / E / R: switch the active handle to Move / Rotate / Scale.
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

        /// <summary>
        /// Active transform handle for the selected instance. Owned here instead of read from
        /// Unity's <c>Tools.current</c>: this tool dispatches via <see cref="SceneView.duringSceneGui"/>
        /// while the user drives it from the WorldPainter inspector window, so the global W/E/R
        /// hotkeys never update <c>Tools.current</c> — it stays frozen on whatever was last active
        /// (the "only the rotation handle ever shows" bug). Set by the HUD buttons or W/E/R here.
        /// </summary>
        public PropTransformMode Mode { get; set; } = PropTransformMode.Move;

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
            cachedPatchLayer  = null;
            cachedPatchEngine = null;
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

            // Root transform (painting → world) so the Select gizmos + click-pick stay aligned
            // with the rendered props under a non-identity WorldPainter root. Null → treat as
            // identity (handles operate directly in world, unchanged from pre-feature behaviour).
            Transform? root = ResolveRootTransform(layer);

            var currentEvent = Event.current;

            // ── Keyboard shortcuts ────────────────────────────────────────────

            if (currentEvent.type == EventType.KeyDown)
            {
                // W/E/R switch the active handle. Handled here because Unity's global tool hotkeys
                // don't reach a duringSceneGui consumer driven from the inspector window.
                if (currentEvent.keyCode == KeyCode.W) { this.Mode = PropTransformMode.Move;   currentEvent.Use(); HandleUtility.Repaint(); return; }
                if (currentEvent.keyCode == KeyCode.E) { this.Mode = PropTransformMode.Rotate; currentEvent.Use(); HandleUtility.Repaint(); return; }
                if (currentEvent.keyCode == KeyCode.R) { this.Mode = PropTransformMode.Scale;  currentEvent.Use(); HandleUtility.Repaint(); return; }

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

                // Delete / Backspace removes the selected instance — mirrors the HUD Remove button.
                if (currentEvent.keyCode == KeyCode.Delete ||
                    currentEvent.keyCode == KeyCode.Backspace)
                {
                    if (this.SelectedIndex >= 0)
                    {
                        this.RemoveSelected(layer);
                        currentEvent.Use();
                        HandleUtility.Repaint();
                        return;
                    }
                }
            }

            // ── Draw the transform handle FIRST ────────────────────────────────
            // The PositionHandle / RotationHandle / ScaleSlider each register their own
            // controlIDs and consume MouseDown when the cursor is on a handle axis. If we
            // run the click-pick first and Use() the event, the handles never see the click
            // and the user can't drag them — which is exactly the bug the user reported.

            if (this.SelectedIndex >= 0)
                this.DrawTransformGizmo(layer, authored, this.SelectedIndex, root);

            // ── Per-instance selection gizmos (dots) ──────────────────────────

            DrawInstanceGizmos(authored, this.SelectedIndex, root);

            // ── Click-pick — only if no handle consumed the event yet ─────────

            if (currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                !currentEvent.alt)
            {
                int picked = PickInstance(layer, authored, currentEvent.mousePosition, root);
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

        // ── Remove selected instance ──────────────────────────────────────────

        /// <summary>
        /// Removes the currently selected instance from <paramref name="layer"/> and rebuilds the
        /// scatter mesh. No-op when nothing is selected. Called by the HUD Remove button and the
        /// Delete / Backspace shortcut.
        ///
        /// <see cref="AuthoredInstancesData.RemoveAt"/> is swap-pop, so the removed slot now holds a
        /// different record — the selection index is cleared rather than reused. The rebuild is
        /// scheduled immediately (unlike a handle drag) because removal is a single discrete action,
        /// not a per-frame storm.
        /// </summary>
        public void RemoveSelected(PropLayer layer)
        {
            var authored = layer.AuthoredInstances;
            if (authored == null) return;

            int idx = this.SelectedIndex;
            if (idx < 0 || idx >= authored.WorkingList.Count) return;

            Undo.RecordObject(authored, "Remove Prop Instance");
            authored.RemoveAt(idx);
            authored.PackBlob();
            EditorUtility.SetDirty(authored);

            this.SelectedIndex = -1;
            WorldPainterRebuildScheduler.MarkPropDirty(layer);
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
        private static int PickInstance(PropLayer layer, AuthoredInstancesData authored, Vector2 mousePos, Transform? root)
        {
            var list   = authored.WorkingList;
            var camera = SceneView.currentDrawingSceneView?.camera ?? Camera.current;
            if (camera == null || list.Count == 0) return -1;

            // Painting → world. Instance records are painting-space; the scene ray + GUI points are
            // world. Prepend the root matrix so picking hits the RENDERED prop under a non-identity root.
            Matrix4x4 rootM = root != null ? root.localToWorldMatrix : Matrix4x4.identity;

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
                    Matrix4x4 trs = rootM * Matrix4x4.TRS(rec.position, rec.rotation, Vector3.one * rec.scale);
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
                Vector3 sp = HandleUtility.WorldToGUIPoint(rootM.MultiplyPoint3x4(list[i].position));
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

        private static void DrawInstanceGizmos(AuthoredInstancesData authored, int selectedIdx, Transform? root)
        {
            var list = authored.WorkingList;
            if (list.Count == 0) return;

            // Painting → world so the dots sit on the rendered props under a non-identity root.
            Matrix4x4 rootM = root != null ? root.localToWorldMatrix : Matrix4x4.identity;

            for (int i = 0; i < list.Count; i++)
            {
                bool isSel = i == selectedIdx;
                Handles.color = isSel
                    ? Color.yellow
                    : new Color(0.3f, 0.8f, 1f, 0.6f);
                Vector3 worldPos = rootM.MultiplyPoint3x4(list[i].position);
                float size = HandleUtility.GetHandleSize(worldPos) * (isSel ? 0.12f : 0.06f);
                Handles.SphereHandleCap(0, worldPos, Quaternion.identity, size, EventType.Repaint);
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
            int                   idx,
            Transform?            root)
        {
            var list = authored.WorkingList;
            if (idx < 0 || idx >= list.Count) return;

            var rec = list[idx];

            // Records are PAINTING space; handles operate in world. Draw the handle at the prop's
            // WORLD pose, then map the edited result back to painting on write-back. Identity/null
            // root → world == painting (passthrough), unchanged from pre-feature behaviour.
            Quaternion rootRot  = root != null ? root.rotation : Quaternion.identity;
            Vector3    worldPos = root != null ? root.TransformPoint(rec.position) : rec.position;
            Quaternion worldRot = rootRot * rec.rotation;

            // Show ONE handle at a time, selected by this tool's own Mode (set by the HUD buttons
            // or W/E/R here). Drawing Position/Rotation/Scale handles all at the same point made
            // them overlap and steal each other's control IDs — the Scale handle's centre cube sat
            // under the Position free-move dot and could never be grabbed (the "scale doesn't work"
            // bug). Mode is NOT read from Tools.current: the global tool hotkeys never reach this
            // duringSceneGui consumer, so Tools.current freezes and only one handle would ever show.
            switch (this.Mode)
            {
                case PropTransformMode.Rotate:
                {
                    EditorGUI.BeginChangeCheck();
                    Quaternion newWorldRot = Handles.RotationHandle(worldRot, worldPos);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(authored, "Rotate Prop Instance");
                        // Map the world rotation back to painting space (strip the root rotation).
                        rec.rotation = Quaternion.Inverse(rootRot) * newWorldRot;
                        CommitRecord(layer, authored, idx, rec);
                    }
                    break;
                }

                case PropTransformMode.Scale:
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 oldScale = Vector3.one * rec.scale;
                    // Scale magnitude is root-independent; draw the handle at the world pose.
                    Vector3 newScale = Handles.ScaleHandle(
                        oldScale, worldPos, worldRot,
                        HandleUtility.GetHandleSize(worldPos));
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

                default: // PropTransformMode.Move → position handle
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newWorldPos = Handles.PositionHandle(worldPos, worldRot);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(authored, "Move Prop Instance");
                        // Map the world position back to painting space.
                        rec.position = root != null ? root.InverseTransformPoint(newWorldPos) : newWorldPos;
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

            // Real-time preview: patch the single GPU instance slot in place so the rendered mesh tracks
            // the handle every frame. This overwrites one 20-byte record in the existing instance buffer
            // (no realloc), so it avoids the GPU-buffer-teardown flicker that forced full rebuilds to be
            // deferred to mouse-up. The deferred full rebuild below STILL runs on release to restore the
            // exact per-chunk AABB + collider pool. No-op when no live engine exists yet (mesh not built).
            TryLivePatch(layer, idx, rec);

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
            // The scheduled rebuild replaces the engine instance, so drop the cached engine — the next
            // drag re-resolves and binds to the freshly-built engine.
            cachedPatchEngine = null;
            pendingRebuild = false;
            pendingLayer   = null;
        }

        // ── Live in-place patch resolver ──────────────────────────────────────
        // Cache the resolved engine for the active layer so a continuous drag doesn't run
        // FindObjectsByType every EndChangeCheck (30-60×/s). Invalidated on Reset() (layer/painter
        // change) and after the deferred rebuild fires (which replaces the engine instance).

        private static PropLayer?           cachedPatchLayer;
        private static InstancedPropEngine? cachedPatchEngine;

        /// <summary>
        /// Patches <paramref name="rec"/>'s transform into the live prop engine's GPU buffer in place,
        /// for real-time handle-drag preview. No-op when no engine exists yet — the deferred rebuild on
        /// mouse-up then provides the update. See <see cref="ChunkedInstanceBuffer.PatchInstance"/>.
        /// </summary>
        private static void TryLivePatch(PropLayer layer, int authoredIdx, InstanceRecord rec)
        {
            InstancedPropEngine? engine = ResolvePropEngine(layer);
            engine?.PatchInstanceTransform(authoredIdx, rec.position, rec.rotation, rec.scale);
        }

        /// <summary>
        /// Finds the live <see cref="InstancedPropEngine"/> for <paramref name="layer"/> across the scene's
        /// WorldPainters, caching the result for the duration of a drag. Mirrors the painter-resolution
        /// pattern in <see cref="WorldPainterRebuildScheduler.MarkPropDirty"/>.
        /// </summary>
        /// <summary>
        /// Resolves the WorldPainter root Transform owning <paramref name="layer"/> (= painting space),
        /// so the Select gizmos + click-pick map painting coords to the rendered world position under
        /// a non-identity root. Returns null when no owning painter is found (→ treated as identity).
        /// </summary>
        private static Transform? ResolveRootTransform(PropLayer layer)
        {
            var painters = Object.FindObjectsByType<WorldPainter>(FindObjectsSortMode.None);
            for (int i = 0; i < painters.Length; ++i)
            {
                WorldPainter p = painters[i];
                if (p == null || p.Map == null) continue;
                if (!p.Map.SurfaceLayers.Contains(layer)) continue;
                return p.transform;
            }
            return null;
        }

        private static InstancedPropEngine? ResolvePropEngine(PropLayer layer)
        {
            if (ReferenceEquals(cachedPatchLayer, layer) && cachedPatchEngine != null)
                return cachedPatchEngine;

            var painters = Object.FindObjectsByType<WorldPainter>(FindObjectsSortMode.None);
            for (int i = 0; i < painters.Length; ++i)
            {
                WorldPainter p = painters[i];
                if (p == null || p.Map == null) continue;
                if (!p.Map.SurfaceLayers.Contains(layer)) continue;

                InstancedPropEngine? engine = p.TryGetPropEngine(layer);
                if (engine != null)
                {
                    cachedPatchLayer  = layer;
                    cachedPatchEngine = engine;
                    return engine;
                }
            }
            return null;
        }
    }
}
