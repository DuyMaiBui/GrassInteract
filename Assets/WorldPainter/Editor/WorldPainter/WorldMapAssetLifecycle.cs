#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// The ONLY path for adding and removing tiles and layers in a <see cref="WorldMapAsset"/>.
    ///
    /// Every mutation follows the pattern:
    ///   1. <c>AssetDatabase.AddObjectToAsset</c> / <c>RemoveObjectFromAsset</c> + <c>DestroyImmediate</c>
    ///   2. <c>EditorUtility.SetDirty(map)</c>
    ///   3. <c>AssetDatabase.SaveAssets()</c>
    ///
    /// Sign-safe sub-asset naming uses underscores for the coordinate separator, with "n" prefix
    /// for negative axes (e.g. Tile_n1_0 for (-1,0)).
    ///
    /// Per-tile density channels are allocated when layers are added and freed when removed.
    /// </summary>
    public static class WorldMapAssetLifecycle
    {
        // â”€â”€ Naming helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // â”€â”€ Tile lifecycle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Creates a new <see cref="TerrainTileAsset"/> sub-asset at <paramref name="coord"/> inside
        /// <paramref name="map"/>. No-op if a tile already exists at that coord.
        ///
        /// After this call: <c>map.GetTile(coord)</c> is non-null; the asset file is saved.
        /// </summary>
        /// <param name="map">The container map (must be a saved asset â€” <c>AssetDatabase.GetAssetPath</c> must return non-empty).</param>
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
            // splatData is left empty (optional in TerrainTileGpuResources.Upload; lazily
            // allocated by the splat brush on first paint).
            tile.heightData = new byte[tile.ExpectedHeightBytes];

            string mapPath = AssetDatabase.GetAssetPath(map);
            AssetDatabase.AddObjectToAsset(tile, mapPath);

            // Allocate density channels for all existing scatter layers.
            foreach (var layer in map.Layers)
                tile.AllocateDensityChannel(layer.name);

            map.RegisterTile(coord, tile);

            // For each GrassLayer variant, create a density texture sub-asset for this new tile
            // and append it to the variant's densityTiles array.
            foreach (var surfaceLayer in map.SurfaceLayers)
            {
                if (surfaceLayer is not GrassLayer grass) continue;
                GrassVariant[] palette = grass.EditorPalette;
                for (int vi = 0; vi < palette.Length; ++vi)
                {
                    Texture2D tex = CreateDensityMap(
                        $"{grass.name}#{vi}@{TileSubAssetName(coord)}",
                        seedFull: false);
                    AssetDatabase.AddObjectToAsset(tex, mapPath);

                    var prevTiles = palette[vi].densityTiles ?? System.Array.Empty<TileDensityTexture>();
                    var newTiles = new TileDensityTexture[prevTiles.Length + 1];
                    prevTiles.CopyTo(newTiles, 0);
                    newTiles[prevTiles.Length] = new TileDensityTexture { coord = coord, tex = tex };
                    palette[vi].densityTiles = newTiles;
                }
                grass.EditorSetPalette(palette);
                EditorUtility.SetDirty(grass);
            }

            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(tile);
            AssetDatabase.SaveAssets();

            return tile;
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

            // Remove per-tile density textures for all GrassLayer variants at this coord.
            foreach (var surfaceLayer in map.SurfaceLayers)
            {
                if (surfaceLayer is not GrassLayer grass) continue;
                GrassVariant[] palette = grass.EditorPalette;
                bool changed = false;
                for (int vi = 0; vi < palette.Length; ++vi)
                {
                    var existing = palette[vi].densityTiles;
                    if (existing == null || existing.Length == 0) continue;

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
                        }
                        else
                        {
                            kept.Add(entry);
                        }
                    }
                    if (kept.Count != existing.Length)
                    {
                        palette[vi].densityTiles = kept.ToArray();
                        changed = true;
                    }
                }
                if (changed)
                {
                    grass.EditorSetPalette(palette);
                    EditorUtility.SetDirty(grass);
                }
            }

            map.UnregisterTile(coord);
            AssetDatabase.RemoveObjectFromAsset(tile);
            Object.DestroyImmediate(tile, allowDestroyingAssets: true);

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }

        // â”€â”€ Layer lifecycle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Adds a <see cref="DensityScatterLayer"/> def sub-asset to <paramref name="map"/> and allocates
        /// a density channel on every existing tile.
        /// </summary>
        /// <param name="map">The container map.</param>
        /// <param name="layerName">Unique base name for this layer (used as layerId).</param>
        /// <returns>The newly created layer def.</returns>
        public static DensityScatterLayer AddDensityLayer(WorldMapAsset map, string layerName)
        {
            var layer = ScriptableObject.CreateInstance<DensityScatterLayer>();
            layer.name = LayerSubAssetName(layerName);

            string mapPath = AssetDatabase.GetAssetPath(map);
            AssetDatabase.AddObjectToAsset(layer, mapPath);
            map.RegisterLayer(layer);

            // Create a blank density-map texture as a sub-asset and assign it so the new layer
            // passes DensityScatterLayer.Validate (non-null, readable, uncompressed) and is
            // immediately paintable + renderable. Removed alongside the layer in RemoveLayer.
            Texture2D densityMap = CreateBlankDensityMap(layer.name);
            AssetDatabase.AddObjectToAsset(densityMap, mapPath);
            AssignDensityMap(layer, densityMap);

            // Create a default grass material as a sub-asset and assign it to render.material —
            // the instanced render tier (GrassRenderer) reads ScatterRenderConfig.Material.
            // Removed alongside the layer in RemoveLayer.
            Material? material = CreateGrassMaterial(layer.name);
            if (material != null)
            {
                AssetDatabase.AddObjectToAsset(material, mapPath);
                AssignRenderMaterial(layer, material);
            }

            // Allocate a per-tile density channel for all existing tiles.
            AllocateDensityChannelOnAllTiles(map, layer.name);

            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();

            return layer;
        }

        /// <summary>
        /// Adds an <see cref="InstanceScatterLayer"/> def sub-asset to <paramref name="map"/>.
        /// Instance layers use per-tile <see cref="TilePropBucket"/>s (not density channels).
        /// </summary>
        /// <param name="map">The container map.</param>
        /// <param name="layerName">Unique base name for this layer (used as layerId).</param>
        /// <returns>The newly created layer def.</returns>
        public static InstanceScatterLayer AddInstanceLayer(WorldMapAsset map, string layerName)
        {
            var layer = ScriptableObject.CreateInstance<InstanceScatterLayer>();
            layer.name = LayerSubAssetName(layerName);

            string mapPath = AssetDatabase.GetAssetPath(map);
            AssetDatabase.AddObjectToAsset(layer, mapPath);
            map.RegisterLayer(layer);

            // Allocate a prop bucket on all existing tiles.
            AllocatePropBucketOnAllTiles(map, layer.name);

            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();

            return layer;
        }

        /// <summary>
        /// Removes a scatter layer def sub-asset from <paramref name="map"/> and frees per-tile channels/buckets.
        /// Works for both <see cref="DensityScatterLayer"/> and <see cref="InstanceScatterLayer"/>.
        /// </summary>
        public static void RemoveLayer(WorldMapAsset map, ScatterLayer layer)
        {
            string layerId = layer.name;
            bool isDensity = layer is DensityScatterLayer;

            // Free per-tile resources before removing the layer def.
            if (isDensity)
                FreeDensityChannelOnAllTiles(map, layerId);
            else
                FreePropBucketOnAllTiles(map, layerId);

            // Remove auto-created sub-assets (see AddDensityLayer) so no orphan sub-asset
            // survives. Only removes objects that live inside THIS map.
            if (layer is DensityScatterLayer densityLayer)
            {
                Texture2D? densityMap = densityLayer.DensityMap;
                if (densityMap != null &&
                    AssetDatabase.GetAssetPath(densityMap) == AssetDatabase.GetAssetPath(map))
                {
                    AssetDatabase.RemoveObjectFromAsset(densityMap);
                    Object.DestroyImmediate(densityMap, allowDestroyingAssets: true);
                }
            }

            Material? renderMat = layer.Render.Material;
            if (renderMat != null &&
                AssetDatabase.GetAssetPath(renderMat) == AssetDatabase.GetAssetPath(map))
            {
                AssetDatabase.RemoveObjectFromAsset(renderMat);
                Object.DestroyImmediate(renderMat, allowDestroyingAssets: true);
            }

            map.UnregisterLayer(layer);
            AssetDatabase.RemoveObjectFromAsset(layer);
            Object.DestroyImmediate(layer, allowDestroyingAssets: true);

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }

        // ── Surface layer lifecycle (unified SplatLayer + GrassLayer) ─────────

        /// <summary>
        /// Adds a <see cref="SplatLayer"/> to <see cref="WorldMapAsset.SurfaceLayers"/> and creates
        /// its <see cref="TerrainLayerSet"/> palette sub-asset, wiring it to BOTH the layer and the
        /// map-level splatSet that <c>GpuTerrainEngine</c> binds. Assign albedo textures on the set.
        /// </summary>
        public static SplatLayer AddSplatLayer(WorldMapAsset map, string layerName)
        {
            string mapPath = AssetDatabase.GetAssetPath(map);

            var layer = ScriptableObject.CreateInstance<SplatLayer>();
            layer.name = $"Splat_{layerName}";
            AssetDatabase.AddObjectToAsset(layer, mapPath);

            var set = ScriptableObject.CreateInstance<TerrainLayerSet>();
            set.name = $"{layer.name}_LayerSet";
            AssetDatabase.AddObjectToAsset(set, mapPath);
            layer.SetLayerSet(set);
            map.SetSplatSet(set);

            // Create a blank albedo texture as a sub-asset and assign it into the set
            // so BuildArray() returns a valid Texture2DArray immediately (not null).
            // Albedo is sRGB (linear: false). Normal maps are intentionally NOT added here.
            Texture2D albedo = CreateBlankAlbedo($"{layer.name}_Albedo0");
            AssetDatabase.AddObjectToAsset(albedo, mapPath);
            AssignLayerAlbedos(set, albedo);

            map.RegisterSurfaceLayer(layer);
            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(layer);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return layer;
        }

        /// <summary>
        /// Adds a <see cref="GrassLayer"/> to <see cref="WorldMapAsset.SurfaceLayers"/> with a default
        /// grass material. Assign a blade mesh to its Render LODs and call <see cref="AddGrassVariant"/>
        /// per texture variant.
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

            map.RegisterSurfaceLayer(layer);
            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();
            return layer;
        }

        /// <summary>
        /// Appends a variant to a <see cref="GrassLayer"/>, creating one density <see cref="Texture2D"/>
        /// sub-asset per existing tile. <paramref name="seedFullDensity"/> fills each tile's density
        /// texture so the variant scatters everywhere immediately (visual verification).
        /// If the map has no tiles yet the variant is created with an empty <c>densityTiles</c> array
        /// (valid — renders nothing until a tile exists).
        /// </summary>
        public static void AddGrassVariant(WorldMapAsset map, GrassLayer layer, string variantName,
            bool seedFullDensity = false)
        {
            string mapPath = AssetDatabase.GetAssetPath(map);
            int index = layer.PaletteCount;

            // Build one density texture per existing tile.
            var tileDensities = new List<TileDensityTexture>();
            foreach (var tile in map.EnumerateTiles())
            {
                Texture2D tex = CreateDensityMap(
                    $"{layer.name}#{index}@{TileSubAssetName(tile.tileCoord)}",
                    seedFullDensity);
                AssetDatabase.AddObjectToAsset(tex, mapPath);
                tileDensities.Add(new TileDensityTexture { coord = tile.tileCoord, tex = tex });
            }

            var variants = new List<GrassVariant>(layer.EditorPalette)
            {
                new GrassVariant
                {
                    name         = variantName,
                    texture      = null,
                    densityTiles = tileDensities.ToArray(),
                },
            };
            layer.EditorSetPalette(variants.ToArray());

            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Removes a unified surface layer and its owned sub-assets (TerrainLayerSet for splat;
        /// per-variant density maps + material for grass).
        /// </summary>
        public static void RemoveSurfaceLayer(WorldMapAsset map, WorldPainterLayer layer)
        {
            string mapPath = AssetDatabase.GetAssetPath(map);

            if (layer is SplatLayer splat)
            {
                TerrainLayerSet? set = splat.LayerSet;
                if (set != null && AssetDatabase.GetAssetPath(set) == mapPath)
                {
                    // Remove albedo sub-assets that are owned by this map before destroying the set.
                    foreach (Texture2D albedo in set.EditorAlbedos)
                    {
                        if (albedo != null && AssetDatabase.GetAssetPath(albedo) == mapPath)
                        {
                            AssetDatabase.RemoveObjectFromAsset(albedo);
                            Object.DestroyImmediate(albedo, allowDestroyingAssets: true);
                        }
                    }

                    if (map.SplatSet == set) map.SetSplatSet(null);
                    AssetDatabase.RemoveObjectFromAsset(set);
                    Object.DestroyImmediate(set, allowDestroyingAssets: true);
                }
            }
            else if (layer is GrassLayer grass)
            {
                foreach (var v in grass.Palette)
                {
                    if (v.densityTiles == null) continue;
                    foreach (var entry in v.densityTiles)
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

            // Create companion AuthoredInstancesData sub-asset and wire it into the layer.
            var authoredData = ScriptableObject.CreateInstance<AuthoredInstancesData>();
            authoredData.name = $"{layer.name}_Authored";
            AssetDatabase.AddObjectToAsset(authoredData, mapPath);
            layer.EditorSetAuthored(authoredData);

            map.RegisterSurfaceLayer(layer);
            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(layer);
            EditorUtility.SetDirty(authoredData);
            AssetDatabase.SaveAssets();
            return layer;
        }

        /// <summary>
        /// Adds a <see cref="GrassLayer"/> with a procedural blade mesh + <paramref name="variantCount"/>
        /// seeded variants — a fully renderable grass layer with zero art dependencies (demos / verification).
        /// </summary>
        public static GrassLayer AddGrassLayerWithBlades(WorldMapAsset map, string layerName, int variantCount)
        {
            GrassLayer layer = AddGrassLayer(map, layerName);
            string mapPath = AssetDatabase.GetAssetPath(map);

            Mesh blade = CreateBladeMesh();
            AssetDatabase.AddObjectToAsset(blade, mapPath);
            layer.EditorSetLods(new[] { new ScatterLod { mesh = blade, maxDistance = 64f } });

            for (int i = 0; i < variantCount; i++)
                AddGrassVariant(map, layer, $"Variant{(char)('A' + i)}", seedFullDensity: true);

            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();
            return layer;
        }

        /// <summary>
        /// One-click demo: a splat layer (assign albedos to its TerrainLayerSet) + a grass layer with
        /// procedural blades and 2 seeded variants. Renders immediately on a map that has tiles.
        /// </summary>
        public static void CreateDemoSurfaceLayers(WorldMapAsset map)
        {
            AddSplatLayer(map, "Ground");
            AddGrassLayerWithBlades(map, "Meadow", variantCount: 2);
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
        /// Density-map sub-asset for a grass variant. <paramref name="seedFull"/> fills the R channel
        /// (full density) so the variant scatters everywhere until per-variant paint-routing lands.
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

        // ── Density-map sub-asset factory ─────────────────────────────────────

        /// <summary>
        /// Creates a blank, readable, uncompressed density map sized to
        /// <see cref="WorldMapGrid.DENSITY_RES"/>. Black = zero density until the user paints.
        /// RGBA32 (density in the R channel) is used for guaranteed <c>SetPixels</c> support —
        /// the same call <see cref="WorldPainterDensityEncoder"/> uses on mouse-up paint.
        /// </summary>
        private static Texture2D CreateBlankDensityMap(string layerBaseName)
        {
            int res = WorldMapGrid.DENSITY_RES;
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name       = $"{layerBaseName}_Density",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            tex.SetPixels(new Color[res * res]); // default (0,0,0,0) → zero density
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        // ── Albedo sub-asset factory ──────────────────────────────────────────

        /// <summary>
        /// Creates a 4×4 white, sRGB (non-linear) blank albedo texture for a new splat layer.
        /// Small size keeps asset overhead negligible; the user replaces it with their art.
        /// RGBA32 ensures <c>SetPixels32</c> support and compatibility with <see cref="TerrainLayerSet.BuildArray"/>.
        /// </summary>
        private static Texture2D CreateBlankAlbedo(string texName)
        {
            const int SIZE = 4;
            var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name       = texName,
                wrapMode   = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[SIZE * SIZE];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        /// <summary>
        /// Assigns <paramref name="albedo"/> as the first entry of <see cref="TerrainLayerSet.layerAlbedos"/>
        /// via <see cref="SerializedObject"/>, mirroring the <see cref="AssignDensityMap"/> pattern.
        /// </summary>
        private static void AssignLayerAlbedos(TerrainLayerSet set, Texture2D albedo)
        {
            using var so = new SerializedObject(set);
            var prop = so.FindProperty("layerAlbedos");
            if (prop == null) return;
            prop.arraySize = 1;
            prop.GetArrayElementAtIndex(0).objectReferenceValue = albedo;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Assigns <paramref name="densityMap"/> to the layer's private serialized field.</summary>
        private static void AssignDensityMap(DensityScatterLayer layer, Texture2D densityMap)
        {
            using var so = new SerializedObject(layer);
            var prop = so.FindProperty("densityMap");
            if (prop == null) return;
            prop.objectReferenceValue = densityMap;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

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

        // â”€â”€ Per-tile channel/bucket allocation helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void AllocateDensityChannelOnAllTiles(WorldMapAsset map, string layerId)
        {
            foreach (var tile in map.EnumerateTiles())
            {
                tile.AllocateDensityChannel(layerId);
                EditorUtility.SetDirty(tile);
            }
        }

        private static void FreeDensityChannelOnAllTiles(WorldMapAsset map, string layerId)
        {
            foreach (var tile in map.EnumerateTiles())
            {
                tile.FreeDensityChannel(layerId);
                EditorUtility.SetDirty(tile);
            }
        }

        private static void AllocatePropBucketOnAllTiles(WorldMapAsset map, string layerId)
        {
            foreach (var tile in map.EnumerateTiles())
            {
                tile.AllocatePropBucket(layerId);
                EditorUtility.SetDirty(tile);
            }
        }

        private static void FreePropBucketOnAllTiles(WorldMapAsset map, string layerId)
        {
            foreach (var tile in map.EnumerateTiles())
            {
                tile.FreePropBucket(layerId);
                EditorUtility.SetDirty(tile);
            }
        }
    }
}
