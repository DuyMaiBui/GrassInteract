#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Editor tool that bakes a <see cref="GrassLayer"/>'s scatter into a <see cref="BakedGrassData"/>
    /// sub-asset stored on the layer asset itself. At runtime, <see cref="GrassGpuEngine"/> detects the
    /// sub-asset and skips <c>GrassScatter.Build</c> + counting-sort, replacing it with 3× SetData.
    ///
    /// Mirrors the <see cref="WorldMapBaker"/> pattern: sub-assets are created via
    /// <c>AssetDatabase.AddObjectToAsset</c> and saved via <c>EditorUtility.SetDirty</c> +
    /// <c>AssetDatabase.SaveAssetIfDirty</c>.
    ///
    /// Menu items: <c>Tools/Library/WorldPainter/Grass Baker/</c>.
    /// </summary>
    public static class WorldGrassBaker
    {
        // ── Menu constants ────────────────────────────────────────────────────
        private const string MENU_BAKE  = "Tools/Library/WorldPainter/Grass Baker/Bake Selected GrassLayer";
        private const string MENU_CLEAR = "Tools/Library/WorldPainter/Grass Baker/Clear Bake on Selected GrassLayer";

        // ── Menu items ────────────────────────────────────────────────────────

        [MenuItem(MENU_BAKE, priority = 200)]
        public static void MenuBakeSelected()
        {
            var layer = Selection.activeObject as GrassLayer;
            if (layer == null)
            {
                EditorUtility.DisplayDialog("WorldGrassBaker",
                    "Select a GrassLayer asset in the Project window first.", "OK");
                return;
            }
            try
            {
                BakeLayer(layer);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldGrassBaker] Bake failed: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("WorldGrassBaker", $"Bake failed:\n{ex.Message}", "OK");
            }
        }

        [MenuItem(MENU_BAKE, validate = true)]
        private static bool MenuBakeSelected_Validate() =>
            Selection.activeObject is GrassLayer;

        [MenuItem(MENU_CLEAR, priority = 201)]
        public static void MenuClearSelected()
        {
            var layer = Selection.activeObject as GrassLayer;
            if (layer == null)
            {
                EditorUtility.DisplayDialog("WorldGrassBaker",
                    "Select a GrassLayer asset in the Project window first.", "OK");
                return;
            }
            ClearBake(layer);
        }

        [MenuItem(MENU_CLEAR, validate = true)]
        private static bool MenuClearSelected_Validate() =>
            Selection.activeObject is GrassLayer;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Bakes the scatter for <paramref name="layer"/> and writes a <see cref="BakedGrassData"/>
        /// sub-asset onto the layer asset. Existing sub-assets for this layer are replaced.
        ///
        /// Workflow (all on the main thread, no Play mode required):
        /// 1. Build scatter via <see cref="GrassScatter.Build"/> using <paramref name="sampler"/>.
        /// 2. Bake CPU arrays via <see cref="ChunkedBladeBuffer.BakeArrays"/> (pure, no GPU).
        /// 3. Validate via <see cref="ChunkedBladeBuffer.ValidatePartition"/> (hard gate).
        /// 4. Pack into <see cref="BakedGrassData"/> and write as a sub-asset.
        /// </summary>
        /// <param name="layer">The <see cref="GrassLayer"/> to bake. Must be a project asset.</param>
        /// <param name="origin">World-space field origin. Default = Vector3.zero (demo scene).</param>
        /// <param name="densityTex">
        /// Density texture to use for scatter. Pass null to scatter at full density
        /// (uses <see cref="FullDensityScatterLayer"/> wrapper that bypasses DensityPlacement).
        /// </param>
        /// <param name="sampler">
        /// Surface sampler. Pass null to use <see cref="FlatEditorSurfaceSampler"/> (flat ground at y=0).
        /// </param>
        /// <returns>The populated <see cref="BakedGrassData"/> sub-asset (already saved).</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="layer"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// If the layer is not a project asset, or if ValidatePartition fails.
        /// </exception>
        public static BakedGrassData BakeLayer(
            GrassLayer       layer,
            Vector3          origin     = default,
            Texture2D?       densityTex = null,
            ISurfaceSampler? sampler    = null)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));

            string layerPath = AssetDatabase.GetAssetPath(layer);
            if (string.IsNullOrEmpty(layerPath))
                throw new InvalidOperationException(
                    $"[WorldGrassBaker] GrassLayer '{layer.name}' is not a project asset. Save it first.");

            // ── Terrain-sampler parity guard (2025-06-20) ─────────────────────
            // The runtime live path (GrassGpuEngine.Build) uses TerrainSurfaceSampler when a
            // Terrain is bound, which reads real terrain heights + uses TerrainSizeXZ as the
            // effective bounds. If no explicit sampler is supplied here, we fall back to
            // FlatEditorSurfaceSampler (Y=0, normal=up), producing a baked blob where ALL blade
            // positions are at y=0 — silent corruption for any terrain-bound layer.
            //
            // We cannot reliably resolve the runtime Terrain from the GrassLayer asset at editor
            // time (GrassLayer has no terrain reference; that association lives in ScatterField
            // which only exists at runtime). The correct fix is for callers that DO have terrain
            // context to construct a TerrainSurfaceSampler and pass it as the `sampler` argument.
            //
            // Until sampler-parity is wired from the calling context, we ABORT the bake for any
            // layer that has non-trivial FieldBounds (indicating terrain use) and no explicit sampler,
            // rather than silently writing a corrupt flat blob.
            //
            // TODO (2025-06-20): wire the caller-side terrain resolution so that scripts / menu
            // items that have access to the in-scene Terrain can pass a TerrainSurfaceSampler here.
            // When that is wired, the explicit sampler will be non-null and this guard will be bypassed.
            if (sampler == null && !IsLikelyFlatLayer(layer))
            {
                string msg =
                    $"WorldGrassBaker: cannot safely bake GrassLayer '{layer.name}' without a terrain " +
                    $"surface sampler.\n\n" +
                    $"This layer has FieldBounds ({layer.FieldBounds.x:0}m × {layer.FieldBounds.y:0}m) " +
                    $"that suggest terrain-bound usage. Baking with the flat fallback (y=0, normal=up) " +
                    $"would place all blades at world Y=0 regardless of terrain height, producing silent " +
                    $"visual corruption.\n\n" +
                    $"To bake, call BakeLayer with an explicit TerrainSurfaceSampler constructed from " +
                    $"the in-scene Terrain component bound to this layer's scatter field.";

                Debug.LogError($"[WorldGrassBaker] {msg}");
                EditorUtility.DisplayDialog("WorldGrassBaker — Bake Aborted", msg, "OK");
                throw new InvalidOperationException($"[WorldGrassBaker] Bake aborted: {msg}");
            }

            ISurfaceSampler activeSampler = sampler ?? new FlatEditorSurfaceSampler(origin.y);

            // Use the layer's registered tile-0 density if no override is supplied.
            Texture2D? tex = densityTex ?? layer.GetTileDensity(Vector2Int.zero);

            // ── 1. Build scatter ──────────────────────────────────────────────
            // GrassTileScatterLayer is transient; it needs a non-null, readable density map.
            // When none is available, fall back to the uniform-density placement wrapper.
            GrassScatterResult scatter;
            ScatterLayer scatterLayer;
            var pool = new InstanceBatchPool();

            if (tex != null && tex.isReadable)
            {
                var tileLayer = GrassTileScatterLayer.Create(layer, Vector2Int.zero, tex);
                scatterLayer  = tileLayer;
                scatter       = GrassScatter.Build(tileLayer, origin, pool, activeSampler);
            }
            else
            {
                // No readable density tex — create a uniform-density transient adapter.
                var uniformLayer = UniformDensityScatterLayer.Create(layer);
                scatterLayer     = uniformLayer;
                scatter          = GrassScatter.Build(uniformLayer, origin, pool, activeSampler);
            }

            try
            {
                // ── 2. BakeArrays (pure CPU, no GPU alloc) ────────────────────
                Vector2 fieldBounds = layer.FieldBounds;
                ChunkedBladeBuffer.BakeResult bakeResult = ChunkedBladeBuffer.BakeArrays(
                    scatter, scatterLayer, origin, fieldBounds, scatterLayer.ScaleRange.y,
                    oriented: scatterLayer.IsOriented);

                // ── 3. ValidatePartition (hard gate — uses scatter.WorldBounds) ─
                // We need a temporary ChunkedBladeBuffer for ValidatePartition. It takes a
                // GrassScatterResult only to check scatter.TotalCount + scatter.WorldBounds;
                // the arrays it inspects are the ones we just set via LoadFromArrays.
                var tempBuffer = new ChunkedBladeBuffer();
                tempBuffer.LoadFromArrays(
                    bakeResult.Blades, bakeResult.Aabbs, bakeResult.Ranges,
                    bakeResult.GridX, bakeResult.GridZ, bakeResult.ChunkSize,
                    bakeResult.TotalChunks, bakeResult.TotalBlades,
                    bakeResult.ScaleMax2, bakeResult.Oriented);

                bool valid = tempBuffer.ValidatePartition(scatter, out string report);
                // Release GPU buffers created by LoadFromArrays — we only needed the CPU side.
                // (No actual GPU alloc in edit-mode LoadFromArrays; Dispose is a no-op here but
                // guards future changes.)
                tempBuffer.Dispose();

                if (!valid)
                    throw new InvalidOperationException(
                        $"[WorldGrassBaker] ValidatePartition FAILED for '{layer.name}':\n{report}");

                WpLog.Log($"[WorldGrassBaker] ValidatePartition: {report}");

                // ── 4. Pack BakedGrassData ────────────────────────────────────
                BakedGrassData baked = GetOrCreateSubAsset(layer, layerPath);
                baked.Pack(
                    bakeResult.Blades, bakeResult.Aabbs, bakeResult.Ranges,
                    bakeResult.GridX, bakeResult.GridZ, bakeResult.ChunkSize,
                    bakeResult.TotalChunks, bakeResult.TotalBlades,
                    bakeResult.ScaleMax2, bakeResult.Oriented,
                    scatter.WorldBounds);

                // ── 5. Save sub-asset and update layer reference ───────────────
                layer.EditorSetBakedGrass(baked);
                EditorUtility.SetDirty(baked);
                EditorUtility.SetDirty(layer);
                AssetDatabase.SaveAssetIfDirty(layer);

                WpLog.Log($"[WorldGrassBaker] Bake complete for '{layer.name}': " +
                          $"{bakeResult.TotalBlades} blades, " +
                          $"Grid={bakeResult.GridX}×{bakeResult.GridZ}, " +
                          $"Chunks={bakeResult.TotalChunks}");

                return baked;
            }
            finally
            {
                GrassScatter.ReturnSlabs(scatter, pool);
                UnityEngine.Object.DestroyImmediate(scatterLayer);
            }
        }

        /// <summary>
        /// Removes the <see cref="BakedGrassData"/> sub-asset from <paramref name="layer"/> and
        /// clears the reference, reverting to the live scatter path.
        /// </summary>
        public static void ClearBake(GrassLayer layer)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));

            string layerPath = AssetDatabase.GetAssetPath(layer);
            if (string.IsNullOrEmpty(layerPath)) return;

            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(layerPath))
            {
                if (obj is BakedGrassData bakedData && !ReferenceEquals(obj, layer))
                {
                    AssetDatabase.RemoveObjectFromAsset(bakedData);
                    UnityEngine.Object.DestroyImmediate(bakedData, allowDestroyingAssets: true);
                }
            }

            layer.EditorSetBakedGrass(null);
            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssetIfDirty(layer);
            AssetDatabase.ImportAsset(layerPath);

            WpLog.Log($"[WorldGrassBaker] Cleared bake for '{layer.name}'.");
        }

        // ── Sub-asset lifecycle (mirrors WorldMapBaker) ───────────────────────

        /// <summary>
        /// Loads the existing <see cref="BakedGrassData"/> sub-asset from the layer asset, or
        /// creates a new one and attaches it via <c>AssetDatabase.AddObjectToAsset</c>.
        /// </summary>
        private static BakedGrassData GetOrCreateSubAsset(GrassLayer layer, string layerPath)
        {
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(layerPath))
            {
                if (obj is BakedGrassData existing && !ReferenceEquals(obj, layer))
                    return existing;
            }

            var baked = ScriptableObject.CreateInstance<BakedGrassData>();
            baked.name = $"{layer.name}_BakedGrass";
            AssetDatabase.AddObjectToAsset(baked, layer);
            return baked;
        }

        // ── Terrain guard helper ──────────────────────────────────────────────

        /// <summary>
        /// Heuristic: returns <c>true</c> when the layer is likely intended for flat ground and
        /// safe to bake with <see cref="FlatEditorSurfaceSampler"/>.
        ///
        /// A layer is considered flat-safe when its <see cref="GrassLayer.SlopeRange"/> maximum
        /// is very small (≤ 1°), indicating the designer explicitly restricted it to flat surfaces.
        /// All other layers (including the common default SlopeRange [0°, 90°]) are assumed to be
        /// terrain-bound and require a real surface sampler for parity with the runtime path.
        ///
        /// This is a conservative heuristic — when in doubt, we abort rather than bake corrupt data.
        /// Pass an explicit <see cref="ISurfaceSampler"/> to bypass this check entirely.
        /// </summary>
        private static bool IsLikelyFlatLayer(GrassLayer layer) =>
            layer.SlopeRange.y <= 1f;

        // ── Inline flat surface sampler (editor-only, zero physics dependency) ──

        /// <summary>
        /// Flat surface sampler for editor bake: all hits return Y = <c>groundY</c>,
        /// normal = Vector3.up, slope = 0. No Physics.Raycast required — safe in edit mode.
        /// </summary>
        private sealed class FlatEditorSurfaceSampler : ISurfaceSampler
        {
            private readonly float groundY;
            public FlatEditorSurfaceSampler(float groundY = 0f) => this.groundY = groundY;

            public bool TrySample(float worldX, float worldZ, out SurfaceHit hit)
            {
                hit = new SurfaceHit
                {
                    Y            = this.groundY,
                    Normal       = Vector3.up,
                    SlopeDeg     = 0f,
                    SplatWeights = null,
                };
                return true;
            }
        }
    }

    // ── UniformDensityScatterLayer ────────────────────────────────────────────

    /// <summary>
    /// Transient <see cref="ScatterLayer"/> adapter that presents a <see cref="GrassLayer"/> with
    /// full uniform density (no per-tile density texture required). Used by <see cref="WorldGrassBaker"/>
    /// when no readable density map is available.
    ///
    /// Creates a 1×1 white Texture2D so <see cref="DensityPlacement"/> always sees full-density
    /// (density=1.0 at all UVs → accept every candidate). The texture is destroyed when the adapter
    /// is destroyed.
    ///
    /// Reports <c>UseExplicitFieldBounds=true</c> so placement uses the layer's FieldBounds.
    /// </summary>
    internal sealed class UniformDensityScatterLayer : ScatterLayer, IDensityPlacementSource
    {
        private GrassLayer layer   = null!;
        private Texture2D  whiteTex = null!;

        internal static UniformDensityScatterLayer Create(GrassLayer layer)
        {
            var a = CreateInstance<UniformDensityScatterLayer>();
            a.hideFlags = HideFlags.HideAndDontSave;
            a.layer     = layer;
            a.name      = $"{layer.name}@uniform";

            // 1×1 white texture — DensityPlacement samples GetPixelBilinear(u,v).r = 1.0 = full density.
            a.whiteTex           = new Texture2D(1, 1, TextureFormat.R8, mipChain: false, linear: true);
            a.whiteTex.hideFlags = HideFlags.HideAndDontSave;
            a.whiteTex.SetPixel(0, 0, Color.white);
            a.whiteTex.Apply(updateMipmaps: false);
            return a;
        }

        private void OnDestroy()
        {
            if (this.whiteTex != null)
                UnityEngine.Object.DestroyImmediate(this.whiteTex);
        }

        // ── ScatterLayer abstract overrides ───────────────────────────────────
        public override ScatterRenderConfig    Render    => this.layer.Render;
        public override ScatterWindConfig      Wind      => this.layer.Wind;
        public override ScatterDeformConfig    Deform    => this.layer.Deform;
        public override ScatterBoundsConfig    Bounds    => this.layer.Bounds;
        public override ScatterPlacementConfig Placement => this.layer.Placement;
        public override Vector2 FieldBounds         => this.layer.FieldBounds;
        public override Vector2 ScaleRange          => this.layer.ScaleRange;
        public override float   ScaleFactor         => this.layer.ScaleFactor;
        public override Vector3 RotationOffsetEuler => this.layer.RotationOffsetEuler;
        public override bool    IsOriented          => this.layer.IsOriented;

        public override IScatterPlacement CreatePlacement() => new DensityPlacement(this);

        // ── IDensityPlacementSource ───────────────────────────────────────────
        public bool       UseExplicitFieldBounds => true;
        public Texture2D? DensityMap     => this.whiteTex; // 1×1 white = full density everywhere
        public int        TargetInstances =>
            Mathf.Max(0, Mathf.RoundToInt(this.layer.TargetDensityPerSqM *
                this.layer.FieldBounds.x * this.layer.FieldBounds.y));
        public int        Seed            => this.layer.Seed;
        public Vector2    SlopeRange      => this.layer.SlopeRange;
        public int        SplatLayerIndex => -1;
        public float      SplatThreshold  => 0.5f;
        public Vector2    RandomPitchRange => this.layer.RandomPitchRange;
        public Vector2    RandomRollRange  => this.layer.RandomRollRange;
        public bool       AlignToNormal    => this.layer.AlignToNormal;
        public float      MaxBladeHeight   => this.layer.Bounds.MaxBladeHeight;
        public float      BendHeadroom     => this.layer.Bounds.BendHeadroom;
    }
}
