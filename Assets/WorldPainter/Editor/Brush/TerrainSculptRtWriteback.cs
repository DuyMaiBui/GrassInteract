#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WorldPainter.Editor
{
    /// <summary>
    /// Reads back the per-tile working RenderTextures (height RT + splat RT) to CPU,
    /// encodes them into the Phase 0 byte formats (R16 height + RGBA32 splat),
    /// writes them into the <see cref="TerrainTileAsset"/>, and triggers a GPU re-upload.
    ///
    /// Height encode uses <see cref="TerrainHeightFormat.EncodeHeight"/> (SSOT) so the
    /// saved asset decodes back to the same value that was displayed — no preview/save drift.
    ///
    /// Async path: call <see cref="RequestAsync"/> at stroke END (mouse-up).
    /// Multiple tiles are queued by tile key; <see cref="Tick"/> drains ALL entries so
    /// every tile touched by a cross-tile stroke persists independently.
    ///
    /// Synchronous path: call <see cref="ExecuteSync"/> for tests or forced saves.
    /// NEVER call per-drag-sample to avoid editor stalls.
    /// </summary>
    public sealed class TerrainSculptRtWriteback : IDisposable
    {
        // ── Async pending queue (keyed by tile — coalesces repeated drag requests) ──

        private sealed class PendingReadback
        {
            public TerrainTileAsset       Tile;
            public TerrainTileGpuResources Gpu;
            public RenderTexture          HeightRT;
            public RenderTexture          SplatRT;

            public PendingReadback(TerrainTileAsset tile, TerrainTileGpuResources gpu,
                RenderTexture heightRT, RenderTexture splatRT)
            {
                this.Tile     = tile;
                this.Gpu      = gpu;
                this.HeightRT = heightRT;
                this.SplatRT  = splatRT;
            }
        }

        private readonly Dictionary<TerrainTileAsset, PendingReadback> pendingMap =
            new Dictionary<TerrainTileAsset, PendingReadback>();

        // ── Async public API ──────────────────────────────────────────────────

        /// <summary>
        /// Queue an async GPU→CPU readback for a tile.
        /// Repeated calls for the same tile coalesce to the latest RTs (throttled drag).
        /// Call <see cref="Tick"/> each editor update to drain the queue.
        /// </summary>
        public void RequestAsync(
            TerrainTileAsset tile,
            TerrainTileGpuResources gpu,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            if (tile == null)    throw new ArgumentNullException(nameof(tile));
            if (gpu == null)     throw new ArgumentNullException(nameof(gpu));
            if (heightRT == null) throw new ArgumentNullException(nameof(heightRT));
            if (splatRT == null)  throw new ArgumentNullException(nameof(splatRT));

            // Upsert — latest RTs win (coalesces repeated drag requests for the same tile).
            this.pendingMap[tile] = new PendingReadback(tile, gpu, heightRT, splatRT);
        }

        /// <summary>
        /// Pump pending async requests. Call from EditorApplication.update or editor window OnGUI.
        /// Drains ALL queued tiles so every tile in a cross-tile stroke persists independently.
        /// </summary>
        public void Tick()
        {
            if (this.pendingMap.Count == 0) return;

            // Snapshot + clear first so new RequestAsync calls during callbacks queue correctly.
            var snapshot = new PendingReadback[this.pendingMap.Count];
            this.pendingMap.Values.CopyTo(snapshot, 0);
            this.pendingMap.Clear();

            foreach (var entry in snapshot)
                this.IssueTileReadback(entry);
        }

        // ── Per-tile async readback chain ─────────────────────────────────────

        private void IssueTileReadback(PendingReadback entry)
        {
            // Capture ALL fields as locals — closures must not reference instance fields
            // or the entry object (which may be reused/overwritten by a concurrent request).
            var tile     = entry.Tile;
            var gpu      = entry.Gpu;
            var heightRT = entry.HeightRT;
            var splatRT  = entry.SplatRT;

            // Unity-null guard: destroyed objects compare == null via operator overload.
            if (tile == null || gpu == null || heightRT == null || splatRT == null) return;

            AsyncGPUReadback.Request(heightRT, 0, request =>
            {
                if (request.hasError)
                {
                    Debug.LogError("[TerrainSculptRtWriteback] AsyncGPUReadback error on height RT " +
                        $"for tile {tile.tileCoord}.");
                    return;
                }
                // Guard again inside callback — domain reload or scene change may have
                // destroyed the tile/gpu between request dispatch and callback.
                if (tile == null || gpu == null || splatRT == null) return;

                var heightRaw = request.GetData<float>();
                this.WriteHeightToAsset(tile, heightRaw);

                // Chain splat readback with its own captured locals.
                AsyncGPUReadback.Request(splatRT, 0, splatRequest =>
                {
                    if (splatRequest.hasError)
                    {
                        Debug.LogError("[TerrainSculptRtWriteback] AsyncGPUReadback error on splat RT " +
                            $"for tile {tile.tileCoord}.");
                        return;
                    }
                    if (tile == null || gpu == null) return;

                    var splatRaw = splatRequest.GetData<Color>();
                    this.WriteSplatToAsset(tile, splatRaw);
                    this.FinalizeWriteback(tile, gpu);
                });
            });
        }

        // ── Synchronous path (for tests and forced saves) ──────────────────────

        /// <summary>
        /// Synchronous writeback: reads RT pixels on the CPU immediately.
        /// Use in tests or for an explicit "Save All" action. Slower than async.
        /// </summary>
        public void ExecuteSync(
            TerrainTileAsset tile,
            TerrainTileGpuResources gpu,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            if (tile == null)    throw new ArgumentNullException(nameof(tile));
            if (gpu == null)     throw new ArgumentNullException(nameof(gpu));
            if (heightRT == null) throw new ArgumentNullException(nameof(heightRT));
            if (splatRT == null)  throw new ArgumentNullException(nameof(splatRT));

            // Read height RT pixels synchronously.
            var prevActive = RenderTexture.active;
            RenderTexture.active = heightRT;
            var tmpHeight = new Texture2D(heightRT.width, heightRT.height,
                TextureFormat.RFloat, mipChain: false, linear: true);
            tmpHeight.ReadPixels(new Rect(0, 0, heightRT.width, heightRT.height), 0, 0);
            tmpHeight.Apply();
            RenderTexture.active = prevActive;

            // Resize heightData array if needed.
            int heightRes = tile.heightRes;
            int expectedBytes = heightRes * heightRes * TerrainHeightFormat.BYTES_PER_SAMPLE;
            if (tile.heightData == null || tile.heightData.Length != expectedBytes)
                tile.heightData = new byte[expectedBytes];

            var pixels = tmpHeight.GetPixels();
            int rtRes = heightRT.width;

            for (int y = 0; y < heightRes; ++y)
            {
                for (int x = 0; x < heightRes; ++x)
                {
                    // Sample the RT at the proportional location if resolutions differ.
                    int rtX = Mathf.Clamp(Mathf.RoundToInt((float)x / (heightRes - 1) * (rtRes - 1)), 0, rtRes - 1);
                    int rtY = Mathf.Clamp(Mathf.RoundToInt((float)y / (heightRes - 1) * (rtRes - 1)), 0, rtRes - 1);
                    float normalized = pixels[rtY * rtRes + rtX].r;
                    float worldY = TerrainHeightFormat.DecodeNormalized(normalized, tile.minHeight, tile.maxHeight);
                    ushort raw = TerrainHeightFormat.EncodeHeight(worldY, tile.minHeight, tile.maxHeight);
                    TerrainHeightFormat.WriteRaw(tile.heightData, y * heightRes + x, raw);
                }
            }
            UnityEngine.Object.DestroyImmediate(tmpHeight);

            // Read splat RT.
            RenderTexture.active = splatRT;
            var tmpSplat = new Texture2D(splatRT.width, splatRT.height,
                TextureFormat.RGBA32, mipChain: false, linear: true);
            tmpSplat.ReadPixels(new Rect(0, 0, splatRT.width, splatRT.height), 0, 0);
            tmpSplat.Apply();
            RenderTexture.active = prevActive;

            int splatRes = tile.splatRes;
            int expectedSplat = splatRes * splatRes * 4;
            if (tile.splatData == null || tile.splatData.Length != expectedSplat)
                tile.splatData = new byte[expectedSplat];

            var splatPx = tmpSplat.GetPixels32();
            int srtRes  = splatRT.width;
            for (int y = 0; y < splatRes; ++y)
            {
                for (int x = 0; x < splatRes; ++x)
                {
                    int sx = Mathf.Clamp(Mathf.RoundToInt((float)x / (splatRes - 1) * (srtRes - 1)), 0, srtRes - 1);
                    int sy = Mathf.Clamp(Mathf.RoundToInt((float)y / (splatRes - 1) * (srtRes - 1)), 0, srtRes - 1);
                    var c32 = splatPx[sy * srtRes + sx];
                    int baseIdx = (y * splatRes + x) * 4;
                    tile.splatData[baseIdx]     = c32.r;
                    tile.splatData[baseIdx + 1] = c32.g;
                    tile.splatData[baseIdx + 2] = c32.b;
                    tile.splatData[baseIdx + 3] = c32.a;
                }
            }
            UnityEngine.Object.DestroyImmediate(tmpSplat);

            this.FinalizeWriteback(tile, gpu);
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        /// <summary>
        /// Discard all queued async readbacks without finalizing them.
        /// Call before mouse-up ExecuteSync so stale throttled-drag entries
        /// (queued while RTs were alive) don't fire after RTs are destroyed.
        /// </summary>
        public void CancelPending() => this.pendingMap.Clear();

        public void Dispose()
        {
            this.pendingMap.Clear();
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void WriteHeightToAsset(TerrainTileAsset tile,
            Unity.Collections.NativeArray<float> heightRaw)
        {
            int heightRes = tile.heightRes;
            int expectedBytes = heightRes * heightRes * TerrainHeightFormat.BYTES_PER_SAMPLE;
            if (tile.heightData == null || tile.heightData.Length != expectedBytes)
                tile.heightData = new byte[expectedBytes];

            int rtRes = (int)Mathf.Sqrt(heightRaw.Length);
            for (int y = 0; y < heightRes; ++y)
            {
                for (int x = 0; x < heightRes; ++x)
                {
                    int rtX = Mathf.Clamp(Mathf.RoundToInt((float)x / (heightRes - 1) * (rtRes - 1)), 0, rtRes - 1);
                    int rtY = Mathf.Clamp(Mathf.RoundToInt((float)y / (heightRes - 1) * (rtRes - 1)), 0, rtRes - 1);
                    float normalized = heightRaw[rtY * rtRes + rtX];
                    float worldY = TerrainHeightFormat.DecodeNormalized(normalized, tile.minHeight, tile.maxHeight);
                    ushort raw = TerrainHeightFormat.EncodeHeight(worldY, tile.minHeight, tile.maxHeight);
                    TerrainHeightFormat.WriteRaw(tile.heightData, y * heightRes + x, raw);
                }
            }
        }

        private void WriteSplatToAsset(TerrainTileAsset tile,
            Unity.Collections.NativeArray<Color> splatRaw)
        {
            int splatRes = tile.splatRes;
            int expectedSplat = splatRes * splatRes * 4;
            if (tile.splatData == null || tile.splatData.Length != expectedSplat)
                tile.splatData = new byte[expectedSplat];

            int srtRes = (int)Mathf.Sqrt(splatRaw.Length);
            for (int y = 0; y < splatRes; ++y)
            {
                for (int x = 0; x < splatRes; ++x)
                {
                    int sx = Mathf.Clamp(Mathf.RoundToInt((float)x / (splatRes - 1) * (srtRes - 1)), 0, srtRes - 1);
                    int sy = Mathf.Clamp(Mathf.RoundToInt((float)y / (splatRes - 1) * (srtRes - 1)), 0, srtRes - 1);
                    Color c = splatRaw[sy * srtRes + sx];
                    int baseIdx = (y * splatRes + x) * 4;
                    tile.splatData[baseIdx]     = (byte)(c.r * 255f + 0.5f);
                    tile.splatData[baseIdx + 1] = (byte)(c.g * 255f + 0.5f);
                    tile.splatData[baseIdx + 2] = (byte)(c.b * 255f + 0.5f);
                    tile.splatData[baseIdx + 3] = (byte)(c.a * 255f + 0.5f);
                }
            }
        }

        private void FinalizeWriteback(TerrainTileAsset tile, TerrainTileGpuResources gpu)
        {
            // Re-upload to GPU for live preview.
            if (tile.IsHeightValid)
                gpu.Upload(tile);

#if UNITY_EDITOR
            EditorUtility.SetDirty(tile);
#endif
            Debug.Log($"[TerrainSculptRtWriteback] Writeback complete for tile {tile.tileCoord}.");
        }
    }
}
