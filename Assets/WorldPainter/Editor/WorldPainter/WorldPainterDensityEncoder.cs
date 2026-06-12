#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Encodes a density RenderTexture into a target density <see cref="Texture2D"/> and persists it.
    /// The target is either a legacy <see cref="DensityScatterLayer"/>'s <c>densityMap</c> or a unified
    /// <c>GrassVariant.densityMap</c> — both are plain RGBA/R textures, so the encoder is target-typed.
    ///
    /// Sits on the same throttled 0.15s pipeline as <see cref="TerrainSculptRtWriteback"/>:
    /// call <see cref="RequestAsync(Texture2D, RenderTexture)"/> during drag, <see cref="ExecuteSync(Texture2D, RenderTexture)"/> on mouse-up.
    /// </summary>
    internal sealed class WorldPainterDensityEncoder
    {
        // ── Pending state (coalesced per target; last RT wins) ────────────────

        private Texture2D?     pendingTarget;
        private RenderTexture? pendingRT;

        // ── Public API (Texture2D target) ─────────────────────────────────────

        /// <summary>Queue an async density readback into <paramref name="target"/>. Last call wins.</summary>
        public void RequestAsync(Texture2D target, RenderTexture densityRT)
        {
            this.pendingTarget = target;
            this.pendingRT     = densityRT;
        }

        /// <summary>Synchronous encode: reads RT pixels on the CPU immediately. Use on mouse-up.</summary>
        public void ExecuteSync(Texture2D target, RenderTexture densityRT)
        {
            if (target == null || densityRT == null) return;

            var prevActive = RenderTexture.active;
            RenderTexture.active = densityRT;
            var tmp = new Texture2D(densityRT.width, densityRT.height,
                TextureFormat.RFloat, mipChain: false, linear: true);
            tmp.ReadPixels(new Rect(0, 0, densityRT.width, densityRT.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prevActive;

            Color[] pixels = tmp.GetPixels();
            Object.DestroyImmediate(tmp);

            WriteCpuPixelsToTarget(target, pixels, densityRT.width, densityRT.height);
        }

        // ── Back-compat overloads (DensityScatterLayer → its DensityMap) ──────

        /// <summary>Legacy overload: routes to the layer's <see cref="DensityScatterLayer.DensityMap"/>.</summary>
        public void RequestAsync(DensityScatterLayer layer, RenderTexture densityRT)
        {
            if (layer != null && layer.DensityMap != null)
                this.RequestAsync(layer.DensityMap, densityRT);
        }

        /// <summary>Legacy overload: routes to the layer's <see cref="DensityScatterLayer.DensityMap"/>.</summary>
        public void ExecuteSync(DensityScatterLayer layer, RenderTexture densityRT)
        {
            if (layer != null && layer.DensityMap != null)
                this.ExecuteSync(layer.DensityMap, densityRT);
        }

        // ── Drain ─────────────────────────────────────────────────────────────

        /// <summary>Drain the pending async request (issue AsyncGPUReadback). Call from OnEditorUpdate.</summary>
        public void Tick()
        {
            if (this.pendingTarget == null || this.pendingRT == null) return;

            var target = this.pendingTarget;
            var rt     = this.pendingRT;
            this.pendingTarget = null;
            this.pendingRT     = null;

            if (target == null || rt == null) return;

            AsyncGPUReadback.Request(rt, 0, request =>
            {
                if (request.hasError || target == null) return;
                var raw = request.GetData<float>();
                WriteToTarget(target, raw, rt.width, rt.height);
            });
        }

        /// <summary>Cancel any queued async request (call before ExecuteSync on mouse-up).</summary>
        public void CancelPending()
        {
            this.pendingTarget = null;
            this.pendingRT     = null;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static void WriteToTarget(
            Texture2D map, Unity.Collections.NativeArray<float> raw, int rtW, int rtH)
        {
            int mapW = map.width, mapH = map.height;
            var pixels = new Color[mapW * mapH];
            for (int y = 0; y < mapH; ++y)
                for (int x = 0; x < mapW; ++x)
                {
                    int rtX = Mathf.Clamp(Mathf.RoundToInt((float)x / (mapW - 1) * (rtW - 1)), 0, rtW - 1);
                    int rtY = Mathf.Clamp(Mathf.RoundToInt((float)y / (mapH - 1) * (rtH - 1)), 0, rtH - 1);
                    float v = raw[rtY * rtW + rtX];
                    pixels[y * mapW + x] = new Color(v, v, v, 1f);
                }
            ApplyAndPersist(map, pixels);
        }

        private static void WriteCpuPixelsToTarget(
            Texture2D map, Color[] rtPixels, int rtW, int rtH)
        {
            int mapW = map.width, mapH = map.height;
            var pixels = new Color[mapW * mapH];
            for (int y = 0; y < mapH; ++y)
                for (int x = 0; x < mapW; ++x)
                {
                    int rtX = Mathf.Clamp(Mathf.RoundToInt((float)x / (mapW - 1) * (rtW - 1)), 0, rtW - 1);
                    int rtY = Mathf.Clamp(Mathf.RoundToInt((float)y / (mapH - 1) * (rtH - 1)), 0, rtH - 1);
                    float v = rtPixels[rtY * rtW + rtX].r;
                    pixels[y * mapW + x] = new Color(v, v, v, 1f);
                }
            ApplyAndPersist(map, pixels);
        }

        private static void ApplyAndPersist(Texture2D map, Color[] pixels)
        {
            map.SetPixels(pixels);
            map.Apply(false);
            EditorUtility.SetDirty(map);
        }
    }
}
