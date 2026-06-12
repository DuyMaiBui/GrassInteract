#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Paint-target selector for unified <c>SurfaceLayers</c> grass variants. Lists the active
    /// painter's GrassLayers + their variants; clicking one makes it the density brush's target
    /// (sets <see cref="WorldPainterState"/> active layer = Meadow + the variant index, and selects
    /// the Paint tool). Then activate the WorldPainter sculpt tool and paint — the stroke writes to
    /// that variant's density map and rebuilds the scatter on release.
    ///
    /// Stop-gap until the SurfaceLayers inspector cards land (plan Phase 4).
    /// </summary>
    internal sealed class WorldPainterSurfacePaintWindow : EditorWindow
    {
        [MenuItem("Tools/WorldPainter/Surface Layers/Paint Target Window")]
        private static void Open()
        {
            var w = GetWindow<WorldPainterSurfacePaintWindow>("WP Surface Paint");
            w.minSize = new Vector2(260f, 160f);
        }

        private void OnInspectorUpdate() => this.Repaint();

        private void OnGUI()
        {
            WorldPainter? painter = WorldPainterState.ActivePainter;
            if (painter == null)
            {
                EditorGUILayout.HelpBox("Select a WorldPainter (it registers as the active painter).",
                    MessageType.Info);
                return;
            }

            WorldMapAsset? map = painter.Map;
            if (map == null)
            {
                EditorGUILayout.HelpBox("The painter's WorldMapAsset is not assigned.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Grass variants — click to set the active paint target",
                EditorStyles.boldLabel);

            bool any = false;
            foreach (var sl in map.SurfaceLayers)
            {
                if (sl is not GrassLayer g) continue;
                any = true;

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(g.DisplayName, EditorStyles.miniBoldLabel);

                for (int i = 0; i < g.Palette.Count; i++)
                {
                    bool active =
                        WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Meadow &&
                        WorldPainterState.ActiveLayerId == g.name &&
                        WorldPainterState.ActiveGrassVariantIndex == i;

                    string dot   = active ? "● " : "○ ";
                    string vname = string.IsNullOrEmpty(g.Palette[i].name) ? $"Variant {i}" : g.Palette[i].name;
                    if (GUILayout.Button($"  {dot}{vname}"))
                    {
                        WorldPainterState.ActiveGrassVariantIndex = i;
                        WorldPainterState.SetActiveLayer(g.name, WorldPainterState.PaintLayerKind.Meadow);
                        WorldPainterState.SetActiveBrushTool("density.paint");
                    }
                }
            }

            if (!any)
            {
                EditorGUILayout.HelpBox(
                    "No GrassLayers in SurfaceLayers. Add one via " +
                    "Tools/WorldPainter/Surface Layers/Add Grass Layer.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Activate the WorldPainter sculpt tool, then paint. The selected variant's density map " +
                "receives the stroke (Paint adds, Erase removes); release rebuilds the scatter.",
                MessageType.None);
        }
    }
}
