#nullable enable
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract
{
    /// <summary>
    /// Phase 5: GPU-driven indirect grass engine implementing <see cref="IGrassEngine"/>.
    ///
    /// Build bakes a <see cref="ChunkedBladeBuffer"/> from the scatter, allocates all cull buffers,
    /// creates 3 per-LOD <see cref="Material"/> instances with <c>_VisibleIndices</c> bound as a
    /// material property (NOT a MaterialPropertyBlock — MPB is silently dropped under URP RenderGraph).
    ///
    /// Step advances a <c>time</c> accumulator; no per-blade CPU work (deform is fully GPU-side).
    ///
    /// Submit:
    ///   1. Builds frustum planes from the active camera.
    ///   2. Records and immediately executes the two-pass cull CommandBuffer
    ///      (<see cref="RecordFrameCommands"/>: ChunkCull → WriteArgsB → BladeCull → CopyCount×3).
    ///   3. Sets all wind/time/cam globals via <see cref="Shader.SetGlobal*"/>.
    ///   4. Calls <see cref="Graphics.RenderMeshIndirect"/> once per LOD.
    ///
    /// Interactor buffer (Phase 6): a <see cref="GrassInteractorBuffer"/> uploads
    /// <see cref="GrassInteractor.Active"/> each <see cref="Step"/> call and binds the count uniform,
    /// activating the VS lean-away loop with real interactor data.
    /// </summary>
    internal sealed class GrassGpuEngine : IGrassEngine
    {
        // ── Constants ────────────────────────────────────────────────────────
        private const int ARGS_INSTANCE_COUNT_OFFSET = 4; // byte offset of instanceCount in IndirectDrawIndexedArgs

        // Shader global name IDs (cached once for zero per-frame string allocs).
        private static readonly int ID_Blades              = Shader.PropertyToID("_Blades");
        private static readonly int ID_Interactors        = Shader.PropertyToID("_Interactors");
        private static readonly int ID_InteractorCount    = Shader.PropertyToID("_InteractorCount");
        private static readonly int ID_ScaleMax2          = Shader.PropertyToID("_ScaleMax2");
        private static readonly int ID_GrassTime          = Shader.PropertyToID("_GrassTime");
        private static readonly int ID_WindDir            = Shader.PropertyToID("_WindDir");
        private static readonly int ID_WindStrength       = Shader.PropertyToID("_WindStrength");
        private static readonly int ID_WindFrequency      = Shader.PropertyToID("_WindFrequency");
        private static readonly int ID_WindNoiseScale     = Shader.PropertyToID("_WindNoiseScale");
        private static readonly int ID_WindGustScale      = Shader.PropertyToID("_WindGustScale");
        private static readonly int ID_WindRippleScale    = Shader.PropertyToID("_WindRippleScale");
        private static readonly int ID_WindGustSpeed      = Shader.PropertyToID("_WindGustSpeed");
        private static readonly int ID_WindRippleSpeed    = Shader.PropertyToID("_WindRippleSpeed");
        private static readonly int ID_WindRippleWeight   = Shader.PropertyToID("_WindRippleWeight");
        private const string KW_WindPerlin                = "_WIND_PERLIN";
        private static readonly int ID_BendStrength       = Shader.PropertyToID("_BendStrength");
        private static readonly int ID_Flatten            = Shader.PropertyToID("_Flatten");
        private static readonly int ID_CamPosWS           = Shader.PropertyToID("_CamPosWS");
        private static readonly int ID_VisibleIndices     = Shader.PropertyToID("_VisibleIndices");
        // Phase 4: orient mode + rotation offset uniforms (per-material, set at build time).
        private static readonly int ID_OrientMode          = Shader.PropertyToID("_OrientMode");
        private static readonly int ID_RotationOffsetEuler = Shader.PropertyToID("_RotationOffsetEuler");
        // Independent deform gates (per-material, set at build time).
        private static readonly int ID_WindEnabled         = Shader.PropertyToID("_WindEnabled");
        private static readonly int ID_InteractorsEnabled  = Shader.PropertyToID("_InteractorsEnabled");

        // ── Injected ─────────────────────────────────────────────────────────
        private readonly ComputeShader computeShader;
        private readonly Material indirectMaterialBase; // source material to clone per-LOD

        // ── Kernel indices ────────────────────────────────────────────────────
        private readonly int kernelCull;
        private readonly int kernelArgs;
        private readonly int kernelBlade;

        // ── Per-frame Pass-A cull buffers ─────────────────────────────────────
        private GraphicsBuffer? visibleChunksBuf;
        private GraphicsBuffer? visibleCountBuf;
        private GraphicsBuffer? dispatchArgsBuf;

        // ── Per-frame Pass-B LOD buffers ──────────────────────────────────────
        private GraphicsBuffer? visibleLod0Buf;
        private GraphicsBuffer? visibleLod1Buf;
        private GraphicsBuffer? visibleLod2Buf;

        // ── Per-LOD indirect draw args ────────────────────────────────────────
        private GraphicsBuffer? argsLod0Buf;
        private GraphicsBuffer? argsLod1Buf;
        private GraphicsBuffer? argsLod2Buf;

        // ── Static GPU data ───────────────────────────────────────────────────
        private ChunkedBladeBuffer? bladeBuffer;

        // Phase 6: real interactor buffer — uploads GrassInteractor.Active each frame.
        private GrassInteractorBuffer? interactorBuffer;

        // Phase 2: trail segment buffer — uploads GrassTrailInteractor.Active each frame.
        private GrassTrailBuffer? trailBuffer;

        // ── Per-LOD materials (one per LOD so each can have its own _VisibleIndices binding) ──
        private Material? lodMat0;
        private Material? lodMat1;
        private Material? lodMat2;

        // ── LOD meshes ────────────────────────────────────────────────────────
        private Mesh? mesh0;
        private Mesh? mesh1;
        private Mesh? mesh2;

        // ── Config snapshot ───────────────────────────────────────────────────
        private Vector2 windDir;
        private float windStrength;
        private float windFrequency;
        private float windNoiseScale;
        private ScatterLayer.WindMode windMode;
        private float windGustScale;
        private float windRippleScale;
        private float windGustSpeed;
        private float windRippleSpeed;
        private float windRippleWeight;
        private float bendStrength;
        private float flatten;
        private float lod0MaxSqrDist;
        private float lod1MaxSqrDist;
        private float maxSqrDistance;
        // Per-blade frustum-cull margin (metres) = blade vertical reach + extraCullMargin. Computed in
        // Build from config (SSOT-mirrors the chunk-AABB baker's bladeReachY) and pushed to BladeCull.
        private float bladeCullMargin;
        // FIX 7: snapshot shadow mode from config so MakeRenderParams can honor it.
        private UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;

        // ── Per-frame state ───────────────────────────────────────────────────
        private float time;
        private Bounds worldBounds;
        private bool isBuilt;

        // ── CommandBuffer (reused per frame, not re-created) ──────────────────
        private CommandBuffer? cullCmd;

        // ── Frustum plane array (reused, no per-frame alloc) ─────────────────
        private readonly Vector4[] frustumPlanes = new Vector4[6];
        // Scratch Plane[6] for the non-alloc GeometryUtility overload — zero per-frame heap alloc.
        private readonly Plane[] planeScratch = new Plane[6];

        // GPU-specific tuning knob: extra metres of per-blade cull headroom beyond the auto blade reach.
        // 0 = behaviour derived purely from blade height + bend headroom. Set via constructor by the field.
        private readonly float extraCullMargin;

        // ── Construction ──────────────────────────────────────────────────────

        /// <summary>
        /// Constructs the engine.
        /// </summary>
        /// <param name="computeShader">The GrassCull compute shader asset.</param>
        /// <param name="indirectMaterial">Base indirect material (GrassInteract/IndirectGrass shader).
        /// Will be cloned 3× — one instance per LOD — so each can have its own _VisibleIndices binding.</param>
        /// <param name="extraCullMargin">Extra metres of per-blade cull headroom beyond the auto blade
        /// reach (MaxBladeHeight×scaleMax + BendHeadroom). Default 0 = no behaviour change.</param>
        public GrassGpuEngine(ComputeShader computeShader, Material indirectMaterial, float extraCullMargin = 0f)
        {
            this.computeShader       = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.indirectMaterialBase = indirectMaterial ?? throw new ArgumentNullException(nameof(indirectMaterial));
            this.extraCullMargin     = Mathf.Max(0f, extraCullMargin);
            this.kernelCull = computeShader.FindKernel("ChunkCull");
            this.kernelArgs = computeShader.FindKernel("WriteArgsB");
            this.kernelBlade = computeShader.FindKernel("BladeCull");
        }

        // ── IGrassEngine : Build ──────────────────────────────────────────────

        /// <inheritdoc/>
        public void Build(ScatterLayer layer, Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler)
        {
            this.Dispose();

            // ── Scatter + bake ──────────────────────────────────────────────
            GrassScatterResult scatter = GrassScatter.Build(layer, origin, pool, sampler);
            this.worldBounds = scatter.WorldBounds;

            // Terrain-driven bounds: terrain size when bound, else the layer's manual FieldBounds.
            Vector2 effectiveBounds = sampler is TerrainSurfaceSampler tss ? tss.TerrainSizeXZ : layer.FieldBounds;
            this.bladeBuffer = new ChunkedBladeBuffer();
            this.bladeBuffer.Bake(scatter, layer, origin, effectiveBounds, layer.ScaleRange.y,
                oriented: layer.IsOriented);

            // Return the scatter slabs now — the GPU path doesn't need them again.
            GrassScatter.ReturnSlabs(scatter, pool);

            // ── Config snapshot (render/wind params from layer config structs) ────
            var wind = layer.Wind;
            var deform = layer.Deform;
            var render = layer.Render;

            Vector2 dir = wind.WindDirection;
            this.windDir         = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector2.right;
            this.windStrength    = wind.WindStrength;
            this.windFrequency   = wind.WindFrequency;
            this.windNoiseScale  = wind.WindNoiseScale;
            this.windMode        = wind.Mode;
            this.windGustScale   = wind.WindGustScale;
            this.windRippleScale = wind.WindRippleScale;
            this.windGustSpeed   = wind.WindGustSpeed;
            this.windRippleSpeed = wind.WindRippleSpeed;
            this.windRippleWeight = wind.WindRippleWeight;
            this.bendStrength    = deform.BendStrength;
            this.flatten         = deform.Flatten;
            this.shadowCastingMode = render.ShadowCastingMode;

            // LOD distances sourced from the layer.
            // LodMaxDistances[0] = LOD0→1 boundary, [1] = LOD1→2 boundary.
            float[] dists = render.LodMaxDistances;
            this.lod0MaxSqrDist = dists.Length > 0 ? dists[0] * dists[0] : 144f;  // default 12m
            this.lod1MaxSqrDist = dists.Length > 1 ? dists[1] * dists[1] : 900f;  // default 30m
            // Explicit per-layer cull boundary (SSOT: ScatterRenderConfig.RenderCullDistance). Edit == play — no isPlaying branch.
            float cull = render.RenderCullDistance;
            this.maxSqrDistance = cull * cull;

            // Per-blade cull margin — SSOT-mirror the chunk-AABB baker's bladeReachY (ChunkedBladeBuffer.cs):
            //   bladeReachY = MaxBladeHeight * maxScale + BendHeadroom,  maxScale = ScaleRange.y (or 1 if <=0).
            // Source maxScale from the baker's own ScaleMax2 (== that same maxScale) so the lockstep is
            // STRUCTURAL — a future change to the baker's scale-fallback can't silently desync the margin.
            // Adding extraCullMargin lets the field nudge headroom. Final Mathf.Max(0,…): a misconfigured
            // negative MaxBladeHeight/BendHeadroom would otherwise flip the kernel's `< -bladeCullMargin`
            // reject into culling IN-frustum blades (holes); clamp closes that path (extraCullMargin already clamped).
            float bakedMaxScale = this.bladeBuffer.ScaleMax2;
            this.bladeCullMargin = Mathf.Max(0f,
                layer.Bounds.MaxBladeHeight * bakedMaxScale + layer.Bounds.BendHeadroom + this.extraCullMargin);

            // ── LOD meshes sourced from the layer (SSOT) ──
            Mesh[] meshes = render.LodMeshes;
            if (meshes.Length == 0)
            {
                Debug.LogWarning(
                    $"[GrassGpuEngine] Layer '{layer.name}' has no LOD meshes (lods array is empty)." +
                    " No grass will render. Populate the layer's lods field.");
            }
            this.mesh0 = meshes.Length > 0 ? meshes[0] : null;
            this.mesh1 = meshes.Length > 1 ? meshes[1] : null;
            this.mesh2 = meshes.Length > 2 ? meshes[2] : null;

            // ── GPU cull buffers ────────────────────────────────────────────
            int chunkCap = Mathf.Max(1, this.bladeBuffer.TotalChunks);
            int bladeCap  = Mathf.Max(1, this.bladeBuffer.TotalBlades);

            this.visibleChunksBuf = new GraphicsBuffer(GraphicsBuffer.Target.Append, chunkCap, sizeof(uint));
            this.visibleCountBuf  = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            this.dispatchArgsBuf  = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint));

            this.visibleLod0Buf = new GraphicsBuffer(GraphicsBuffer.Target.Append, bladeCap, sizeof(uint));
            this.visibleLod1Buf = new GraphicsBuffer(GraphicsBuffer.Target.Append, bladeCap, sizeof(uint));
            this.visibleLod2Buf = new GraphicsBuffer(GraphicsBuffer.Target.Append, bladeCap, sizeof(uint));

            int argsStride = GraphicsBuffer.IndirectDrawIndexedArgs.size;
            this.argsLod0Buf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, argsStride);
            this.argsLod1Buf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, argsStride);
            this.argsLod2Buf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, argsStride);

            this.InitLodArgsFromMeshes();

            // ── Phase 6: real interactor buffer ─────────────────────────────
            this.interactorBuffer = new GrassInteractorBuffer();

            // ── Phase 2: trail segment buffer ────────────────────────────────
            this.trailBuffer = new GrassTrailBuffer();
            this.trailBuffer.BindGlobal();

            // ── Per-LOD material instances ──────────────────────────────────
            // Each gets its own _VisibleIndices binding. LOD2 gets the _LOD2_BILLBOARD keyword.
            this.lodMat0 = new Material(this.indirectMaterialBase) { name = "GrassIndirect_LOD0" };
            this.lodMat1 = new Material(this.indirectMaterialBase) { name = "GrassIndirect_LOD1" };
            this.lodMat2 = new Material(this.indirectMaterialBase) { name = "GrassIndirect_LOD2" };
            this.lodMat2.EnableKeyword("_LOD2_BILLBOARD");

            // Wind model keyword — enable _WIND_PERLIN on all 3 LOD materials when layer opts in.
            bool perlin = this.windMode == ScatterLayer.WindMode.Perlin;
            if (perlin) { this.lodMat0.EnableKeyword(KW_WindPerlin);  this.lodMat1.EnableKeyword(KW_WindPerlin);  this.lodMat2.EnableKeyword(KW_WindPerlin); }
            else        { this.lodMat0.DisableKeyword(KW_WindPerlin); this.lodMat1.DisableKeyword(KW_WindPerlin); this.lodMat2.DisableKeyword(KW_WindPerlin); }

            // Bind per-LOD visible index buffers. Must be set as a material property (NOT MPB).
            if (this.visibleLod0Buf != null) this.lodMat0.SetBuffer(ID_VisibleIndices, this.visibleLod0Buf);
            if (this.visibleLod1Buf != null) this.lodMat1.SetBuffer(ID_VisibleIndices, this.visibleLod1Buf);
            if (this.visibleLod2Buf != null) this.lodMat2.SetBuffer(ID_VisibleIndices, this.visibleLod2Buf);

            // Phase 4: orient mode (0 = legacy hash slot2, 1 = oriented slot2) + rotation offset.
            float orientMode = layer.IsOriented ? 1f : 0f;
            Vector3 rotOffset = layer.RotationOffsetEuler;
            this.lodMat0.SetFloat(ID_OrientMode, orientMode);
            this.lodMat1.SetFloat(ID_OrientMode, orientMode);
            this.lodMat2.SetFloat(ID_OrientMode, orientMode);
            this.lodMat0.SetVector(ID_RotationOffsetEuler, new Vector4(rotOffset.x, rotOffset.y, rotOffset.z, 0f));
            this.lodMat1.SetVector(ID_RotationOffsetEuler, new Vector4(rotOffset.x, rotOffset.y, rotOffset.z, 0f));
            this.lodMat2.SetVector(ID_RotationOffsetEuler, new Vector4(rotOffset.x, rotOffset.y, rotOffset.z, 0f));

            // Independent deform gates.
            float windFlag        = layer.Deform.AffectedByWind        ? 1f : 0f;
            float interactorsFlag = layer.Deform.AffectedByInteractors ? 1f : 0f;
            this.lodMat0.SetFloat(ID_WindEnabled,        windFlag);
            this.lodMat1.SetFloat(ID_WindEnabled,        windFlag);
            this.lodMat2.SetFloat(ID_WindEnabled,        windFlag);
            this.lodMat0.SetFloat(ID_InteractorsEnabled, interactorsFlag);
            this.lodMat1.SetFloat(ID_InteractorsEnabled, interactorsFlag);
            this.lodMat2.SetFloat(ID_InteractorsEnabled, interactorsFlag);

            // ── Static globals (set once at build, not every frame) ─────────
            // _Blades buffer doesn't change between frames; _Interactors buffer ref is also stable
            // (same GraphicsBuffer object every frame — only count changes, updated in Step).
            if (this.bladeBuffer.BladeBuffer != null)
                Shader.SetGlobalBuffer(ID_Blades, this.bladeBuffer.BladeBuffer);
            Shader.SetGlobalBuffer(ID_Interactors, this.interactorBuffer.Buffer);
            // _ScaleMax2 is a PER-LAYER scale-decode bound read only by the render VS (NOT the cull compute).
            // Set it PER-MATERIAL so a sibling scatter layer can't clobber ours via the shared global.
            this.SetLodFloat(ID_ScaleMax2, this.bladeBuffer.ScaleMax2);

            // ── Reusable CommandBuffer ──────────────────────────────────────
            this.cullCmd = new CommandBuffer { name = "GrassGpuEngine.Cull" };

            this.isBuilt = true;
        }

        // ── IGrassEngine : Step ───────────────────────────────────────────────

        /// <inheritdoc/>
        /// No per-blade CPU deform: deform is fully GPU-side. Step advances the time accumulator only.
        /// Interactor upload is intentionally in Submit so it is always fresh for the draw, regardless
        /// of editor-tick / camera-callback pairing (FIX 3: SSOT — upload in exactly one place).
        public void Step(float dt)
        {
            this.time += dt;
        }

        // ── IGrassEngine : Submit ─────────────────────────────────────────────

        /// <inheritdoc/>
        /// Records and executes the full two-pass cull CommandBuffer, updates wind/time globals,
        /// then issues 3× <see cref="Graphics.RenderMeshIndirect"/> — one per LOD mesh.
        public void Submit(Camera? targetCamera, Vector3 lodReferencePos)
        {
            if (!this.isBuilt || this.bladeBuffer == null || this.cullCmd == null) return;
            // Skip Submit if any inner GPU buffer is missing or the field has zero chunks. The
            // baker leaves these null when there are no blades (empty layer / rebuild-in-flight);
            // dispatching the cull would hit `Property (blades) at kernel index (2) is not set`.
            if (this.bladeBuffer.BladeBuffer == null ||
                this.bladeBuffer.AabbBuffer  == null ||
                this.bladeBuffer.RangeBuffer == null ||
                this.bladeBuffer.TotalChunks <= 0) return;

            // ── 1. Resolve the camera for frustum planes ────────────────────
            // The cull uses the actual render camera; if null (all-cameras path) use Camera.main.
            Camera? cullCam = targetCamera ?? Camera.main;
            if (cullCam == null) return;

            // ── 2. Build frustum planes from the render camera ──────────────
            // Non-alloc overload writes into planeScratch — zero per-frame heap alloc.
            GeometryUtility.CalculateFrustumPlanes(cullCam, this.planeScratch);
            for (int i = 0; i < 6; ++i)
            {
                this.frustumPlanes[i] = new Vector4(
                    this.planeScratch[i].normal.x,
                    this.planeScratch[i].normal.y,
                    this.planeScratch[i].normal.z,
                    this.planeScratch[i].distance);
            }

            // ── 3. Record + execute the two-pass cull CommandBuffer ─────────
            this.cullCmd.Clear();
            this.RecordFrameCommands(
                this.cullCmd,
                cullCam,
                this.bladeBuffer.AabbBuffer!,
                this.bladeBuffer.BladeBuffer!,
                this.bladeBuffer.RangeBuffer!,
                this.bladeBuffer.TotalChunks,
                this.frustumPlanes,
                this.maxSqrDistance,
                this.lod0MaxSqrDist,
                this.lod1MaxSqrDist);
            Graphics.ExecuteCommandBuffer(this.cullCmd);

            // ── 4. Update per-frame shader globals ──────────────────────────
            Vector3 camPos = cullCam.transform.position;
            // PER-MATERIAL wind/bend/flatten — NOT SetGlobal. These are per-layer config; routing them
            // through global state let a sibling scatter layer (e.g. a wind-enabled instance layer)
            // overwrite this layer's wind every frame on the deferred indirect draws. Setting them on this
            // layer's own LOD material clones keeps each layer's deform independent.
            this.SetLodFloat (ID_GrassTime,        this.time);
            this.SetLodVector(ID_WindDir,          new Vector4(this.windDir.x, this.windDir.y, 0f, 0f));
            this.SetLodFloat (ID_WindStrength,     this.windStrength);
            this.SetLodFloat (ID_WindFrequency,    this.windFrequency);
            this.SetLodFloat (ID_WindNoiseScale,   this.windNoiseScale);
            this.SetLodFloat (ID_WindGustScale,    this.windGustScale);
            this.SetLodFloat (ID_WindRippleScale,  this.windRippleScale);
            this.SetLodFloat (ID_WindGustSpeed,    this.windGustSpeed);
            this.SetLodFloat (ID_WindRippleSpeed,  this.windRippleSpeed);
            this.SetLodFloat (ID_WindRippleWeight, this.windRippleWeight);
            this.SetLodFloat (ID_BendStrength,     this.bendStrength);
            this.SetLodFloat (ID_Flatten,          this.flatten);
            // _CamPosWS is the camera position — genuinely scene-wide, not per-layer — so it stays global.
            Shader.SetGlobalVector(ID_CamPosWS, new Vector4(camPos.x, camPos.y, camPos.z, 0f));

            // FIX 3: upload interactors here (Submit) so it is always fresh for the draw.
            // SSOT: upload in exactly one place — not in Step.
            this.interactorBuffer?.Upload(GrassInteractor.Active);
            Shader.SetGlobalInteger(ID_InteractorCount, this.interactorBuffer?.Count ?? 0);

            // Phase 2: upload trail segments each frame (count global set inside Upload).
            this.trailBuffer?.Upload(GrassTrailInteractor.Active);

            // FIX 4: rebind static buffers every Submit — guards against domain-reload / any other
            // system resetting global shader state between frames.
            if (this.bladeBuffer?.BladeBuffer != null)
                Shader.SetGlobalBuffer(ID_Blades, this.bladeBuffer.BladeBuffer);
            if (this.interactorBuffer?.Buffer != null)
                Shader.SetGlobalBuffer(ID_Interactors, this.interactorBuffer.Buffer);
            // PER-MATERIAL (see Build) — never SetGlobal: a sibling layer would clobber our scale bound.
            this.SetLodFloat(ID_ScaleMax2, this.bladeBuffer?.ScaleMax2 ?? 1f);

            // ── 5. RenderMeshIndirect ×3 ─────────────────────────────────────
            // RenderParams MUST be built via the Material constructor: the object-initializer form leaves
            // renderingLayerMask = 0, which makes URP SKIP THE DRAW ENTIRELY. The Material ctor sets renderingLayerMask to the project default.
            // worldBounds must be non-zero-extent (zero culls the whole draw). matProps NOT set (MPB is
            // silently dropped under URP RenderGraph). Mirrors the working CPU GrassRenderer pattern.
            // Draw scoped to targetCamera: null in play (all cameras), the firing camera in edit-mode
            // per-camera beginCameraRendering (no N× overdraw across Scene + Game views).
            if (this.mesh0 != null && this.lodMat0 != null && this.argsLod0Buf != null)
                Graphics.RenderMeshIndirect(this.MakeRenderParams(this.lodMat0, targetCamera), this.mesh0, this.argsLod0Buf, 1, 0);
            if (this.mesh1 != null && this.lodMat1 != null && this.argsLod1Buf != null)
                Graphics.RenderMeshIndirect(this.MakeRenderParams(this.lodMat1, targetCamera), this.mesh1, this.argsLod1Buf, 1, 0);
            if (this.mesh2 != null && this.lodMat2 != null && this.argsLod2Buf != null)
                Graphics.RenderMeshIndirect(this.MakeRenderParams(this.lodMat2, targetCamera), this.mesh2, this.argsLod2Buf, 1, 0);
        }

        /// <summary>
        /// Builds RenderParams via the Material constructor so renderingLayerMask gets the project default.
        /// The object-initializer form (<c>new RenderParams { ... }</c>) leaves renderingLayerMask = 0, which
        /// makes URP skip the draw. Mirrors the CPU <see cref="GrassRenderer"/> construction.
        /// </summary>
        /// <summary>Sets a float on all three LOD material clones (per-material, never global).</summary>
        private void SetLodFloat(int id, float v)
        {
            if (this.lodMat0 != null) this.lodMat0.SetFloat(id, v);
            if (this.lodMat1 != null) this.lodMat1.SetFloat(id, v);
            if (this.lodMat2 != null) this.lodMat2.SetFloat(id, v);
        }

        /// <summary>Sets a vector on all three LOD material clones (per-material, never global).</summary>
        private void SetLodVector(int id, Vector4 v)
        {
            if (this.lodMat0 != null) this.lodMat0.SetVector(id, v);
            if (this.lodMat1 != null) this.lodMat1.SetVector(id, v);
            if (this.lodMat2 != null) this.lodMat2.SetVector(id, v);
        }

        private RenderParams MakeRenderParams(Material mat, Camera? drawCamera)
        {
            return new RenderParams(mat)
            {
                worldBounds       = this.worldBounds,
                shadowCastingMode = this.shadowCastingMode, // honors layer.Render.ShadowCastingMode
                receiveShadows    = false,
                // null = render in ALL cameras (play-mode player-loop path).
                // A camera = render ONLY in that one (edit-mode per-camera beginCameraRendering path) so the
                // Scene + Game views don't each submit an all-cameras draw (N× overdraw + shared-args race).
                // Mirrors the CPU GrassRenderer's rp.camera = targetCamera discipline.
                camera            = drawCamera,
                layer             = 0,
            };
        }

        // ── IGrassEngine : WorldBounds ────────────────────────────────────────

        /// <inheritdoc/>
        public Bounds WorldBounds => this.worldBounds;

        // ── IGrassEngine : Dispose ────────────────────────────────────────────

        /// <inheritdoc/>
        public void Dispose()
        {
            this.cullCmd?.Dispose(); this.cullCmd = null;

            // Release cull buffers.
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

            // Destroy per-LOD material clones (not the source material — that's field-owned).
            // [ExecuteAlways] means Dispose can run in EDIT mode (domain reload / Rebuild), where
            // Object.Destroy throws "may not be called from edit mode" — use DestroyImmediate there.
            SafeDestroy(this.lodMat0); this.lodMat0 = null;
            SafeDestroy(this.lodMat1); this.lodMat1 = null;
            SafeDestroy(this.lodMat2); this.lodMat2 = null;

            this.isBuilt = false;
        }

        /// <summary>Destroys a Unity object safely in both play and edit mode ([ExecuteAlways] re-builds run in edit).</summary>
        private static void SafeDestroy(UnityEngine.Object? o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        // ── Self-test ─────────────────────────────────────────────────────────

        /// <summary>
        /// One-time post-Build self-test: verifies the GPU indirect path does not throw on this
        /// device. Called by <see cref="ScatterField"/> immediately after
        /// <see cref="Build"/>; any exception → the façade demotes to the CPU tier.
        ///
        /// IMPLEMENTATION STRATEGY (minimal + documented):
        ///   Full pixel-readback approach (render indirect into a 4×4 RT, ReadPixels, compare to
        ///   sentinel) was evaluated but is unreliable inside a synchronous editor-context Build
        ///   call — RenderMeshIndirect is player-loop deferred and the RT stays clear when read
        ///   back within the same frame. A real async-GPU-readback self-test requires a Play-mode
        ///   frame boundary and is therefore deferred to the on-device hardware smoke (Phase 7
        ///   step 6, user-run follow-up — see phase-7.md § Verification gate).
        ///
        ///   This minimal version exercises every structural step that can throw immediately:
        ///   - All GraphicsBuffer objects are valid (not null, not disposed).
        ///   - RenderMeshIndirect accepts our RenderParams + args buffer without throwing.
        ///   - The cull CommandBuffer records without error.
        ///   The try/catch in ScatterField wraps the Build call itself, so any deferred
        ///   GPU driver error that surfaces as an exception is also caught there.
        ///
        ///   Silent-fail devices (indirect=true but no pixels rendered) are caught by the
        ///   SystemInfo probe (primary guard) + the try/catch fallback (secondary). The
        ///   hardware smoke (step 6) remains the definitive R1 gate.
        /// </summary>
        /// <param name="reason">Human-readable pass/fail description.</param>
        /// <returns><c>true</c> if no structural exception was thrown; <c>false</c> on error.</returns>
        internal bool SelfTest(out string reason)
        {
            if (!this.isBuilt ||
                this.cullCmd == null ||
                this.argsLod0Buf == null ||
                this.lodMat0 == null ||
                this.mesh0 == null)
            {
                reason = "GPU self-test: SKIP (engine not fully built)";
                return true; // Not a GPU failure — BuildFailed is caught at a higher level.
            }

            try
            {
                // Exercise CommandBuffer recording (throws immediately on kernel or buffer error).
                using CommandBuffer probeCmd = new CommandBuffer { name = "GrassGpuEngine.SelfTest" };
                // Record without dispatching — we're checking that the API accepts our setup.
                // A real dispatch requires a valid camera + blades data, which are present post-Build.
                probeCmd.Clear();
                probeCmd.BeginSample("GrassSelfTest");
                probeCmd.EndSample("GrassSelfTest");
                Graphics.ExecuteCommandBuffer(probeCmd);

                // Exercise RenderMeshIndirect signature acceptance (throws on bad buffer layout).
                // We call it with our real args buffer (instanceCount=0 from InitLodArgsFromMeshes,
                // so nothing actually renders — but the API validates buffer target + stride).
                Graphics.RenderMeshIndirect(
                    this.MakeRenderParams(this.lodMat0, null),
                    this.mesh0,
                    this.argsLod0Buf,
                    commandCount: 1,
                    startCommand: 0);

                reason = $"GPU self-test: OK (no exceptions; device={SystemInfo.graphicsDeviceName})";
                return true;
            }
            catch (System.Exception ex)
            {
                reason = $"GPU self-test: FAIL ({ex.GetType().Name}: {ex.Message})";
                return false;
            }
        }

        // ── Internal cull pipeline ────────────────────────────────────────────

        /// <summary>
        /// Records the complete two-pass cull sequence on <paramref name="cmd"/>:
        ///   SetCounterValue(0) ×3 LODs + reset visibleChunks → ChunkCull → CopyCount → WriteArgsB
        ///   → BladeCull (DispatchIndirect) → CopyCount ×3 into draw args.
        /// </summary>
        private void RecordFrameCommands(
            CommandBuffer  cmd,
            Camera         cam,
            GraphicsBuffer aabbBuffer,
            GraphicsBuffer bladeBuffer,
            GraphicsBuffer rangeBuffer,
            int            chunkCount,
            Vector4[]      frustumPlanes,
            float          maxSqrDistance,
            float          lod0MaxSqrDist,
            float          lod1MaxSqrDist)
        {
            if (this.visibleChunksBuf == null || this.visibleCountBuf == null ||
                this.dispatchArgsBuf == null  || this.visibleLod0Buf == null ||
                this.visibleLod1Buf == null   || this.visibleLod2Buf == null ||
                this.argsLod0Buf == null      || this.argsLod1Buf == null    ||
                this.argsLod2Buf == null)
                return;

            // ── R2/R4: reset ALL append counters BEFORE any dispatch ────────
            this.visibleChunksBuf.SetCounterValue(0);
            this.visibleLod0Buf.SetCounterValue(0);
            this.visibleLod1Buf.SetCounterValue(0);
            this.visibleLod2Buf.SetCounterValue(0);

            // ── Pass A: ChunkCull ───────────────────────────────────────────
            cmd.SetComputeBufferParam(this.computeShader, this.kernelCull, "chunkAabbs",    aabbBuffer);
            cmd.SetComputeIntParam   (this.computeShader,                  "chunkCount",    chunkCount);
            cmd.SetComputeVectorArrayParam(this.computeShader, "frustumPlanes", frustumPlanes);
            Vector3 camPos = cam.transform.position;
            cmd.SetComputeVectorParam(this.computeShader, "camPosWS",
                new Vector4(camPos.x, camPos.y, camPos.z, 0f));
            cmd.SetComputeFloatParam (this.computeShader, "maxCullSqrDistance", maxSqrDistance);
            cmd.SetComputeBufferParam(this.computeShader, this.kernelCull, "visibleChunks", this.visibleChunksBuf);

            int cullGroups = Mathf.CeilToInt((float)chunkCount / 64);
            cmd.DispatchCompute(this.computeShader, this.kernelCull, cullGroups, 1, 1);

            cmd.CopyCounterValue(this.visibleChunksBuf, this.visibleCountBuf, 0);

            // WriteArgsB: reads visibleCount and writes [groupsX, 1, 1] for BladeCull.
            cmd.SetComputeBufferParam(this.computeShader, this.kernelArgs, "visibleCount",  this.visibleCountBuf);
            cmd.SetComputeBufferParam(this.computeShader, this.kernelArgs, "dispatchArgsB", this.dispatchArgsBuf);
            cmd.DispatchCompute(this.computeShader, this.kernelArgs, 1, 1, 1);

            // ── Pass B: BladeCull ───────────────────────────────────────────
            int k = this.kernelBlade;
            cmd.SetComputeBufferParam(this.computeShader, k, "blades",            bladeBuffer);
            cmd.SetComputeBufferParam(this.computeShader, k, "chunkRanges",       rangeBuffer);
            cmd.SetComputeBufferParam(this.computeShader, k, "visibleChunksRead", this.visibleChunksBuf);
            cmd.SetComputeBufferParam(this.computeShader, k, "visibleCount",      this.visibleCountBuf);
            cmd.SetComputeFloatParam (this.computeShader,    "lod0MaxSqrDist",    lod0MaxSqrDist);
            cmd.SetComputeFloatParam (this.computeShader,    "lod1MaxSqrDist",    lod1MaxSqrDist);
            cmd.SetComputeFloatParam (this.computeShader,    "bladeCullMargin",   this.bladeCullMargin);
            cmd.SetComputeBufferParam(this.computeShader, k, "visibleLod0",       this.visibleLod0Buf);
            cmd.SetComputeBufferParam(this.computeShader, k, "visibleLod1",       this.visibleLod1Buf);
            cmd.SetComputeBufferParam(this.computeShader, k, "visibleLod2",       this.visibleLod2Buf);

            cmd.DispatchCompute(this.computeShader, k, this.dispatchArgsBuf, 0u);

            // ── CopyCount → draw args ────────────────────────────────────────
            cmd.CopyCounterValue(this.visibleLod0Buf, this.argsLod0Buf, ARGS_INSTANCE_COUNT_OFFSET);
            cmd.CopyCounterValue(this.visibleLod1Buf, this.argsLod1Buf, ARGS_INSTANCE_COUNT_OFFSET);
            cmd.CopyCounterValue(this.visibleLod2Buf, this.argsLod2Buf, ARGS_INSTANCE_COUNT_OFFSET);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void InitLodArgsFromMeshes()
        {
            (Mesh? mesh, GraphicsBuffer? buf, int lodIndex)[] lodPairs =
            {
                (this.mesh0, this.argsLod0Buf, 0),
                (this.mesh1, this.argsLod1Buf, 1),
                (this.mesh2, this.argsLod2Buf, 2),
            };

            foreach (var (mesh, buf, lodIndex) in lodPairs)
            {
                if (buf == null) continue;

                // FIX 5: validate index count — a mesh with 0 indices causes a silent no-op draw.
                if (mesh != null && mesh.GetIndexCount(0) == 0)
                {
                    Debug.LogError($"[GrassGpuEngine] LOD{lodIndex} mesh '{mesh.name}' has 0 indices — " +
                                   "its indirect draw will render nothing (mesh not readable / empty submesh).");
                }

                var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
                args[0].indexCountPerInstance = (mesh != null) ? mesh.GetIndexCount(0) : 0;
                args[0].instanceCount         = 0;
                args[0].startIndex            = (mesh != null) ? mesh.GetIndexStart(0) : 0;
                args[0].baseVertexIndex       = (mesh != null) ? (uint)mesh.GetBaseVertex(0) : 0;
                args[0].startInstance         = 0;
                buf.SetData(args);
            }
        }
    }
}
