#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WorldTools.Editor
{
    /// <summary>
    /// One-click auto-setup + bake of Unity's built-in Occlusion Culling for scenes that use
    /// built-in <see cref="Terrain"/>, with mobile-oriented defaults and honest reporting.
    /// Menu: <c>Tools ▸ World ▸ Occlusion Culling ▸ …</c>.
    ///
    /// SCOPE REALITY (verified against Unity 6000.3 docs):
    /// - Occlusion culling culls STATIC <see cref="MeshRenderer"/>s and the Terrain mesh that are
    ///   hidden behind static occluders (e.g. props behind a hill).
    /// - It does NOT cull terrain DETAIL grass or terrain TREES — those are culled by
    ///   <c>Terrain.detailObjectDistance</c> / <c>Terrain.treeDistance</c>, never the PVS.
    /// - The bake-tuning params (Smallest Occluder / Smallest Hole / Backface Threshold) are
    ///   Occlusion-Culling-WINDOW-ONLY in 6000.3 — there is no public scripting setter. This tool
    ///   bakes with whatever is currently saved and LOGS the recommended mobile values so you can
    ///   apply them once in the window (menu item below opens it).
    /// - On a mostly-open terrain world, occlusion culling can be net-negative (per-frame query
    ///   cost with few actual occlusions). Prefer frustum + distance culling as the primary system;
    ///   use this bake only when the scene has dense static prop geometry behind real occluders.
    /// </summary>
    public static class OcclusionCullingAutoSetup
    {
        // ── Menu ──────────────────────────────────────────────────────────────
        private const string MENU_ROOT = "Tools/World/Occlusion Culling/";

        // ── Mobile bake-param recommendations (window-only; logged, not set via API) ──
        private const float RECOMMENDED_SMALLEST_OCCLUDER  = 5f;   // metres — only hills/buildings occlude
        private const float RECOMMENDED_SMALLEST_HOLE      = 0.5f; // metres — smallest see-through gap
        private const float RECOMMENDED_BACKFACE_THRESHOLD = 100f; // keep all backfaces (default)

        // Warn if baked PVS data exceeds this — large data hurts mobile load + RAM.
        private const long MOBILE_DATA_WARN_BYTES = 5L * 1024 * 1024;

        // Vertical padding (metres) added above/below world bounds for the Occlusion Area, so the
        // camera's view volume is fully enclosed (a kart/camera rises above the ground plane).
        private const float AREA_VERTICAL_PADDING_M = 50f;

        private const string AREA_OBJECT_NAME = "OcclusionArea_Auto";

        // ── Public menu entry points ──────────────────────────────────────────

        [MenuItem(MENU_ROOT + "Auto-Configure + Bake (Mobile)", false, 0)]
        private static void AutoConfigureAndBake()
        {
            if (!ConfigureStaticsAndArea())
            {
                return;
            }

            BeginBake();
        }

        [MenuItem(MENU_ROOT + "Auto-Configure Statics + Area (no bake)", false, 1)]
        private static void ConfigureStaticsOnly()
        {
            if (ConfigureStaticsAndArea())
            {
                LogBakeParamRecommendations();
                Debug.Log("[Occlusion] Statics + Occlusion Area configured. Run the bake when ready.");
            }
        }

        [MenuItem(MENU_ROOT + "Bake Now", false, 2)]
        private static void BakeOnly()
        {
            BeginBake();
        }

        [MenuItem(MENU_ROOT + "Clear Baked Data", false, 20)]
        private static void ClearData()
        {
            StaticOcclusionCulling.Clear();
            StaticOcclusionCulling.RemoveCacheFolder();
            Debug.Log("[Occlusion] Cleared baked occlusion data for the open scene(s).");
        }

        [MenuItem(MENU_ROOT + "Open Occlusion Culling Window (set bake params)", false, 21)]
        private static void OpenWindow()
        {
            LogBakeParamRecommendations();
            EditorApplication.ExecuteMenuItem("Window/Rendering/Occlusion Culling");
        }

        // ── Configuration ─────────────────────────────────────────────────────

        /// <summary>
        /// Marks terrains + likely-static mesh renderers as occluder/occludee and ensures a single
        /// Occlusion Area sized to the world bounds. Returns false (with a dialog) if nothing usable
        /// was found. Fully undoable.
        /// </summary>
        private static bool ConfigureStaticsAndArea()
        {
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

            if (terrains.Length == 0 && renderers.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Occlusion Culling",
                    "No Terrain or MeshRenderer found in the open scene. Nothing to configure.",
                    "OK");
                return false;
            }

            var occluderFlags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

            int terrainCount = 0;
            foreach (var terrain in terrains)
            {
                AddStaticFlags(terrain.gameObject, occluderFlags);
                terrainCount++;
            }

            int propCount = 0;
            int skippedCount = 0;
            foreach (var renderer in renderers)
            {
                // Never flag the auto Occlusion Area or moving objects (a moving object cannot be a
                // baked occluder; flagging it static produces wrong culling). Dynamic occludee-only
                // is handled at runtime by Renderer.allowOcclusionWhenDynamic, not this bake.
                if (IsLikelyDynamic(renderer))
                {
                    skippedCount++;
                    continue;
                }

                AddStaticFlags(renderer.gameObject, occluderFlags);
                propCount++;
            }

            if (!TryComputeWorldBounds(terrains, renderers, out var worldBounds))
            {
                EditorUtility.DisplayDialog(
                    "Occlusion Culling",
                    "Could not compute world bounds (no renderable geometry). Statics were flagged, " +
                    "but no Occlusion Area was created.",
                    "OK");
                return false;
            }

            EnsureOcclusionArea(worldBounds);

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"[Occlusion] Configured: {terrainCount} terrain(s) + {propCount} static prop(s) " +
                $"flagged Occluder|Occludee, {skippedCount} dynamic renderer(s) skipped. " +
                $"Occlusion Area '{AREA_OBJECT_NAME}' sized to {worldBounds.size:F0} m. " +
                "NOTE: terrain detail grass/trees are NOT occlusion-culled — use Terrain " +
                "Detail Distance / Tree Distance for those.");

            return true;
        }

        private static void AddStaticFlags(GameObject go, StaticEditorFlags flags)
        {
            var current = GameObjectUtility.GetStaticEditorFlags(go);
            var merged = current | flags;
            if (merged == current)
            {
                return;
            }

            Undo.RegisterCompleteObjectUndo(go, "Set Occlusion Static Flags");
            GameObjectUtility.SetStaticEditorFlags(go, merged);
        }

        /// <summary>
        /// Heuristic: a renderer is treated as dynamic (and skipped) if it or any ancestor carries a
        /// physics body, animator, or character controller — the common markers of moving content.
        /// Reviewable: the configure step logs how many were skipped, and all changes are undoable.
        /// </summary>
        private static bool IsLikelyDynamic(MeshRenderer renderer)
        {
            var go = renderer.gameObject;

            if (go.name == AREA_OBJECT_NAME)
            {
                return true;
            }

            if (renderer.GetComponentInParent<Rigidbody>() != null) return true;
            if (renderer.GetComponentInParent<Animator>() != null) return true;
            if (renderer.GetComponentInParent<CharacterController>() != null) return true;

            return false;
        }

        private static bool TryComputeWorldBounds(
            IReadOnlyList<Terrain> terrains,
            IReadOnlyList<MeshRenderer> renderers,
            out Bounds worldBounds)
        {
            bool has = false;
            worldBounds = new Bounds();

            foreach (var terrain in terrains)
            {
                var data = terrain.terrainData;
                if (data == null)
                {
                    continue;
                }

                // TerrainData.bounds is local; terrains are axis-aligned, so offset by position.
                var local = data.bounds;
                var tb = new Bounds(terrain.transform.position + local.center, local.size);
                Encapsulate(ref worldBounds, ref has, tb);
            }

            foreach (var renderer in renderers)
            {
                if (renderer.gameObject.name == AREA_OBJECT_NAME)
                {
                    continue;
                }

                Encapsulate(ref worldBounds, ref has, renderer.bounds);
            }

            if (!has)
            {
                return false;
            }

            // Pad vertically so the camera's view volume is enclosed.
            var size = worldBounds.size;
            size.y += AREA_VERTICAL_PADDING_M * 2f;
            worldBounds.size = size;
            return true;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool has, Bounds add)
        {
            if (!has)
            {
                bounds = add;
                has = true;
            }
            else
            {
                bounds.Encapsulate(add);
            }
        }

        private static void EnsureOcclusionArea(Bounds worldBounds)
        {
            var existing = GameObject.Find(AREA_OBJECT_NAME);
            OcclusionArea area;

            if (existing != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(existing, "Configure Occlusion Area");
                area = existing.GetComponent<OcclusionArea>();
                if (area == null)
                {
                    area = Undo.AddComponent<OcclusionArea>(existing);
                }
            }
            else
            {
                var go = new GameObject(AREA_OBJECT_NAME);
                Undo.RegisterCreatedObjectUndo(go, "Create Occlusion Area");
                area = Undo.AddComponent<OcclusionArea>(go);
            }

            // Keep the GameObject transform at the bounds centre with unit scale, and express the
            // volume entirely through OcclusionArea.size (local-space) to avoid scale double-counting.
            area.transform.position = worldBounds.center;
            area.transform.rotation = Quaternion.identity;
            area.transform.localScale = Vector3.one;
            area.center = Vector3.zero;
            area.size = worldBounds.size;
        }

        // ── Bake ──────────────────────────────────────────────────────────────

        private static void BeginBake()
        {
            if (StaticOcclusionCulling.isRunning)
            {
                Debug.LogWarning("[Occlusion] A bake is already running.");
                return;
            }

            // Persist first — the PVS asset is linked to the saved scene path.
            EditorSceneManager.SaveOpenScenes();

            LogBakeParamRecommendations();

            if (!StaticOcclusionCulling.Compute())
            {
                Debug.LogError("[Occlusion] Bake failed to start (StaticOcclusionCulling.Compute returned false).");
                return;
            }

            Debug.Log("[Occlusion] Bake started…");
            EditorApplication.update += PollBake;
        }

        private static void PollBake()
        {
            if (StaticOcclusionCulling.isRunning)
            {
                EditorUtility.DisplayProgressBar("Baking Occlusion Culling", "Computing PVS…", 0.5f);
                return;
            }

            EditorApplication.update -= PollBake;
            EditorUtility.ClearProgressBar();

            long bytes = StaticOcclusionCulling.umbraDataSize;
            float mb = bytes / (1024f * 1024f);

            if (bytes > MOBILE_DATA_WARN_BYTES)
            {
                Debug.LogWarning(
                    $"[Occlusion] Bake complete — PVS data = {mb:F2} MB (exceeds the " +
                    $"{MOBILE_DATA_WARN_BYTES / (1024f * 1024f):F0} MB mobile budget). " +
                    "Reduce it: raise Smallest Occluder, raise Smallest Hole, or shrink the " +
                    "Occlusion Area to only where the camera actually travels.");
            }
            else
            {
                Debug.Log($"[Occlusion] Bake complete — PVS data = {mb:F2} MB.");
            }
        }

        private static void LogBakeParamRecommendations()
        {
            Debug.Log(
                "[Occlusion] Recommended MOBILE bake params (set once in " +
                "Window ▸ Rendering ▸ Occlusion Culling ▸ Bake — these are window-only, not scriptable " +
                "in Unity 6000.3): " +
                $"Smallest Occluder = {RECOMMENDED_SMALLEST_OCCLUDER} m, " +
                $"Smallest Hole = {RECOMMENDED_SMALLEST_HOLE} m, " +
                $"Backface Threshold = {RECOMMENDED_BACKFACE_THRESHOLD}.");
        }
    }
}
