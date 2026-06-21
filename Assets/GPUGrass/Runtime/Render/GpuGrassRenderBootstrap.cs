#nullable enable
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GPUGrass
{
    /// <summary>
    /// Wires <see cref="GpuGrassController.RendererFactory"/> to construct the GPU indirect tier
    /// (<see cref="GpuGrassRenderer"/>) from a <see cref="GpuGrassConfig"/>'s serialized render assets.
    /// Runs automatically before the first scene loads (play) and on editor load (edit mode), so a
    /// controller's <see cref="GpuGrassController.Rebuild"/> can build the engine with zero manual wiring.
    /// </summary>
    internal static class GpuGrassRenderBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallRuntime() => GpuGrassController.RendererFactory = Create;

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InstallEditor() => GpuGrassController.RendererFactory = Create;
#endif

        /// <summary>
        /// Builds a <see cref="GpuGrassRenderer"/> from the config's render assets. Returns <c>null</c>
        /// (controller stays grass-less) when the compute shader or a base material can't be resolved —
        /// re-run <c>Tools ▸ GPUGrass ▸ Auto-Setup Grass On Terrain</c> to (re)assign them.
        /// </summary>
        private static IGpuGrassRenderer? Create(GpuGrassConfig config)
        {
            if (config == null) return null;

            ComputeShader? cull = config.CullCompute;
            if (cull == null)
            {
                Debug.LogError(
                    $"[GPUGrass] Config '{config.name}' has no GrassCull compute shader assigned — " +
                    "re-run Tools ▸ GPUGrass ▸ Auto-Setup Grass On Terrain.");
                return null;
            }

            // Prefer the authored material; fall back to a transient material from the shipped shader.
            Material? baseMat = config.GrassMaterial;
            if (baseMat == null && config.IndirectShader != null)
                baseMat = new Material(config.IndirectShader) { name = "GpuGrassIndirect (transient)" };

            if (baseMat == null)
            {
                Debug.LogError(
                    $"[GPUGrass] Config '{config.name}' has no grass material or indirect shader assigned — " +
                    "re-run Tools ▸ GPUGrass ▸ Auto-Setup Grass On Terrain.");
                return null;
            }

            return new GpuGrassRenderer(cull, baseMat);
        }
    }
}
