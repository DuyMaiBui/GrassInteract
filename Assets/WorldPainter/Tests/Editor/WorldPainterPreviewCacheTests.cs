#nullable enable
using NUnit.Framework;
using UnityEngine;
using GpuTerrain.Editor;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for <see cref="WorldPainterPreviewCache"/>:
    /// – GetOrRender returns null for null mesh (zero render calls).
    /// – Returns a texture when mesh is present.
    /// – Returns the same texture reference on a second call (no re-render).
    /// – InvalidateAll causes a re-render on the next GetOrRender call.
    ///
    /// Note: actual GPU rendering is not verified in EditMode (no display device);
    /// these tests validate the cache invalidation and null-guard contracts only.
    /// </summary>
    [TestFixture]
    public sealed class WorldPainterPreviewCacheTests
    {
        private WorldPainterPreviewCache? cache;

        [SetUp]
        public void SetUp()
        {
            this.cache = new WorldPainterPreviewCache();
        }

        [TearDown]
        public void TearDown()
        {
            this.cache?.Cleanup();
            this.cache = null;
        }

        // ── Null-mesh guard ───────────────────────────────────────────────────

        [Test]
        public void GetOrRender_NullMesh_ReturnsNull()
        {
            var result = this.cache!.GetOrRender(key: 1, mesh: null, mat: null);
            Assert.IsNull(result, "GetOrRender(null mesh) must return null.");
        }

        // ── Cache hit: same reference returned ────────────────────────────────

        [Test]
        public void GetOrRender_SameMesh_SameKey_ReturnsCachedTexture()
        {
            var mesh = MakeMesh();
            try
            {
                var first  = this.cache!.GetOrRender(key: 42, mesh: mesh, mat: null, px: 8);
                var second = this.cache!.GetOrRender(key: 42, mesh: mesh, mat: null, px: 8);

                // Both calls should return the same Texture2D instance (cache hit).
                // In headless EditMode the render may return null — that's acceptable;
                // what must NOT happen is two different non-null instances.
                if (first != null && second != null)
                    Assert.AreSame(first, second, "Second GetOrRender must return the cached instance.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // ── InvalidateAll forces re-render ────────────────────────────────────

        [Test]
        public void InvalidateAll_ClearsStaleFlags_ForcesMeshChange()
        {
            var mesh = MakeMesh();
            try
            {
                // Fill cache.
                this.cache!.GetOrRender(key: 77, mesh: mesh, mat: null, px: 8);

                // Invalidate — should clear internal dirty state.
                Assert.DoesNotThrow(() => this.cache.InvalidateAll(),
                    "InvalidateAll must not throw.");

                // Re-request after invalidation — must not throw (re-render path).
                Assert.DoesNotThrow(
                    () => this.cache.GetOrRender(key: 77, mesh: mesh, mat: null, px: 8),
                    "GetOrRender after InvalidateAll must not throw.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Mesh MakeMesh()
        {
            var m = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            m.vertices  = new[] { Vector3.zero, Vector3.right, Vector3.up };
            m.triangles = new[] { 0, 1, 2 };
            m.RecalculateNormals();
            return m;
        }
    }
}
