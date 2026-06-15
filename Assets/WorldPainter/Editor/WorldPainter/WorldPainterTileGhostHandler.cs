#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// SceneView-side renderer + click handler for inspector-driven tile-topology editing.
    /// Replaces the deleted <c>WorldPainterNeighborGrowOverlay</c>'s scene-overlay icon —
    /// the mode toggle now lives in the inspector's <see cref="WorldPainterTileStrip"/>,
    /// this class only reads <see cref="WorldPainterState.TileEditMode"/> and reacts.
    ///
    /// Lifetime is the inspector's lifetime (created in CreateInspectorGUI, disposed in
    /// OnDisable). The inspector subscribes <see cref="OnSceneGui"/> to
    /// <see cref="SceneView.duringSceneGui"/>.
    /// </summary>
    internal sealed class WorldPainterTileGhostHandler
    {
        // Add (grow) — green ghost preview.
        private static readonly Color ADD_FILL         = new Color(0.30f, 0.80f, 0.40f, 0.12f);
        private static readonly Color ADD_BORDER       = new Color(0.30f, 0.80f, 0.40f, 0.70f);
        private static readonly Color ADD_HOVER_FILL   = new Color(0.50f, 1.00f, 0.60f, 0.25f);
        private static readonly Color ADD_HOVER_BORDER = new Color(0.50f, 1.00f, 0.60f, 1.00f);

        // Remove (delete) — red ghost preview, same visual language as Add.
        private static readonly Color REMOVE_FILL         = new Color(0.85f, 0.25f, 0.25f, 0.14f);
        private static readonly Color REMOVE_BORDER       = new Color(0.85f, 0.25f, 0.25f, 0.70f);
        private static readonly Color REMOVE_HOVER_FILL   = new Color(1.00f, 0.35f, 0.35f, 0.30f);
        private static readonly Color REMOVE_HOVER_BORDER = new Color(1.00f, 0.35f, 0.35f, 1.00f);

        private const float DRAW_Y = 0.05f;

        public void OnSceneGui(SceneView sceneView)
        {
            var mode = WorldPainterState.TileEditMode;
            if (mode == WorldPainterState.TileEditModeKind.Off) return;

            var painter = WorldPainterState.ActivePainter;
            if (painter == null) return;

            var map = painter.Map;
            if (map == null) return;

            float tileSize = map.Grid.tileSizeM;
            if (tileSize <= 0f) tileSize = 256f;
            Event e = Event.current;

            // Claim the input layer so the click doesn't fall through to default object picking.
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            if (mode == WorldPainterState.TileEditModeKind.Add)
                this.DrawAndHandleAdd(sceneView, painter, map, tileSize, e);
            else
                this.DrawAndHandleRemove(sceneView, painter, map, tileSize, e);
        }

        // ── Add (grow) ────────────────────────────────────────────────────────

        private void DrawAndHandleAdd(SceneView sceneView, WorldPainter painter,
                                      WorldMapAsset map, float tileSize, Event e)
        {
            Vector2Int[] openSlots = CollectOpenSlots(map);
            if (openSlots.Length == 0) return;

            Vector2Int? mouseSlot = ResolveMouseTile(openSlots, tileSize);

            if (e.type == EventType.MouseDown && e.button == 0 && mouseSlot.HasValue)
            {
                bool shiftHeld = (e.modifiers & EventModifiers.Shift) != 0;
                AddTileAt(painter, map, mouseSlot.Value, shiftHeld);
                e.Use();
                return;
            }

            if (e.type != EventType.Repaint && e.type != EventType.Layout)
                if (mouseSlot.HasValue) sceneView.Repaint();

            foreach (var coord in openSlots)
            {
                bool hovered = mouseSlot.HasValue && mouseSlot.Value == coord;
                DrawTileQuad(coord, tileSize,
                    hovered ? ADD_HOVER_FILL   : ADD_FILL,
                    hovered ? ADD_HOVER_BORDER : ADD_BORDER,
                    "+");
            }
        }

        // ── Remove (delete) ──────────────────────────────────────────────────

        private void DrawAndHandleRemove(SceneView sceneView, WorldPainter painter,
                                         WorldMapAsset map, float tileSize, Event e)
        {
            Vector2Int[] tiles = CollectExistingTiles(map);
            if (tiles.Length == 0) return;

            Vector2Int? mouseTile = ResolveMouseTile(tiles, tileSize);

            if (e.type == EventType.MouseDown && e.button == 0 && mouseTile.HasValue)
            {
                TryRemoveTile(painter, map, mouseTile.Value);
                e.Use();
                return;
            }

            if (e.type != EventType.Repaint && e.type != EventType.Layout)
                if (mouseTile.HasValue) sceneView.Repaint();

            foreach (var coord in tiles)
            {
                bool hovered = mouseTile.HasValue && mouseTile.Value == coord;
                DrawTileQuad(coord, tileSize,
                    hovered ? REMOVE_HOVER_FILL   : REMOVE_FILL,
                    hovered ? REMOVE_HOVER_BORDER : REMOVE_BORDER,
                    "X");
            }
        }

        // ── Operations ───────────────────────────────────────────────────────

        private static void AddTileAt(WorldPainter painter, WorldMapAsset map,
                                      Vector2Int coord, bool selectForSculpt)
        {
            Undo.RecordObject(map, "Add Tile");
            var tile = WorldMapAssetLifecycle.AddTile(map, coord);
            EditorUtility.SetDirty(painter);
            EditorUtility.SetDirty(map);

            // Incremental build of the new tile (does NOT rebuild every other tile).
            painter.AddTileToRender(tile);

            if (selectForSculpt && tile != null)
                Selection.activeObject = tile;
        }

        private static void TryRemoveTile(WorldPainter painter, WorldMapAsset map, Vector2Int coord)
        {
            if (map.TileCount <= 1)
            {
                EditorUtility.DisplayDialog(
                    "Cannot remove the last tile",
                    "This is the only tile in the map. Removing it would leave an empty map " +
                    "with no edge to grow new tiles from.",
                    "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                $"Delete tile ({coord.x}, {coord.y})?",
                "This permanently destroys the tile's height, splat, and scatter data. " +
                "This cannot be undone.",
                "Delete", "Cancel");
            if (!confirmed) return;

            painter.RemoveTileFromRender(coord);
            WorldMapAssetLifecycle.RemoveTile(map, coord);
            EditorUtility.SetDirty(map);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static Vector2Int[] CollectOpenSlots(WorldMapAsset map)
        {
            var result = new HashSet<Vector2Int>();
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

        private static Vector2Int[] CollectExistingTiles(WorldMapAsset map)
        {
            var list = new List<Vector2Int>(map.TileCount);
            foreach (var coord in map.EnumerateTileCoords())
                list.Add(coord);
            return list.ToArray();
        }

        private static Vector2Int? ResolveMouseTile(Vector2Int[] coords, float tileSize)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            float t = ray.direction.y == 0f ? -1f : (DRAW_Y - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return null;

            Vector3 hit = ray.origin + ray.direction * t;
            foreach (var coord in coords)
            {
                float cx = coord.x * tileSize;
                float cz = coord.y * tileSize;
                if (hit.x >= cx && hit.x <= cx + tileSize && hit.z >= cz && hit.z <= cz + tileSize)
                    return coord;
            }
            return null;
        }

        private static void DrawTileQuad(Vector2Int coord, float tileSize,
                                         Color fill, Color border, string label)
        {
            float x0 = coord.x * tileSize, z0 = coord.y * tileSize;
            float x1 = x0 + tileSize,      z1 = z0 + tileSize;
            float y  = DRAW_Y;

            var v0 = new Vector3(x0, y, z0);
            var v1 = new Vector3(x1, y, z0);
            var v2 = new Vector3(x1, y, z1);
            var v3 = new Vector3(x0, y, z1);

            Handles.DrawSolidRectangleWithOutline(new[] { v0, v1, v2, v3 }, fill, Color.clear);

            Handles.color = border;
            Handles.DrawLines(new[] { v0, v1, v1, v2, v2, v3, v3, v0 });

            Handles.Label(new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f), label);
        }
    }
}
