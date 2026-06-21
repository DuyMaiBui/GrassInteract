#nullable enable
// WorldPainterShaderWarmup.cs — Phase 6a: ShaderVariantCollection warmup
//
// HOW TO CREATE / REGENERATE THE VARIANT COLLECTION
// ─────────────────────────────────────────────────────────────────────────────────
// The ShaderVariantCollection asset is NOT checked in automatically — it must be
// recorded from a representative play-through, then saved to the path below.
//
// Steps:
//   1. Open the WorldPainter demo scene.
//   2. In the Graphics Settings (Edit → Project Settings → Graphics), check
//      "Log Shader Compilation" to help verify coverage.
//   3. Enter Play mode. Walk the camera through the grass field, orbit the terrain,
//      trigger all LOD transitions (close, mid, far).
//   4. Open Window → Analysis → Shader Variant Collection.
//   5. Click "Save to asset" → navigate to:
//        Assets/WorldPainter/Runtime/Resources/ShaderVariants/WorldPainterVariants.shadervariants
//      (This is the Resources/ path loaded by Resources.Load in WarmUp — create the folder if needed.)
//   6. Exit Play mode.
//   7. Add the asset to Graphics Settings → Preloaded Shaders list (or leave this
//      script to handle it via ShaderVariantCollection.WarmUp at runtime).
//
// WHY WARMUP MATTERS (1% lows):
//   On mobile, the GPU driver compiles each shader variant on its very first draw call.
//   Without warmup, the first traversal through the grass field produces micro-stalls
//   (5–50 ms each on Adreno 730 class) that appear as 1%-low dips in PerformanceConsole.
//   Warming up during the loading screen front-loads all compilation to a non-interactive
//   window, eliminating the stalls from the play session.
//
// LOAD-TIME COST:
//   WarmUp() blocks on the main thread for the duration of compilation. On Adreno 730
//   with the stripped variant set (~30–60 variants after Phase 6a stripping, down from
//   ~200+), this takes approximately 100–300 ms — an acceptable loading-screen tax.
//   If this proves too long, switch to the async Graphics.WarmupShaderVariants API
//   (Unity 6 / URP 16+) which amortises compilation over multiple frames.
// ─────────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Warms up the WorldPainter ShaderVariantCollection before the first scene loads,
    /// eliminating first-use shader-compile hitches during gameplay (improves 1% lows).
    ///
    /// The collection asset is loaded from the path defined in
    /// <see cref="VARIANT_COLLECTION_RESOURCES_PATH"/>. If the asset does not yet exist
    /// (before the first play-through recording), the warmup is silently skipped — the
    /// game still runs correctly, just without warmup (hitches may occur on first traversal).
    ///
    /// See the file header comment for how to record and save the collection asset.
    /// </summary>
    public static class WorldPainterShaderWarmup
    {
        // Path relative to a Resources/ folder (without extension).
        // Asset must live at:
        //   Assets/WorldPainter/Runtime/Resources/ShaderVariants/WorldPainterVariants.shadervariants
        private const string VARIANT_COLLECTION_RESOURCES_PATH =
            "ShaderVariants/WorldPainterVariants";

        /// <summary>
        /// Called automatically at subsystem registration — before BeforeSceneLoad —
        /// so warmup runs as early as possible during the loading pipeline.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void WarmUp()
        {
            var collection = Resources.Load<ShaderVariantCollection>(VARIANT_COLLECTION_RESOURCES_PATH);

            if (collection == null)
            {
                // Collection not yet created (no play-through recording done).
                // Silently skip — no pink shaders, no crash; just no warmup benefit yet.
                // See file header for how to create the asset.
                WpLog.Log(
                    "[WorldPainterShaderWarmup] No ShaderVariantCollection found at " +
                    $"'Resources/{VARIANT_COLLECTION_RESOURCES_PATH}'. " +
                    "Shader warmup skipped. See WorldPainterShaderWarmup.cs for recording instructions.");
                return;
            }

            if (collection.isWarmedUp)
            {
                // Already warmed (e.g. added to Graphics Settings → Preloaded Shaders).
                return;
            }

            // WarmUp() compiles all variants in the collection on the main thread.
            // Keep the variant count low (< 60 after Phase 6a stripping) to bound the cost.
            collection.WarmUp();

            WpLog.Log(
                $"[WorldPainterShaderWarmup] Warmed up {collection.shaderCount} shaders, " +
                $"{collection.variantCount} variants.");
        }
    }
}
