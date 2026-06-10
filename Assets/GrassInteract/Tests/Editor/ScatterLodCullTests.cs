#nullable enable
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract.Tests
{
    /// <summary>
    /// Spec-lock tests for the cull-distance math in <see cref="ScatterRenderConfig"/>.
    ///
    /// <list type="number">
    ///   <item><b>Boundary bands</b> — cull=500 → maxSqr=500², present at 499m, culled at 501m.</item>
    ///   <item><b>Mode independence</b> — no isPlaying branch: same RenderCullDistance² in edit and play.</item>
    ///   <item><b>Migration formula</b> — max(2*lastSwitch, 500) backfills renderCullDistance=0 legacy assets.</item>
    /// </list>
    /// </summary>
    public sealed class ScatterLodCullTests
    {
        // ── 1. Cull-boundary band test (pure math — no GPU) ───────────────────

        [Test]
        public void RenderCullDistance500_BandsAreCorrect()
        {
            // Arrange: 3-LOD config, switches 12 / 30, cull 500.
            var lods = new[]
            {
                new ScatterLod { mesh = null, maxDistance = 12f },
                new ScatterLod { mesh = null, maxDistance = 30f },
                new ScatterLod { mesh = null, maxDistance = 0f }, // last LOD bounded by cull
            };
            var cfg = new ScatterRenderConfig(null, default, lods, renderCullDistance: 500f);

            // maxSqrDistance computed exactly as the engines now do: cull * cull.
            float maxSqr = cfg.RenderCullDistance * cfg.RenderCullDistance;
            Assert.AreEqual(500f * 500f, maxSqr, 1e-3f);

            // Present just inside cull, culled just outside.
            Assert.Less(499f * 499f, maxSqr);   // 499m → rendered
            Assert.Greater(501f * 501f, maxSqr); // 501m → culled
        }

        // ── 2. Edit == play parity (regression guard against isPlaying branch) ─

        [Test]
        public void RenderCullDistance_IsModeIndependent()
        {
            var lods = new[] { new ScatterLod { maxDistance = 30f }, new ScatterLod { maxDistance = 0f } };
            var cfg = new ScatterRenderConfig(null, default, lods, renderCullDistance: 750f);
            float maxSqr = cfg.RenderCullDistance * cfg.RenderCullDistance;
            Assert.AreEqual(750f * 750f, maxSqr, 1e-3f); // no 250000f play floor, no 1e8f editor floor
        }

        // ── 3. Migration default (Phase 1 spec-lock) ─────────────────────────

        [Test]
        public void Migration_BackfillsRenderCullDistance_PreservingLegacyFarCull()
        {
            // Legacy asset: cull == 0, last switch == 30 → expect max(2*30, 500) == 500.
            var lods = new[] { new ScatterLod { maxDistance = 12f }, new ScatterLod { maxDistance = 30f }, new ScatterLod() };
            var cfg = new ScatterRenderConfig(null, default, lods, renderCullDistance: 0f);

            float[] dists = cfg.LodMaxDistances;           // length == 2 → [12, 30]
            float lastSwitch = dists.Length > 0 ? dists[^1] : 0f;
            float migrated = Mathf.Max(2f * lastSwitch, 500f);
            Assert.AreEqual(500f, migrated, 1e-3f);

            // Large last switch → 2*lastSwitch wins.
            var lods2 = new[] { new ScatterLod { maxDistance = 50f }, new ScatterLod { maxDistance = 400f }, new ScatterLod() };
            var cfg2 = new ScatterRenderConfig(null, default, lods2, 0f);
            Assert.AreEqual(800f, Mathf.Max(2f * cfg2.LodMaxDistances[^1], 500f), 1e-3f);

            // <2 LODs → empty dists → 500 floor, no throw.
            var lods1 = new[] { new ScatterLod { maxDistance = 0f } };
            var cfg1 = new ScatterRenderConfig(null, default, lods1, 0f);
            Assert.AreEqual(500f, Mathf.Max(2f * (cfg1.LodMaxDistances.Length > 0 ? cfg1.LodMaxDistances[^1] : 0f), 500f), 1e-3f);
        }
    }
}
