#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Perf badge displaying draw calls, dispatches, instance count, and VRAM.
    /// Values are updated on the 0.15s async tick — NEVER per-repaint.
    /// Reads Unity's rendering statistics via <see cref="UnityStats"/>.
    /// Design §6 / Phase 6 task 4.
    /// </summary>
    internal sealed class WorldPainterPerfBadge
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int TICK_MS = 150;

        // ── Labels ────────────────────────────────────────────────────────────

        private Label? drawCallsLabel;
        private Label? dispatchesLabel;
        private Label? instancesLabel;
        private Label? vramLabel;

        // Cached values updated on the async tick.
        private int   cachedDrawCalls;
        private int   cachedDispatches;
        private int   cachedInstances;
        private long  cachedVramBytes;

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Builds and returns the perf badge VisualElement.</summary>
        public VisualElement Build(WorldPainter painter)
        {
            var badge = new VisualElement();
            badge.AddToClassList("wp-perf-badge");

            badge.Add(this.BuildItem("DC", ref this.drawCallsLabel));
            badge.Add(this.BuildItem("SPC", ref this.dispatchesLabel));
            badge.Add(this.BuildItem("Inst", ref this.instancesLabel));
            badge.Add(this.BuildItem("VRAM", ref this.vramLabel));

            badge.schedule.Execute(() => this.Tick(painter)).Every(TICK_MS);

            return badge;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private VisualElement BuildItem(string key, ref Label? labelRef)
        {
            var item = new VisualElement();
            item.AddToClassList("wp-perf-item");

            var keyLbl = new Label(key + ":");
            keyLbl.AddToClassList("wp-perf-key");
            item.Add(keyLbl);

            var valLbl = new Label("—");
            valLbl.AddToClassList("wp-perf-val");
            item.Add(valLbl);

            labelRef = valLbl;
            return item;
        }

        private void Tick(WorldPainter painter)
        {
            this.SampleStats(painter);
            this.PushToLabels();
        }

        private void SampleStats(WorldPainter painter)
        {
            // Draw calls from UnityEditor.UnityStats (editor-only).
            this.cachedDrawCalls  = UnityStats.drawCalls;
            // Set-pass calls serve as a proxy for compute dispatches (no direct API).
            this.cachedDispatches = UnityStats.setPassCalls;
            this.cachedInstances  = this.CountInstances(painter);

            // VRAM: Profiler.GetAllocatedMemoryForGraphicsDriver is the most reliable
            // cross-platform GPU memory estimate in the Editor.
            this.cachedVramBytes = Profiler.GetAllocatedMemoryForGraphicsDriver();
        }

        private int CountInstances(WorldPainter painter)
        {
            int count = 0;
            if (painter.ScatterLayers != null)
            {
                foreach (var layer in painter.ScatterLayers)
                {
                    if (layer is InstanceScatterLayer il)
                        count += il.AuthoredInstances?.Count ?? 0;
                }
            }
            return count;
        }

        private void PushToLabels()
        {
            if (this.drawCallsLabel  != null) this.drawCallsLabel.text  = this.cachedDrawCalls.ToString();
            if (this.dispatchesLabel != null) this.dispatchesLabel.text = this.cachedDispatches.ToString();
            if (this.instancesLabel  != null) this.instancesLabel.text  = this.cachedInstances.ToString();

            if (this.vramLabel != null)
            {
                float mb = this.cachedVramBytes / (1024f * 1024f);
                this.vramLabel.text = mb >= 1024f
                    ? $"{mb / 1024f:F1}GB"
                    : $"{mb:F0}MB";
            }
        }
    }
}
