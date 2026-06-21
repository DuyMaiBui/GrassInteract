#nullable enable
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace GPUGrass.Tests
{
    /// <summary>
    /// Contract guard: the C# blittable GPU structs MUST stay byte-for-byte identical to the HLSL structs
    /// in <c>GrassCull.compute</c> and <c>GpuGrassIndirect.shader</c>. A silent stride drift here is the
    /// highest-risk failure mode of the extraction (garbage blade data, wrong interactor falloff) and
    /// compiles cleanly — so it is pinned here instead of left to runtime discovery.
    /// </summary>
    public sealed class GpuStructStrideTests
    {
        [Test]
        public void BladeInstance_Is20Bytes() =>
            Assert.AreEqual(20, Marshal.SizeOf<GpuGrassBladeInstance>(),
                "BladeInstance must be float3+uint+uint = 20 B (matches HLSL BladeInstance + BLADE_STRIDE).");

        [Test]
        public void ChunkAabb_Is24Bytes() =>
            Assert.AreEqual(24, Marshal.SizeOf<GpuGrassChunkAabb>(),
                "ChunkAabb must be float3 min + float3 max = 24 B (matches HLSL ChunkAabb).");

        [Test]
        public void ChunkRange_Is8Bytes() =>
            Assert.AreEqual(8, Marshal.SizeOf<GpuGrassChunkRange>(),
                "ChunkRange must be uint start + uint count = 8 B (matches HLSL ChunkRange).");

        [Test]
        public void InteractorGpu_Is32Bytes()
        {
            Assert.AreEqual(32, Marshal.SizeOf<GpuGrassInteractorBuffer.InteractorGpu>(),
                "InteractorGpu must be 32 B (matches HLSL GrassInteractorGpu).");
            Assert.AreEqual(32, GpuGrassInteractorBuffer.STRIDE, "STRIDE constant must match the struct size.");
        }

        [Test]
        public void TrailSegmentGpu_Is48Bytes()
        {
            Assert.AreEqual(48, Marshal.SizeOf<GpuGrassTrailBuffer.TrailSegmentGpu>(),
                "TrailSegmentGpu must be 48 B (matches HLSL GrassTrailSegmentGpu).");
            Assert.AreEqual(48, GpuGrassTrailBuffer.STRIDE, "STRIDE constant must match the struct size.");
        }
    }
}
