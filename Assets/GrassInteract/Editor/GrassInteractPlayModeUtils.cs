#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// Utility for diagnosing and fixing the Play-mode trample trail bug.
    /// The root cause is <see cref="EditorSceneManager.playModeStartScene"/> being set to a
    /// scene that does not contain the grass field — so pressing Play loads the wrong scene.
    /// </summary>
    internal static class GrassInteractPlayModeUtils
    {
        private const string MenuBase = "Tools/GrassInteract/";

        /// <summary>
        /// Clears the play-mode start scene override so Unity always plays the currently-open scene.
        /// Invoke from the menu or run automatically on domain reload if a bad scene is detected.
        /// </summary>
        [MenuItem(MenuBase + "Clear Play-Mode Start Scene")]
        internal static void ClearPlayModeStartScene()
        {
            SceneAsset? current = EditorSceneManager.playModeStartScene;
            if (current == null)
            {
                Debug.Log("[GrassInteract] playModeStartScene is already null (plays current scene). Nothing to do.");
                return;
            }

            string path = AssetDatabase.GetAssetPath(current);
            EditorSceneManager.playModeStartScene = null;
            Debug.Log($"[GrassInteract] Cleared playModeStartScene (was '{path}'). Press Play to use the currently-open scene.");
        }

        [MenuItem(MenuBase + "Log Play-Mode Start Scene")]
        internal static void LogPlayModeStartScene()
        {
            SceneAsset? scene = EditorSceneManager.playModeStartScene;
            if (scene == null)
            {
                Debug.Log("[GrassInteract] playModeStartScene = null → Play loads the currently-open scene (correct).");
            }
            else
            {
                string path = AssetDatabase.GetAssetPath(scene);
                Debug.LogWarning($"[GrassInteract] playModeStartScene = '{path}' → Play will load THIS scene instead of the one you have open! " +
                                 $"Run 'Tools/GrassInteract/Clear Play-Mode Start Scene' to fix.");
            }
        }

        /// <summary>
        /// Auto-runs once after every domain reload in the editor. If <see cref="EditorSceneManager.playModeStartScene"/>
        /// points to a non-grass scene, log a prominent warning so the developer is alerted immediately.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void CheckOnLoad()
        {
            SceneAsset? scene = EditorSceneManager.playModeStartScene;
            if (scene == null)
                return;

            string path = AssetDatabase.GetAssetPath(scene);
            // Warn if the override scene doesn't look like the grass demo
            if (!path.Contains("GrassInteractDemo"))
            {
                Debug.LogWarning($"[GrassInteract] playModeStartScene is set to '{path}'. " +
                                 $"This means pressing Play will NOT load your currently-open grass demo scene! " +
                                 $"Run 'Tools/GrassInteract/Clear Play-Mode Start Scene' to fix, or check 'Tools/GrassInteract/Log Play-Mode Start Scene'.");
            }
        }
    }
}
