#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Procedural grass-blade cluster mesh generator: N crossed quads share a single horizontal
    /// pivot (centered on X/Z, base at Y=0), each rotated 60° around Y from the previous so the
    /// cluster reads as volumetric grass from any horizontal angle (3 quads → "6-pointed star"
    /// silhouette from above).
    ///
    /// Each quad is vertically subdivided into N segments (matches the demo blade asset:
    /// LOD0=4 segs / 16 tris, LOD1=2 segs / 4 tris, LOD2=1 seg / 2 tris). The extra rows let
    /// per-vertex deform (wind / interactor lean) bend the blade smoothly instead of pivoting
    /// the whole quad rigidly about the base.
    ///
    /// Square aspect (width = height) by default so a custom <c>_BaseMap</c> grass texture
    /// (e.g. a 256×256 blade card) samples without horizontal/vertical stretching. UV spans the
    /// full (0,0)→(1,1) range per quad: <c>uv.x</c> = left→right across the blade, <c>uv.y</c>
    /// = base→tip (drives the BaseColor→TipColor gradient in the grass shader's heightT).
    ///
    /// Triangles are written in both windings (front + back) per segment so the cluster renders
    /// correctly under any cull mode without doubling vertex count.
    ///
    /// LOD convention: LOD0 = 3 quads + 4 segments, LOD1 = 2 quads + 2 segments, LOD2 = 1 quad
    /// + 1 segment. Each step drops one square AND halves vertical detail (mirrors the demo
    /// blade asset's LOD ladder).
    /// </summary>
    public static class GrassBladeClusterMesh
    {
        // Square by default so a BaseMap (e.g. an authored grass-card texture) renders 1:1.
        private const float DEFAULT_SIZE = 0.25f;

        /// <summary>
        /// Build a crossed-quad grass cluster mesh.
        /// </summary>
        /// <param name="squareCount">1, 2, or 3 quads — clamped to that range.</param>
        /// <param name="verticalSegments">Vertical subdivisions per quad (≥1). More = smoother bend.</param>
        /// <param name="width">Horizontal blade width in metres (full span, centered on pivot).</param>
        /// <param name="height">Vertical blade height in metres (base at Y=0, tip at Y=height).</param>
        public static Mesh Build(
            int squareCount,
            int verticalSegments = 4,
            float width  = DEFAULT_SIZE,
            float height = DEFAULT_SIZE)
        {
            int q  = Mathf.Clamp(squareCount, 1, 3);
            int s  = Mathf.Max(1, verticalSegments);
            int rows = s + 1;                 // (segments + 1) rows of verts per quad
            int vertsPerQuad = rows * 2;      // 2 columns (left + right) per row
            // 2 tris × 3 indices × 2 windings (front + back) per segment.
            int trisPerQuad  = s * 12;

            float halfW = width * 0.5f;

            var verts     = new Vector3[q * vertsPerQuad];
            var uvs       = new Vector2[q * vertsPerQuad];
            var normals   = new Vector3[q * vertsPerQuad];
            var triangles = new int[q * trisPerQuad];

            for (int qi = 0; qi < q; ++qi)
            {
                // Quad orientation: rotate "width axis" 60° per quad around Y. The plane contains
                // the width axis (horizontal) and the up axis (vertical). Quad 0 lies along X.
                float angleRad = qi * 60f * Mathf.Deg2Rad;
                float cos      = Mathf.Cos(angleRad);
                float sin      = Mathf.Sin(angleRad);

                int vBase = qi * vertsPerQuad;
                int tBase = qi * trisPerQuad;

                // Emit (segments+1) rows of 2 verts each, stacked from base (y=0) to tip (y=height).
                for (int r = 0; r < rows; ++r)
                {
                    float t  = (float)r / s;          // 0 at base, 1 at tip
                    float y  = height * t;
                    int   vL = vBase + r * 2 + 0;     // left column
                    int   vR = vBase + r * 2 + 1;     // right column

                    verts[vL] = new Vector3(-halfW * cos, y, -halfW * sin);
                    verts[vR] = new Vector3( halfW * cos, y,  halfW * sin);

                    // Full UV per quad — _BaseMap samples once across the blade (square aspect).
                    uvs[vL] = new Vector2(0f, t);
                    uvs[vR] = new Vector2(1f, t);

                    // Flat up-facing normals — keeps lighting consistent across rotated cards
                    // (matches the demo GrassBlade asset convention).
                    normals[vL] = Vector3.up;
                    normals[vR] = Vector3.up;
                }

                // Emit 2 tris per segment, both windings (works under any shader cull mode).
                for (int seg = 0; seg < s; ++seg)
                {
                    int bl = vBase + seg * 2 + 0;       // bottom-left   of this segment
                    int br = vBase + seg * 2 + 1;       // bottom-right
                    int tl = vBase + (seg + 1) * 2 + 0; // top-left
                    int tr = vBase + (seg + 1) * 2 + 1; // top-right
                    int ti = tBase + seg * 12;

                    // Front winding.
                    triangles[ti + 0] = bl; triangles[ti + 1] = tl; triangles[ti + 2] = br;
                    triangles[ti + 3] = tl; triangles[ti + 4] = tr; triangles[ti + 5] = br;
                    // Back winding (visible from the opposite side).
                    triangles[ti + 6] = bl; triangles[ti + 7] = br; triangles[ti + 8] = tl;
                    triangles[ti + 9] = br; triangles[ti +10] = tr; triangles[ti +11] = tl;
                }
            }

            var mesh = new Mesh { name = $"GrassBladeCluster_x{q}_seg{s}" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── Editor authoring: save 3-LOD set as project assets ──────────────────

        private const string MENU_PATH = "Tools/WorldPainter/Generate Grass Blade Cluster (3 LODs)";

        [MenuItem(MENU_PATH)]
        private static void GenerateClusterLodSet()
        {
            string folder = EditorUtility.SaveFolderPanel(
                "Save Grass Blade Cluster LODs",
                "Assets/WorldPainter",
                "Meshes");
            if (string.IsNullOrEmpty(folder)) return;

            // Convert absolute path to "Assets/..." project-relative form.
            string projRoot = Application.dataPath.Substring(
                0, Application.dataPath.Length - "Assets".Length);
            if (!folder.StartsWith(projRoot))
            {
                EditorUtility.DisplayDialog(
                    "Invalid folder",
                    "Pick a folder inside the project's Assets/ directory.",
                    "OK");
                return;
            }
            string relFolder = folder.Substring(projRoot.Length).Replace('\\', '/');

            // LOD ladder mirrors the demo GrassBlade asset:
            //   LOD0: 3 squares × 4 vertical segments (highest detail, smooth bend)
            //   LOD1: 2 squares × 2 segments         (mid)
            //   LOD2: 1 square  × 1 segment          (single quad, lowest cost)
            SaveClusterMesh(relFolder, "GrassBladeCluster_LOD0", squareCount: 3, verticalSegments: 4);
            SaveClusterMesh(relFolder, "GrassBladeCluster_LOD1", squareCount: 2, verticalSegments: 2);
            SaveClusterMesh(relFolder, "GrassBladeCluster_LOD2", squareCount: 1, verticalSegments: 1);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void SaveClusterMesh(string folder, string baseName, int squareCount, int verticalSegments)
        {
            Mesh mesh = Build(squareCount, verticalSegments);
            mesh.name = baseName;
            string path = Path.Combine(folder, baseName + ".asset").Replace('\\', '/');
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(path));
        }
    }
}
