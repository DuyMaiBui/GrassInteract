#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Static device-capability probe for tier selection.
    ///
    /// <see cref="TryGpu"/> returns <c>true</c> when the current device meets the minimum
    /// bar for the GPU indirect tier:
    ///   - <see cref="SystemInfo.supportsComputeShaders"/>
    ///   - <see cref="SystemInfo.supportsIndirectArgumentsBuffer"/>
    ///   - <see cref="SystemInfo.maxComputeBufferInputsVertex"/> > 0
    ///
    /// The method is pure (no side-effects, no allocations beyond the one-time string build)
    /// and safe to call from any thread or editor context.
    /// </summary>
    public static class GrassTierProbe
    {
        /// <summary>
        /// Returns <c>true</c> when the GPU indirect tier is viable on the current device.
        /// Populates <paramref name="reason"/> with a human-readable one-liner regardless of
        /// the result (suitable for a single <see cref="Debug.Log"/> call).
        /// </summary>
        /// <param name="reason">
        /// On success: e.g. "GPU tier: OK (compute+indirect+VS-buffers supported)".<br/>
        /// On failure: names the first flag that failed, e.g.
        ///   "CPU tier: supportsIndirectArgumentsBuffer=false".
        /// </param>
        /// <returns><c>true</c> → GPU tier viable; <c>false</c> → fall back to CPU tier.</returns>
        public static bool TryGpu(out string reason)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                reason = "CPU tier: supportsComputeShaders=false";
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

            reason = "GPU tier: OK (compute+indirect+VS-buffers supported)";
            return true;
        }
    }
}
