#nullable enable
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPUGrass
{
    /// <summary>
    /// The GPU-driven indirect render tier for GPUGrass — the standalone, WorldPainter-free extraction of
    /// <c>GrassGpuEngine</c>. Builds a chunked blade buffer from the editor bake, runs a two-pass GPU cull
    /// (compute: ChunkCull → WriteArgsB → BladeCull), then issues one
    /// <see cref="Graphics.RenderMeshIndirect"/> per LOD. Wind + interactor/trail bend are fully GPU-side
    /// (the VS reconstructs each blade's TRS and deform); the CPU only advances a time accumulator and
    /// uploads the interactor/trail registries each frame.
    ///
    /// Stripped vs the WorldPainter original: no painting↔world root transform (terrain grass is
    /// world-space), no live scatter / baked-blob path (placement comes from
    /// <see cref="GpuGrassBakeData"/>), no adaptive-density controller, no runtime scale-factor, and no
    /// PBR/normal/emission/oriented keyword machinery (<see cref="GpuGrassConfig"/> exposes none of them).
    /// </summary>
    internal sealed class GpuGrassRenderer : IGpuGrassRenderer
    {
        private const int ARGS_INSTANCE_COUNT_OFFSET = 4; // byte offset of instanceCount in IndirectDrawIndexedArgs
        private const int CHUNK_SIZE = 16;                // world-space XZ cell size for the cull grid (m)
        private const int FULL_DENSITY_THRESHOLD = 256;   // BladeCull keeps blade when (hash%256) < this

        // Shader global / per-material property IDs (cached once — zero per-frame string allocs).
        private static readonly int ID_Blades              = Shader.PropertyToID("_Blades");
        private static readonly int ID_Interactors         = Shader.PropertyToID("_Interactors");
        private static readonly int ID_InteractorCount     = Shader.PropertyToID("_InteractorCount");
        private static readonly int ID_ScaleMax2           = Shader.PropertyToID("_ScaleMax2");
        private static readonly int ID_ScaleFactor         = Shader.PropertyToID("_ScaleFactor");
        private static readonly int ID_GrassTime           = Shader.PropertyToID("_GrassTime");
        private static readonly int ID_WindDir             = Shader.PropertyToID("_WindDir");
        private static readonly int ID_WindStrength        = Shader.PropertyToID("_WindStrength");
        private static readonly int ID_WindFrequency       = Shader.PropertyToID("_WindFrequency");
        private static readonly int ID_WindNoiseScale      = Shader.PropertyToID("_WindNoiseScale");
        private static readonly int ID_BendStrength        = Shader.PropertyToID("_BendStrength");
        private static readonly int ID_Flatten             = Shader.PropertyToID("_Flatten");
        private static readonly int ID_CamPosWS            = Shader.PropertyToID("_CamPosWS");
        private static readonly int ID_VisibleIndices      = Shader.PropertyToID("_VisibleIndices");
        private static readonly int ID_OrientMode          = Shader.PropertyToID("_OrientMode");
        private static readonly int ID_RotationOffsetEuler = Shader.PropertyToID("_RotationOffsetEuler");
        private static readonly int ID_WindEnabled         = Shader.PropertyToID("_WindEnabled");
        private static readonly int ID_InteractorsEnabled  = Shader.PropertyToID("_InteractorsEnabled");
        private const string KW_ReceiveShadows             = "_RECEIVE_SHADOWS";
        private const string KW_Lod2Billboard              = "_LOD2_BILLBOARD";

        // ── Injected ──────────────────────────────────────────────────────────
        private readonly ComputeShader computeShader;
        private readonly Material indirectMaterialBase;

        // ── Kernel indices ────────────────────────────────────────────────────
        private readonly int kernelCull;
        private readonly int kernelArgs;
        private readonly int kernelBlade;

        // ── Per-frame cull buffers (Pass A) ───────────────────────────────────
        private GraphicsBuffer? visibleChunksBuf;
        private GraphicsBuffer? visibleCountBuf;
        private GraphicsBuffer? dispatchArgsBuf;

        // ── Per-LOD visible-index buffers (Pass B) ────────────────────────────
        private GraphicsBuffer? visibleLod0Buf;
        private GraphicsBuffer? visibleLod1Buf;
        private GraphicsBuffer? visibleLod2Buf;

        // ── Per-LOD indirect draw args ────────────────────────────────────────
        private GraphicsBuffer? argsLod0Buf;
        private GraphicsBuffer? argsLod1Buf;
        private GraphicsBuffer? argsLod2Buf;

        // ── Static GPU data + per-frame registries ────────────────────────────
        private GpuGrassChunkedBuffer? bladeBuffer;
        private GpuGrassInteractorBuffer? interactorBuffer;
        private GpuGrassTrailBuffer? trailBuffer;

        // ── Per-LOD material clones (each owns its _VisibleIndices / _Blades binding) ──
        private Material? lodMat0;
        private Material? lodMat1;
        private Material? lodMat2;
        private Material?[] lodMats = Array.Empty<Material?>();

        private Mesh? mesh0;
        private Mesh? mesh1;
        private Mesh? mesh2;

        // ── Compute property IDs (Hi-Z occlusion bindings) ───────────────────
        private static readonly int ID_OcclusionEnabled = Shader.PropertyToID("occlusionEnabled");
        private static readonly int ID_PrevViewProj     = Shader.PropertyToID("prevViewProj");
        private static readonly int ID_HiZSize          = Shader.PropertyToID("hiZSize");
        private static readonly int ID_HiZMipCount      = Shader.PropertyToID("hiZMipCount");
        private static readonly int ID_HiZ              = Shader.PropertyToID("hiZ");

        // ── Config snapshot ───────────────────────────────────────────────────
        private Vector2 windDir;
        private float windStrength, windFrequency, windNoiseScale;
        private float bendStrength, flatten;
        private float lod0MaxSqrDist, lod1MaxSqrDist, maxSqrDistance;
        private float bladeCullMargin;
        private bool interactorsEnabled, trailEnabled, receiveShadows;
        private ShadowCastingMode shadowCastingMode;
        private bool enableOcclusionCulling;

        // ── Per-frame state ───────────────────────────────────────────────────
        private float time;
        private Bounds worldBounds;
        private bool isBuilt;
        // Adaptive density: 256 = full (all blades pass the BladeCull skip). Driven by SetDensity.
        private int densityThreshold = FULL_DENSITY_THRESHOLD;

        private CommandBuffer? cullCmd;
        private readonly Vector4[] frustumPlanes = new Vector4[6];
        private readonly Plane[]   planeScratch  = new Plane[6];

        /// <summary>
        /// Constructs the GPU tier. <paramref name="computeShader"/> is GPUGrass's GrassCull compute;
        /// <paramref name="indirectMaterial"/> is the GPUGrass/IndirectGrass base material (cloned 3× —
        /// one per LOD — so each can own its <c>_VisibleIndices</c> + <c>_Blades</c> binding).
        /// </summary>
        public GpuGrassRenderer(ComputeShader computeShader, Material indirectMaterial)
        {
            this.computeShader        = computeShader  ?? throw new ArgumentNullException(nameof(computeShader));
            this.indirectMaterialBase = indirectMaterial ?? throw new ArgumentNullException(nameof(indirectMaterial));
            this.kernelCull  = computeShader.FindKernel("ChunkCull");
            this.kernelArgs  = computeShader.FindKernel("WriteArgsB");
            this.kernelBlade = computeShader.FindKernel("BladeCull");
        }

        // ── IGpuGrassRenderer : Build ─────────────────────────────────────────

        /// <inheritdoc/>
        public void Build(GpuGrassConfig config, GpuGrassBakeData bake)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (bake == null)   throw new ArgumentNullException(nameof(bake));
            this.Dispose();

            // ── Config snapshot ──────────────────────────────────────────────
            Vector2 dir = config.WindDirection;
            this.windDir        = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector2.right;
            this.windStrength   = config.WindStrength;
            this.windFrequency  = config.WindFrequency;
            this.windNoiseScale = config.WindNoiseScale;
            this.bendStrength   = config.BendStrength;
            this.flatten        = config.Flatten;
            this.interactorsEnabled    = true;
            this.trailEnabled          = config.EnableTrailInteractors;
            this.receiveShadows        = config.ReceiveShadows;
            this.shadowCastingMode     = config.ShadowCastingMode;
            this.enableOcclusionCulling = config.EnableOcclusionCulling;

            // LOD thresholds: missing switch distances extend the last present LOD to the cull boundary.
            (this.lod0MaxSqrDist, this.lod1MaxSqrDist, this.maxSqrDistance) =
                LodThresholds(config.LodMaxDistances, config.RenderCullDistance);

            // ── Chunked blade buffer from the bake ───────────────────────────
            float scaleMax       = config.ScaleRange.y > 0f ? config.ScaleRange.y : 1f;
            float maxBladeHeight = Mathf.Max(0f, config.BladeHeightRange.y);
            // Bend headroom: a blade can lean ~its own length, so reserve one blade-height of slack.
            float bendHeadroom   = maxBladeHeight * scaleMax;
            float bladeReachY    = maxBladeHeight * scaleMax + bendHeadroom;
            float lateralPad     = scaleMax + bendHeadroom;
            this.bladeCullMargin = Mathf.Max(0f, bladeReachY);

            this.bladeBuffer = new GpuGrassChunkedBuffer();
            this.bladeBuffer.Build(bake, scaleMax, bladeReachY, lateralPad, CHUNK_SIZE);

            // Field-wide bounds for the per-Submit whole-field frustum gate + RenderMeshIndirect culling.
            // Inflate so a tall/leaning blade never gets the whole indirect draw frustum-culled early.
            Bounds wb = bake.WorldBounds;
            wb.Expand(new Vector3(lateralPad * 2f, bladeReachY * 2f, lateralPad * 2f));
            this.worldBounds = wb;

            // ── LOD meshes ───────────────────────────────────────────────────
            Mesh[] meshes = config.LodMeshes;
            if (meshes.Length == 0)
                Debug.LogWarning($"[GpuGrassRenderer] Config '{config.name}' has no LOD meshes — no grass will render.");
            this.mesh0 = meshes.Length > 0 ? meshes[0] : null;
            this.mesh1 = meshes.Length > 1 ? meshes[1] : null;
            this.mesh2 = meshes.Length > 2 ? meshes[2] : null;

            // Fold missing LOD slots DOWN into the nearest present LOD. The BladeCull compute always
            // buckets by distance into LOD0/1/2; if a config carries more switch distances than authored
            // meshes (e.g. the default {15,40} against a single placeholder LOD0 mesh), blades past the
            // first switch would bucket into a mesh-less LOD whose RenderMeshIndirect is skipped → they
            // silently vanish. Extending the surviving LOD's band to the cull boundary keeps every blade
            // drawn by a real mesh. farCap honours the "cull≤0 = render all" contract (float.MaxValue).
            float farCap = this.maxSqrDistance > 0f ? this.maxSqrDistance : float.MaxValue;
            if (this.mesh2 == null) this.lod1MaxSqrDist = farCap;            // no LOD2 → its band → LOD1
            if (this.mesh1 == null) this.lod0MaxSqrDist = this.lod1MaxSqrDist; // no LOD1 → its band → LOD0

            // ── GPU cull buffers ─────────────────────────────────────────────
            int chunkCap = Mathf.Max(1, this.bladeBuffer.TotalChunks);
            int bladeCap = Mathf.Max(1, this.bladeBuffer.TotalBlades);

            this.visibleChunksBuf = new GraphicsBuffer(GraphicsBuffer.Target.Append, chunkCap, sizeof(uint));
            this.visibleCountBuf  = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            this.dispatchArgsBuf  = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint));
            this.visibleLod0Buf   = new GraphicsBuffer(GraphicsBuffer.Target.Append, bladeCap, sizeof(uint));
            this.visibleLod1Buf   = new GraphicsBuffer(GraphicsBuffer.Target.Append, bladeCap, sizeof(uint));
            this.visibleLod2Buf   = new GraphicsBuffer(GraphicsBuffer.Target.Append, bladeCap, sizeof(uint));

            int argsStride = GraphicsBuffer.IndirectDrawIndexedArgs.size;
            this.argsLod0Buf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, argsStride);
            this.argsLod1Buf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, argsStride);
            this.argsLod2Buf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, argsStride);
            this.InitLodArgsFromMeshes();

            // ── Registries ───────────────────────────────────────────────────
            this.interactorBuffer = new GpuGrassInteractorBuffer();
            this.trailBuffer = new GpuGrassTrailBuffer();
            this.trailBuffer.BindGlobal();

            // ── Per-LOD material clones ──────────────────────────────────────
            this.lodMat0 = new Material(this.indirectMaterialBase) { name = "GpuGrassIndirect_LOD0" };
            this.lodMat1 = new Material(this.indirectMaterialBase) { name = "GpuGrassIndirect_LOD1" };
            this.lodMat2 = new Material(this.indirectMaterialBase) { name = "GpuGrassIndirect_LOD2" };
            this.lodMat2.EnableKeyword(KW_Lod2Billboard);
            this.lodMats = new Material?[] { this.lodMat0, this.lodMat1, this.lodMat2 };

            if (this.visibleLod0Buf != null) this.lodMat0.SetBuffer(ID_VisibleIndices, this.visibleLod0Buf);
            if (this.visibleLod1Buf != null) this.lodMat1.SetBuffer(ID_VisibleIndices, this.visibleLod1Buf);
            if (this.visibleLod2Buf != null) this.lodMat2.SetBuffer(ID_VisibleIndices, this.visibleLod2Buf);

            // Per-material static uniforms (legacy yaw-only orient, no authored offset).
            float interactorsFlag = this.interactorsEnabled ? 1f : 0f;
            foreach (Material? m in this.lodMats)
            {
                if (m == null) continue;
                m.SetFloat(ID_OrientMode, 0f);
                m.SetVector(ID_RotationOffsetEuler, Vector4.zero);
                m.SetFloat(ID_WindEnabled, 1f);
                m.SetFloat(ID_InteractorsEnabled, interactorsFlag);
                if (this.receiveShadows) m.EnableKeyword(KW_ReceiveShadows);
                else                     m.DisableKeyword(KW_ReceiveShadows);
            }

            // _Blades is PER-MATERIAL (per-field) — must NOT be a shared global (a second field's Submit
            // would clobber it on the deferred indirect draw). _Interactors is genuinely shared → global.
            if (this.bladeBuffer.BladeBuffer != null)
                this.SetLodBuffer(ID_Blades, this.bladeBuffer.BladeBuffer);
            Shader.SetGlobalBuffer(ID_Interactors, this.interactorBuffer.Buffer);
            this.SetLodFloat(ID_ScaleMax2, this.bladeBuffer.ScaleMax2);
            this.SetLodFloat(ID_ScaleFactor, 1f);

            this.PushInvariantWindUniforms();

            this.cullCmd = new CommandBuffer { name = "GpuGrassRenderer.Cull" };
            this.isBuilt = true;
        }

        // ── IGpuGrassRenderer : Step ──────────────────────────────────────────

        /// <inheritdoc/>
        public void Step(float dt)
        {
            this.time += dt; // deform is fully GPU-side; CPU only advances wind time.
        }

        /// <inheritdoc/>
        public void SetDensity(float normalized01)
        {
            // Map density fraction → BladeCull threshold (0..256). The compute keeps a blade when
            // (hash % 256) < densityThreshold, so threshold == fraction × 256 keeps that fraction of blades.
            // Clamp ≥1 so we never request an all-skip dispatch.
            this.densityThreshold = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(normalized01) * 256f), 1, 256);
        }

        // ── IGpuGrassRenderer : Submit ────────────────────────────────────────

        /// <inheritdoc/>
        public void Submit(Camera? targetCamera, Vector3 lodReferencePos)
        {
            if (!this.isBuilt || this.bladeBuffer == null || this.cullCmd == null) return;
            if (this.bladeBuffer.BladeBuffer == null ||
                this.bladeBuffer.AabbBuffer  == null ||
                this.bladeBuffer.RangeBuffer == null ||
                this.bladeBuffer.TotalChunks <= 0) return;

            // Cull against the actual render camera; fall back to Camera.main on the all-cameras path.
            Camera? cullCam = targetCamera ?? Camera.main;
            if (cullCam == null) return;

            GeometryUtility.CalculateFrustumPlanes(cullCam, this.planeScratch);

            // Whole-field gate: the cull dispatch + 3 indirect draws run unconditionally every Submit
            // (URP frustum-culls only rasterization, never the compute), so skip the entire chain when
            // this field's conservative AABB is fully outside the frustum.
            if (!GeometryUtility.TestPlanesAABB(this.planeScratch, this.worldBounds))
                return;

            for (int i = 0; i < 6; ++i)
                this.frustumPlanes[i] = new Vector4(
                    this.planeScratch[i].normal.x, this.planeScratch[i].normal.y,
                    this.planeScratch[i].normal.z, this.planeScratch[i].distance);

            // ── Two-pass cull ────────────────────────────────────────────────
            this.cullCmd.Clear();
            this.RecordFrameCommands(this.cullCmd, cullCam);
            Graphics.ExecuteCommandBuffer(this.cullCmd);

            // ── Per-frame uniforms ───────────────────────────────────────────
            this.SetLodFloat(ID_GrassTime, this.time);
#if UNITY_EDITOR
            // Editor: re-push invariant uniforms each frame for live-edit + domain-reload recovery.
            this.PushInvariantWindUniforms();
            if (this.bladeBuffer.BladeBuffer != null) this.SetLodBuffer(ID_Blades, this.bladeBuffer.BladeBuffer);
            if (this.interactorBuffer?.Buffer != null) Shader.SetGlobalBuffer(ID_Interactors, this.interactorBuffer.Buffer);
            this.SetLodFloat(ID_ScaleMax2, this.bladeBuffer.ScaleMax2);
            this.SetLodFloat(ID_ScaleFactor, 1f);
            // Re-snapshot occlusion flag for live-edit recovery (the config may have changed).
            // NOTE: Build() is the canonical snapshot; this just keeps the field in sync during editing.
#endif
            Vector3 camPos = cullCam.transform.position;
            Shader.SetGlobalVector(ID_CamPosWS, new Vector4(camPos.x, camPos.y, camPos.z, 0f));

            // Upload interactor + trail registries (single place — always fresh for the draw).
            this.interactorBuffer?.Upload(GrassInteractor.Active);
            Shader.SetGlobalInteger(ID_InteractorCount, this.interactorBuffer?.Count ?? 0);
            if (this.trailEnabled) this.trailBuffer?.Upload(GrassTrailInteractor.Active);

            // ── RenderMeshIndirect ×3 (skip absent LOD meshes) ───────────────
            if (this.mesh0 != null && this.lodMat0 != null && this.argsLod0Buf != null)
                Graphics.RenderMeshIndirect(this.MakeRenderParams(this.lodMat0, targetCamera), this.mesh0, this.argsLod0Buf, 1, 0);
            if (this.mesh1 != null && this.lodMat1 != null && this.argsLod1Buf != null)
                Graphics.RenderMeshIndirect(this.MakeRenderParams(this.lodMat1, targetCamera), this.mesh1, this.argsLod1Buf, 1, 0);
            if (this.mesh2 != null && this.lodMat2 != null && this.argsLod2Buf != null)
                Graphics.RenderMeshIndirect(this.MakeRenderParams(this.lodMat2, targetCamera), this.mesh2, this.argsLod2Buf, 1, 0);
        }

        /// <inheritdoc/>
        public Bounds WorldBounds => this.worldBounds;

        // ── Internals ─────────────────────────────────────────────────────────

        private RenderParams MakeRenderParams(Material mat, Camera? drawCamera)
        {
            // RenderParams MUST be built via the Material ctor: the object-initializer form leaves
            // renderingLayerMask = 0, which makes URP SKIP THE DRAW. The ctor sets the project default.
            return new RenderParams(mat)
            {
                worldBounds       = this.worldBounds,
                shadowCastingMode = this.shadowCastingMode,
                receiveShadows    = this.receiveShadows,
                camera            = drawCamera, // null = all cameras (play); a camera = edit-mode per-camera submit
                layer             = 0,
            };
        }

        private void PushInvariantWindUniforms()
        {
            this.SetLodVector(ID_WindDir,        new Vector4(this.windDir.x, this.windDir.y, 0f, 0f));
            this.SetLodFloat (ID_WindStrength,   this.windStrength);
            this.SetLodFloat (ID_WindFrequency,  this.windFrequency);
            this.SetLodFloat (ID_WindNoiseScale, this.windNoiseScale);
            this.SetLodFloat (ID_BendStrength,   this.bendStrength);
            this.SetLodFloat (ID_Flatten,        this.flatten);
        }

        private void SetLodFloat(int id, float v)
        {
            if (this.lodMat0 != null) this.lodMat0.SetFloat(id, v);
            if (this.lodMat1 != null) this.lodMat1.SetFloat(id, v);
            if (this.lodMat2 != null) this.lodMat2.SetFloat(id, v);
        }

        private void SetLodVector(int id, Vector4 v)
        {
            if (this.lodMat0 != null) this.lodMat0.SetVector(id, v);
            if (this.lodMat1 != null) this.lodMat1.SetVector(id, v);
            if (this.lodMat2 != null) this.lodMat2.SetVector(id, v);
        }

        private void SetLodBuffer(int id, GraphicsBuffer buf)
        {
            if (this.lodMat0 != null) this.lodMat0.SetBuffer(id, buf);
            if (this.lodMat1 != null) this.lodMat1.SetBuffer(id, buf);
            if (this.lodMat2 != null) this.lodMat2.SetBuffer(id, buf);
        }

        /// <summary>
        /// Records the two-pass cull: reset append counters → ChunkCull → CopyCount → WriteArgsB →
        /// BladeCull (indirect) → CopyCount×3 into the per-LOD draw args. Density is fixed at full
        /// (<see cref="FULL_DENSITY_THRESHOLD"/>) — GPUGrass has no adaptive-density controller.
        /// </summary>
        private void RecordFrameCommands(CommandBuffer cmd, Camera cam)
        {
            if (this.bladeBuffer?.AabbBuffer == null || this.bladeBuffer.BladeBuffer == null ||
                this.bladeBuffer.RangeBuffer == null ||
                this.visibleChunksBuf == null || this.visibleCountBuf == null || this.dispatchArgsBuf == null ||
                this.visibleLod0Buf == null || this.visibleLod1Buf == null || this.visibleLod2Buf == null ||
                this.argsLod0Buf == null || this.argsLod1Buf == null || this.argsLod2Buf == null)
                return;

            int chunkCount = this.bladeBuffer.TotalChunks;
            Vector3 camPos = cam.transform.position;

            // Reset ALL append counters before any dispatch.
            this.visibleChunksBuf.SetCounterValue(0);
            this.visibleLod0Buf.SetCounterValue(0);
            this.visibleLod1Buf.SetCounterValue(0);
            this.visibleLod2Buf.SetCounterValue(0);

            // ── Pass A: ChunkCull ────────────────────────────────────────────
            cmd.SetComputeBufferParam(this.computeShader, this.kernelCull, "chunkAabbs", this.bladeBuffer.AabbBuffer);
            cmd.SetComputeIntParam(this.computeShader, "chunkCount", chunkCount);
            cmd.SetComputeVectorArrayParam(this.computeShader, "frustumPlanes", this.frustumPlanes);
            cmd.SetComputeVectorParam(this.computeShader, "camPosWS", new Vector4(camPos.x, camPos.y, camPos.z, 0f));
            cmd.SetComputeFloatParam(this.computeShader, "maxCullSqrDistance", this.maxSqrDistance);
            cmd.SetComputeBufferParam(this.computeShader, this.kernelCull, "visibleChunks", this.visibleChunksBuf);

            // ── Hi-Z occlusion bindings (Phase P3) ──────────────────────────
            // Fetch the per-camera Hi-Z state (built by GpuGrassHiZFeature this frame).
            // If unavailable or not yet ready → set occlusionEnabled=0 (fail-open).
            // occlusionEnabled=0 makes ChunkCull behave byte-identically to Phase 2.
            // Only touch the per-camera Hi-Z registry when occlusion is enabled — otherwise
            // GetOrCreate would allocate a dictionary entry per camera every frame for nothing.
            GpuGrassHiZ? hiZ = this.enableOcclusionCulling ? GpuGrassHiZ.GetOrCreate(cam) : null;
            bool useOcclusion = hiZ != null && hiZ.IsReady && hiZ.Pyramid != null;
            cmd.SetComputeIntParam(this.computeShader, ID_OcclusionEnabled, useOcclusion ? 1 : 0);
            if (useOcclusion && hiZ!.Pyramid != null)
            {
                cmd.SetComputeMatrixParam(this.computeShader, ID_PrevViewProj, hiZ.PrevViewProj);
                cmd.SetComputeTextureParam(this.computeShader, this.kernelCull, ID_HiZ, hiZ.Pyramid);
                cmd.SetComputeVectorParam(this.computeShader, ID_HiZSize,
                    new Vector2(hiZ.BaseWidth, hiZ.BaseHeight));
                cmd.SetComputeIntParam(this.computeShader, ID_HiZMipCount, hiZ.MipCount);
            }
            else
            {
                // Bind a 1×1 fallback texture so the kernel still has a valid resource even when
                // occlusionEnabled=0 (avoids a missing-resource GPU error on some drivers).
                cmd.SetComputeTextureParam(this.computeShader, this.kernelCull, ID_HiZ,
                    Texture2D.blackTexture);
                cmd.SetComputeVectorParam(this.computeShader, ID_HiZSize, new Vector2(1f, 1f));
                cmd.SetComputeIntParam(this.computeShader, ID_HiZMipCount, 1);
            }
            // ── [END Hi-Z bindings] ──────────────────────────────────────────

            int cullGroups = Mathf.CeilToInt(chunkCount / 64f);
            cmd.DispatchCompute(this.computeShader, this.kernelCull, cullGroups, 1, 1);
            cmd.CopyCounterValue(this.visibleChunksBuf, this.visibleCountBuf, 0);

            // WriteArgsB: visibleCount → [groupsX,1,1] for the indirect BladeCull dispatch.
            cmd.SetComputeBufferParam(this.computeShader, this.kernelArgs, "visibleCount", this.visibleCountBuf);
            cmd.SetComputeBufferParam(this.computeShader, this.kernelArgs, "dispatchArgsB", this.dispatchArgsBuf);
            cmd.DispatchCompute(this.computeShader, this.kernelArgs, 1, 1, 1);

            // ── Pass B: BladeCull (one group per visible chunk) ──────────────
            int k = this.kernelBlade;
            cmd.SetComputeBufferParam(this.computeShader, k, "blades", this.bladeBuffer.BladeBuffer);
            cmd.SetComputeBufferParam(this.computeShader, k, "chunkRanges", this.bladeBuffer.RangeBuffer);
            cmd.SetComputeBufferParam(this.computeShader, k, "visibleChunksRead", this.visibleChunksBuf);
            cmd.SetComputeBufferParam(this.computeShader, k, "visibleCount", this.visibleCountBuf);
            cmd.SetComputeFloatParam(this.computeShader, "lod0MaxSqrDist", this.lod0MaxSqrDist);
            cmd.SetComputeFloatParam(this.computeShader, "lod1MaxSqrDist", this.lod1MaxSqrDist);
            cmd.SetComputeFloatParam(this.computeShader, "bladeCullMargin", this.bladeCullMargin);
            cmd.SetComputeIntParam(this.computeShader, "densityThreshold", this.densityThreshold);
            cmd.SetComputeBufferParam(this.computeShader, k, "visibleLod0", this.visibleLod0Buf);
            cmd.SetComputeBufferParam(this.computeShader, k, "visibleLod1", this.visibleLod1Buf);
            cmd.SetComputeBufferParam(this.computeShader, k, "visibleLod2", this.visibleLod2Buf);

            cmd.DispatchCompute(this.computeShader, k, this.dispatchArgsBuf, 0u);

            // CopyCount → instanceCount field of each LOD's indirect draw args.
            cmd.CopyCounterValue(this.visibleLod0Buf, this.argsLod0Buf, ARGS_INSTANCE_COUNT_OFFSET);
            cmd.CopyCounterValue(this.visibleLod1Buf, this.argsLod1Buf, ARGS_INSTANCE_COUNT_OFFSET);
            cmd.CopyCounterValue(this.visibleLod2Buf, this.argsLod2Buf, ARGS_INSTANCE_COUNT_OFFSET);
        }

        private void InitLodArgsFromMeshes()
        {
            (Mesh? mesh, GraphicsBuffer? buf)[] lodPairs =
            {
                (this.mesh0, this.argsLod0Buf),
                (this.mesh1, this.argsLod1Buf),
                (this.mesh2, this.argsLod2Buf),
            };

            foreach (var (mesh, buf) in lodPairs)
            {
                if (buf == null) continue;
                if (mesh != null && mesh.GetIndexCount(0) == 0)
                    Debug.LogError($"[GpuGrassRenderer] LOD mesh '{mesh.name}' has 0 indices — its draw renders nothing.");

                var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
                args[0].indexCountPerInstance = (mesh != null) ? mesh.GetIndexCount(0) : 0;
                args[0].instanceCount         = 0;
                args[0].startIndex            = (mesh != null) ? mesh.GetIndexStart(0) : 0;
                args[0].baseVertexIndex       = (mesh != null) ? (uint)mesh.GetBaseVertex(0) : 0;
                args[0].startInstance         = 0;
                buf.SetData(args);
            }
        }

        /// <summary>
        /// Converts LOD switch distances + cull distance into squared thresholds. Missing switch entries
        /// extend the last present LOD to the cull boundary (cull≤0 → no cap) so blades never bucket into
        /// an empty (mesh-less) LOD slot. Standalone copy of WorldPainter's <c>LodCullMath.Thresholds</c>.
        /// </summary>
        internal static (float lod0Sqr, float lod1Sqr, float maxSqr) LodThresholds(float[]? switches, float cull)
        {
            float maxSqr = cull * cull;
            float farSqr = cull > 0f ? maxSqr : float.MaxValue;
            float l0 = switches != null && switches.Length > 0 ? switches[0] * switches[0] : farSqr;
            float l1 = switches != null && switches.Length > 1 ? switches[1] * switches[1] : farSqr;
            return (l0, l1, maxSqr);
        }

        // ── IGpuGrassRenderer : Dispose ───────────────────────────────────────

        /// <inheritdoc/>
        public void Dispose()
        {
            this.cullCmd?.Dispose(); this.cullCmd = null;

            this.visibleChunksBuf?.Release(); this.visibleChunksBuf = null;
            this.visibleCountBuf?.Release();  this.visibleCountBuf  = null;
            this.dispatchArgsBuf?.Release();  this.dispatchArgsBuf  = null;
            this.visibleLod0Buf?.Release(); this.visibleLod0Buf = null;
            this.visibleLod1Buf?.Release(); this.visibleLod1Buf = null;
            this.visibleLod2Buf?.Release(); this.visibleLod2Buf = null;
            this.argsLod0Buf?.Release(); this.argsLod0Buf = null;
            this.argsLod1Buf?.Release(); this.argsLod1Buf = null;
            this.argsLod2Buf?.Release(); this.argsLod2Buf = null;

            this.interactorBuffer?.Dispose(); this.interactorBuffer = null;
            this.trailBuffer?.Dispose(); this.trailBuffer = null;
            this.bladeBuffer?.Dispose(); this.bladeBuffer = null;

            SafeDestroy(this.lodMat0); this.lodMat0 = null;
            SafeDestroy(this.lodMat1); this.lodMat1 = null;
            SafeDestroy(this.lodMat2); this.lodMat2 = null;
            this.lodMats = Array.Empty<Material?>();

            this.isBuilt = false;
        }

        /// <summary>Destroys a Unity object safely in both play and edit mode ([ExecuteAlways] rebuilds run in edit).</summary>
        private static void SafeDestroy(UnityEngine.Object? o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
