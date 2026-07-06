#nullable enable
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPUGrass
{
    /// <summary>
    /// The GPU-driven indirect render tier for GPUGrass — the standalone, WorldPainter-free extraction of
    /// <c>GrassGpuEngine</c>. Builds a chunked blade buffer from the editor bake, runs a multi-pass GPU cull
    /// (compute: ChunkCull → WriteArgsB → BladeCullCount → WriteLodOffsets → BladeCullScatter — see
    /// <see cref="RecordFrameCommands"/> and GrassCull.compute), then issues one
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
        private static readonly int ID_LodOffsets          = Shader.PropertyToID("_LodOffsets");
        private static readonly int ID_LodIndex            = Shader.PropertyToID("_LodIndex");
        private static readonly int ID_OrientMode          = Shader.PropertyToID("_OrientMode");
        private static readonly int ID_RotationOffsetEuler = Shader.PropertyToID("_RotationOffsetEuler");
        private static readonly int ID_WindEnabled         = Shader.PropertyToID("_WindEnabled");
        private static readonly int ID_InteractorsEnabled  = Shader.PropertyToID("_InteractorsEnabled");
        private const string KW_ReceiveShadows             = "_RECEIVE_SHADOWS";
        private const string KW_Lod2Billboard              = "_LOD2_BILLBOARD";
        private const string KW_Alphaclip                  = "_ALPHACLIP";
        private const string KW_AlphaclipShadows           = "_ALPHACLIP_SHADOWS";
        private static readonly int ID_Alphaclip           = Shader.PropertyToID("_Alphaclip");
        private static readonly int ID_AlphaclipShadows    = Shader.PropertyToID("_AlphaclipShadows");

        // ── Injected ──────────────────────────────────────────────────────────
        private readonly ComputeShader computeShader;
        private readonly Material indirectMaterialBase;

        // ── Kernel indices ────────────────────────────────────────────────────
        private readonly int kernelCull;
        private readonly int kernelArgs;
        private readonly int kernelBladeCount;
        private readonly int kernelWriteLodOffsets;
        private readonly int kernelBladeScatter;

        // ── Per-frame cull buffers (Pass A) ───────────────────────────────────
        private GraphicsBuffer? visibleChunksBuf;
        private GraphicsBuffer? visibleCountBuf;
        private GraphicsBuffer? dispatchArgsBuf;

        // ── Merged per-LOD visible-index buffer (Pass B, mobile #5+#6) ────────
        // ONE packed buffer (2-bit LOD tag in the high bits of each uint index) replaces the three
        // bladeCap-sized append buffers — 4 B/blade scratch instead of 12 B/blade. lodCounts/lodOffsets/
        // lodCursor are tiny (3 uints each) — see GrassCull.compute for the count→offset→scatter pipeline.
        private GraphicsBuffer? visibleBladesBuf;
        private GraphicsBuffer? lodCountsBuf;
        private GraphicsBuffer? lodOffsetsBuf;
        private GraphicsBuffer? lodCursorBuf;

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
            this.kernelCull            = computeShader.FindKernel("ChunkCull");
            this.kernelArgs            = computeShader.FindKernel("WriteArgsB");
            this.kernelBladeCount      = computeShader.FindKernel("BladeCullCount");
            this.kernelWriteLodOffsets = computeShader.FindKernel("WriteLodOffsets");
            this.kernelBladeScatter    = computeShader.FindKernel("BladeCullScatter");
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

            // The merged visible-index buffer packs a 2-bit LOD tag into the top bits of each blade index
            // (GpuGrassLodTag / GrassCull.compute BladeCullScatter), leaving 30 bits for the index. Past that
            // the pack silently masks the index into the tag bits → wrong-mesh/position blades with no error.
            // Practically unreachable (2^30 blades ≈ a 21 GB blade buffer), but fail LOUD, not silently.
            if (this.bladeBuffer.TotalBlades > GpuGrassLodTag.INDEX_MASK)
                Debug.LogError($"[GpuGrassRenderer] Bake has {this.bladeBuffer.TotalBlades} blades, exceeding the " +
                               $"{GpuGrassLodTag.INDEX_MASK} (2^30-1) LOD-tag index limit — packed indices will corrupt. " +
                               "Reduce density or split the terrain.");

            this.visibleChunksBuf = new GraphicsBuffer(GraphicsBuffer.Target.Append, chunkCap, sizeof(uint));
            this.visibleCountBuf  = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            this.dispatchArgsBuf  = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint));

            // Merged per-LOD visible-index buffer (mobile #5+#6): ONE bladeCap-sized buffer (4 B/blade)
            // instead of three (12 B/blade) — the sum of all three LOD partitions can never exceed
            // TotalBlades, so a single shared allocation always covers every distribution.
            this.visibleBladesBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bladeCap, sizeof(uint));
            this.lodCountsBuf   = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint));
            this.lodOffsetsBuf  = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint));
            this.lodCursorBuf   = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint));

            int argsStride = GraphicsBuffer.IndirectDrawIndexedArgs.size;
            // IndirectArguments | Structured: still consumed by RenderMeshIndirect as draw-args, but ALSO
            // compute-writable (WriteLodOffsets writes instanceCount directly — no per-LOD hidden Append
            // counter exists anymore to CopyCounterValue from).
            const GraphicsBuffer.Target argsTarget = GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured;
            this.argsLod0Buf = new GraphicsBuffer(argsTarget, argsStride / sizeof(uint), sizeof(uint));
            this.argsLod1Buf = new GraphicsBuffer(argsTarget, argsStride / sizeof(uint), sizeof(uint));
            this.argsLod2Buf = new GraphicsBuffer(argsTarget, argsStride / sizeof(uint), sizeof(uint));
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

            // _VisibleIndices is now the SAME shared packed buffer for all 3 LOD materials — each material's
            // _LodIndex constant (set below) tells the vertex shader which _LodOffsets[] entry is its
            // partition's base offset into that shared buffer.
            if (this.visibleBladesBuf != null) this.SetLodBuffer(ID_VisibleIndices, this.visibleBladesBuf);
            if (this.lodOffsetsBuf != null)    this.SetLodBuffer(ID_LodOffsets, this.lodOffsetsBuf);

            // Per-material static uniforms (legacy yaw-only orient, no authored offset).
            float interactorsFlag = this.interactorsEnabled ? 1f : 0f;
            // Alpha-clip is a [Toggle(_ALPHACLIP)] property: its FLOAT and its KEYWORD are decoupled. The
            // clones from `new Material(base)` inherit only whatever keyword the base had enabled — but any
            // path that set the float via SetFloat (mobile preset, authoring, re-import) leaves the keyword
            // OFF, so the `#if defined(_ALPHACLIP)` clip compiles out and cutout grass renders as solid quads.
            // Make the float the SSOT: sync the keyword onto every draw clone from the base material's float.
            bool alphaClip        = this.indirectMaterialBase.GetFloat(ID_Alphaclip)        > 0.5f;
            bool alphaClipShadows = this.indirectMaterialBase.GetFloat(ID_AlphaclipShadows) > 0.5f;
            for (int i = 0; i < this.lodMats.Length; i++)
            {
                Material? m = this.lodMats[i];
                if (m == null) continue;
                m.SetFloat(ID_OrientMode, 0f);
                m.SetVector(ID_RotationOffsetEuler, Vector4.zero);
                m.SetFloat(ID_WindEnabled, 1f);
                m.SetFloat(ID_InteractorsEnabled, interactorsFlag);
                m.SetFloat(ID_LodIndex, i); // 0/1/2 — selects this material's partition in _LodOffsets
                if (this.receiveShadows) m.EnableKeyword(KW_ReceiveShadows);
                else                     m.DisableKeyword(KW_ReceiveShadows);
                if (alphaClip) m.EnableKeyword(KW_Alphaclip);               else m.DisableKeyword(KW_Alphaclip);
                if (alphaClipShadows) m.EnableKeyword(KW_AlphaclipShadows); else m.DisableKeyword(KW_AlphaclipShadows);
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
        /// Records the multi-pass cull: reset append counter → ChunkCull → CopyCount → WriteArgsB (also
        /// zeroes lodCounts) → BladeCullCount (indirect, counts per-LOD survivors) → WriteLodOffsets
        /// (prefix-sum → per-LOD base offsets + seeds the scatter cursor + writes indirect instanceCounts)
        /// → BladeCullScatter (indirect, SAME dispatch as Count — writes packed indices into their LOD's
        /// partition of the single shared visibleBlades buffer). Mobile #5+#6 — see GrassCull.compute.
        /// Density is fixed at full (<see cref="FULL_DENSITY_THRESHOLD"/>) — GPUGrass has no
        /// adaptive-density controller.
        /// </summary>
        private void RecordFrameCommands(CommandBuffer cmd, Camera cam)
        {
            if (this.bladeBuffer?.AabbBuffer == null || this.bladeBuffer.BladeBuffer == null ||
                this.bladeBuffer.RangeBuffer == null ||
                this.visibleChunksBuf == null || this.visibleCountBuf == null || this.dispatchArgsBuf == null ||
                this.visibleBladesBuf == null || this.lodCountsBuf == null || this.lodOffsetsBuf == null ||
                this.lodCursorBuf == null ||
                this.argsLod0Buf == null || this.argsLod1Buf == null || this.argsLod2Buf == null)
                return;

            int chunkCount = this.bladeBuffer.TotalChunks;
            Vector3 camPos = cam.transform.position;

            // Reset the chunk-append counter before any dispatch. lodCounts is zeroed inside WriteArgsB
            // (below) instead of here — it needs no separate reset call.
            this.visibleChunksBuf.SetCounterValue(0);

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

            // WriteArgsB: visibleCount → [groupsX,1,1] for the indirect BladeCullCount/Scatter dispatches;
            // also zeroes lodCounts before BladeCullCount accumulates into it this frame.
            cmd.SetComputeBufferParam(this.computeShader, this.kernelArgs, "visibleCount", this.visibleCountBuf);
            cmd.SetComputeBufferParam(this.computeShader, this.kernelArgs, "dispatchArgsB", this.dispatchArgsBuf);
            cmd.SetComputeBufferParam(this.computeShader, this.kernelArgs, "lodCounts", this.lodCountsBuf);
            cmd.DispatchCompute(this.computeShader, this.kernelArgs, 1, 1, 1);

            // ── Pass B1: BladeCullCount (one group per visible chunk) — counts per-LOD survivors ──
            int kCount = this.kernelBladeCount;
            cmd.SetComputeBufferParam(this.computeShader, kCount, "blades", this.bladeBuffer.BladeBuffer);
            cmd.SetComputeBufferParam(this.computeShader, kCount, "chunkRanges", this.bladeBuffer.RangeBuffer);
            cmd.SetComputeBufferParam(this.computeShader, kCount, "visibleChunksRead", this.visibleChunksBuf);
            cmd.SetComputeBufferParam(this.computeShader, kCount, "visibleCount", this.visibleCountBuf);
            cmd.SetComputeFloatParam(this.computeShader, "lod0MaxSqrDist", this.lod0MaxSqrDist);
            cmd.SetComputeFloatParam(this.computeShader, "lod1MaxSqrDist", this.lod1MaxSqrDist);
            cmd.SetComputeFloatParam(this.computeShader, "bladeCullMargin", this.bladeCullMargin);
            cmd.SetComputeIntParam(this.computeShader, "densityThreshold", this.densityThreshold);
            cmd.SetComputeBufferParam(this.computeShader, kCount, "lodCounts", this.lodCountsBuf);

            cmd.DispatchCompute(this.computeShader, kCount, this.dispatchArgsBuf, 0u);

            // ── Pass B2: WriteLodOffsets (single-thread) — prefix-sum offsets, seed scatter cursor, ──
            // ── write each LOD's instanceCount into its indirect draw-args buffer. ──────────────────
            int kOffsets = this.kernelWriteLodOffsets;
            cmd.SetComputeBufferParam(this.computeShader, kOffsets, "lodCounts", this.lodCountsBuf);
            cmd.SetComputeBufferParam(this.computeShader, kOffsets, "lodOffsets", this.lodOffsetsBuf);
            cmd.SetComputeBufferParam(this.computeShader, kOffsets, "lodCursor", this.lodCursorBuf);
            cmd.SetComputeBufferParam(this.computeShader, kOffsets, "argsLod0", this.argsLod0Buf);
            cmd.SetComputeBufferParam(this.computeShader, kOffsets, "argsLod1", this.argsLod1Buf);
            cmd.SetComputeBufferParam(this.computeShader, kOffsets, "argsLod2", this.argsLod2Buf);
            cmd.DispatchCompute(this.computeShader, kOffsets, 1, 1, 1);

            // ── Pass B3: BladeCullScatter — SAME indirect dispatch args as BladeCullCount (identical ──
            // ── chunk/thread assignment, so counts and writes always agree). Scatter-writes packed ────
            // ── (lod<<30 | index) into visibleBlades. ───────────────────────────────────────────────
            int kScatter = this.kernelBladeScatter;
            cmd.SetComputeBufferParam(this.computeShader, kScatter, "blades", this.bladeBuffer.BladeBuffer);
            cmd.SetComputeBufferParam(this.computeShader, kScatter, "chunkRanges", this.bladeBuffer.RangeBuffer);
            cmd.SetComputeBufferParam(this.computeShader, kScatter, "visibleChunksRead", this.visibleChunksBuf);
            cmd.SetComputeBufferParam(this.computeShader, kScatter, "visibleCount", this.visibleCountBuf);
            cmd.SetComputeBufferParam(this.computeShader, kScatter, "lodCursor", this.lodCursorBuf);
            cmd.SetComputeBufferParam(this.computeShader, kScatter, "visibleBlades", this.visibleBladesBuf);

            cmd.DispatchCompute(this.computeShader, kScatter, this.dispatchArgsBuf, 0u);
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

                // Written as a raw uint[5] (not the IndirectDrawIndexedArgs struct) because the buffer is
                // now declared with stride=sizeof(uint) (IndirectArguments|Structured, so WriteLodOffsets
                // can compute-write instanceCount directly) — layout: [0]=indexCountPerInstance
                // [1]=instanceCount [2]=startIndex [3]=baseVertexIndex [4]=startInstance.
                var args = new uint[5];
                args[0] = (mesh != null) ? mesh.GetIndexCount(0) : 0;
                args[1] = 0; // instanceCount — written per-frame by WriteLodOffsets
                args[2] = (mesh != null) ? mesh.GetIndexStart(0) : 0;
                args[3] = (mesh != null) ? (uint)mesh.GetBaseVertex(0) : 0;
                args[4] = 0;
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
            this.visibleBladesBuf?.Release(); this.visibleBladesBuf = null;
            this.lodCountsBuf?.Release();  this.lodCountsBuf  = null;
            this.lodOffsetsBuf?.Release(); this.lodOffsetsBuf = null;
            this.lodCursorBuf?.Release();  this.lodCursorBuf  = null;
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
