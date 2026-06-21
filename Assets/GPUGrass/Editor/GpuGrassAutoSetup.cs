#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GPUGrass.Editor
{
    /// <summary>
    /// Per-terrain wiring helper: adds a <see cref="GpuGrassController"/>, creates a per-terrain
    /// <see cref="GpuGrassBakeData"/> asset, wires render assets, disables the terrain's own detail
    /// rendering, bakes the painted detail, and rebuilds. The <see cref="GpuGrassConfig"/> (including its
    /// LOD blade mesh, assigned manually) is injected by the caller so one shared instance — and the SAME
    /// mesh — covers every terrain in the scene.
    ///
    /// Entry point for scripts: <see cref="SetupOnTerrain"/>. For scene-wide setup (all terrains, one config)
    /// use <see cref="GpuGrassSceneSetup"/>.
    /// </summary>
    public static class GpuGrassAutoSetup
    {
        private const string GENERATED_FOLDER = "Assets/GPUGrass/Generated";
        private const string CULL_COMPUTE_PATH = "Assets/GPUGrass/Shaders/GrassCull.compute";
        private const string INDIRECT_SHADER_NAME = "GPUGrass/IndirectGrass";

        /// <summary>
        /// Wires a single <paramref name="terrain"/> to GPUGrass using the supplied
        /// <paramref name="sharedConfig"/> (caller owns config lifetime + SSOT). Adds the controller,
        /// creates a per-terrain bake asset, wires render assets, disables terrain detail rendering, bakes
        /// the painted detail, and rebuilds. The LOD blade mesh is NOT auto-created — assign it on the shared
        /// config so all terrains share it. Returns the baked blade count (0 if nothing is painted). Does NOT
        /// call <see cref="AssetDatabase.SaveAssets"/>; the caller batches saves.
        /// </summary>
        public static int SetupOnTerrain(Terrain terrain, GpuGrassConfig sharedConfig, string? assetFolder = null)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0;

            // Resolve the per-scene bake folder when the caller didn't supply one.
            string folder = string.IsNullOrEmpty(assetFolder)
                ? GpuGrassSceneSetup.EnsureSceneBakeFolder()
                : assetFolder!;

            // 1) Controller component.
            var controller = terrain.GetComponent<GpuGrassController>();
            if (controller == null)
                controller = Undo.AddComponent<GpuGrassController>(terrain.gameObject);

            // 2) Assign the shared config — the SAME instance for every terrain (Guard 1).
            controller.Config = sharedConfig;

            // 3) Per-terrain bake-data asset (each terrain keeps its own; Guard 2).
            if (controller.Bake == null)
            {
                var bake = ScriptableObject.CreateInstance<GpuGrassBakeData>();
                string path = AssetDatabase.GenerateUniqueAssetPath(
                    $"{folder}/{terrain.name}_GpuGrassBake.asset");
                AssetDatabase.CreateAsset(bake, path);
                controller.Bake = bake;
            }

            controller.Terrain = terrain;

            // 4) Wire render assets (compute + shader + material) — idempotent on the shared config.
            //    NOTE: the LOD blade mesh is NOT auto-created — the user assigns it manually on the shared
            //    config, so every terrain renders with the SAME mesh (SSOT). A config with no LOD mesh bakes
            //    fine but renders nothing until a mesh is assigned (the renderer logs a warning).
            WireRenderAssets(sharedConfig, folder);

            // 5) Disable the terrain's own detail (grass) rendering — keep painted data as bake source.
            Undo.RecordObject(terrain, "Disable Terrain Detail Rendering");
            terrain.detailObjectDistance = 0f;

            // 7) Bake the painted detail layer(s) → per-terrain placement asset.
            int bladeCount = GpuGrassBaker.Bake(terrain, sharedConfig, controller.Bake!);
            EditorUtility.SetDirty(controller.Bake);
            EditorUtility.SetDirty(sharedConfig);

            EditorUtility.SetDirty(terrain);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

            // Force the controller to (re)build with the freshly baked data + wired assets.
            controller.Rebuild();

            string painted = bladeCount > 0
                ? $"baked {bladeCount} blades from {sharedConfig.PlacementSource} (tier: {controller.ResolvedTier})."
                : (sharedConfig.PlacementSource == GrassPlacementSource.TerrainSurface
                    ? "0 blades from the terrain surface — loosen density / slope / altitude band in the config."
                    : "no detail painted yet — paint grass in Terrain ▸ Paint Details (or set PlacementSource = " +
                      "TerrainSurface in the config to scatter over the whole terrain), then re-run to bake.");
            Debug.Log(
                $"[GPUGrass] Setup complete on '{terrain.name}': controller + bake-data + render " +
                $"assets wired, terrain detail rendering disabled. {painted}");
            return bladeCount;
        }

        /// <summary>
        /// Resolves + assigns the GrassCull compute, IndirectGrass shader, and a material into the shared
        /// config. The indirect renderer REQUIRES a material on the <c>GPUGrass/IndirectGrass</c> shader
        /// (its vertex stage rebuilds each blade from the <c>_Blades</c>/<c>_VisibleIndices</c> buffers); a
        /// material on any other shader (e.g. URP/Lit) renders nothing. So if the config has no material —
        /// or the assigned one uses the wrong shader — a fresh indirect material is created in
        /// <paramref name="assetFolder"/>. Idempotent once a correct material exists. Called both at
        /// config-creation time (<see cref="GpuGrassSceneSetup.EnsureSharedConfig"/>) and per terrain during
        /// <see cref="SetupOnTerrain"/>.
        /// </summary>
        internal static void WireRenderAssets(GpuGrassConfig config, string assetFolder)
        {
            var cull   = AssetDatabase.LoadAssetAtPath<ComputeShader>(CULL_COMPUTE_PATH);
            var shader = Shader.Find(INDIRECT_SHADER_NAME);
            if (cull == null)
                Debug.LogError($"[GPUGrass] Could not load compute shader at '{CULL_COMPUTE_PATH}'.");
            if (shader == null)
                Debug.LogError($"[GPUGrass] Could not find shader '{INDIRECT_SHADER_NAME}' (still compiling?).");

            Material? material = config.GrassMaterial;
            bool wrongShader = material != null && (material.shader == null || material.shader != shader);
            if (shader != null && (material == null || wrongShader))
            {
                if (wrongShader)
                    Debug.LogWarning(
                        $"[GPUGrass] Config '{config.name}' grass material used '{material!.shader?.name ?? "<none>"}' " +
                        $"instead of '{INDIRECT_SHADER_NAME}' — creating a correct indirect material (grass would " +
                        "not render otherwise).");

                material = new Material(shader) { name = $"{config.name}_GpuGrassMat" };
                string path = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder}/{config.name}_GpuGrassMat.mat");
                AssetDatabase.CreateAsset(material, path);
            }

            config.SetRenderAssets(cull, shader, material);
        }

        /// <summary>Creates <c>Assets/GPUGrass/Generated</c> if it does not already exist.</summary>
        internal static void EnsureGeneratedFolder()
        {
            if (AssetDatabase.IsValidFolder(GENERATED_FOLDER))
                return;

            if (!AssetDatabase.IsValidFolder("Assets/GPUGrass"))
                AssetDatabase.CreateFolder("Assets", "GPUGrass");

            AssetDatabase.CreateFolder("Assets/GPUGrass", "Generated");
        }
    }
}
