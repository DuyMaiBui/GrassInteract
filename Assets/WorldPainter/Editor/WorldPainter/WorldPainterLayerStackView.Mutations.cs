#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Mutation half of <see cref="WorldPainterLayerStackView"/> (partial).
    ///
    /// Contains: add-menu, drag-reorder implementation, add/remove/reorder mutations
    /// (all wrapped in <c>Undo.RecordObject</c>).  The add-layer path enforces the
    /// 4-layer RGBA32 hard cap with a surfaced <see cref="Debug.LogError"/> message
    /// (errors-over-fallbacks rule).
    /// </summary>
    internal sealed partial class WorldPainterLayerStackView
    {
        // ── Add menu ──────────────────────────────────────────────────────────

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Splat layer"), false, () => this.AddSplatLayer());
            menu.AddItem(new GUIContent("Grass layer"), false, () => this.AddGrassLayer());
            menu.AddItem(new GUIContent("Props layer"), false, () => this.AddPropLayer());
            menu.AddItem(new GUIContent("Biome preset"), false, () => this.AddBiomeRow());
            menu.ShowAsContext();
        }

        // ── Drag-reorder implementation ───────────────────────────────────────

        private void RegisterDragReorder(VisualElement row, int splatIndex)
        {
            row.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button == 0 && e.ctrlKey)
                {
                    this.dragFromIndex = splatIndex;
                    e.StopPropagation();
                }
            });

            row.RegisterCallback<MouseUpEvent>(e =>
            {
                if (e.button == 0 && this.dragFromIndex >= 0 && this.dragFromIndex != splatIndex)
                    this.ReorderSplatLayer(this.dragFromIndex, splatIndex);
                this.dragFromIndex = -1;
            });
        }

        private void ReorderSplatLayer(int fromIdx, int toIdx)
        {
            if (fromIdx < 0 || toIdx < 0) return;
            if (fromIdx >= this.splatLayersProp.arraySize) return;
            if (toIdx >= this.splatLayersProp.arraySize) return;

            Undo.RecordObject(this.painter, "Reorder Splat Layer");
            this.splatLayersProp.MoveArrayElement(fromIdx, toIdx);
            this.serializedObject.ApplyModifiedProperties();
            this.RefreshStack();
        }

        // ── Mutations (persisted via SerializedObject) ────────────────────────

        private void AddSplatLayer()
        {
            // Hard cap: 4 layers max — surfaced error, not silent drop.
            if (this.splatLayersProp.arraySize >= WorldPainter.MAX_SPLAT_LAYERS)
            {
                Debug.LogError(
                    $"[WorldPainter] Cannot add splat layer: RGBA32 splat RT supports " +
                    $"at most {WorldPainter.MAX_SPLAT_LAYERS} layers. Remove one first.");
                return;
            }

            Undo.RecordObject(this.painter, "Add Splat Layer");

            int newIdx = this.splatLayersProp.arraySize;
            this.splatLayersProp.InsertArrayElementAtIndex(newIdx);

            var elem     = this.splatLayersProp.GetArrayElementAtIndex(newIdx);
            var nameProp = elem.FindPropertyRelative("name");
            if (nameProp != null)
                nameProp.stringValue = $"Splat {newIdx}";

            this.serializedObject.ApplyModifiedProperties();
            WorldPainterState.ActiveLayerIndex = 0;
            this.RefreshStack();
        }

        private void RemoveSplatLayer(int index)
        {
            if (index < 0 || index >= this.splatLayersProp.arraySize) return;

            Undo.RecordObject(this.painter, "Remove Splat Layer");
            this.splatLayersProp.DeleteArrayElementAtIndex(index);
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveLayerIndex =
                Mathf.Max(0, WorldPainterState.ActiveLayerIndex - 1);
            this.RefreshStack();
        }

        private void AddGrassLayer()
        {
            // Grass layers live as sub-assets of the WorldMapAsset (SSOT) — the scatter engine
            // builds from WorldMapAsset.Layers (WorldPainter.Scatter.RebuildScatter). Require a
            // saved map; a loose .asset would never reach the engine (errors-over-fallbacks).
            WorldMapAsset? map = this.painter.Map;
            if (map == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(map)))
            {
                Debug.LogError(
                    "[WorldPainter] Cannot add a grass layer: assign and save a World Map first " +
                    "(use the 'Create World Map' button). Scatter layers are stored as sub-assets " +
                    "of the map.");
                return;
            }

            // Create the layer as a sub-asset of the map (+ allocate per-tile density channels).
            string baseName = $"Grass {this.scatterLayersProp.arraySize}";
            DensityScatterLayer layer = WorldMapAssetLifecycle.AddDensityLayer(map, baseName);

            // Reference it from the painter's scatter list so the stack + detail card resolve it.
            Undo.RecordObject(this.painter, "Add Grass Layer");

            int newIdx = this.scatterLayersProp.arraySize;
            this.scatterLayersProp.InsertArrayElementAtIndex(newIdx);
            this.scatterLayersProp.GetArrayElementAtIndex(newIdx).objectReferenceValue = layer;
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveLayerIndex = 1 + this.painter.SplatLayers.Count + newIdx;
            this.RefreshStack();
        }

        private void RemoveScatterLayer(int index)
        {
            if (index < 0 || index >= this.scatterLayersProp.arraySize) return;

            var layer = this.scatterLayersProp.GetArrayElementAtIndex(index)
                            .objectReferenceValue as ScatterLayer;

            // Confirm before deleting — removal destroys the layer's sub-assets (def + density
            // map + material) from the World Map and is NOT undoable.
            string layerName = layer != null ? layer.name : $"layer {index}";
            if (!EditorUtility.DisplayDialog(
                    "Remove Scatter Layer",
                    $"Remove '{layerName}'?\n\nThis permanently deletes the layer and its " +
                    "density-map and material sub-assets from the World Map. This cannot be undone.",
                    "Remove", "Cancel"))
                return;

            // If this layer is a sub-asset of the map, remove it there too (def + density
            // texture + per-tile channels) — the scatter engine builds from map.Layers, so a
            // map-only leftover would keep rendering after the stack row is gone.
            WorldMapAsset? map = this.painter.Map;
            if (layer != null && map != null &&
                !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(map)) &&
                AssetDatabase.GetAssetPath(layer) == AssetDatabase.GetAssetPath(map))
            {
                WorldMapAssetLifecycle.RemoveLayer(map, layer);
            }

            Undo.RecordObject(this.painter, "Remove Scatter Layer");
            this.scatterLayersProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
            this.scatterLayersProp.DeleteArrayElementAtIndex(index);
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveLayerIndex =
                Mathf.Max(0, WorldPainterState.ActiveLayerIndex - 1);
            this.RefreshStack();
        }

        private void AddPropLayer()
        {
            // Create a new InstanceScatterLayer sub-asset with smart defaults.
            var layer = ScriptableObject.CreateInstance<InstanceScatterLayer>();
            layer.name = $"Props {this.scatterLayersProp.arraySize}";

            // Create a companion AuthoredInstancesData sub-asset.
            var authoredData = ScriptableObject.CreateInstance<AuthoredInstancesData>();
            authoredData.name = $"{layer.name}_Authored";

            string scenePath = this.painter.gameObject.scene.path;
            if (!string.IsNullOrEmpty(scenePath))
            {
                string dir       = System.IO.Path.GetDirectoryName(scenePath)!;
                string layerPath = System.IO.Path.Combine(dir, $"{layer.name}.asset").Replace('\\', '/');
                string dataPath  = System.IO.Path.Combine(dir, $"{authoredData.name}.asset").Replace('\\', '/');
                AssetDatabase.CreateAsset(layer, layerPath);
                AssetDatabase.CreateAsset(authoredData, dataPath);

                // Wire the authored data into the layer via SerializedObject.
                using var layerSo = new SerializedObject(layer);
                var authoredProp  = layerSo.FindProperty("authoredInstances");
                if (authoredProp != null)
                {
                    authoredProp.objectReferenceValue = authoredData;
                    layerSo.ApplyModifiedPropertiesWithoutUndo();
                }

                AssetDatabase.SaveAssets();
            }

            Undo.RecordObject(this.painter, "Add Prop Layer");
            int newIdx = this.scatterLayersProp.arraySize;
            this.scatterLayersProp.InsertArrayElementAtIndex(newIdx);
            this.scatterLayersProp.GetArrayElementAtIndex(newIdx).objectReferenceValue = layer;
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveLayerIndex = 1 + this.painter.SplatLayers.Count + newIdx;
            this.RefreshStack();
        }

        private void RemoveBiomeLayer(int index)
        {
            if (this.biomesProp == null) return;
            if (index < 0 || index >= this.biomesProp.arraySize) return;

            Undo.RecordObject(this.painter, "Remove Biome Preset");
            this.biomesProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
            this.biomesProp.DeleteArrayElementAtIndex(index);
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveLayerIndex =
                Mathf.Max(0, WorldPainterState.ActiveLayerIndex - 1);
            this.RefreshStack();
        }

        // Row helpers (SelectLayer, CreateBaseRow, MakeEyeToggle, MakeLockToggle,
        // LayerIcon, AddBiomeRow) are in WorldPainterLayerStackView.RowHelpers.cs
    }
}
