#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Header mini-map showing loaded tiles as colored squares and the scene
    /// camera position as a small dot.
    /// Refreshed on the 0.15s async tick from the inspector.
    /// Design §4.5.
    /// </summary>
    internal sealed class WorldPainterMiniMap
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int TICK_MS   = 150;
        private const int CELL_PX   = 10;
        private const int PADDING   = 2;

        // ── State ─────────────────────────────────────────────────────────────

        private IMGUIContainer? canvas;

        // Snapshot updated on tick — drawn from IMGUI callback.
        private List<Vector2Int> tileCoords = new();
        private Vector2 cameraDot;    // normalized [0..1] within tile-grid bounds
        private RectInt gridBounds;   // min/max of tile coords

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Builds and returns the mini-map VisualElement.</summary>
        public VisualElement Build(WorldPainter painter)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("wp-mini-map");

            this.canvas = new IMGUIContainer(this.DrawIMGUI);
            wrapper.Add(this.canvas);

            wrapper.schedule.Execute(() => this.Tick(painter)).Every(TICK_MS);

            return wrapper;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private void Tick(WorldPainter painter)
        {
            // Rebuild tile coord list from painter.
            this.tileCoords.Clear();
            if (painter.Tiles != null)
            {
                foreach (var entry in painter.Tiles)
                    if (entry.tileAsset != null)
                        this.tileCoords.Add(entry.coord);
            }

            // Compute grid bounds.
            if (this.tileCoords.Count > 0)
            {
                int minX = int.MaxValue, minY = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue;
                foreach (var c in this.tileCoords)
                {
                    if (c.x < minX) minX = c.x;
                    if (c.y < minY) minY = c.y;
                    if (c.x > maxX) maxX = c.x;
                    if (c.y > maxY) maxY = c.y;
                }
                this.gridBounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            else
            {
                this.gridBounds = new RectInt(0, 0, 1, 1);
            }

            // Camera dot: project scene camera world position to normalized tile-grid.
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                float tileSize = painter.WorldGridConfig.tileSizeM > 0
                    ? painter.WorldGridConfig.tileSizeM : 256f;
                float camX = sceneView.camera.transform.position.x / tileSize;
                float camZ = sceneView.camera.transform.position.z / tileSize;
                float normX = (this.gridBounds.width  > 0)
                    ? (camX - this.gridBounds.xMin) / this.gridBounds.width  : 0.5f;
                float normY = (this.gridBounds.height > 0)
                    ? (camZ - this.gridBounds.yMin) / this.gridBounds.height : 0.5f;
                this.cameraDot = new Vector2(normX, normY);
            }

            this.canvas?.MarkDirtyRepaint();
        }

        private void DrawIMGUI()
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type != EventType.Repaint) return;

            // Background.
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));

            if (this.tileCoords.Count == 0) return;

            float cellW = rect.width  / Mathf.Max(1, this.gridBounds.width);
            float cellH = rect.height / Mathf.Max(1, this.gridBounds.height);

            // Draw tile cells.
            foreach (var coord in this.tileCoords)
            {
                float x = rect.x + (coord.x - this.gridBounds.xMin) * cellW;
                float y = rect.y + (coord.y - this.gridBounds.yMin) * cellH;
                var cellRect = new Rect(x + 1, y + 1, cellW - 2, cellH - 2);
                EditorGUI.DrawRect(cellRect, new Color(0.25f, 0.5f, 0.35f, 0.8f));
            }

            // Draw camera dot.
            float dotX = rect.x + this.cameraDot.x * rect.width;
            float dotY = rect.y + this.cameraDot.y * rect.height;
            EditorGUI.DrawRect(new Rect(dotX - 2, dotY - 2, 5, 5), Color.white);
        }
    }
}
