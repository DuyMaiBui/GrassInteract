#nullable enable

namespace GPUGrass
{
    /// <summary>
    /// Pure-C# mirror of the 2-bit LOD tag packed into the high bits of each entry in the merged
    /// <c>visibleBladesBuf</c> (docs/GPUGrass.md §8.2 #5+#6). Packing itself happens GPU-side
    /// (<c>GrassCull.compute</c> BladeCullScatter); this class exists so the bit-math has an EditMode-testable
    /// mirror without a live GPU (see <c>Tests/LodTagPackTests.cs</c>) — mirrors the existing
    /// <see cref="GpuGrassRenderer.LodThresholds"/> pattern. MUST stay byte/bit-identical to the HLSL side:
    /// <c>GrassCull.compute</c> (pack) and <c>GpuGrassIndirect.shader</c> (unpack, "mask off the top 2 bits").
    /// </summary>
    internal static class GpuGrassLodTag
    {
        /// <summary>Bit position of the 2-bit LOD tag within the packed uint (top 2 bits of 32).</summary>
        internal const int LOD_SHIFT = 30;

        /// <summary>Mask isolating the low 30 bits — the real blade index range (TotalBlades ≪ 2^30).</summary>
        internal const uint INDEX_MASK = 0x3FFFFFFFu;

        /// <summary>Packs a 0..2 LOD tag + a &lt; 2^30 blade index into one uint: <c>(lod &lt;&lt; 30) | index</c>.</summary>
        internal static uint Pack(uint lod, uint index) => (lod << LOD_SHIFT) | (index & INDEX_MASK);

        /// <summary>Recovers the original blade index by masking off the top 2 (tag) bits.</summary>
        internal static uint UnpackIndex(uint packed) => packed & INDEX_MASK;

        /// <summary>Recovers the 2-bit LOD tag from the top bits.</summary>
        internal static uint UnpackLod(uint packed) => packed >> LOD_SHIFT;
    }
}
