#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// One-shot menu items that generate the seed assets in Editor/Defaults/.
    ///
    /// Menu: Tools > GrassInteract > Build Default Assets
    ///
    /// Assets produced:
    ///   - Default_LOD0_Prop.mesh   — 0.5 m cube mesh
    ///   - Default_Material.mat     — copy of GrassInteractIndirectMat (or Unlit/fallback)
    ///   - Default_LOD0_Grass.mesh  — copy-reference to Meshes/GrassBlade_LOD0
    ///   - Default_DensityMap_512_white.png — 512x512 R8 white (template only)
    ///
    /// These are EDITOR-ONLY seed assets; runtime does not ship them.
    /// </summary>
    public static class BuildDefaultAssets
    {
        private const string DEFAULTS_DIR  = "Assets/GrassInteract/Editor/Defaults";
        private const string PROP_MESH     = DEFAULTS_DIR + "/Default_LOD0_Prop.mesh";
        private const string GRASS_MESH    = DEFAULTS_DIR + "/Default_LOD0_Grass.mesh";
        private const string MATERIAL      = DEFAULTS_DIR + "/Default_Material.mat";
        private const string DENSITY_TEX   = DEFAULTS_DIR + "/Default_DensityMap_512_white.png";

        private const string SRC_GRASS_MESH = "Assets/GrassInteract/Meshes/GrassBlade_LOD0.asset";
        private const string SRC_MATERIAL   = "Assets/GrassInteract/Demo/GrassInteractIndirectMat.mat";

        [MenuItem("Tools/GrassInteract/Build Default Assets")]
        public static void BuildAll()
        {
            EnsureDir(DEFAULTS_DIR);
            BuildPropMesh();
            CopyGrassMesh();
            CopyMaterial();
            BuildDensityTexture();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GrassInteract] Default assets built in Editor/Defaults/.");
        }

        // ── Prop mesh (0.5m cube) ─────────────────────────────────────────────

        private static void BuildPropMesh()
        {
            if (AssetDatabase.LoadAssetAtPath<Mesh>(PROP_MESH) != null) return;

            Mesh mesh = BuildCubeMesh(0.5f, "Default_LOD0_Prop");
            AssetDatabase.CreateAsset(mesh, PROP_MESH);
        }

        private static Mesh BuildCubeMesh(float halfSize, string meshName)
        {
            float h = halfSize;

            Vector3[] vertices =
            {
                // Front
                new(-h, -h,  h), new( h, -h,  h), new( h,  h,  h), new(-h,  h,  h),
                // Back
                new( h, -h, -h), new(-h, -h, -h), new(-h,  h, -h), new( h,  h, -h),
                // Left
                new(-h, -h, -h), new(-h, -h,  h), new(-h,  h,  h), new(-h,  h, -h),
                // Right
                new( h, -h,  h), new( h, -h, -h), new( h,  h, -h), new( h,  h,  h),
                // Top
                new(-h,  h,  h), new( h,  h,  h), new( h,  h, -h), new(-h,  h, -h),
                // Bottom
                new(-h, -h, -h), new( h, -h, -h), new( h, -h,  h), new(-h, -h,  h),
            };

            Vector3[] normals =
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.back,    Vector3.back,    Vector3.back,    Vector3.back,
                Vector3.left,    Vector3.left,    Vector3.left,    Vector3.left,
                Vector3.right,   Vector3.right,   Vector3.right,   Vector3.right,
                Vector3.up,      Vector3.up,      Vector3.up,      Vector3.up,
                Vector3.down,    Vector3.down,    Vector3.down,    Vector3.down,
            };

            Vector2[] uv =
            {
                new(0,0), new(1,0), new(1,1), new(0,1),
                new(0,0), new(1,0), new(1,1), new(0,1),
                new(0,0), new(1,0), new(1,1), new(0,1),
                new(0,0), new(1,0), new(1,1), new(0,1),
                new(0,0), new(1,0), new(1,1), new(0,1),
                new(0,0), new(1,0), new(1,1), new(0,1),
            };

            int[] triangles =
            {
                0,  2,  1,   0,  3,  2,
                4,  6,  5,   4,  7,  6,
                8,  10, 9,   8,  11, 10,
                12, 14, 13,  12, 15, 14,
                16, 18, 17,  16, 19, 18,
                20, 22, 21,  20, 23, 22,
            };

            var mesh = new Mesh { name = meshName };
            mesh.vertices  = vertices;
            mesh.normals   = normals;
            mesh.uv        = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── Grass mesh (copy-reference to LOD0) ───────────────────────────────

        private static void CopyGrassMesh()
        {
            if (AssetDatabase.LoadAssetAtPath<Mesh>(GRASS_MESH) != null) return;
            if (!File.Exists(SRC_GRASS_MESH))
            {
                Debug.LogWarning($"[GrassInteract] Source grass mesh not found: {SRC_GRASS_MESH}. " +
                                 "Skipping Default_LOD0_Grass.mesh copy.");
                return;
            }
            AssetDatabase.CopyAsset(SRC_GRASS_MESH, GRASS_MESH);
        }

        // ── Material (copy of demo mat, or fallback) ──────────────────────────

        private static void CopyMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(MATERIAL) != null) return;

            if (AssetDatabase.LoadAssetAtPath<Material>(SRC_MATERIAL) != null)
            {
                AssetDatabase.CopyAsset(SRC_MATERIAL, MATERIAL);
            }
            else
            {
                // Fallback: plain white Unlit/Color material.
                Shader sh = Shader.Find("Unlit/Color") ?? Shader.Find("Standard")!;
                var mat   = new Material(sh) { name = "Default_Material", color = Color.white };
                AssetDatabase.CreateAsset(mat, MATERIAL);
            }
        }

        // ── Density texture (512x512 R8 white) ───────────────────────────────

        private static void BuildDensityTexture()
        {
            // Only generate if the PNG doesn't already exist.
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(DENSITY_TEX) != null) return;

            const int SIZE = 512;
            var tex = new Texture2D(SIZE, SIZE, TextureFormat.R8, false)
            {
                name      = "Default_DensityMap_512_white",
                wrapMode  = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[SIZE * SIZE];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 0, 0, 255);   // R8: red channel = 1.0
            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string absPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath)!,
                DENSITY_TEX.Replace('/', Path.DirectorySeparatorChar));

            File.WriteAllBytes(absPath, png);
            AssetDatabase.ImportAsset(DENSITY_TEX);

            // Configure importer: readable + R8 uncompressed.
            var importer = AssetImporter.GetAtPath(DENSITY_TEX) as TextureImporter;
            if (importer != null)
            {
                importer.isReadable          = true;
                importer.mipmapEnabled       = false;
                importer.filterMode          = FilterMode.Bilinear;
                importer.wrapMode            = TextureWrapMode.Clamp;
                importer.textureCompression  = TextureImporterCompression.Uncompressed;
                importer.textureType         = TextureImporterType.Default;
                importer.sRGBTexture         = false;

                var settings = importer.GetDefaultPlatformTextureSettings();
                settings.format              = TextureImporterFormat.R8;
                settings.overridden          = true;
                importer.SetPlatformTextureSettings(settings);

                importer.SaveAndReimport();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void EnsureDir(string dir)
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
