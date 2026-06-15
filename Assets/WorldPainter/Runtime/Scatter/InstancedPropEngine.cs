#nullable enable
using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter
{
    /// <summary>
    /// GPU-indirect engine for <see cref="PropLayerScatterLayer"/> mesh props. Replaces the former
    /// MeshScatterEngine: same chunked GPU frustum-cull + per-LOD indirect draw + pooled colliders,
    /// PLUS an optional C#(Burst)-simulated whole-instance rigid tilt (<see cref="InstanceTiltSimulator"/>)
    /// that leans props away from moving interactors and springs them back.
    ///
    /// The tilt is computed in C# (so recovery has real timed spring-back state), uploaded as a compact
    /// per-instance quaternion buffer (<c>_InstanceTilt</c>), and applied rigidly about each instance pivot
    /// in <c>ScatterInstanced.shader</c>. Base transforms stay static in the GPU instance buffer; only the
    /// small tilt buffer uploads per frame.
    /// </summary>
    internal sealed class InstancedPropEngine : IGrassEngine
    {
        // ── Constants ────────────────────────────────────────────────────────
        private const int ARGS_INSTANCE_COUNT_OFFSET = 4;
        private const int MAX_LODS = 3;

        // ── Shader global IDs ─────────────────────────────────────────────────
        private static readonly int ID_Instances             = Shader.PropertyToID("_Instances");
        private static readonly int ID_ScaleMax2             = Shader.PropertyToID("_ScaleMax2");
        private static readonly int ID_VisibleIndices        = Shader.PropertyToID("_VisibleIndices");
        private static readonly int ID_OrientMode            = Shader.PropertyToID("_OrientMode");
        private static readonly int ID_RotationOffsetEuler   = Shader.PropertyToID("_RotationOffsetEuler");
        private static readonly int ID_WindEnabled           = Shader.PropertyToID("_WindEnabled");
        private static readonly int ID_InteractorsEnabled    = Shader.PropertyToID("_InteractorsEnabled");
        private static readonly int ID_Interactors           = Shader.PropertyToID("_Interactors");
        private static readonly int ID_InteractorCount       = Shader.PropertyToID("_InteractorCount");
        private static readonly int ID_GrassTime             = Shader.PropertyToID("_GrassTime");
        private static readonly int ID_WindDir               = Shader.PropertyToID("_WindDir");
        private static readonly int ID_WindStrength          = Shader.PropertyToID("_WindStrength");
        private static readonly int ID_WindFrequency         = Shader.PropertyToID("_WindFrequency");
        private static readonly int ID_WindNoiseScale        = Shader.PropertyToID("_WindNoiseScale");
        private static readonly int ID_BendStrength          = Shader.PropertyToID("_BendStrength");
        private static readonly int ID_Flatten               = Shader.PropertyToID("_Flatten");
        private static readonly int ID_InstanceTilt          = Shader.PropertyToID("_InstanceTilt");
        private static readonly int ID_TiltEnabled           = Shader.PropertyToID("_TiltEnabled");
        private static readonly int ID_AnchorOffset          = Shader.PropertyToID("_AnchorOffset");
        // Phase 2: PBR material property IDs.
        private static readonly int ID_NormalMap             = Shader.PropertyToID("_NormalMap");
        private static readonly int ID_NormalStrength        = Shader.PropertyToID("_NormalStrength");
        private static readonly int ID_MaskMap               = Shader.PropertyToID("_MaskMap");
        private static readonly int ID_EmissionMap           = Shader.PropertyToID("_EmissionMap");
        private static readonly int ID_EmissionColor         = Shader.PropertyToID("_EmissionColor");
        private const string KW_NormalMap                    = "_NORMALMAP";
        private const string KW_Pbr                         = "_PBR";
        private const string KW_MaskMap                     = "_MASKMAP";
        private const string KW_Emission                    = "_EMISSION";
        // Phase 3: Shadow material property IDs and keywords.
        private static readonly int ID_ShadowStrength        = Shader.PropertyToID("_ShadowStrength");
        private static readonly int ID_ShadowTint            = Shader.PropertyToID("_ShadowTint");
        private static readonly int ID_ShadowDepthBias       = Shader.PropertyToID("_ShadowDepthBias");
        private static readonly int ID_ShadowNormalBias      = Shader.PropertyToID("_ShadowNormalBias");
        private const string KW_ReceiveShadows               = "_RECEIVE_SHADOWS";
        private const string KW_ShadowTint                   = "_SHADOW_TINT";
        private const string KW_AlphaclipShadows             = "_ALPHACLIP_SHADOWS";

        // ── Injected ─────────────────────────────────────────────────────────
        private readonly ComputeShader computeShader;
        private readonly Material      materialBase; // source; cloned per-LOD

        // ── Kernel indices ────────────────────────────────────────────────────
        private readonly int kernelCull;
        private readonly int kernelArgs;
        private readonly int kernelBlade;

        // ── Per-frame Pass-A cull buffers ─────────────────────────────────────
        private GraphicsBuffer? visibleChunksBuf;
        private GraphicsBuffer? visibleCountBuf;
        private GraphicsBuffer? dispatchArgsBuf;

        // ── Per-frame Pass-B LOD index buffers ───────────────────────────────
        private GraphicsBuffer? visibleLod0Buf;
        private GraphicsBuffer? visibleLod1Buf;
        private GraphicsBuffer? visibleLod2Buf;

        // ── Per-LOD indirect draw args ────────────────────────────────────────
        private GraphicsBuffer? argsLod0Buf;

        // ── DIAGNOSTIC throttle (remove with matching prints) ─────────────
        private float lastSubmitDiagLogTime = -1f;
        private GraphicsBuffer? argsLod1Buf;
        private GraphicsBuffer? argsLod2Buf;

        // ── Static GPU data ───────────────────────────────────────────────────
        private ChunkedInstanceBuffer? instanceBuffer;

        // ── Per-instance colliders (Play-mode only) ───────────────────────────
        private GameObject?                       colliderRoot;
        private InstanceColliderPool?             colliderPool;
        private InstanceVisibilityColliderDriver? colliderDriver;

        // ── Per-LOD material clones ───────────────────────────────────────────
        private Material? lodMat0;
        private Material? lodMat1;
        private Material? lodMat2;

        // ── LOD meshes ────────────────────────────────────────────────────────
        private Mesh? mesh0;
        private Mesh? mesh1;
        private Mesh? mesh2;

        // ── Config snapshot ───────────────────────────────────────────────────
        private float lod0MaxSqrDist;
        private float lod1MaxSqrDist;
        private float maxSqrDistance;
        private float bladeCullMargin;

        // ── Phase 3: shadow config snapshot ──────────────────────────────────
        private UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;
        private bool receiveShadows;

        // ── Deform state ──────────────────────────────────────────────────────
        private bool affectedByWind;
        private bool affectedByInteractors;
        private bool interactsWithDeform;

        private GrassInteractorBuffer? interactorBuffer;
        private float deformTime;

        private Vector2 windSnapshotDir;
        private float   windSnapshotStrength;
        private float   windSnapshotFrequency;
        private float   windSnapshotNoiseScale;
        private float   windSnapshotBendStrength;
        private float   windSnapshotFlatten;

        // ── Rigid-tilt state ──────────────────────────────────────────────────
        private InstanceTiltSimulator? tiltSim;
        private bool tiltEnabled;

        // ── Per-frame state ───────────────────────────────────────────────────
        private Bounds worldBounds;
        private bool isBuilt;

        // ── CommandBuffer ────────────────────────────────────────────────────
        private CommandBuffer? cullCmd;

        // ── Frustum plane scratch ────────────────────────────────────────────
        private readonly Vector4[] frustumPlanes = new Vector4[6];
        private readonly Plane[]   planeScratch  = new Plane[6];

        // ── Construction ──────────────────────────────────────────────────────

        public InstancedPropEngine(ComputeShader computeShader, Material material)
        {
            this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.materialBase  = material      ?? throw new ArgumentNullException(nameof(material));

            this.kernelCull  = computeShader.FindKernel("ChunkCull");
            this.kernelArgs  = computeShader.FindKernel("WriteArgsB");
            this.kernelBlade = computeShader.FindKernel("BladeCull");
        }

        // ── IGrassEngine : Build ──────────────────────────────────────────────

        public void Build(ScatterLayer layer, Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler)
        {
            this.Dispose();

            GrassScatterResult scatter = GrassScatter.Build(layer, origin, pool, sampler);
            this.worldBounds = scatter.WorldBounds;

            Bounds meshBounds = ComputeMeshBounds(layer.Render.LodMeshes);

            // ── DIAGNOSTIC (remove with matching prints) ──────────────────────
            // The shader does posWS = inst.posWS + baseRot·(localPos · scale). `localPos` is the
            // RAW mesh vertex coordinate. If meshBounds.center ≠ Vector3.zero, the mesh was
            // authored with its origin OFFSET from its visual center — so the rendered mesh
            // appears at world (inst.posWS + meshBounds.center) instead of inst.posWS. This is
            // the classic "the placement gizmo is right but the mesh renders 70 m away" bug.
            Mesh[] lodMeshesDiag = layer.Render.LodMeshes;
            string lodDiag = $"LODs={lodMeshesDiag.Length}";
            for (int li = 0; li < lodMeshesDiag.Length; ++li)
            {
                Mesh? lm = lodMeshesDiag[li];
                lodDiag += lm != null
                    ? $" | LOD{li}={lm.name} c={lm.bounds.center:F4} e={lm.bounds.extents:F4}"
                    : $" | LOD{li}=<null>";
            }
            UnityEngine.Debug.Log(
                $"[InstancedPropEngine] meshBounds.center={meshBounds.center:F4} " +
                $"extents={meshBounds.extents:F4} {lodDiag}");

            Vector2 effectiveBounds = sampler is TerrainSurfaceSampler tss
                ? tss.TerrainSizeXZ
                : layer.FieldBounds;

            this.instanceBuffer = new ChunkedInstanceBuffer();
            this.instanceBuffer.Bake(
                scatter, origin, effectiveBounds, layer.ScaleRange.y,
                meshBounds, oriented: layer.IsOriented, chunkSize: 8);

            // Phase-H: pooled + culled per-instance colliders.
            this.BuildColliderRuntime(layer);

            // Rigid-tilt sim (PropLayerScatterLayer only, when interactor-tilt is enabled). Reads the BAKED
            // instance order from instanceBuffer.Instances so the tilt buffer index == _Instances index.
            var instLayer = layer as PropLayerScatterLayer;
            this.tiltEnabled = instLayer != null && instLayer.Tilt.AffectedByInteractors;
            if (this.tiltEnabled && instLayer != null)
            {
                this.tiltSim = new InstanceTiltSimulator(this.instanceBuffer, instLayer);
                if (instLayer.Tilt.ColliderFollowsTilt)
                    Debug.LogWarning(
                        $"[InstancedPropEngine] Layer '{layer.name}': colliderFollowsTilt=true is not yet " +
                        "wired — colliders stay at their base orientation.");
            }

            GrassScatter.ReturnSlabs(scatter, pool);

            Mesh[] meshes = layer.Render.LodMeshes;
            if (meshes.Length == 0)
                Debug.LogWarning(
                    $"[InstancedPropEngine] Layer '{layer.name}' has no LOD meshes. No props will render.");

            this.mesh0 = meshes.Length > 0 ? meshes[0] : null;
            this.mesh1 = meshes.Length > 1 ? meshes[1] : null;
            this.mesh2 = meshes.Length > 2 ? meshes[2] : null;

            float[] dists = layer.Render.LodMaxDistances;
            // Missing LOD switch distances extend the last present LOD to the cull boundary (cull<=0 → no cap),
            // NOT an arbitrary 12/30 m default — otherwise instances past 12 m bucket into empty (mesh-less)
            // LOD slots and vanish regardless of renderCullDistance (1-LOD prop draw-distance bug).
            float cull = layer.Render.RenderCullDistance;
            (this.lod0MaxSqrDist, this.lod1MaxSqrDist, this.maxSqrDistance) = LodCullMath.Thresholds(dists, cull);

            float bakedScaleMax = this.instanceBuffer.ScaleMax;
            // Expand the per-instance cull margin by the max-tilt sweep so a tilted prop never pops.
            float tiltSweep = (this.tiltEnabled && instLayer != null)
                ? Mathf.Sin(Mathf.Deg2Rad * instLayer.Tilt.MaxTiltAngle)
                : 0f;
            this.bladeCullMargin = Mathf.Max(0f,
                meshBounds.extents.magnitude * bakedScaleMax * (1f + tiltSweep));

            int chunkCap = Mathf.Max(1, this.instanceBuffer.TotalChunks);
            int instCap  = Mathf.Max(1, this.instanceBuffer.TotalInstances);

            this.visibleChunksBuf = new GraphicsBuffer(GraphicsBuffer.Target.Append, chunkCap, sizeof(uint));
            this.visibleCountBuf  = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            this.dispatchArgsBuf  = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, sizeof(uint));
            this.visibleLod0Buf   = new GraphicsBuffer(GraphicsBuffer.Target.Append, instCap, sizeof(uint));
            this.visibleLod1Buf   = new GraphicsBuffer(GraphicsBuffer.Target.Append, instCap, sizeof(uint));
            this.visibleLod2Buf   = new GraphicsBuffer(GraphicsBuffer.Target.Append, instCap, sizeof(uint));

            int argsStride = GraphicsBuffer.IndirectDrawIndexedArgs.size;
            this.argsLod0Buf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, argsStride);
            this.argsLod1Buf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, argsStride);
            this.argsLod2Buf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, argsStride);

            this.InitLodArgs();

            this.lodMat0 = new Material(this.materialBase) { name = "ScatterInstanced_LOD0" };
            this.lodMat1 = new Material(this.materialBase) { name = "ScatterInstanced_LOD1" };
            this.lodMat2 = new Material(this.materialBase) { name = "ScatterInstanced_LOD2" };

            if (this.visibleLod0Buf != null) this.lodMat0.SetBuffer(ID_VisibleIndices, this.visibleLod0Buf);
            if (this.visibleLod1Buf != null) this.lodMat1.SetBuffer(ID_VisibleIndices, this.visibleLod1Buf);
            if (this.visibleLod2Buf != null) this.lodMat2.SetBuffer(ID_VisibleIndices, this.visibleLod2Buf);

            float orientMode = layer.IsOriented ? 1f : 0f;
            Vector3 rotOffset = layer.RotationOffsetEuler;
            this.lodMat0.SetFloat(ID_OrientMode, orientMode);
            this.lodMat1.SetFloat(ID_OrientMode, orientMode);
            this.lodMat2.SetFloat(ID_OrientMode, orientMode);
            this.lodMat0.SetVector(ID_RotationOffsetEuler, new Vector4(rotOffset.x, rotOffset.y, rotOffset.z, 0f));
            this.lodMat1.SetVector(ID_RotationOffsetEuler, new Vector4(rotOffset.x, rotOffset.y, rotOffset.z, 0f));
            this.lodMat2.SetVector(ID_RotationOffsetEuler, new Vector4(rotOffset.x, rotOffset.y, rotOffset.z, 0f));

            float tiltFlag = this.tiltEnabled ? 1f : 0f;
            this.lodMat0.SetFloat(ID_TiltEnabled, tiltFlag);
            this.lodMat1.SetFloat(ID_TiltEnabled, tiltFlag);
            this.lodMat2.SetFloat(ID_TiltEnabled, tiltFlag);

            // Per-layer deform sampling anchor (instance-only). Static — set once at Build.
            // Shader samples wind/interactor from posWS + baseRot*(_AnchorOffset.xyz * scale).
            Vector3 anchor = instLayer != null ? instLayer.AnchorOffsetLocal : Vector3.zero;
            var anchorV = new Vector4(anchor.x, anchor.y, anchor.z, 0f);
            this.lodMat0.SetVector(ID_AnchorOffset, anchorV);
            this.lodMat1.SetVector(ID_AnchorOffset, anchorV);
            this.lodMat2.SetVector(ID_AnchorOffset, anchorV);

            this.affectedByWind        = layer.Deform.AffectedByWind;
            this.affectedByInteractors = layer.Deform.AffectedByInteractors;
            this.interactsWithDeform   = this.affectedByWind || this.affectedByInteractors;
            float windFlag        = this.affectedByWind        ? 1f : 0f;
            float interactorsFlag = this.affectedByInteractors ? 1f : 0f;
            this.lodMat0.SetFloat(ID_WindEnabled,        windFlag);
            this.lodMat1.SetFloat(ID_WindEnabled,        windFlag);
            this.lodMat2.SetFloat(ID_WindEnabled,        windFlag);
            this.lodMat0.SetFloat(ID_InteractorsEnabled, interactorsFlag);
            this.lodMat1.SetFloat(ID_InteractorsEnabled, interactorsFlag);
            this.lodMat2.SetFloat(ID_InteractorsEnabled, interactorsFlag);

            // Phase 2: PBR material keyword + texture binding.
            this.ApplyPbrKeywords(layer.Render);

            // Phase 3: snapshot shadow config; bind shadow keywords + params.
            this.shadowCastingMode = layer.Render.ShadowCastingMode;
            this.receiveShadows    = layer.Render.ReceiveShadows;
            this.ApplyShadowKeywords(layer.Render);

            if (this.interactsWithDeform)
            {
                this.interactorBuffer = new GrassInteractorBuffer();
                Shader.SetGlobalBuffer(ID_Interactors, this.interactorBuffer.Buffer);
            }
            this.deformTime = 0f;

            Vector2 rawDir = layer.Wind.WindDirection;
            this.windSnapshotDir          = rawDir.sqrMagnitude > 1e-8f ? rawDir.normalized : Vector2.right;
            this.windSnapshotStrength     = layer.Wind.WindStrength;
            this.windSnapshotFrequency    = layer.Wind.WindFrequency;
            this.windSnapshotNoiseScale   = layer.Wind.WindNoiseScale;
            this.windSnapshotBendStrength = layer.Deform.BendStrength;
            this.windSnapshotFlatten      = layer.Deform.Flatten;

            if (this.instanceBuffer.InstanceBuffer != null)
                Shader.SetGlobalBuffer(ID_Instances, this.instanceBuffer.InstanceBuffer);
            // _ScaleMax2 is a PER-LAYER scale-decode bound consumed only by the render VS (NOT the cull
            // compute). Set it PER-MATERIAL so a second scatter layer's ScaleMax can't clobber ours via the
            // shared global. (Root-cause class: per-layer render uniforms must never go through SetGlobal.)
            this.SetLodFloat(ID_ScaleMax2, this.instanceBuffer.ScaleMax);

            if (this.tiltSim?.TiltBuffer != null)
                Shader.SetGlobalBuffer(ID_InstanceTilt, this.tiltSim.TiltBuffer);

            this.cullCmd = new CommandBuffer { name = "InstancedPropEngine.Cull" };
            this.isBuilt = true;
        }

        // ── IGrassEngine : Step ───────────────────────────────────────────────

        public void Step(float dt)
        {
            if (this.affectedByWind)
                this.deformTime += dt;
            this.tiltSim?.Step(dt);
        }

        // ── IGrassEngine : Submit ─────────────────────────────────────────────

        public void Submit(Camera? targetCamera, Vector3 lodReferencePos)
        {
            if (!this.isBuilt || this.instanceBuffer == null || this.cullCmd == null)
                return;
            if (this.instanceBuffer.InstanceBuffer == null ||
                this.instanceBuffer.AabbBuffer     == null ||
                this.instanceBuffer.RangeBuffer    == null ||
                this.instanceBuffer.TotalChunks    <= 0) return;

            Camera? cullCam = targetCamera ?? Camera.main;
            if (cullCam == null) return;

            GeometryUtility.CalculateFrustumPlanes(cullCam, this.planeScratch);
            for (int i = 0; i < 6; ++i)
            {
                this.frustumPlanes[i] = new Vector4(
                    this.planeScratch[i].normal.x,
                    this.planeScratch[i].normal.y,
                    this.planeScratch[i].normal.z,
                    this.planeScratch[i].distance);
            }

            this.cullCmd.Clear();
            this.RecordFrameCommands(
                this.cullCmd, cullCam,
                this.instanceBuffer.AabbBuffer!,
                this.instanceBuffer.InstanceBuffer!,
                this.instanceBuffer.RangeBuffer!,
                this.instanceBuffer.TotalChunks,
                this.frustumPlanes,
                this.maxSqrDistance, this.lod0MaxSqrDist, this.lod1MaxSqrDist);
            Graphics.ExecuteCommandBuffer(this.cullCmd);

            // Tick the GPU-readback collider driver AFTER the cull command buffer executes
            // so all three visibleLodNBufs and their count buffers contain the current frame's results.
            if (this.colliderDriver != null &&
                this.visibleLod0Buf != null &&
                this.visibleLod1Buf != null &&
                this.visibleLod2Buf != null &&
                Application.isPlaying)
                this.colliderDriver.Tick(this.visibleLod0Buf, this.visibleLod1Buf, this.visibleLod2Buf);

            if (this.instanceBuffer.InstanceBuffer != null)
                Shader.SetGlobalBuffer(ID_Instances, this.instanceBuffer.InstanceBuffer);
            // PER-MATERIAL (see Build) — never SetGlobal: a sibling layer would clobber our scale bound.
            this.SetLodFloat(ID_ScaleMax2, this.instanceBuffer.ScaleMax);

            if (this.tiltSim?.TiltBuffer != null)
                Shader.SetGlobalBuffer(ID_InstanceTilt, this.tiltSim.TiltBuffer);

            if (this.affectedByWind)
            {
                // PER-MATERIAL wind/bend/flatten — NOT SetGlobal. These are per-layer config; routing them
                // through global state let a wind-enabled instance layer clobber the density layer's wind
                // every frame (last-Submit-wins on deferred indirect draws). Setting them on THIS layer's
                // own LOD material clones keeps each layer's deform independent.
                Vector2 cfgDir = this.windSnapshotDir;
                this.SetLodFloat (ID_GrassTime,     this.deformTime);
                this.SetLodVector(ID_WindDir,       new Vector4(cfgDir.x, cfgDir.y, 0f, 0f));
                this.SetLodFloat (ID_WindStrength,  this.windSnapshotStrength);
                this.SetLodFloat (ID_WindFrequency, this.windSnapshotFrequency);
                this.SetLodFloat (ID_WindNoiseScale,this.windSnapshotNoiseScale);
                this.SetLodFloat (ID_BendStrength,  this.windSnapshotBendStrength);
                this.SetLodFloat (ID_Flatten,       this.windSnapshotFlatten);
            }

            if (this.affectedByInteractors && this.interactorBuffer != null)
            {
                // Interactor buffer + count are genuinely scene-wide (one registry of active interactors),
                // so they stay global — every layer reads the same live interactor set.
                this.interactorBuffer.Upload(GrassInteractor.Active);
                Shader.SetGlobalBuffer(ID_Interactors,      this.interactorBuffer.Buffer);
                Shader.SetGlobalInteger(ID_InteractorCount, this.interactorBuffer.Count);
            }

            // ── DIAGNOSTIC (remove with matching prints) ──────────────────────
            // Throttle: log once per second so we don't drown the console. Captures the EXACT
            // uniforms the shader sees right before issuing the indirect draw.
            if (UnityEngine.Time.realtimeSinceStartup - lastSubmitDiagLogTime > 1f)
            {
                lastSubmitDiagLogTime = UnityEngine.Time.realtimeSinceStartup;
                float omSeen = this.lodMat0 != null ? this.lodMat0.GetFloat(ID_OrientMode)        : -1f;
                float weSeen = this.lodMat0 != null ? this.lodMat0.GetFloat(ID_WindEnabled)       : -1f;
                float ieSeen = this.lodMat0 != null ? this.lodMat0.GetFloat(ID_InteractorsEnabled): -1f;
                float teSeen = this.lodMat0 != null ? this.lodMat0.GetFloat(ID_TiltEnabled)       : -1f;
                Vector4 aoSeen = this.lodMat0 != null ? this.lodMat0.GetVector(ID_AnchorOffset)         : Vector4.zero;
                Vector4 roSeen = this.lodMat0 != null ? this.lodMat0.GetVector(ID_RotationOffsetEuler)  : Vector4.zero;
                float smSeen = this.lodMat0 != null ? this.lodMat0.GetFloat(ID_ScaleMax2)         : -1f;
                UnityEngine.Debug.Log(
                    $"[InstancedPropEngine.Submit] _OrientMode={omSeen:F2} _ScaleMax2={smSeen:F4} " +
                    $"_AnchorOffset={aoSeen} _RotationOffsetEuler={roSeen} " +
                    $"_Wind={weSeen} _Interactors={ieSeen} _Tilt={teSeen}");
            }

            if (this.mesh0 != null && this.lodMat0 != null && this.argsLod0Buf != null)
                Graphics.RenderMeshIndirect(this.MakeRenderParams(this.lodMat0, targetCamera), this.mesh0, this.argsLod0Buf, 1, 0);
            if (this.mesh1 != null && this.lodMat1 != null && this.argsLod1Buf != null)
                Graphics.RenderMeshIndirect(this.MakeRenderParams(this.lodMat1, targetCamera), this.mesh1, this.argsLod1Buf, 1, 0);
            if (this.mesh2 != null && this.lodMat2 != null && this.argsLod2Buf != null)
                Graphics.RenderMeshIndirect(this.MakeRenderParams(this.lodMat2, targetCamera), this.mesh2, this.argsLod2Buf, 1, 0);
        }

        // ── IGrassEngine : WorldBounds ────────────────────────────────────────

        public Bounds WorldBounds => this.worldBounds;

        // ── IGrassEngine : Dispose ────────────────────────────────────────────

        public void Dispose()
        {
            // Drain GPU readbacks FIRST — before releasing any GraphicsBuffer that a
            // readback may still be reading (visibleLod0Buf, Lod0CountBuffer).
            // colliderDriver.Dispose() calls AsyncGPUReadback.WaitAllRequests() internally.
            this.colliderDriver?.Dispose();
            this.colliderDriver = null;

            this.cullCmd?.Dispose(); this.cullCmd = null;

            this.visibleChunksBuf?.Release(); this.visibleChunksBuf = null;
            this.visibleCountBuf?.Release();  this.visibleCountBuf  = null;
            this.dispatchArgsBuf?.Release();  this.dispatchArgsBuf  = null;
            this.visibleLod0Buf?.Release();   this.visibleLod0Buf   = null;
            this.visibleLod1Buf?.Release();   this.visibleLod1Buf   = null;
            this.visibleLod2Buf?.Release();   this.visibleLod2Buf   = null;
            this.argsLod0Buf?.Release();      this.argsLod0Buf      = null;
            this.argsLod1Buf?.Release();      this.argsLod1Buf      = null;
            this.argsLod2Buf?.Release();      this.argsLod2Buf      = null;

            this.instanceBuffer?.Dispose(); this.instanceBuffer = null;
            this.interactorBuffer?.Dispose(); this.interactorBuffer = null;
            this.interactsWithDeform = false;
            this.deformTime = 0f;

            this.tiltSim?.Dispose(); this.tiltSim = null;
            this.tiltEnabled = false;

            this.colliderPool?.Dispose();
            this.colliderPool   = null;
            SafeDestroy(this.colliderRoot); this.colliderRoot = null;

            SafeDestroy(this.lodMat0); this.lodMat0 = null;
            SafeDestroy(this.lodMat1); this.lodMat1 = null;
            SafeDestroy(this.lodMat2); this.lodMat2 = null;

            this.isBuilt = false;
        }

        // ── Per-instance colliders (Play-mode only) ───────────────────────────

        private void BuildColliderRuntime(ScatterLayer layer)
        {
            if (layer is not PropLayerScatterLayer instLayer) return;
            if (!Application.isPlaying) return;
            if (!instLayer.GenerateColliders && !instLayer.AnyRecordWantsCollider()) return;

            var authored = instLayer.AuthoredInstances;
            if (authored == null) return;

            Mesh[] lodMeshes       = instLayer.Render.LodMeshes;
            Mesh? layerDefaultMesh = instLayer.DefaultColliderMesh != null
                ? instLayer.DefaultColliderMesh
                : (lodMeshes.Length > 0 ? lodMeshes[0] : null);

            NativeArray<InstanceRecord> records = authored.GetRuntimeRecords();
            if (records.Length == 0) return;

            this.colliderRoot = new GameObject("ScatterColliderPool");
            Transform rootT   = this.colliderRoot.transform;

            this.colliderPool = this.colliderRoot.AddComponent<InstanceColliderPool>();
            this.colliderPool.Init(instLayer.PoolCap, layerDefaultMesh, instLayer.DefaultColliderConvex,
                instLayer.DefaultColliderMaterial);
            // Small prewarm — lazy budgeted Acquire fills the rest progressively.
            this.colliderPool.Prewarm(Mathf.Min(instLayer.MaxCollidersPerFrame, instLayer.PoolCap));

            int count           = records.Length;
            var positions       = new Vector3[count];
            var rotations       = new Quaternion[count];
            var scales          = new float[count];
            var meshes          = new Mesh?[count];
            var convexFlags     = new bool[count];
            var wantsCollider   = new bool[count];
            var materials       = new PhysicsMaterial?[count];

            for (int i = 0; i < count; ++i)
            {
                InstanceRecord rec = records[i];

                Mesh? colMesh = null;
                PhysicsMaterial? colMat = null;
                if ((rec.overrideMask & InstanceOverrideMask.ColliderConfigured) != 0)
                {
                    if (rec.colliderMeshRefIndex >= 0)
                        colMesh = authored.GetObjectRef(rec.colliderMeshRefIndex) as Mesh;
                    if (rec.colliderMaterialRefIndex >= 0)
                        colMat = authored.GetObjectRef(rec.colliderMaterialRefIndex) as PhysicsMaterial;
                }
                colMesh ??= layerDefaultMesh;
                // colMat stays null when unset → InstanceColliderPool falls back to the layer default.

                bool wouldWant = instLayer.GenerateColliders || rec.generateCollider;
                if (wouldWant && colMesh == null)
                {
                    Debug.LogWarning(
                        $"[InstancedPropEngine] Record {i}: collider requested but no collider mesh " +
                        "available (no per-record override, no layer default, no lod0 mesh) — skipping.");
                }

                positions[i]     = rec.position;
                rotations[i]     = rec.rotation;
                scales[i]        = rec.scale * rec.colliderScale;
                meshes[i]        = colMesh;
                convexFlags[i]   = rec.colliderConvex;
                wantsCollider[i] = colMesh != null && (instLayer.GenerateColliders || rec.generateCollider);
                materials[i]     = colMat;
            }

            // Verify 1:1 prop invariant: scatter.TotalCount must equal records.Length so flatIdx == pool key i.
            // InstancePlacement emits exactly one instance per authored record, so this always holds for props.
            if (this.instanceBuffer!.TotalInstances != count)
            {
                Debug.LogError(
                    $"[InstancedPropEngine] 1:1 prop invariant violated: " +
                    $"instanceBuffer.TotalInstances={this.instanceBuffer.TotalInstances} != records.Length={count}. " +
                    "Collider culling disabled for this layer.");
                return;
            }

            int[]? sortedToAuthored = this.instanceBuffer.SortedToAuthored;
            if (sortedToAuthored == null)
            {
                Debug.LogError(
                    "[InstancedPropEngine] sortedToAuthored is null after bake. Collider culling disabled.");
                return;
            }

            if (instLayer.CullColliders)
            {
                this.colliderDriver = new InstanceVisibilityColliderDriver(
                    this.colliderPool,
                    sortedToAuthored,
                    positions, rotations, scales, meshes, convexFlags, wantsCollider, materials,
                    instLayer.PoolCap,
                    instLayer.MaxCollidersPerFrame);
                Debug.Log(
                    $"[InstancedPropEngine] GPU-readback collider driver: pool cap={instLayer.PoolCap}, " +
                    $"records={count}, sortedToAuthored.Length={sortedToAuthored.Length}.");
            }
            else
            {
                int acquired = 0;
                for (int i = 0; i < count; ++i)
                {
                    if (!wantsCollider[i]) continue;
                    MeshCollider? mc = this.colliderPool.Acquire(
                        i, positions[i], rotations[i], scales[i], meshes[i], convexFlags[i], materials[i]);
                    if (mc != null) acquired++;
                }
                Debug.Log(
                    $"[InstancedPropEngine] Collider runtime (no cull): acquired {acquired}/{count} colliders.");
            }
        }

        // ── Internal cull pipeline ────────────────────────────────────────────

        private void RecordFrameCommands(
            CommandBuffer  cmd,
            Camera         cam,
            GraphicsBuffer aabbBuffer,
            GraphicsBuffer instanceBuffer,
            GraphicsBuffer rangeBuffer,
            int            chunkCount,
            Vector4[]      frustumPlanes,
            float          maxSqrDistance,
            float          lod0MaxSqrDist,
            float          lod1MaxSqrDist)
        {
            if (this.visibleChunksBuf == null || this.visibleCountBuf == null ||
                this.dispatchArgsBuf  == null  || this.visibleLod0Buf == null ||
                this.visibleLod1Buf   == null  || this.visibleLod2Buf == null ||
                this.argsLod0Buf      == null  || this.argsLod1Buf    == null ||
                this.argsLod2Buf      == null)
                return;

            this.visibleChunksBuf.SetCounterValue(0);
            this.visibleLod0Buf.SetCounterValue(0);
            this.visibleLod1Buf.SetCounterValue(0);
            this.visibleLod2Buf.SetCounterValue(0);

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

            cmd.SetComputeBufferParam(this.computeShader, this.kernelArgs, "visibleCount",  this.visibleCountBuf);
            cmd.SetComputeBufferParam(this.computeShader, this.kernelArgs, "dispatchArgsB", this.dispatchArgsBuf);
            cmd.DispatchCompute(this.computeShader, this.kernelArgs, 1, 1, 1);

            int k = this.kernelBlade;
            cmd.SetComputeBufferParam(this.computeShader, k, "blades",            instanceBuffer);
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

            cmd.CopyCounterValue(this.visibleLod0Buf, this.argsLod0Buf, ARGS_INSTANCE_COUNT_OFFSET);
            cmd.CopyCounterValue(this.visibleLod1Buf, this.argsLod1Buf, ARGS_INSTANCE_COUNT_OFFSET);
            cmd.CopyCounterValue(this.visibleLod2Buf, this.argsLod2Buf, ARGS_INSTANCE_COUNT_OFFSET);

            // Copy each LOD counter to the driver's dedicated count buffers so the readback
            // driver can read exact per-band counts without touching the args buffers.
            if (this.colliderDriver?.Lod0CountBuffer != null)
                cmd.CopyCounterValue(this.visibleLod0Buf, this.colliderDriver.Lod0CountBuffer, 0);
            if (this.colliderDriver?.Lod1CountBuffer != null)
                cmd.CopyCounterValue(this.visibleLod1Buf, this.colliderDriver.Lod1CountBuffer, 0);
            if (this.colliderDriver?.Lod2CountBuffer != null)
                cmd.CopyCounterValue(this.visibleLod2Buf, this.colliderDriver.Lod2CountBuffer, 0);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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

        /// <summary>
        /// Phase 2: Binds PBR textures and sets shader_feature_local keywords on all LOD material clones.
        /// All defaults are OFF — with no toggles enabled the original stylized lighting path runs unchanged.
        /// Props use mesh tangents for normal mapping (imported meshes already have tangent data).
        /// </summary>
        private void ApplyPbrKeywords(ScatterRenderConfig cfg)
        {
            Material?[] mats = { this.lodMat0, this.lodMat1, this.lodMat2 };
            foreach (Material? mat in mats)
            {
                if (mat == null) continue;

                // _NORMALMAP — props use mesh tangents (no blade-basis synthesis needed)
                if (cfg.UseNormalMap && cfg.NormalMap != null)
                {
                    mat.EnableKeyword(KW_NormalMap);
                    mat.SetTexture(ID_NormalMap, cfg.NormalMap);
                    mat.SetFloat(ID_NormalStrength, cfg.NormalStrength);
                }
                else
                {
                    mat.DisableKeyword(KW_NormalMap);
                }

                // _PBR + _MASKMAP (sub-feature inside PBR)
                if (cfg.UsePbr)
                {
                    mat.EnableKeyword(KW_Pbr);
                    if (cfg.MaskMap != null)
                    {
                        mat.EnableKeyword(KW_MaskMap);
                        mat.SetTexture(ID_MaskMap, cfg.MaskMap);
                    }
                    else
                    {
                        mat.DisableKeyword(KW_MaskMap);
                    }
                }
                else
                {
                    mat.DisableKeyword(KW_Pbr);
                    mat.DisableKeyword(KW_MaskMap);
                }

                // _EMISSION
                if (cfg.UseEmission)
                {
                    mat.EnableKeyword(KW_Emission);
                    if (cfg.EmissionMap != null)
                        mat.SetTexture(ID_EmissionMap, cfg.EmissionMap);
                    mat.SetColor(ID_EmissionColor, cfg.EmissionColor);
                }
                else
                {
                    mat.DisableKeyword(KW_Emission);
                }
            }
        }

        /// <summary>
        /// Phase 3: Binds shadow keywords and per-layer shadow params on all LOD material clones.
        /// All defaults are OFF/0 — keyword-off path is byte-identical to the Phase 2 all-OFF baseline.
        /// </summary>
        private void ApplyShadowKeywords(ScatterRenderConfig cfg)
        {
            Material?[] mats = { this.lodMat0, this.lodMat1, this.lodMat2 };
            foreach (Material? mat in mats)
            {
                if (mat == null) continue;

                // _RECEIVE_SHADOWS — enable URP shadow coord lookup (only meaningful when _PBR is also on).
                if (cfg.ReceiveShadows)
                    mat.EnableKeyword(KW_ReceiveShadows);
                else
                    mat.DisableKeyword(KW_ReceiveShadows);

                // _SHADOW_TINT — modulate received shadow term by strength + tint.
                // Only meaningful when _RECEIVE_SHADOWS is also on; kept separate for granular control.
                if (cfg.ReceiveShadows && (cfg.ShadowStrength < 0.999f || cfg.ShadowTint != Color.white))
                {
                    mat.EnableKeyword(KW_ShadowTint);
                    mat.SetFloat(ID_ShadowStrength, cfg.ShadowStrength);
                    mat.SetColor(ID_ShadowTint, cfg.ShadowTint);
                }
                else
                {
                    mat.DisableKeyword(KW_ShadowTint);
                }

                // _ALPHACLIP_SHADOWS — alpha-clip the shadow caster by base-map alpha.
                // Default OFF (solid-quad caster). _Cutoff defaults to 0.5 in the shader property.
                if (cfg.UseAlphaclipShadows)
                    mat.EnableKeyword(KW_AlphaclipShadows);
                else
                    mat.DisableKeyword(KW_AlphaclipShadows);

                // Bias values — always set (zero is the safe default; no keyword needed for 0-offsets).
                mat.SetFloat(ID_ShadowDepthBias,  cfg.ShadowDepthBias);
                mat.SetFloat(ID_ShadowNormalBias, cfg.ShadowNormalBias);
            }
        }

        private RenderParams MakeRenderParams(Material mat, Camera? drawCamera) =>
            new RenderParams(mat)
            {
                worldBounds       = this.worldBounds,
                // Phase 3: ShadowCastingMode driven from config (no hardcoded On); receive from config (no hardcoded true).
                shadowCastingMode = this.shadowCastingMode, // honors layer.Render.ShadowCastingMode
                receiveShadows    = this.receiveShadows,    // honors layer.Render.ReceiveShadows (default false)
                camera            = drawCamera,
                layer             = 0,
            };

        private void InitLodArgs()
        {
            (Mesh? mesh, GraphicsBuffer? buf, int idx)[] lodPairs =
            {
                (this.mesh0, this.argsLod0Buf, 0),
                (this.mesh1, this.argsLod1Buf, 1),
                (this.mesh2, this.argsLod2Buf, 2),
            };

            foreach (var (mesh, buf, idx) in lodPairs)
            {
                if (buf == null) continue;
                if (mesh != null && mesh.GetIndexCount(0) == 0)
                    Debug.LogError($"[InstancedPropEngine] LOD{idx} mesh '{mesh.name}' has 0 indices.");

                var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
                args[0].indexCountPerInstance = (mesh != null) ? mesh.GetIndexCount(0) : 0;
                args[0].instanceCount         = 0;
                args[0].startIndex            = (mesh != null) ? mesh.GetIndexStart(0) : 0;
                args[0].baseVertexIndex       = (mesh != null) ? (uint)mesh.GetBaseVertex(0) : 0;
                args[0].startInstance         = 0;
                buf.SetData(args);
            }
        }

        private static Bounds ComputeMeshBounds(Mesh[] meshLODs)
        {
            bool any = false;
            var result = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Mesh? m in meshLODs)
            {
                if (m == null) continue;
                if (!any) { result = m.bounds; any = true; }
                else        result.Encapsulate(m.bounds);
            }
            if (!any)
                result = new Bounds(Vector3.zero, Vector3.one);
            return result;
        }

        private static void SafeDestroy(UnityEngine.Object? o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
