#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Live readout strip shown below the layer stack.
    /// Displays height histogram bucket, density estimate, and instance counts.
    /// Values are refreshed on the 0.15s async tick — NEVER per-frame CPU recount.
    /// Design §6.
    /// </summary>
    internal sealed class WorldPainterLiveReadoutStrip
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int TICK_MS = 150;

        // ── State ─────────────────────────────────────────────────────────────

        private Label? heightLabel;
        private Label? densityLabel;
        private Label? instanceLabel;

        // Cached values updated on the async tick.
        private float cachedMinHeight;
        private float cachedMaxHeight;
        private float cachedDensityPct;
        private int   cachedInstanceCount;

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Builds and returns the readout strip VisualElement.</summary>
        public VisualElement Build(WorldPainter painter)
        {
            var strip = new VisualElement();
            strip.AddToClassList("wp-readout-strip");

            strip.Add(this.BuildCell("HEIGHT", ref this.heightLabel, "—"));
            strip.Add(this.BuildCell("DENSITY", ref this.densityLabel, "—"));
            strip.Add(this.BuildCell("INSTANCES", ref this.instanceLabel, "—"));

            // 0.15s async tick — never per-frame.
            strip.schedule.Execute(() => this.Tick(painter)).Every(TICK_MS);

            return strip;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private VisualElement BuildCell(string key, ref Label? labelRef, string defaultVal)
        {
            var cell = new VisualElement();
            cell.AddToClassList("wp-readout-cell");

            var keyLabel = new Label(key);
            keyLabel.AddToClassList("wp-readout-label");
            cell.Add(keyLabel);

            var valLabel = new Label(defaultVal);
            valLabel.AddToClassList("wp-readout-value");
            cell.Add(valLabel);

            labelRef = valLabel;
            return cell;
        }

        private void Tick(WorldPainter painter)
        {
            this.RefreshFromPainter(painter);
            this.PushToLabels();
        }

        private void RefreshFromPainter(WorldPainter painter)
        {
            // Height range from tile assets (CPU-safe: reads from asset data, not GPU).
            var tiles = painter.Tiles;
            if (tiles != null && tiles.Count > 0)
            {
                float minH = float.MaxValue;
                float maxH = float.MinValue;
                foreach (var entry in tiles)
                {
                    var tile = entry.tileAsset;
                    if (tile == null) continue;
                    if (tile.minHeight < minH) minH = tile.minHeight;
                    if (tile.maxHeight > maxH) maxH = tile.maxHeight;
                }
                this.cachedMinHeight = minH == float.MaxValue ? 0f : minH;
                this.cachedMaxHeight = maxH == float.MinValue ? 0f : maxH;
            }
            else
            {
                this.cachedMinHeight = 0f;
                this.cachedMaxHeight = 0f;
            }

            // Density: fraction of scatter layers that have non-null refs.
            int totalLayers   = painter.ScatterLayers?.Count ?? 0;
            int activeLayers  = 0;
            if (painter.ScatterLayers != null)
            {
                foreach (var sl in painter.ScatterLayers)
                    if (sl != null) activeLayers++;
            }
            this.cachedDensityPct = totalLayers > 0
                ? (activeLayers / (float)totalLayers) * 100f
                : 0f;

            // Instance count: scatter layers count.
            this.cachedInstanceCount = totalLayers;
        }

        private void PushToLabels()
        {
            if (this.heightLabel != null)
                this.heightLabel.text =
                    $"{this.cachedMinHeight:F0}m – {this.cachedMaxHeight:F0}m";

            if (this.densityLabel != null)
                this.densityLabel.text = $"{this.cachedDensityPct:F0}%";

            if (this.instanceLabel != null)
                this.instanceLabel.text = this.cachedInstanceCount.ToString();
        }
    }
}
