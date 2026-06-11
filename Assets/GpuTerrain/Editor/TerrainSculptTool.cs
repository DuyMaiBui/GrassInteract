#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Scene-view brush EditorTool for GPU terrain sculpt and paint.
    ///
    /// Retargeted (P1): binds to TerrainSculptState.ActiveRenderer and resolves the
    /// tile under the cursor per stroke, operating on that tile's LIVE engine resources
    /// so the rendered mesh updates in real time via VTF preview binding.
    ///
    /// Working RT lifetime: one RFloat + one ARGBFloat RT owned by the tool, created in
    /// OnActivated, released in OnWillBeDeactivated, re-seeded per stroke from the tile
    /// that is under the cursor.
    /// </summary>
    [EditorTool("Terrain Sculpt")]
    internal sealed class TerrainSculptTool : EditorTool
    {
        private RenderTexture?  heightRT;
        private RenderTexture?  splatRT;
        private ComputeShader?  brushCompute;

        // Current stroke target (resolved per mouse-down)
        private TerrainTileAsset?        strokeTile;
        private TerrainTileGpuResources? strokeGpu;
        private Vector2Int               strokeCoord;

        // Writeback is tool-owned (drives its own EditorApplication.update pump in OnActivated).
        // Undo uses the shared SSOT on TerrainSculptState — never construct a separate instance.
        private readonly TerrainSculptRtWriteback writeback = new TerrainSculptRtWriteback();
        private TerrainBrushStroke? stroke;

        public override GUIContent toolbarIcon =>
            EditorGUIUtility.IconContent("TerrainInspector.TerrainToolRaise");

        public override void OnActivated()
        {
            this.brushCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/GpuTerrain/Shaders/TerrainBrush.compute");
            this.stroke = new TerrainBrushStroke(TerrainSculptState.Undo, this.writeback);
            EditorApplication.update += this.OnEditorUpdate;
            this.EnsureRTs();
        }

        public override void OnWillBeDeactivated()
        {
            EditorApplication.update -= this.OnEditorUpdate;
            this.ReleaseRTs();
        }

        private void OnEditorUpdate() => this.writeback.Tick();

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;
            if (this.brushCompute == null || this.heightRT == null ||
                this.splatRT == null || this.stroke == null) return;

            var renderer = TerrainSculptState.ActiveRenderer;
            if (renderer == null) return;

            Event e         = Event.current;
            int   controlId = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            Ray  ray    = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = this.TryGetBrushWorldPoint(ray, renderer, out Vector3 worldPoint,
                out Vector3 normal);

            if (hasHit)
            {
                TerrainBrushPreview.Set(worldPoint, normal,
                    TerrainSculptState.BrushSize, TerrainSculptState.BrushColor());
                HandleUtility.Repaint();
            }

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && hasHit:
                {
                    var coord = TerrainWorldGrid.WorldToTileCoord(worldPoint.x, worldPoint.z);
                    var engine = renderer.EngineForCoord(coord);
                    var gpu    = renderer.ResourcesForCoord(coord);
                    if (engine == null || gpu == null) break;

                    this.strokeCoord = coord;
                    this.strokeGpu   = gpu;
                    this.strokeTile  = FindTileForCoord(renderer, coord);
                    if (this.strokeTile == null) break;

                    // Seed working RT from current committed height texture.
                    this.SeedHeightRT(gpu);
                    renderer.BeginSculptPreview(coord, this.heightRT!);

                    GUIUtility.hotControl = controlId;
                    this.stroke.BeginStroke(this.strokeTile);
                    this.stroke.Dispatch(worldPoint, this.strokeTile,
                        this.brushCompute, this.heightRT!, this.splatRT!);
                    this.stroke.ThrottledWriteback(true, this.strokeTile,
                        this.strokeGpu, this.heightRT!, this.splatRT!);
                    TerrainSculptState.LastStrokedCoord = coord;
                    e.Use();
                    break;
                }

                case EventType.MouseDrag when e.button == 0 && this.stroke.InStroke:
                    if (hasHit && this.strokeTile != null && this.strokeGpu != null)
                    {
                        this.stroke.Dispatch(worldPoint, this.strokeTile,
                            this.brushCompute, this.heightRT!, this.splatRT!);
                        this.stroke.ThrottledWriteback(false, this.strokeTile,
                            this.strokeGpu, this.heightRT!, this.splatRT!);
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && this.stroke.InStroke:
                    if (this.strokeTile != null && this.strokeGpu != null)
                    {
                        this.stroke.EndStroke(this.strokeTile, this.strokeGpu,
                            this.heightRT!, this.splatRT!);
                        // L1: EndSculptPreview rebinds the Texture2D as _HeightTex.  This is safe
                        // ONLY because TerrainTileGpuResources.Upload reuses the same Texture2D
                        // object when res/format match (T3 stale-rebind fix).  If Upload ever
                        // allocates a new Texture2D on commit, this rebind must refresh the ref.
                        renderer.EndSculptPreview(this.strokeCoord);
                    }
                    this.strokeTile = null;
                    this.strokeGpu  = null;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }

            this.DrawHud();
        }

        // ── Seeding working RT from committed height ──────────────────────────

        private void SeedHeightRT(TerrainTileGpuResources gpu)
        {
            if (this.heightRT == null || gpu.HeightTexture == null) return;
            // Copy normalized [0,1] Texture2D into the RFloat working RT.
            // Decode parity verified: both paths yield [0,1] through SampleHeightVTF.
            Graphics.Blit(gpu.HeightTexture, this.heightRT);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static TerrainTileAsset? FindTileForCoord(
            GpuTerrainRenderer renderer, Vector2Int coord)
        {
            var tiles = renderer.Tiles;
            for (int i = 0; i < tiles.Count; i++)
                if (tiles[i] != null && tiles[i].tileCoord == coord)
                    return tiles[i];
            return null;
        }

        /// <summary>Physics raycast first; falls back to tile mid-height XZ plane.</summary>
        private bool TryGetBrushWorldPoint(Ray ray, GpuTerrainRenderer renderer,
            out Vector3 worldPoint, out Vector3 normal)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            {
                worldPoint = hit.point;
                normal     = hit.normal;
                return true;
            }

            // Plane fallback: use mid-height of first valid tile.
            var tiles = renderer.Tiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] == null) continue;
                var tile    = tiles[i];
                var origin2d = TerrainWorldGrid.TileOriginWorld(tile.tileCoord);
                float midY   = (tile.minHeight + tile.maxHeight) * 0.5f;
                var plane    = new Plane(Vector3.up, new Vector3(origin2d.x, midY, origin2d.y));
                if (plane.Raycast(ray, out float dist))
                {
                    worldPoint = ray.GetPoint(dist);
                    normal     = Vector3.up;
                    return true;
                }
            }

            worldPoint = Vector3.zero;
            normal     = Vector3.up;
            return false;
        }

        private void EnsureRTs()
        {
            int res = TerrainSculptConfig.BRUSH_RT_RES;
            this.heightRT = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat)
                { name = "TerrainToolHeightRT", enableRandomWrite = true };
            this.heightRT.Create();

            this.splatRT = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat)
                { name = "TerrainToolSplatRT", enableRandomWrite = true };
            this.splatRT.Create();
        }

        private void ReleaseRTs()
        {
            if (this.heightRT != null)
            {
                this.heightRT.Release();
                Object.DestroyImmediate(this.heightRT);
                this.heightRT = null;
            }
            if (this.splatRT != null)
            {
                this.splatRT.Release();
                Object.DestroyImmediate(this.splatRT);
                this.splatRT = null;
            }
            this.strokeTile = null;
            this.strokeGpu  = null;
        }

        private void DrawHud()
        {
            Handles.BeginGUI();
            var area = new Rect(8, 8, 210, 52);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Terrain Sculpt", EditorStyles.boldLabel);
            string modeName = TerrainSculptState.PaintMode
                ? $"Paint – Layer {TerrainSculptState.SplatLayer}"
                : TerrainSculptState.SculptSubMode.ToString();
            GUILayout.Label($"Mode: {modeName}", EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
