#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// GPU-free CPU mirrors of the seam-aware Smooth math in <c>TerrainBrush.compute</c>
    /// (<c>SampleNeighborhoodDensity</c> / <c>SampleNeighborhoodAlpha</c> remap, the density
    /// 7×7 box blur, and the active-channel-only alphamap blur + <c>RenormalizeAlphaOthers</c>).
    ///
    /// These pin the kernel contract on machines without a GPU — the same pattern as
    /// <see cref="TerrainBrushMathTests"/> (falloff) and <see cref="DensityBrushMathTests"/>
    /// (stamp spacing). The mirrors below must stay byte-for-byte equivalent to the HLSL: the
    /// shared-edge remap is <c>nb - (RES-1)</c> for +X/+Z overflow and <c>(RES-1)+nb</c> for the
    /// −X/−Z underflow (identical to the shipped height Smooth convention on the same RES grid).
    /// </summary>
    [TestFixture]
    public sealed class BrushSmoothSeamMathTests
    {
        // ── Seam remap reference (mirror of SampleNeighborhood* index math) ──────

        private const int RES = 8;

        /// <summary>+X / +Z overflow remap: a tap one past the tile edge reads neighbour column 1
        /// (column 0 duplicates this tile's edge column under the shared-edge convention).</summary>
        private static int RemapPositive(int nb) => nb - (RES - 1);

        /// <summary>−X / −Z underflow remap.</summary>
        private static int RemapNegative(int nb) => (RES - 1) + nb;

        [Test]
        public void Remap_PosOverflow_ByOne_MapsToNeighborColumnOne()
        {
            // First tap past the right edge (nb = RES) → neighbour column 1, not 0.
            Assert.AreEqual(1, RemapPositive(RES));
        }

        [Test]
        public void Remap_PosOverflow_ByThree_MapsToNeighborColumnThree()
        {
            Assert.AreEqual(3, RemapPositive(RES + 2));
        }

        [Test]
        public void Remap_NegUnderflow_ByOne_MapsToNeighborColumnPenultimate()
        {
            // First tap past the left edge (nb = -1) → neighbour column RES-2.
            Assert.AreEqual(RES - 2, RemapNegative(-1));
        }

        [Test]
        public void Remap_RemappedIndices_StayInRange()
        {
            // The blur window is ±3, so overflow taps span nb ∈ [RES, RES+2] and [-3, -1].
            for (int o = 0; o <= 2; ++o)
            {
                int p = RemapPositive(RES + o);
                Assert.GreaterOrEqual(p, 0); Assert.Less(p, RES, $"+overflow {RES + o} out of range");
            }
            for (int o = 1; o <= 3; ++o)
            {
                int n = RemapNegative(-o);
                Assert.GreaterOrEqual(n, 0); Assert.Less(n, RES, $"-underflow {-o} out of range");
            }
        }

        // ── Density seam-aware sampler + box blur (mirror of PaintDensity mode 2) ──

        /// <summary>Mirror of <c>SampleNeighborhoodDensity</c>. Only the +X neighbour is modelled
        /// here (the directions are symmetric); pass <paramref name="hasPosX"/> = false to mirror
        /// an unbound border (tap skipped, exactly the original single-tile clamp).</summary>
        private static bool SampleDensity(
            int nbx, int nby, float[,] self, float[,]? posX, bool hasPosX, out float d)
        {
            bool inX = nbx >= 0 && nbx < RES;
            bool inY = nby >= 0 && nby < RES;
            if (inX && inY) { d = self[nbx, nby]; return true; }

            if (!inX && inY && nbx >= RES && hasPosX && posX != null)
            { d = posX[RemapPositive(nbx), nby]; return true; }

            d = 0f;
            return false;
        }

        private static float SmoothDensityAt(
            int x, int y, float[,] self, float[,]? posX, bool hasPosX, float strengthWeight)
        {
            float sum = 0f, cnt = 0f;
            for (int dy = -3; dy <= 3; ++dy)
                for (int dx = -3; dx <= 3; ++dx)
                    if (SampleDensity(x + dx, y + dy, self, posX, hasPosX, out float d))
                    { sum += d; cnt += 1f; }

            float cur = self[x, y];
            float avg = cnt > 0f ? sum / cnt : cur;
            return Mathf.Lerp(cur, avg, strengthWeight);
        }

        private static float[,] Filled(float v)
        {
            var g = new float[RES, RES];
            for (int x = 0; x < RES; ++x)
                for (int y = 0; y < RES; ++y) g[x, y] = v;
            return g;
        }

        [Test]
        public void DensitySmooth_UniformField_IsUnchanged()
        {
            var self = Filled(0.6f);
            // Interior texel, no seam involved → blur of a constant returns the constant.
            float result = SmoothDensityAt(4, 4, self, null, false, strengthWeight: 1f);
            Assert.AreEqual(0.6f, result, 1e-5f);
        }

        [Test]
        public void DensitySmooth_SeamBound_ReadsAcrossEdge_PullsTowardNeighbor()
        {
            var self = Filled(1f);     // this tile fully dense
            var posX = Filled(0f);     // neighbour empty across the +X seam

            // Edge column: with the neighbour bound, the +X overflow taps read 0 → average < 1.
            float seam   = SmoothDensityAt(RES - 1, 4, self, posX, hasPosX: true,  strengthWeight: 1f);
            // Same texel with NO neighbour bound: overflow taps skipped → stays 1 (clamped).
            float clamp  = SmoothDensityAt(RES - 1, 4, self, posX, hasPosX: false, strengthWeight: 1f);

            Assert.Less(seam, 1f, "Seam-bound edge must pull toward the empty neighbour.");
            Assert.AreEqual(1f, clamp, 1e-5f, "Unbound border must reproduce the single-tile clamp.");
            Assert.Less(seam, clamp, "Seam-aware result must differ from the clamped result.");
        }

        [Test]
        public void DensitySmooth_ZeroStrength_IsNoOp()
        {
            var self = Filled(1f);
            var posX = Filled(0f);
            float result = SmoothDensityAt(RES - 1, 4, self, posX, hasPosX: true, strengthWeight: 0f);
            Assert.AreEqual(1f, result, 1e-5f, "strength*weight = 0 → texel unchanged.");
        }

        // ── Active-channel-only alphamap smooth (mirror of PaintAlphamap mode 2) ──

        /// <summary>Mirror of <c>RenormalizeAlphaOthers</c>: scale the non-active channels down so
        /// the total stays ≤ 1; total ≤ 1 is left untouched.</summary>
        private static Vector4 RenormalizeOthers(Vector4 t, int channel)
        {
            float total = t.x + t.y + t.z + t.w;
            if (total <= 1f) return t;

            float active   = channel == 0 ? t.x : channel == 1 ? t.y : channel == 2 ? t.z : t.w;
            float budget   = Mathf.Clamp01(1f - active);
            float otherSum = total - active;
            float scale    = otherSum > 1e-6f ? budget / otherSum : 0f;

            if (channel != 0) t.x *= scale;
            if (channel != 1) t.y *= scale;
            if (channel != 2) t.z *= scale;
            if (channel != 3) t.w *= scale;
            return t;
        }

        /// <summary>Active-channel-only smooth result for one texel given the blurred active-channel
        /// average. Mirrors the mode-2 branch: lerp the active channel toward the average, leave the
        /// others, then renormalise.</summary>
        private static Vector4 SmoothAlpha(Vector4 cur, int channel, float blurredAvg, float paint)
        {
            float curActive = channel == 0 ? cur.x : channel == 1 ? cur.y : channel == 2 ? cur.z : cur.w;
            float blended   = Mathf.Clamp01(Mathf.Lerp(curActive, blurredAvg, paint));

            var t = cur;
            if (channel == 0) t.x = blended;
            if (channel == 1) t.y = blended;
            if (channel == 2) t.z = blended;
            if (channel == 3) t.w = blended;
            return RenormalizeOthers(t, channel);
        }

        [Test]
        public void AlphaSmooth_OnlyActiveChannelChanges_WhenTotalStaysLEOne()
        {
            // Active channel 0 = 0.8, others small; smoothing pulls active down to 0.4 → total < 1.
            var cur = new Vector4(0.8f, 0.1f, 0.05f, 0f);
            Vector4 r = SmoothAlpha(cur, channel: 0, blurredAvg: 0.4f, paint: 1f);

            Assert.AreEqual(0.4f, r.x, 1e-5f, "Active channel blends toward the average.");
            Assert.AreEqual(0.1f, r.y, 1e-5f, "Other channels untouched when total ≤ 1.");
            Assert.AreEqual(0.05f, r.z, 1e-5f);
            Assert.AreEqual(0f, r.w, 1e-5f);
        }

        [Test]
        public void AlphaSmooth_RenormalizesOthers_WhenActiveRaisesTotalAboveOne()
        {
            // Others sum to 0.8; smoothing raises active 0.2 → 0.6 makes total 1.4 > 1.
            var cur = new Vector4(0.2f, 0.5f, 0.3f, 0f);
            Vector4 r = SmoothAlpha(cur, channel: 0, blurredAvg: 0.6f, paint: 1f);

            Assert.AreEqual(0.6f, r.x, 1e-5f, "Active channel preserved at its blended value.");
            float total = r.x + r.y + r.z + r.w;
            Assert.LessOrEqual(total, 1f + 1e-5f, "Total must be renormalised to ≤ 1.");
            // Others scaled proportionally: budget 0.4 split over otherSum 0.8 → scale 0.5.
            Assert.AreEqual(0.25f, r.y, 1e-5f);
            Assert.AreEqual(0.15f, r.z, 1e-5f);
        }

        [Test]
        public void AlphaSmooth_ZeroStrength_IsNoOp()
        {
            var cur = new Vector4(0.8f, 0.1f, 0.05f, 0f);
            Vector4 r = SmoothAlpha(cur, channel: 0, blurredAvg: 0.4f, paint: 0f);
            Assert.AreEqual(cur.x, r.x, 1e-5f);
            Assert.AreEqual(cur.y, r.y, 1e-5f);
        }

        [Test]
        public void AlphaRenormalize_TotalAtOrBelowOne_Untouched()
        {
            var t = new Vector4(0.5f, 0.3f, 0.2f, 0f); // total = 1.0
            Vector4 r = RenormalizeOthers(t, channel: 0);
            Assert.AreEqual(t, r);
        }
    }
}
