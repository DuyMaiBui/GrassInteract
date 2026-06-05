#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// Generates three procedural blade meshes (LOD0 / LOD1 / LOD2) and saves them as project assets.
    /// Run via <c>Tools ▸ GrassInteract ▸ Build Blade Meshes</c>, then assign them to a ScatterLayer's `lods` array.
    ///
    /// LOD0 — cross-quad (two perpendicular tapered strips, 4 segments each → 24+24 = 48 triangles).
    ///         Visible from any horizontal angle and from above. Used for nearby blades.
    ///
    /// LOD1 — single upright quad (2 segments → 4 triangles). Simpler geometry for mid-range blades.
    ///         Adequate at distances where the cross silhouette doesn't matter.
    ///
    /// LOD2 — single flat quad (1 segment → 2 triangles). Minimal geometry for distant blades.
    ///         The indirect shader billboards this toward the camera in the vertex shader
    ///         when the _LOD2_BILLBOARD keyword is enabled on the LOD2 material.
    /// </summary>
    public static class GrassBladeMeshBuilder
    {
        private const string OUTPUT_DIR = "Assets/GrassInteract/Meshes";

        /// <summary>Unscaled blade height (metres). Mirror this into ScatterLayer.MaxBladeHeight for correct AABBs.</summary>
        public const float BLADE_HEIGHT = 1.0f;

        private const float BLADE_HALF_WIDTH = 0.06f;

        [MenuItem("Tools/GrassInteract/Build Blade Meshes")]
        public static void BuildAll()
        {
            if (!Directory.Exists(OUTPUT_DIR))
                Directory.CreateDirectory(OUTPUT_DIR);

            // LOD0: cross-quad, 4 segments per strip (24+24 = 48 tris).
            Save(BuildCrossQuadBlade(4), "GrassBlade_LOD0");

            // LOD1: single upright quad, 2 segments (4 tris).
            Save(BuildSingleQuadBlade(2), "GrassBlade_LOD1");

            // LOD2: single flat quad, 1 segment (2 tris). Billboard in VS.
            Save(BuildSingleQuadBlade(1), "GrassBlade_LOD2");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[GrassBladeMeshBuilder] Wrote LOD0 (cross-quad,48tri) / LOD1 (quad,4tri) / " +
                      $"LOD2 (quad,2tri) blade meshes to {OUTPUT_DIR}.");
        }

        // ── LOD0: cross-quad ──────────────────────────────────────────────────

        /// <summary>
        /// A cross-quad blade: two perpendicular tapered strips (XY-facing-+Z and ZY-facing-+X), each
        /// <paramref name="segments"/> quads tall. The cross shape keeps the blade visible from ALL
        /// horizontal angles and from above — a single flat strip is nearly invisible edge-on.
        /// </summary>
        private static Mesh BuildCrossQuadBlade(int segments)
        {
            int rows = segments + 1;
            int vertsPerStrip = rows * 2;
            int totalVerts = vertsPerStrip * 2;

            var vertices  = new Vector3[totalVerts];
            var normals   = new Vector3[totalVerts];
            var uv        = new Vector2[totalVerts];

            for (int r = 0; r < rows; ++r)
            {
                float t = r / (float)segments;
                float halfWidth = Mathf.Lerp(BLADE_HALF_WIDTH, 0f, t);
                float y = t * BLADE_HEIGHT;

                // Strip A — XY plane, faces +Z.
                vertices[r * 2 + 0] = new Vector3(-halfWidth, y, 0f);
                vertices[r * 2 + 1] = new Vector3(+halfWidth, y, 0f);
                normals[r * 2 + 0]  = Vector3.forward;
                normals[r * 2 + 1]  = Vector3.forward;
                uv[r * 2 + 0]       = new Vector2(0f, t);
                uv[r * 2 + 1]       = new Vector2(1f, t);

                // Strip B — ZY plane, faces +X (perpendicular to strip A).
                int b = vertsPerStrip + r * 2;
                vertices[b + 0] = new Vector3(0f, y, -halfWidth);
                vertices[b + 1] = new Vector3(0f, y, +halfWidth);
                normals[b + 0]  = Vector3.right;
                normals[b + 1]  = Vector3.right;
                uv[b + 0]       = new Vector2(0f, t);
                uv[b + 1]       = new Vector2(1f, t);
            }

            var triangles = new int[segments * 6 * 2];
            int ti = 0;
            for (int strip = 0; strip < 2; ++strip)
            {
                int stripBase = strip * vertsPerStrip;
                for (int s = 0; s < segments; ++s)
                {
                    int b = stripBase + s * 2;
                    triangles[ti++] = b + 0;
                    triangles[ti++] = b + 2;
                    triangles[ti++] = b + 1;
                    triangles[ti++] = b + 1;
                    triangles[ti++] = b + 2;
                    triangles[ti++] = b + 3;
                }
            }

            var mesh = new Mesh { name = "GrassBlade_CrossQuad" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── LOD1 + LOD2: single upright quad ─────────────────────────────────

        /// <summary>
        /// A single upright tapered quad (blade strip facing +Z), <paramref name="segments"/> quads tall.
        /// LOD1 = 2 segments (4 tris); LOD2 = 1 segment (2 tris, billboard in VS).
        /// </summary>
        private static Mesh BuildSingleQuadBlade(int segments)
        {
            int rows = segments + 1;
            int totalVerts = rows * 2;

            var vertices  = new Vector3[totalVerts];
            var normals   = new Vector3[totalVerts];
            var uv        = new Vector2[totalVerts];

            for (int r = 0; r < rows; ++r)
            {
                float t = r / (float)segments;
                float halfWidth = Mathf.Lerp(BLADE_HALF_WIDTH, 0f, t);
                float y = t * BLADE_HEIGHT;

                vertices[r * 2 + 0] = new Vector3(-halfWidth, y, 0f);
                vertices[r * 2 + 1] = new Vector3(+halfWidth, y, 0f);
                normals[r * 2 + 0]  = Vector3.forward;
                normals[r * 2 + 1]  = Vector3.forward;
                uv[r * 2 + 0]       = new Vector2(0f, t);
                uv[r * 2 + 1]       = new Vector2(1f, t);
            }

            var triangles = new int[segments * 6];
            int ti = 0;
            for (int s = 0; s < segments; ++s)
            {
                int b = s * 2;
                triangles[ti++] = b + 0;
                triangles[ti++] = b + 2;
                triangles[ti++] = b + 1;
                triangles[ti++] = b + 1;
                triangles[ti++] = b + 2;
                triangles[ti++] = b + 3;
            }

            var mesh = new Mesh { name = "GrassBlade_SingleQuad" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private static void Save(Mesh mesh, string assetName)
        {
            string path = $"{OUTPUT_DIR}/{assetName}.asset";
            Mesh? existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // Preserve the asset GUID (and any references) by overwriting in place.
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
        }
    }
}
