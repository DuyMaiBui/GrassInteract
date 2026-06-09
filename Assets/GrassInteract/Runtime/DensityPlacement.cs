#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Procedural-density placement strategy: density-map sample + RNG accept/reject + slope/splat filter.
    /// Mirrors the existing main code path in <see cref="GrassScatter.Build"/>.
    /// </summary>
    internal sealed class DensityPlacement : IScatterPlacement
    {
        private readonly IDensityPlacementSource source;

        public DensityPlacement(IDensityPlacementSource source)
        {
            this.source = source;
        }

        public GrassScatterResult Build(Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler)
        {
            Texture2D densityMap = this.source.DensityMap!;    // caller validated readable + uncompressed

            // Terrain-driven bounds: when a terrain is bound, the field rect = the terrain's XZ size
            // (origin is the terrain center, set by ScatterField). The layer's manual FieldBounds is the
            // fallback for non-terrain (raycast) fields only.
            Vector2 bounds = sampler is TerrainSurfaceSampler tss ? tss.TerrainSizeXZ : this.source.FieldBounds;

            // Flat draw-order accumulation (no spatial buckets) + tracked min/max snapped Y for a cull-safe AABB.
            var matrices  = new List<Matrix4x4>(this.source.TargetInstances);
            var positions = new List<Vector3>(this.source.TargetInstances);
            var normals   = new List<Vector3>(this.source.TargetInstances);
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;

            var rng = new System.Random(this.source.Seed);
            var space = new GrassFieldSpace(origin, bounds); // SAME rect placement keys off
            float halfX    = bounds.x * 0.5f;
            float halfZ    = bounds.y * 0.5f;
            float minScale = this.source.ScaleRange.x;
            float maxScale = this.source.ScaleRange.y;

            // Phase 4: orientation parameters.
            Vector2 slopeRange          = this.source.SlopeRange;
            int     splatLayerIndex     = this.source.SplatLayerIndex;
            float   splatThreshold      = this.source.SplatThreshold;
            Vector2 pitchRange          = this.source.RandomPitchRange;
            Vector2 rollRange           = this.source.RandomRollRange;
            bool    alignToNormal       = this.source.AlignToNormal;
            Quaternion rotationOffset   = Quaternion.Euler(this.source.RotationOffsetEuler);
            bool    hasPitchRange       = pitchRange.x != pitchRange.y;
            bool    hasRollRange        = rollRange.x  != rollRange.y;

            bool warnedNoHit = false;

            for (int i = 0; i < this.source.TargetInstances; ++i)
            {
                // ── Fixed rng draw order (localX, localZ, accept[, yaw, scale]) keeps placement byte-stable
                // vs the prior spatial-chunk scatter. Phase 4 appends pitch/roll draws AFTER the existing
                // yaw+scale pair so the accepted-candidate sequence stays byte-identical when ranges are zero.
                float localX = (float)rng.NextDouble() * bounds.x - halfX;
                float localZ = (float)rng.NextDouble() * bounds.y - halfZ;
                float accept = (float)rng.NextDouble();

                float worldX = origin.x + localX;
                float worldZ = origin.z + localZ;

                Vector2 uv = space.WorldToUv(new Vector3(worldX, origin.y, worldZ));
                float density = densityMap.GetPixelBilinear(uv.x, uv.y).r;
                if (accept > density)
                    continue; // rejected — this XZ is sparser than the roll

                // Draw yaw + scale BEFORE the surface sample so byte order is preserved for defaults.
                float yaw   = (float)rng.NextDouble() * 360f;
                float scale = Mathf.Lerp(minScale, maxScale, (float)rng.NextDouble());

                // Phase 4: pitch/roll draws AFTER yaw+scale (appended, not inserted).
                // Only consumed when the range is non-degenerate so the default sequence is unchanged.
                float pitch = 0f;
                if (hasPitchRange)
                    pitch = Mathf.Lerp(pitchRange.x, pitchRange.y, (float)rng.NextDouble());

                float roll = 0f;
                if (hasRollRange)
                    roll = Mathf.Lerp(rollRange.x, rollRange.y, (float)rng.NextDouble());

                // Snap onto the ground surface; fall back to the field plane Y if nothing is hit.
                float   worldY     = origin.y;
                Vector3 surfNormal = Vector3.up;

                if (sampler.TrySample(worldX, worldZ, out SurfaceHit hit))
                {
                    // Slope filter — uses slopeRange (default 0..90 = no filter).
                    if (hit.SlopeDeg < slopeRange.x || hit.SlopeDeg > slopeRange.y)
                        continue;

                    // Phase 4: splat-mask filter (-1 = off).
                    if (splatLayerIndex >= 0)
                    {
                        if (hit.SplatWeights == null ||
                            splatLayerIndex >= hit.SplatWeights.Length ||
                            hit.SplatWeights[splatLayerIndex] < splatThreshold)
                            continue;
                    }

                    worldY     = hit.Y;
                    surfNormal = hit.Normal;
                }
                else
                {
                    // No ground / terrain hole — fall back to field-plane Y, warn once.
                    if (!warnedNoHit)
                    {
                        Debug.LogWarning($"[{nameof(GrassScatter)}] No ground hit under a blade; " +
                            $"falling back to field-plane Y = {origin.y}. For raycast: ensure the ground " +
                            "has a collider on the ScatterLayer's GroundSnapMask. For terrain: candidate may " +
                            "be in a hole or outside the terrain bounds.");
                        warnedNoHit = true;
                    }
                }

                var worldPos = new Vector3(worldX, worldY, worldZ);

                // Phase 4 rotation composition (all defaults → final == Euler(0,yaw,0) → byte-identical):
                //   baseRot  = Euler(pitch, yaw, roll)   (pitch/roll = 0 by default)
                //   align    = FromToRotation(up, normal) when alignToNormal; identity otherwise
                //   offset   = Euler(rotationOffsetEuler); identity when offset is zero
                //   final    = align * baseRot * offset
                // With defaults: align=identity, pitch=roll=0, offset=identity → final=Euler(0,yaw,0).
                Quaternion baseRot = Quaternion.Euler(pitch, yaw, roll);
                Quaternion align   = alignToNormal
                    ? Quaternion.FromToRotation(Vector3.up, surfNormal)
                    : Quaternion.identity;
                Quaternion final   = align * baseRot * rotationOffset;

                Matrix4x4 trs = Matrix4x4.TRS(worldPos, final, new Vector3(scale, scale, scale));

                matrices.Add(trs);
                positions.Add(worldPos);
                normals.Add(surfNormal);
                if (worldY < minY) minY = worldY;
                if (worldY > maxY) maxY = worldY;
            }

            int total     = matrices.Count;
            int slabCount = Mathf.Max(1, Mathf.CeilToInt(total / (float)InstanceBatchPool.MAX_INSTANCES_PER_BATCH));
            var baseSlabs     = new Matrix4x4[slabCount][];
            var positionSlabs = new Vector3[slabCount][];
            var normalSlabs   = new Vector3[slabCount][];
            var slabCounts    = new int[slabCount];

            for (int b = 0; b < slabCount; ++b)
            {
                int start = b * InstanceBatchPool.MAX_INSTANCES_PER_BATCH;
                int count = Mathf.Min(InstanceBatchPool.MAX_INSTANCES_PER_BATCH, total - start);
                if (count < 0) count = 0;

                Matrix4x4[] slab    = pool.Rent();
                var posSlab         = new Vector3[InstanceBatchPool.MAX_INSTANCES_PER_BATCH];
                var nrmSlab         = new Vector3[InstanceBatchPool.MAX_INSTANCES_PER_BATCH];
                for (int k = 0; k < count; ++k)
                {
                    slab[k]    = matrices[start + k];
                    posSlab[k] = positions[start + k];
                    nrmSlab[k] = normals[start + k];
                }
                baseSlabs[b]     = slab;
                positionSlabs[b] = posSlab;
                normalSlabs[b]   = nrmSlab;
                slabCounts[b]    = count;
            }

            Bounds worldBounds = BuildFieldBounds(this.source, origin, bounds, halfX, halfZ, maxScale, minY, maxY);
            return new GrassScatterResult(baseSlabs, slabCounts, positionSlabs, normalSlabs, total, worldBounds);
        }

        /// <summary>
        /// One field-wide AABB: XZ = the field rect (origin ± halfBounds) expanded by lateral pad; Y spans
        /// [minSnappedY, maxSnappedY + bladeReachY] where bladeReachY covers the tallest scaled blade plus
        /// bend/wind headroom, so no blade is ever wrongly culled.
        /// Mirrors <c>GrassScatter.BuildFieldBounds</c> — kept internal so R5 can unify when BuildFieldBounds
        /// is promoted to internal static.
        /// </summary>
        private static Bounds BuildFieldBounds(IDensityPlacementSource source, Vector3 origin, Vector2 bounds,
            float halfX, float halfZ, float maxScale, float minY, float maxY)
        {
            float maxBladeHeight = source.MaxBladeHeight;
            float bendHeadroom   = source.BendHeadroom;
            float bladeReachY = maxBladeHeight * maxScale + bendHeadroom;
            float lateralPad = maxScale + bendHeadroom;

            if (float.IsPositiveInfinity(minY))
            {
                minY = origin.y;
                maxY = origin.y;
            }

            float yLow = minY;
            float yHigh = maxY + bladeReachY;
            var center = new Vector3(origin.x, (yLow + yHigh) * 0.5f, origin.z);
            var size = new Vector3(bounds.x, Mathf.Max(yHigh - yLow, 0.01f), bounds.y);
            var aabb = new Bounds(center, size);
            aabb.Expand(new Vector3(lateralPad, 0f, lateralPad));
            return aabb;
        }
    }
}
