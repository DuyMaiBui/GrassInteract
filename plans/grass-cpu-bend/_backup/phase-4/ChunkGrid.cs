#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Builds the chunk grid from a <see cref="GrassLayer"/>: seeded rejection sampling against the layer's
    /// density map (a candidate at world XZ is kept with probability = the density sampled there), each kept
    /// blade snapped onto the ground by a downward raycast, then bucketed into square spatial tiles and
    /// pre-partitioned into pooled instancing batches. Build-time only — the per-frame path touches none of
    /// this. Deterministic: same seed + same density map + same static colliders → identical field.
    /// </summary>
    public static class ChunkGrid
    {
        private const float RAY_START_HEIGHT = 1000f; // metres above the field origin to start the snap ray
        private const float RAY_MAX_DISTANCE = 5000f;  // downward cast length

        public static GrassChunk[] Build(GrassLayer layer, Vector3 origin, InstanceBatchPool pool)
        {
            GrassLODConfig config = layer.RenderConfig!; // caller validated layer (RenderConfig != null)
            Texture2D densityMap = layer.DensityMap!;    // caller validated readable + uncompressed

            Vector2 bounds = layer.FieldBounds;
            float chunkSize = layer.ChunkSize;
            int chunksX = Mathf.Max(1, Mathf.CeilToInt(bounds.x / chunkSize));
            int chunksZ = Mathf.Max(1, Mathf.CeilToInt(bounds.y / chunkSize));
            int chunkCount = chunksX * chunksZ;

            // One temporary matrix list per chunk + tracked min/max snapped Y for cull-safe AABBs.
            var buckets = new List<Matrix4x4>[chunkCount];
            var bucketMinY = new float[chunkCount];
            var bucketMaxY = new float[chunkCount];
            for (int i = 0; i < chunkCount; ++i)
            {
                buckets[i] = new List<Matrix4x4>();
                bucketMinY[i] = float.PositiveInfinity;
                bucketMaxY[i] = float.NegativeInfinity;
            }

            var rng = new System.Random(layer.Seed);
            var space = new GrassFieldSpace(origin, bounds); // SAME rect the field binds + trample reads
            float halfX = bounds.x * 0.5f;
            float halfZ = bounds.y * 0.5f;
            float minScale = layer.ScaleRange.x;
            float maxScale = layer.ScaleRange.y;
            int groundMask = layer.GroundSnapMask.value;
            bool warnedNoHit = false;

            for (int i = 0; i < layer.TargetInstances; ++i)
            {
                // Fixed rng draw order (localX, localZ, accept[, yaw, scale]) keeps placement deterministic.
                float localX = (float)rng.NextDouble() * bounds.x - halfX;
                float localZ = (float)rng.NextDouble() * bounds.y - halfZ;
                float accept = (float)rng.NextDouble();

                float worldX = origin.x + localX;
                float worldZ = origin.z + localZ;

                Vector2 uv = space.WorldToUv(new Vector3(worldX, origin.y, worldZ));
                float density = densityMap.GetPixelBilinear(uv.x, uv.y).r;
                if (accept > density)
                    continue; // rejected — this XZ is sparser than the roll

                float yaw = (float)rng.NextDouble() * 360f;
                float scale = Mathf.Lerp(minScale, maxScale, (float)rng.NextDouble());

                // Snap onto the ground collider; fall back to the field plane Y if nothing is hit.
                float worldY;
                var rayOrigin = new Vector3(worldX, origin.y + RAY_START_HEIGHT, worldZ);
                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, RAY_MAX_DISTANCE, groundMask))
                {
                    worldY = hit.point.y;
                }
                else
                {
                    worldY = origin.y;
                    if (!warnedNoHit)
                    {
                        Debug.LogWarning($"[{nameof(ChunkGrid)}] No ground collider hit under a blade " +
                            $"(mask {groundMask}); falling back to field-plane Y = {origin.y}. Ensure the " +
                            "ground has a collider on the GrassLayer's GroundSnapMask.");
                        warnedNoHit = true;
                    }
                }

                var worldPos = new Vector3(worldX, worldY, worldZ);
                Matrix4x4 trs = Matrix4x4.TRS(worldPos, Quaternion.Euler(0f, yaw, 0f),
                    new Vector3(scale, scale, scale));

                int cx = Mathf.Clamp((int)((localX + halfX) / chunkSize), 0, chunksX - 1);
                int cz = Mathf.Clamp((int)((localZ + halfZ) / chunkSize), 0, chunksZ - 1);
                int idx = cz * chunksX + cx;
                buckets[idx].Add(trs);
                if (worldY < bucketMinY[idx]) bucketMinY[idx] = worldY;
                if (worldY > bucketMaxY[idx]) bucketMaxY[idx] = worldY;
            }

            // Vertical span a blade can occupy above its base: tallest scaled blade + deform headroom.
            float bladeReachY = config.MaxBladeHeight * maxScale + config.BendHeadroom;
            float lateralPad = maxScale + config.BendHeadroom;

            var chunks = new List<GrassChunk>(chunkCount);
            for (int cz = 0; cz < chunksZ; ++cz)
            {
                for (int cx = 0; cx < chunksX; ++cx)
                {
                    int idx = cz * chunksX + cx;
                    List<Matrix4x4> bucket = buckets[idx];
                    if (bucket.Count == 0)
                        continue;

                    int batchCount = Mathf.CeilToInt(bucket.Count / (float)InstanceBatchPool.MAX_INSTANCES_PER_BATCH);
                    var batches = new Matrix4x4[batchCount][];
                    var counts = new int[batchCount];
                    for (int b = 0; b < batchCount; ++b)
                    {
                        int start = b * InstanceBatchPool.MAX_INSTANCES_PER_BATCH;
                        int count = Mathf.Min(InstanceBatchPool.MAX_INSTANCES_PER_BATCH, bucket.Count - start);
                        Matrix4x4[] slab = pool.Rent();
                        for (int k = 0; k < count; ++k)
                            slab[k] = bucket[start + k];
                        batches[b] = slab;
                        counts[b] = count;
                    }

                    // Chunk AABB: tile rect in XZ; Y spans the chunk's actual snapped terrain range plus the
                    // blade reach above it, so blades on slopes are never frustum-culled.
                    float yLow = bucketMinY[idx];
                    float yHigh = bucketMaxY[idx] + bladeReachY;
                    float centerX = origin.x - halfX + (cx + 0.5f) * chunkSize;
                    float centerZ = origin.z - halfZ + (cz + 0.5f) * chunkSize;
                    var center = new Vector3(centerX, (yLow + yHigh) * 0.5f, centerZ);
                    var size = new Vector3(chunkSize, Mathf.Max(yHigh - yLow, 0.01f), chunkSize);
                    var aabb = new Bounds(center, size);
                    aabb.Expand(new Vector3(lateralPad, 0f, lateralPad)); // XZ headroom; Y already exact

                    chunks.Add(new GrassChunk(aabb, batches, counts));
                }
            }

            return chunks.ToArray();
        }

        /// <summary>Returns every chunk's pooled slabs so a subsequent <see cref="Build"/> can reuse them.</summary>
        public static void ReturnSlabs(GrassChunk[]? chunks, InstanceBatchPool pool)
        {
            if (chunks == null)
                return;

            foreach (GrassChunk chunk in chunks)
                foreach (Matrix4x4[] slab in chunk.Batches)
                    pool.Return(slab);
        }
    }
}
