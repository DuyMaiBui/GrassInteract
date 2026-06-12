#nullable enable
using System;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Owns the lifetime of the runtime <see cref="Texture2DArray"/> built from a
    /// <see cref="TerrainLayerSet"/>'s albedo palette. ONE per map — the array is map-level and
    /// shared across every per-tile <see cref="GpuTerrainEngine"/> (building it per tile would
    /// allocate + leak N copies).
    ///
    /// <see cref="Build"/> is idempotent: it disposes any prior array before rebuilding, so it is
    /// safe to call on every <c>TryBuild</c>. <see cref="Dispose"/> releases the array.
    /// </summary>
    public sealed class TerrainLayerSetBinder : IDisposable
    {
        private Texture2DArray? array;

        /// <summary>The built albedo array, or null when no set/palette is assigned.</summary>
        public Texture2DArray? Array => this.array;

        /// <summary>UV tiling from the source set (falls back to the shading default).</summary>
        public float Tiling { get; private set; } = TerrainShadingConfig.DEFAULT_LAYER_TILING;

        /// <summary>
        /// (Re)builds the array from <paramref name="set"/>. A null set (or empty palette)
        /// releases the array and resets tiling to the default — callers then bind a null array,
        /// leaving the material's existing albedo binding untouched.
        /// </summary>
        public void Build(TerrainLayerSet? set)
        {
            this.Dispose();
            if (set == null) return;
            this.array  = set.BuildArray();
            this.Tiling = set.LayerTiling;
        }

        public void Dispose()
        {
            if (this.array == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(this.array);
            else UnityEngine.Object.DestroyImmediate(this.array);
            this.array = null;
        }
    }
}
