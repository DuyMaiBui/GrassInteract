#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Mutation half of <see cref="WorldPainterLayerStackView"/> (partial).
    ///
    /// Phase 5b: drag-reorder legacy methods removed (depended on splatLayersProp).
    /// Contains: add-menu, unified add/remove mutations, biome row mutations.
    /// </summary>
    internal sealed partial class WorldPainterLayerStackView
    {
        // ── Add menu ──────────────────────────────────────────────────────────

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Splat layer"), false, () => this.AddSplatLayerUnified());
            menu.AddItem(new GUIContent("Grass layer"), false, () => this.AddGrassLayerUnified());
            menu.AddItem(new GUIContent("Props layer"), false, () => this.AddPropLayerUnified());
            menu.AddItem(new GUIContent("Biome preset"), false, () => this.AddBiomeRow());
            menu.ShowAsContext();
        }

        // ── Unified add mutations (Phase 2 — all go through WorldMapAssetLifecycle) ──

        /// <summary>
        /// Adds a unified <see cref="SplatLayer"/> via <see cref="WorldMapAssetLifecycle.AddSplatLayer"/>.
        /// Requires a saved WorldMapAsset.
        /// </summary>
        private void AddSplatLayerUnified()
        {
            WorldMapAsset? map = this.painter.Map;
            if (!this.RequireSavedMap(map, "splat")) return;

            // Count existing splat layers in the unified system for a smart default name.
            int existingSplatCount = 0;
            foreach (var sl in map!.SurfaceLayers)
                if (sl is SplatLayer) existingSplatCount++;

            string layerName = existingSplatCount == 0 ? "Ground" : $"Splat {existingSplatCount}";
            SplatLayer newLayer = WorldMapAssetLifecycle.AddSplatLayer(map!, layerName);
            this.SelectSurfaceLayer(newLayer);
            this.RefreshStack();
        }

        /// <summary>
        /// Adds a unified <see cref="GrassLayer"/> with blade mesh + 2 variants via
        /// <see cref="WorldMapAssetLifecycle.AddGrassLayerWithBlades"/>.
        /// Requires a saved WorldMapAsset.
        /// </summary>
        private void AddGrassLayerUnified()
        {
            WorldMapAsset? map = this.painter.Map;
            if (!this.RequireSavedMap(map, "grass")) return;

            int existingGrassCount = 0;
            foreach (var sl in map!.SurfaceLayers)
                if (sl is GrassLayer) existingGrassCount++;

            string layerName = existingGrassCount == 0 ? "Grass" : $"Grass {existingGrassCount}";
            GrassLayer newLayer = WorldMapAssetLifecycle.AddGrassLayerWithBlades(map!, layerName);
            this.SelectSurfaceLayer(newLayer);
            this.RefreshStack();
        }

        /// <summary>
        /// Adds a unified <see cref="PropLayer"/> via <see cref="WorldMapAssetLifecycle.AddPropLayer"/>.
        /// Requires a saved WorldMapAsset.
        /// </summary>
        private void AddPropLayerUnified()
        {
            WorldMapAsset? map = this.painter.Map;
            if (!this.RequireSavedMap(map, "props")) return;

            int existingPropCount = 0;
            foreach (var sl in map!.SurfaceLayers)
                if (sl is PropLayer) existingPropCount++;

            string layerName = existingPropCount == 0 ? "Props" : $"Props {existingPropCount}";
            PropLayer newLayer = WorldMapAssetLifecycle.AddPropLayer(map!, layerName);
            this.SelectSurfaceLayer(newLayer);
            this.RefreshStack();
        }

        /// <summary>
        /// Removes the unified surface layer at <paramref name="surfaceIndex"/> in
        /// <c>map.SurfaceLayers</c>. Confirms before destroying sub-assets.
        /// </summary>
        private void RemoveSurfaceLayerAt(int surfaceIndex)
        {
            WorldMapAsset? map = this.painter.Map;
            if (map == null) return;

            var layers = map.SurfaceLayers;
            if (surfaceIndex < 0 || surfaceIndex >= layers.Count) return;

            WorldPainterLayer layer = layers[surfaceIndex];
            string layerName = layer.DisplayName;

            if (!EditorUtility.DisplayDialog(
                    "Remove Surface Layer",
                    $"Remove '{layerName}'?\n\nThis permanently deletes the layer and its " +
                    "sub-assets from the World Map. This cannot be undone.",
                    "Remove", "Cancel"))
                return;

            // Clear active selection if the removed layer was active.
            if (WorldPainterState.ActiveLayerId == layer.name)
            {
                WorldPainterState.SetActiveLayer(string.Empty, WorldPainterState.PaintLayerKind.None);
            }

            WorldMapAssetLifecycle.RemoveSurfaceLayer(map, layer);
            this.RefreshStack();
        }

        // ── Biome row mutation (unchanged from Phase 1) ───────────────────────

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

        // ── Guard helper ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns false and logs an error if <paramref name="map"/> is null or unsaved.
        /// </summary>
        private bool RequireSavedMap(WorldMapAsset? map, string layerKind)
        {
            if (map == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(map)))
            {
                Debug.LogError(
                    $"[WorldPainter] Cannot add a {layerKind} layer: assign and save a World Map first " +
                    "(use the 'Create World Map' button). Surface layers are stored as sub-assets " +
                    "of the map.");
                return false;
            }
            return true;
        }

        // Row helpers (SelectLayer, SelectSurfaceLayer, CreateBaseRow, MakeEyeToggle, MakeLockToggle,
        // LayerIcon, AddBiomeRow) are in WorldPainterLayerStackView.RowHelpers.cs
    }
}
