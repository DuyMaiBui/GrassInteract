#nullable enable
using UnityEditor;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// World-space brush decal cursor for terrain sculpt strokes.
    ///
    /// Mirrors the GrassInteract <c>ScatterBrushPreview</c> pattern:
    /// — The tool pushes brush state each frame via <see cref="Set"/>.
    /// — Actual drawing happens from <see cref="SceneView.duringSceneGui"/> during
    ///   <see cref="EventType.Repaint"/> (so DrawMeshNow renders in world space, not
    ///   in the Scene-view corner like it would from OnToolGUI).
    /// — Auto-hides when <see cref="Set"/> stops being called (freshness timeout).
    ///
    /// Geometry: a TESSELLATED radial disc, not a flat quad. Every repaint each vertex
    /// samples the terrain height (via the <see cref="HeightFn"/> the tool supplies) so
    /// the decal HUGS the surface instead of clipping through ridges / floating over dips.
    /// Falls back to the hit-point height for verts that resolve off-tile.
    ///
    /// Visual: translucent filled disc + bright rim + center dot, tinted by sculpt mode.
    /// </summary>
    [InitializeOnLoad]
    internal static class TerrainBrushPreview
    {
        /// <summary>
        /// Per-vertex terrain height query. Returns true + world Y when (wx,wz) is on a
        /// loaded tile. Supplied by the sculpt tool so the disc can conform to the surface.
        /// </summary>
        internal delegate bool HeightFn(float worldX, float worldZ, out float worldY);

        // ── Constants ─────────────────────────────────────────────────────────

        // Lift the conformed disc above the surface so it isn't occluded by the GPU-rendered
        // terrain. The rendered mesh (CDLOD morph + skirts) can dip below the CPU heightmap the
        // disc samples — worse at coarse LOD — so a flat 5 cm wasn't enough. Lift scales with
        // brush radius (bigger brush ⇒ coarser surrounding LOD ⇒ larger dip), with a floor.
        private const float  Y_OFFSET_MIN      = 0.15f; // metres (floor for small brushes)
        private const float  Y_OFFSET_FRACTION = 0.15f; // × brushRadius
        // Reject hover points farther than 1e6 m from origin (|p|² > 1e12) — anything larger is a
        // degenerate fallback-plane pick, not a real terrain hit.
        private const float  MAX_HIT_SQR   = 1e12f;
        private const int    DISC_TEX_SIZE = 64;
        private const double FRESH_SECONDS = 0.25;
        private const string DECAL_SHADER  = "GpuTerrain/BrushDecal"; // ZTest Always → draws over terrain

        // Disc tessellation — enough rings/segments to follow terrain curvature smoothly.
        private const int RINGS    = 10;
        private const int SEGMENTS = 48;

        // ── Cached resources ──────────────────────────────────────────────────

        private static Mesh?      discMesh;
        private static Material?  discMaterial;
        private static Texture2D? discTexture;
        private static bool       warnLogged;

        // Unit-circle XZ offset (range [-0.5,0.5]) per disc vertex, parallel to the mesh
        // vertex array. Reused each repaint to compute conformed world positions (no alloc).
        private static Vector2[]? unitOffsets;
        private static Vector3[]? worldVerts;

        // ── Brush state (pushed by tool) ──────────────────────────────────────

        private static Vector3   hitPoint;
        private static float     brushRadius;
        private static Color     tintColor;
        private static HeightFn? heightAt;
        private static double    lastSetTime = -1000d;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        static TerrainBrushPreview()
        {
            SceneView.duringSceneGui += OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        private static void Cleanup()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            DestroyResources();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Pushes the current brush state. Call from <c>TerrainSculptTool.OnToolGUI</c>
        /// every event the brush is hovering a surface. <paramref name="height"/> lets the
        /// disc conform to the terrain; pass null to fall back to a flat disc at the hit point.
        /// </summary>
        internal static void Set(Vector3 worldPoint, float radius, Color tint, HeightFn? height)
        {
            hitPoint    = worldPoint;
            brushRadius = radius;
            tintColor   = tint;
            heightAt    = height;
            lastSetTime = EditorApplication.timeSinceStartup;
        }

        // ── Scene draw ────────────────────────────────────────────────────────

        private static void OnSceneGui(SceneView sceneView)
        {
            if (EditorApplication.timeSinceStartup - lastSetTime > FRESH_SECONDS) return;
            if (Event.current.type != EventType.Repaint) return;

            EnsureResources();
            if (discMaterial == null || discMesh == null ||
                unitOffsets == null || worldVerts == null) return;

            // Guard against an invalid hover point (e.g. fallback-plane ray nearly parallel to
            // the plane → astronomical dist → huge worldPoint). Baking that into mesh verts would
            // trip Unity's "abnormal mesh bounds" warning. Skip the draw instead.
            if (!IsFinite(hitPoint) || hitPoint.sqrMagnitude > MAX_HIT_SQR ||
                !IsFinite(brushRadius) || brushRadius <= 0f)
                return;

            // Conform every vertex to the terrain surface (XZ from the unit disc, Y sampled).
            float diameter = brushRadius * 2f;
            float lift = Mathf.Max(Y_OFFSET_MIN, brushRadius * Y_OFFSET_FRACTION);
            for (int i = 0; i < unitOffsets.Length; ++i)
            {
                float wx = hitPoint.x + unitOffsets[i].x * diameter;
                float wz = hitPoint.z + unitOffsets[i].y * diameter;
                float wy = heightAt != null && heightAt(wx, wz, out float sampled)
                    ? sampled
                    : hitPoint.y; // off-tile (or no sampler) → flat fallback at hit height
                worldVerts[i] = new Vector3(wx, wy + lift, wz);
            }
            discMesh.SetVertices(worldVerts);
            discMesh.RecalculateBounds();

            discMaterial.mainTexture = EnsureDiscTexture();
            discMaterial.color       = tintColor;
            discMaterial.SetPass(0);
            // World positions are baked into the mesh → draw with identity transform.
            Graphics.DrawMeshNow(discMesh, Matrix4x4.identity);

            sceneView.Repaint();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        private static bool IsFinite(Vector3 v) =>
            IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);

        private static void EnsureResources()
        {
            discMesh ??= CreateUnitDisc(RINGS, SEGMENTS);
            if (discMaterial == null && !warnLogged)
                discMaterial = CreateMaterial();
        }

        /// <summary>
        /// Builds a tessellated radial disc in the XZ plane (unit circle, radius 0.5,
        /// Y=0). UV.xy = (0.5 + x, 0.5 + z) so the disc texture (fill/rim/center-dot)
        /// maps the same as the old quad. Populates <see cref="unitOffsets"/> +
        /// <see cref="worldVerts"/> so the repaint loop can conform vertices to terrain.
        /// </summary>
        internal static Mesh CreateUnitDisc(int rings, int segments)
        {
            rings    = Mathf.Max(1, rings);
            segments = Mathf.Max(3, segments);

            int vertCount = 1 + rings * segments; // center + ring vertices
            var positions = new Vector3[vertCount];
            var uvs       = new Vector2[vertCount];
            var offsets   = new Vector2[vertCount];

            // Center vertex.
            positions[0] = Vector3.zero;
            uvs[0]       = new Vector2(0.5f, 0.5f);
            offsets[0]   = Vector2.zero;

            int v = 1;
            for (int r = 1; r <= rings; ++r)
            {
                float radius = 0.5f * (r / (float)rings);
                for (int s = 0; s < segments; ++s)
                {
                    float ang = (s / (float)segments) * Mathf.PI * 2f;
                    float x = Mathf.Cos(ang) * radius;
                    float z = Mathf.Sin(ang) * radius;
                    positions[v] = new Vector3(x, 0f, z);
                    uvs[v]       = new Vector2(0.5f + x, 0.5f + z);
                    offsets[v]   = new Vector2(x, z);
                    ++v;
                }
            }

            var tris = new System.Collections.Generic.List<int>(rings * segments * 6);
            // Inner fan: center → ring 1.
            for (int s = 0; s < segments; ++s)
            {
                int a = 1 + s;
                int b = 1 + (s + 1) % segments;
                tris.Add(0); tris.Add(a); tris.Add(b);
            }
            // Ring strips: ring r → ring r+1.
            for (int r = 1; r < rings; ++r)
            {
                int inner = 1 + (r - 1) * segments;
                int outer = 1 + r * segments;
                for (int s = 0; s < segments; ++s)
                {
                    int sn  = (s + 1) % segments;
                    int i0  = inner + s;
                    int i1  = inner + sn;
                    int o0  = outer + s;
                    int o1  = outer + sn;
                    tris.Add(i0); tris.Add(o0); tris.Add(o1);
                    tris.Add(i0); tris.Add(o1); tris.Add(i1);
                }
            }

            var mesh = new Mesh { name = "TerrainBrushDecalDisc", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            unitOffsets = offsets;
            worldVerts  = new Vector3[vertCount];
            return mesh;
        }

        private static Material? CreateMaterial()
        {
            Shader? shader = Shader.Find(DECAL_SHADER);
            if (shader == null)
            {
                warnLogged = true;
                Debug.LogWarning($"[TerrainBrushPreview] Shader '{DECAL_SHADER}' not found.");
                return null;
            }
            // Cull/ZTest/Blend are baked into the shader pass (ZTest Always → draws over terrain).
            return new Material(shader) { name = "TerrainBrushDecalMat",
                                          hideFlags = HideFlags.HideAndDontSave };
        }

        private static Texture2D EnsureDiscTexture()
        {
            if (discTexture != null) return discTexture;

            const int N = DISC_TEX_SIZE;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "TerrainBrushDisc", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[N * N];
            for (int y = 0; y < N; ++y)
            {
                for (int x = 0; x < N; ++x)
                {
                    float u    = (x + 0.5f) / N - 0.5f;
                    float v    = (y + 0.5f) / N - 0.5f;
                    float dist = Mathf.Sqrt(u * u + v * v) * 2f;
                    // Translucent fill in inner 80%; sharp bright rim at 80-100%.
                    float fill = Mathf.Clamp01(1f - Mathf.SmoothStep(0.6f, 0.8f, dist)) * 0.3f;
                    float rim  = Mathf.Clamp01(Mathf.SmoothStep(0.78f, 0.82f, dist) -
                                               Mathf.SmoothStep(0.96f, 1.0f, dist));
                    float dot  = Mathf.Clamp01(1f - dist / 0.08f);
                    float alpha = Mathf.Clamp01(fill + rim + dot * 0.8f);
                    px[y * N + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            discTexture = tex;
            return tex;
        }

        private static void DestroyResources()
        {
            if (discMesh      != null) { Object.DestroyImmediate(discMesh);      discMesh      = null; }
            if (discMaterial  != null) { Object.DestroyImmediate(discMaterial);  discMaterial  = null; }
            if (discTexture   != null) { Object.DestroyImmediate(discTexture);   discTexture   = null; }
            unitOffsets = null;
            worldVerts  = null;
            warnLogged  = false;
        }
    }
}
