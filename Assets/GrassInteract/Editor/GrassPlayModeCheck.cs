#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    [InitializeOnLoad]
    internal static class GrassPlayModeCheck
    {
        static GrassPlayModeCheck()
        {
            var scene = EditorSceneManager.playModeStartScene;
            if (scene == null)
                Debug.Log("[GrassPlayModeCheck] playModeStartScene = NULL (correct — will play current scene)");
            else
            {
                string path = AssetDatabase.GetAssetPath(scene);
                Debug.LogWarning($"[GrassPlayModeCheck] playModeStartScene = '{path}' — WRONG SCENE BUG CONFIRMED");
                EditorSceneManager.playModeStartScene = null;
                Debug.Log("[GrassPlayModeCheck] playModeStartScene cleared to null");
            }
        }
    }
}
