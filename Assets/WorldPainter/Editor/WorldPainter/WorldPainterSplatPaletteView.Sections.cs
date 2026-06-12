#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    // Partial: Splat / Meadow / Prop section build + refresh + mutation logic.
    internal sealed partial class WorldPainterSplatPaletteView
    {
        // ── Splat section ─────────────────────────────────────────────────────

        private VisualElement BuildSplatSection()
        {
            var section = new VisualElement();
            section.AddToClassList("wp-palette-section");
            section.Add(BuildSectionHeader("SPLAT", this.TryAddSplatLayer));

            this.splatSwatchRow = new VisualElement();
            this.splatSwatchRow.AddToClassList("wp-palette-swatch-row");
            section.Add(this.splatSwatchRow);

            this.RefreshSplatSwatches();
            return section;
        }

        internal void RefreshSplatSwatches()
        {
            if (this.splatSwatchRow == null) return;

            this.serializedObject.Update();
            this.splatSwatchRow.Clear();

            for (int i = 0; i < this.splatLayersProp.arraySize; i++)
            {
                var elem     = this.splatLayersProp.GetArrayElementAtIndex(i);
                var albedoP  = elem.FindPropertyRelative("albedo");
                var nameProp = elem.FindPropertyRelative("name");

                Texture2D? albedo = albedoP?.objectReferenceValue as Texture2D;
                Texture2D? thumb  = (albedo != null)
                    ? (AssetPreview.GetAssetPreview(albedo) ?? albedo) : null;

                string layerName = nameProp?.stringValue ?? $"S{i}";
                string layerId   = layerName;
                int    captured  = i;

                bool isActive = WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Splat
                             && WorldPainterState.ActiveLayerId == layerId;

                this.splatSwatchRow.Add(this.BuildChip(
                    isActive, layerName, thumb,
                    onSelect: () =>
                    {
                        WorldPainterState.SetActiveLayer(layerId, WorldPainterState.PaintLayerKind.Splat);
                        WorldPainterState.ActiveLayerIndex = captured + 1; // legacy compat
                        this.RefreshAll();
                    },
                    onRemove: () => this.RemoveSplatLayer(captured)));
            }
        }

        private void TryAddSplatLayer()
        {
            if (this.splatLayersProp.arraySize >= WorldPainter.MAX_SPLAT_LAYERS)
            {
                Debug.LogError(
                    $"[WorldPainter] Cannot add splat layer: at most {WorldPainter.MAX_SPLAT_LAYERS} layers.");
                return;
            }
            Undo.RecordObject(this.painter, "Add Splat Layer");
            int newIdx = this.splatLayersProp.arraySize;
            this.splatLayersProp.InsertArrayElementAtIndex(newIdx);
            var nameProp = this.splatLayersProp.GetArrayElementAtIndex(newIdx)
                .FindPropertyRelative("name");
            if (nameProp != null) nameProp.stringValue = $"Splat {newIdx}";
            this.serializedObject.ApplyModifiedProperties();
            this.RefreshSplatSwatches();
        }

        private void RemoveSplatLayer(int index)
        {
            if (index < 0 || index >= this.splatLayersProp.arraySize) return;
            Undo.RecordObject(this.painter, "Remove Splat Layer");
            this.splatLayersProp.DeleteArrayElementAtIndex(index);
            this.serializedObject.ApplyModifiedProperties();
            if (WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Splat)
                WorldPainterState.SetActiveLayer(string.Empty, WorldPainterState.PaintLayerKind.None);
            WorldPainterState.ActiveLayerIndex = Mathf.Max(0, WorldPainterState.ActiveLayerIndex - 1);
            this.RefreshSplatSwatches();
        }

        // ── Meadow section ────────────────────────────────────────────────────

        private VisualElement BuildMeadowSection()
        {
            var section = new VisualElement();
            section.AddToClassList("wp-palette-section");
            section.Add(BuildSectionHeader("MEADOW", this.TryAddMeadowLayer));

            this.meadowSwatchRow = new VisualElement();
            this.meadowSwatchRow.AddToClassList("wp-palette-swatch-row");
            section.Add(this.meadowSwatchRow);

            this.RefreshMeadowSwatches();
            return section;
        }

        internal void RefreshMeadowSwatches()
        {
            if (this.meadowSwatchRow == null) return;
            this.meadowSwatchRow.Clear();

            IReadOnlyList<ScatterLayer>? mapLayers = this.painter.Map?.Layers;
            if (mapLayers == null) return;

            int chipKey = 10000;
            foreach (ScatterLayer sl in mapLayers)
            {
                if (!(sl is DensityScatterLayer)) continue;
                string layerId = sl.name;
                Mesh?  mesh    = sl.Render.LodMeshes.Length > 0 ? sl.Render.LodMeshes[0] : null;
                Texture2D? thumb = this.previewCache.GetOrRender(chipKey++, mesh, null, 40);
                bool isActive = WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Meadow
                             && WorldPainterState.ActiveLayerId == layerId;
                string captured = layerId;
                var density = (DensityScatterLayer)sl;
                this.meadowSwatchRow.Add(this.BuildChip(
                    isActive, sl.name, thumb,
                    onSelect: () =>
                    {
                        WorldPainterState.SetActiveLayer(captured, WorldPainterState.PaintLayerKind.Meadow);
                        this.RefreshAll();
                    },
                    onRemove: () => this.RemoveMeadowLayer(density)));
            }
        }

        private void TryAddMeadowLayer()
        {
            WorldMapAsset? map = this.painter.Map;
            if (map == null) { Debug.LogWarning("[WorldPainter] Assign a WorldMapAsset first."); return; }
            int n = 0;
            foreach (ScatterLayer sl in map.Layers) if (sl is DensityScatterLayer) n++;
            WorldMapAssetLifecycle.AddDensityLayer(map, $"Meadow{n}");
            this.RefreshMeadowSwatches();
        }

        private void RemoveMeadowLayer(DensityScatterLayer layer)
        {
            WorldMapAsset? map = this.painter.Map;
            if (map == null) return;
            if (WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Meadow
             && WorldPainterState.ActiveLayerId == layer.name)
                WorldPainterState.SetActiveLayer(string.Empty, WorldPainterState.PaintLayerKind.None);
            WorldMapAssetLifecycle.RemoveLayer(map, layer);
            this.RefreshMeadowSwatches();
        }

        // ── Prop section (shell — placement behavior ships in P7) ─────────────

        private VisualElement BuildPropSection()
        {
            var section = new VisualElement();
            section.AddToClassList("wp-palette-section");
            section.Add(BuildSectionHeader("PROP", this.TryAddPropLayer));

            this.propSwatchRow = new VisualElement();
            this.propSwatchRow.AddToClassList("wp-palette-swatch-row");
            section.Add(this.propSwatchRow);

            this.RefreshPropSwatches();
            return section;
        }

        internal void RefreshPropSwatches()
        {
            if (this.propSwatchRow == null) return;
            this.propSwatchRow.Clear();

            IReadOnlyList<ScatterLayer>? mapLayers = this.painter.Map?.Layers;
            if (mapLayers == null) return;

            int chipKey = 20000;
            foreach (ScatterLayer sl in mapLayers)
            {
                if (!(sl is InstanceScatterLayer)) continue;
                string layerId = sl.name;
                Mesh?  mesh    = sl.Render.Lods.Length > 0 ? sl.Render.Lods[0].mesh : null;
                Texture2D? thumb = this.previewCache.GetOrRender(chipKey++, mesh, null, 40);
                bool isActive = WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Prop
                             && WorldPainterState.ActiveLayerId == layerId;
                string captured = layerId;
                var inst = (InstanceScatterLayer)sl;
                this.propSwatchRow.Add(this.BuildChip(
                    isActive, sl.name, thumb,
                    onSelect: () =>
                    {
                        WorldPainterState.SetActiveLayer(captured, WorldPainterState.PaintLayerKind.Prop);
                        this.RefreshAll();
                    },
                    onRemove: () => this.RemovePropLayer(inst)));
            }
        }

        private void TryAddPropLayer()
        {
            WorldMapAsset? map = this.painter.Map;
            if (map == null) { Debug.LogWarning("[WorldPainter] Assign a WorldMapAsset first."); return; }
            int n = 0;
            foreach (ScatterLayer sl in map.Layers) if (sl is InstanceScatterLayer) n++;
            WorldMapAssetLifecycle.AddInstanceLayer(map, $"Prop{n}");
            this.RefreshPropSwatches();
        }

        private void RemovePropLayer(InstanceScatterLayer layer)
        {
            WorldMapAsset? map = this.painter.Map;
            if (map == null) return;
            if (WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Prop
             && WorldPainterState.ActiveLayerId == layer.name)
                WorldPainterState.SetActiveLayer(string.Empty, WorldPainterState.PaintLayerKind.None);
            WorldMapAssetLifecycle.RemoveLayer(map, layer);
            this.RefreshPropSwatches();
        }
    }
}
