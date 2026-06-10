#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Transform-tool-style placement editor for an <see cref="InstanceScatterLayer"/>. Supports
    /// three <see cref="PlaceMode"/>s: <see cref="PlaceMode.Place"/> (single click),
    /// <see cref="PlaceMode.Scatter"/> (brush-radius flood), <see cref="PlaceMode.Select"/>
    /// (single + multi-select with batch transform/collider edit), and <see cref="PlaceMode.Erase"/>.
    ///
    /// All tool state is read from <see cref="ScatterAuthoringState.I"/> — no <c>EditorPrefs</c> reads.
    /// All edits flow through <see cref="AuthoredInstancesData"/> with Undo and the debounced
    /// <see cref="ScatterRebuildScheduler"/>.
    ///
    /// In-scene HUD is minimal (cursor disc + mode label). Per-instance/batch editing lives in
    /// <see cref="InstancePanel"/> (in-window).
    /// </summary>
    // Global (unscoped) EditorTool — no typeof() target.
    // InstanceScatterLayer is a ScriptableObject sub-asset; Unity's tool context does NOT
    // track sub-asset selections as typed EditorTool targets, so specifying
    // typeof(InstanceScatterLayer) silently prevents activation (same issue as DensityPaintTool).
    // The active layer is read from ScatterAuthoringState.I.ActiveInstanceLayer instead of
    // this.target. InstancePanel.BindLayer() keeps that field in sync.
    [EditorTool("Instance Placement")]
    internal sealed class InstancePlacementTool : EditorTool
    {
        internal enum PlaceMode { Place = 0, Select = 1, Erase = 2, Scatter = 3, Anchor = 4 }

        // ── State shortcuts (read ScatterAuthoringState) ───────────────────────

        private static PlaceMode Mode
        {
            get => (PlaceMode)ScatterAuthoringState.I.PlaceMode;
            set => ScatterAuthoringState.I.PlaceMode = (int)value;
        }

        private static bool  AlignToNormal  => ScatterAuthoringState.I.AlignToNormal;
        private static float ScaleMin       => ScatterAuthoringState.I.PlaceScaleMin;
        private static float ScaleMax       => ScatterAuthoringState.I.PlaceScaleMax;
        private static float EraseRadius    => ScatterAuthoringState.I.EraseRadius;
        private static float BrushSize      => ScatterAuthoringState.I.BrushSize;

        // ── Constants ──────────────────────────────────────────────────────────

        // Pick radius: pixels from the instance pivot. Overridden per-instance by mesh size below.
        private const float PICK_PIXELS_MIN  = 8f;
        private const int   MAX_DRAW        = 4000;
        private const float DRAW_RADIUS     = 60f;
        private const int   MAX_SCATTER_PER_STROKE = 64; // cap to avoid O(n²) stalls

        // ── Per-tool state ─────────────────────────────────────────────────────

        private int selectedIndex = -1;
        private readonly HashSet<int> multiSelection = new();

        // ── EditorTool ─────────────────────────────────────────────────────────

        public override GUIContent toolbarIcon => EditorGUIUtility.IconContent("Transform Icon");

        public override void OnActivated()
        {
            InstancePlacementToolTracker.ActiveTool = this;
        }

        public override void OnWillBeDeactivated()
        {
            if (ReferenceEquals(InstancePlacementToolTracker.ActiveTool, this))
                InstancePlacementToolTracker.ActiveTool = null;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView sv) return;
            InstanceScatterLayer? layer = ScatterAuthoringState.I.ActiveInstanceLayer;
            if (layer == null) return;

            AuthoredInstancesData? authored = layer.AuthoredInstances;
            (ScatterField? field, int layerIdx) = ScatterFieldLookup.FindOwningField(layer);

            // Minimal HUD: just cursor disc + mode label in the top-left corner.
            this.DrawMinimalHud();

            if (authored == null || field == null) return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            // Claim the default scene control so clicks reach this tool instead of being consumed by
            // the Scene view's object-picking (otherwise place/erase clicks just change selection).
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            Vector3 camPos = sv.camera != null ? sv.camera.transform.position : Vector3.zero;
            this.DrawInstances(authored, camPos);

            LayerMask mask = field.ResolveGroundMask(layer);
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, mask.value == 0 ? ~0 : mask.value);

            switch (Mode)
            {
                case PlaceMode.Place:
                    this.OnPlace(e, controlId, authored, layer, field, layerIdx, hasHit, hit);
                    break;
                case PlaceMode.Scatter:
                    this.OnScatter(e, controlId, authored, layer, field, layerIdx, hasHit, hit);
                    break;
                case PlaceMode.Select:
                    this.OnSelect(e, controlId, authored, field, layerIdx, layer);
                    break;
                case PlaceMode.Erase:
                    this.OnErase(e, controlId, authored, field, layerIdx, hasHit, hit);
                    break;
                case PlaceMode.Anchor:
                    this.OnAnchor(authored, layer, field, layerIdx);
                    break;
            }

            if (hasHit && Mode != PlaceMode.Select && Mode != PlaceMode.Anchor)
            {
                if (Mode == PlaceMode.Place)
                {
                    // Push ghost state — the actual DrawMeshNow fires from
                    // InstanceGhostPreview.OnSceneGui during the Repaint phase.
                    // Placement is unconditional (no spacing requirement), so the ghost is always valid.
                    InstanceGhostPreview.Set(layer, hit.point, hit.normal, spacingOk: true, visible: true);
                    // Wire disc: cursor ring + fallback when ghost has no LOD0 mesh.
                    ScatterGizmos.BrushDisc(hit.point, hit.normal, 0.5f, ScatterGizmos.BrushColor);
                }
                else
                {
                    // Not Place mode — hide any lingering ghost.
                    InstanceGhostPreview.Clear();
                    Color c = Mode == PlaceMode.Erase ? ScatterGizmos.EraseColor : ScatterGizmos.BrushColor;
                    float r = Mode == PlaceMode.Erase ? EraseRadius : BrushSize;
                    ScatterGizmos.BrushDisc(hit.point, hit.normal, r, c);
                }
                HandleUtility.Repaint();
            }
            else
            {
                // No hit or Select mode — ensure ghost is hidden.
                InstanceGhostPreview.Clear();
            }
        }

        // ── Minimal HUD ────────────────────────────────────────────────────────

        private void DrawMinimalHud()
        {
            Handles.BeginGUI();
            var area = new Rect(8, 8, 200, 32f);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"Placement: {Mode}", EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        // ── Place (single click) ───────────────────────────────────────────────

        private void OnPlace(Event e, int controlId, AuthoredInstancesData authored, InstanceScatterLayer layer,
            ScatterField field, int layerIdx, bool hasHit, RaycastHit hit)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && hasHit)
            {
                GUIUtility.hotControl = controlId;
                // Free placement — no spacing requirement; every click places an instance.
                Undo.RegisterCompleteObjectUndo(authored, "Place Instance");
                authored.AddRecord(this.BuildRecord(hit.point, hit.normal, layer));
                // Use RebuildImmediate so the instance appears in the scene immediately on
                // click — MarkDirty has a 150ms debounce which feels laggy for single-clicks.
                authored.PackBlob();
                EditorUtility.SetDirty(authored);
                if (layerIdx >= 0) ScatterRebuildScheduler.RebuildImmediate(field, layerIdx);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                GUIUtility.hotControl = 0;
            }
        }

        // ── Scatter (brush-radius flood) ───────────────────────────────────────

        private void OnScatter(Event e, int controlId, AuthoredInstancesData authored, InstanceScatterLayer layer,
            ScatterField field, int layerIdx, bool hasHit, RaycastHit hit)
        {
            bool isDown = e.type == EventType.MouseDown && e.button == 0 && !e.alt && hasHit;
            if (!isDown) return;

            GUIUtility.hotControl = controlId;
            float radius = BrushSize;
            LayerMask mask = field.ResolveGroundMask(layer);

            // Generate candidate positions within the brush disc and place each that lands on
            // the ground. Candidates are capped at MAX_SCATTER_PER_STROKE to bound the stroke
            // cost. No spacing requirement — overlapping/dense clumps are allowed.
            bool anyPlaced = false;
            int attempts = 0;
            int maxAttempts = MAX_SCATTER_PER_STROKE * 4; // generous attempt budget
            int placed = 0;

            // We build a single undo record for the whole stroke.
            Undo.RegisterCompleteObjectUndo(authored, "Scatter Instances");

            while (attempts < maxAttempts && placed < MAX_SCATTER_PER_STROKE)
            {
                attempts++;
                // Random point in disc (rejection sampling).
                Vector2 rnd = Random.insideUnitCircle * radius;
                Vector3 candidate = hit.point + new Vector3(rnd.x, 0f, rnd.y);

                // Project the candidate onto the ground via a short raycast from above.
                Ray probeRay = new Ray(candidate + Vector3.up * 50f, Vector3.down);
                if (!Physics.Raycast(probeRay, out RaycastHit probeHit, 100f, mask.value == 0 ? ~0 : mask.value))
                    continue;

                authored.AddRecord(this.BuildRecord(probeHit.point, probeHit.normal, layer));
                anyPlaced = true;
                placed++;
            }

            if (anyPlaced)
                Commit(authored, field, layerIdx);

            e.Use();
        }

        // ── Record factory ─────────────────────────────────────────────────────

        private InstanceRecord BuildRecord(Vector3 hitPoint, Vector3 normal, InstanceScatterLayer layer)
        {
            // No random yaw — placement is deterministic so the placed instance matches the ghost preview.
            Quaternion rot = AlignToNormal ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
            float scale = Random.Range(Mathf.Min(ScaleMin, ScaleMax), Mathf.Max(ScaleMin, ScaleMax));

            // Place FROM the anchor: offset the pivot so the deform anchor lands exactly on the clicked
            // point. At runtime the shader/sim sample from pivot + rot·(anchor·scale) == hitPoint.
            Vector3 pivot = hitPoint - rot * (layer.AnchorOffsetLocal * scale);

            return new InstanceRecord
            {
                position = pivot, rotation = rot, scale = scale,
                overrideMask = InstanceOverrideMask.None,
                colliderScale = 1f, colliderMeshRefIndex = -1, colliderMaterialRefIndex = -1,
            };
        }

        // ── Select + Transform (single + multi) ────────────────────────────────

        private void OnSelect(Event e, int controlId, AuthoredInstancesData authored,
            ScatterField field, int layerIdx, InstanceScatterLayer layer)
        {
            // ── Draw handles FIRST so they can register their control IDs and ──
            // ── claim hotControl on MouseDown before the pick logic sees the event.
            // If a handle grabs the drag, GUIUtility.hotControl != 0 / event is Used.
            if (this.multiSelection.Count == 0)
            {
                this.DrawSingleSelectHandles(authored, field, layerIdx);
            }
            else
            {
                // Multi-select: draw dots for each selected instance
                foreach (int idx in this.multiSelection)
                {
                    if (!authored.TryGetRecord(idx, out InstanceRecord r)) continue;
                    ScatterGizmos.InstanceDot(r.position, HandleUtility.GetHandleSize(r.position) * 0.12f, ScatterGizmos.SelectedColor);
                }
            }

            // ── Pick: only if this MouseDown was NOT consumed by a handle above ──
            // Guard: hotControl == 0 means no handle claimed the drag; proceed with pick.
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt
                && GUIUtility.hotControl == 0)
            {
                int picked = PickNearest(authored, e.mousePosition, layer);
                bool shift = e.shift;

                if (picked >= 0)
                {
                    if (shift)
                    {
                        // Toggle in multi-selection
                        if (this.multiSelection.Contains(picked))
                            this.multiSelection.Remove(picked);
                        else
                            this.multiSelection.Add(picked);
                        this.selectedIndex = picked;
                    }
                    else
                    {
                        // Single select — clear multi
                        this.multiSelection.Clear();
                        this.selectedIndex = picked;
                    }
                }
                else if (!shift)
                {
                    // Click empty — clear all
                    this.multiSelection.Clear();
                    this.selectedIndex = -1;
                }
                e.Use();
            }
        }

        private void DrawSingleSelectHandles(AuthoredInstancesData authored, ScatterField? field, int layerIdx)
        {
            if (this.selectedIndex < 0 || !authored.TryGetRecord(this.selectedIndex, out InstanceRecord rec))
                return;

            ScatterGizmos.InstanceDot(rec.position, HandleUtility.GetHandleSize(rec.position) * 0.12f, ScatterGizmos.SelectedColor);

            // ── Scene label: pos / rot(euler) / scale / collider state ────────
            bool colliderOn = (rec.overrideMask & InstanceOverrideMask.ColliderConfigured) != 0;
            Vector3 euler   = rec.rotation.eulerAngles;
            string label    = $"pos ({rec.position.x:F2}, {rec.position.y:F2}, {rec.position.z:F2})\n" +
                              $"rot ({euler.x:F1}, {euler.y:F1}, {euler.z:F1})\n" +
                              $"scale {rec.scale:F3}  collider:{(colliderOn ? "on" : "off")}";
            Handles.Label(rec.position + Vector3.up * (HandleUtility.GetHandleSize(rec.position) * 0.5f),
                label, EditorStyles.miniLabel);

            // ── All three handles drawn simultaneously ─────────────────────────
            // Each has its own Begin/EndChangeCheck so only the actively dragged
            // handle writes back and the others don't clobber the unchanged channels.

            // ── Position handle ────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(rec.position, rec.rotation);
            if (EditorGUI.EndChangeCheck() && field != null)
            {
                Undo.RegisterCompleteObjectUndo(authored, "Move Instance");
                rec.position = newPos;
                authored.SetRecord(this.selectedIndex, rec);
                Commit(authored, field, layerIdx);
                return; // one operation per frame is enough
            }

            // ── Rotation handle ────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.RotationHandle(rec.rotation, rec.position);
            if (EditorGUI.EndChangeCheck() && field != null)
            {
                Undo.RegisterCompleteObjectUndo(authored, "Rotate Instance");
                rec.rotation = newRot;
                authored.SetRecord(this.selectedIndex, rec);
                Commit(authored, field, layerIdx);
                return;
            }

            // ── Scale handle ───────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            Vector3 newScaleVec = Handles.ScaleHandle(
                Vector3.one * rec.scale,
                rec.position,
                rec.rotation,
                HandleUtility.GetHandleSize(rec.position));
            if (EditorGUI.EndChangeCheck() && field != null)
            {
                Undo.RegisterCompleteObjectUndo(authored, "Scale Instance");
                rec.scale = Mathf.Max(0.0001f, newScaleVec.x);
                authored.SetRecord(this.selectedIndex, rec);
                Commit(authored, field, layerIdx);
            }
        }

        /// <summary>
        /// Picks the instance whose screen-space disc (sized by LOD0 mesh bounds × instance scale)
        /// contains <paramref name="mouse"/>. Falls back to a minimum radius of
        /// <see cref="PICK_PIXELS_MIN"/> so small/distant instances remain clickable.
        /// Returns the closest overlapping index, or -1 if none.
        /// </summary>
        private static int PickNearest(AuthoredInstancesData authored, Vector2 mouse,
            InstanceScatterLayer? layer)
        {
            // Compute a world-space reference radius from LOD0 mesh bounds.
            // This is the half-diagonal of the mesh's AABB scaled up by the instance scale.
            // Screen-project it to pixels via HandleUtility.GetHandleSize (which gives the
            // handle "unit" in world space at that depth) to get a per-instance pixel radius.
            float meshWorldRadius = 0.5f; // fallback: 0.5 m radius
            if (layer != null)
            {
                var lods = layer.Render.Lods;
                if (lods.Length > 0 && lods[0].mesh != null)
                    meshWorldRadius = lods[0].mesh!.bounds.extents.magnitude;
            }

            var list = authored.WorkingList;
            int   best     = -1;
            float bestDist = float.PositiveInfinity;

            for (int i = 0; i < list.Count; ++i)
            {
                Vector3 worldPos  = list[i].position;
                float   instScale = list[i].scale;

                // World-space pick radius = mesh radius × instance scale.
                float worldPickRadius = meshWorldRadius * instScale;

                // Convert the world-space pick radius to screen pixels.
                // GetHandleSize returns the size of one "handle unit" at worldPos depth.
                // We divide worldPickRadius by that unit to get the screen-space radius.
                float handleUnit   = HandleUtility.GetHandleSize(worldPos);
                float screenRadius = (worldPickRadius / handleUnit) * EditorGUIUtility.pixelsPerPoint * 80f;
                // The *80f factor maps from the normalised handle space to approximate screen pixels.
                // Clamped to PICK_PIXELS_MIN so tiny/distant instances stay selectable.
                screenRadius = Mathf.Max(screenRadius, PICK_PIXELS_MIN);

                Vector2 gui = HandleUtility.WorldToGUIPoint(worldPos);
                float d     = Vector2.Distance(gui, mouse);

                if (d <= screenRadius && d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }

        // ── Erase ──────────────────────────────────────────────────────────────

        private void OnErase(Event e, int controlId, AuthoredInstancesData authored, ScatterField field, int layerIdx,
            bool hasHit, RaycastHit hit)
        {
            bool act = (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && !e.alt && hasHit;
            if (!act) return;

            GUIUtility.hotControl = controlId;
            float sqr = EraseRadius * EraseRadius;
            var list = authored.WorkingList;
            bool removed = false;
            for (int i = list.Count - 1; i >= 0; --i)
            {
                if ((list[i].position - hit.point).sqrMagnitude <= sqr)
                {
                    if (!removed) Undo.RegisterCompleteObjectUndo(authored, "Erase Instances");
                    authored.RemoveRecordSwapPop(i);
                    removed = true;
                }
            }
            if (removed)
            {
                this.selectedIndex = -1;
                this.multiSelection.Clear();
                Commit(authored, field, layerIdx);
            }
            e.Use();
        }

        // ── Anchor (per-layer deform anchor offset, via a Scene-view handle) ───

        /// <summary>
        /// Edits the layer's per-layer <see cref="InstanceScatterLayer.AnchorOffsetLocal"/> with a
        /// Scene-view position handle. The handle sits at the world anchor of a REFERENCE instance
        /// (the selected one, else the first authored record): <c>pivot + rot·(offset·scale)</c> —
        /// the exact point the shader/sim sample wind + interactors from. Dragging the handle
        /// back-projects to a new local offset (rotation- and scale-normalised) and re-bakes the layer.
        /// </summary>
        private void OnAnchor(AuthoredInstancesData authored, InstanceScatterLayer layer,
            ScatterField field, int layerIdx)
        {
            var list = authored.WorkingList;
            if (list.Count == 0)
            {
                Handles.BeginGUI();
                GUILayout.BeginArea(new Rect(8, 44, 280, 20), GUI.skin.box);
                GUILayout.Label("Anchor: place at least one instance first.", EditorStyles.miniLabel);
                GUILayout.EndArea();
                Handles.EndGUI();
                return;
            }

            int refIdx = (this.selectedIndex >= 0 && this.selectedIndex < list.Count) ? this.selectedIndex : 0;
            InstanceRecord rec = list[refIdx];

            Vector3    pivot       = rec.position;
            Quaternion rot         = rec.rotation;
            float      scale       = Mathf.Max(1e-4f, rec.scale);
            Vector3    anchorLocal = layer.AnchorOffsetLocal;

            // World anchor = pivot + rot·(localOffset · scale). Mirrors the shader/sim sampling point.
            Vector3 worldAnchor = pivot + rot * (anchorLocal * scale);

            // Visual: pivot dot, dotted connector, and the local-offset readout at the handle.
            ScatterGizmos.InstanceDot(pivot, HandleUtility.GetHandleSize(pivot) * 0.06f, ScatterGizmos.InstanceColor);
            Handles.color = ScatterGizmos.SelectedColor;
            Handles.DrawDottedLine(pivot, worldAnchor, 3f);
            Handles.Label(worldAnchor + Vector3.up * (HandleUtility.GetHandleSize(worldAnchor) * 0.4f),
                $"Anchor (ref #{refIdx})\nlocal ({anchorLocal.x:F2}, {anchorLocal.y:F2}, {anchorLocal.z:F2})",
                EditorStyles.miniLabel);

            // Position handle, oriented to the instance so the axes feel local to the prop.
            EditorGUI.BeginChangeCheck();
            Vector3 newWorld = Handles.PositionHandle(worldAnchor, rot);
            if (EditorGUI.EndChangeCheck())
            {
                // Back-project: localOffset = inverse(rot)·(newWorld − pivot) / scale.
                Vector3 newLocal = (Quaternion.Inverse(rot) * (newWorld - pivot)) / scale;

                var so = new SerializedObject(layer);
                SerializedProperty? prop = so.FindProperty("anchorOffsetLocal");
                if (prop != null)
                {
                    Undo.RegisterCompleteObjectUndo(layer, "Edit Anchor Offset");
                    prop.vector3Value = newLocal;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(layer);
                    // Re-bake so InstancedPropEngine.Build pushes the new _AnchorOffset to the materials.
                    if (layerIdx >= 0) ScatterRebuildScheduler.MarkDirty(field, layerIdx);
                }
            }
        }

        // ── Drawing (instances as dots, with collider-indicator tint) ──────────

        private void DrawInstances(AuthoredInstancesData authored, Vector3 camPos)
        {
            var list = authored.WorkingList;
            float sqr = DRAW_RADIUS * DRAW_RADIUS;
            int drawn = 0;
            for (int i = 0; i < list.Count && drawn < MAX_DRAW; ++i)
            {
                if (i == this.selectedIndex && this.multiSelection.Count == 0) continue;
                if (this.multiSelection.Contains(i)) continue; // drawn in OnSelect

                Vector3 p = list[i].position;
                if ((p - camPos).sqrMagnitude > sqr) continue;

                // Tint: amber if collider-configured, else default instance color.
                bool colliderConfigured = (list[i].overrideMask & InstanceOverrideMask.ColliderConfigured) != 0;
                Color dotColor = colliderConfigured ? ScatterGizmos.FieldBoundsColor : ScatterGizmos.InstanceColor;
                ScatterGizmos.InstanceDot(p, HandleUtility.GetHandleSize(p) * 0.05f, dotColor);
                drawn++;
            }
        }

        // ── Commit helper ──────────────────────────────────────────────────────

        private static void Commit(AuthoredInstancesData authored, ScatterField field, int layerIdx)
        {
            authored.PackBlob();
            EditorUtility.SetDirty(authored);
            if (layerIdx >= 0) ScatterRebuildScheduler.MarkDirty(field, layerIdx);
        }

        // ── Public accessors for InstancePanel ────────────────────────────────

        /// <summary>The currently selected single-instance index (-1 if none).</summary>
        internal int SelectedIndex => this.selectedIndex;

        /// <summary>All indices currently in the multi-selection set.</summary>
        internal IReadOnlyCollection<int> MultiSelection => this.multiSelection;

        /// <summary>Clears the single- and multi-selection.</summary>
        internal void ClearSelection()
        {
            this.selectedIndex = -1;
            this.multiSelection.Clear();
        }
    }
}
