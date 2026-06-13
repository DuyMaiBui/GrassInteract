#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// WorldPainter partial — unified SurfaceLayers (grass variants) scatter orchestration.
    ///
    /// Parallels the FROZEN <see cref="RebuildScatter"/> path but iterates the new
    /// <see cref="WorldMapAsset.SurfaceLayers"/>. For each <see cref="GrassLayer"/> it builds N
    /// frozen engines — one per palette variant — via a transient <see cref="GrassVariantScatterLayer"/>
    /// adapter, REUSING the frozen private helpers (<c>SelectAndBuildScatterEngine</c>,
    /// <c>ResolveScatterInfra</c>, <c>BuildScatterSampler</c>) which are accessible because this is the
    /// same partial class.
    ///
    /// Zero edits to <c>WorldPainter.Scatter.cs</c> or any engine file (those stay FROZEN).
    /// </summary>
    public sealed partial class WorldPainter
    {
        // One engine + its adapter per (grass layer, variant). Adapters are kept alive for the
        // lifetime of their engine (the engine retains the layer reference for Submit-time config).
        private readonly List<IGrassEngine>             surfaceEngines  = new();
        private readonly List<GrassVariantScatterLayer> surfaceAdapters = new();

        // One engine + its adapter per PropLayer (layer-global, not per-tile).
        private readonly List<IGrassEngine>            surfacePropEngines  = new();
        private readonly List<PropLayerScatterLayer>   surfacePropAdapters = new();

        /// <summary>(Re)builds one frozen engine per grass-variant from <see cref="WorldMapAsset.SurfaceLayers"/>.</summary>
        internal void RebuildSurfaceLayers()
        {
            this.DisposeSurfaceLayers();

            if (this.map == null) return;
            IReadOnlyList<WorldPainterLayer> layers = this.map.SurfaceLayers;
            if (layers.Count == 0) return;

            // Reuse the frozen grounding sampler + GPU infra resolution.
            this.scatterSampler ??= this.BuildScatterSampler();
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
                // Prop layers: one InstancedPropEngine per PropLayer (layer-global, not per-tile).
                if (layers[i] is PropLayer prop)
                {
                    this.TryBuildPropLayerEngine(i, prop, painterOrigin);
                    continue;
                }

                if (layers[i] is not GrassLayer grass) continue; // splat layers don't scatter

                IReadOnlyList<GrassVariant> palette = grass.Palette;
                for (int v = 0; v < palette.Count; ++v)
                {
                    GrassVariant variant = palette[v];

                    // Per-tile: build one engine per (variant, tile). Each tile gets its own
                    // adapter at the tile center origin with that tile's density texture.
                    bool anyTile = false;
                    if (this.map != null)
                    {
                        foreach (TerrainTileAsset tile in this.map.EnumerateTiles())
                        {
                            Texture2D? tileTex = GrassLayer.GetTileDensity(variant, tile.tileCoord);
                            // tileTex == null means no density painted yet → skip (valid state).
                            if (tileTex == null) continue;

                            var adapter = GrassVariantScatterLayer.Create(grass, v, tile.tileCoord, tileTex);

                            if (!adapter.Validate(out string tileErr))
                            {
                                Debug.Log($"[WorldPainter.SurfaceLayers] {adapter.name}: {tileErr} — skipped.", this);
                                DestroyAdapter(adapter);
                                continue;
                            }

                            // Per-tile origin = tile center world XZ at the painter's Y.
                            Vector2 tileOriginXZ = TerrainWorldGrid.TileOriginWorld(tile.tileCoord);
                            float halfTile = TerrainWorldGrid.TILE_SIZE_M * 0.5f;
                            var tileOrigin = new Vector3(
                                tileOriginXZ.x + halfTile,
                                painterOrigin.y,
                                tileOriginXZ.y + halfTile);

                            ISurfaceSampler sampler = (ISurfaceSampler?)this.scatterSampler
                                ?? new RaycastSurfaceSampler(adapter.Placement.GroundSnapMask, tileOrigin.y);

                            IGrassEngine engine = this.SelectAndBuildScatterEngine(
                                i, adapter, tileOrigin, sampler, gpuCapable, probeReason);
                            engine.Build(adapter, tileOrigin, this.scatterPool!, sampler);

                            this.surfaceEngines.Add(engine);
                            this.surfaceAdapters.Add(adapter);
                            anyTile = true;

                            Debug.Log($"[WorldPainter.SurfaceLayers] {adapter.name} → " +
                                      $"tier={this.ScatterActiveTierName}.", this);
                        }
                    }

                    // anyTile == false and map != null means no density painted yet for this variant
                    // across any tile — quiet no-op is correct; no log needed.
                }
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

            if (prop.Render.LodMeshes.Length == 0)
            {
                Debug.LogWarning(
                    $"[WorldPainter.SurfaceLayers] PropLayer [{layerIndex}] '{prop.name}': " +
                    "LodMeshes is empty — no props will render.", this);
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
                Debug.Log(
                    $"[WorldPainter.SurfaceLayers] PropLayer [{layerIndex}] '{prop.name}': " +
                    $"{error} — skipped.", this);
                DestroyPropAdapter(adapter);
                return;
            }

            ISurfaceSampler sampler = (ISurfaceSampler?)this.scatterSampler
                ?? new RaycastSurfaceSampler(prop.Placement.GroundSnapMask, origin.y);

            try
            {
                var engine = new InstancedPropEngine(this.scatterCullCompute, mat);
                engine.Build(adapter, origin, this.scatterPool!, sampler);

                this.surfacePropEngines.Add(engine);
                this.surfacePropAdapters.Add(adapter);

                Debug.Log(
                    $"[WorldPainter.SurfaceLayers] PropLayer [{layerIndex}] '{prop.name}' → InstancedPropEngine.", this);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WorldPainter.SurfaceLayers] PropLayer [{layerIndex}] '{prop.name}' engine threw " +
                    $"{ex.GetType().Name}: {ex.Message}", this);
                DestroyPropAdapter(adapter);
            }
        }

        private static void DestroyAdapter(GrassVariantScatterLayer? adapter)
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
