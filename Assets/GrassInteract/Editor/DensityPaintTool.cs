#nullable enable
using System.IO;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Terrain-tool-style density brush for a <see cref="DensityScatterLayer"/>. Raycasts the ground,
    /// writes the R8 <c>densityMap</c>, re-scatters live through <see cref="ScatterRebuildScheduler"/>,
    /// and persists the painted pixels back to the texture asset (PNG) on stroke end.
    /// Brush disc + falloff ring drawn via the shared <see cref="ScatterGizmos"/>.
    ///
    /// Paint alignment reuses the runtime <see cref="GrassFieldSpace"/> mapping (SSOT) so painted pixels
    /// land exactly where <see cref="DensityPlacement"/> samples them. An optional <see cref="BrushStamp"/>
    /// from the owning <see cref="TerrainScatterConfig"/> modulates the kernel.
    /// </summary>
    [EditorTool("Density Paint", typeof(DensityScatterLayer))]
    internal sealed class DensityPaintTool : EditorTool
    {
        private enum PaintMode { Paint, Erase, Smooth }

        // ── Brush settings (persisted via EditorPrefs) ─────────────────────────

        private const string K_SIZE  = "GrassInteract.Brush.Size";
        private const string K_OPAC  = "GrassInteract.Brush.Opacity";
        private const string K_FALL  = "GrassInteract.Brush.Falloff";
        private const string K_FLOW  = "GrassInteract.Brush.Flow";
        private const string K_MODE  = "GrassInteract.Brush.Mode";
        private const string K_STAMP = "GrassInteract.Brush.Stamp";

        private static float Size    { get => EditorPrefs.GetFloat(K_SIZE, 3f);  set => EditorPrefs.SetFloat(K_SIZE, value); }
        private static float Opacity { get => EditorPrefs.GetFloat(K_OPAC, 1f);  set => EditorPrefs.SetFloat(K_OPAC, value); }
        private static float Falloff { get => EditorPrefs.GetFloat(K_FALL, 0.5f);set => EditorPrefs.SetFloat(K_FALL, value); }
        private static float Flow    { get => EditorPrefs.GetFloat(K_FLOW, 0.5f);set => EditorPrefs.SetFloat(K_FLOW, value); }
        private static PaintMode Mode { get => (PaintMode)EditorPrefs.GetInt(K_MODE, 0); set => EditorPrefs.SetInt(K_MODE, (int)value); }
        private static int   StampIndex { get => EditorPrefs.GetInt(K_STAMP, -1); set => EditorPrefs.SetInt(K_STAMP, value); }

        // ── Active stroke state ────────────────────────────────────────────────

        private bool       painting;
        private Color[]?   pixels;
        private int        texW, texH;
        private Texture2D? activeMap;
        private Texture2D? activeStamp; // resolved at stroke start, null = procedural falloff

        public override GUIContent toolbarIcon => EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSplat");

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;
            if (this.target is not DensityScatterLayer layer) return;

            (ScatterField? field, int layerIdx) = ScatterFieldLookup.FindOwningField(layer);

            // Validate the density map up front — errors-over-silent-fallback.
            bool valid = layer.Validate(out string error);

            this.DrawSettingsWindow(valid, valid ? null : error, field);

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
                ScatterGizmos.BrushDisc(hit.point, hit.normal, Size, BrushColorForMode());
                ScatterGizmos.FalloffRing(hit.point, hit.normal, Size * Falloff, Size, ScatterGizmos.BrushFalloffColor);
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
                SaveToAsset(this.activeMap, this.pixels, this.texW, this.texH);
                if (layerIdx >= 0) ScatterRebuildScheduler.MarkDirty(field, layerIdx);
            }
            this.pixels = null;
            this.activeMap = null;
            this.activeStamp = null;
        }

        /// <summary>Persists the painted pixels back to the texture's source PNG so edits survive reload.</summary>
        private static void SaveToAsset(Texture2D map, Color[] px, int w, int h)
        {
            string path = AssetDatabase.GetAssetPath(map);
            if (string.IsNullOrEmpty(path)) return; // runtime-only texture — nothing to persist

            // Encode via a temp RGBA32 texture (R8 is not directly EncodeToPNG-able).
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tmp.SetPixels(px);
            tmp.Apply(false);
            byte[] png = tmp.EncodeToPNG();
            Object.DestroyImmediate(tmp);
            if (png == null) return;

            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); // re-imports with the map's R8 settings
        }

        // ── Paint kernel ───────────────────────────────────────────────────────

        private void PaintAt(Vector3 worldHit, Vector3 origin, Vector2 bounds)
        {
            if (this.pixels == null || this.activeMap == null) return;

            var space = new GrassFieldSpace(origin, bounds);
            Vector2 centerUv = space.WorldToUv(worldHit);

            // Pixel-space bounding box for the brush radius (world metres → UV → pixels).
            float radUvX = Size / Mathf.Max(0.0001f, bounds.x);
            float radUvY = Size / Mathf.Max(0.0001f, bounds.y);
            int minX = Mathf.Clamp(Mathf.FloorToInt((centerUv.x - radUvX) * this.texW), 0, this.texW - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((centerUv.x + radUvX) * this.texW), 0, this.texW - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt((centerUv.y - radUvY) * this.texH), 0, this.texH - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt((centerUv.y + radUvY) * this.texH), 0, this.texH - 1);

            float strength = Mathf.Clamp01(Opacity) * Mathf.Clamp01(Flow);
            float inner = Mathf.Clamp01(Falloff);
            Texture2D? stamp = this.activeStamp;

            for (int py = minY; py <= maxY; ++py)
            {
                for (int px = minX; px <= maxX; ++px)
                {
                    // World position of this pixel center → horizontal distance to the hit.
                    var uv = new Vector2((px + 0.5f) / this.texW, (py + 0.5f) / this.texH);
                    Vector3 pw = space.UvToWorld(uv, worldHit.y);
                    float dx = pw.x - worldHit.x;
                    float dz = pw.z - worldHit.z;
                    float distXZ = Mathf.Sqrt(dx * dx + dz * dz);
                    if (distXZ > Size) continue;

                    // Falloff: a brush stamp (when selected) replaces the procedural curve.
                    float falloff;
                    if (stamp != null)
                    {
                        float su = Mathf.Clamp01(dx / (2f * Size) + 0.5f);
                        float sv = Mathf.Clamp01(dz / (2f * Size) + 0.5f);
                        falloff = stamp.GetPixelBilinear(su, sv).r;
                    }
                    else
                    {
                        float t = Size <= 0f ? 0f : distXZ / Size;
                        falloff = t <= inner ? 1f : Mathf.SmoothStep(1f, 0f, (t - inner) / Mathf.Max(0.0001f, 1f - inner));
                    }

                    float w = strength * falloff;
                    if (w <= 0f) continue;

                    int idx = py * this.texW + px;
                    float r = this.pixels[idx].r;

                    r = Mode switch
                    {
                        PaintMode.Paint  => Mathf.Clamp01(r + w),
                        PaintMode.Erase  => Mathf.Clamp01(r - w),
                        PaintMode.Smooth => Mathf.Lerp(r, this.NeighborAverage(px, py), w),
                        _ => r,
                    };
                    this.pixels[idx] = new Color(r, r, r, 1f);
                }
            }

            // Live preview: update CPU pixels (DensityPlacement reads GetPixelBilinear) + GPU copy.
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

        // ── Brush stamp ────────────────────────────────────────────────────────

        /// <summary>Resolves the selected stamp's shape texture, or null (procedural falloff / unreadable).</summary>
        private static Texture2D? ResolveStamp(ScatterField field)
        {
            int idx = StampIndex;
            if (idx < 0 || field.Config == null) return null;
            var stamps = field.Config.BrushStamps;
            if (idx >= stamps.Count) return null;
            Texture2D? shape = stamps[idx].Shape;
            return shape != null && shape.isReadable ? shape : null;
        }

        // ── In-scene settings panel ────────────────────────────────────────────

        private void DrawSettingsWindow(bool valid, string? error, ScatterField? field)
        {
            int stampCount = field != null && field.Config != null ? field.Config.BrushStamps.Count : 0;
            float h = !valid ? 96f : (152f + (stampCount > 0 ? 18f : 0f));

            Handles.BeginGUI();
            var area = new Rect(8, 8, 250, h);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Density Paint", EditorStyles.boldLabel);

            if (field == null)
                EditorGUILayout.HelpBox("No active ScatterField owns this layer.", MessageType.Warning);

            if (!valid)
            {
                EditorGUILayout.HelpBox(error ?? "Density map invalid.", MessageType.Error);
            }
            else
            {
                Mode    = (PaintMode)GUILayout.Toolbar((int)Mode, new[] { "Paint", "Erase", "Smooth" });
                Size    = EditorGUILayout.Slider("Size",    Size,    0.1f, 50f);
                Opacity = EditorGUILayout.Slider("Opacity", Opacity, 0f,   1f);
                Falloff = EditorGUILayout.Slider("Falloff", Falloff, 0f,   1f);
                Flow    = EditorGUILayout.Slider("Flow",    Flow,    0f,   1f);

                if (stampCount > 0 && field != null && field.Config != null)
                {
                    var names = new string[stampCount + 1];
                    names[0] = "None (procedural)";
                    var stamps = field.Config.BrushStamps;
                    for (int i = 0; i < stampCount; ++i)
                        names[i + 1] = stamps[i].DisplayName;

                    int current = StampIndex < 0 || StampIndex >= stampCount ? 0 : StampIndex + 1;
                    int sel = EditorGUILayout.Popup("Stamp", current, names);
                    StampIndex = sel == 0 ? -1 : sel - 1;
                }
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static Color BrushColorForMode() =>
            Mode == PaintMode.Erase ? ScatterGizmos.EraseColor : ScatterGizmos.BrushColor;
    }
}
