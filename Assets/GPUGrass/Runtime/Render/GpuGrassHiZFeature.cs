#nullable enable
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace GPUGrass
{
    /// <summary>
    /// URP Scriptable Renderer Feature that builds the per-camera Hi-Z depth pyramid used by
    /// <see cref="GpuGrassRenderer"/> for chunk occlusion culling.
    ///
    /// Setup: add this feature to your URP Renderer asset (Renderer Features list) and assign the
    /// <c>HiZBuild.compute</c> shader to the <c>Hi Z Build Compute</c> field. The URP Renderer must have
    /// <b>Depth Texture</b> enabled (the feature also requests it via
    /// <see cref="ScriptableRenderPassInput.Depth"/>).
    ///
    /// RenderGraph path (URP 17 / Unity 6): the pass runs at
    /// <see cref="RenderPassEvent.AfterRenderingOpaques"/>, reads <c>UniversalResourceData.cameraDepthTexture</c>,
    /// and dispatches <c>HiZBuild.compute</c> into a persistent per-camera mip-chain RenderTexture
    /// (owned by <see cref="GpuGrassHiZ"/>). When depth is unavailable, the camera's
    /// <see cref="GpuGrassHiZ.IsReady"/> is set false so the renderer falls back to frustum+distance
    /// culling only (fail-open).
    /// </summary>
    public sealed class GpuGrassHiZFeature : ScriptableRendererFeature
    {
        [Tooltip("The HiZBuild.compute shader (CopyDepth + ReduceMip kernels). Assign in the Renderer asset.")]
        public ComputeShader? hiZBuildCompute;

        private HiZBuildPass? pass;

        /// <inheritdoc/>
        public override void Create()
        {
            this.pass = new HiZBuildPass(this.hiZBuildCompute)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques,
            };
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (this.pass == null || this.hiZBuildCompute == null)
                return;

            this.pass.Setup(this.hiZBuildCompute);
            renderer.EnqueuePass(this.pass);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            this.pass = null;
            GpuGrassHiZ.ReleaseAll();
            base.Dispose(disposing);
        }

        // ── Inner render pass (RenderGraph) ───────────────────────────────────

        private sealed class HiZBuildPass : ScriptableRenderPass
        {
            private const string KERNEL_COPY_DEPTH = "CopyDepth";
            private const string KERNEL_REDUCE_MIP = "ReduceMip";

            // Property IDs cached once.
            private static readonly int ID_CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            private static readonly int ID_HiZMipDst    = Shader.PropertyToID("_HiZMipDst");
            private static readonly int ID_HiZMipSrc    = Shader.PropertyToID("_HiZMipSrc");
            private static readonly int ID_ScreenSize   = Shader.PropertyToID("_ScreenSize");
            private static readonly int ID_HiZDstSize   = Shader.PropertyToID("_HiZDstSize");
            private static readonly int ID_ZBufferParams = Shader.PropertyToID("_ZBufferParams");

            // One-time warning guard — log once, not every frame.
            private static bool depthWarningLogged;

            private ComputeShader? computeShader;
            private int kernelCopy;
            private int kernelReduce;

            public HiZBuildPass(ComputeShader? cs)
            {
                this.computeShader = cs;
                if (cs != null)
                {
                    this.kernelCopy   = cs.FindKernel(KERNEL_COPY_DEPTH);
                    this.kernelReduce = cs.FindKernel(KERNEL_REDUCE_MIP);
                }
                // Ensure URP generates _CameraDepthTexture.
                this.ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void Setup(ComputeShader cs)
            {
                if (this.computeShader != cs)
                {
                    this.computeShader = cs;
                    this.kernelCopy    = cs.FindKernel(KERNEL_COPY_DEPTH);
                    this.kernelReduce  = cs.FindKernel(KERNEL_REDUCE_MIP);
                }
            }

            /// <summary>Per-pass data captured for the deferred render-graph execution.</summary>
            private sealed class PassData
            {
                public ComputeShader cs = null!;
                public int kernelCopy;
                public int kernelReduce;
                public TextureHandle depth;
                public GpuGrassHiZ hiz = null!;
                public int screenW, screenH;
                public int baseW, baseH;
                public Vector4 zBufferParams;
                public Matrix4x4 viewProj;
            }

            /// <inheritdoc/>
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (this.computeShader == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                Camera cam = cameraData.camera;
                if (cam == null)
                    return;

                // Only game/scene cameras get a pyramid (skip preview/reflection probes).
                if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection)
                    return;

                GpuGrassHiZ hiz = GpuGrassHiZ.GetOrCreate(cam);

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                TextureHandle depth = resourceData.cameraDepthTexture;
                if (!depth.IsValid())
                {
                    if (!depthWarningLogged)
                    {
                        Debug.LogWarning(
                            "[GpuGrassHiZ] cameraDepthTexture is unavailable — grass occlusion culling is " +
                            "disabled (fail-open). Enable Depth Texture on the URP Renderer asset.");
                        depthWarningLogged = true;
                    }
                    hiz.MarkNotReady();
                    return;
                }

                // Base pyramid size = half the render target (mobile fill cap; see HiZBuild.compute).
                int screenW = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
                int screenH = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
                int baseW   = Mathf.Max(1, screenW / 2);
                int baseH   = Mathf.Max(1, screenH / 2);

                hiz.EnsurePyramid(baseW, baseH);
                if (hiz.Pyramid == null)
                {
                    hiz.MarkNotReady();
                    return;
                }

                // _ZBufferParams from THIS camera (compute can't rely on the camera global).
                float near = cam.nearClipPlane;
                float far  = cam.farClipPlane;
                float fn   = far / Mathf.Max(near, 1e-6f);
                Vector4 zbp = new(1f - fn, fn, (1f - fn) / far, fn / far);

                // VP for this frame; consumed by the grass cull NEXT frame via PrevViewProj reprojection.
                Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;

                using (var builder = renderGraph.AddUnsafePass<PassData>("GpuGrassHiZ.Build", out PassData data))
                {
                    data.cs           = this.computeShader;
                    data.kernelCopy   = this.kernelCopy;
                    data.kernelReduce = this.kernelReduce;
                    data.depth        = depth;
                    data.hiz          = hiz;
                    data.screenW      = screenW;
                    data.screenH      = screenH;
                    data.baseW        = baseW;
                    data.baseH        = baseH;
                    data.zBufferParams = zbp;
                    data.viewProj      = viewProj;

                    builder.UseTexture(depth, AccessFlags.Read);
                    builder.AllowPassCulling(false); // compute output (the pyramid) is consumed outside the graph
                    builder.SetRenderFunc<PassData>(static (data, ctx) => ExecutePass(data, ctx));
                }
            }

            private static void ExecutePass(PassData data, UnsafeGraphContext ctx)
            {
                RenderTexture? pyramid = data.hiz.Pyramid;
                if (pyramid == null) return;

                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                int mipCount = data.hiz.MipCount;

                cmd.SetComputeVectorParam(data.cs, ID_ZBufferParams, data.zBufferParams);

                // ── Mip 0: CopyDepth (linearize _CameraDepthTexture → pyramid mip 0) ──
                // TextureHandle implicitly converts to RTHandle inside the pass.
                cmd.SetComputeTextureParam(data.cs, data.kernelCopy, ID_CameraDepthTexture, data.depth);
                cmd.SetComputeTextureParam(data.cs, data.kernelCopy, ID_HiZMipDst, pyramid, 0);
                cmd.SetComputeVectorParam(data.cs, ID_ScreenSize, new Vector2(data.screenW, data.screenH));
                cmd.SetComputeVectorParam(data.cs, ID_HiZDstSize, new Vector2(data.baseW, data.baseH));

                int gx0 = Mathf.CeilToInt(data.baseW / 8f);
                int gy0 = Mathf.CeilToInt(data.baseH / 8f);
                cmd.DispatchCompute(data.cs, data.kernelCopy, gx0, gy0, 1);

                // ── Mips 1..N: ReduceMip (max-Z of 2×2 parent = conservative farthest) ──
                int srcW = data.baseW, srcH = data.baseH;
                for (int mip = 1; mip < mipCount; mip++)
                {
                    int dstW = Mathf.Max(1, srcW / 2);
                    int dstH = Mathf.Max(1, srcH / 2);

                    cmd.SetComputeTextureParam(data.cs, data.kernelReduce, ID_HiZMipSrc, pyramid, mip - 1);
                    cmd.SetComputeTextureParam(data.cs, data.kernelReduce, ID_HiZMipDst, pyramid, mip);
                    cmd.SetComputeVectorParam(data.cs, ID_HiZDstSize, new Vector2(dstW, dstH));

                    int gx = Mathf.CeilToInt(dstW / 8f);
                    int gy = Mathf.CeilToInt(dstH / 8f);
                    cmd.DispatchCompute(data.cs, data.kernelReduce, gx, gy, 1);

                    srcW = dstW;
                    srcH = dstH;
                }

                // Advance the VP ring + mark ready ONLY now — at GPU-execution time, after the pyramid
                // has actually been written. Marking ready at record time would let the grass cull read
                // an unwritten (zeroed) pyramid on the first frame → false occlusion.
                data.hiz.MarkReady(data.viewProj);
            }
        }
    }
}
