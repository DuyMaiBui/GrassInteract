#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Terrain-tool-style density brush for a <see cref="DensityScatterLayer"/>. Raycasts the ground,
    /// writes the R8 <c>densityMap</c>, re-scatters live through <see cref="ScatterRebuildScheduler"/>,
    /// and persists the painted pixels back to the texture asset (PNG) on stroke end via
    /// <see cref="DensityMapFactory.PersistPixels"/> (SSOT — no duplicate persist path here).
    ///
    /// All tool state (brush size, opacity, falloff, flow, paint mode, active stamp) is read from
    /// <see cref="ScatterAuthoringState.I"/> — this tool no longer writes or reads EditorPrefs.
    ///
    /// Stamp resolution follows <see cref="StampRef"/>:
    ///   <see cref="StampRef.StampSource.None"/>   → procedural falloff kernel (same as old StampIndex == -1)
    ///   <see cref="StampRef.StampSource.Config"/>  → <c>field.Config.BrushStamps[index]</c>
    ///   <see cref="StampRef.StampSource.Global"/>  → <see cref="ScatterBrushLibraryProvider.Library"/>.Stamps[index]
    ///
    /// The in-scene <see cref="DrawSettingsWindow"/> is intentionally minimal (brush cursor disc + mode
    /// label only) — all settings now live in the Scatter Studio window.
    /// </summary>
    [EditorTool("Density Paint", typeof(DensityScatterLayer))]
    internal sealed class DensityPaintTool : EditorTool
    {
        internal enum PaintMode { Paint, Erase, Smooth }

        // ── Active stroke state ────────────────────────────────────────────────

        private bool       painting;
        private Color[]?   pixels;
        private int        texW, texH;
        private Texture2D? activeMap;
        private Texture2D? activeStamp; // resolved at stroke start; null = procedural falloff

        public override GUIContent toolbarIcon => EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSplat");

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;
            if (this.target is not DensityScatterLayer layer) return;

            (ScatterField? field, int layerIdx) = ScatterFieldLookup.FindOwningField(layer);

            // Auto-create density map on first paint if none exists.
            if (field != null && layer.DensityMap == null)
                TryAutoCreateDensityMap(layer, field);

            bool valid = layer.Validate(out string error);

            this.DrawSettingsWindow(valid, valid ? null : error);

            if (!valid || field == null)
                return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            Vector3 origin = field.ResolveFieldOrigin();
            Vector2 bounds = field.ResolveFieldBoundsXZ(layer);
            LayerMask mask = field.ResolveGroundMask(layer);

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity,
                mask.value == 0 ? ~0 : mask.value);

            if (hasHit)
            {
                float size = ScatterAuthoringState.I.BrushSize;
                float falloff = ScatterAuthoringState.I.BrushFalloff;
                ScatterGizmos.BrushDisc(hit.point, hit.normal, size, this.BrushColorForMode());
                ScatterGizmos.FalloffRing(hit.point, hit.normal, size * falloff, size, ScatterGizmos.BrushFalloffColor);
                HandleUtility.Repaint();
            }

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && hasHit:
                    GUIUtility.hotControl = controlId;
                    this.BeginStroke(layer, field);
                    this.PaintAt(hit.point, origin, bounds);
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && this.painting:
                    if (hasHit) this.PaintAt(hit.point, origin, bounds);
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && this.painting:
                    this.EndStroke(field, layerIdx);
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        // ── Auto-create density map ────────────────────────────────────────────

        /// <summary>
        /// On the very first paint stroke for a layer with no <c>densityMap</c> assigned,
        /// creates a blank density map via <see cref="DensityMapFactory.CreateBlank"/> and
        /// assigns it to the layer via <see cref="SerializedObject"/> (Undo-tracked, asset-dirty).
        /// </summary>
        private static void TryAutoCreateDensityMap(DensityScatterLayer layer, ScatterField field)
        {
            int size = 512; // default resolution

            Texture2D? map = DensityMapFactory.CreateBlank(size, field.Config);
            if (map == null)
            {
                Debug.LogError("[DensityPaintTool] Auto-create density map failed — see previous errors.");
                return;
            }

            Undo.RegisterCompleteObjectUndo(layer, "Auto-Create Density Map");

            var so = new SerializedObject(layer);
            var prop = so.FindProperty("densityMap");
            if (prop != null)
            {
                prop.objectReferenceValue = map;
                so.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();

            Debug.Log($"[DensityPaintTool] Auto-created density map '{map.name}' for layer '{layer.name}'.");
        }

        // ── Stroke lifecycle ───────────────────────────────────────────────────

        private void BeginStroke(DensityScatterLayer layer, ScatterField field)
        {
            Texture2D map = layer.DensityMap!;
            Undo.RegisterCompleteObjectUndo(map, "Paint Density");
            this.activeMap = map;
            this.texW = map.width;
            this.texH = map.height;
            this.pixels = map.GetPixels();
            this.activeStamp = ResolveStamp(field);
            this.painting = true;
        }

        private void EndStroke(ScatterField field, int layerIdx)
        {
            this.painting = false;
            if (this.activeMap != null && this.pixels != null)
            {
                this.activeMap.SetPixels(this.pixels);
                this.activeMap.Apply(false);
                EditorUtility.SetDirty(this.activeMap);
                // SSOT: delegate PNG persistence to the factory (no duplicate path).
                DensityMapFactory.PersistPixels(this.activeMap, this.pixels, this.texW, this.texH);
                if (layerIdx >= 0) ScatterRebuildScheduler.MarkDirty(field, layerIdx);
            }
            this.pixels = null;
            this.activeMap = null;
            this.activeStamp = null;
        }

        // ── Paint kernel ───────────────────────────────────────────────────────

        private void PaintAt(Vector3 worldHit, Vector3 origin, Vector2 bounds)
        {
            if (this.pixels == null || this.activeMap == null) return;

            float size    = ScatterAuthoringState.I.BrushSize;
            float opacity = ScatterAuthoringState.I.BrushOpacity;
            float falloff = ScatterAuthoringState.I.BrushFalloff;
            float flow    = ScatterAuthoringState.I.BrushFlow;
            var   mode    = (PaintMode)ScatterAuthoringState.I.PaintMode;

            var space = new GrassFieldSpace(origin, bounds);
            Vector2 centerUv = space.WorldToUv(worldHit);

            float radUvX = size / Mathf.Max(0.0001f, bounds.x);
            float radUvY = size / Mathf.Max(0.0001f, bounds.y);
            int minX = Mathf.Clamp(Mathf.FloorToInt((centerUv.x - radUvX) * this.texW), 0, this.texW - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((centerUv.x + radUvX) * this.texW), 0, this.texW - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt((centerUv.y - radUvY) * this.texH), 0, this.texH - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt((centerUv.y + radUvY) * this.texH), 0, this.texH - 1);

            float strength = Mathf.Clamp01(opacity) * Mathf.Clamp01(flow);
            float inner    = Mathf.Clamp01(falloff);
            Texture2D? stamp = this.activeStamp;

            for (int py = minY; py <= maxY; ++py)
            {
                for (int px = minX; px <= maxX; ++px)
                {
                    var uv = new Vector2((px + 0.5f) / this.texW, (py + 0.5f) / this.texH);
                    Vector3 pw = space.UvToWorld(uv, worldHit.y);
                    float dx = pw.x - worldHit.x;
                    float dz = pw.z - worldHit.z;
                    float distXZ = Mathf.Sqrt(dx * dx + dz * dz);
                    if (distXZ > size) continue;

                    float brushFalloff;
                    if (stamp != null)
                    {
                        float su = Mathf.Clamp01(dx / (2f * size) + 0.5f);
                        float sv = Mathf.Clamp01(dz / (2f * size) + 0.5f);
                        brushFalloff = stamp.GetPixelBilinear(su, sv).r;
                    }
                    else
                    {
                        float t = size <= 0f ? 0f : distXZ / size;
                        brushFalloff = t <= inner ? 1f : Mathf.SmoothStep(1f, 0f, (t - inner) / Mathf.Max(0.0001f, 1f - inner));
                    }

                    float w = strength * brushFalloff;
                    if (w <= 0f) continue;

                    int idx = py * this.texW + px;
                    float r = this.pixels[idx].r;

                    r = mode switch
                    {
                        PaintMode.Paint  => Mathf.Clamp01(r + w),
                        PaintMode.Erase  => Mathf.Clamp01(r - w),
                        PaintMode.Smooth => Mathf.Lerp(r, this.NeighborAverage(px, py), w),
                        _ => r,
                    };
                    this.pixels[idx] = new Color(r, r, r, 1f);
                }
            }

            // Live preview: push CPU pixels to GPU.
            this.activeMap.SetPixels(this.pixels);
            this.activeMap.Apply(false);
        }

        private float NeighborAverage(int px, int py)
        {
            if (this.pixels == null) return 0f;
            float sum = 0f; int n = 0;
            for (int dy = -1; dy <= 1; ++dy)
            for (int dx = -1; dx <= 1; ++dx)
            {
                int nx = px + dx, ny = py + dy;
                if (nx < 0 || ny < 0 || nx >= this.texW || ny >= this.texH) continue;
                sum += this.pixels[ny * this.texW + nx].r; n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        // ── Stamp resolution ───────────────────────────────────────────────────

        /// <summary>
        /// Resolves the active <see cref="StampRef"/> from <see cref="ScatterAuthoringState"/> to a
        /// concrete readable <see cref="Texture2D"/>, or <c>null</c> for procedural falloff.
        ///
        /// Resolution table (matches old StampIndex semantics):
        ///   StampSource.None   → null (procedural; was StampIndex == -1)
        ///   StampSource.Config → field.Config.BrushStamps[index] (was StampIndex >= 0 into Config list)
        ///   StampSource.Global → ScatterBrushLibraryProvider.Library.Stamps[index]
        /// </summary>
        private static Texture2D? ResolveStamp(ScatterField field)
        {
            StampRef stampRef = ScatterAuthoringState.I.ActiveStamp;

            if (stampRef.IsNone) return null;

            BrushStamp? stamp = null;

            switch (stampRef.Source)
            {
                case StampRef.StampSource.Config:
                    if (field.Config == null) return null;
                    var configStamps = field.Config.BrushStamps;
                    if (stampRef.Index < 0 || stampRef.Index >= configStamps.Count) return null;
                    stamp = configStamps[stampRef.Index];
                    break;

                case StampRef.StampSource.Global:
                    var globalStamps = ScatterBrushLibraryProvider.Library.Stamps;
                    if (stampRef.Index < 0 || stampRef.Index >= globalStamps.Count) return null;
                    stamp = globalStamps[stampRef.Index];
                    break;

                default:
                    return null;
            }

            if (stamp == null) return null;
            Texture2D? shape = stamp.Shape;
            return shape != null && shape.isReadable ? shape : null;
        }

        // ── Minimal in-scene HUD ───────────────────────────────────────────────

        /// <summary>
        /// Minimal in-scene overlay: shows the tool title, a mode label, and any validation error.
        /// All brush settings now live in the Scatter Studio window.
        /// </summary>
        private void DrawSettingsWindow(bool valid, string? error)
        {
            float h = valid ? 52f : 88f;
            Handles.BeginGUI();
            var area = new Rect(8, 8, 200, h);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Density Paint", EditorStyles.boldLabel);

            if (!valid)
                EditorGUILayout.HelpBox(error ?? "Density map invalid.", MessageType.Error);
            else
            {
                var mode = (PaintMode)ScatterAuthoringState.I.PaintMode;
                GUILayout.Label($"Mode: {mode}", EditorStyles.miniLabel);
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private Color BrushColorForMode()
        {
            var mode = (PaintMode)ScatterAuthoringState.I.PaintMode;
            return mode == PaintMode.Erase ? ScatterGizmos.EraseColor : ScatterGizmos.BrushColor;
        }
    }
}
