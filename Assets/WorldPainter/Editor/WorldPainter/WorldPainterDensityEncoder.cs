#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Encodes a density RenderTexture into the bytes of a <see cref="DensityScatterLayer"/>'s
    /// <c>densityMap</c> (<see cref="Texture2D"/>) and persists the asset.
    ///
    /// Sits on the same throttled 0.15s pipeline as <see cref="TerrainSculptRtWriteback"/>:
    /// call <see cref="RequestAsync"/> during drag, <see cref="ExecuteSync"/> on mouse-up.
    ///
    /// The encoder is a separate class (not embedded in TerrainSculptRtWriteback) so the
    /// density payload remains opt-in — tiles without a density layer pay zero cost.
    /// </summary>
    internal sealed class WorldPainterDensityEncoder
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int SMOOTH_KERNEL_HALF = 3;

        // ── Pending state (coalesced per layer, mirrors TerrainSculptRtWriteback pattern) ──

        private DensityScatterLayer? pendingLayer;
        private RenderTexture?       pendingRT;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Queue an async density readback. Coalesces repeated calls; last RT wins.
        /// Call <see cref="Tick"/> from EditorApplication.update to drain.
        /// </summary>
        public void RequestAsync(DensityScatterLayer layer, RenderTexture densityRT)
        {
            this.pendingLayer = layer;
            this.pendingRT    = densityRT;
        }

        /// <summary>
        /// Drain the pending async request (issue AsyncGPUReadback).
        /// Mirrors TerrainSculptRtWriteback.Tick — call from the same OnEditorUpdate hook.
        /// </summary>
        public void Tick()
        {
            if (this.pendingLayer == null || this.pendingRT == null) return;

            var layer = this.pendingLayer;
            var rt    = this.pendingRT;
            this.pendingLayer = null;
            this.pendingRT    = null;

            if (layer == null || rt == null) return;

            AsyncGPUReadback.Request(rt, 0, request =>
            {
                if (request.hasError || layer == null) return;
                var raw = request.GetData<float>();
                WriteToLayer(layer, raw, rt.width, rt.height);
            });
        }

        /// <summary>
        /// Synchronous encode: reads RT pixels on the CPU immediately.
        /// Use on mouse-up or for forced saves — slower than async.
        /// </summary>
        public void ExecuteSync(DensityScatterLayer layer, RenderTexture densityRT)
        {
            if (layer == null) return;
            if (densityRT == null) return;

            var prevActive = RenderTexture.active;
            RenderTexture.active = densityRT;
            var tmp = new Texture2D(densityRT.width, densityRT.height,
                TextureFormat.RFloat, mipChain: false, linear: true);
            tmp.ReadPixels(new Rect(0, 0, densityRT.width, densityRT.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prevActive;

            Color[] pixels = tmp.GetPixels();
            Object.DestroyImmediate(tmp);

            WriteCpuPixelsToLayer(layer, pixels, densityRT.width, densityRT.height);
        }

        /// <summary>Cancel any queued async request (call before ExecuteSync on mouse-up).</summary>
        public void CancelPending()
        {
            this.pendingLayer = null;
            this.pendingRT    = null;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static void WriteToLayer(
            DensityScatterLayer layer,
            Unity.Collections.NativeArray<float> raw,
            int rtW, int rtH)
        {
            Texture2D? map = layer.DensityMap;
            if (map == null) return;

            int mapW = map.width;
            int mapH = map.height;

            var pixels = new Color[mapW * mapH];
            for (int y = 0; y < mapH; ++y)
            {
                for (int x = 0; x < mapW; ++x)
                {
                    int rtX = Mathf.Clamp(Mathf.RoundToInt((float)x / (mapW - 1) * (rtW - 1)), 0, rtW - 1);
                    int rtY = Mathf.Clamp(Mathf.RoundToInt((float)y / (mapH - 1) * (rtH - 1)), 0, rtH - 1);
                    float v = raw[rtY * rtW + rtX];
                    pixels[y * mapW + x] = new Color(v, v, v, 1f);
                }
            }

            ApplyAndPersist(layer, map, pixels);
        }

        private static void WriteCpuPixelsToLayer(
            DensityScatterLayer layer, Color[] rtPixels, int rtW, int rtH)
        {
            Texture2D? map = layer.DensityMap;
            if (map == null) return;

            int mapW = map.width;
            int mapH = map.height;

            var pixels = new Color[mapW * mapH];
            for (int y = 0; y < mapH; ++y)
            {
                for (int x = 0; x < mapW; ++x)
                {
                    int rtX = Mathf.Clamp(Mathf.RoundToInt((float)x / (mapW - 1) * (rtW - 1)), 0, rtW - 1);
                    int rtY = Mathf.Clamp(Mathf.RoundToInt((float)y / (mapH - 1) * (rtH - 1)), 0, rtH - 1);
                    float v = rtPixels[rtY * rtW + rtX].r;
                    pixels[y * mapW + x] = new Color(v, v, v, 1f);
                }
            }

            ApplyAndPersist(layer, map, pixels);
        }

        private static void ApplyAndPersist(
            DensityScatterLayer layer, Texture2D map, Color[] pixels)
        {
            map.SetPixels(pixels);
            map.Apply(false);
            EditorUtility.SetDirty(map);
            EditorUtility.SetDirty(layer);
            Debug.Log($"[WorldPainterDensityEncoder] Density written to layer '{layer.name}'.");
        }
    }
}
