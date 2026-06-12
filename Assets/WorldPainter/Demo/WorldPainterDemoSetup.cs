#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldPainter;
using WorldPainter.Editor;

namespace WorldPainter.Demo
{
    /// <summary>
    /// P9 demo setup: builds the WorldPainterDemo scene programmatically.
    /// Invoked via Tools/WorldPainter/P9 Demo Setup.
    /// </summary>
    public static class WorldPainterDemoSetup
    {
        private const string MAP_PATH    = "Assets/WorldPainter/Demo/DemoWorldMap.asset";
        private const string OUTPUT_PATH = "Assets/WorldPainter/Demo/Baked";

        [MenuItem("Tools/WorldPainter/P9 Demo Setup")]
        public static void RunSetup()
        {
            // ── 1. Ensure demo scene is active ────────────────────────────────
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Debug.Log($"[P9] Active scene: '{activeScene.name}' ({activeScene.path})");

            // ── 2. Create WorldMapAsset on disk (idempotent) ──────────────────
            if (AssetDatabase.LoadAssetAtPath<WorldMapAsset>(MAP_PATH) != null)
            {
                AssetDatabase.DeleteAsset(MAP_PATH);
                AssetDatabase.Refresh();
            }

            var map = ScriptableObject.CreateInstance<WorldMapAsset>();
            AssetDatabase.CreateAsset(map, MAP_PATH);
            AssetDatabase.SaveAssets();
            Debug.Log($"[P9] Created WorldMapAsset at '{MAP_PATH}'");

            // ── 3. Add 2×2 tile set + one negative-coord tile ─────────────────
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int(0,  0));
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int(1,  0));
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int(0,  1));
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int(1,  1));
            WorldMapAssetLifecycle.AddTile(map, new Vector2Int(-1, 0));  // negative coord
            Debug.Log($"[P9] Tile count after 2×2 + (-1,0): {map.TileCount}");

            // ── 4. Add scatter layers (density + instance) ────────────────────
            var meadowLayer = WorldMapAssetLifecycle.AddDensityLayer(map, "Meadow");
            var propLayer   = WorldMapAssetLifecycle.AddInstanceLayer(map, "Props");
            Debug.Log($"[P9] Layer count: {map.Layers.Count} (Meadow + Props)");

            // ── 5. Verify per-tile density channel allocation ──────────────────
            int tilesMissingChannel = 0;
            foreach (var tile in map.EnumerateTiles())
            {
                var ch = tile.GetDensityChannel(meadowLayer.name);
                if (ch == null || ch.densityData == null)
                {
                    Debug.LogError($"[P9] FAIL: tile '{tile.name}' missing density channel for '{meadowLayer.name}'");
                    tilesMissingChannel++;
                }
                else
                {
                    Debug.Log($"[P9] tile '{tile.name}' has density channel: densityData.Length={ch.densityData.Length}");
                }
            }
            Debug.Log($"[P9] Channel verification: {(tilesMissingChannel == 0 ? "PASS" : $"FAIL ({tilesMissingChannel} missing)")}");

            // ── 6. Find or create WorldPainter in the current scene ───────────
            WorldPainter? painter = null;
#if UNITY_2023_1_OR_NEWER
            var painters = Object.FindObjectsByType<WorldPainter>(FindObjectsSortMode.None);
#else
            var painters = Object.FindObjectsOfType<WorldPainter>();
#endif
            foreach (var p in painters)
            {
                if (p.gameObject.scene == activeScene)
                {
                    painter = p;
                    break;
                }
            }

            if (painter == null)
            {
                var go = new GameObject("WorldPainter");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, activeScene);
                painter = go.AddComponent<WorldPainter>();
                Debug.Log($"[P9] Created WorldPainter GameObject in scene '{activeScene.name}'");
            }
            else
            {
                Debug.Log($"[P9] Found existing WorldPainter in scene: '{painter.name}'");
            }

            // ── 7. Assign map and add a splat layer ───────────────────────────
            painter.Map = map;
            painter.AddSplatLayer(new SplatLayerDef { name = "Grass", tiling = new Vector2(1f, 1f) });
            EditorUtility.SetDirty(painter);
            EditorSceneManager.MarkSceneDirty(activeScene);
            Debug.Log($"[P9] Assigned DemoWorldMap to WorldPainter; splatLayers={painter.SplatLayers.Count}");

            // ── 8. Bake via WorldMapBaker ─────────────────────────────────────
            var manifest = WorldMapBaker.BakeAll(map, OUTPUT_PATH);
            Debug.Log($"[P9] Bake complete: {manifest.Count} tile(s) baked to '{OUTPUT_PATH}'");

            // ── 9. Save scene ─────────────────────────────────────────────────
            EditorSceneManager.SaveScene(activeScene);

            // ── Summary ───────────────────────────────────────────────────────
            Debug.Log(
                $"[P9] SETUP COMPLETE. " +
                $"tiles={map.TileCount}, layers={map.Layers.Count}, " +
                $"baked={manifest.Count}, " +
                $"scene='{activeScene.name}', painter='{painter.name}', " +
                $"painter.Map={(painter.Map != null ? painter.Map.name : "NULL")}, " +
                $"channelErrors={tilesMissingChannel}");
        }
    }
}
