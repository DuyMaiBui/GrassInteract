#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Phase 2b: encoder for per-tile alphamap textures (the TerrainPalette weight maps).
    ///
    /// Direct mirror of <see cref="WorldPainterDensityEncoder"/>: takes a brushed RGBA
    /// RenderTexture and writes the pixels straight into the bound target
    /// <see cref="Texture2D"/> via <c>SetPixels32 + Apply + SetDirty</c>. The terrain
    /// shader was already bound to that Texture2D in
    /// <see cref="GpuTerrainEngine.BindPalette"/>, so the visual change is instant —
    /// no byte[] indirection like the legacy splat path.
    ///
    /// Lifetime: <see cref="RequestAsync"/> during drag (throttled), <see cref="Tick"/>
    /// drains pending readbacks, <see cref="ExecuteSync"/> on mouse-up for the final commit.
    /// </summary>
    internal sealed class WorldPainterAlphamapEncoder
    {
        // Last RT per target wins (drag throttle, mirrors density encoder).
        private readonly Dictionary<Texture2D, RenderTexture> pendingQueue = new();

        // ── Public API ────────────────────────────────────────────────────────

        public void RequestAsync(Texture2D target, RenderTexture alphamapRT)
        {
            if (target == null || alphamapRT == null) return;
            this.pendingQueue[target] = alphamapRT;
        }

        public void ExecuteSync(Texture2D target, RenderTexture alphamapRT)
        {
            if (target == null || alphamapRT == null) return;

            var prevActive = RenderTexture.active;
            RenderTexture.active = alphamapRT;
            var tmp = new Texture2D(alphamapRT.width, alphamapRT.height,
                TextureFormat.RGBA32, mipChain: false, linear: true);
            tmp.ReadPixels(new Rect(0, 0, alphamapRT.width, alphamapRT.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prevActive;

            Color32[] pixels = tmp.GetPixels32();
            Object.DestroyImmediate(tmp);

            WriteResampled(target, pixels, alphamapRT.width, alphamapRT.height);
        }

        public void Tick()
        {
            if (this.pendingQueue.Count == 0) return;

            var snapshot = new Dictionary<Texture2D, RenderTexture>(this.pendingQueue);
            this.pendingQueue.Clear();

            foreach (var (target, rt) in snapshot)
            {
                if (target == null || rt == null) continue;

                var capturedTarget = target;
                var capturedRt     = rt;
                AsyncGPUReadback.Request(capturedRt, 0, request =>
                {
                    if (request.hasError || capturedTarget == null) return;
                    var raw = request.GetData<Color32>();
                    WriteResampledFromNative(capturedTarget, raw, capturedRt.width, capturedRt.height);
                });
            }
        }

        public void CancelPending() => this.pendingQueue.Clear();

        // ── Private helpers ───────────────────────────────────────────────────

        private static void WriteResampledFromNative(
            Texture2D map, Unity.Collections.NativeArray<Color32> raw, int rtW, int rtH)
        {
            int mapW = map.width, mapH = map.height;
            var pixels = new Color32[mapW * mapH];
            for (int y = 0; y < mapH; ++y)
            {
                int rtY = Mathf.Clamp(Mathf.RoundToInt((float)y / (mapH - 1) * (rtH - 1)), 0, rtH - 1);
                for (int x = 0; x < mapW; ++x)
                {
                    int rtX = Mathf.Clamp(Mathf.RoundToInt((float)x / (mapW - 1) * (rtW - 1)), 0, rtW - 1);
                    pixels[y * mapW + x] = raw[rtY * rtW + rtX];
                }
            }
            ApplyAndPersist(map, pixels);
        }

        private static void WriteResampled(Texture2D map, Color32[] rtPixels, int rtW, int rtH)
        {
            int mapW = map.width, mapH = map.height;
            var pixels = new Color32[mapW * mapH];
            for (int y = 0; y < mapH; ++y)
            {
                int rtY = Mathf.Clamp(Mathf.RoundToInt((float)y / (mapH - 1) * (rtH - 1)), 0, rtH - 1);
                for (int x = 0; x < mapW; ++x)
                {
                    int rtX = Mathf.Clamp(Mathf.RoundToInt((float)x / (mapW - 1) * (rtW - 1)), 0, rtW - 1);
                    pixels[y * mapW + x] = rtPixels[rtY * rtW + rtX];
                }
            }
            ApplyAndPersist(map, pixels);
        }

        private static void ApplyAndPersist(Texture2D map, Color32[] pixels)
        {
            map.SetPixels32(pixels);
            map.Apply(updateMipmaps: false);
            EditorUtility.SetDirty(map);
        }
    }
}
