#nullable enable

using System.Collections.Generic;

using UnityEditor;

using UnityEngine;

namespace WorldPainter.Editor

{

    /// <summary>

    /// The ONLY path for adding and removing tiles and surface layers in a <see cref="WorldMapAsset"/>.

    ///

    /// Every mutation follows the pattern:

    ///   1. <c>AssetDatabase.AddObjectToAsset</c> / <c>RemoveObjectFromAsset</c> + <c>DestroyImmediate</c>

    ///   2. <c>EditorUtility.SetDirty(map)</c>

    ///   3. <c>AssetDatabase.SaveAssets()</c>

    ///

    /// Sign-safe sub-asset naming uses underscores for the coordinate separator, with "n" prefix

    /// for negative axes (e.g. Tile_n1_0 for (-1,0)).

    /// </summary>

    public static class WorldMapAssetLifecycle

    {

        // ── Naming helpers ──────────────────────────────────────────────────────

        /// <summary>

        /// Builds the sub-asset name for a tile at <paramref name="coord"/>.

        /// Sign-safe: negative coordinates use "n" prefix (e.g. Tile_n1_0 for (-1,0)).

        /// </summary>

        public static string TileSubAssetName(Vector2Int coord)

        {

            string xStr = coord.x < 0 ? $"n{-coord.x}" : $"{coord.x}";

            string yStr = coord.y < 0 ? $"n{-coord.y}" : $"{coord.y}";

            return $"Tile_{xStr}_{yStr}";

        }

        /// <summary>

        /// Builds the sub-asset name for a scatter layer def sub-asset.

        /// </summary>

        public static string LayerSubAssetName(string layerBaseName)

        {

            return $"Layer_{layerBaseName}";

        }

        // ── Tile lifecycle ──────────────────────────────────────────────────────

        /// <summary>

        /// Creates a new <see cref="TerrainTileAsset"/> sub-asset at <paramref name="coord"/> inside

        /// <paramref name="map"/>. No-op if a tile already exists at that coord.

        ///

        /// After this call: <c>map.GetTile(coord)</c> is non-null; the asset file is saved.

        /// For each existing <see cref="GrassLayer"/> a density texture is created for this tile

        /// and appended to the layer's <c>densityTiles</c> array.

        /// </summary>

        /// <param name="map">The container map (must be a saved asset — <c>AssetDatabase.GetAssetPath</c> must return non-empty).</param>

        /// <param name="coord">Signed tile coordinate (negatives allowed).</param>

        /// <returns>The newly created tile, or the existing tile if coord was already occupied.</returns>

        public static TerrainTileAsset AddTile(WorldMapAsset map, Vector2Int coord)

        {

            var existing = map.GetTile(coord);

            if (existing != null) return existing;

            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();

            tile.name      = TileSubAssetName(coord);

            tile.tileCoord = coord;

            // Seed a valid flat tile (zero-filled = flat at minHeight). Without this the tile's

            // heightData stays Array.Empty<byte>(), so TerrainTileAsset.IsHeightValid is false and

            // WorldPainter.BuildOneTileAsset silently skips it — the tile never renders.

            tile.heightData = new byte[tile.ExpectedHeightBytes];
            // (Phase 3 cleanup — splatData seeding removed; per-tile palette weights live in alphamaps[].)

            string mapPath = AssetDatabase.GetAssetPath(map);

            AssetDatabase.AddObjectToAsset(tile, mapPath);

            map.RegisterTile(coord, tile);

            // Phase-1 alphamaps — one RGBA32 sub-asset per ceil(palette.Count / 4). First
            // alphamap is seeded full-R so palette[0] paints the whole tile by default;
            // subsequent alphamaps and channels start at zero. Called after the tile is
            // attached so the new sub-assets nest correctly under mapPath.
            AllocateAndSeedTileAlphamaps(map, tile, mapPath);

            // For each GrassLayer, create a density texture sub-asset for this new tile

            // and append it to the layer's densityTiles array.

            foreach (var surfaceLayer in map.SurfaceLayers)

            {

                if (surfaceLayer is not GrassLayer grass) continue;

                Texture2D tex = CreateDensityMap(

                    $"{grass.name}@{TileSubAssetName(coord)}",

                    seedFull: false);

                AssetDatabase.AddObjectToAsset(tex, mapPath);

                var prevTiles = grass.EditorTileDensities ?? System.Array.Empty<TileDensityTexture>();

                var newTiles = new TileDensityTexture[prevTiles.Length + 1];

                prevTiles.CopyTo(newTiles, 0);

                newTiles[prevTiles.Length] = new TileDensityTexture { coord = coord, tex = tex };

                grass.EditorSetTileDensities(newTiles);

                EditorUtility.SetDirty(grass);

            }

            EditorUtility.SetDirty(map);

            EditorUtility.SetDirty(tile);

            AssetDatabase.SaveAssets();

            return tile;

        }

        // ── Phase 1: terrain-layer palette + per-tile alphamap lifecycle ──────────

        /// <summary>Default resolution for newly-created per-tile alphamap textures.</summary>
        internal const int DEFAULT_ALPHAMAP_RES = 512;

        /// <summary>
        /// Appends <paramref name="terrainLayer"/> to <c>map.TerrainPalette</c> and grows EVERY
        /// existing tile's <c>alphamaps[]</c> if the new palette count crosses a multiple of 4
        /// (an extra RGBA32 sub-asset is needed). Returns the new palette index.
        /// </summary>
        public static int AddTerrainLayer(WorldMapAsset map, TerrainLayer terrainLayer)
        {
            if (map == null || terrainLayer == null) return -1;

            string mapPath = AssetDatabase.GetAssetPath(map);
            int    prevCount    = map.TerrainPalette.Count;
            int    prevAlphamaps = Mathf.CeilToInt(prevCount / 4f);

            int newIndex = map.EditorAppendTerrainLayer(terrainLayer);
            if (newIndex < 0) return -1;

            int newAlphamaps = Mathf.CeilToInt(map.TerrainPalette.Count / 4f);

            // Crossed a 4-boundary → every tile needs one MORE alphamap sub-asset. Index 0
            // of the new alphamap is the new palette layer's channel (since it's the first
            // entry in the new alphamap group), but we seed it ZERO — the existing
            // alphamaps[0] keeps full-R weight so the world doesn't suddenly recolour.
            if (newAlphamaps > prevAlphamaps)
            {
                foreach (var coord in map.EnumerateTileCoords())
                {
                    var tile = map.GetTile(coord);
                    if (tile == null) continue;
                    GrowTileAlphamaps(tile, newAlphamaps, mapPath);
                    EditorUtility.SetDirty(tile);
                }
            }

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
            return newIndex;
        }

        /// <summary>
        /// Removes the TerrainLayer at <paramref name="index"/> from the palette. Existing
        /// tile alphamaps are NOT trimmed — the channel is simply ignored by the shader (cheap
        /// + non-destructive in case the user re-adds a layer later).
        /// </summary>
        public static void RemoveTerrainLayer(WorldMapAsset map, int index)
        {
            if (map == null) return;
            map.EditorRemoveTerrainLayerAt(index);
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Called from <see cref="AddTile"/>. Creates <c>map.AlphamapCountForPalette</c>
        /// RGBA32 sub-assets, seeds alphamap 0 to (R=255, 0, 0, 0) so palette[0] paints
        /// fully by default. No-op when the palette is empty.
        /// </summary>
        private static void AllocateAndSeedTileAlphamaps(WorldMapAsset map, TerrainTileAsset tile, string mapPath)
        {
            int needed = map.AlphamapCountForPalette;
            if (needed <= 0) { tile.EditorSetAlphamaps(System.Array.Empty<Texture2D>()); return; }

            int res = DEFAULT_ALPHAMAP_RES;
            var maps = new Texture2D[needed];
            for (int k = 0; k < needed; k++)
            {
                bool seedFullR = k == 0; // first alphamap → palette[0] full weight default.
                maps[k] = CreateBlankAlphamap($"{tile.name}_Alphamap{k}", res,
                    seedFullR ? new Color32(255, 0, 0, 0) : new Color32(0, 0, 0, 0));
                AssetDatabase.AddObjectToAsset(maps[k], mapPath);
            }
            tile.EditorSetAlphamaps(maps);
        }

        /// <summary>
        /// Grows a tile's alphamap array to <paramref name="targetCount"/>. Existing entries
        /// are preserved verbatim. New entries are blank (channel weights all zero), so a
        /// palette grow does NOT change the rendered terrain — only adds capacity.
        /// </summary>
        private static void GrowTileAlphamaps(TerrainTileAsset tile, int targetCount, string mapPath)
        {
            var prev = tile.EditorAlphamaps ?? System.Array.Empty<Texture2D>();
            if (prev.Length >= targetCount) return;

            int res = DEFAULT_ALPHAMAP_RES;
            var next = new Texture2D[targetCount];
            System.Array.Copy(prev, next, prev.Length);
            for (int k = prev.Length; k < targetCount; k++)
            {
                next[k] = CreateBlankAlphamap($"{tile.name}_Alphamap{k}", res, new Color32(0, 0, 0, 0));
                AssetDatabase.AddObjectToAsset(next[k], mapPath);
            }
            tile.EditorSetAlphamaps(next);
        }

        /// <summary>
        /// Builds a fresh RGBA32 alphamap texture filled with <paramref name="fill"/>. Tagged
        /// linear because the weights are NOT colour data — they're per-layer mix coefficients.
        /// </summary>
        private static Texture2D CreateBlankAlphamap(string texName, int size, Color32 fill)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name       = texName,
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        /// <summary>

        /// Removes the <see cref="TerrainTileAsset"/> at <paramref name="coord"/> from <paramref name="map"/>.

        /// Calls <c>RemoveObjectFromAsset</c> + <c>DestroyImmediate</c> to eliminate orphan sub-assets.

        /// No-op if no tile exists at coord.

        /// </summary>

        public static void RemoveTile(WorldMapAsset map, Vector2Int coord)

        {

            var tile = map.GetTile(coord);

            if (tile == null) return;

            string mapPath = AssetDatabase.GetAssetPath(map);

            // Remove per-tile alphamap textures (palette weights) before destroying the parent.

            var tileAlphas = tile.EditorAlphamaps;
            if (tileAlphas != null)
            {
                foreach (var alpha in tileAlphas)
                {
                    if (alpha != null && AssetDatabase.GetAssetPath(alpha) == mapPath)
                    {
                        AssetDatabase.RemoveObjectFromAsset(alpha);
                        Object.DestroyImmediate(alpha, allowDestroyingAssets: true);
                    }
                }
                tile.EditorSetAlphamaps(System.Array.Empty<Texture2D>());
            }

            // Remove per-tile density textures for all GrassLayers at this coord.

            foreach (var surfaceLayer in map.SurfaceLayers)

            {

                if (surfaceLayer is not GrassLayer grass) continue;

                var existing = grass.EditorTileDensities;

                if (existing == null || existing.Length == 0) continue;

                bool changed = false;

                var kept = new List<TileDensityTexture>(existing.Length);

                foreach (var entry in existing)

                {

                    if (entry.coord == coord)

                    {

                        if (entry.tex != null &&

                            AssetDatabase.GetAssetPath(entry.tex) == mapPath)

                        {

                            AssetDatabase.RemoveObjectFromAsset(entry.tex);

                            Object.DestroyImmediate(entry.tex, allowDestroyingAssets: true);

                        }

                        changed = true;

                    }

                    else

                    {

                        kept.Add(entry);

                    }

                }

                if (changed)

                {

                    grass.EditorSetTileDensities(kept.ToArray());

                    EditorUtility.SetDirty(grass);

                }

            }

            map.UnregisterTile(coord);

            AssetDatabase.RemoveObjectFromAsset(tile);

            Object.DestroyImmediate(tile, allowDestroyingAssets: true);

            EditorUtility.SetDirty(map);

            AssetDatabase.SaveAssets();

        }

        // ── Surface layer lifecycle (Phase 3 cleanup — SplatLayer removed) ─────

        // (Phase 3 cleanup — AddSplatLayer / CreateBlankAlbedo / AssignLayerAlbedos /
        //  DEFAULT_ALBEDO_TINTS / DEFAULT_ALBEDO_RES / IsValidAlbedoRes removed.
        //  WorldMap.TerrainPalette + AddTerrainLayer replace the entire codepath.)

        /// <summary>

        /// Adds a <see cref="GrassLayer"/> to <see cref="WorldMapAsset.SurfaceLayers"/> with a default

        /// grass material. Eagerly creates one density texture sub-asset per existing tile (seeded full

        /// so the layer is visible immediately). Call <see cref="AddGrassLayerWithBlades"/> to also

        /// add a procedural blade mesh in one step.

        /// </summary>

        public static GrassLayer AddGrassLayer(WorldMapAsset map, string layerName)

        {

            string mapPath = AssetDatabase.GetAssetPath(map);

            var layer = ScriptableObject.CreateInstance<GrassLayer>();

            layer.name = $"Grass_{layerName}";

            AssetDatabase.AddObjectToAsset(layer, mapPath);

            Material? material = CreateGrassMaterial(layer.name);

            if (material != null)

            {

                AssetDatabase.AddObjectToAsset(material, mapPath);

                layer.EditorSetMaterial(material);

            }

            // Apply sensible defaults — empty structs default to all-zero (no wind, no bend, zero
            // blade height, no LOD) which renders nothing visible. These match the legacy
            // hand-tuned ScatterLayer authoring so a brand-new GrassLayer is immediately usable.
            ApplyGrassLayerDefaults(layer);

            // Eagerly create one density texture per existing tile, seeded EMPTY (zero R channel).
            // New grass layers must start blank — the artist paints density in afterwards. This
            // mirrors the per-tile-added path above (seedFull: false), keeping both entry points
            // consistent.

            var tileDensities = new List<TileDensityTexture>();

            foreach (var tile in map.EnumerateTiles())

            {

                Texture2D tex = CreateDensityMap(

                    $"{layer.name}@{TileSubAssetName(tile.tileCoord)}",

                    seedFull: false);

                AssetDatabase.AddObjectToAsset(tex, mapPath);

                tileDensities.Add(new TileDensityTexture { coord = tile.tileCoord, tex = tex });

            }

            if (tileDensities.Count > 0)

            {

                layer.EditorSetTileDensities(tileDensities.ToArray());

            }

            map.RegisterSurfaceLayer(layer);

            EditorUtility.SetDirty(map);

            EditorUtility.SetDirty(layer);

            AssetDatabase.SaveAssets();

            return layer;

        }

        /// <summary>

        /// Removes a unified surface layer and its owned sub-assets (TerrainLayerSet for splat;

        /// per-tile density maps + material for grass).

        /// </summary>

        public static void RemoveSurfaceLayer(WorldMapAsset map, WorldPainterLayer layer)

        {

            string mapPath = AssetDatabase.GetAssetPath(map);

            if (layer is GrassLayer grass)

            {

                // Remove all per-tile density textures owned by this map.

                var tiles = grass.EditorTileDensities;

                if (tiles != null)

                {

                    foreach (var entry in tiles)

                    {

                        if (entry.tex != null &&

                            AssetDatabase.GetAssetPath(entry.tex) == mapPath)

                        {

                            AssetDatabase.RemoveObjectFromAsset(entry.tex);

                            Object.DestroyImmediate(entry.tex, allowDestroyingAssets: true);

                        }

                    }

                }

                Material? mat = grass.Render.Material;

                if (mat != null && AssetDatabase.GetAssetPath(mat) == mapPath)

                {

                    AssetDatabase.RemoveObjectFromAsset(mat);

                    Object.DestroyImmediate(mat, allowDestroyingAssets: true);

                }

                foreach (var lod in grass.Render.Lods)

                {

                    if (lod.mesh != null && AssetDatabase.GetAssetPath(lod.mesh) == mapPath)

                    {

                        AssetDatabase.RemoveObjectFromAsset(lod.mesh);

                        Object.DestroyImmediate(lod.mesh, allowDestroyingAssets: true);

                    }

                }

            }

            else if (layer is PropLayer prop)

            {

                // Remove companion AuthoredInstancesData sub-asset if owned by this map.

                AuthoredInstancesData? authored = prop.AuthoredInstances;

                if (authored != null && AssetDatabase.GetAssetPath(authored) == mapPath)

                {

                    AssetDatabase.RemoveObjectFromAsset(authored);

                    Object.DestroyImmediate(authored, allowDestroyingAssets: true);

                }

                // Remove material if owned by this map.

                Material? propMat = prop.Render.Material;

                if (propMat != null && AssetDatabase.GetAssetPath(propMat) == mapPath)

                {

                    AssetDatabase.RemoveObjectFromAsset(propMat);

                    Object.DestroyImmediate(propMat, allowDestroyingAssets: true);

                }

                // Remove LOD meshes if owned by this map.

                foreach (var lod in prop.Render.Lods)

                {

                    if (lod.mesh != null && AssetDatabase.GetAssetPath(lod.mesh) == mapPath)

                    {

                        AssetDatabase.RemoveObjectFromAsset(lod.mesh);

                        Object.DestroyImmediate(lod.mesh, allowDestroyingAssets: true);

                    }

                }

            }

            map.UnregisterSurfaceLayer(layer);

            AssetDatabase.RemoveObjectFromAsset(layer);

            Object.DestroyImmediate(layer, allowDestroyingAssets: true);

            EditorUtility.SetDirty(map);

            AssetDatabase.SaveAssets();

        }

        /// <summary>

        /// Adds a <see cref="PropLayer"/> to <see cref="WorldMapAsset.SurfaceLayers"/> and creates a

        /// companion <see cref="AuthoredInstancesData"/> sub-asset, wiring it into the layer.

        /// Assign LOD meshes, a material, and author instance records before runtime builds the engine.

        /// </summary>

        public static PropLayer AddPropLayer(WorldMapAsset map, string layerName)

        {

            string mapPath = AssetDatabase.GetAssetPath(map);

            var layer = ScriptableObject.CreateInstance<PropLayer>();

            layer.name = $"Prop_{layerName}";

            AssetDatabase.AddObjectToAsset(layer, mapPath);

            // Create a default material so InstancedPropEngine actually renders. Without one,
            // TryBuildPropLayerEngine bails with "Material is not assigned — no props will render".

            Material? propMat = CreatePropMaterial(layer.name);
            if (propMat != null)
            {
                AssetDatabase.AddObjectToAsset(propMat, mapPath);
                layer.EditorSetMaterial(propMat);
            }

            // Apply sensible defaults so the layer renders without manual tuning.
            ApplyPropLayerDefaults(layer);

            // Create companion AuthoredInstancesData sub-asset and wire it into the layer.

            var authoredData = ScriptableObject.CreateInstance<AuthoredInstancesData>();

            authoredData.name = $"{layer.name}_Authored";

            AssetDatabase.AddObjectToAsset(authoredData, mapPath);

            layer.EditorSetAuthored(authoredData);

            map.RegisterSurfaceLayer(layer);

            EditorUtility.SetDirty(map);

            EditorUtility.SetDirty(layer);

            EditorUtility.SetDirty(authoredData);
            if (propMat != null) EditorUtility.SetDirty(propMat);

            AssetDatabase.SaveAssets();

            return layer;

        }

        /// <summary>Apply sensible defaults to a freshly-created <see cref="GrassLayer"/>.</summary>
        private static void ApplyGrassLayerDefaults(GrassLayer layer)
        {
            layer.EditorSetWind(new ScatterWindConfig(
                windMode:         ScatterLayer.WindMode.Perlin,
                windDirection:    new Vector2(1f, 0f),
                windStrength:     0.5f,
                windFrequency:    1.5f,
                windNoiseScale:   0.4f,
                windGustScale:    1.0f,
                windRippleScale:  0.3f,
                windGustSpeed:    1.0f,
                windRippleSpeed:  1.2f,
                windRippleWeight: 0.5f));

            layer.EditorSetDeform(new ScatterDeformConfig(
                affectedByWind:        true,
                affectedByInteractors: true,
                bendStrength:          0.7f,
                flatten:               0.5f,
                recoveryRate:          0.5f));

            layer.EditorSetBounds(new ScatterBoundsConfig(
                maxBladeHeight: 0.8f,
                bendHeadroom:   0.2f,
                chunkSize:      32));

            layer.EditorSetPlacement(new ScatterPlacementConfig(groundSnapMask: ~0));
        }

        /// <summary>Apply sensible defaults to a freshly-created <see cref="PropLayer"/>.</summary>
        private static void ApplyPropLayerDefaults(PropLayer layer)
        {
            layer.EditorSetWind(new ScatterWindConfig(
                windMode:         ScatterLayer.WindMode.Perlin,
                windDirection:    new Vector2(1f, 0f),
                windStrength:     0.3f,
                windFrequency:    1.2f,
                windNoiseScale:   0.4f,
                windGustScale:    1.0f,
                windRippleScale:  0.3f,
                windGustSpeed:    1.0f,
                windRippleSpeed:  1.0f,
                windRippleWeight: 0.4f));

            layer.EditorSetDeform(new ScatterDeformConfig(
                affectedByWind:        true,
                affectedByInteractors: false,
                bendStrength:          0.3f,
                flatten:               0.0f,
                recoveryRate:          0.5f));

            layer.EditorSetBounds(new ScatterBoundsConfig(
                maxBladeHeight: 2.0f,
                bendHeadroom:   0.5f,
                chunkSize:      32));

            layer.EditorSetPlacement(new ScatterPlacementConfig(groundSnapMask: ~0));
        }

        /// <summary>
        /// Builds a default indirect-instanced material for a PropLayer. Reuses the same
        /// "WorldPainter/IndirectGrass" shader that grass uses (it consumes the BladeInstance
        /// StructuredBuffer that InstancedPropEngine binds). Marked as a sub-asset of the map.
        /// </summary>
        private static Material? CreatePropMaterial(string layerName)
        {
            var shader = Shader.Find("WorldPainter/IndirectGrass");
            if (shader == null)
            {
                Debug.LogWarning("[WorldPainter] WorldPainter/IndirectGrass shader missing — " +
                                 "PropLayer material left unassigned. Assign one manually.");
                return null;
            }
            return new Material(shader) { name = $"{layerName}_Material" };
        }

        /// <summary>

        /// Adds a <see cref="GrassLayer"/> with a procedural blade mesh — a fully renderable grass layer

        /// with zero art dependencies (demos / verification). Density textures are seeded full so the

        /// layer scatters immediately on all existing tiles.

        /// </summary>

        public static GrassLayer AddGrassLayerWithBlades(WorldMapAsset map, string layerName)

        {

            GrassLayer layer = AddGrassLayer(map, layerName);

            string mapPath = AssetDatabase.GetAssetPath(map);

            Mesh blade = CreateBladeMesh();

            AssetDatabase.AddObjectToAsset(blade, mapPath);

            layer.EditorSetLods(new[] { new ScatterLod { mesh = blade, maxDistance = 64f } });

            EditorUtility.SetDirty(layer);

            AssetDatabase.SaveAssets();

            return layer;

        }

        /// <summary>

        /// One-click demo: a grass layer with procedural blades. Renders immediately on a map

        /// that has tiles. (Phase 3 cleanup — splat seeding removed; add ground textures via the

        /// BrushDock TerrainPalette strip instead.)

        /// </summary>

        public static void CreateDemoSurfaceLayers(WorldMapAsset map)

        {

            AddGrassLayerWithBlades(map, "Meadow");

        }

        /// <summary>Procedural upright grass-blade quad (0.1m wide × 0.5m tall), up-facing normals.</summary>

        private static Mesh CreateBladeMesh()

        {

            var mesh = new Mesh { name = "GrassBlade" };

            mesh.vertices = new[]

            {

                new Vector3(-0.05f, 0f, 0f), new Vector3(0.05f, 0f, 0f),

                new Vector3(-0.04f, 0.5f, 0f), new Vector3(0.04f, 0.5f, 0f),

            };

            mesh.uv        = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };

            mesh.normals   = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };

            mesh.RecalculateBounds();

            return mesh;

        }

        /// <summary>

        /// Density-map sub-asset for a grass tile. <paramref name="seedFull"/> fills the R channel

        /// (full density) so the tile scatters everywhere until per-tile paint-routing lands.

        /// </summary>

        private static Texture2D CreateDensityMap(string baseName, bool seedFull)

        {

            int res = WorldMapGrid.DENSITY_RES;

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: false, linear: true)

            {

                name       = $"{baseName}_Density",

                wrapMode   = TextureWrapMode.Clamp,

                filterMode = FilterMode.Bilinear,

            };

            var pixels = new Color[res * res];

            if (seedFull)

                for (int i = 0; i < pixels.Length; i++)

                    pixels[i] = new Color(1f, 0f, 0f, 1f);

            tex.SetPixels(pixels);

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return tex;

        }

        // (Phase 3 cleanup — albedo / TerrainLayerSet factory removed.)

        // ── Grass material sub-asset factory ──────────────────────────────────

        /// <summary>Shader for the per-layer grass material — the instanced render tier reads it.</summary>

        private const string INSTANCED_GRASS_SHADER = "WorldPainter/InstancedGrass";

        /// <summary>

        /// Creates a default grass material on the <see cref="INSTANCED_GRASS_SHADER"/> shader.

        /// Returns null (with a surfaced error) when the shader cannot be found — the layer is

        /// then created without a material and the user assigns one manually.

        /// </summary>

        private static Material? CreateGrassMaterial(string layerBaseName)

        {

            Shader? shader = Shader.Find(INSTANCED_GRASS_SHADER);

            if (shader == null)

            {

                Debug.LogError(

                    $"[WorldPainter] Grass shader '{INSTANCED_GRASS_SHADER}' not found — new layer " +

                    "created without a material; assign one manually on the layer's Render config.");

                return null;

            }

            return new Material(shader) { name = $"{layerBaseName}_Material" };

        }

        /// <summary>Assigns <paramref name="material"/> to the layer's <c>render.material</c> serialized field.</summary>

        private static void AssignRenderMaterial(ScatterLayer layer, Material material)

        {

            using var so = new SerializedObject(layer);

            var prop = so.FindProperty("render")?.FindPropertyRelative("material");

            if (prop == null) return;

            prop.objectReferenceValue = material;

            so.ApplyModifiedPropertiesWithoutUndo();

        }

    }

}
