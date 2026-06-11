#nullable enable
using GrassInteract;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Density RT lifecycle half of <see cref="WorldPainterSculptTool"/> (partial).
    ///
    /// Manages the per-scatter-layer <see cref="RenderTexture"/> used by the
    /// <c>PaintDensity</c> compute kernel. Allocated lazily on first Grass-layer
    /// stamp; seeded from the committed <see cref="GrassInteract.DensityScatterLayer.DensityMap"/>;
    /// released on <see cref="TeardownActiveStroke"/>.
    ///
    /// Density writeback is driven by <see cref="WorldPainterDensityEncoder"/> on the
    /// same 0.15s async pipeline + mouse-up sync flush as <see cref="TerrainSculptRtWriteback"/>.
    /// </summary>
    internal sealed partial class WorldPainterSculptTool
    {
        // ── Density RT management ─────────────────────────────────────────────

        internal RenderTexture? GetOrCreateDensityRT(GrassInteract.DensityScatterLayer layer)
        {
            // Reuse existing RT if the same layer is still active.
            if (this.densityRT != null && ReferenceEquals(this.activeDensityLayer, layer))
                return this.densityRT;

            // New layer or first touch — release old RT and allocate fresh.
            this.ReleaseDensityRT();

            int res = TerrainSculptConfig.BRUSH_RT_RES;
            var rt = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat)
            {
                name = $"WorldPainterDensityRT_{layer.name}",
                enableRandomWrite = true,
            };
            rt.Create();

            // Seed from the committed density map so edits continue from the saved state.
            if (layer.DensityMap != null)
                Graphics.Blit(layer.DensityMap, rt);

            this.densityRT          = rt;
            this.activeDensityLayer = layer;
            return rt;
        }

        internal void ReleaseDensityRT()
        {
            if (this.densityRT != null)
            {
                if (RenderTexture.active == this.densityRT) RenderTexture.active = null;
                this.densityRT.Release();
                Object.DestroyImmediate(this.densityRT);
                this.densityRT = null;
            }
            this.activeDensityLayer = null;
        }
    }
}
