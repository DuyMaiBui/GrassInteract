#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GPUGrass.Editor
{
    /// <summary>
    /// Inspector for <see cref="GpuGrassConfig"/> that adds a Build button to re-bake placement data.
    ///
    /// The button is ENABLED ONLY when a bake-affecting field (placement source, density, scale range,
    /// blade height, slope, altitude band, seed — see <see cref="GpuGrassBakeSignature"/>) differs from
    /// what the baked data was produced with, or a referenced field has never been baked. Render-only
    /// tunables (LOD, wind, bend, shadows, material) never enable it — they need no re-bake.
    ///
    /// Baking needs a Terrain, which lives on the scene's <see cref="GpuGrassController"/>s — so Build
    /// operates on every controller in the open scene(s) whose Config is this asset, re-baking each
    /// (terrain → its bake data) and refreshing the live renderer.
    /// </summary>
    [CustomEditor(typeof(GpuGrassConfig))]
    internal sealed class GpuGrassConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            this.DrawDefaultInspector();

            var config = (GpuGrassConfig)this.target;

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);

                List<GpuGrassController> users = FindControllersUsing(config);
                if (users.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No GpuGrassController in the open scene(s) uses this config. Open the grass scene " +
                        "(or run Tools ▸ GPUGrass ▸ Scene Grass Setup) to bake against a terrain.",
                        MessageType.Info);
                    return;
                }

                var current = GpuGrassBakeSignature.From(config);
                bool dirty = users.Any(c => IsStale(c, current));

                EditorGUILayout.HelpBox(
                    dirty
                        ? "Placement settings changed since the last bake — rebuild to apply."
                        : "Bake is up to date with the current placement settings.",
                    dirty ? MessageType.Warning : MessageType.None);

                using (new EditorGUI.DisabledScope(!dirty))
                {
                    string label = $"Build Bake Data ({users.Count} terrain{(users.Count == 1 ? "" : "s")})";
                    if (GUILayout.Button(label, GUILayout.Height(28)))
                        Rebuild(users, config);
                }
            }
        }

        private static List<GpuGrassController> FindControllersUsing(GpuGrassConfig config) =>
            Object.FindObjectsByType<GpuGrassController>(FindObjectsSortMode.None)
                  .Where(c => c.Config == config)
                  .ToList();

        // Stale iff the referenced bake is missing/empty or was produced with different placement params.
        private static bool IsStale(GpuGrassController c, GpuGrassBakeSignature current) =>
            c.Bake == null || c.Bake.InstanceCount == 0 || c.Bake.BakedSignature != current;

        private static void Rebuild(List<GpuGrassController> users, GpuGrassConfig config)
        {
            int totalBlades = 0, terrains = 0;
            foreach (GpuGrassController c in users)
            {
                if (c.Terrain == null || c.Bake == null)
                    continue;
                // GpuGrassBaker.Bake restamps c.Bake.BakedSignature to From(config), so the button
                // greys out again immediately after this rebuild.
                totalBlades += GpuGrassBaker.Bake(c.Terrain, config, c.Bake);
                EditorUtility.SetDirty(c.Bake);
                c.Rebuild(); // refresh the live renderer from the new bake
                terrains++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GPUGrass] Rebuilt {totalBlades} blade(s) across {terrains} terrain(s) for config '{config.name}'.");
        }
    }
}
