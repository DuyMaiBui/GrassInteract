#nullable enable
using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract
{
    /// <summary>
    /// GPU-indirect engine for <see cref="InstanceScatterLayer"/> mesh props. Replaces the former
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
        private static readonly int ID_CamPosWS              = Shader.PropertyToID("_CamPosWS");
        private static readonly int ID_InstanceTilt          = Shader.PropertyToID("_InstanceTilt");
        private static readonly int ID_TiltEnabled           = Shader.PropertyToID("_TiltEnabled");

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
        private GraphicsBuffer? argsLod1Buf;
        private GraphicsBuffer? argsLod2Buf;

        // ── Static GPU data ───────────────────────────────────────────────────
        private ChunkedInstanceBuffer? instanceBuffer;

        // ── Per-instance colliders (Play-mode only) ───────────────────────────
        private GameObject?            colliderRoot;
        private InstanceColliderPool?  colliderPool;
        private InstanceFrustumCuller? colliderCuller;

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

            Vector2 effectiveBounds = sampler is TerrainSurfaceSampler tss
                ? tss.TerrainSizeXZ
                : layer.FieldBounds;

            this.instanceBuffer = new ChunkedInstanceBuffer();
            this.instanceBuffer.Bake(
                scatter, origin, effectiveBounds, layer.ScaleRange.y,
                meshBounds, oriented: layer.IsOriented, chunkSize: 8);

            // Phase-H: pooled + culled per-instance colliders.
            this.BuildColliderRuntime(layer);

            // Rigid-tilt sim (InstanceScatterLayer only, when interactor-tilt is enabled). Reads the BAKED
            // instance order from instanceBuffer.Instances so the tilt buffer index == _Instances index.
            var instLayer = layer as InstanceScatterLayer;
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
            float d0 = dists.Length > 0 ? dists[0] : 12f;
            float d1 = dists.Length > 1 ? dists[1] : 30f;
            this.lod0MaxSqrDist = d0 * d0;
            this.lod1MaxSqrDist = d1 * d1;
            float minCullSqr = Application.isPlaying ? 250000f : 1e8f;
            this.maxSqrDistance = Mathf.Max(this.lod1MaxSqrDist * 4f, minCullSqr);

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
            Shader.SetGlobalFloat(ID_ScaleMax2, this.instanceBuffer.ScaleMax);

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

            if (this.instanceBuffer.InstanceBuffer != null)
                Shader.SetGlobalBuffer(ID_Instances, this.instanceBuffer.InstanceBuffer);
            Shader.SetGlobalFloat(ID_ScaleMax2, this.instanceBuffer.ScaleMax);

            if (this.tiltSim?.TiltBuffer != null)
                Shader.SetGlobalBuffer(ID_InstanceTilt, this.tiltSim.TiltBuffer);

            if (this.affectedByWind)
            {
                Vector3 camPos = cullCam.transform.position;
                Vector2 cfgDir = this.windSnapshotDir;
                Shader.SetGlobalFloat(ID_GrassTime,      this.deformTime);
                Shader.SetGlobalVector(ID_WindDir,        new Vector4(cfgDir.x, cfgDir.y, 0f, 0f));
                Shader.SetGlobalFloat(ID_WindStrength,    this.windSnapshotStrength);
                Shader.SetGlobalFloat(ID_WindFrequency,   this.windSnapshotFrequency);
                Shader.SetGlobalFloat(ID_WindNoiseScale,  this.windSnapshotNoiseScale);
                Shader.SetGlobalFloat(ID_BendStrength,    this.windSnapshotBendStrength);
                Shader.SetGlobalFloat(ID_Flatten,         this.windSnapshotFlatten);
                Shader.SetGlobalVector(ID_CamPosWS,       new Vector4(camPos.x, camPos.y, camPos.z, 0f));
            }

            if (this.affectedByInteractors && this.interactorBuffer != null)
            {
                this.interactorBuffer.Upload(GrassInteractor.Active);
                Shader.SetGlobalBuffer(ID_Interactors,      this.interactorBuffer.Buffer);
                Shader.SetGlobalInteger(ID_InteractorCount, this.interactorBuffer.Count);
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
            this.colliderPool  = null;
            this.colliderCuller = null; // component lives on colliderRoot — destroyed below
            SafeDestroy(this.colliderRoot); this.colliderRoot = null;

            SafeDestroy(this.lodMat0); this.lodMat0 = null;
            SafeDestroy(this.lodMat1); this.lodMat1 = null;
            SafeDestroy(this.lodMat2); this.lodMat2 = null;

            this.isBuilt = false;
        }

        // ── Per-instance colliders (Play-mode only) ───────────────────────────

        private void BuildColliderRuntime(ScatterLayer layer)
        {
            if (layer is not InstanceScatterLayer instLayer) return;
            if (!Application.isPlaying) return;
            if (!instLayer.AnyRecordWantsCollider()) return;

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
            this.colliderPool.Init(instLayer.PoolCap, layerDefaultMesh, instLayer.DefaultColliderConvex);
            this.colliderPool.Prewarm(Mathf.Min(records.Length, instLayer.PoolCap));

            int count           = records.Length;
            var positions       = new Vector3[count];
            var rotations       = new Quaternion[count];
            var scales          = new float[count];
            var meshes          = new Mesh?[count];
            var convexFlags     = new bool[count];
            var wantsCollider   = new bool[count];

            for (int i = 0; i < count; ++i)
            {
                InstanceRecord rec = records[i];

                Mesh? colMesh = null;
                if ((rec.overrideMask & InstanceOverrideMask.ColliderConfigured) != 0 &&
                    rec.colliderMeshRefIndex >= 0)
                {
                    colMesh = authored.GetObjectRef(rec.colliderMeshRefIndex) as Mesh;
                }
                colMesh ??= layerDefaultMesh;

                if (rec.generateCollider && colMesh == null)
                {
                    Debug.LogWarning(
                        $"[InstancedPropEngine] Record {i}: generateCollider=true but no collider mesh " +
                        "available (no per-record override, no layer default, no lod0 mesh) — skipping.");
                }

                positions[i]     = rec.position;
                rotations[i]     = rec.rotation;
                scales[i]        = rec.scale * rec.colliderScale;
                meshes[i]        = colMesh;
                convexFlags[i]   = rec.colliderConvex;
                wantsCollider[i] = rec.generateCollider && colMesh != null;
            }

            if (instLayer.CullColliders)
            {
                this.colliderCuller = rootT.gameObject.AddComponent<InstanceFrustumCuller>();
                this.colliderCuller.Init(null /* Camera.main at runtime */, instLayer.CullDistance, this.colliderPool);
                this.colliderCuller.SetRecords(positions, rotations, scales, meshes, convexFlags, wantsCollider);
                Debug.Log(
                    $"[InstancedPropEngine] Collider runtime: pool cap={instLayer.PoolCap}, " +
                    $"cullDist={instLayer.CullDistance}m, records={count}.");
            }
            else
            {
                int acquired = 0;
                for (int i = 0; i < count; ++i)
                {
                    if (!wantsCollider[i]) continue;
                    MeshCollider? mc = this.colliderPool.Acquire(
                        i, positions[i], rotations[i], scales[i], meshes[i], convexFlags[i]);
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
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private RenderParams MakeRenderParams(Material mat, Camera? drawCamera) =>
            new RenderParams(mat)
            {
                worldBounds       = this.worldBounds,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                receiveShadows    = true,
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
