#nullable enable
using System.Collections.Generic;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    [CustomEditor(typeof(TerrainScatterConfig))]
    public sealed class TerrainScatterConfigEditor : OdinEditor
    {
        private const string PATH_DEFAULT_MATERIAL =
            "Assets/GrassInteract/Editor/Defaults/Default_Material.mat";
        private const string PATH_DEFAULT_GRASS_MESH =
            "Assets/GrassInteract/Editor/Defaults/Default_LOD0_Grass.mesh";
        private const string PATH_DEFAULT_PROP_MESH =
            "Assets/GrassInteract/Editor/Defaults/Default_LOD0_Prop.mesh";

        [SerializeField] private ScatterLayer? selectedLayer;
        [SerializeField] private Vector2 detailScroll;

        private void OnEnable()
        {
            DensityScenePaintSession.SessionChanged += this.OnSessionChanged;
            InstancePlacementOverlay.SessionChanged += this.OnSessionChanged;
        }

        private void OnDisable()
        {
            DensityScenePaintSession.SessionChanged -= this.OnSessionChanged;
            InstancePlacementOverlay.SessionChanged -= this.OnSessionChanged;
        }

        public override void OnInspectorGUI()
        {
            var config = (TerrainScatterConfig)this.target;

            this.serializedObject.Update();
            this.ValidateSelection(config);

            this.DrawHeader();
            EditorGUILayout.Space(6f);
            this.DrawSceneBindingStatus(config);
            EditorGUILayout.Space(6f);
            this.DrawConfigFields();
            EditorGUILayout.Space(8f);
            this.DrawLayerSection(config, "Density Layers", this.GetDensityLayers(config), isDensity: true);
            EditorGUILayout.Space(8f);
            this.DrawLayerSection(config, "Instance Layers", this.GetInstanceLayers(config), isDensity: false);
            EditorGUILayout.Space(8f);
            this.DrawDetailPanel(config);

            this.serializedObject.ApplyModifiedProperties();
        }

        private void OnSessionChanged()
        {
            this.Repaint();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scatter Layer Authoring", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Author density and instance layers directly in this inspector. Scene paint and placement tools stay on the selected layer below.",
                    MessageType.Info);
            }
        }

        private void DrawSceneBindingStatus(TerrainScatterConfig config)
        {
            if (ScatterFieldLookup.TryFindSingleActiveFieldForConfig(config, out ScatterField? field, out string error))
            {
                EditorGUILayout.HelpBox(
                    $"Active ScatterField: {field!.name}. SceneView authoring is ready.",
                    MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(error, MessageType.Warning);
        }

        private void DrawConfigFields()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(this.serializedObject.FindProperty("cullCompute"));
                EditorGUILayout.PropertyField(this.serializedObject.FindProperty("indirectMaterial"));
                EditorGUILayout.PropertyField(this.serializedObject.FindProperty("brushStamps"), includeChildren: true);
            }
        }

        private void DrawLayerSection(TerrainScatterConfig config, string title, IReadOnlyList<ScatterLayer> layers, bool isDensity)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(isDensity ? "+ Density" : "+ Instance", EditorStyles.miniButton, GUILayout.Width(90f)))
                    {
                        this.CreateNewLayer(config, isDensity);
                    }
                }

                if (layers.Count == 0)
                {
                    EditorGUILayout.HelpBox($"No {title.ToLowerInvariant()} configured.", MessageType.Info);
                    return;
                }

                foreach (ScatterLayer layer in layers)
                {
                    if (layer == null)
                        continue;

                    this.DrawLayerRow(layer);
                }
            }
        }

        private void DrawLayerRow(ScatterLayer layer)
        {
            bool isSelected = this.selectedLayer == layer;
            Color previousBackground = GUI.backgroundColor;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = isSelected ? new Color(0.32f, 0.55f, 0.88f) : previousBackground;
                if (GUILayout.Button(layer.name, GUILayout.Height(24f)))
                {
                    this.SelectLayer(layer);
                }
                GUI.backgroundColor = previousBackground;

                GUILayout.Label(layer is DensityScatterLayer ? "Density" : "Instance", EditorStyles.miniLabel, GUILayout.Width(56f));

                if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.Width(56f)))
                {
                    this.RemoveLayerWithConfirmation((TerrainScatterConfig)this.target, layer);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void CreateNewLayer(TerrainScatterConfig config, bool isDensity)
        {
            string defaultName = isDensity
                ? $"Layer_Density_{config.Layers.Count}"
                : $"Layer_Instance_{config.Layers.Count}";

            Material? material = AssetDatabase.LoadAssetAtPath<Material>(PATH_DEFAULT_MATERIAL);
            Mesh? mesh = AssetDatabase.LoadAssetAtPath<Mesh>(isDensity ? PATH_DEFAULT_GRASS_MESH : PATH_DEFAULT_PROP_MESH);

            ScatterLayer? layer = isDensity
                ? config.CreateDensityLayer(defaultName, material, mesh)
                : config.CreateInstanceLayer(defaultName, material, mesh);

            if (layer == null)
                return;

            this.SelectLayer(layer);
            this.Repaint();
        }

        private void RemoveLayerWithConfirmation(TerrainScatterConfig config, ScatterLayer layer)
        {
            if (!EditorUtility.DisplayDialog(
                    "Delete Layer",
                    $"Delete '{layer.name}' and all its owned data? This cannot be undone.",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            this.ExitSceneModeForLayer(layer);
            if (this.selectedLayer == layer)
                this.selectedLayer = null;

            config.DeleteLayer(layer);
            this.Repaint();
        }

        private void DrawDetailPanel(TerrainScatterConfig config)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Selected Layer", EditorStyles.boldLabel);
                this.DrawAuthoringToolsStrip(config);
                EditorGUILayout.Space(6f);

                if (this.selectedLayer == null)
                {
                    EditorGUILayout.HelpBox("Select a layer to edit its properties and Scene authoring tools.", MessageType.Info);
                    return;
                }

                this.detailScroll = EditorGUILayout.BeginScrollView(this.detailScroll, GUILayout.MinHeight(280f));
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(this.selectedLayer.name, EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                    GUILayout.Space(8f);
                    GUILayout.Label(this.selectedLayer is DensityScatterLayer ? "Density" : "Instance", EditorStyles.miniBoldLabel, GUILayout.Width(56f));
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.Space(6f);
                using (var tree = PropertyTree.Create(this.selectedLayer))
                {
                    tree.Draw(false);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawAuthoringToolsStrip(TerrainScatterConfig config)
        {
            if (this.selectedLayer == null)
            {
                EditorGUILayout.HelpBox("Select a layer to enter Scene authoring tools.", MessageType.Info);
                return;
            }

            if (!ScatterFieldLookup.TryFindSingleActiveFieldForConfig(config, out _, out string error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            }

            if (this.selectedLayer is DensityScatterLayer density)
            {
                this.DrawDensityAuthoringTools(density);
                return;
            }

            if (this.selectedLayer is InstanceScatterLayer instance)
            {
                this.DrawInstanceAuthoringTools(instance);
            }
        }

        private void DrawDensityAuthoringTools(DensityScatterLayer layer)
        {
            EditorGUILayout.LabelField("Density Scene Tools", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                this.DrawMiniActionButton("Paint", DensityScenePaintSession.IsActiveFor(layer) && DensityScenePaintSession.Mode == DensityScenePaintMode.Paint, () =>
                {
                    DensityScenePaintSession.Enter(layer, DensityScenePaintMode.Paint);
                });
                this.DrawMiniActionButton("Erase", DensityScenePaintSession.IsActiveFor(layer) && DensityScenePaintSession.Mode == DensityScenePaintMode.Erase, () =>
                {
                    DensityScenePaintSession.Enter(layer, DensityScenePaintMode.Erase);
                });
                this.DrawMiniActionButton("Exit Scene Mode", false, () =>
                {
                    DensityScenePaintSession.Exit();
                });
            }

            EditorGUILayout.Space(4f);
            float radius = EditorGUILayout.Slider("Radius", DensityScenePaintSession.BrushRadius, 0.25f, 50f);
            float strength = EditorGUILayout.Slider("Strength", DensityScenePaintSession.BrushStrength, 0f, 1f);
            float falloff = EditorGUILayout.Slider("Falloff", DensityScenePaintSession.BrushFalloff, 0f, 1f);

            if (!Mathf.Approximately(radius, DensityScenePaintSession.BrushRadius))
                DensityScenePaintSession.SetBrushRadius(radius);
            if (!Mathf.Approximately(strength, DensityScenePaintSession.BrushStrength))
                DensityScenePaintSession.SetBrushStrength(strength);
            if (!Mathf.Approximately(falloff, DensityScenePaintSession.BrushFalloff))
                DensityScenePaintSession.SetBrushFalloff(falloff);

            if (DensityScenePaintSession.IsActiveFor(layer))
            {
                string modeName = DensityScenePaintSession.Mode == DensityScenePaintMode.Paint ? "Paint" : "Erase";
                EditorGUILayout.HelpBox($"Active: {modeName} Scene mode — Esc to exit.", MessageType.None);
            }
        }

        private void DrawInstanceAuthoringTools(InstanceScatterLayer layer)
        {
            EditorGUILayout.LabelField("Instance Scene Tools", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                this.DrawMiniActionButton("Select", InstancePlacementOverlay.IsActiveFor(layer) && InstancePlacementOverlay.Mode == InstanceEditMode.Select, () =>
                {
                    InstancePlacementOverlay.EnterMode(layer, InstanceEditMode.Select);
                });
                this.DrawMiniActionButton("Place", InstancePlacementOverlay.IsActiveFor(layer) && InstancePlacementOverlay.Mode == InstanceEditMode.Place, () =>
                {
                    InstancePlacementOverlay.EnterMode(layer, InstanceEditMode.Place);
                });
                this.DrawMiniActionButton("Erase", InstancePlacementOverlay.IsActiveFor(layer) && InstancePlacementOverlay.Mode == InstanceEditMode.Erase, () =>
                {
                    InstancePlacementOverlay.EnterMode(layer, InstanceEditMode.Erase);
                });
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                this.DrawMiniActionButton("Exit Scene Mode", false, () =>
                {
                    InstancePlacementOverlay.ExitSceneMode();
                });
            }

            EditorGUILayout.Space(4f);
            InstancePlacementOverlay.SnapAlignNormal = EditorGUILayout.Toggle("Snap Align", InstancePlacementOverlay.SnapAlignNormal);
            float eraseRadius = EditorGUILayout.Slider("Erase Radius", InstancePlacementOverlay.EraseBrushRadius, 0.25f, 50f);
            if (!Mathf.Approximately(eraseRadius, InstancePlacementOverlay.EraseBrushRadius))
                InstancePlacementOverlay.EraseBrushRadius = eraseRadius;

            if (InstancePlacementOverlay.IsActiveFor(layer))
            {
                EditorGUILayout.HelpBox($"Active: {InstancePlacementOverlay.Mode} Scene mode — Esc to exit.", MessageType.None);
            }
        }

        private void DrawMiniActionButton(string label, bool isActive, System.Action onClick)
        {
            Color previousBackground = GUI.backgroundColor;
            if (isActive)
                GUI.backgroundColor = new Color(0.32f, 0.55f, 0.88f);

            if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Height(24f)))
                onClick();

            GUI.backgroundColor = previousBackground;
        }

        private void SelectLayer(ScatterLayer layer)
        {
            this.ExitConflictingSceneModes(layer);
            this.selectedLayer = layer;
        }

        private void ExitConflictingSceneModes(ScatterLayer layer)
        {
            if (DensityScenePaintSession.ActiveLayer != null && DensityScenePaintSession.ActiveLayer != layer)
                DensityScenePaintSession.Exit();

            if (InstancePlacementOverlay.ActiveLayer != null && InstancePlacementOverlay.ActiveLayer != layer)
                InstancePlacementOverlay.ExitSceneMode();
        }

        private void ExitSceneModeForLayer(ScatterLayer layer)
        {
            if (layer is DensityScatterLayer density && DensityScenePaintSession.IsActiveFor(density))
                DensityScenePaintSession.Exit();

            if (layer is InstanceScatterLayer instance && InstancePlacementOverlay.IsActiveFor(instance))
                InstancePlacementOverlay.ExitSceneMode();
        }

        private void ValidateSelection(TerrainScatterConfig config)
        {
            if (this.selectedLayer == null)
                return;

            foreach (ScatterLayer layer in config.Layers)
            {
                if (layer == this.selectedLayer)
                    return;
            }

            this.ExitSceneModeForLayer(this.selectedLayer);
            this.selectedLayer = null;
        }

        private IReadOnlyList<ScatterLayer> GetDensityLayers(TerrainScatterConfig config)
        {
            var layers = new List<ScatterLayer>();
            foreach (ScatterLayer layer in config.Layers)
            {
                if (layer is DensityScatterLayer)
                    layers.Add(layer);
            }

            return layers;
        }

        private IReadOnlyList<ScatterLayer> GetInstanceLayers(TerrainScatterConfig config)
        {
            var layers = new List<ScatterLayer>();
            foreach (ScatterLayer layer in config.Layers)
            {
                if (layer is InstanceScatterLayer)
                    layers.Add(layer);
            }

            return layers;
        }
    }
}
