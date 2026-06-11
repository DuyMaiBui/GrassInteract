#nullable enable
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for <see cref="TerrainLayerSet.BuildArray"/>.
    ///
    /// Covers:
    ///   - Exact slice count = min(N, MAX_SPLAT_LAYERS).
    ///   - More than MAX_SPLAT_LAYERS layers truncates to MAX_SPLAT_LAYERS.
    ///   - No layers → BuildArray returns null.
    ///   - Output format is mobile-compatible (RGBA32 from uncompressed sources).
    /// </summary>
    [TestFixture]
    public sealed class TerrainLayerSetTests
    {
        private const int MAX = TerrainShadingConfig.MAX_SPLAT_LAYERS;

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Texture2D MakeTex(int size = 4, TextureFormat fmt = TextureFormat.RGBA32)
        {
            var tex = new Texture2D(size, size, fmt, false);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static TerrainLayerSet MakeLayerSet(int layerCount)
        {
            var asset = ScriptableObject.CreateInstance<TerrainLayerSet>();
            // Assign via reflection since field is private serialized.
            var field = typeof(TerrainLayerSet)
                .GetField("layerAlbedos",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "layerAlbedos field must exist on TerrainLayerSet.");

            var textures = new Texture2D[layerCount];
            for (int i = 0; i < layerCount; i++)
                textures[i] = MakeTex();
            field!.SetValue(asset, textures);
            return asset;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void BuildArray_NoLayers_ReturnsNull()
        {
            var asset = MakeLayerSet(0);
            var array = asset.BuildArray();
            Assert.IsNull(array, "BuildArray with 0 layers must return null.");
        }

        [Test]
        public void BuildArray_OneLayers_SliceCountIsOne()
        {
            var asset = MakeLayerSet(1);
            var array = asset.BuildArray();
            Assert.IsNotNull(array);
            Assert.AreEqual(1, array!.depth, "Slice count must equal layer count (1).");
        }

        [Test]
        public void BuildArray_ExactlyMaxLayers_SliceCountIsMax()
        {
            var asset = MakeLayerSet(MAX);
            var array = asset.BuildArray();
            Assert.IsNotNull(array);
            Assert.AreEqual(MAX, array!.depth, $"Slice count must be MAX_SPLAT_LAYERS={MAX}.");
        }

        [Test]
        public void BuildArray_MoreThanMaxLayers_TruncatesToMax()
        {
            var asset = MakeLayerSet(MAX + 2);
            // Expect a LogWarning — swallow it to keep the test output clean.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("truncating"));
            var array = asset.BuildArray();
            Assert.IsNotNull(array);
            Assert.AreEqual(MAX, array!.depth,
                $"Slice count must be capped at MAX_SPLAT_LAYERS={MAX} even with {MAX + 2} inputs.");
        }

        [Test]
        public void BuildArray_ActiveLayerCount_RespectsMax()
        {
            var asset = MakeLayerSet(MAX + 3);
            Assert.AreEqual(MAX, asset.ActiveLayerCount,
                "ActiveLayerCount must not exceed MAX_SPLAT_LAYERS.");
        }

        [Test]
        public void BuildArray_Format_IsMobileCompatible()
        {
            // RGBA32 source → output must be RGBA32 (uncompressed mobile-safe).
            var asset = MakeLayerSet(2);
            var array = asset.BuildArray();
            Assert.IsNotNull(array);
            Assert.AreEqual(TextureFormat.RGBA32, array!.format,
                "Output Texture2DArray format must be mobile-compatible RGBA32 for uncompressed input.");
        }
    }
}
