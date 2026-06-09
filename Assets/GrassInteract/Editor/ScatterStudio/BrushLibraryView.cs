#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrassInteract.Editor
{
    /// <summary>
    /// UI Toolkit view that renders the brush-stamp thumbnail grid in the Scatter Studio window.
    /// Attaches to the <c>#brush-library</c> <see cref="VisualElement"/> reserved by Phase 1.B
    /// in <c>ScatterStudio.uxml</c> — this class does NOT edit the uxml file.
    ///
    /// Tabs: <b>Global</b> stamps (<see cref="ScatterBrushLibraryProvider.Library"/>) and
    /// <b>Config</b> stamps (<see cref="TerrainScatterConfig.BrushStamps"/>).
    ///
    /// All mutating operations are Undo-wrapped. StampRef is written to
    /// <see cref="ScatterAuthoringState.I"/>.
    /// </summary>
    internal sealed class BrushLibraryView
    {
        // ── Tabs ──────────────────────────────────────────────────────────────

        private enum Tab { Global, Config }

        // ── Root ──────────────────────────────────────────────────────────────

        private readonly VisualElement root;

        // ── State ─────────────────────────────────────────────────────────────

        private Tab            activeTab    = Tab.Global;
        private ScatterField?  activeField;
        private int            renamingIndex = -1; // index of tile currently being renamed (-1 = none)

        // ── Elements ──────────────────────────────────────────────────────────

        private readonly VisualElement gridContainer;
        private readonly Button        tabGlobal;
        private readonly Button        tabConfig;
        private readonly Button        addStampButton;

        // ── Tile size ─────────────────────────────────────────────────────────

        private const float TILE_SIZE    = 72f;
        private const float TILE_PADDING = 4f;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates the view and attaches it to <paramref name="mountPoint"/>.
        /// </summary>
        /// <param name="mountPoint">The <c>#brush-library</c> VisualElement from the uxml.</param>
        internal BrushLibraryView(VisualElement mountPoint)
        {
            this.root = mountPoint;

            // ── Header: tab strip + add button ────────────────────────────────
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems    = Align.Center;
            header.style.marginBottom  = 4f;

            this.tabGlobal = new Button(() => this.SwitchTab(Tab.Global)) { text = "Global" };
            this.tabConfig = new Button(() => this.SwitchTab(Tab.Config)) { text = "Config" };
            this.addStampButton = new Button(this.OnAddStamp) { text = "+ New Stamp" };
            this.addStampButton.style.marginLeft = StyleKeyword.Auto; // push to the right

            header.Add(this.tabGlobal);
            header.Add(this.tabConfig);
            header.Add(this.addStampButton);
            this.root.Add(header);

            // ── Tile grid (wrapping flex row) ─────────────────────────────────
            this.gridContainer = new VisualElement();
            this.gridContainer.style.flexDirection = FlexDirection.Row;
            this.gridContainer.style.flexWrap      = Wrap.Wrap;
            this.gridContainer.style.paddingTop    = TILE_PADDING;
            this.root.Add(this.gridContainer);

            // ── Drop-zone: drag a texture onto the header to add a stamp ──────
            this.root.RegisterCallback<DragUpdatedEvent>(this.OnDragUpdated);
            this.root.RegisterCallback<DragPerformEvent>(this.OnDragPerform);

            // ── Undo listener ─────────────────────────────────────────────────
            Undo.undoRedoPerformed += this.Rebuild;

            this.Rebuild();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Binds the view to a (possibly null) scatter field.</summary>
        internal void Bind(ScatterField? field)
        {
            this.activeField = field;
            this.Rebuild();
        }

        // ── Rebuild ───────────────────────────────────────────────────────────

        private void Rebuild()
        {
            this.gridContainer.Clear();
            this.renamingIndex = -1;

            this.UpdateTabStyles();
            this.AddNoneTile();

            if (this.activeTab == Tab.Global)
                this.BuildGlobalTiles();
            else
                this.BuildConfigTiles();
        }

        private void UpdateTabStyles()
        {
            // Simple active/inactive visual: use border on the active tab.
            SetTabActive(this.tabGlobal, this.activeTab == Tab.Global);
            SetTabActive(this.tabConfig, this.activeTab == Tab.Config);
        }

        private static void SetTabActive(Button btn, bool active)
        {
            btn.style.borderBottomWidth = active ? 2f : 0f;
            btn.style.borderBottomColor = active
                ? new Color(0.3f, 0.6f, 1f)
                : Color.clear;
        }

        private void SwitchTab(Tab tab)
        {
            if (this.activeTab == tab) return;
            this.activeTab = tab;
            this.Rebuild();
        }

        // ── "None" tile ───────────────────────────────────────────────────────

        private void AddNoneTile()
        {
            bool selected = ScatterAuthoringState.I.ActiveStamp.IsNone;
            var tile = this.MakeTile(null, "None\n(procedural)", selected, -1);

            tile.clicked += () =>
            {
                ScatterAuthoringState.I.ActiveStamp = new StampRef(StampRef.StampSource.None, 0);
                this.Rebuild();
            };

            this.gridContainer.Add(tile);
        }

        // ── Global tab ────────────────────────────────────────────────────────

        private void BuildGlobalTiles()
        {
            IReadOnlyList<BrushStamp> stamps = ScatterBrushLibraryProvider.Library.Stamps;
            for (int i = 0; i < stamps.Count; ++i)
            {
                int idx = i; // capture
                BrushStamp stamp = stamps[i];

                bool selected = !ScatterAuthoringState.I.ActiveStamp.IsNone
                    && ScatterAuthoringState.I.ActiveStamp.Source == StampRef.StampSource.Global
                    && ScatterAuthoringState.I.ActiveStamp.Index == idx;

                var tile = this.MakeTile(stamp.Shape, stamp.DisplayName, selected, idx);

                // Click: select this stamp
                tile.clicked += () =>
                {
                    ScatterAuthoringState.I.ActiveStamp = new StampRef(StampRef.StampSource.Global, idx);
                    this.Rebuild();
                };

                // Right-click: rename / remove
                tile.RegisterCallback<ContextClickEvent>(_ => this.ShowGlobalContextMenu(idx));

                // Double-click: start inline rename
                tile.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.clickCount == 2)
                        this.BeginRename(idx, tile);
                });

                this.gridContainer.Add(tile);
            }
        }

        private void ShowGlobalContextMenu(int idx)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename"),   false, () => this.BeginRenameDeferred(idx));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Remove"),   false, () =>
            {
                ScatterBrushLibraryProvider.Library.Remove(idx);
                this.Rebuild();
            });
            menu.ShowAsContext();
        }

        // ── Config tab ────────────────────────────────────────────────────────

        private void BuildConfigTiles()
        {
            if (this.activeField?.Config == null)
            {
                this.gridContainer.Add(new Label("No config selected.") { style = { unityFontStyleAndWeight = FontStyle.Italic } });
                return;
            }

            IReadOnlyList<BrushStamp> stamps = this.activeField.Config.BrushStamps;
            for (int i = 0; i < stamps.Count; ++i)
            {
                int idx = i;
                BrushStamp stamp = stamps[i];

                bool selected = !ScatterAuthoringState.I.ActiveStamp.IsNone
                    && ScatterAuthoringState.I.ActiveStamp.Source == StampRef.StampSource.Config
                    && ScatterAuthoringState.I.ActiveStamp.Index == idx;

                var tile = this.MakeTile(stamp.Shape, stamp.DisplayName, selected, idx);

                tile.clicked += () =>
                {
                    ScatterAuthoringState.I.ActiveStamp = new StampRef(StampRef.StampSource.Config, idx);
                    this.Rebuild();
                };

                tile.RegisterCallback<ContextClickEvent>(_ => this.ShowConfigContextMenu(idx));

                tile.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.clickCount == 2)
                        this.BeginRename(idx, tile);
                });

                this.gridContainer.Add(tile);
            }
        }

        private void ShowConfigContextMenu(int idx)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename"), false, () => this.BeginRenameDeferred(idx));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Remove"), false, () => this.RemoveConfigStamp(idx));
            menu.ShowAsContext();
        }

        private void RemoveConfigStamp(int idx)
        {
            var config = this.activeField?.Config;
            if (config == null) return;

            var so = new SerializedObject(config);
            var list = so.FindProperty("brushStamps");
            if (list == null || idx < 0 || idx >= list.arraySize) return;

            // Get the stamp sub-asset before removing from the list so we can destroy it.
            var stampProp = list.GetArrayElementAtIndex(idx);
            var stampObj = stampProp.objectReferenceValue;

            Undo.RegisterCompleteObjectUndo(config, "Remove Config Brush Stamp");

            // Clear the reference first (required before DeleteArrayElementAtIndex to avoid
            // the "set-to-null then delete" two-step).
            stampProp.objectReferenceValue = null;
            list.DeleteArrayElementAtIndex(idx);
            so.ApplyModifiedProperties();

            if (stampObj != null)
                Undo.DestroyObjectImmediate(stampObj);

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            this.Rebuild();
        }

        // ── Add stamp ─────────────────────────────────────────────────────────

        private void OnAddStamp()
        {
            if (this.activeTab == Tab.Global)
            {
                ScatterBrushLibraryProvider.Library.AddStamp(null);
            }
            else
            {
                this.AddConfigStamp(null);
            }
            this.Rebuild();
        }

        private void AddConfigStamp(Texture2D? shape)
        {
            var config = this.activeField?.Config;
            if (config == null)
            {
                Debug.LogWarning("[BrushLibraryView] No active config — cannot add a config stamp.");
                return;
            }

            Undo.RegisterCompleteObjectUndo(config, "Add Config Brush Stamp");

            var stamp = ScriptableObject.CreateInstance<BrushStamp>();
            stamp.name = shape != null ? shape.name : "Stamp";
            Undo.RegisterCreatedObjectUndo(stamp, "Add Config Brush Stamp");

            AssetDatabase.AddObjectToAsset(stamp, config);

            // Assign shape via SerializedObject so it tracks correctly.
            var so = new SerializedObject(stamp);
            var shapeProp = so.FindProperty("shape");
            if (shapeProp != null && shape != null)
            {
                shapeProp.objectReferenceValue = shape;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Append to the config's brushStamps list.
            var configSo = new SerializedObject(config);
            var list = configSo.FindProperty("brushStamps");
            if (list != null)
            {
                list.arraySize += 1;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = stamp;
                configSo.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        // ── Drag and drop ─────────────────────────────────────────────────────

        private void OnDragUpdated(DragUpdatedEvent _)
        {
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is Texture2D)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    return;
                }
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
        }

        private void OnDragPerform(DragPerformEvent _)
        {
            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is Texture2D tex)
                {
                    if (this.activeTab == Tab.Global)
                        ScatterBrushLibraryProvider.Library.AddStamp(tex);
                    else
                        this.AddConfigStamp(tex);
                }
            }
            this.Rebuild();
        }

        // ── Inline rename ─────────────────────────────────────────────────────

        /// <summary>Deferred rename (called from context menu — the menu closes before we can focus a field).</summary>
        private void BeginRenameDeferred(int idx)
        {
            // Schedule one frame later via EditorApplication.delayCall so the context menu
            // closes before we add the TextField.
            EditorApplication.delayCall += () => this.BeginRenameAtIndex(idx);
        }

        private void BeginRenameAtIndex(int idx)
        {
            this.renamingIndex = idx;
            this.Rebuild();
        }

        private void BeginRename(int idx, VisualElement tile)
        {
            this.renamingIndex = idx;
            this.Rebuild();
        }

        // ── Tile factory ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates a single tile: a square button showing the stamp thumbnail (or a placeholder)
        /// and a label. When <paramref name="tileIndex"/> == <see cref="renamingIndex"/> the label
        /// is replaced by a focused <see cref="TextField"/> for inline renaming.
        /// </summary>
        private Button MakeTile(Texture2D? thumb, string label, bool selected, int tileIndex)
        {
            var btn = new Button();
            btn.style.width     = TILE_SIZE;
            btn.style.height    = TILE_SIZE + 20f;
            btn.style.marginTop = btn.style.marginBottom =
                btn.style.marginLeft = btn.style.marginRight = TILE_PADDING;
            btn.style.paddingTop = btn.style.paddingBottom =
                btn.style.paddingLeft = btn.style.paddingRight = 2f;
            btn.style.flexDirection   = FlexDirection.Column;
            btn.style.alignItems      = Align.Center;
            btn.style.justifyContent  = Justify.SpaceBetween;

            if (selected)
            {
                btn.style.borderTopWidth = btn.style.borderBottomWidth =
                    btn.style.borderLeftWidth = btn.style.borderRightWidth = 2f;
                btn.style.borderTopColor = btn.style.borderBottomColor =
                    btn.style.borderLeftColor = btn.style.borderRightColor = new Color(0.3f, 0.6f, 1f);
            }

            // Thumbnail
            var image = new Image();
            image.style.width  = TILE_SIZE - 8f;
            image.style.height = TILE_SIZE - 8f;
            if (thumb != null)
                image.image = thumb;
            else
            {
                // Placeholder: draw a dark square with an "x" label
                image.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            }
            btn.Add(image);

            // Label or rename field
            if (tileIndex >= 0 && tileIndex == this.renamingIndex)
            {
                var tf = new TextField();
                tf.value = label;
                tf.style.width = TILE_SIZE - 4f;
                tf.style.fontSize = 9f;
                tf.RegisterCallback<FocusOutEvent>(_ => this.CommitRename(tileIndex, tf.value));
                tf.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                        this.CommitRename(tileIndex, tf.value);
                    else if (evt.keyCode == KeyCode.Escape)
                    {
                        this.renamingIndex = -1;
                        this.Rebuild();
                    }
                });
                btn.Add(tf);
                // Schedule focus so it works after the rebuild layout pass.
                btn.schedule.Execute(() => tf.Focus()).StartingIn(50);
            }
            else
            {
                var lbl = new Label(label);
                lbl.style.fontSize  = 9f;
                lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                lbl.style.whiteSpace = WhiteSpace.Normal;
                lbl.style.overflow   = Overflow.Hidden;
                lbl.style.maxWidth   = TILE_SIZE - 4f;
                btn.Add(lbl);
            }

            return btn;
        }

        // ── Commit rename ─────────────────────────────────────────────────────

        private void CommitRename(int idx, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                this.renamingIndex = -1;
                this.Rebuild();
                return;
            }

            if (this.activeTab == Tab.Global)
                ScatterBrushLibraryProvider.Library.Rename(idx, newName);
            else
                this.RenameConfigStamp(idx, newName);

            this.renamingIndex = -1;
            this.Rebuild();
        }

        private void RenameConfigStamp(int idx, string newName)
        {
            var config = this.activeField?.Config;
            if (config == null) return;

            var stamps = config.BrushStamps;
            if (idx < 0 || idx >= stamps.Count) return;

            var stamp = stamps[idx];
            Undo.RegisterCompleteObjectUndo(stamp, "Rename Config Brush Stamp");

            var so = new SerializedObject(stamp);
            var nameProp = so.FindProperty("displayName");
            if (nameProp != null)
            {
                nameProp.stringValue = newName;
                so.ApplyModifiedProperties();
            }
            stamp.name = newName;
            EditorUtility.SetDirty(stamp);
            AssetDatabase.SaveAssets();
        }
    }
}
