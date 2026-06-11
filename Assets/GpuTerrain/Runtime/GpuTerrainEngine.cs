#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GpuTerrain
{
    /// <summary>
    /// GPU-driven CDLOD terrain engine for one tile.
    /// Build → Step → Submit → Dispose.
    ///
    /// Submit discipline (mirrors GrassGpuEngine verbatim — Risk-20 mitigation):
    /// - Non-zero worldBounds on RenderParams.
    /// - RenderParams via Material constructor (preserves renderingLayerMask).
    /// - Bind buffers to material directly — NEVER rp.matProps (MPB silently dropped under URP RenderGraph).
    /// - Rebind buffers each Submit (domain-reload guard).
    /// - CopyCounterValue writes instanceCount into IndirectArguments buffer (same pattern as GrassGpuEngine).
    /// </summary>
    public sealed class GpuTerrainEngine : IDisposable
    {
        // ── Constants ─────────────────────────────────────────────────────────
        // Byte offset of instanceCount in IndirectDrawIndexedArgs (second uint = 4 bytes in).
        private const uint ARGS_INSTANCE_COUNT_OFFSET = 4;

        // ── Property ID cache ─────────────────────────────────────────────────
        private static readonly int ID_NodeBuffer         = Shader.PropertyToID("_NodeBuffer");
        private static readonly int ID_VisibleNodeIndices = Shader.PropertyToID("_VisibleNodeIndices");
        private static readonly int ID_MinHeight          = Shader.PropertyToID("_MinHeight");
        private static readonly int ID_MaxHeight          = Shader.PropertyToID("_MaxHeight");
        private static readonly int ID_HeightTex          = Shader.PropertyToID("_HeightTex");

        // ── Injected ──────────────────────────────────────────────────────────
        private readonly ComputeShader computeShader;
        private readonly Material      sourceMaterial;

        // ── Kernel index ──────────────────────────────────────────────────────
        private readonly int kernelNodeCull;

        // ── GPU buffers ────────────────────────────────────────────────────────
        private GraphicsBuffer? visibleNodesBuf; // Append: visible node indices
        private GraphicsBuffer? indirectArgsBuf; // IndirectArguments: draw args

        // ── Scene data ────────────────────────────────────────────────────────
        private TerrainTileGpuResources? gpuResources;
        private TerrainTileAsset?        tileAsset;
        private CdlodQuadtree?           quadtree;
        private TerrainNodeBuffer?       terrainNodeBuf;

        // ── Render objects ────────────────────────────────────────────────────
        private Material? patchMaterial;
        private Mesh?     patchMesh;
        private Bounds    worldBounds;

        // ── CommandBuffer (reused) ─────────────────────────────────────────────
        private CommandBuffer? cullCmd;

        // ── Frustum planes (reused, no alloc) ────────────────────────────────
        private readonly Vector4[] frustumPlanes = new Vector4[6];
        private readonly Plane[]   planeScratch  = new Plane[6];

        // ── State ─────────────────────────────────────────────────────────────
        private bool isBuilt;

        // ── Construction ──────────────────────────────────────────────────────

        public GpuTerrainEngine(ComputeShader computeShader, Material patchMaterial)
        {
            this.computeShader  = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.sourceMaterial = patchMaterial ?? throw new ArgumentNullException(nameof(patchMaterial));
            this.kernelNodeCull = computeShader.FindKernel("NodeCull");
        }

        // ── Build ─────────────────────────────────────────────────────────────

        public void Build(TerrainTileAsset tile, TerrainTileGpuResources gpuRes, float[] lodRangesM)
        {
            this.Dispose();

            if (!tile.IsHeightValid)
                throw new InvalidOperationException("[GpuTerrainEngine] Tile height data invalid.");

            this.tileAsset    = tile;
            this.gpuResources = gpuRes;

            // Tile world bounds (non-zero extent required — RenderGraph culls zero-extent draws)
            Vector2 origin = TerrainWorldGrid.TileOriginWorld(tile.tileCoord);
            this.worldBounds = new Bounds(
                new Vector3(origin.x + TerrainWorldGrid.TILE_SIZE_M * 0.5f,
                            (tile.minHeight + tile.maxHeight) * 0.5f,
                            origin.y + TerrainWorldGrid.TILE_SIZE_M * 0.5f),
                new Vector3(TerrainWorldGrid.TILE_SIZE_M,
                            Mathf.Max(1f, tile.maxHeight - tile.minHeight),
                            TerrainWorldGrid.TILE_SIZE_M));

            // CDLOD quadtree
            this.quadtree = new CdlodQuadtree(
                origin.x, origin.y,
                tile.minHeight, tile.maxHeight,
                TerrainWorldGrid.TILE_SIZE_M,
                lodRangesM,
                tileIdx: 0);

            // Node buffer
            this.terrainNodeBuf = new TerrainNodeBuffer();

            // Patch mesh (shared singleton)
            this.patchMesh = TerrainPatchMesh.GetOrCreate();

            if (this.patchMesh.GetIndexCount(0) == 0)
                Debug.LogError("[GpuTerrainEngine] Patch mesh has 0 indices — draw will render nothing.");

            // Cull buffers
            int maxNodes = 512; // safe upper bound for one tile
            this.visibleNodesBuf = new GraphicsBuffer(
                GraphicsBuffer.Target.Append, maxNodes, sizeof(uint));

            // Indirect draw args (5 uint32 = IndirectDrawIndexedArgs)
            this.indirectArgsBuf = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments,
                1, GraphicsBuffer.IndirectDrawIndexedArgs.size);

            var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
            args[0].indexCountPerInstance = this.patchMesh.GetIndexCount(0);
            args[0].instanceCount         = 0;
            args[0].startIndex            = this.patchMesh.GetIndexStart(0);
            args[0].baseVertexIndex       = (uint)this.patchMesh.GetBaseVertex(0);
            args[0].startInstance         = 0;
            this.indirectArgsBuf.SetData(args);

            // Per-material clone (Material ctor — preserves renderingLayerMask)
            this.patchMaterial = new Material(this.sourceMaterial)
            {
                name = "TerrainPatch_Instance"
            };
            this.patchMaterial.SetFloat(ID_MinHeight, tile.minHeight);
            this.patchMaterial.SetFloat(ID_MaxHeight, tile.maxHeight);
            if (gpuRes.HeightTexture != null)
                this.patchMaterial.SetTexture(ID_HeightTex, gpuRes.HeightTexture);

            this.cullCmd = new CommandBuffer { name = "GpuTerrainEngine.Cull" };
            this.isBuilt = true;
        }

        // ── Step ─────────────────────────────────────────────────────────────

        public void Step(float dt) { /* No per-frame CPU sim for terrain. */ }

        // ── Submit ────────────────────────────────────────────────────────────

        public void Submit(Camera? targetCamera, Vector3 cameraPos)
        {
            if (!this.isBuilt || this.quadtree == null || this.terrainNodeBuf == null ||
                this.cullCmd == null || this.patchMesh == null || this.patchMaterial == null)
                return;

            Camera? cullCam = targetCamera ?? Camera.main;
            if (cullCam == null) return;

            // 1. CDLOD quadtree selection (CPU)
            IReadOnlyList<CdlodNode> nodes = this.quadtree.Select(cameraPos);
            if (nodes.Count == 0) return;

            // 2. Upload nodes + AABBs to GPU
            if (this.tileAsset == null) return;
            this.terrainNodeBuf.Upload(nodes, this.tileAsset.minHeight, this.tileAsset.maxHeight);

            // 3. Rebind node buffer to material (domain-reload guard — matches GrassGpuEngine discipline)
            if (this.terrainNodeBuf.NodeBuffer != null)
                this.patchMaterial.SetBuffer(ID_NodeBuffer, this.terrainNodeBuf.NodeBuffer);
            if (this.visibleNodesBuf != null)
                this.patchMaterial.SetBuffer(ID_VisibleNodeIndices, this.visibleNodesBuf);

            // 4. Frustum planes
            GeometryUtility.CalculateFrustumPlanes(cullCam, this.planeScratch);
            for (int i = 0; i < 6; ++i)
                this.frustumPlanes[i] = new Vector4(
                    this.planeScratch[i].normal.x,
                    this.planeScratch[i].normal.y,
                    this.planeScratch[i].normal.z,
                    this.planeScratch[i].distance);

            // 5. Cull CommandBuffer
            this.cullCmd.Clear();
            this.RecordCullCommands(cullCam, nodes.Count);
            Graphics.ExecuteCommandBuffer(this.cullCmd);

            // 6. RenderMeshIndirect — SSOT submit discipline
            if (this.indirectArgsBuf != null)
                Graphics.RenderMeshIndirect(
                    this.MakeRenderParams(targetCamera),
                    this.patchMesh,
                    this.indirectArgsBuf,
                    commandCount: 1,
                    startCommand: 0);
        }

        // ── SelfTest ─────────────────────────────────────────────────────────

        /// <summary>
        /// Mirrors GrassGpuEngine.SelfTest: exercises CommandBuffer recording and
        /// RenderMeshIndirect signature acceptance without throwing.
        /// </summary>
        public bool SelfTest(out string reason)
        {
            if (!this.isBuilt || this.patchMesh == null || this.patchMaterial == null ||
                this.indirectArgsBuf == null || this.cullCmd == null)
            {
                reason = "GPU self-test: SKIP (engine not fully built)";
                return true;
            }

            try
            {
                using var probeCmd = new CommandBuffer { name = "TerrainEngine.SelfTest" };
                probeCmd.BeginSample("TerrainSelfTest");
                probeCmd.EndSample("TerrainSelfTest");
                Graphics.ExecuteCommandBuffer(probeCmd);

                Graphics.RenderMeshIndirect(
                    this.MakeRenderParams(null),
                    this.patchMesh,
                    this.indirectArgsBuf,
                    commandCount: 1,
                    startCommand: 0);

                reason = $"GPU self-test: OK (device={SystemInfo.graphicsDeviceName})";
                return true;
            }
            catch (Exception ex)
            {
                reason = $"GPU self-test: FAIL ({ex.GetType().Name}: {ex.Message})";
                return false;
            }
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            this.cullCmd?.Dispose();           this.cullCmd         = null;
            this.visibleNodesBuf?.Release();   this.visibleNodesBuf = null;
            this.indirectArgsBuf?.Release();   this.indirectArgsBuf = null;
            this.terrainNodeBuf?.Dispose();    this.terrainNodeBuf  = null;
            SafeDestroy(this.patchMaterial);   this.patchMaterial   = null;
            this.isBuilt = false;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void RecordCullCommands(Camera cam, int nodeCount)
        {
            if (this.visibleNodesBuf == null || this.indirectArgsBuf == null ||
                this.terrainNodeBuf?.AabbBuffer == null) return;

            // Reset append counter
            this.visibleNodesBuf.SetCounterValue(0);

            // NodeCull dispatch
            int k = this.kernelNodeCull;
            this.cullCmd!.SetComputeBufferParam(this.computeShader, k, "nodeAabbs",
                this.terrainNodeBuf.AabbBuffer);
            this.cullCmd.SetComputeIntParam(this.computeShader, "nodeCount", nodeCount);
            this.cullCmd.SetComputeVectorArrayParam(this.computeShader, "frustumPlanes",
                this.frustumPlanes);
            Vector3 camPos = cam.transform.position;
            this.cullCmd.SetComputeVectorParam(this.computeShader, "camPosWS",
                new Vector4(camPos.x, camPos.y, camPos.z, 0f));
            this.cullCmd.SetComputeFloatParam(this.computeShader, "maxCullSqrDistance", 0f);
            this.cullCmd.SetComputeBufferParam(this.computeShader, k, "visibleNodes",
                this.visibleNodesBuf);

            int groups = Mathf.CeilToInt((float)nodeCount / 64);
            this.cullCmd.DispatchCompute(this.computeShader, k, groups, 1, 1);

            // CopyCounterValue → instanceCount field of draw args (byte offset 4)
            // Mirrors GrassGpuEngine.RecordFrameCommands CopyCount → argsLod0Buf.
            this.cullCmd.CopyCounterValue(
                this.visibleNodesBuf,
                this.indirectArgsBuf,
                ARGS_INSTANCE_COUNT_OFFSET);
        }

        private RenderParams MakeRenderParams(Camera? drawCamera)
        {
            return new RenderParams(this.patchMaterial!)
            {
                worldBounds       = this.worldBounds, // non-zero — RenderGraph culls zero-extent draws
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                receiveShadows    = true,
                camera            = drawCamera,
                layer             = 0,
            };
        }

        private static void SafeDestroy(UnityEngine.Object? o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
