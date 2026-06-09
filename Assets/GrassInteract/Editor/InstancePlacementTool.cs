#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Transform-tool-style placement editor for an <see cref="InstanceScatterLayer"/>. Place /
    /// Select+Transform / Erase authored records, with move-rotate-scale <see cref="Handles"/> writing
    /// back to the selected record. The per-instance panel exposes collider config + a PhysicsMaterial
    /// override (Phase 1 payload). All edits flow through the <see cref="AuthoredInstancesData"/> working
    /// list with Undo and the debounced <see cref="ScatterRebuildScheduler"/>.
    /// </summary>
    [EditorTool("Instance Placement", typeof(InstanceScatterLayer))]
    internal sealed class InstancePlacementTool : EditorTool
    {
        private enum PlaceMode { Place, Select, Erase }

        private const string K_MODE  = "GrassInteract.Place.Mode";
        private const string K_ALIGN = "GrassInteract.Place.Align";
        private const string K_YAW   = "GrassInteract.Place.RandYaw";
        private const string K_SMIN  = "GrassInteract.Place.ScaleMin";
        private const string K_SMAX  = "GrassInteract.Place.ScaleMax";
        private const string K_ERASE = "GrassInteract.Place.EraseRadius";

        private static PlaceMode Mode { get => (PlaceMode)EditorPrefs.GetInt(K_MODE, 0); set => EditorPrefs.SetInt(K_MODE, (int)value); }
        private static bool  AlignToNormal { get => EditorPrefs.GetBool(K_ALIGN, false); set => EditorPrefs.SetBool(K_ALIGN, value); }
        private static bool  RandomYaw     { get => EditorPrefs.GetBool(K_YAW, true);    set => EditorPrefs.SetBool(K_YAW, value); }
        private static float ScaleMin      { get => EditorPrefs.GetFloat(K_SMIN, 1f);    set => EditorPrefs.SetFloat(K_SMIN, value); }
        private static float ScaleMax      { get => EditorPrefs.GetFloat(K_SMAX, 1f);    set => EditorPrefs.SetFloat(K_SMAX, value); }
        private static float EraseRadius   { get => EditorPrefs.GetFloat(K_ERASE, 2f);   set => EditorPrefs.SetFloat(K_ERASE, value); }

        private const float PICK_PIXELS  = 16f;
        private const int   MAX_DRAW     = 4000;
        private const float DRAW_RADIUS  = 60f;

        private int selectedIndex = -1;

        public override GUIContent toolbarIcon => EditorGUIUtility.IconContent("Transform Icon");

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView sv) return;
            if (this.target is not InstanceScatterLayer layer) return;

            AuthoredInstancesData? authored = layer.AuthoredInstances;
            (ScatterField? field, int layerIdx) = ScatterFieldLookup.FindOwningField(layer);

            this.DrawSettingsWindow(layer, authored, field, layerIdx);

            if (authored == null || field == null) return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            Vector3 camPos = sv.camera != null ? sv.camera.transform.position : Vector3.zero;
            this.DrawInstances(authored, camPos);

            Vector3 origin = field.ResolveFieldOrigin();
            LayerMask mask = field.ResolveGroundMask(layer);
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, mask.value == 0 ? ~0 : mask.value);

            switch (Mode)
            {
                case PlaceMode.Place:  this.OnPlace(e, controlId, authored, layer, field, layerIdx, hasHit, hit); break;
                case PlaceMode.Select: this.OnSelect(e, controlId, authored, field, layerIdx); break;
                case PlaceMode.Erase:  this.OnErase(e, controlId, authored, field, layerIdx, hasHit, hit); break;
            }

            if (hasHit && Mode != PlaceMode.Select)
            {
                Color c = Mode == PlaceMode.Erase ? ScatterGizmos.EraseColor : ScatterGizmos.BrushColor;
                float r = Mode == PlaceMode.Erase ? EraseRadius : 0.5f;
                ScatterGizmos.BrushDisc(hit.point, hit.normal, r, c);
                HandleUtility.Repaint();
            }
        }

        // ── Place ──────────────────────────────────────────────────────────────

        private void OnPlace(Event e, int controlId, AuthoredInstancesData authored, InstanceScatterLayer layer,
            ScatterField field, int layerIdx, bool hasHit, RaycastHit hit)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && hasHit)
            {
                GUIUtility.hotControl = controlId;
                if (this.RespectsSpacing(authored, hit.point, layer.PlaceSpacing))
                {
                    Undo.RegisterCompleteObjectUndo(authored, "Place Instance");
                    authored.AddRecord(BuildRecord(hit.point, hit.normal));
                    Commit(authored, field, layerIdx);
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                GUIUtility.hotControl = 0;
            }
        }

        private static InstanceRecord BuildRecord(Vector3 pos, Vector3 normal)
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

        // ── Select + Transform ─────────────────────────────────────────────────

        private void OnSelect(Event e, int controlId, AuthoredInstancesData authored, ScatterField field, int layerIdx)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                this.selectedIndex = PickNearest(authored, e.mousePosition);
                e.Use();
            }

            if (this.selectedIndex < 0 || !authored.TryGetRecord(this.selectedIndex, out InstanceRecord rec))
                return;

            ScatterGizmos.InstanceDot(rec.position, HandleUtility.GetHandleSize(rec.position) * 0.12f, ScatterGizmos.SelectedColor);

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(rec.position, rec.rotation);
            Quaternion newRot = Handles.RotationHandle(rec.rotation, rec.position);
            Vector3 newScaleVec = Handles.ScaleHandle(Vector3.one * rec.scale, rec.position, rec.rotation,
                HandleUtility.GetHandleSize(rec.position));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(authored, "Transform Instance");
                rec.position = newPos;
                rec.rotation = newRot;
                rec.scale = Mathf.Max(0.0001f, newScaleVec.x);
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
                Commit(authored, field, layerIdx);
            }
            e.Use();
        }

        // ── Drawing ────────────────────────────────────────────────────────────

        private void DrawInstances(AuthoredInstancesData authored, Vector3 camPos)
        {
            var list = authored.WorkingList;
            float sqr = DRAW_RADIUS * DRAW_RADIUS;
            int drawn = 0;
            for (int i = 0; i < list.Count && drawn < MAX_DRAW; ++i)
            {
                if (i == this.selectedIndex) continue;
                Vector3 p = list[i].position;
                if ((p - camPos).sqrMagnitude > sqr) continue;
                ScatterGizmos.InstanceDot(p, HandleUtility.GetHandleSize(p) * 0.05f, ScatterGizmos.InstanceColor);
                drawn++;
            }
        }

        // ── Settings + per-instance panel ──────────────────────────────────────

        private void DrawSettingsWindow(InstanceScatterLayer layer, AuthoredInstancesData? authored,
            ScatterField? field, int layerIdx)
        {
            Handles.BeginGUI();
            var area = new Rect(8, 8, 260, this.selectedIndex >= 0 ? 300 : 150);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Instance Placement", EditorStyles.boldLabel);

            if (authored == null)
                EditorGUILayout.HelpBox("Layer has no AuthoredInstancesData sub-asset.", MessageType.Error);
            else if (field == null)
                EditorGUILayout.HelpBox("No active ScatterField owns this layer.", MessageType.Warning);

            Mode = (PlaceMode)GUILayout.Toolbar((int)Mode, new[] { "Place", "Select", "Erase" });

            switch (Mode)
            {
                case PlaceMode.Place:
                    AlignToNormal = EditorGUILayout.Toggle("Align To Normal", AlignToNormal);
                    RandomYaw     = EditorGUILayout.Toggle("Random Yaw", RandomYaw);
                    ScaleMin      = EditorGUILayout.FloatField("Scale Min", ScaleMin);
                    ScaleMax      = EditorGUILayout.FloatField("Scale Max", ScaleMax);
                    GUILayout.Label($"Spacing: {layer.PlaceSpacing:0.##} m", EditorStyles.miniLabel);
                    break;
                case PlaceMode.Erase:
                    EraseRadius = EditorGUILayout.Slider("Erase Radius", EraseRadius, 0.1f, 20f);
                    break;
                case PlaceMode.Select:
                    if (authored != null && this.selectedIndex >= 0)
                        this.DrawSelectedInstance(authored, field, layerIdx);
                    else
                        GUILayout.Label("Click an instance to select.", EditorStyles.miniLabel);
                    break;
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private void DrawSelectedInstance(AuthoredInstancesData authored, ScatterField? field, int layerIdx)
        {
            if (!authored.TryGetRecord(this.selectedIndex, out InstanceRecord rec)) { this.selectedIndex = -1; return; }

            GUILayout.Label($"Instance #{this.selectedIndex}", EditorStyles.boldLabel);

            bool configured = (rec.overrideMask & InstanceOverrideMask.ColliderConfigured) != 0;
            bool generate = configured && rec.generateCollider;

            EditorGUI.BeginChangeCheck();
            bool newGenerate = EditorGUILayout.Toggle("Generate Collider", generate);
            bool newConvex   = EditorGUILayout.Toggle("Convex", rec.colliderConvex);
            float newScale   = EditorGUILayout.FloatField("Collider Scale", rec.colliderScale <= 0f ? 1f : rec.colliderScale);

            var curMesh = authored.GetObjectRef(rec.colliderMeshRefIndex) as Mesh;
            var newMesh = (Mesh?)EditorGUILayout.ObjectField("Mesh Override", curMesh, typeof(Mesh), false);

            var curMat = authored.GetObjectRef(rec.colliderMaterialRefIndex) as PhysicsMaterial;
            var newMat = (PhysicsMaterial?)EditorGUILayout.ObjectField("PhysicsMaterial", curMat, typeof(PhysicsMaterial), false);

            if (EditorGUI.EndChangeCheck() && field != null)
            {
                Undo.RegisterCompleteObjectUndo(authored, "Edit Instance Collider");
                if (newGenerate || newMesh != null || newMat != null)
                {
                    int meshRef = newMesh != null ? authored.EnsureObjectRef(newMesh) : -1;
                    int matRef  = newMat  != null ? authored.EnsureObjectRef(newMat)  : -1;
                    authored.SetColliderConfig(this.selectedIndex, newGenerate, newConvex, newScale, meshRef, matRef);
                }
                else
                {
                    authored.ClearColliderConfig(this.selectedIndex);
                }
                Commit(authored, field, layerIdx);
            }
        }

        // ── Commit helper ──────────────────────────────────────────────────────

        private static void Commit(AuthoredInstancesData authored, ScatterField field, int layerIdx)
        {
            authored.PackBlob();
            EditorUtility.SetDirty(authored);
            if (layerIdx >= 0) ScatterRebuildScheduler.MarkDirty(field, layerIdx);
        }
    }
}
