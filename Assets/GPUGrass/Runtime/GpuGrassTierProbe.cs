#nullable enable
using UnityEngine;

namespace GPUGrass
{
    /// <summary>
    /// Device-capability probe for tier selection — the standalone copy of WorldPainter's GrassTierProbe.
    /// Returns true when the GPU indirect tier is viable (compute shaders + indirect-args buffer +
    /// vertex-stage buffer reads). Anything below — notably OpenGL ES 3.0, which has no compute — falls
    /// back to the CPU instanced tier.
    /// </summary>
    public static class GpuGrassTierProbe
    {
        /// <summary>
        /// <c>true</c> → GPU indirect tier viable; <c>false</c> → use the CPU instanced tier.
        /// <paramref name="reason"/> carries a human-readable explanation for logging.
        /// </summary>
        public static bool TryGpu(out string reason)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                reason = "CPU tier: supportsComputeShaders=false (e.g. OpenGL ES 3.0)";
                return false;
            }

            if (!SystemInfo.supportsIndirectArgumentsBuffer)
            {
                reason = "CPU tier: supportsIndirectArgumentsBuffer=false";
                return false;
            }

            if (SystemInfo.maxComputeBufferInputsVertex <= 0)
            {
                reason = $"CPU tier: maxComputeBufferInputsVertex={SystemInfo.maxComputeBufferInputsVertex} (must be >0)";
                return false;
            }

            reason = "GPU tier: OK (compute + indirect + vertex-stage buffers supported)";
            return true;
        }

        /// <summary>
        /// Resolves the AUTO device tier under the policy: low-end (system memory below the threshold) →
        /// <see cref="GrassDeviceTier.Disabled"/>; compute-capable → <see cref="GrassDeviceTier.Gpu"/>
        /// (interactive GPUGrass); otherwise → <see cref="GrassDeviceTier.TerrainFallback"/> (Unity's
        /// built-in terrain detail grass) when allowed, else Disabled.
        /// </summary>
        public static GrassDeviceTier ClassifyAuto(bool allowTerrainFallback, int lowEndMemoryThresholdMB)
        {
            if (lowEndMemoryThresholdMB > 0 &&
                SystemInfo.systemMemorySize > 0 &&
                SystemInfo.systemMemorySize < lowEndMemoryThresholdMB)
            {
                return GrassDeviceTier.Disabled;
            }

            if (TryGpu(out _))
            {
                return GrassDeviceTier.Gpu;
            }

            return allowTerrainFallback ? GrassDeviceTier.TerrainFallback : GrassDeviceTier.Disabled;
        }
    }

    /// <summary>Resolved grass tier for the current device under the GPUGrass tier policy.</summary>
    public enum GrassDeviceTier
    {
        /// <summary>Compute-capable → interactive GPUGrass (GPU indirect).</summary>
        Gpu,
        /// <summary>No compute, but an acceptable device → Unity's built-in Terrain detail grass.</summary>
        TerrainFallback,
        /// <summary>Low-end → no grass at all.</summary>
        Disabled,
    }
}
