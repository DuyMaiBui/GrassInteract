#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GPUGrass
{
    /// <summary>
    /// Per-camera Hi-Z (hierarchical depth) pyramid buffer for grass chunk occlusion culling.
    /// One instance is created per camera (keyed by the Camera object) and shared across all
    /// grass fields that render from that camera.
    ///
    /// Usage:
    ///   <see cref="GpuGrassHiZFeature"/> calls <see cref="GetOrCreate"/> after opaques to build
    ///   the pyramid. <see cref="GpuGrassRenderer"/> calls <see cref="GetOrCreate"/> per-frame to
    ///   read the texture + previous-frame VP matrix and bind them into ChunkCull.
    ///
    /// Fail-open: <see cref="IsReady"/> is false until the first depth capture succeeds. The renderer
    /// must check <see cref="IsReady"/> before enabling occlusion — missing/unready means no occlusion.
    /// </summary>
    internal sealed class GpuGrassHiZ
    {
        // Static per-camera registry. Cameras are looked up by reference (object identity).
        private static readonly Dictionary<Camera, GpuGrassHiZ> instances = new();

        // The R32F mip-chain pyramid RT. Full mip chain; enableRandomWrite=true.
        private RenderTexture? pyramid;

        /// <summary>Base width of the pyramid (= half screen width, clamped to power-of-two-ish).</summary>
        public int BaseWidth { get; private set; }

        /// <summary>Base height of the pyramid (= half screen height).</summary>
        public int BaseHeight { get; private set; }

        /// <summary>Number of mip levels in the pyramid (including mip 0).</summary>
        public int MipCount { get; private set; }

        /// <summary>The pyramid RenderTexture (R32F, full mip chain, enableRandomWrite). Null until first build.</summary>
        public RenderTexture? Pyramid => this.pyramid;

        /// <summary>
        /// False until at least one successful depth capture has been recorded.
        /// When false, the renderer must skip occlusion culling (fail-open: behave like today).
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>View-projection matrix from the PREVIOUS frame, used for reprojection in ChunkCull.</summary>
        public Matrix4x4 PrevViewProj { get; private set; } = Matrix4x4.identity;

        /// <summary>View-projection matrix for the CURRENT frame (updated by GpuGrassHiZFeature each frame).</summary>
        public Matrix4x4 CurrentViewProj { get; private set; } = Matrix4x4.identity;

        // ── Static registry ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the <see cref="GpuGrassHiZ"/> instance for <paramref name="cam"/>, creating one if absent.
        /// Call from both the renderer feature (to build) and the renderer (to read/bind).
        /// </summary>
        public static GpuGrassHiZ GetOrCreate(Camera cam)
        {
            if (!instances.TryGetValue(cam, out var hiz))
            {
                PruneDeadCameras(); // release entries whose camera was destroyed (play/edit cycles)
                hiz = new GpuGrassHiZ();
                instances[cam] = hiz;
            }
            return hiz;
        }

        /// <summary>Releases Hi-Z entries whose camera has been destroyed (Unity null check). Cheap — the
        /// camera count is tiny — and only runs on a registry miss, not every frame.</summary>
        private static void PruneDeadCameras()
        {
            List<Camera>? dead = null;
            foreach (var kvp in instances)
                // Unity's overloaded == reports a destroyed camera; the C# wrapper itself is non-null.
                if (kvp.Key == null) (dead ??= new List<Camera>()).Add(kvp.Key!);
            if (dead == null) return;
            foreach (var cam in dead)
            {
                if (instances.TryGetValue(cam, out var hiz)) hiz.ReleasePyramid();
                instances.Remove(cam);
            }
        }

        /// <summary>
        /// Removes and releases the Hi-Z resources for <paramref name="cam"/>.
        /// Call from <c>Camera.onPostRender</c> or when the camera is destroyed/disabled.
        /// </summary>
        public static void Release(Camera cam)
        {
            if (instances.TryGetValue(cam, out var hiz))
            {
                hiz.ReleasePyramid();
                instances.Remove(cam);
            }
        }

        /// <summary>Releases all per-camera Hi-Z instances. Call on domain reload / app quit.</summary>
        public static void ReleaseAll()
        {
            foreach (var hiz in instances.Values)
                hiz.ReleasePyramid();
            instances.Clear();
        }

        // ── Instance methods (called by GpuGrassHiZFeature) ──────────────────

        /// <summary>
        /// Ensures the pyramid RT exists and matches the requested base dimensions.
        /// Rebuilds if size changed. Called by <see cref="GpuGrassHiZFeature"/> before dispatching.
        /// </summary>
        public void EnsurePyramid(int baseW, int baseH)
        {
            if (this.pyramid != null && this.BaseWidth == baseW && this.BaseHeight == baseH)
                return;

            this.ReleasePyramid();

            // Clamp to at least 1x1.
            baseW = Mathf.Max(1, baseW);
            baseH = Mathf.Max(1, baseH);

            // Compute mip count: log2(max(w,h)) + 1, clamped to [1, 12].
            int maxDim = Mathf.Max(baseW, baseH);
            int mips = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(maxDim, 2f)) + 1, 1, 12);

            var rt = new RenderTexture(baseW, baseH, 0, RenderTextureFormat.RFloat)
            {
                name           = "GpuGrass_HiZPyramid",
                useMipMap      = true,
                autoGenerateMips = false,
                enableRandomWrite = true,
                filterMode     = FilterMode.Point,
                wrapMode       = TextureWrapMode.Clamp,
            };
            rt.Create();

            this.pyramid   = rt;
            this.BaseWidth  = baseW;
            this.BaseHeight = baseH;
            this.MipCount   = mips;
        }

        /// <summary>
        /// Called by <see cref="GpuGrassHiZFeature"/> after the pyramid has been successfully built
        /// for this frame. Advances the VP ring (prev ← current) and marks the buffer ready.
        /// </summary>
        public void MarkReady(Matrix4x4 currentViewProj)
        {
            this.PrevViewProj    = this.CurrentViewProj;
            this.CurrentViewProj = currentViewProj;
            this.IsReady         = true;
        }

        /// <summary>
        /// Called by <see cref="GpuGrassHiZFeature"/> when depth is unavailable (missing texture,
        /// platform gap). Clears the ready flag so the renderer falls back to frustum-only cull.
        /// </summary>
        public void MarkNotReady()
        {
            this.IsReady = false;
        }

        private void ReleasePyramid()
        {
            if (this.pyramid != null)
            {
                this.pyramid.Release();
                Object.Destroy(this.pyramid);
                this.pyramid = null;
            }
            this.IsReady = false;
        }

        // ── Pure math — internal static so EditMode tests can reach them ──────

        /// <summary>
        /// Projects all 8 corners of <paramref name="aabb"/> through <paramref name="viewProj"/> into
        /// NDC, then into a [0,1]×[0,1] screen rect, and returns the NEAREST (smallest linear-depth)
        /// corner depth in <paramref name="nearestDepth"/>.
        ///
        /// Returns <c>false</c> when the projection is degenerate or all corners are behind the near
        /// plane — the caller must KEEP the chunk (fail-open: do not cull on missing data).
        ///
        /// The returned <paramref name="screenRect"/> is in normalised [0,1] UV space, X right, Y up.
        /// </summary>
        internal static bool TryProjectAabbToScreenRect(
            Bounds aabb,
            Matrix4x4 viewProj,
            out Rect screenRect,
            out float nearestDepth)
        {
            screenRect   = Rect.zero;
            nearestDepth = 0f;

            Vector3 mn = aabb.min;
            Vector3 mx = aabb.max;

            // 8 corners of the AABB.
            Vector3[] corners = new Vector3[8]
            {
                new(mn.x, mn.y, mn.z),
                new(mx.x, mn.y, mn.z),
                new(mn.x, mx.y, mn.z),
                new(mx.x, mx.y, mn.z),
                new(mn.x, mn.y, mx.z),
                new(mx.x, mn.y, mx.z),
                new(mn.x, mx.y, mx.z),
                new(mx.x, mx.y, mx.z),
            };

            float minX =  float.MaxValue;
            float minY =  float.MaxValue;
            float maxX = -float.MaxValue;
            float maxY = -float.MaxValue;
            float minDepth = float.MaxValue;

            int behindCount = 0;

            foreach (var c in corners)
            {
                // Project via view-projection matrix.
                Vector4 clip = viewProj * new Vector4(c.x, c.y, c.z, 1f);

                // clip.w < 0 or ≈ 0 means behind or on the near plane.
                if (clip.w <= 1e-6f)
                {
                    behindCount++;
                    continue;
                }

                // Perspective divide → NDC.
                float ndcX = clip.x / clip.w;
                float ndcY = clip.y / clip.w;

                // NDC → UV [0,1].
                float uvX = ndcX * 0.5f + 0.5f;
                float uvY = ndcY * 0.5f + 0.5f;

                // Linear eye depth: clip.w IS eye-space depth for a standard projection
                // (column-major Unity convention: w = -z_eye for GL, w = z_eye for DX).
                // Use clip.w directly as a monotone proxy for linear depth — sufficient for
                // conservative cull (nearest = smallest w among forward-facing corners).
                float eyeDepth = clip.w;

                if (uvX < minX) minX = uvX;
                if (uvX > maxX) maxX = uvX;
                if (uvY < minY) minY = uvY;
                if (uvY > maxY) maxY = uvY;
                if (eyeDepth < minDepth) minDepth = eyeDepth;
            }

            // All 8 corners behind the camera (or degenerate) → fail-open.
            if (behindCount == 8)
                return false;

            // Degenerate rect → fail-open.
            if (maxX <= minX || maxY <= minY)
                return false;

            // Clamp rect to [0,1] (partial off-screen is fine — visible portion suffices).
            minX = Mathf.Clamp01(minX);
            minY = Mathf.Clamp01(minY);
            maxX = Mathf.Clamp01(maxX);
            maxY = Mathf.Clamp01(maxY);

            if (maxX <= minX || maxY <= minY)
                return false;

            screenRect   = Rect.MinMaxRect(minX, minY, maxX, maxY);
            nearestDepth = minDepth;
            return true;
        }

        /// <summary>
        /// Returns the coarsest mip level (highest index = most blurred) whose single texel covers
        /// <paramref name="screenRectInPixels"/> (a rect in pyramid-pixel space at mip 0).
        /// Result is clamped to <c>[0, mipCount-1]</c>.
        ///
        /// Strategy: find the mip at which the texel size (1 &lt;&lt; mip) exceeds the rect's
        /// larger dimension. That texel is guaranteed to cover the entire rect.
        /// </summary>
        internal static int SelectMip(Rect screenRectInPixels, int baseSize, int mipCount)
        {
            if (mipCount <= 1) return 0;

            float rectMax = Mathf.Max(screenRectInPixels.width, screenRectInPixels.height);
            if (rectMax <= 0f) return mipCount - 1; // degenerate → coarsest (fail-open: keep chunk)

            // Find smallest mip whose texel size >= rect max dimension.
            // texelSize at mip m = (1 << m). We want 1<<m >= rectMax.
            int mip = 0;
            while (mip < mipCount - 1 && (1 << mip) < (int)rectMax)
                mip++;

            return Mathf.Clamp(mip, 0, mipCount - 1);
        }
    }
}
