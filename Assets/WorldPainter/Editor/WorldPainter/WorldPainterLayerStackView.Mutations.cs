#nullable enable
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Mutation half of <see cref="WorldPainterLayerStackView"/> (partial).
    ///
    /// Contains: unified grass/prop add mutations, surface-layer removal, and the per-layer
    /// enable/disable toggle. Layers are created via explicit "+ Grass" / "+ Props" header
    /// buttons (the old GenericMenu add-flow and the biome subsystem were removed).
    /// </summary>
    internal sealed partial class WorldPainterLayerStackView
    {
        // ── Unified add mutations (all go through WorldMapAssetLifecycle) ──────

        /// <summary>
        /// Adds an empty unified <see cref="GrassLayer"/> — material + per-tile empty density
        /// textures only, NO blade mesh. The user assigns LODs via the inline LOD editor shown in
        /// the inspector detail card when the layer is selected, before painting.
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
            GrassLayer newLayer = WorldMapAssetLifecycle.AddGrassLayer(map!, layerName);
            this.SelectSurfaceLayer(newLayer);
            this.RefreshStack();
            // Selecting the new layer surfaces its inline LOD editor in the inspector detail card.
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
            // Selecting the new layer surfaces its inline LOD editor in the inspector detail card.
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

        // ── Per-layer enable / disable (eye toggle) ───────────────────────────

        /// <summary>
        /// Sets the layer's <see cref="WorldPainterLayer.Enabled"/> flag and rebuilds just that
        /// layer's engines (coalesced) so a hidden layer disappears / a shown layer reappears
        /// immediately. Undo-recorded.
        /// </summary>
        private void SetLayerEnabled(WorldPainterLayer layer, bool enabled)
        {
            if (layer == null || layer.Enabled == enabled) return;

            Undo.RecordObject(layer, enabled ? "Show Layer" : "Hide Layer");
            layer.Enabled = enabled;
            EditorUtility.SetDirty(layer);

            if (layer is GrassLayer grass)     WorldPainterRebuildScheduler.MarkGrassDirty(grass);
            else if (layer is PropLayer prop)  WorldPainterRebuildScheduler.MarkPropDirty(prop);

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

        // Selection + toggle factories are in WorldPainterLayerStackView.RowHelpers.cs.
    }
}
