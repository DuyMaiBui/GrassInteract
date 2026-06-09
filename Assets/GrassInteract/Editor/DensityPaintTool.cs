#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Terrain-tool-style density brush for a <see cref="DensityScatterLayer"/>. Raycasts the ground,
    /// writes the R8 <c>densityMap</c>, and re-scatters live through <see cref="ScatterRebuildScheduler"/>.
    /// Brush disc + falloff ring drawn via the shared <see cref="ScatterGizmos"/>.
    ///
    /// Paint alignment reuses the runtime <see cref="GrassFieldSpace"/> mapping (SSOT) so painted pixels
    /// land exactly where <see cref="DensityPlacement"/> samples them.
    /// </summary>
    [EditorTool("Density Paint", typeof(DensityScatterLayer))]
    internal sealed class DensityPaintTool : EditorTool
    {
        private enum PaintMode { Paint, Erase, Smooth }

        // ── Brush settings (persisted via EditorPrefs) ─────────────────────────

        private const string K_SIZE = "GrassInteract.Brush.Size";
        private const string K_OPAC = "GrassInteract.Brush.Opacity";
        private const string K_FALL = "GrassInteract.Brush.Falloff";
        private const string K_FLOW = "GrassInteract.Brush.Flow";
        private const string K_MODE = "GrassInteract.Brush.Mode";

        private static float Size    { get => EditorPrefs.GetFloat(K_SIZE, 3f);  set => EditorPrefs.SetFloat(K_SIZE, value); }
        private static float Opacity { get => EditorPrefs.GetFloat(K_OPAC, 1f);  set => EditorPrefs.SetFloat(K_OPAC, value); }
        private static float Falloff { get => EditorPrefs.GetFloat(K_FALL, 0.5f);set => EditorPrefs.SetFloat(K_FALL, value); }
        private static float Flow    { get => EditorPrefs.GetFloat(K_FLOW, 0.5f);set => EditorPrefs.SetFloat(K_FLOW, value); }
        private static PaintMode Mode { get => (PaintMode)EditorPrefs.GetInt(K_MODE, 0); set => EditorPrefs.SetInt(K_MODE, (int)value); }

        // ── Active stroke state ────────────────────────────────────────────────

        private bool      painting;
        private Color[]?  pixels;
        private int       texW, texH;
        private Texture2D? activeMap;

        public override GUIContent toolbarIcon => EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSplat");

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;
            if (this.target is not DensityScatterLayer layer) return;

            (ScatterField? field, int layerIdx) = ScatterFieldLookup.FindOwningField(layer);

            // Validate the density map up front — errors-over-silent-fallback.
            bool valid = layer.Validate(out string error);

            this.DrawSettingsWindow(valid, valid ? null : error, field == null);

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
                    this.BeginStroke(layer);
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

        private void BeginStroke(DensityScatterLayer layer)
        {
            Texture2D map = layer.DensityMap!;
            Undo.RegisterCompleteObjectUndo(map, "Paint Density");
            this.activeMap = map;
            this.texW = map.width;
            this.texH = map.height;
            this.pixels = map.GetPixels();
            this.painting = true;
        }

        private void EndStroke(ScatterField field, int layerIdx)
        {
            this.painting = false;
            if (this.activeMap != null)
            {
                EditorUtility.SetDirty(this.activeMap);
                if (layerIdx >= 0) ScatterRebuildScheduler.MarkDirty(field, layerIdx);
            }
            this.pixels = null;
            this.activeMap = null;
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

            for (int py = minY; py <= maxY; ++py)
            {
                for (int px = minX; px <= maxX; ++px)
                {
                    // World position of this pixel center → horizontal distance to the hit.
                    var uv = new Vector2((px + 0.5f) / this.texW, (py + 0.5f) / this.texH);
                    Vector3 pw = space.UvToWorld(uv, worldHit.y);
                    float distXZ = new Vector2(pw.x - worldHit.x, pw.z - worldHit.z).magnitude;
                    if (distXZ > Size) continue;

                    float t = Size <= 0f ? 0f : distXZ / Size;
                    float falloff = t <= inner ? 1f : Mathf.SmoothStep(1f, 0f, (t - inner) / Mathf.Max(0.0001f, 1f - inner));
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

        // ── In-scene settings panel ────────────────────────────────────────────

        private void DrawSettingsWindow(bool valid, string? error, bool noField)
        {
            Handles.BeginGUI();
            var area = new Rect(8, 8, 240, valid ? 132 : 96);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Density Paint", EditorStyles.boldLabel);

            if (noField)
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
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static Color BrushColorForMode() =>
            Mode == PaintMode.Erase ? ScatterGizmos.EraseColor : ScatterGizmos.BrushColor;
    }
}
