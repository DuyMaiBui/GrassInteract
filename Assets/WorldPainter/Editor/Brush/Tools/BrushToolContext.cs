#nullable enable
using WorldPainter;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Per-stamp payload handed to an <see cref="IBrushTool.Apply"/>. Carries the GPU/CPU
    /// handles a tool needs plus a back-reference to the owning <see cref="WorldPainterSculptTool"/>
    /// for shared resources (falloff LUT, density RT lifecycle, prop emitter, encoder).
    ///
    /// The common brush uniforms (<c>_BrushCenterUV</c>, <c>_BrushRadiusUV</c>, <c>_Strength</c>,
    /// <c>_RTRes</c>, <c>_BrushShape</c>) are already set on <see cref="Compute"/> by
    /// <c>BindAndDispatch</c> before the tool runs; tools only set their kernel-specific params.
    /// </summary>
    internal readonly struct BrushToolContext
    {
        /// <summary>Owning sculpt tool — exposes shared resources (LUT, density RT, emitters).</summary>
        public readonly WorldPainterSculptTool Tool;

        /// <summary>The active painter (layer lists, tile lookups).</summary>
        public readonly WorldPainter Painter;

        /// <summary>Brush world-space stamp centre.</summary>
        public readonly Vector3 WorldPos;

        /// <summary>The tile being stamped (for per-tile normalization, e.g. flatten target).</summary>
        public readonly TerrainTileAsset Tile;

        /// <summary>Per-tile working height RenderTexture (RFloat, random-write).</summary>
        public readonly RenderTexture HeightRT;

        /// <summary>Per-tile working splat RenderTexture (RGBA, random-write).</summary>
        public readonly RenderTexture SplatRT;

        /// <summary>The brush compute shader (kernels already share the common uniforms).</summary>
        public readonly ComputeShader Compute;

        /// <summary>Dispatch thread-group count per edge (ceil(RTRes / threadGroupSize)).</summary>
        public readonly int Groups;

        public BrushToolContext(
            WorldPainterSculptTool tool,
            WorldPainter           painter,
            Vector3                worldPos,
            TerrainTileAsset       tile,
            RenderTexture          heightRT,
            RenderTexture          splatRT,
            ComputeShader          compute,
            int                    groups)
        {
            this.Tool     = tool;
            this.Painter  = painter;
            this.WorldPos = worldPos;
            this.Tile     = tile;
            this.HeightRT = heightRT;
            this.SplatRT  = splatRT;
            this.Compute  = compute;
            this.Groups   = groups;
        }
    }
}
