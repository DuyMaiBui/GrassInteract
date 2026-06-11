#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Cached <see cref="PreviewRenderUtility"/> thumbnails for scatter-layer LOD meshes.
    /// Renders ONCE per (mesh, material) pair and caches the result; invalidates only when
    /// the mesh or material reference changes. Never re-renders on idle inspector repaint.
    ///
    /// Design §6 perf: zero <c>PreviewRenderUtility.Render</c> calls on idle repaint.
    /// Both <see cref="WorldPainterLodPreviewPanel"/> and <see cref="WorldPainterLodBandRuler"/>
    /// use this cache.
    /// </summary>
    internal sealed class WorldPainterPreviewCache
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float DEFAULT_YAW   = 130f;
        private const float DEFAULT_PITCH = -18f;
        private const float DEFAULT_DIST  = 2.5f;

        // ── Cache entry ───────────────────────────────────────────────────────

        private sealed class Entry
        {
            public Mesh?     mesh;
            public Material? mat;
            public Texture2D? thumb;
            public PreviewRenderUtility? pru;
        }

        // ── State ─────────────────────────────────────────────────────────────

        private readonly Dictionary<int, Entry> cache = new();
        private Material? defaultMat;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public WorldPainterPreviewCache()
        {
            AssemblyReloadEvents.beforeAssemblyReload += this.Cleanup;
        }

        public void Cleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= this.Cleanup;
            foreach (var e in this.cache.Values)
            {
                e.pru?.Cleanup();
                e.pru = null;
                if (e.thumb != null)
                    Object.DestroyImmediate(e.thumb);
            }
            this.cache.Clear();
            DestroyIfSet(ref this.defaultMat);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a cached thumbnail for <paramref name="mesh"/> at the given pixel size.
        /// If the cached entry is stale (mesh or material changed) it is re-rendered.
        /// Returns null when <paramref name="mesh"/> is null.
        /// </summary>
        public Texture2D? GetOrRender(int key, Mesh? mesh, Material? mat, int px = 24)
        {
            if (mesh == null) return null;

            mat ??= this.EnsureDefaultMat();

            if (!this.cache.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                this.cache[key] = entry;
            }

            bool dirty = entry.mesh != mesh || entry.mat != mat || entry.thumb == null;
            if (!dirty) return entry.thumb;

            // Re-render into a new Texture2D.
            entry.mesh = mesh;
            entry.mat  = mat;
            entry.pru ??= new PreviewRenderUtility();

            var tex = this.RenderThumbnail(entry.pru, mesh, mat, px);

            if (entry.thumb != null)
                Object.DestroyImmediate(entry.thumb);
            entry.thumb = tex;
            return tex;
        }

        /// <summary>Invalidates all cached entries (forces re-render on next request).</summary>
        public void InvalidateAll()
        {
            foreach (var e in this.cache.Values)
            {
                e.mesh = null;
                e.mat  = null;
            }
        }

        // ── Internal render ───────────────────────────────────────────────────

        private Texture2D RenderThumbnail(
            PreviewRenderUtility pru, Mesh mesh, Material mat, int px)
        {
            var rect = new Rect(0, 0, px, px);
            pru.BeginPreview(rect, GUIStyle.none);

            // Camera orbit.
            Quaternion rot = Quaternion.Euler(DEFAULT_PITCH, DEFAULT_YAW, 0f);
            Bounds b = mesh.bounds;
            float radius = Mathf.Max(0.05f, b.extents.magnitude);
            float dist   = DEFAULT_DIST * radius * 2f;
            pru.camera.transform.position = b.center + rot * new Vector3(0f, 0f, -dist);
            pru.camera.transform.rotation = rot;
            pru.camera.nearClipPlane = 0.01f;
            pru.camera.farClipPlane  = 100f;
            pru.camera.fieldOfView   = 30f;

            if (pru.lights.Length > 0)
            {
                pru.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                pru.lights[0].intensity = 1.1f;
            }
            pru.ambientColor = new Color(0.30f, 0.30f, 0.32f, 1f);

            pru.DrawMesh(mesh, Matrix4x4.identity, mat, 0);
            pru.camera.Render();
            Texture src = pru.EndPreview();

            // Blit to owned Texture2D so we can cache it independently of the PRU.
            var result = new Texture2D(px, px, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            RenderTexture prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(px, px, 0);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            result.ReadPixels(new Rect(0, 0, px, px), 0, 0);
            result.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Material EnsureDefaultMat()
        {
            if (this.defaultMat != null) return this.defaultMat;
            Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")!;
            this.defaultMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            SetColorAny(this.defaultMat, new Color(0.62f, 0.62f, 0.66f));
            return this.defaultMat;
        }

        private static void SetColorAny(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
        }

        private static void DestroyIfSet(ref Material? m)
        {
            if (m == null) return;
            Object.DestroyImmediate(m);
            m = null;
        }
    }
}
