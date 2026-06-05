#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// Builds placeholder prop meshes (procedural rock shapes) used by Mesh-kind
    /// <see cref="ScatterLayer"/>s, and a one-shot menu that scaffolds a complete test layer
    /// (3 LOD meshes + material + Mesh-kind layer wired into the demo config).
    ///
    /// Menu items:
    ///   - <c>Tools/GrassInteract/Build Prop Mesh</c> — LOD0 only (legacy).
    ///   - <c>Tools/GrassInteract/Build Prop Rock + Demo Layer</c> — 3 LODs + material + auto-wired layer.
    /// </summary>
    public static class ScatterPropMeshBuilder
    {
        private const string MESH_DIR        = "Assets/GrassInteract/Meshes";
        private const string MESH_PATH_LOD0  = MESH_DIR + "/ScatterPropRock_LOD0.mesh";
        private const string MESH_PATH_LOD1  = MESH_DIR + "/ScatterPropRock_LOD1.mesh";
        private const string MESH_PATH_LOD2  = MESH_DIR + "/ScatterPropRock_LOD2.mesh";

        private const string DEMO_DIR        = "Assets/GrassInteract/Demo";
        private const string MAT_PATH        = DEMO_DIR + "/ScatterPropRock.mat";
        private const string DEMO_CFG_PATH   = DEMO_DIR + "/GrassInteractDemoScatterConfig.asset";
        private const string SHADER_NAME     = "GrassInteract/ScatterInstanced";

        // ── Legacy single-LOD menu (kept for backwards compatibility) ─────────

        [MenuItem("Tools/GrassInteract/Build Prop Mesh")]
        public static Mesh BuildPropMesh()
        {
            EnsureDir(MESH_DIR);
            Mesh mesh = BuildRockMeshLOD0();
            SaveMeshAsset(mesh, MESH_PATH_LOD0);
            Debug.Log($"[ScatterPropMeshBuilder] Saved placeholder rock mesh to {MESH_PATH_LOD0}");
            return mesh;
        }

        // ── One-shot: 3 LOD meshes + material + auto-wired demo layer ─────────

        [MenuItem("Tools/GrassInteract/Build Prop Rock + Demo Layer")]
        public static void BuildPropRockAndDemoLayer()
        {
            // 1. Generate the 3 LOD meshes.
            EnsureDir(MESH_DIR);
            Mesh lod0 = SaveMeshAsset(BuildRockMeshLOD0(), MESH_PATH_LOD0);
            Mesh lod1 = SaveMeshAsset(BuildRockMeshLOD1(), MESH_PATH_LOD1);
            Mesh lod2 = SaveMeshAsset(BuildRockMeshLOD2(), MESH_PATH_LOD2);

            // 2. Create / refresh the rock material.
            EnsureDir(DEMO_DIR);
            Material mat = LoadOrCreateRockMaterial();

            // 3. Load the demo config; bail loudly if missing.
            var config = AssetDatabase.LoadAssetAtPath<TerrainScatterConfig>(DEMO_CFG_PATH);
            if (config == null)
            {
                EditorUtility.DisplayDialog("Build Prop Rock",
                    $"Could not find demo config at:\n{DEMO_CFG_PATH}\n\n" +
                    "Make sure the demo scene has been set up first.",
                    "OK");
                return;
            }

            // 4. Create a new instance layer and configure it as a mesh-prop rock layer.
            InstanceScatterLayer layer = config.CreateInstanceLayer("Rock");
            WireRockLayer(layer, mat, lod0, lod1, lod2);

            // 5. Surface it: select the config so the inspector opens, ping the new layer asset.
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(layer);

            Debug.Log("[ScatterPropMeshBuilder] Built 3 LOD rock meshes, created " +
                      $"{MAT_PATH}, and added 'Rock' Mesh layer to {DEMO_CFG_PATH}. " +
                      "Open the config inspector and paint density on the new layer to scatter.");
        }

        // ── Layer wiring ──────────────────────────────────────────────────────

        private static void WireRockLayer(InstanceScatterLayer layer, Material mat, Mesh lod0, Mesh lod1, Mesh lod2)
        {
            using var so = new SerializedObject(layer);

            // Kind field removed in Phase A (route via InteractsWithDeform = false for mesh-prop layers).
            // meshMaterial collapsed to 'material' in Phase A.
            so.FindProperty("material").objectReferenceValue = mat;
            so.FindProperty("affectedByWind").boolValue       = false;
            so.FindProperty("affectedByInteractors").boolValue = false;
            so.FindProperty("windStrength").floatValue         = 0f;
            so.FindProperty("bendStrength").floatValue         = 0f;

            // lods[] is a struct array (ScatterLod { Mesh mesh; float maxDistance }).
            SerializedProperty lods = so.FindProperty("lods");
            lods.arraySize = 3;
            WriteLod(lods.GetArrayElementAtIndex(0), lod0, 30f);
            WriteLod(lods.GetArrayElementAtIndex(1), lod1, 80f);
            WriteLod(lods.GetArrayElementAtIndex(2), lod2, 400f);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WriteLod(SerializedProperty elem, Mesh mesh, float maxDistance)
        {
            elem.FindPropertyRelative("mesh").objectReferenceValue = mesh;
            elem.FindPropertyRelative("maxDistance").floatValue    = maxDistance;
        }

        // ── Material ──────────────────────────────────────────────────────────

        private static Material LoadOrCreateRockMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MAT_PATH);
            if (existing != null) return existing;

            Shader sh = Shader.Find(SHADER_NAME);
            if (sh == null)
            {
                Debug.LogError($"[ScatterPropMeshBuilder] Shader '{SHADER_NAME}' not found. " +
                               "ScatterInstanced.shader missing from the project?");
                return new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.45f, 0.45f) };
            }

            var mat = new Material(sh)
            {
                name  = "ScatterPropRock",
                color = new Color(0.45f, 0.45f, 0.45f), // neutral grey
            };
            AssetDatabase.CreateAsset(mat, MAT_PATH);
            return mat;
        }

        // ── Mesh builders ─────────────────────────────────────────────────────

        /// <summary>LOD0: 24-vert diamond-pyramid (two square pyramids sharing a mid-rim).</summary>
        private static Mesh BuildRockMeshLOD0()
        {
            var verts = new Vector3[24];
            var norms = new Vector3[24];
            var uvs   = new Vector2[24];
            var tris  = new int[24];

            float half = 0.35f;
            float mid  = 0.55f;
            float apex = 1.0f;
            float bot  = 0.0f;

            Vector3 r0 = new(-half, mid, -half);
            Vector3 r1 = new( half, mid, -half);
            Vector3 r2 = new( half, mid,  half);
            Vector3 r3 = new(-half, mid,  half);
            Vector3 top   = new(0f, apex, 0f);
            Vector3 base_ = new(0f, bot,  0f);

            int vi = 0, ti = 0;
            void AddTri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                verts[vi] = a; norms[vi] = n; uvs[vi] = new Vector2(0.5f, 1f); tris[ti++] = vi++;
                verts[vi] = b; norms[vi] = n; uvs[vi] = new Vector2(0f, 0f);   tris[ti++] = vi++;
                verts[vi] = c; norms[vi] = n; uvs[vi] = new Vector2(1f, 0f);   tris[ti++] = vi++;
            }

            AddTri(top, r1, r0); AddTri(top, r2, r1); AddTri(top, r3, r2); AddTri(top, r0, r3);
            AddTri(r0, r1, base_); AddTri(r1, r2, base_); AddTri(r2, r3, base_); AddTri(r3, r0, base_);

            return BuildMesh("ScatterPropRock_LOD0", verts, norms, uvs, tris);
        }

        /// <summary>LOD1: octahedron — apex + anti-apex + 4-vert equator. 8 tris, 24 flat-shaded verts.</summary>
        private static Mesh BuildRockMeshLOD1()
        {
            var verts = new Vector3[24];
            var norms = new Vector3[24];
            var uvs   = new Vector2[24];
            var tris  = new int[24];

            float half = 0.40f;
            float mid  = 0.50f;
            float apex = 1.0f;
            float bot  = 0.0f;

            Vector3 e0 = new(-half, mid, 0f);
            Vector3 e1 = new( 0f,   mid, -half);
            Vector3 e2 = new( half, mid, 0f);
            Vector3 e3 = new( 0f,   mid,  half);
            Vector3 top   = new(0f, apex, 0f);
            Vector3 base_ = new(0f, bot,  0f);

            int vi = 0, ti = 0;
            void AddTri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                verts[vi] = a; norms[vi] = n; uvs[vi] = new Vector2(0.5f, 1f); tris[ti++] = vi++;
                verts[vi] = b; norms[vi] = n; uvs[vi] = new Vector2(0f, 0f);   tris[ti++] = vi++;
                verts[vi] = c; norms[vi] = n; uvs[vi] = new Vector2(1f, 0f);   tris[ti++] = vi++;
            }

            AddTri(top, e1, e0); AddTri(top, e2, e1); AddTri(top, e3, e2); AddTri(top, e0, e3);
            AddTri(e0, e1, base_); AddTri(e1, e2, base_); AddTri(e2, e3, base_); AddTri(e3, e0, base_);

            return BuildMesh("ScatterPropRock_LOD1", verts, norms, uvs, tris);
        }

        /// <summary>LOD2: tetrahedron — apex + 3-vert base. 4 tris, 12 flat-shaded verts.</summary>
        private static Mesh BuildRockMeshLOD2()
        {
            var verts = new Vector3[12];
            var norms = new Vector3[12];
            var uvs   = new Vector2[12];
            var tris  = new int[12];

            float r = 0.45f;
            float apex = 1.0f;
            Vector3 b0 = new(0f,             0f,  r);
            Vector3 b1 = new( r * 0.866f,    0f, -r * 0.5f);
            Vector3 b2 = new(-r * 0.866f,    0f, -r * 0.5f);
            Vector3 top = new(0f, apex, 0f);

            int vi = 0, ti = 0;
            void AddTri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                verts[vi] = a; norms[vi] = n; uvs[vi] = new Vector2(0.5f, 1f); tris[ti++] = vi++;
                verts[vi] = b; norms[vi] = n; uvs[vi] = new Vector2(0f, 0f);   tris[ti++] = vi++;
                verts[vi] = c; norms[vi] = n; uvs[vi] = new Vector2(1f, 0f);   tris[ti++] = vi++;
            }

            AddTri(top, b1, b0);
            AddTri(top, b2, b1);
            AddTri(top, b0, b2);
            AddTri(b0, b1, b2); // base (camera looking down sees this)

            return BuildMesh("ScatterPropRock_LOD2", verts, norms, uvs, tris);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Mesh BuildMesh(string name, Vector3[] verts, Vector3[] norms, Vector2[] uvs, int[] tris)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices  = verts;
            mesh.normals   = norms;
            mesh.uv        = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Creates the asset at <paramref name="path"/>, or copies the generated mesh's data INTO the
        /// existing asset so references already pointing at it stay valid across regenerations.
        /// </summary>
        private static Mesh SaveMeshAsset(Mesh generated, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                existing.vertices  = generated.vertices;
                existing.normals   = generated.normals;
                existing.uv        = generated.uv;
                existing.triangles = generated.triangles;
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            AssetDatabase.CreateAsset(generated, path);
            AssetDatabase.SaveAssets();
            return generated;
        }

        private static void EnsureDir(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
