#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// WorldPainter partial — unified SurfaceLayers scatter orchestration.
    ///
    /// Parallels the FROZEN <see cref="RebuildScatter"/> path but iterates the new
    /// <see cref="WorldMapAsset.SurfaceLayers"/>. For each <see cref="GrassLayer"/> it builds one
    /// frozen engine per tile — via a transient <see cref="GrassTileScatterLayer"/> adapter,
    /// REUSING the frozen private helpers (<c>SelectAndBuildScatterEngine</c>,
    /// <c>ResolveScatterInfra</c>, <c>BuildScatterSampler</c>) which are accessible because this is the
    /// same partial class.
    ///
    /// Zero edits to <c>WorldPainter.Scatter.cs</c> or any engine file (those stay FROZEN).
    /// </summary>
    public sealed partial class WorldPainter
    {
        // One engine + its adapter per (grass layer, tile). Adapters are kept alive for the
        // lifetime of their engine (the engine retains the layer reference for Submit-time config).
        private readonly List<IGrassEngine>          surfaceEngines  = new();
        private readonly List<GrassTileScatterLayer> surfaceAdapters = new();

        // One engine + its adapter per PropLayer (layer-global, not per-tile).
        private readonly List<IGrassEngine>            surfacePropEngines  = new();
        private readonly List<PropLayerScatterLayer>   surfacePropAdapters = new();

        /// <summary>(Re)builds one frozen engine per (grass layer, tile) from <see cref="WorldMapAsset.SurfaceLayers"/>.</summary>
        internal void RebuildSurfaceLayers()
        {
            this.DisposeSurfaceLayers();

            if (this.map == null) return;
            IReadOnlyList<WorldPainterLayer> layers = this.map.SurfaceLayers;
            if (layers.Count == 0) return;

            // MAP-WIDE grounding sampler: resolve the tile per query position so grass on EVERY
            // tile is grounded. The frozen BuildScatterSampler() binds to the FIRST valid tile only
            // (HeightmapSurfaceSampler single-tile ctor) — that returns false off that one tile, so
            // grass never placed on any tile but the first (multi-tile no-render bug). Nulled on
            // teardown in DisposeSurfaceLayers so it rebuilds each pass.
            this.scatterSampler ??= new HeightmapSurfaceSampler(c => this.map != null ? this.map.GetTile(c) : null);
            this.ResolveScatterInfra();
            this.scatterPool ??= new InstanceBatchPool(this.scatterPrewarmSlabs);

#if UNITY_EDITOR || !UNITY_WEBGL
            bool gpuCapable = GrassTierProbe.TryGpu(out string probeReason);
#else
            bool gpuCapable  = false;
            string probeReason = "WebGL";
#endif

            Vector3 painterOrigin = this.transform.position;

            for (int i = 0; i < layers.Count; ++i)
            {
                // Hidden layers (eye toggle off) are skipped — no engine built, nothing rendered.
                if (layers[i] == null || !layers[i].Enabled) continue;

                // Prop layers: one InstancedPropEngine per PropLayer (layer-global, not per-tile).
                if (layers[i] is PropLayer prop)
                {
                    this.TryBuildPropLayerEngine(i, prop, painterOrigin);
                    continue;
                }

                if (layers[i] is not GrassLayer grass) continue; // splat layers don't scatter
                this.BuildGrassLayerEngines(i, grass, painterOrigin, gpuCapable, probeReason);
            }
        }

        /// <summary>
        /// Rebuild only one <see cref="GrassLayer"/>'s engines (per-tile). Other layers' engines
        /// stay live. Used by the inspector to surface authoring edits immediately and by the
        /// density brush to refresh the scatter while dragging.
        /// </summary>
        internal void RebuildGrassLayer(GrassLayer layer)
        {
            if (layer == null || this.map == null) return;

            this.DisposeGrassLayerEngines(layer);

            // Hidden (eye toggle off) → stay disposed; the dispose above already removed its engines.
            if (!layer.Enabled) return;

            // Resolve infra lazily — RebuildSurfaceLayers does the same. Safe to call repeatedly.
            this.scatterSampler ??= new HeightmapSurfaceSampler(c => this.map != null ? this.map.GetTile(c) : null);
            this.ResolveScatterInfra();
            this.scatterPool ??= new InstanceBatchPool(this.scatterPrewarmSlabs);

#if UNITY_EDITOR || !UNITY_WEBGL
            bool gpuCapable = GrassTierProbe.TryGpu(out string probeReason);
#else
            bool gpuCapable  = false;
            string probeReason = "WebGL";
#endif

            int layerIndex = this.map.SurfaceLayers is System.Collections.Generic.IList<WorldPainterLayer> il
                ? il.IndexOf(layer)
                : -1;
            this.BuildGrassLayerEngines(layerIndex, layer, this.transform.position, gpuCapable, probeReason);
        }

        /// <summary>
        /// Rebuild only one <see cref="PropLayer"/>'s engine. Other layers' engines stay live.
        /// </summary>
        internal void RebuildPropLayer(PropLayer layer)
        {
            if (layer == null || this.map == null) return;

            this.DisposePropLayerEngines(layer);

            // Hidden (eye toggle off) → stay disposed.
            if (!layer.Enabled) return;

            this.scatterSampler ??= new HeightmapSurfaceSampler(c => this.map != null ? this.map.GetTile(c) : null);
            this.ResolveScatterInfra();
            this.scatterPool ??= new InstanceBatchPool(this.scatterPrewarmSlabs);

            int layerIndex = this.map.SurfaceLayers is System.Collections.Generic.IList<WorldPainterLayer> il
                ? il.IndexOf(layer)
                : -1;
            this.TryBuildPropLayerEngine(layerIndex, layer, this.transform.position);
        }

        /// <summary>
        /// Returns the live <see cref="InstancedPropEngine"/> built for <paramref name="layer"/>, or
        /// null when the layer has no engine (not built / hidden / no mesh assigned). Used by the editor
        /// Select tool to patch a single instance's transform in place during a handle drag (real-time,
        /// flicker-free) instead of a full <see cref="RebuildPropLayer"/>.
        /// </summary>
        internal InstancedPropEngine? TryGetPropEngine(PropLayer layer)
        {
            if (layer == null) return null;
            for (int i = 0; i < this.surfacePropAdapters.Count; ++i)
            {
                if (this.surfacePropAdapters[i].SourceLayer == layer)
                    return this.surfacePropEngines[i] as InstancedPropEngine;
            }
            return null;
        }

        /// <summary>Builds one engine per tile for <paramref name="grass"/>.</summary>
        private void BuildGrassLayerEngines(int layerIndex, GrassLayer grass, Vector3 painterOrigin,
            bool gpuCapable, string probeReason)
        {
            if (this.map == null) return;

            foreach (TerrainTileAsset tile in this.map.EnumerateTiles())
            {
                Texture2D? tileTex = grass.GetTileDensity(tile.tileCoord);
                // tileTex == null means no density painted yet → skip (valid state).
                if (tileTex == null) continue;

                var adapter = GrassTileScatterLayer.Create(grass, tile.tileCoord, tileTex);

                if (!adapter.Validate(out string tileErr))
                {
                    DestroyAdapter(adapter);
                    continue;
                }

                Vector2 tileOriginXZ = TerrainWorldGrid.TileOriginWorld(tile.tileCoord);
                float halfTile = TerrainWorldGrid.TILE_SIZE_M * 0.5f;
                var tileOrigin = new Vector3(
                    tileOriginXZ.x + halfTile,
                    painterOrigin.y,
                    tileOriginXZ.y + halfTile);

                ISurfaceSampler sampler = (ISurfaceSampler?)this.scatterSampler
                    ?? new RaycastSurfaceSampler(adapter.Placement.GroundSnapMask, tileOrigin.y);

                IGrassEngine engine = this.SelectAndBuildScatterEngine(
                    layerIndex, adapter, tileOrigin, sampler, gpuCapable, probeReason);
                engine.Build(adapter, tileOrigin, this.scatterPool!, sampler);

                this.surfaceEngines.Add(engine);
                this.surfaceAdapters.Add(adapter);
            }
        }

        /// <summary>Dispose only the engines/adapters that belong to <paramref name="layer"/>.</summary>
        private void DisposeGrassLayerEngines(GrassLayer layer)
        {
            for (int i = this.surfaceAdapters.Count - 1; i >= 0; --i)
            {
                if (this.surfaceAdapters[i].SourceLayer != layer) continue;
                this.surfaceEngines[i].Dispose();
                DestroyAdapter(this.surfaceAdapters[i]);
                this.surfaceEngines.RemoveAt(i);
                this.surfaceAdapters.RemoveAt(i);
            }
        }

        /// <summary>Dispose only the engines/adapters that belong to <paramref name="layer"/>.</summary>
        private void DisposePropLayerEngines(PropLayer layer)
        {
            for (int i = this.surfacePropAdapters.Count - 1; i >= 0; --i)
            {
                if (this.surfacePropAdapters[i].SourceLayer != layer) continue;
                this.surfacePropEngines[i].Dispose();
                DestroyPropAdapter(this.surfacePropAdapters[i]);
                this.surfacePropEngines.RemoveAt(i);
                this.surfacePropAdapters.RemoveAt(i);
            }
        }

        /// <summary>Advances surface-layer scatter simulation (grass + props).</summary>
        internal void StepSurfaceLayers(float dt)
        {
            for (int i = 0; i < this.surfaceEngines.Count; ++i)
                this.surfaceEngines[i].Step(dt);
            for (int i = 0; i < this.surfacePropEngines.Count; ++i)
                this.surfacePropEngines[i].Step(dt);
        }

        /// <summary>Submits surface-layer scatter draws for this frame (grass + props).</summary>
        internal void SubmitSurfaceLayers(Camera? targetCamera)
        {
            Vector3 lodRef = targetCamera != null
                ? targetCamera.transform.position
                : (Camera.main != null ? Camera.main.transform.position : this.transform.position);

            if (this.surfaceEngines.Count > 0)
            {
                for (int i = 0; i < this.surfaceEngines.Count; ++i)
                    this.surfaceEngines[i].Submit(targetCamera, lodRef);
            }

            if (this.surfacePropEngines.Count > 0)
            {
                for (int i = 0; i < this.surfacePropEngines.Count; ++i)
                    this.surfacePropEngines[i].Submit(targetCamera, lodRef);
            }
        }

        /// <summary>Disposes all surface-layer engines + their transient adapters (grass + props).</summary>
        internal void DisposeSurfaceLayers()
        {
            for (int i = 0; i < this.surfaceEngines.Count; ++i)
                this.surfaceEngines[i].Dispose();
            this.surfaceEngines.Clear();

            for (int i = 0; i < this.surfaceAdapters.Count; ++i)
                DestroyAdapter(this.surfaceAdapters[i]);
            this.surfaceAdapters.Clear();

            for (int i = 0; i < this.surfacePropEngines.Count; ++i)
                this.surfacePropEngines[i].Dispose();
            this.surfacePropEngines.Clear();

            for (int i = 0; i < this.surfacePropAdapters.Count; ++i)
                DestroyPropAdapter(this.surfacePropAdapters[i]);
            this.surfacePropAdapters.Clear();

            // Null the cached grounding sampler so RebuildSurfaceLayers rebuilds it (it uses
            // `scatterSampler ??= BuildScatterSampler()`). Without this, the sampler binds to the
            // first valid tile forever — stale after a Map swap or a tile becoming height-valid
            // later (the old RebuildScatter path nulled it in DisposeScatterEngines, now removed).
            this.scatterSampler = null;
        }

        /// <summary>
        /// Builds one <see cref="InstancedPropEngine"/> for a <see cref="PropLayer"/> via the
        /// transient <see cref="PropLayerScatterLayer"/> adapter, mirroring the guards in the
        /// frozen <c>TryBuildScatterInstancedPropEngine</c> (which can't accept the adapter type).
        /// </summary>
        private void TryBuildPropLayerEngine(int layerIndex, PropLayer prop, Vector3 origin)
        {
            if (this.scatterCullCompute == null)
            {
                Debug.LogError(
                    $"[WorldPainter.SurfaceLayers] PropLayer [{layerIndex}] '{prop.name}': " +
                    "scatterCullCompute is not assigned. Skipping.", this);
                return;
            }

            Mesh[] propMeshes = prop.Render.LodMeshes;
            if (propMeshes.Length == 0 || propMeshes[0] == null)
            {
                Debug.LogWarning(
                    $"[WorldPainter.SurfaceLayers] PropLayer [{layerIndex}] '{prop.name}': " +
                    "LOD 0 mesh is missing — assign one via the Setup panel (🔧 on the layer row) " +
                    "before painting. No props will render until then.", this);
                return;
            }

            Material? mat = prop.Render.Material;
            if (mat == null)
            {
                Debug.LogWarning(
                    $"[WorldPainter.SurfaceLayers] PropLayer [{layerIndex}] '{prop.name}': " +
                    "Material is not assigned — no props will render.", this);
                return;
            }

            var adapter = PropLayerScatterLayer.Create(prop);

            if (!adapter.Validate(out string error))
            {
                DestroyPropAdapter(adapter);
                return;
            }

            ISurfaceSampler sampler = (ISurfaceSampler?)this.scatterSampler
                ?? new RaycastSurfaceSampler(prop.Placement.GroundSnapMask, origin.y);

            try
            {
                var engine = new InstancedPropEngine(this.scatterCullCompute, mat);
                engine.Build(adapter, origin, this.scatterPool!, sampler);
                engine.BindRootSpace(this.RootBinder);

                this.surfacePropEngines.Add(engine);
                this.surfacePropAdapters.Add(adapter);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WorldPainter.SurfaceLayers] PropLayer [{layerIndex}] '{prop.name}' engine threw " +
                    $"{ex.GetType().Name}: {ex.Message}", this);
                DestroyPropAdapter(adapter);
            }
        }

        private static void DestroyAdapter(GrassTileScatterLayer? adapter)
        {
            if (adapter == null) return;
            if (Application.isPlaying) Destroy(adapter);
            else DestroyImmediate(adapter);
        }

        private static void DestroyPropAdapter(PropLayerScatterLayer? adapter)
        {
            if (adapter == null) return;
            if (Application.isPlaying) Destroy(adapter);
            else DestroyImmediate(adapter);
        }
    }
}
