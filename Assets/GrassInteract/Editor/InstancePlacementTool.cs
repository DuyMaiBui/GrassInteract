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
    [EditorTool("Instance Placement", typeof(InstanceScatterLayer))]
    internal sealed class InstancePlacementTool : EditorTool
    {
        internal enum PlaceMode { Place = 0, Select = 1, Erase = 2, Scatter = 3 }

        // ── State shortcuts (read ScatterAuthoringState) ───────────────────────

        private static PlaceMode Mode
        {
            get => (PlaceMode)ScatterAuthoringState.I.PlaceMode;
            set => ScatterAuthoringState.I.PlaceMode = (int)value;
        }

        private static bool  AlignToNormal => ScatterAuthoringState.I.AlignToNormal;
        private static bool  RandomYaw     => ScatterAuthoringState.I.RandomYaw;
        private static float ScaleMin      => ScatterAuthoringState.I.PlaceScaleMin;
        private static float ScaleMax      => ScatterAuthoringState.I.PlaceScaleMax;
        private static float EraseRadius   => ScatterAuthoringState.I.EraseRadius;
        private static float BrushSize     => ScatterAuthoringState.I.BrushSize;

        // ── Constants ──────────────────────────────────────────────────────────

        private const float PICK_PIXELS     = 16f;
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
            if (this.target is not InstanceScatterLayer layer) return;

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
                    this.OnSelect(e, controlId, authored, field, layerIdx);
                    break;
                case PlaceMode.Erase:
                    this.OnErase(e, controlId, authored, field, layerIdx, hasHit, hit);
                    break;
            }

            if (hasHit && Mode != PlaceMode.Select)
            {
                Color c = Mode == PlaceMode.Erase ? ScatterGizmos.EraseColor : ScatterGizmos.BrushColor;
                float r = Mode == PlaceMode.Erase ? EraseRadius : (Mode == PlaceMode.Scatter ? BrushSize : 0.5f);
                ScatterGizmos.BrushDisc(hit.point, hit.normal, r, c);
                HandleUtility.Repaint();
            }
        }

        // ── Minimal HUD ────────────────────────────────────────────────────────

        private void DrawMinimalHud()
        {
            Handles.BeginGUI();
            var area = new Rect(8, 8, 180, 32);
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
                if (this.RespectsSpacing(authored, hit.point, layer.PlaceSpacing))
                {
                    Undo.RegisterCompleteObjectUndo(authored, "Place Instance");
                    authored.AddRecord(this.BuildRecord(hit.point, hit.normal));
                    Commit(authored, field, layerIdx);
                }
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
            float spacing = layer.PlaceSpacing;
            LayerMask mask = field.ResolveGroundMask(layer);

            // Generate candidate positions within the brush disc and place those that
            // respect spacing. Candidates are capped at MAX_SCATTER_PER_STROKE to
            // avoid O(n²) stalls on dense fields.
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

                if (!this.RespectsSpacing(authored, probeHit.point, spacing))
                    continue;

                authored.AddRecord(this.BuildRecord(probeHit.point, probeHit.normal));
                anyPlaced = true;
                placed++;
            }

            if (anyPlaced)
                Commit(authored, field, layerIdx);

            e.Use();
        }

        // ── Record factory ─────────────────────────────────────────────────────

        private InstanceRecord BuildRecord(Vector3 pos, Vector3 normal)
        {
            float yaw = RandomYaw ? Random.Range(0f, 360f) : 0f;
            Quaternion align = AlignToNormal ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
            Quaternion rot = align * Quaternion.Euler(0f, yaw, 0f);
            float scale = Random.Range(Mathf.Min(ScaleMin, ScaleMax), Mathf.Max(ScaleMin, ScaleMax));
            return new InstanceRecord
            {
                position = pos, rotation = rot, scale = scale,
                overrideMask = InstanceOverrideMask.None,
                colliderScale = 1f, colliderMeshRefIndex = -1, colliderMaterialRefIndex = -1,
            };
        }

        private bool RespectsSpacing(AuthoredInstancesData authored, Vector3 pos, float spacing)
        {
            float sqr = spacing * spacing;
            var list = authored.WorkingList;
            for (int i = 0; i < list.Count; ++i)
                if ((list[i].position - pos).sqrMagnitude < sqr) return false;
            return true;
        }

        // ── Select + Transform (single + multi) ────────────────────────────────

        private void OnSelect(Event e, int controlId, AuthoredInstancesData authored, ScatterField field, int layerIdx)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                int picked = PickNearest(authored, e.mousePosition);
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

            // Single-select transform handles
            if (this.multiSelection.Count == 0)
            {
                this.DrawSingleSelectHandles(authored, field, layerIdx);
                return;
            }

            // Multi-select: draw dots for each selected instance
            foreach (int idx in this.multiSelection)
            {
                if (!authored.TryGetRecord(idx, out InstanceRecord r)) continue;
                ScatterGizmos.InstanceDot(r.position, HandleUtility.GetHandleSize(r.position) * 0.12f, ScatterGizmos.SelectedColor);
            }
        }

        private void DrawSingleSelectHandles(AuthoredInstancesData authored, ScatterField? field, int layerIdx)
        {
            if (this.selectedIndex < 0 || !authored.TryGetRecord(this.selectedIndex, out InstanceRecord rec))
                return;

            ScatterGizmos.InstanceDot(rec.position, HandleUtility.GetHandleSize(rec.position) * 0.12f, ScatterGizmos.SelectedColor);

            EditorGUI.BeginChangeCheck();
            Vector3    newPos      = Handles.PositionHandle(rec.position, rec.rotation);
            Quaternion newRot      = Handles.RotationHandle(rec.rotation, rec.position);
            Vector3    newScaleVec = Handles.ScaleHandle(Vector3.one * rec.scale, rec.position, rec.rotation,
                HandleUtility.GetHandleSize(rec.position));
            if (EditorGUI.EndChangeCheck() && field != null)
            {
                Undo.RegisterCompleteObjectUndo(authored, "Transform Instance");
                rec.position = newPos;
                rec.rotation = newRot;
                rec.scale    = Mathf.Max(0.0001f, newScaleVec.x);
                authored.SetRecord(this.selectedIndex, rec);
                Commit(authored, field, layerIdx);
            }
        }

        private static int PickNearest(AuthoredInstancesData authored, Vector2 mouse)
        {
            var list = authored.WorkingList;
            int best = -1; float bestDist = PICK_PIXELS;
            for (int i = 0; i < list.Count; ++i)
            {
                Vector2 gui = HandleUtility.WorldToGUIPoint(list[i].position);
                float d = Vector2.Distance(gui, mouse);
                if (d < bestDist) { bestDist = d; best = i; }
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
