#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WorldTools.Editor
{
    /// <summary>
    /// Disables (or re-enables) a Unity built-in <see cref="Terrain"/>'s DETAIL (grass) RENDERING while
    /// leaving the detail DATA intact. This is the switch for the hybrid setup: the Terrain renders the
    /// ground, the painted detail/grass layer stays as the placement source of truth (read by
    /// <see cref="TerrainGrassDensityBaker"/> / <c>TerrainData.GetDetailLayer</c>), and the external
    /// GrassInteract system renders the interactive grass — so the Terrain must NOT also draw its own
    /// billboard/detail grass on top.
    ///
    /// Mechanism: <c>Terrain.detailObjectDistance = 0</c> stops detail rendering without touching the
    /// painted detail layers in <c>TerrainData</c> (trees, controlled by <c>treeDistance</c>, are
    /// unaffected). The value serializes on the Terrain component, so it persists into builds — no
    /// runtime component needed. Operates on the SELECTED terrains, or every terrain in the open scene
    /// when none are selected. Fully undoable.
    /// </summary>
    public static class TerrainDetailRenderToggle
    {
        private const float DISABLED_DISTANCE = 0f;

        // Unity's default Terrain "Detail Distance". Re-enable restores this (not necessarily the
        // project's prior custom value) — adjust per terrain afterward if you had a non-default range.
        private const float DEFAULT_DISTANCE = 80f;

        [MenuItem("Tools/World/Grass/Disable Terrain Detail Rendering (keep data)", false, 20)]
        private static void DisableDetailRendering()
        {
            Apply(DISABLED_DISTANCE, "Disable Terrain Detail Rendering");
        }

        [MenuItem("Tools/World/Grass/Re-enable Terrain Detail Rendering", false, 21)]
        private static void EnableDetailRendering()
        {
            Apply(DEFAULT_DISTANCE, "Re-enable Terrain Detail Rendering");
        }

        /// <summary>
        /// Sets a single terrain's detail render distance (0 = off). Undoable + marks the terrain dirty so
        /// the change persists. Shared entry point used by <see cref="TerrainGrassDensityBaker"/>.
        /// </summary>
        internal static void SetDetailDistance(Terrain terrain, float distance)
        {
            if (terrain == null)
            {
                return;
            }

            Undo.RecordObject(terrain, "Set Terrain Detail Distance");
            terrain.detailObjectDistance = distance;
            EditorUtility.SetDirty(terrain);
        }

        private static void Apply(float distance, string undoLabel)
        {
            Terrain[] terrains = GetTargetTerrains();
            if (terrains.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Terrain Detail Rendering",
                    "No Terrain selected or found in the open scene.",
                    "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoLabel);

            foreach (Terrain terrain in terrains)
            {
                SetDetailDistance(terrain, distance);
            }

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            bool disabling = distance <= 0f;
            Debug.Log(
                $"[TerrainDetailRenderToggle] {(disabling ? "Disabled" : "Re-enabled")} detail (grass) " +
                $"rendering on {terrains.Length} terrain(s) (detailObjectDistance = {distance}). " +
                "Painted detail data is unchanged" +
                (disabling ? " — still readable by the Grass Density Baker." : "."));
        }

        private static Terrain[] GetTargetTerrains()
        {
            var selected = Selection.GetFiltered<Terrain>(SelectionMode.Editable | SelectionMode.ExcludePrefab);
            if (selected.Length > 0)
            {
                return selected;
            }

            return Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        }
    }
}
