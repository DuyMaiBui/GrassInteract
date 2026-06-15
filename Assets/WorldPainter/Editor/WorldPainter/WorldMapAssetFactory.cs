#nullable enable
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Single-entry-point factory for creating a <see cref="WorldMapAsset"/> from the
    /// current scene. Invoked from <see cref="WorldPainterInspector"/> empty-state button.
    ///
    /// Contract (Phase 4 spec):
    ///   1. Save dialog — once — produces the <c>.asset</c> path.
    ///   2. Creates the <see cref="WorldMapAsset"/> on disk via <see cref="AssetDatabase"/>.
    ///   3. Calls <see cref="WorldMapAssetLifecycle.AddTile"/> at (0,0) → first tile.
    ///   4. Finds or creates a <see cref="WorldPainter"/> GameObject in the CURRENT scene.
    ///   5. Assigns <c>map</c> on the component and marks the scene dirty.
    ///   NEVER calls <see cref="EditorSceneManager.NewScene"/> — the current scene is untouched.
    /// </summary>
    public static class WorldMapAssetFactory
    {
        // ── Default save directory ────────────────────────────────────────────

        private const string DEFAULT_DIR  = "Assets";
        private const string DEFAULT_NAME = "WorldMap";
        private const string ASSET_EXT    = "asset";

        // ── Entry point (called by inspector "Create World Map" button) ───────

        /// <summary>
        /// Opens a save dialog, creates the <see cref="WorldMapAsset"/> with its first tile
        /// at (0,0), then finds (or creates) a <see cref="WorldPainter"/> GameObject in the
        /// current scene and assigns the map.
        ///
        /// Returns the newly created map on success, or null if the user cancelled the dialog.
        /// </summary>
        /// <param name="targetPainter">
        /// Optional existing <see cref="WorldPainter"/> component to use.
        /// When null the factory searches the current scene (FindObjectOfType) before
        /// creating a new GameObject.
        /// </param>
        public static WorldMapAsset? CreateAndAssign(WorldPainter? targetPainter = null)
        {
            // ── 1. Save dialog ────────────────────────────────────────────────

            string savePath = EditorUtility.SaveFilePanelInProject(
                title      : "Create World Map",
                defaultName: DEFAULT_NAME,
                extension  : ASSET_EXT,
                message    : "Choose where to save the WorldMapAsset.",
                path       : DEFAULT_DIR);

            if (string.IsNullOrEmpty(savePath))
                return null; // user cancelled

            // Ensure the directory exists.
            string? dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                Directory.CreateDirectory(dir);

            // ── 2. Create the WorldMapAsset on disk ───────────────────────────

            var map = ScriptableObject.CreateInstance<WorldMapAsset>();
            AssetDatabase.CreateAsset(map, savePath);
            AssetDatabase.SaveAssets();

            // ── 3. AddTile at (0,0) ───────────────────────────────────────────

            WorldMapAssetLifecycle.AddTile(map, Vector2Int.zero);

            // ── 4. Find or create WorldPainter in the current scene ───────────

            WorldPainter painter = targetPainter ?? FindOrCreatePainterInCurrentScene();

            // ── 5. Assign map + mark dirty ────────────────────────────────────

            Undo.RecordObject(painter, "Assign WorldMapAsset");
            painter.Map = map;
            EditorUtility.SetDirty(painter);
            EditorSceneManager.MarkSceneDirty(painter.gameObject.scene);

            // Ping asset in project window for discoverability.
            EditorGUIUtility.PingObject(map);
            Selection.activeObject = painter.gameObject;

            return map;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Searches the currently active scene for a <see cref="WorldPainter"/> component.
        /// If none is found, creates a new GameObject named "WorldPainter" and adds the component.
        /// NEVER switches or creates scenes.
        /// </summary>
        private static WorldPainter FindOrCreatePainterInCurrentScene()
        {
            // Search the active scene (doesn't include DontDestroyOnLoad or additive scenes).
            Scene activeScene = SceneManager.GetActiveScene();

            // FindObjectsByType (Unity 2023+) / FindObjectsOfType for older version safety.
#if UNITY_2023_1_OR_NEWER
            var painters = Object.FindObjectsByType<WorldPainter>(FindObjectsSortMode.None);
#else
            var painters = Object.FindObjectsOfType<WorldPainter>();
#endif
            foreach (var p in painters)
            {
                if (p.gameObject.scene == activeScene)
                    return p;
            }

            // None found — create a new GameObject in the active scene.
            var go = new GameObject("WorldPainter");
            SceneManager.MoveGameObjectToScene(go, activeScene);
            Undo.RegisterCreatedObjectUndo(go, "Create WorldPainter GameObject");
            return go.AddComponent<WorldPainter>();
        }
    }
}
