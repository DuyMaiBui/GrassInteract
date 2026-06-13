#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Encodes a density RenderTexture into a target density <see cref="Texture2D"/> and persists it.
    /// The target is a per-tile <c>GrassVariant.densityTiles</c> entry — a plain R texture.
    ///
    /// Sits on the same throttled 0.15s pipeline as <see cref="TerrainSculptRtWriteback"/>:
    /// call <see cref="RequestAsync(Texture2D, RenderTexture)"/> during drag, <see cref="ExecuteSync(Texture2D, RenderTexture)"/> on mouse-up.
    /// </summary>
    internal sealed class WorldPainterDensityEncoder
    {
        // ── Pending state (keyed by target; last RT per target wins) ─────────
        // Using a Dictionary so a single brush stamp straddling two tiles enqueues BOTH targets
        // instead of the second call overwriting the first ("last call wins" caused live-preview
        // flicker on tile seams during drag).

        private readonly Dictionary<Texture2D, RenderTexture> pendingQueue = new();

        // ── Public API (Texture2D target) ─────────────────────────────────────

        /// <summary>
        /// Queue an async density readback into <paramref name="target"/>.
        /// A later RT for the SAME target replaces the earlier one (still correct — same tile, newer paint).
        /// Different targets accumulate so seam tiles are not dropped.
        /// </summary>
        public void RequestAsync(Texture2D target, RenderTexture densityRT)
        {
            this.pendingQueue[target] = densityRT;
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

        // ── Drain ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Drain ALL pending async requests (one AsyncGPUReadback per queued target).
        /// The throttle cadence (0.15s) applies to the whole drain, not per-entry.
        /// Call from OnEditorUpdate.
        /// </summary>
        public void Tick()
        {
            if (this.pendingQueue.Count == 0) return;

            // Snapshot and clear before issuing callbacks (re-entrant safety).
            var snapshot = new Dictionary<Texture2D, RenderTexture>(this.pendingQueue);
            this.pendingQueue.Clear();

            foreach (var (target, rt) in snapshot)
            {
                if (target == null || rt == null) continue;

                // Capture locals for the closure — target/rt are loop vars.
                var capturedTarget = target;
                var capturedRt     = rt;
                AsyncGPUReadback.Request(capturedRt, 0, request =>
                {
                    if (request.hasError || capturedTarget == null) return;
                    var raw = request.GetData<float>();
                    WriteToTarget(capturedTarget, raw, capturedRt.width, capturedRt.height);
                });
            }
        }

        /// <summary>Cancel all queued async requests (call before ExecuteSync on mouse-up).</summary>
        public void CancelPending()
        {
            this.pendingQueue.Clear();
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
