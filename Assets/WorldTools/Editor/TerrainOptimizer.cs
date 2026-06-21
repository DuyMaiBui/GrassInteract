#nullable enable
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WorldTools.Editor
{
    /// <summary>
    /// One-click optimization for Unity built-in <see cref="Terrain"/>: turns on GPU-instanced terrain
    /// drawing, applies mobile-oriented LOD / draw-distance / memory settings, and marks the terrain
    /// occluder/occludee-static so it participates in occlusion culling. Menu:
    /// <c>Tools ▸ World ▸ Terrain ▸ …</c>.
    ///
    /// What it does (all undoable, logged before→after):
    /// • <see cref="Terrain.drawInstanced"/> = true — draws terrain patches with GPU instancing
    ///   (one instanced draw instead of one per patch), cutting CPU draw-call + render-thread cost.
    ///   Skipped with a warning when the device/editor reports no instancing support.
    /// • <see cref="Terrain.heightmapPixelError"/> ↑ — coarser screen-space LOD = fewer terrain triangles.
    /// • <see cref="Terrain.basemapDistance"/> ↓ — full splat detail only near the camera; the cheap
    ///   combined basemap renders beyond it.
    /// • <see cref="Terrain.keepUnusedRenderingResources"/> = false — frees per-terrain GPU resources the
    ///   active camera doesn't need (memory).
    /// • <see cref="Terrain.allowAutoConnect"/> = true — neighbouring terrain tiles stitch seams.
    /// • Occluder|Occludee <see cref="StaticEditorFlags"/> on the terrain GameObject.
    ///
    /// Occlusion culling NOTE: marking the terrain static makes it occlusion-ready, but the PVS still has
    /// to be BAKED. This tool delegates that (props + Occlusion Area + bake) to the existing
    /// <see cref="OcclusionCullingAutoSetup"/> — it offers to run it at the end rather than duplicating it.
    /// It does NOT touch <c>detailObjectDistance</c> (owned by the grass tools) or the terrain material.
    /// </summary>
    public static class TerrainOptimizer
    {
        private const string MENU_ROOT = "Tools/World/Terrain/";
        private const string OCCLUSION_BAKE_MENU = "Tools/World/Occlusion Culling/Auto-Configure + Bake (Mobile)";

        // ── Optimization targets (edit here; every change is logged before→after + undoable) ──
        private const float OPTIMIZED_PIXEL_ERROR      = 8f;    // ↑ from default 5 → fewer LOD triangles
        private const float OPTIMIZED_BASEMAP_DISTANCE = 200f;  // splat detail within this range, basemap beyond

        private static readonly StaticEditorFlags OCCLUSION_FLAGS =
            StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

        // ── Menu entry points ─────────────────────────────────────────────────

        /// <summary>
        /// THE one-click button: optimize every terrain in the open scene (GPU instancing + LOD/memory +
        /// occlusion-static) AND automatically run the occlusion-culling auto-configure + PVS bake — no
        /// prompts. Everything set up in a single action.
        /// </summary>
        [MenuItem(MENU_ROOT + "⚡ One-Click Optimize All + Bake Occlusion", false, 0)]
        private static void OneClickOptimizeAll()
        {
            var terrains = new List<Terrain>(Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None));
            if (terrains.Count == 0)
            {
                EditorUtility.DisplayDialog("Terrain Optimizer", "No Terrain found in the open scene.", "OK");
                return;
            }
            Optimize(terrains, autoBake: true);
        }

        [MenuItem(MENU_ROOT + "Optimize Selected (GPU Instancing + Occlusion-Ready)", false, 20)]
        private static void OptimizeSelected()
        {
            var terrains = CollectSelectedTerrains();
            if (terrains.Count == 0)
            {
                EditorUtility.DisplayDialog("Terrain Optimizer",
                    "Select one or more Terrain GameObjects first (or use the One-Click option).", "OK");
                return;
            }
            Optimize(terrains, autoBake: false);
        }

        [MenuItem(MENU_ROOT + "Optimize All In Scene (no bake)", false, 21)]
        private static void OptimizeAll()
        {
            var terrains = new List<Terrain>(Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None));
            if (terrains.Count == 0)
            {
                EditorUtility.DisplayDialog("Terrain Optimizer", "No Terrain found in the open scene.", "OK");
                return;
            }
            Optimize(terrains, autoBake: false);
        }

        // ── Core ──────────────────────────────────────────────────────────────

        private static void Optimize(IReadOnlyList<Terrain> terrains, bool autoBake)
        {
            bool instancingSupported = SystemInfo.supportsInstancing;
            var log = new StringBuilder();
            int count = 0;

            foreach (Terrain terrain in terrains)
            {
                if (terrain == null) continue;

                Undo.RecordObject(terrain, "Optimize Terrain");

                log.Append("• ").Append(terrain.name).Append(": ");

                // GPU instancing — the headline optimization.
                if (instancingSupported)
                {
                    log.Append("drawInstanced ").Append(terrain.drawInstanced).Append("→true  ");
                    terrain.drawInstanced = true;
                }
                else
                {
                    log.Append("drawInstanced SKIPPED (no instancing support)  ");
                }

                // LOD: coarser screen-space error → fewer triangles. Only ever RAISE it (never make the
                // terrain denser than the user already set).
                if (terrain.heightmapPixelError < OPTIMIZED_PIXEL_ERROR)
                {
                    log.Append("pixelErr ").Append(terrain.heightmapPixelError.ToString("0.#"))
                       .Append("→").Append(OPTIMIZED_PIXEL_ERROR.ToString("0.#")).Append("  ");
                    terrain.heightmapPixelError = OPTIMIZED_PIXEL_ERROR;
                }

                // Basemap distance: pull splat detail in (cheap basemap beyond). Only ever LOWER it.
                if (terrain.basemapDistance > OPTIMIZED_BASEMAP_DISTANCE)
                {
                    log.Append("basemap ").Append(terrain.basemapDistance.ToString("0"))
                       .Append("→").Append(OPTIMIZED_BASEMAP_DISTANCE.ToString("0")).Append("  ");
                    terrain.basemapDistance = OPTIMIZED_BASEMAP_DISTANCE;
                }

                // Memory: free GPU resources the active camera isn't using.
                if (terrain.keepUnusedRenderingResources)
                {
                    terrain.keepUnusedRenderingResources = false;
                    log.Append("keepUnused→false  ");
                }

                // Seam stitching across neighbouring tiles.
                if (!terrain.allowAutoConnect)
                {
                    terrain.allowAutoConnect = true;
                    log.Append("autoConnect→true  ");
                }

                // Occlusion-ready: flag the terrain GameObject occluder|occludee static.
                if (AddStaticFlags(terrain.gameObject, OCCLUSION_FLAGS))
                    log.Append("occlusion-static set  ");

                EditorUtility.SetDirty(terrain);
                log.Append('\n');
                count++;
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log($"[TerrainOptimizer] Optimized {count} terrain(s):\n{log}");

            if (autoBake)
            {
                // One-click path: run the full occlusion auto-configure + PVS bake immediately, no prompt.
                Debug.Log("[TerrainOptimizer] One-click: running occlusion auto-configure + bake…");
                EditorApplication.ExecuteMenuItem(OCCLUSION_BAKE_MENU);
            }
            else
            {
                OfferOcclusionBake(count);
            }
        }

        /// <summary>Adds <paramref name="flags"/> to the GameObject's static flags. Returns true if changed.</summary>
        private static bool AddStaticFlags(GameObject go, StaticEditorFlags flags)
        {
            StaticEditorFlags current = GameObjectUtility.GetStaticEditorFlags(go);
            StaticEditorFlags merged = current | flags;
            if (merged == current) return false;

            Undo.RegisterCompleteObjectUndo(go, "Set Occlusion Static Flags");
            GameObjectUtility.SetStaticEditorFlags(go, merged);
            return true;
        }

        /// <summary>
        /// Offers to run the existing occlusion-culling auto-configure + bake (props + Occlusion Area + PVS)
        /// rather than duplicating that logic here. The terrain is already occlusion-static at this point.
        /// </summary>
        private static void OfferOcclusionBake(int count)
        {
            bool bake = EditorUtility.DisplayDialog(
                "Terrain Optimizer",
                $"Optimized {count} terrain(s): GPU-instanced drawing enabled, LOD/memory tuned, and marked " +
                "occluder/occludee-static.\n\nOcclusion culling is set up but not yet baked. Run the occlusion " +
                "auto-configure + PVS bake now? (also flags static props and sizes an Occlusion Area)",
                "Bake Occlusion Culling", "Skip (I'll bake later)");

            if (bake)
                EditorApplication.ExecuteMenuItem(OCCLUSION_BAKE_MENU);
        }

        private static List<Terrain> CollectSelectedTerrains()
        {
            var result = new List<Terrain>();
            foreach (GameObject go in Selection.gameObjects)
            {
                var terrain = go.GetComponent<Terrain>();
                if (terrain != null && !result.Contains(terrain))
                    result.Add(terrain);
            }
            return result;
        }
    }
}
