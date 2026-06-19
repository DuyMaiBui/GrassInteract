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
    /// Height-only readback writer (Phase 3 cleanup — splat path is gone). Reads back the
    /// per-tile height RenderTexture to CPU, encodes it into the R16 byte format, writes it
    /// into the <see cref="TerrainTileAsset"/>, and triggers a GPU re-upload.
    ///
    /// Async path: call <see cref="RequestAsync"/> per drag stamp; <see cref="Tick"/> drains
    /// the queue. Sync path: <see cref="ExecuteSync"/> on mouse-up.
    ///
    /// Seam sync: after every writeback the shared edge row/column between adjacent tiles is
    /// copied so both tiles' edge texels are byte-identical. Register neighbours via
    /// <see cref="RegisterNeighbours"/> before the stroke begins.
    /// </summary>
    public sealed class TerrainSculptRtWriteback : IDisposable
    {
        private sealed class PendingReadback
        {
            public TerrainTileAsset        Tile;
            public TerrainTileGpuResources Gpu;
            public RenderTexture           HeightRT;

            public PendingReadback(TerrainTileAsset tile, TerrainTileGpuResources gpu,
                RenderTexture heightRT)
            {
                this.Tile     = tile;
                this.Gpu      = gpu;
                this.HeightRT = heightRT;
            }
        }

        private readonly Dictionary<TerrainTileAsset, PendingReadback> pendingMap =
            new Dictionary<TerrainTileAsset, PendingReadback>();

        // ── Neighbour registry (keyed by tile for seam sync) ──────────────────

        private readonly Dictionary<Vector2Int, TerrainTileAsset> neighbourMap =
            new Dictionary<Vector2Int, TerrainTileAsset>();

        // Parallel coord → GPU resources map so seam sync can re-upload a neighbour's height
        // texture after it edits the neighbour's CPU edge bytes (non-resident tiles have no
        // entry — they aren't rendered, so no upload is needed).
        private readonly Dictionary<Vector2Int, TerrainTileGpuResources> neighbourGpuMap =
            new Dictionary<Vector2Int, TerrainTileGpuResources>();

        public void RegisterNeighbours(
            IEnumerable<(Vector2Int coord, TerrainTileAsset tile, TerrainTileGpuResources? gpu)> tiles)
        {
            this.neighbourMap.Clear();
            this.neighbourGpuMap.Clear();
            foreach (var (coord, tile, gpu) in tiles)
            {
                this.neighbourMap[coord] = tile;
                if (gpu != null)
                    this.neighbourGpuMap[coord] = gpu;
            }
        }

        // ── Async ────────────────────────────────────────────────────────────

        public void RequestAsync(
            TerrainTileAsset tile,
            TerrainTileGpuResources gpu,
            RenderTexture heightRT)
        {
            if (tile == null)     throw new ArgumentNullException(nameof(tile));
            if (gpu == null)      throw new ArgumentNullException(nameof(gpu));
            if (heightRT == null) throw new ArgumentNullException(nameof(heightRT));

            this.pendingMap[tile] = new PendingReadback(tile, gpu, heightRT);
        }

        public void Tick()
        {
            if (this.pendingMap.Count == 0) return;

            var snapshot = new PendingReadback[this.pendingMap.Count];
            this.pendingMap.Values.CopyTo(snapshot, 0);
            this.pendingMap.Clear();

            foreach (var entry in snapshot)
                this.IssueTileReadback(entry);
        }

        private void IssueTileReadback(PendingReadback entry)
        {
            var tile     = entry.Tile;
            var gpu      = entry.Gpu;
            var heightRT = entry.HeightRT;
            if (tile == null || gpu == null || heightRT == null) return;

            AsyncGPUReadback.Request(heightRT, 0, request =>
            {
                if (request.hasError)
                {
                    WpLog.Error("[TerrainSculptRtWriteback] AsyncGPUReadback error on height RT " +
                        $"for tile {tile.tileCoord}.");
                    return;
                }
                if (tile == null || gpu == null) return;

                var heightRaw = request.GetData<float>();
                this.WriteHeightToAsset(tile, heightRaw);
                this.FinalizeWriteback(tile, gpu);
            });
        }

        // ── Sync ─────────────────────────────────────────────────────────────

        public void ExecuteSync(
            TerrainTileAsset tile,
            TerrainTileGpuResources gpu,
            RenderTexture heightRT)
        {
            if (tile == null)     throw new ArgumentNullException(nameof(tile));
            if (gpu == null)      throw new ArgumentNullException(nameof(gpu));
            if (heightRT == null) throw new ArgumentNullException(nameof(heightRT));

            var prevActive = RenderTexture.active;
            RenderTexture.active = heightRT;
            var tmpHeight = new Texture2D(heightRT.width, heightRT.height,
                TextureFormat.RFloat, mipChain: false, linear: true);
            tmpHeight.ReadPixels(new Rect(0, 0, heightRT.width, heightRT.height), 0, 0);
            tmpHeight.Apply();
            RenderTexture.active = prevActive;

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
                    int rtX = Mathf.Clamp(Mathf.RoundToInt((float)x / (heightRes - 1) * (rtRes - 1)), 0, rtRes - 1);
                    int rtY = Mathf.Clamp(Mathf.RoundToInt((float)y / (heightRes - 1) * (rtRes - 1)), 0, rtRes - 1);
                    float normalized = pixels[rtY * rtRes + rtX].r;
                    float worldY = TerrainHeightFormat.DecodeNormalized(normalized, tile.minHeight, tile.maxHeight);
                    ushort raw = TerrainHeightFormat.EncodeHeight(worldY, tile.minHeight, tile.maxHeight);
                    TerrainHeightFormat.WriteRaw(tile.heightData, y * heightRes + x, raw);
                }
            }
            UnityEngine.Object.DestroyImmediate(tmpHeight);

            this.FinalizeWriteback(tile, gpu);
        }

        public void CancelPending() => this.pendingMap.Clear();
        public void Dispose()       => this.pendingMap.Clear();

        // ── Private helpers ──────────────────────────────────────────────────

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

        private void FinalizeWriteback(TerrainTileAsset tile, TerrainTileGpuResources gpu)
        {
            this.ApplySeamSync(tile);

            if (tile.IsHeightValid)
                gpu.Upload(tile);

#if UNITY_EDITOR
            EditorUtility.SetDirty(tile);
#endif
        }

        // ── Seam sync (height only) ──────────────────────────────────────────

        public void ApplySeamSync(TerrainTileAsset sourceTile)
        {
            if (sourceTile == null) return;
            var coord = sourceTile.tileCoord;

            var plusX = new Vector2Int(coord.x + 1, coord.y);
            if (this.neighbourMap.TryGetValue(plusX, out var neighX) && neighX != null
                && SyncHeightColumn(sourceTile, neighX, isRight: true))
                this.UploadNeighbour(plusX, neighX);

            var minusX = new Vector2Int(coord.x - 1, coord.y);
            if (this.neighbourMap.TryGetValue(minusX, out var neighMX) && neighMX != null
                && SyncHeightColumn(sourceTile, neighMX, isRight: false))
                this.UploadNeighbour(minusX, neighMX);

            var plusZ = new Vector2Int(coord.x, coord.y + 1);
            if (this.neighbourMap.TryGetValue(plusZ, out var neighZ) && neighZ != null
                && SyncHeightRow(sourceTile, neighZ, isTop: true))
                this.UploadNeighbour(plusZ, neighZ);

            var minusZ = new Vector2Int(coord.x, coord.y - 1);
            if (this.neighbourMap.TryGetValue(minusZ, out var neighMZ) && neighMZ != null
                && SyncHeightRow(sourceTile, neighMZ, isTop: false))
                this.UploadNeighbour(minusZ, neighMZ);
        }

        // Re-upload a neighbour tile whose shared-edge bytes were just rewritten by seam sync.
        // Without this, the neighbour's GPU height texture keeps its own (divergent) edge and a
        // visible crack opens at the seam — the Smooth-tool symptom, since Smooth is the only
        // kernel whose edge value diverges between tiles (it box-averages within one tile's RT).
        private void UploadNeighbour(Vector2Int coord, TerrainTileAsset neighbour)
        {
            if (neighbour.IsHeightValid
                && this.neighbourGpuMap.TryGetValue(coord, out var gpu) && gpu != null)
                gpu.Upload(neighbour);
        }

        private static bool SyncHeightColumn(TerrainTileAsset src, TerrainTileAsset dst, bool isRight)
        {
            if (!src.IsHeightValid || !dst.IsHeightValid) return false;

            int srcRes = src.heightRes;
            int dstRes = dst.heightRes;
            int srcCol = isRight ? srcRes - 1 : 0;
            int dstCol = isRight ? 0         : dstRes - 1;

            for (int dstRow = 0; dstRow < dstRes; ++dstRow)
            {
                int srcRow = (srcRes == dstRes)
                    ? dstRow
                    : Mathf.Clamp(Mathf.RoundToInt((float)dstRow / (dstRes - 1) * (srcRes - 1)), 0, srcRes - 1);

                int srcByte = (srcRow * srcRes + srcCol) * TerrainHeightFormat.BYTES_PER_SAMPLE;
                int dstByte = (dstRow * dstRes + dstCol) * TerrainHeightFormat.BYTES_PER_SAMPLE;

                dst.heightData[dstByte]     = src.heightData[srcByte];
                dst.heightData[dstByte + 1] = src.heightData[srcByte + 1];
            }
            return true;
        }

        private static bool SyncHeightRow(TerrainTileAsset src, TerrainTileAsset dst, bool isTop)
        {
            if (!src.IsHeightValid || !dst.IsHeightValid) return false;

            int srcRes = src.heightRes;
            int dstRes = dst.heightRes;
            int srcRow = isTop ? srcRes - 1 : 0;
            int dstRow = isTop ? 0          : dstRes - 1;

            for (int dstCol = 0; dstCol < dstRes; ++dstCol)
            {
                int srcCol = (srcRes == dstRes)
                    ? dstCol
                    : Mathf.Clamp(Mathf.RoundToInt((float)dstCol / (dstRes - 1) * (srcRes - 1)), 0, srcRes - 1);

                int srcByte = (srcRow * srcRes + srcCol) * TerrainHeightFormat.BYTES_PER_SAMPLE;
                int dstByte = (dstRow * dstRes + dstCol) * TerrainHeightFormat.BYTES_PER_SAMPLE;

                dst.heightData[dstByte]     = src.heightData[srcByte];
                dst.heightData[dstByte + 1] = src.heightData[srcByte + 1];
            }
            return true;
        }
    }
}
