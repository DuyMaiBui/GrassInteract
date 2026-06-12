#nullable enable
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// SceneView overlay for tile-topology editing. The panel has an Off / Add / Remove mode
    /// selector (mutually exclusive buttons):
    ///
    ///   • Off: overlay draws nothing and intercepts no clicks (sculpting unaffected).
    ///   • Add (default): translucent green "+" ghost quads at every open N/E/S/W neighbour
    ///     edge of existing tiles. Click → <see cref="WorldMapAssetLifecycle.AddTile"/>.
    ///     Shift-click → add + select the new tile for sculpting.
    ///   • Remove: existing tiles drawn as translucent red "X" ghost quads.
    ///     Click → confirm dialog → <see cref="WorldMapAssetLifecycle.RemoveTile"/>.
    ///
    /// Add and Remove are explicit modes (never both at once) so a delete-click can never hijack a
    /// sculpt-click on an existing tile. Uses <see cref="WorldMapAsset.HasOpenNeighbor"/> (P1 API).
    /// </summary>
    // NOTE: overlay id is intentionally NEW ("wp-tile-editor", not the old "wp-neighbor-grow").
    // Unity persists overlay visibility per id; a previously-seen id keeps its saved (hidden) state
    // and ignores defaultDisplay. A fresh id is the only reliable way to make defaultDisplay=true
    // actually show the overlay by default.
    [Overlay(typeof(SceneView), "wp-tile-editor", "WorldPainter Tiles", true)]
    internal sealed class WorldPainterNeighborGrowOverlay : Overlay
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

        private bool sceneGuiRegistered;

        private enum TileEditMode { Off, Add, Remove }

        // Active mode. Off = draw nothing / intercept nothing. Defaults to Add so the green
        // grow-ghosts show by default (preserves prior behaviour).
        private TileEditMode mode = TileEditMode.Add;

        // Mode buttons kept so the active-highlight refreshes on click.
        private readonly System.Collections.Generic.Dictionary<TileEditMode, Button> modeButtons = new();

        // ── Overlay lifecycle ─────────────────────────────────────────────────

        public override VisualElement CreatePanelContent()
        {
            if (!this.sceneGuiRegistered)
            {
                SceneView.duringSceneGui += this.OnSceneGUI;
                this.displayedChanged   += this.OnDisplayedChanged;
                this.sceneGuiRegistered  = true;
            }

            var root = new VisualElement();
            root.style.minWidth = 200;

            var hint = new Label("Add: green '+' edges → click to create.\n" +
                                 "Remove: red 'X' tiles → click to delete.");
            hint.style.marginBottom = 6;
            hint.style.whiteSpace = WhiteSpace.Normal;
            root.Add(hint);

            // Mode selector — Off / Add / Remove (mutually exclusive).
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            this.modeButtons.Clear();
            this.AddModeButton(row, TileEditMode.Off,    "Off");
            this.AddModeButton(row, TileEditMode.Add,    "Add");
            this.AddModeButton(row, TileEditMode.Remove, "Remove");
            root.Add(row);

            // Hot-rebuild button — forces a full WorldPainter render rebuild on demand.
            var rebuildBtn = new Button(RebuildActivePainter) { text = "Rebuild World" };
            rebuildBtn.tooltip = "Force a full rebuild of the terrain render (disposes + rebuilds every tile). " +
                                 "Use if the render looks out of sync.";
            rebuildBtn.style.marginTop = 6;
            root.Add(rebuildBtn);

            this.RefreshModeButtons();
            return root;
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

        // ── Mode selector ───────────────────────────────────────────────────────

        private void AddModeButton(VisualElement row, TileEditMode m, string label)
        {
            var btn = new Button(() =>
            {
                this.mode = m;
                this.RefreshModeButtons();
                SceneView.RepaintAll();
            }) { text = label };
            btn.style.flexGrow    = 1;
            btn.style.marginRight = (m == TileEditMode.Remove) ? 0 : 2;
            this.modeButtons[m] = btn;
            row.Add(btn);
        }

        private void RefreshModeButtons()
        {
            foreach (var kv in this.modeButtons)
            {
                bool active = kv.Key == this.mode;
                var btn = kv.Value;
                if (active)
                {
                    btn.style.backgroundColor         = new Color(0.24f, 0.37f, 0.58f);
                    btn.style.color                   = Color.white;
                    btn.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                else
                {
                    btn.style.backgroundColor         = StyleKeyword.Null;
                    btn.style.color                   = StyleKeyword.Null;
                    btn.style.unityFontStyleAndWeight = StyleKeyword.Null;
                }
            }
        }

        // ── Hot rebuild ─────────────────────────────────────────────────────────

        private static void RebuildActivePainter()
        {
            var painter = WorldPainterState.ActivePainter;
            if (painter == null)
            {
                Debug.LogWarning("[WorldPainterNeighborGrowOverlay] No active WorldPainter to rebuild " +
                                 "(select the WorldPainter GameObject first).");
                return;
            }

            painter.TryBuild();          // internal — full dispose + rebuild of every tile
            SceneView.RepaintAll();
            Debug.Log("[WorldPainterNeighborGrowOverlay] WorldPainter rebuilt.");
        }

        // ── SceneGUI dispatch ───────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sceneView)
        {
            var painter = WorldPainterState.ActivePainter;
            if (painter == null) return;

            var map = painter.Map;
            if (map == null) return;

            float tileSize = map.Grid.tileSizeM;
            Event e = Event.current;

            switch (this.mode)
            {
                case TileEditMode.Add:
                    this.DrawAndHandleAdd(sceneView, painter, map, tileSize, e);
                    break;
                case TileEditMode.Remove:
                    this.DrawAndHandleRemove(sceneView, painter, map, tileSize, e);
                    break;
                // TileEditMode.Off → draw nothing, intercept nothing.
            }
        }

        // ── Add (grow) ──────────────────────────────────────────────────────────

        private void DrawAndHandleAdd(SceneView sceneView, WorldPainter painter,
                                      WorldMapAsset map, float tileSize, Event e)
        {
            var openSlots = CollectOpenSlots(map);
            if (openSlots.Length == 0) return;

            Vector2Int? mouseSlot = ResolveMouseTile(openSlots, tileSize);

            if (e.type == EventType.MouseDown && e.button == 0 && mouseSlot.HasValue)
            {
                bool shiftHeld = (e.modifiers & EventModifiers.Shift) != 0;
                AddTile(painter, map, mouseSlot.Value, shiftHeld);
                e.Use();
                return;
            }

            foreach (var coord in openSlots)
            {
                bool hovered = mouseSlot.HasValue && mouseSlot.Value == coord;
                DrawTileQuad(coord, tileSize,
                    hovered ? ADD_HOVER_FILL   : ADD_FILL,
                    hovered ? ADD_HOVER_BORDER : ADD_BORDER,
                    "+");
            }

            if (mouseSlot.HasValue) sceneView.Repaint();
        }

        // ── Remove (delete) ──────────────────────────────────────────────────────

        private void DrawAndHandleRemove(SceneView sceneView, WorldPainter painter,
                                         WorldMapAsset map, float tileSize, Event e)
        {
            var tiles = CollectExistingTiles(map);
            if (tiles.Length == 0) return;

            Vector2Int? mouseTile = ResolveMouseTile(tiles, tileSize);

            if (e.type == EventType.MouseDown && e.button == 0 && mouseTile.HasValue)
            {
                TryRemoveTile(painter, map, mouseTile.Value);
                e.Use();
                return;
            }

            foreach (var coord in tiles)
            {
                bool hovered = mouseTile.HasValue && mouseTile.Value == coord;
                DrawTileQuad(coord, tileSize,
                    hovered ? REMOVE_HOVER_FILL   : REMOVE_FILL,
                    hovered ? REMOVE_HOVER_BORDER : REMOVE_BORDER,
                    "X");
            }

            if (mouseTile.HasValue) sceneView.Repaint();
        }

        // ── Operations ──────────────────────────────────────────────────────────

        private static void AddTile(WorldPainter painter, WorldMapAsset map,
                                    Vector2Int coord, bool selectForSculpt)
        {
            Undo.RecordObject(map, "Add Tile");
            var tile = WorldMapAssetLifecycle.AddTile(map, coord);
            EditorUtility.SetDirty(painter);
            EditorUtility.SetDirty(map);

            // Build ONLY the new tile into the live render (incremental) instead of letting a full
            // TryBuild rebuild every tile. See WorldPainter.TileEdit.cs.
            painter.AddTileToRender(tile);

            if (selectForSculpt && tile != null)
                Selection.activeObject = tile;

            Debug.Log($"[WorldPainterNeighborGrowOverlay] Added tile at {coord}" +
                      (selectForSculpt ? " (selected for sculpt)." : "."));
        }

        private static void TryRemoveTile(WorldPainter painter, WorldMapAsset map, Vector2Int coord)
        {
            // Guard the final tile — removing it leaves an empty map with no edge to grow from.
            if (map.TileCount <= 1)
            {
                EditorUtility.DisplayDialog(
                    "Cannot remove the last tile",
                    "This is the only tile in the map. Removing it would leave an empty map with " +
                    "no edge to grow new tiles from.",
                    "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                $"Delete tile ({coord.x}, {coord.y})?",
                "This permanently destroys the tile's height, splat, and scatter data. " +
                "This cannot be undone.",
                "Delete", "Cancel");
            if (!confirmed) return;

            // Tear down ONLY this tile's render engine (incremental) before removing its data, so the
            // rest of the world is not rebuilt. See WorldPainter.TileEdit.cs.
            painter.RemoveTileFromRender(coord);

            WorldMapAssetLifecycle.RemoveTile(map, coord);
            EditorUtility.SetDirty(map);
            Debug.Log($"[WorldPainterNeighborGrowOverlay] Removed tile at {coord}.");
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

        private static Vector2Int[] CollectExistingTiles(WorldMapAsset map)
        {
            var list = new System.Collections.Generic.List<Vector2Int>(map.TileCount);
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
