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

            // Allocate density channels for all existing layers.
            foreach (var layer in map.Layers)
                tile.AllocateDensityChannel(layer.name);

            map.RegisterTile(coord, tile);
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

            map.RegisterSurfaceLayer(layer);
            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(layer);
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
        /// Appends a variant (texture + own density map sub-asset) to a <see cref="GrassLayer"/>.
        /// <paramref name="seedFullDensity"/> fills the density map so the variant scatters across the
        /// whole field immediately (visual verification before paint-routing exists).
        /// </summary>
        public static void AddGrassVariant(WorldMapAsset map, GrassLayer layer, string variantName,
            bool seedFullDensity = false)
        {
            string mapPath = AssetDatabase.GetAssetPath(map);
            int index = layer.PaletteCount;

            Texture2D density = CreateDensityMap($"{layer.name}#{index}", seedFullDensity);
            AssetDatabase.AddObjectToAsset(density, mapPath);

            var variants = new List<GrassVariant>(layer.EditorPalette)
            {
                new GrassVariant { name = variantName, texture = null, densityMap = density },
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
                    if (map.SplatSet == set) map.SetSplatSet(null);
                    AssetDatabase.RemoveObjectFromAsset(set);
                    Object.DestroyImmediate(set, allowDestroyingAssets: true);
                }
            }
            else if (layer is GrassLayer grass)
            {
                foreach (var v in grass.Palette)
                {
                    if (v.densityMap != null &&
                        AssetDatabase.GetAssetPath(v.densityMap) == mapPath)
                    {
                        AssetDatabase.RemoveObjectFromAsset(v.densityMap);
                        Object.DestroyImmediate(v.densityMap, allowDestroyingAssets: true);
                    }
                }

                Material? mat = grass.Render.Material;
                if (mat != null && AssetDatabase.GetAssetPath(mat) == mapPath)
                {
                    AssetDatabase.RemoveObjectFromAsset(mat);
                    Object.DestroyImmediate(mat, allowDestroyingAssets: true);
                }
            }

            map.UnregisterSurfaceLayer(layer);
            AssetDatabase.RemoveObjectFromAsset(layer);
            Object.DestroyImmediate(layer, allowDestroyingAssets: true);
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
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
