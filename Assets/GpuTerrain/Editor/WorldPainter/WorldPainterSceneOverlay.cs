#nullable enable
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Scene-view Overlay toolbar showing:
    ///   - Active-layer chip (name + type color dot)
    ///   - LOD0 mini-thumb (reuses WorldPainterPreviewCache thumbnail)
    ///   - Brush size readout
    ///   - Mode dot (paint / erase / smooth)
    ///
    /// Design §4.5 / Phase 6 task 5.
    /// Layer switching from the overlay writes directly to
    /// <see cref="WorldPainterState.ActiveLayerIndex"/>.
    /// </summary>
    [Overlay(typeof(SceneView), "wp-scene-overlay", "WorldPainter HUD")]
    internal sealed class WorldPainterSceneOverlay : Overlay
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int TICK_MS = 150;

        private static readonly Color[] MODE_COLORS =
        {
            new Color(0.30f, 0.75f, 0.40f), // paint   → green
            new Color(0.80f, 0.30f, 0.30f), // erase   → red
            new Color(0.40f, 0.60f, 0.90f), // smooth  → blue
        };

        // ── UI ────────────────────────────────────────────────────────────────

        private Label?        layerChipLabel;
        private Image?        lodThumb;
        private Label?        sizeLabel;
        private VisualElement? modeDot;
        private VisualElement? root;

        // ── Overlay API ───────────────────────────────────────────────────────

        public override VisualElement CreatePanelContent()
        {
            this.root = new VisualElement();
            this.root.style.flexDirection = FlexDirection.Row;
            this.root.style.alignItems    = Align.Center;
            this.root.style.paddingLeft   = 4;
            this.root.style.paddingRight  = 4;
            this.root.style.height        = 28;

            // Mode dot.
            this.modeDot = new VisualElement();
            this.modeDot.style.width        = 8;
            this.modeDot.style.height       = 8;
            this.modeDot.style.borderTopLeftRadius     = 4;
            this.modeDot.style.borderTopRightRadius    = 4;
            this.modeDot.style.borderBottomLeftRadius  = 4;
            this.modeDot.style.borderBottomRightRadius = 4;
            this.modeDot.style.marginRight  = 6;
            this.modeDot.style.backgroundColor = new StyleColor(MODE_COLORS[0]);
            this.root.Add(this.modeDot);

            // LOD0 thumbnail.
            this.lodThumb = new Image();
            this.lodThumb.style.width  = 20;
            this.lodThumb.style.height = 20;
            this.lodThumb.style.marginRight = 4;
            this.lodThumb.style.borderTopLeftRadius     = 2;
            this.lodThumb.style.borderTopRightRadius    = 2;
            this.lodThumb.style.borderBottomLeftRadius  = 2;
            this.lodThumb.style.borderBottomRightRadius = 2;
            this.root.Add(this.lodThumb);

            // Active layer chip.
            this.layerChipLabel = new Label("—");
            this.layerChipLabel.style.marginRight = 8;
            this.layerChipLabel.style.fontSize = 11;
            this.root.Add(this.layerChipLabel);

            // Brush size readout.
            this.sizeLabel = new Label("r—");
            this.sizeLabel.style.fontSize = 11;
            this.root.Add(this.sizeLabel);

            this.root.schedule.Execute(this.Tick).Every(TICK_MS);

            return this.root;
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        private void Tick()
        {
            var painter = WorldPainterState.ActivePainter;
            if (painter == null)
            {
                if (this.layerChipLabel != null) this.layerChipLabel.text = "No Painter";
                return;
            }

            // Layer name.
            string layerName = this.ResolveLayerName(painter);
            if (this.layerChipLabel != null)
                this.layerChipLabel.text = layerName;

            // Mode dot color based on layer type.
            LayerType lt = WorldPainterState.ActiveLayerType(painter, out _);
            int modeIdx = lt switch
            {
                LayerType.Splat  => 0,
                LayerType.Grass  => 0,
                LayerType.Props  => 0,
                LayerType.Biome  => 2,
                _                => 0,
            };
            if (this.modeDot != null)
                this.modeDot.style.backgroundColor = new StyleColor(MODE_COLORS[modeIdx]);

            // Brush size.
            float size = WorldPainterState.Brush.size;
            if (this.sizeLabel != null)
                this.sizeLabel.text = $"r{size:F0}m";
        }

        private string ResolveLayerName(WorldPainter painter)
        {
            int idx = WorldPainterState.ActiveLayerIndex;
            if (idx < 0) return "No Layer";
            if (idx == 0) return "Height";

            int splatCount = painter.SplatLayers.Count;
            if (idx <= splatCount)
            {
                var def = painter.SplatLayers[idx - 1];
                return string.IsNullOrEmpty(def.name) ? $"Splat {idx}" : def.name;
            }

            int scatterOff = idx - splatCount - 1;
            if (scatterOff >= 0 && scatterOff < painter.ScatterLayers.Count)
            {
                var layer = painter.ScatterLayers[scatterOff];
                return layer != null ? layer.name : $"Scatter {scatterOff}";
            }

            int biomeOff = idx - splatCount - painter.ScatterLayers.Count - 1;
            if (biomeOff >= 0 && biomeOff < painter.Biomes.Count)
            {
                var biome = painter.Biomes[biomeOff];
                return biome != null ? biome.name : $"Biome {biomeOff}";
            }

            return $"Layer {idx}";
        }
    }
}
