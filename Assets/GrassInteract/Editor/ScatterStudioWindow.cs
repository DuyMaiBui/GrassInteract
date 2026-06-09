#nullable enable
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Main authoring window for the GrassInteract scatter system.
    /// Opens from <c>Tools &gt; GrassInteract &gt; Scatter Studio</c>.
    ///
    /// Responsibilities:
    /// - Tracks the active <see cref="ScatterField"/> via a header <see cref="ObjectField"/> and
    ///   Unity's Selection API (auto-follows scene selection).
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

        private const string UXML_PATH = "Assets/GrassInteract/Editor/ScatterStudio/ScatterStudio.uxml";
        private const string USS_PATH  = "Assets/GrassInteract/Editor/ScatterStudio/ScatterStudio.uss";
        private const string LIGHT_USS_PATH = "Assets/GrassInteract/Editor/ScatterStudio/ScatterStudioLight.uss";

        // ── State ─────────────────────────────────────────────────────────────

        private ScatterField? activeField;
        private SerializedObject? serializedConfig;

        // ── Sub-views ─────────────────────────────────────────────────────────

        private LayerRailView? railView;
        private LayerPanelView? panelView;
        private BrushLibraryView? brushLibraryView;
        private DensityPaintPanel? densityPaintPanel;
        private InstancePanel? instancePanel;

        // ── Header controls (cached for toggle wiring) ────────────────────────

        private Toggle? previewToggle;
        private Toggle? collidersToggle;

        // ── Menu item ─────────────────────────────────────────────────────────

        [MenuItem("Tools/GrassInteract/Scatter Studio")]
        internal static void OpenWindow()
        {
            var window = GetWindow<ScatterStudioWindow>("Scatter Studio");
            window.minSize = new Vector2(480f, 320f);
            window.Show();
        }

        // ── EditorWindow lifecycle ────────────────────────────────────────────

        private void CreateGUI()
        {
            // Load UXML
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);
            if (uxml == null)
            {
                Debug.LogError($"[ScatterStudioWindow] Could not load UXML at path: {UXML_PATH}. " +
                               "Ensure the file exists at that exact location.");
                this.rootVisualElement.Add(new Label($"Error: UXML not found at {UXML_PATH}"));
                return;
            }

            // Load USS
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(USS_PATH);
            if (uss == null)
            {
                Debug.LogError($"[ScatterStudioWindow] Could not load USS at path: {USS_PATH}. " +
                               "Ensure the file exists at that exact location.");
                this.rootVisualElement.Add(new Label($"Error: USS not found at {USS_PATH}"));
                return;
            }

            // Apply skin class before any other setup so Phase 5 USS can target .pro / .light
            this.rootVisualElement.AddToClassList(EditorGUIUtility.isProSkin ? "pro" : "light");

            uxml.CloneTree(this.rootVisualElement);
            this.rootVisualElement.styleSheets.Add(uss);

            // Light-skin palette overrides (.light-scoped rules; inert under .pro)
            var lightUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LIGHT_USS_PATH);
            if (lightUss != null)
                this.rootVisualElement.styleSheets.Add(lightUss);

            // Wire header controls
            this.WireHeader();

            // Initialize sub-views
            this.railView  = new LayerRailView(this.rootVisualElement.Q<VisualElement>("layer-rail"));
            this.panelView = new LayerPanelView(this.rootVisualElement.Q<VisualElement>("layer-panel-scroll"));
            this.brushLibraryView = new BrushLibraryView(this.rootVisualElement.Q<VisualElement>("brush-library"));
            this.densityPaintPanel = new DensityPaintPanel(this.rootVisualElement.Q<VisualElement>("density-paint-panel"));
            this.instancePanel = new InstancePanel(this.rootVisualElement.Q<VisualElement>("instance-panel"));

            this.railView.LayerSelected += this.OnLayerSelected;

            // Hook selection changes
            Selection.selectionChanged += this.OnSelectionChanged;

            // Seed with current selection
            this.SyncToSelection();
        }

        private void OnDestroy()
        {
            Selection.selectionChanged -= this.OnSelectionChanged;
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

            // Preview toggle — wired to ScatterFieldEditorTick.PreviewEnabled (the SSOT)
            this.previewToggle = this.rootVisualElement.Q<Toggle>("preview-toggle");
            if (this.previewToggle != null)
            {
                this.previewToggle.SetValueWithoutNotify(ScatterFieldEditorTick.PreviewEnabled);
                this.previewToggle.RegisterValueChangedCallback(evt =>
                {
                    ScatterFieldEditorTick.PreviewEnabled = evt.newValue;
                    SceneView.RepaintAll();
                    // Keep colliders toggle enabled state in sync
                    if (this.collidersToggle != null)
                        this.collidersToggle.SetEnabled(evt.newValue);
                });
            }

            // Colliders toggle — wired to ScatterFieldEditorTick.PreviewColliders (the SSOT)
            this.collidersToggle = this.rootVisualElement.Q<Toggle>("colliders-toggle");
            if (this.collidersToggle != null)
            {
                this.collidersToggle.SetValueWithoutNotify(ScatterFieldEditorTick.PreviewColliders);
                this.collidersToggle.SetEnabled(ScatterFieldEditorTick.PreviewEnabled);
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

        // ── Selection handling ────────────────────────────────────────────────

        private void OnSelectionChanged()
        {
            this.SyncToSelection();
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
            if (ReferenceEquals(this.activeField, field)) return;

            this.activeField = field;

            // Sync the field picker
            var fieldPicker = this.rootVisualElement.Q<ObjectField>("field-picker");
            if (fieldPicker != null)
                fieldPicker.SetValueWithoutNotify(field);

            // Build SerializedObject over the config
            this.serializedConfig = field?.Config != null
                ? new SerializedObject(field.Config)
                : null;

            // Bind rootVisualElement to config so PropertyFields inside the panel auto-sync
            if (this.serializedConfig != null)
                this.rootVisualElement.Bind(this.serializedConfig);
            else
                this.rootVisualElement.Unbind();

            // Rebuild rail
            this.railView?.Bind(field, this.serializedConfig);

            // Refresh brush library + paint/instance panels for the active field
            this.brushLibraryView?.Bind(field);
            this.densityPaintPanel?.Bind(field);
            this.instancePanel?.Bind(field);

            // Clear panel (no layer selected after field change)
            this.panelView?.ShowLayer(null);
        }

        // ── Layer selection ───────────────────────────────────────────────────

        private void OnLayerSelected(ScatterLayer? layer)
        {
            this.panelView?.ShowLayer(layer);
            this.instancePanel?.BindLayer(layer as InstanceScatterLayer);
        }
    }
}
