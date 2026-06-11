#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Scene-view brush EditorTool for GPU terrain sculpt and paint.
    ///
    /// P2: resolves ALL tiles a brush circle overlaps per stroke step, not just the
    /// tile under the cursor center. Uses <see cref="TerrainPaintTargetResolver.Resolve"/>
    /// with residencySet=null (operates on the renderer's explicit tiles list).
    ///
    /// Per-tile RT cache (≤4 entries) keeps live VTF preview active on multiple tiles
    /// simultaneously across a tile border.  Cache released on mouse-up and
    /// OnWillBeDeactivated (no RT leak).
    ///
    /// Stroke dispatch + RT cache methods live in TerrainSculptTool.Stroke.cs (partial).
    /// </summary>
    [EditorTool("Terrain Sculpt")]
    internal sealed partial class TerrainSculptTool : EditorTool
    {
        private ComputeShader? brushCompute;

        // Writeback is tool-owned. Undo uses the shared SSOT on TerrainSculptState.
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
        }

        public override void OnWillBeDeactivated()
        {
            EditorApplication.update -= this.OnEditorUpdate;
            // M1: if deactivated mid-stroke, rebind committed Texture2D on every touched tile
            // before releasing RTs so no engine holds a dangling RT as _HeightTex.
            if (this.stroke?.InStroke == true)
                this.TeardownActiveStroke(TerrainSculptState.ActiveRenderer);
            else
                this.rtCache.ReleaseAll();
        }

        private void OnEditorUpdate() => this.writeback.Tick();

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;
            if (this.brushCompute == null || this.stroke == null) return;

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
                    this.HandleMouseDown(renderer, worldPoint, controlId);
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && this.stroke.InStroke:
                    if (hasHit) this.HandleMouseDrag(renderer, worldPoint);
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && this.stroke.InStroke:
                    this.HandleMouseUp(renderer);
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }

            this.DrawHud();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        internal static TerrainTileAsset? FindTileForCoord(
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
                worldPoint = hit.point; normal = hit.normal; return true;
            }
            var tiles = renderer.Tiles;
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] == null) continue;
                var tile     = tiles[i];
                var origin2d = TerrainWorldGrid.TileOriginWorld(tile.tileCoord);
                float midY   = (tile.minHeight + tile.maxHeight) * 0.5f;
                var plane    = new Plane(Vector3.up, new Vector3(origin2d.x, midY, origin2d.y));
                if (plane.Raycast(ray, out float dist))
                {
                    worldPoint = ray.GetPoint(dist); normal = Vector3.up; return true;
                }
            }
            worldPoint = Vector3.zero; normal = Vector3.up; return false;
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
