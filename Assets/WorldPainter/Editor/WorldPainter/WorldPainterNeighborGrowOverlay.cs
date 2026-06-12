#nullable enable
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// SceneView overlay that draws translucent clickable quad outlines at every open N/E/S/W
    /// neighbour edge of existing tiles. Click → AddTile. Shift-click → add + select for sculpt.
    /// Uses <see cref="WorldMapAsset.HasOpenNeighbor"/> (P1 API).
    /// </summary>
    [Overlay(typeof(SceneView), "wp-neighbor-grow", "WorldPainter Grow")]
    internal sealed class WorldPainterNeighborGrowOverlay : Overlay
    {
        private static readonly Color GHOST_FILL   = new Color(0.30f, 0.80f, 0.40f, 0.12f);
        private static readonly Color GHOST_BORDER = new Color(0.30f, 0.80f, 0.40f, 0.70f);
        private static readonly Color HOVER_FILL   = new Color(0.50f, 1.00f, 0.60f, 0.25f);
        private static readonly Color HOVER_BORDER = new Color(0.50f, 1.00f, 0.60f, 1.00f);
        private const float DRAW_Y = 0.05f;

        private bool sceneGuiRegistered;

        // ── Overlay lifecycle ─────────────────────────────────────────────────

        public override UnityEngine.UIElements.VisualElement CreatePanelContent()
        {
            if (!this.sceneGuiRegistered)
            {
                SceneView.duringSceneGui += this.OnSceneGUI;
                this.displayedChanged   += this.OnDisplayedChanged;
                this.sceneGuiRegistered  = true;
            }
            return new UnityEngine.UIElements.Label("Tile-grow ghost quads active");
        }

        private void OnDisplayedChanged(bool visible)
        {
            if (!visible)
            {
                SceneView.duringSceneGui -= this.OnSceneGUI;
                this.displayedChanged    -= this.OnDisplayedChanged;
                this.sceneGuiRegistered   = false;
            }
        }

        // ── SceneGUI ──────────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sceneView)
        {
            var painter = WorldPainterState.ActivePainter;
            if (painter == null) return;

            var map = painter.Map;
            if (map == null) return;

            var openSlots = CollectOpenSlots(map);
            if (openSlots.Length == 0) return;

            float tileSize = map.Grid.tileSizeM;
            Event e = Event.current;

            Vector2Int? mouseSlot = ResolveMouseSlot(openSlots, tileSize);

            if (e.type == EventType.MouseDown && e.button == 0 && mouseSlot.HasValue)
            {
                bool shiftHeld = (e.modifiers & EventModifiers.Shift) != 0;
                GrowTile(painter, map, mouseSlot.Value, shiftHeld);
                e.Use();
                return;
            }

            foreach (var coord in openSlots)
                DrawGhostQuad(coord, tileSize, mouseSlot.HasValue && mouseSlot.Value == coord);

            if (mouseSlot.HasValue)
                sceneView.Repaint();
        }

        // ── Grow ──────────────────────────────────────────────────────────────

        private static void GrowTile(WorldPainter painter, WorldMapAsset map,
                                     Vector2Int coord, bool selectForSculpt)
        {
            Undo.RecordObject(map, "Grow Tile");
            var tile = WorldMapAssetLifecycle.AddTile(map, coord);
            EditorUtility.SetDirty(painter);
            EditorUtility.SetDirty(map);

            if (selectForSculpt && tile != null)
                Selection.activeObject = tile;

            Debug.Log($"[WorldPainterNeighborGrowOverlay] Added tile at {coord}" +
                      (selectForSculpt ? " (selected for sculpt)." : "."));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Vector2Int[] CollectOpenSlots(WorldMapAsset map)
        {
            var result = new System.Collections.Generic.HashSet<Vector2Int>();
            foreach (var coord in map.EnumerateTileCoords())
            {
                if (map.HasOpenNeighbor(coord, out var openEdges))
                    foreach (var edge in openEdges)
                        result.Add(edge);
            }
            var arr = new Vector2Int[result.Count];
            result.CopyTo(arr);
            return arr;
        }

        private static Vector2Int? ResolveMouseSlot(Vector2Int[] slots, float tileSize)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            float t = ray.direction.y == 0f ? -1f : (DRAW_Y - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return null;

            Vector3 hit = ray.origin + ray.direction * t;
            foreach (var coord in slots)
            {
                float cx = coord.x * tileSize;
                float cz = coord.y * tileSize;
                if (hit.x >= cx && hit.x <= cx + tileSize && hit.z >= cz && hit.z <= cz + tileSize)
                    return coord;
            }
            return null;
        }

        private static void DrawGhostQuad(Vector2Int coord, float tileSize, bool hovered)
        {
            float x0 = coord.x * tileSize, z0 = coord.y * tileSize;
            float x1 = x0 + tileSize,      z1 = z0 + tileSize;
            float y  = DRAW_Y;

            var v0 = new Vector3(x0, y, z0);
            var v1 = new Vector3(x1, y, z0);
            var v2 = new Vector3(x1, y, z1);
            var v3 = new Vector3(x0, y, z1);

            Color fill   = hovered ? HOVER_FILL   : GHOST_FILL;
            Color border = hovered ? HOVER_BORDER : GHOST_BORDER;

            Handles.DrawSolidRectangleWithOutline(new[] { v0, v1, v2, v3 }, fill, Color.clear);

            Handles.color = border;
            Handles.DrawLines(new[] { v0, v1, v1, v2, v2, v3, v3, v0 });

            Handles.Label(new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f), "+");
        }
    }
}
