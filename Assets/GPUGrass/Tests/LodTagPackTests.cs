#nullable enable
using NUnit.Framework;

namespace GPUGrass.Tests
{
    /// <summary>
    /// EditMode round-trip tests for <see cref="GpuGrassLodTag"/> — the 2-bit LOD tag packed into the high
    /// bits of each merged-buffer entry (docs/GPUGrass.md §8.2 #5+#6). Pure C# mirror of the HLSL pack
    /// (<c>GrassCull.compute</c> BladeCullScatter) / unpack (<c>GpuGrassIndirect.shader</c> vertex stage).
    /// Guard 3 (blade-count invariant) depends on this bit-math never colliding or truncating an index.
    /// </summary>
    public sealed class LodTagPackTests
    {
        [TestCase(0u, 0u)]
        [TestCase(0u, 1u)]
        [TestCase(1u, 0u)]
        [TestCase(2u, 0u)]
        [TestCase(0u, 123456u)]
        [TestCase(1u, 123456u)]
        [TestCase(2u, 123456u)]
        [TestCase(0u, 0x3FFFFFFFu)] // max representable index (30 bits)
        [TestCase(1u, 0x3FFFFFFFu)]
        [TestCase(2u, 0x3FFFFFFFu)]
        public void PackThenUnpack_RoundTrips(uint lod, uint index)
        {
            uint packed = GpuGrassLodTag.Pack(lod, index);

            Assert.AreEqual(index, GpuGrassLodTag.UnpackIndex(packed), "Index must round-trip exactly.");
            Assert.AreEqual(lod, GpuGrassLodTag.UnpackLod(packed), "LOD tag must round-trip exactly.");
        }

        [Test]
        public void Pack_DoesNotCollideAcrossLods_ForSameIndex()
        {
            const uint index = 42u;
            uint p0 = GpuGrassLodTag.Pack(0u, index);
            uint p1 = GpuGrassLodTag.Pack(1u, index);
            uint p2 = GpuGrassLodTag.Pack(2u, index);

            Assert.AreNotEqual(p0, p1);
            Assert.AreNotEqual(p1, p2);
            Assert.AreNotEqual(p0, p2);
        }

        [Test]
        public void UnpackIndex_MasksOffTopTwoBitsOnly()
        {
            // A raw packed value with lod=3 (invalid tag, but masking must still recover the low 30 bits) —
            // guards that UnpackIndex is a pure mask, never a lod-dependent branch.
            uint raw = (3u << GpuGrassLodTag.LOD_SHIFT) | 777u;
            Assert.AreEqual(777u, GpuGrassLodTag.UnpackIndex(raw));
        }
    }
}
