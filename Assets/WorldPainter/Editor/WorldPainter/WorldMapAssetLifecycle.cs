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

            // splatData is left empty (optional in TerrainTileGpuResources.Upload; lazily

            // allocated by the splat brush on first paint).

            tile.heightData = new byte[tile.ExpectedHeightBytes];

            string mapPath = AssetDatabase.GetAssetPath(map);

            AssetDatabase.AddObjectToAsset(tile, mapPath);

            map.RegisterTile(coord, tile);

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

            // Eagerly create one density texture per existing tile (seeded full = visible immediately).

            var tileDensities = new List<TileDensityTexture>();

            foreach (var tile in map.EnumerateTiles())

            {

                Texture2D tex = CreateDensityMap(

                    $"{layer.name}@{TileSubAssetName(tile.tileCoord)}",

                    seedFull: true);

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

        /// One-click demo: a splat layer (assign albedos to its TerrainLayerSet) + a grass layer with

        /// procedural blades. Renders immediately on a map that has tiles.

        /// </summary>

        public static void CreateDemoSurfaceLayers(WorldMapAsset map)

        {

            AddSplatLayer(map, "Ground");

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
