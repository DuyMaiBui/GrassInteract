#nullable enable

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Inspector strip that exposes the tile-topology mode (Off / Add / Remove) as two
    /// toggle buttons. Clicking Add turns Add mode ON and forces Remove off; clicking the
    /// active button again switches back to Off. While a mode is active,
    /// <see cref="WorldPainterTileGhostHandler"/> draws green/red ghost quads in the scene
    /// view and the user clicks them to add/remove tiles in place. The explicit (X, Y) +
    /// "Add"/"Remove" buttons are still available below the mode toggle for users who
    /// prefer typing a coord directly.
    /// </summary>
    internal sealed class WorldPainterTileStrip
    {
        // ── UI handles ────────────────────────────────────────────────────────

        private Button?       addModeButton;
        private Button?       removeModeButton;
        private IntegerField? coordXField;
        private IntegerField? coordYField;
        private Button?       coordAddButton;
        private Button?       coordRemoveButton;
        private Label?        statusLabel;

        // ── Build ─────────────────────────────────────────────────────────────

        public VisualElement Build(WorldPainter painter)
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.marginTop     = 4;

            // ── Mode toggle row ───────────────────────────────────────────────
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.alignItems    = Align.Center;

            var modeLabel = new Label("Mode:");
            modeLabel.style.fontSize    = 10;
            modeLabel.style.marginRight = 4;
            modeRow.Add(modeLabel);

            this.addModeButton = new Button(() => ToggleMode(WorldPainterState.TileEditModeKind.Add))
            {
                text    = "+ Add",
                tooltip = "Toggle Add mode: scene view shows green ghost quads at open " +
                          "neighbour edges. Click a ghost to add a tile there.",
            };
            this.addModeButton.AddToClassList("wp-mode-btn");
            this.addModeButton.style.flexGrow    = 1;
            this.addModeButton.style.marginRight = 2;
            modeRow.Add(this.addModeButton);

            this.removeModeButton = new Button(() => ToggleMode(WorldPainterState.TileEditModeKind.Remove))
            {
                text    = "× Remove",
                tooltip = "Toggle Remove mode: scene view shows red ghost quads on existing " +
                          "tiles. Click a ghost to remove (confirmation dialog).",
            };
            this.removeModeButton.AddToClassList("wp-mode-btn");
            this.removeModeButton.style.flexGrow = 1;
            modeRow.Add(this.removeModeButton);

            root.Add(modeRow);

            // ── Explicit coord row ────────────────────────────────────────────
            var coordRow = new VisualElement();
            coordRow.style.flexDirection = FlexDirection.Row;
            coordRow.style.alignItems    = Align.Center;
            coordRow.style.marginTop     = 2;

            var coordLabel = new Label("@");
            coordLabel.style.fontSize    = 10;
            coordLabel.style.marginRight = 2;
            coordRow.Add(coordLabel);

            this.coordXField = new IntegerField { label = "", value = 0 };
            this.coordXField.style.width       = 44;
            this.coordXField.style.marginRight = 2;
            this.coordXField.tooltip = "X coordinate";
            coordRow.Add(this.coordXField);

            this.coordYField = new IntegerField { label = "", value = 0 };
            this.coordYField.style.width       = 44;
            this.coordYField.style.marginRight = 4;
            this.coordYField.tooltip = "Y coordinate";
            coordRow.Add(this.coordYField);

            if (WorldPainterState.LastStrokedCoord.HasValue)
            {
                this.coordXField.value = WorldPainterState.LastStrokedCoord.Value.x;
                this.coordYField.value = WorldPainterState.LastStrokedCoord.Value.y;
            }

            this.coordAddButton = new Button(() => this.OnAddClicked(painter))
            {
                text    = "+",
                tooltip = "Add a tile at the explicit (X, Y) above.",
            };
            this.coordAddButton.style.width       = 26;
            this.coordAddButton.style.marginRight = 2;
            coordRow.Add(this.coordAddButton);

            this.coordRemoveButton = new Button(() => this.OnRemoveClicked(painter))
            {
                text    = "×",
                tooltip = "Remove the tile at the explicit (X, Y) above (confirmation dialog).",
            };
            this.coordRemoveButton.style.width = 26;
            coordRow.Add(this.coordRemoveButton);

            this.statusLabel = new Label(string.Empty);
            this.statusLabel.style.fontSize   = 9;
            this.statusLabel.style.marginLeft = 6;
            this.statusLabel.style.color      = new StyleColor(new Color(1f, 0.6f, 0.2f));
            coordRow.Add(this.statusLabel);

            root.Add(coordRow);

            // ── Wire mode-state changes back into the button visuals ─────────
            this.RefreshModeButtons();
            System.Action<WorldPainterState.TileEditModeKind> onModeChanged = _ => this.RefreshModeButtons();
            WorldPainterState.TileEditModeChanged += onModeChanged;
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
                WorldPainterState.TileEditModeChanged -= onModeChanged);

            // 200ms poll for the EXPLICIT coord row enabled state (driven by occupancy).
            root.schedule.Execute(() => this.UpdateCoordButtonStates(painter)).Every(200);

            return root;
        }

        // ── Mode toggle ───────────────────────────────────────────────────────

        /// <summary>
        /// Click semantic: clicking a mode button while it's already active turns the mode OFF.
        /// Clicking while the OTHER mode is active switches directly to this mode (does not
        /// require a separate "off" click).
        /// </summary>
        private static void ToggleMode(WorldPainterState.TileEditModeKind kind)
        {
            WorldPainterState.TileEditMode =
                WorldPainterState.TileEditMode == kind
                    ? WorldPainterState.TileEditModeKind.Off
                    : kind;
            SceneView.RepaintAll(); // ghost handler needs an immediate repaint
        }

        private void RefreshModeButtons()
        {
            if (this.addModeButton == null || this.removeModeButton == null) return;

            var mode = WorldPainterState.TileEditMode;
            bool addActive    = mode == WorldPainterState.TileEditModeKind.Add;
            bool removeActive = mode == WorldPainterState.TileEditModeKind.Remove;

            // Active mode button keeps the highlight via the shared USS class.
            if (addActive) this.addModeButton.AddToClassList("wp-mode-btn--active");
            else           this.addModeButton.RemoveFromClassList("wp-mode-btn--active");
            if (removeActive) this.removeModeButton.AddToClassList("wp-mode-btn--active");
            else              this.removeModeButton.RemoveFromClassList("wp-mode-btn--active");

            // While one mode is on, the OTHER button is disabled (the active one stays
            // clickable so the user can toggle it back off).
            this.addModeButton.SetEnabled(!removeActive);
            this.removeModeButton.SetEnabled(!addActive);
        }

        // ── Explicit-coord path (typed X/Y) ────────────────────────────────────

        private void OnAddClicked(WorldPainter painter)
        {
            WorldMapAsset? map = painter.Map;
            if (map == null) { this.ShowStatus("No map assigned."); return; }

            var coord = this.CurrentCoord();
            if (map.GetTile(coord) != null)
            {
                this.ShowStatus($"Tile already exists at {coord}.");
                return;
            }

            WorldMapAssetLifecycle.AddTile(map, coord);
            EditorUtility.SetDirty(painter);
            SceneView.RepaintAll();
            this.ShowStatus($"Added tile at {coord}.");
        }

        private void OnRemoveClicked(WorldPainter painter)
        {
            WorldMapAsset? map = painter.Map;
            if (map == null) { this.ShowStatus("No map assigned."); return; }

            var coord = this.CurrentCoord();
            if (map.GetTile(coord) == null)
            {
                this.ShowStatus($"No tile at {coord}.");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Remove tile?",
                $"Remove tile at {coord}? This deletes its sub-assets including per-grass density textures.",
                "Remove",
                "Cancel");

            if (!confirmed) return;

            WorldMapAssetLifecycle.RemoveTile(map, coord);
            EditorUtility.SetDirty(painter);
            SceneView.RepaintAll();
            this.ShowStatus($"Removed tile at {coord}.");
        }

        private Vector2Int CurrentCoord()
        {
            int x = this.coordXField?.value ?? 0;
            int y = this.coordYField?.value ?? 0;
            return new Vector2Int(x, y);
        }

        private void UpdateCoordButtonStates(WorldPainter painter)
        {
            WorldMapAsset? map = painter.Map;
            if (map == null)
            {
                if (this.coordAddButton    != null) this.coordAddButton.SetEnabled(false);
                if (this.coordRemoveButton != null) this.coordRemoveButton.SetEnabled(false);
                return;
            }

            var coord     = this.CurrentCoord();
            bool occupied = map.GetTile(coord) != null;

            if (this.coordAddButton    != null) this.coordAddButton.SetEnabled(!occupied);
            if (this.coordRemoveButton != null) this.coordRemoveButton.SetEnabled(occupied);
        }

        private void ShowStatus(string message)
        {
            if (this.statusLabel == null) return;
            this.statusLabel.text = message;
            this.statusLabel.schedule.Execute(() =>
            {
                if (this.statusLabel != null) this.statusLabel.text = string.Empty;
            }).StartingIn(3000);
        }
    }
}
