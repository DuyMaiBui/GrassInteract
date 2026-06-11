#nullable enable
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Main authoring window for the GrassInteract scatter system.
    /// Opens from <c>Tools &gt; GrassInteract &gt; Scatter Studio</c>, or by double-clicking /
    /// pressing the inspector button on a <see cref="TerrainScatterConfig"/> asset.
    ///
    /// Responsibilities:
    /// - Tracks the active <see cref="ScatterField"/> via a header <see cref="ObjectField"/> and
    ///   Unity's Selection API (auto-follows scene selection).
    /// - Binds directly to a <see cref="TerrainScatterConfig"/> even when no field is present
    ///   (config-only mode); surfaces a "Create ScatterField" button in Paint/Place tabs.
    /// - Tab bar selects one of four pages: Layer · Brushes · Paint · Place.
    ///   Auto-context switches tabs based on the selected layer type.
    /// - Builds a <see cref="SerializedObject"/> over the active <see cref="TerrainScatterConfig"/>
    ///   and binds the <c>rootVisualElement</c> so all <see cref="PropertyField"/>s auto-sync.
    /// - Hosts <see cref="LayerRailView"/> (left rail) and <see cref="LayerPanelView"/> (center).
    /// - Wires header toggles to <see cref="ScatterFieldEditorTick"/> (the surviving SSOT).
    /// - Rebuilds on demand via the "Rebuild" header button → <see cref="ScatterField.Rebuild"/>.
    /// - Adds <c>.pro</c> / <c>.light</c> class to the root element for Phase 5 USS theming.
    ///
    /// All re-scatter is routed through <see cref="ScatterRebuildScheduler"/> inside
    /// <see cref="LayerRailView"/> and <see cref="LayerPanelView"/>; this class never calls
    /// <c>Rebuild()</c> directly (except the explicit Rebuild button which is the escape hatch).
    /// </summary>
    internal sealed class ScatterStudioWindow : EditorWindow
    {
        // ── Asset paths ───────────────────────────────────────────────────────

        private const string UXML_PATH      = "Assets/GrassInteract/Editor/ScatterStudio/ScatterStudio.uxml";
        private const string USS_PATH       = "Assets/GrassInteract/Editor/ScatterStudio/ScatterStudio.uss";
        private const string LIGHT_USS_PATH = "Assets/GrassInteract/Editor/ScatterStudio/ScatterStudioLight.uss";

        // ── Session persistence keys ──────────────────────────────────────────
        // SessionState survives domain reload (recompile) but clears on editor restart,
        // so the restored binding is never stale across sessions.

        private const string SESSION_KEY_FIELD  = "GrassInteract.ScatterStudio.FieldGID";
        private const string SESSION_KEY_CONFIG = "GrassInteract.ScatterStudio.ConfigGUID";

        // ── Tab indices ───────────────────────────────────────────────────────

        private const int TAB_LAYER   = 0;
        private const int TAB_BRUSHES = 1;
        private const int TAB_PAINT   = 2;
        private const int TAB_PLACE   = 3;

        // ── State ─────────────────────────────────────────────────────────────

        private ScatterField?          activeField;
        private TerrainScatterConfig?  activeConfig;
        private SerializedObject?      serializedConfig;
        private int                    activeTabIndex = TAB_LAYER;

        // ── Sub-views ─────────────────────────────────────────────────────────

        private LayerRailView?    railView;
        private LayerPanelView?   panelView;
        private BrushLibraryView? brushLibraryView;
        private DensityPaintPanel? densityPaintPanel;
        private InstancePanel?    instancePanel;

        // ── Tab bar elements ──────────────────────────────────────────────────

        private Button?       tabLayerBtn;
        private Button?       tabBrushesBtn;
        private Button?       tabPaintBtn;
        private Button?       tabPlaceBtn;
        private VisualElement? pageLayer;
        private VisualElement? pageBrushes;
        private VisualElement? pagePaint;
        private VisualElement? pagePlace;

        // ── Header controls ───────────────────────────────────────────────────

        private Toggle? collidersToggle;

        // ── "No field" banners (one per content page that needs a field) ──────

        private Label? paintNoFieldLabel;
        private Label? placeNoFieldLabel;

        // ── Menu item ─────────────────────────────────────────────────────────

        [MenuItem("Tools/GrassInteract/Scatter Studio")]
        internal static void OpenWindow()
        {
            var window = GetWindow<ScatterStudioWindow>("Scatter Studio");
            window.minSize = new Vector2(480f, 320f);
            window.Show();
        }

        /// <summary>
        /// Opens the window bound to <paramref name="config"/>. Resolves an owning
        /// <see cref="ScatterField"/> in the current scene if one exists; otherwise opens in
        /// config-only mode and surfaces the "Create ScatterField" button.
        /// </summary>
        internal static void OpenForConfig(TerrainScatterConfig config)
        {
            var window = GetWindow<ScatterStudioWindow>("Scatter Studio");
            window.minSize = new Vector2(480f, 320f);
            window.Show();

            // Try to find a scene field that references this config.
            ScatterField? owningField = null;
            ScatterField[] all = Object.FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            foreach (ScatterField f in all)
            {
                if (f.Config == config)
                {
                    owningField = f;
                    break;
                }
            }

            window.SetActiveConfig(config, owningField);
        }

        // ── EditorWindow lifecycle ────────────────────────────────────────────

        private void CreateGUI()
        {
            // Load UXML
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);
            if (uxml == null)
            {
                Debug.LogError($"[ScatterStudioWindow] Could not load UXML at path: {UXML_PATH}.");
                this.rootVisualElement.Add(new Label($"Error: UXML not found at {UXML_PATH}"));
                return;
            }

            // Load USS
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(USS_PATH);
            if (uss == null)
            {
                Debug.LogError($"[ScatterStudioWindow] Could not load USS at path: {USS_PATH}.");
                this.rootVisualElement.Add(new Label($"Error: USS not found at {USS_PATH}"));
                return;
            }

            // Apply skin class before any other setup so USS can target .pro / .light
            this.rootVisualElement.AddToClassList(EditorGUIUtility.isProSkin ? "pro" : "light");

            uxml.CloneTree(this.rootVisualElement);
            this.rootVisualElement.styleSheets.Add(uss);

            // Light-skin palette overrides (.light-scoped rules; inert under .pro)
            var lightUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LIGHT_USS_PATH);
            if (lightUss != null)
                this.rootVisualElement.styleSheets.Add(lightUss);

            // Cache tab bar elements
            this.tabLayerBtn  = this.rootVisualElement.Q<Button>("tab-layer");
            this.tabBrushesBtn = this.rootVisualElement.Q<Button>("tab-brushes");
            this.tabPaintBtn  = this.rootVisualElement.Q<Button>("tab-paint");
            this.tabPlaceBtn  = this.rootVisualElement.Q<Button>("tab-place");
            this.pageLayer    = this.rootVisualElement.Q<VisualElement>("page-layer");
            this.pageBrushes  = this.rootVisualElement.Q<VisualElement>("page-brushes");
            this.pagePaint    = this.rootVisualElement.Q<VisualElement>("page-paint");
            this.pagePlace    = this.rootVisualElement.Q<VisualElement>("page-place");

            // Wire tab buttons
            this.tabLayerBtn?.RegisterCallback<ClickEvent>(_ => this.SelectTab(TAB_LAYER));
            this.tabBrushesBtn?.RegisterCallback<ClickEvent>(_ => this.SelectTab(TAB_BRUSHES));
            this.tabPaintBtn?.RegisterCallback<ClickEvent>(_ => this.SelectTab(TAB_PAINT));
            this.tabPlaceBtn?.RegisterCallback<ClickEvent>(_ => this.SelectTab(TAB_PLACE));

            // Add "no field" hint labels to paint + place pages
            this.paintNoFieldLabel = new Label("Assign a ScatterField in the header (or Create one) to enable painting.")
            {
                style =
                {
                    marginTop = 12,
                    marginLeft = 8,
                    marginRight = 8,
                    whiteSpace = WhiteSpace.Normal,
                }
            };
            this.pagePaint?.Add(this.paintNoFieldLabel);

            this.placeNoFieldLabel = new Label("Assign a ScatterField in the header (or Create one) to enable placement.")
            {
                style =
                {
                    marginTop = 12,
                    marginLeft = 8,
                    marginRight = 8,
                    whiteSpace = WhiteSpace.Normal,
                }
            };
            this.pagePlace?.Add(this.placeNoFieldLabel);

            // Wire header controls
            this.WireHeader();

            // Initialize sub-views
            this.railView          = new LayerRailView(this.rootVisualElement.Q<VisualElement>("layer-rail"));
            this.panelView         = new LayerPanelView(this.rootVisualElement.Q<VisualElement>("layer-panel-scroll"));
            this.brushLibraryView  = new BrushLibraryView(this.rootVisualElement.Q<VisualElement>("brush-library"));
            this.densityPaintPanel = new DensityPaintPanel(this.rootVisualElement.Q<VisualElement>("density-paint-panel"));
            this.instancePanel     = new InstancePanel(this.rootVisualElement.Q<VisualElement>("instance-panel"));

            this.railView.LayerSelected += this.OnLayerSelected;

            // Default tab state (no layer selected)
            this.RefreshTabVisibility(selectedLayer: null);
            this.SelectTab(TAB_LAYER);

            // Hook selection changes
            Selection.selectionChanged += this.OnSelectionChanged;

            // Restore the field/config bound before the last domain reload; else seed from selection.
            this.RestoreOrSyncSelection();
        }

        private void OnDestroy()
        {
            Selection.selectionChanged -= this.OnSelectionChanged;
            this.instancePanel?.Cleanup();
        }

        // ── Header wiring ─────────────────────────────────────────────────────

        private void WireHeader()
        {
            // Field picker (ObjectField)
            var fieldPicker = this.rootVisualElement.Q<ObjectField>("field-picker");
            if (fieldPicker != null)
            {
                fieldPicker.objectType = typeof(ScatterField);
                fieldPicker.RegisterValueChangedCallback(evt =>
                {
                    this.SetActiveField(evt.newValue as ScatterField);
                });
            }

            // Colliders toggle — wired to ScatterFieldEditorTick.PreviewColliders (the SSOT)
            this.collidersToggle = this.rootVisualElement.Q<Toggle>("colliders-toggle");
            if (this.collidersToggle != null)
            {
                this.collidersToggle.SetValueWithoutNotify(ScatterFieldEditorTick.PreviewColliders);
                this.collidersToggle.RegisterValueChangedCallback(evt =>
                {
                    ScatterFieldEditorTick.PreviewColliders = evt.newValue;
                });
            }

            // Rebuild button — explicit escape hatch; calls field.Rebuild() directly (by design)
            var rebuildButton = this.rootVisualElement.Q<Button>("rebuild-button");
            if (rebuildButton != null)
            {
                rebuildButton.clicked += () =>
                {
                    if (this.activeField != null)
                        this.activeField.Rebuild();
                };
            }
        }

        // ── Tab management ────────────────────────────────────────────────────

        /// <summary>Shows the page for <paramref name="tabIndex"/> and hides all others.</summary>
        private void SelectTab(int tabIndex)
        {
            this.activeTabIndex = tabIndex;

            // Button active-class toggling
            SetTabActive(this.tabLayerBtn,   tabIndex == TAB_LAYER);
            SetTabActive(this.tabBrushesBtn, tabIndex == TAB_BRUSHES);
            SetTabActive(this.tabPaintBtn,   tabIndex == TAB_PAINT);
            SetTabActive(this.tabPlaceBtn,   tabIndex == TAB_PLACE);

            // Page visibility
            SetPageVisible(this.pageLayer,   tabIndex == TAB_LAYER);
            SetPageVisible(this.pageBrushes, tabIndex == TAB_BRUSHES);
            SetPageVisible(this.pagePaint,   tabIndex == TAB_PAINT);
            SetPageVisible(this.pagePlace,   tabIndex == TAB_PLACE);

            // Auto-activate the scoped InstancePlacementTool when the Place tab is shown,
            // so clicking the tab is sufficient — no separate button press required.
            if (tabIndex == TAB_PLACE)
                this.instancePanel?.ActivateTool();
        }

        private static void SetTabActive(Button? btn, bool active)
        {
            if (btn == null) return;
            if (active)
                btn.AddToClassList("tab-button--active");
            else
                btn.RemoveFromClassList("tab-button--active");
        }

        private static void SetPageVisible(VisualElement? page, bool visible)
        {
            if (page == null) return;
            page.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Shows/hides tab buttons based on the type of the selected layer.
        /// - No layer    → only Layer tab enabled.
        /// - Density     → Layer, Brushes, Paint.
        /// - Instance    → Layer, Place.
        /// </summary>
        private void RefreshTabVisibility(ScatterLayer? selectedLayer)
        {
            bool isDensity  = selectedLayer is DensityScatterLayer;
            bool isInstance = selectedLayer is InstanceScatterLayer;

            SetTabEnabled(this.tabLayerBtn,   enabled: true);
            SetTabEnabled(this.tabBrushesBtn, enabled: isDensity);
            SetTabEnabled(this.tabPaintBtn,   enabled: isDensity);
            SetTabEnabled(this.tabPlaceBtn,   enabled: isInstance);

            // Snap active tab to a visible/enabled one
            if (this.activeTabIndex == TAB_BRUSHES && !isDensity)
                this.SelectTab(TAB_LAYER);
            else if (this.activeTabIndex == TAB_PAINT && !isDensity)
                this.SelectTab(TAB_LAYER);
            else if (this.activeTabIndex == TAB_PLACE && !isInstance)
                this.SelectTab(TAB_LAYER);

            // Auto-switch to the contextual tab when a layer type becomes available
            if (isDensity && this.activeTabIndex == TAB_LAYER)
                this.SelectTab(TAB_PAINT);
            else if (isInstance && this.activeTabIndex == TAB_LAYER)
                this.SelectTab(TAB_PLACE);
        }

        private static void SetTabEnabled(Button? btn, bool enabled)
        {
            if (btn == null) return;
            btn.SetEnabled(enabled);
            if (!enabled)
                btn.AddToClassList("tab-button--disabled");
            else
                btn.RemoveFromClassList("tab-button--disabled");
        }

        // ── Selection handling ────────────────────────────────────────────────

        private void OnSelectionChanged()
        {
            this.SyncToSelection();
        }

        /// <summary>
        /// Restores the field/config bound before the last domain reload from <see cref="SessionState"/>.
        /// Falls back to config-only mode if the scene field can't resolve, then to the live selection.
        /// </summary>
        private void RestoreOrSyncSelection()
        {
            // 1. Try the ScatterField scene object via its GlobalObjectId.
            string fieldGid = SessionState.GetString(SESSION_KEY_FIELD, string.Empty);
            if (!string.IsNullOrEmpty(fieldGid) && GlobalObjectId.TryParse(fieldGid, out GlobalObjectId gid))
            {
                if (GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) is ScatterField field)
                {
                    this.SetActiveField(field);
                    return;
                }
            }

            // 2. Fall back to config-only mode via the asset GUID.
            string configGuid = SessionState.GetString(SESSION_KEY_CONFIG, string.Empty);
            if (!string.IsNullOrEmpty(configGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(configGuid);
                var config = AssetDatabase.LoadAssetAtPath<TerrainScatterConfig>(path);
                if (config != null)
                {
                    this.SetActiveConfig(config, null);
                    return;
                }
            }

            // 3. Nothing persisted (or it no longer resolves) — seed from the live selection.
            this.SyncToSelection();
        }

        /// <summary>Writes the current field/config identity to <see cref="SessionState"/> for restore.</summary>
        private static void SaveSessionBinding(ScatterField? field, TerrainScatterConfig? config)
        {
            if (field != null)
                SessionState.SetString(SESSION_KEY_FIELD, GlobalObjectId.GetGlobalObjectIdSlow(field).ToString());
            else
                SessionState.EraseString(SESSION_KEY_FIELD);

            string configGuid = config != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(config))
                : string.Empty;
            if (!string.IsNullOrEmpty(configGuid))
                SessionState.SetString(SESSION_KEY_CONFIG, configGuid);
            else
                SessionState.EraseString(SESSION_KEY_CONFIG);
        }

        private void SyncToSelection()
        {
            // Prefer a ScatterField directly in the selection
            foreach (var obj in Selection.objects)
            {
                if (obj is ScatterField sf)
                {
                    this.SetActiveField(sf);
                    return;
                }
                if (obj is GameObject go)
                {
                    var comp = go.GetComponent<ScatterField>();
                    if (comp != null)
                    {
                        this.SetActiveField(comp);
                        return;
                    }
                }
            }
            // Do NOT clear the active field on unrelated selection — keep last valid field active
        }

        // ── Field binding ─────────────────────────────────────────────────────

        private void SetActiveField(ScatterField? field)
        {
            TerrainScatterConfig? config = field?.Config;
            this.SetActiveConfig(config, field);
        }

        /// <summary>
        /// Binds the window to a config (required) and an optional owning field.
        /// When <paramref name="field"/> is null the window operates in config-only mode:
        /// layer and brush editing work; Paint and Place tabs show a "Create ScatterField" banner.
        /// </summary>
        internal void SetActiveConfig(TerrainScatterConfig? config, ScatterField? field)
        {
            bool fieldChanged  = !ReferenceEquals(this.activeField, field);
            bool configChanged = !ReferenceEquals(this.activeConfig, config);
            if (!fieldChanged && !configChanged) return;

            this.activeField  = field;
            this.activeConfig = config;

            // Persist for restore across the next domain reload (recompile).
            SaveSessionBinding(field, config);

            // Sync the field picker
            var fieldPicker = this.rootVisualElement.Q<ObjectField>("field-picker");
            if (fieldPicker != null)
                fieldPicker.SetValueWithoutNotify(field);

            // Build SerializedObject over the config
            this.serializedConfig = config != null ? new SerializedObject(config) : null;

            // Bind rootVisualElement to config so PropertyFields inside panels auto-sync
            if (this.serializedConfig != null)
                this.rootVisualElement.Bind(this.serializedConfig);
            else
                this.rootVisualElement.Unbind();

            // Rebuild rail
            this.railView?.Bind(field, this.serializedConfig);

            // Refresh brush library + paint/instance panels
            this.brushLibraryView?.Bind(field);
            this.densityPaintPanel?.Bind(field);
            this.instancePanel?.Bind(field);

            // Clear panel (no layer selected after config change)
            this.panelView?.ShowLayer(null);

            // Refresh "Create ScatterField" banners
            this.RefreshNoFieldBanners();

            // Reset tab context — no layer selected after rebind
            this.RefreshTabVisibility(selectedLayer: null);
            this.SelectTab(TAB_LAYER);
        }

        /// <summary>Shows or hides the "Create ScatterField" UI on paint/place pages.</summary>
        private void RefreshNoFieldBanners()
        {
            bool hasField = this.activeField != null;

            // Show/hide hint labels
            if (this.paintNoFieldLabel != null)
                this.paintNoFieldLabel.style.display = hasField ? DisplayStyle.None : DisplayStyle.Flex;
            if (this.placeNoFieldLabel != null)
                this.placeNoFieldLabel.style.display = hasField ? DisplayStyle.None : DisplayStyle.Flex;

            // Ensure the Create button exists/is removed from both pages
            this.EnsureCreateFieldButton(this.pagePaint, "create-field-btn-paint", !hasField);
            this.EnsureCreateFieldButton(this.pagePlace, "create-field-btn-place", !hasField);
        }

        private void EnsureCreateFieldButton(VisualElement? page, string btnName, bool show)
        {
            if (page == null) return;
            var existing = page.Q<Button>(btnName);
            if (!show)
            {
                existing?.RemoveFromHierarchy();
                return;
            }
            if (existing != null) return; // already there

            var btn = new Button();
            btn.name = btnName;
            btn.text = "Create ScatterField";
            btn.style.marginTop = 6;
            btn.style.marginLeft = 8;
            btn.style.marginRight = 8;
            btn.clicked += this.OnCreateScatterFieldClicked;
            page.Add(btn);
        }

        private void OnCreateScatterFieldClicked()
        {
            if (this.activeConfig == null) return;

            // Create a new GameObject + ScatterField component
            var go = new GameObject("ScatterField");
            var field = go.AddComponent<ScatterField>();

            // Assign the config via SerializedObject so Undo + dirty are tracked properly
            var so = new SerializedObject(field);
            SerializedProperty configProp = so.FindProperty("config");
            if (configProp != null)
            {
                configProp.objectReferenceValue = this.activeConfig;
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning("[ScatterStudioWindow] Could not find 'config' serialized property on ScatterField.");
            }

            Undo.RegisterCreatedObjectUndo(go, "Create ScatterField");
            Selection.activeGameObject = go;

            // Rebind the window to the new field
            this.SetActiveConfig(this.activeConfig, field);
        }

        // ── Layer selection ───────────────────────────────────────────────────

        private void OnLayerSelected(ScatterLayer? layer)
        {
            this.panelView?.ShowLayer(layer);
            this.instancePanel?.BindLayer(layer as InstanceScatterLayer);
            this.densityPaintPanel?.BindLayer(layer as DensityScatterLayer);
            this.RefreshTabVisibility(layer);
        }
    }
}
