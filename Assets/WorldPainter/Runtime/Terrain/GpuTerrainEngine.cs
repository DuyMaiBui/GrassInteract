#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter
{
    /// <summary>
    /// Minimal interface for a per-tile terrain engine: submit a frame and dispose GPU resources.
    /// Used by TerrainTileResidencySet.ResidentTile so tests can inject a no-op engine
    /// without a real ComputeShader (GpuTerrainEngine constructor requires non-null shaders).
    /// </summary>
    public interface ITerrainEngine : IDisposable
    {
        void Submit(Camera? targetCamera, Vector3 cameraPos);
    }

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
    public sealed class GpuTerrainEngine : ITerrainEngine
    {
        // ── Constants ─────────────────────────────────────────────────────────
        // Byte offset of instanceCount in IndirectDrawIndexedArgs (second uint = 4 bytes in).
        private const uint ARGS_INSTANCE_COUNT_OFFSET = 4;

        // Minimum skirt depth (metres) so flat/low tiles still drop a visible curtain.
        private const float MIN_SKIRT_DEPTH_M = 2f;

        /// <summary>
        /// Metres skirt-border vertices drop to cover inter-tile LOD-seam cracks. A seam gap can
        /// never exceed the tile's height variation, so the full range guarantees coverage; floored
        /// so flat tiles still skirt. SSOT — drives both the _SkirtDepth binding AND the draw bounds
        /// Y expansion (the skirt descends below minHeight, so the cull bounds must include it).
        /// </summary>
        private static float SkirtDepthFor(TerrainTileAsset tile) =>
            Mathf.Max(MIN_SKIRT_DEPTH_M, tile.maxHeight - tile.minHeight);

        // ── Property ID cache ─────────────────────────────────────────────────
        private static readonly int ID_NodeBuffer         = Shader.PropertyToID("_NodeBuffer");
        private static readonly int ID_VisibleNodeIndices = Shader.PropertyToID("_VisibleNodeIndices");
        private static readonly int ID_MinHeight          = Shader.PropertyToID("_MinHeight");
        private static readonly int ID_MaxHeight          = Shader.PropertyToID("_MaxHeight");
        private static readonly int ID_HeightTex          = Shader.PropertyToID("_HeightTex");
        // Phase 5b: baked normal texture + live-sculpt keyword.
        private static readonly int ID_BakedNormalTex     = Shader.PropertyToID("_BakedNormalTex");
        private const string KEYWORD_LIVE_SCULPT          = "_WP_LIVE_SCULPT";
        // B1 fix: tile-local UV uniforms so non-(0,0) tiles sample the correct texel region.
        // Also resolves MINOR-2: TILE_SIZE_M is no longer a hardcoded literal in the shader.
        private static readonly int ID_TileOriginWS       = Shader.PropertyToID("_TileOriginWS");
        private static readonly int ID_TileSizeM          = Shader.PropertyToID("_TileSizeM");
        // Skirt depth (inter-tile CDLOD crack fix): metres skirt-border vertices drop.
        private static readonly int ID_SkirtDepth         = Shader.PropertyToID("_SkirtDepth");
        // Phase 2a — TerrainPalette + alphamap bindings.
        private static readonly int ID_TerrainPaletteArray  = Shader.PropertyToID(TerrainShadingConfig.PROPERTY_PALETTE_ARRAY);
        private static readonly int ID_TerrainPaletteCount  = Shader.PropertyToID(TerrainShadingConfig.PROPERTY_PALETTE_COUNT);
        private static readonly int ID_TerrainPaletteTilings = Shader.PropertyToID(TerrainShadingConfig.PROPERTY_PALETTE_TILINGS);
        private static readonly int ID_TerrainAlphamapCount = Shader.PropertyToID(TerrainShadingConfig.PROPERTY_ALPHAMAP_COUNT);
        private static readonly int[] ID_TerrainAlphamap    =
        {
            Shader.PropertyToID(TerrainShadingConfig.PROPERTY_ALPHAMAP_0),
            Shader.PropertyToID(TerrainShadingConfig.PROPERTY_ALPHAMAP_1),
            Shader.PropertyToID(TerrainShadingConfig.PROPERTY_ALPHAMAP_2),
            Shader.PropertyToID(TerrainShadingConfig.PROPERTY_ALPHAMAP_3),
        };

        // ── Injected ──────────────────────────────────────────────────────────
        private readonly ComputeShader computeShader;
        private readonly Material      sourceMaterial;

        // Root-transform binder (painting↔world). Null or identity → cull runs in world space
        // (which equals painting space at identity). Bound by WorldPainter after Build.
        private WorldRootBinder? rootSpace;

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

        // [terrain-build-diag] one-shot guard for the Submit-path diagnostic log.
        private bool loggedSubmitDiag;

        // [terrain-build-diag] one-shot guard for the post-cull indirect-args readback.
        private bool loggedDrawDiag;

        // ── Construction ──────────────────────────────────────────────────────

        public GpuTerrainEngine(ComputeShader computeShader, Material patchMaterial)
        {
            this.computeShader  = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
            this.sourceMaterial = patchMaterial ?? throw new ArgumentNullException(nameof(patchMaterial));
            this.kernelNodeCull = computeShader.FindKernel("NodeCull");
        }

        // ── Test accessors ────────────────────────────────────────────────────

        /// <summary>
        /// World-space XZ min-corner of the built tile. Zero-vector when not built.
        /// Internal — used only by EditMode regression tests (B1 fix verification).
        /// </summary>
        internal Vector2 TileOriginWS { get; private set; }

        // ── Internal sculpt seam ──────────────────────────────────────────────

        /// <summary>The committed height Texture2D for this tile (null when not built).</summary>
        internal Texture2D? HeightTexture => this.gpuResources?.HeightTexture;

        /// <summary>The live GPU resources for this tile (null when not built).</summary>
        internal TerrainTileGpuResources? GpuResources => this.gpuResources;

        /// <summary>
        /// Bind the working RenderTexture as _HeightTex for instant VTF preview.
        /// Decode parity: RT stores normalized [0,1]; SampleHeightVTF produces same range.
        ///
        /// Phase 5b: also forces _WP_LIVE_SCULPT so the normal is re-derived from the live
        /// height RT rather than the now-stale baked normal texture during a stroke.
        /// </summary>
        internal void BeginSculptPreview(RenderTexture rt)
        {
            if (this.patchMaterial != null)
            {
                this.patchMaterial.SetTexture(ID_HeightTex, rt);
                // Phase 5b: force live derivation during sculpting — baked normal is stale.
                this.patchMaterial.EnableKeyword(KEYWORD_LIVE_SCULPT);
            }
        }

        /// <summary>
        /// Rebind the committed Texture2D to restore normal rendering after stroke.
        ///
        /// Phase 5b: restore the baked-normal keyword state — re-enable baked fetch if a
        /// baked normal is available; leave _WP_LIVE_SCULPT on if there is none.
        /// </summary>
        internal void EndSculptPreview()
        {
            if (this.patchMaterial == null) return;
            if (this.gpuResources?.HeightTexture != null)
                this.patchMaterial.SetTexture(ID_HeightTex, this.gpuResources.HeightTexture);
            // Phase 5b: restore keyword to match whether a baked normal is available.
            if (this.gpuResources?.NormalTexture != null)
                this.patchMaterial.DisableKeyword(KEYWORD_LIVE_SCULPT);
            else
                this.patchMaterial.EnableKeyword(KEYWORD_LIVE_SCULPT);
        }

        /// <summary>
        /// Bind the live-painted alphamap RenderTexture at slot <paramref name="alphamapIdx"/>
        /// so the terrain shader samples it directly during a brush stroke. Without this the
        /// shader would still see the stale committed <see cref="Texture2D"/> until the
        /// throttled async readback completes — making fast drags look like sparse dots.
        /// </summary>
        internal void BeginAlphamapPreview(int alphamapIdx, Texture rt)
        {
            if (this.patchMaterial == null) return;
            if (alphamapIdx < 0 || alphamapIdx >= ID_TerrainAlphamap.Length) return;
            this.patchMaterial.SetTexture(ID_TerrainAlphamap[alphamapIdx], rt);
        }

        /// <summary>Restore the committed Texture2D alphamap binding after a stroke ends.</summary>
        internal void EndAlphamapPreview(int alphamapIdx)
        {
            if (this.patchMaterial == null || this.tileAsset == null) return;
            if (alphamapIdx < 0 || alphamapIdx >= ID_TerrainAlphamap.Length) return;

            var alphamaps = this.tileAsset.Alphamaps;
            Texture tex = (alphamaps != null && alphamapIdx < this.tileAsset.AlphamapCount
                           && alphamaps[alphamapIdx] != null)
                ? (Texture)alphamaps[alphamapIdx]
                : Texture2D.blackTexture;
            this.patchMaterial.SetTexture(ID_TerrainAlphamap[alphamapIdx], tex);
        }

        // ── Build ─────────────────────────────────────────────────────────────

        public void Build(TerrainTileAsset tile, TerrainTileGpuResources gpuRes, float[] lodRangesM)
        {
            this.Dispose();

            if (!tile.IsHeightValid)
                throw new InvalidOperationException("[GpuTerrainEngine] Tile height data invalid.");

            this.tileAsset    = tile;
            this.gpuResources = gpuRes;

            // Tile world bounds (non-zero extent required — RenderGraph culls zero-extent draws).
            // Y range = [minHeight - skirtDepth, maxHeight]: the skirt drops vertices BELOW
            // minHeight, so the cull bounds must include the curtain or an edge-on camera framing
            // only the skirt would cull the tile and re-expose the very crack the skirt covers.
            Vector2 origin = TerrainWorldGrid.TileOriginWorld(tile.tileCoord);
            float skirtDepth = SkirtDepthFor(tile);
            float boundsBottomY = tile.minHeight - skirtDepth;
            float boundsTopY    = tile.maxHeight;
            this.worldBounds = new Bounds(
                new Vector3(origin.x + TerrainWorldGrid.TILE_SIZE_M * 0.5f,
                            (boundsBottomY + boundsTopY) * 0.5f,
                            origin.y + TerrainWorldGrid.TILE_SIZE_M * 0.5f),
                new Vector3(TerrainWorldGrid.TILE_SIZE_M,
                            Mathf.Max(1f, boundsTopY - boundsBottomY),
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
                WpLog.Error("[GpuTerrainEngine] Patch mesh has 0 indices — draw will render nothing.");

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
            // new Material(Material) does NOT reliably copy material-LOCAL shader keywords
            // across Unity versions, so the clone would silently drop toggles enabled on the
            // source asset (e.g. _TERRAIN_STOCHASTIC anti-tiling) and render the OFF variant.
            // Propagate the enabled keyword set explicitly so material-level toggles actually
            // reach the rendered clone. Source material stays the SSOT for these toggles.
            this.patchMaterial.shaderKeywords = this.sourceMaterial.shaderKeywords;
            this.patchMaterial.SetFloat(ID_MinHeight, tile.minHeight);
            this.patchMaterial.SetFloat(ID_MaxHeight, tile.maxHeight);
            if (gpuRes.HeightTexture != null)
                this.patchMaterial.SetTexture(ID_HeightTex, gpuRes.HeightTexture);

            // Phase 5b: bind baked normal texture and set keyword.
            // When the normal texture is available, enable the baked-fetch path in the shader.
            // When absent (pre-Phase-5 tiles), enable _WP_LIVE_SCULPT so the shader falls back
            // to the 4-tap height derivation path.  Editor sculpting also forces _WP_LIVE_SCULPT
            // (see BeginSculptPreview) so live painting still works regardless.
            if (gpuRes.NormalTexture != null)
            {
                this.patchMaterial.SetTexture(ID_BakedNormalTex, gpuRes.NormalTexture);
                this.patchMaterial.DisableKeyword(KEYWORD_LIVE_SCULPT);
            }
            else
            {
                this.patchMaterial.EnableKeyword(KEYWORD_LIVE_SCULPT);
            }

            // Phase 2a — per-tile alphamaps (one RGBA32 per 4 palette layers). Bound here
            // so the tile material clone carries its own per-tile binding; the map-level
            // palette array is set by BindPalette (shared across tiles).
            BindTileAlphamaps(this.patchMaterial, tile);

            // B1 fix + MINOR-2: bind tile-local UV uniforms.
            // origin is already computed above; pass it as a Vector4 for SetVector
            // (shader reads it as float2 — x,y components used; zw ignored).
            this.patchMaterial.SetVector(ID_TileOriginWS, new Vector4(origin.x, origin.y, 0f, 0f));
            this.patchMaterial.SetFloat(ID_TileSizeM, TerrainWorldGrid.TILE_SIZE_M);
            this.TileOriginWS = origin;

            // Skirt depth (SSOT: SkirtDepthFor, same value used for the draw-bounds Y expansion).
            this.patchMaterial.SetFloat(ID_SkirtDepth, skirtDepth);

            this.cullCmd = new CommandBuffer { name = "GpuTerrainEngine.Cull" };
            this.isBuilt = true;
        }

        // ── Layer-count keyword names (Phase 5d) ──────────────────────────────
        private const string KEYWORD_LAYERS_4  = "_TERRAIN_LAYERS_4";
        private const string KEYWORD_LAYERS_8  = "_TERRAIN_LAYERS_8";
        private const string KEYWORD_LAYERS_16 = "_TERRAIN_LAYERS_16";

        // ── Terrain palette binding (Phase 2a) ────────────────────────────────

        /// <summary>
        /// Binds the map-level TerrainLayer palette array + per-layer tilings onto this
        /// tile's material clone. Call AFTER <see cref="Build"/>. The array is owned by
        /// <see cref="TerrainPaletteBinder"/>; this engine only references it.
        ///
        /// Phase 5d: also sets the layer-count variant keyword on the material so the shader
        /// compiler can eliminate dead alphamap samples for tiles with few layers.
        /// </summary>
        internal void BindPalette(TerrainPaletteBinder binder)
        {
            if (this.patchMaterial == null || binder == null) return;
            if (binder.Array != null)
                this.patchMaterial.SetTexture(ID_TerrainPaletteArray, binder.Array);
            this.patchMaterial.SetInt(ID_TerrainPaletteCount, binder.ActiveCount);
            this.patchMaterial.SetVectorArray(ID_TerrainPaletteTilings, binder.Tilings);

            // Phase 5d: set layer-count keyword so the shader loop cap matches the actual tile.
            // Keyword is EXCLUSIVE — enable the correct bucket, disable the others.
            // Buckets: ≤4 layers → _TERRAIN_LAYERS_4, ≤8 → _TERRAIN_LAYERS_8, else _TERRAIN_LAYERS_16.
            int layerCount = binder.ActiveCount;
            this.patchMaterial.DisableKeyword(KEYWORD_LAYERS_4);
            this.patchMaterial.DisableKeyword(KEYWORD_LAYERS_8);
            this.patchMaterial.DisableKeyword(KEYWORD_LAYERS_16);
            if (layerCount <= 4)
                this.patchMaterial.EnableKeyword(KEYWORD_LAYERS_4);
            else if (layerCount <= 8)
                this.patchMaterial.EnableKeyword(KEYWORD_LAYERS_8);
            else
                this.patchMaterial.EnableKeyword(KEYWORD_LAYERS_16);
        }

        /// <summary>
        /// Bind the WorldPainter root-transform binder so CDLOD selection + frustum culling run in
        /// PAINTING space (matching the painting-space node positions). Identity root → no transform.
        /// </summary>
        internal void BindRootSpace(WorldRootBinder binder) => this.rootSpace = binder;

        /// <summary>
        /// Pushes <paramref name="tile"/>.alphamaps[] onto <paramref name="material"/>.
        /// Slots beyond the tile's count are bound to <c>Texture2D.blackTexture</c> so the
        /// shader can safely read them when the loop body short-circuits on count.
        ///
        /// Phase 5c: generates mips on any alphamap that was created pre-Phase-5 (mipmapCount==1).
        /// New assets are created with mipChain:true in WorldMapAssetLifecycle.CreateBlankAlphamap,
        /// but existing serialized assets may still have the old mipChain:false state.  Generating
        /// mips at bind time is a one-off O(N_texels) CPU cost per cold Build; subsequent rebuilds
        /// skip it (mipmapCount already >1 after the first Apply).
        /// </summary>
        private static void BindTileAlphamaps(Material material, TerrainTileAsset tile)
        {
            int count = tile.AlphamapCount;
            material.SetInt(ID_TerrainAlphamapCount, Mathf.Min(count, ID_TerrainAlphamap.Length));

            var alphamaps = tile.Alphamaps;
            for (int i = 0; i < ID_TerrainAlphamap.Length; i++)
            {
                Texture tex;
                if (i < count && alphamaps[i] != null)
                {
                    var map = alphamaps[i];
                    // Phase 5c: back-compat mip generation for pre-Phase-5 assets.
                    // isReadable guards against assets imported with makeNoLongerReadable:true
                    // (shouldn't happen for alphamaps, but guarded for safety).
                    if (map.mipmapCount <= 1 && map.isReadable)
                        map.Apply(updateMipmaps: true, makeNoLongerReadable: false);
                    tex = map;
                }
                else
                {
                    tex = Texture2D.blackTexture;
                }
                material.SetTexture(ID_TerrainAlphamap[i], tex);
            }
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
            if (cullCam == null)
            {
                if (!this.loggedSubmitDiag)
                {
                    this.loggedSubmitDiag = true;
                    WpLog.Warning("[GpuTerrainEngine] Submit BAIL: cullCam null (targetCamera null AND no Camera.main) → terrain not drawn.");
                }
                return;
            }

            // ── Whole-tile frustum gate ─────────────────────────────────────
            // Skip the entire submit (quadtree select + node upload + cull dispatch + forward and
            // shadow indirect draws) when this tile's world-space AABB is fully outside the camera
            // frustum. With many resident tiles most are off-screen each frame, yet the cull compute
            // and draws otherwise run unconditionally (URP culls only rasterization, not the
            // dispatch). worldBounds already includes the skirt Y-depth, so a fully-outside box has
            // nothing visible. planeScratch holds WORLD-space planes (reused for the cull below).
            GeometryUtility.CalculateFrustumPlanes(cullCam, this.planeScratch);
            Bounds worldTileBounds = (this.rootSpace != null && !this.rootSpace.IsIdentity)
                ? this.rootSpace.PaintingBoundsToWorld(this.worldBounds)
                : this.worldBounds;
            bool inFrustum = GeometryUtility.TestPlanesAABB(this.planeScratch, worldTileBounds);
            if (!this.loggedSubmitDiag)
            {
                this.loggedSubmitDiag = true;
                WpLog.Log($"[GpuTerrainEngine] Submit diag: cullCam='{cullCam.name}', targetCamNull={(targetCamera == null)}, " +
                          $"inFrustum={inFrustum}, bounds(center={worldTileBounds.center}, size={worldTileBounds.size}), " +
                          $"camPos={cameraPos}, rootIdentity={(this.rootSpace == null || this.rootSpace.IsIdentity)}");
            }
            if (!inFrustum)
                return;

            // 1. CDLOD quadtree selection (CPU) — in PAINTING space. Nodes live in painting
            // space, so the camera driving LOD selection must be transformed by the root's
            // inverse TRS (no-op when identity). Preserves the XZ-only crack invariant.
            Vector3 selectPos = (this.rootSpace != null && !this.rootSpace.IsIdentity)
                ? this.rootSpace.WorldToPainting(cameraPos) : cameraPos;
            IReadOnlyList<CdlodNode> nodes = this.quadtree.Select(selectPos);
            if (nodes.Count == 0)
            {
                WpLog.Warning($"[GpuTerrainEngine] Submit: 0 nodes selected at selectPos={selectPos} → tile not drawn.");
                return;
            }

            // 2. Upload nodes + AABBs to GPU
            if (this.tileAsset == null) return;
            this.terrainNodeBuf.Upload(nodes, this.tileAsset.minHeight, this.tileAsset.maxHeight);

            // 3. Rebind node buffer to material (domain-reload guard — matches GrassGpuEngine discipline)
            if (this.terrainNodeBuf.NodeBuffer != null)
                this.patchMaterial.SetBuffer(ID_NodeBuffer, this.terrainNodeBuf.NodeBuffer);
            if (this.visibleNodesBuf != null)
                this.patchMaterial.SetBuffer(ID_VisibleNodeIndices, this.visibleNodesBuf);

            // 4. Frustum planes → PAINTING space when the root is non-identity (node AABBs the cull
            // compute tests are in painting space). planeScratch was already filled by the whole-tile
            // frustum gate above, so reuse it here rather than recomputing.
            if (this.rootSpace != null && !this.rootSpace.IsIdentity)
            {
                this.rootSpace.WorldPlanesToPainting(this.planeScratch, this.frustumPlanes);
            }
            else
            {
                for (int i = 0; i < 6; ++i)
                    this.frustumPlanes[i] = new Vector4(
                        this.planeScratch[i].normal.x,
                        this.planeScratch[i].normal.y,
                        this.planeScratch[i].normal.z,
                        this.planeScratch[i].distance);
            }

            // 5. Cull CommandBuffer
            this.cullCmd.Clear();
            this.RecordCullCommands(cullCam, nodes.Count);
            Graphics.ExecuteCommandBuffer(this.cullCmd);

            // [terrain-build-diag] One-shot readback of post-cull indirect args. instanceCount =
            // visible patches the GPU NodeCull produced. 0 ⇒ cull rejected every node on-device
            // (the invisible cause); >0 ⇒ the draw runs but the shader/mesh yields nothing visible.
            if (!this.loggedDrawDiag && this.indirectArgsBuf != null)
            {
                this.loggedDrawDiag = true;
                var dbgArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
                this.indirectArgsBuf.GetData(dbgArgs);
                WpLog.Log($"[GpuTerrainEngine] Indirect args after cull: instanceCount={dbgArgs[0].instanceCount}, " +
                          $"indexCountPerInstance={dbgArgs[0].indexCountPerInstance}, selectedNodes={nodes.Count}, " +
                          $"patchMeshIdx={(this.patchMesh != null ? this.patchMesh.GetIndexCount(0).ToString() : "null")}");
            }

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
            if (this.rootSpace != null && !this.rootSpace.IsIdentity)
                camPos = this.rootSpace.WorldToPainting(camPos);
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
            // worldBounds is what URP/RenderGraph frustum-culls the indirect draw against in WORLD
            // space. this.worldBounds is in PAINTING space (built from TileOriginWorld); under a
            // non-identity root the geometry renders at LocalToWorld·bounds, so the painting-space
            // bounds would cull the whole tile at the wrong location. Map to world (identity → no-op).
            Bounds drawBounds = (this.rootSpace != null && !this.rootSpace.IsIdentity)
                ? this.rootSpace.PaintingBoundsToWorld(this.worldBounds)
                : this.worldBounds;

            return new RenderParams(this.patchMaterial!)
            {
                worldBounds       = drawBounds, // non-zero — RenderGraph culls zero-extent draws
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
