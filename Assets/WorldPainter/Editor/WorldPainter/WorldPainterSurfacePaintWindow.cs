#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Control panel for unified <c>SurfaceLayers</c> authoring + paint-target selection. Add/remove
    /// splat &amp; grass layers, and click a grass layer row to make it the density brush's target
    /// (sets <see cref="WorldPainterState"/> active layer = Meadow + Paint tool). Then activate the
    /// WorldPainter sculpt tool and paint. Editing per-layer fields (blade mesh, splat albedos) is done
    /// on each sub-asset's default inspector via the Select buttons.
    /// </summary>
    internal sealed class WorldPainterSurfacePaintWindow : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("Tools/WorldPainter/Surface Layers/Control Panel")]
        private static void Open()
        {
            var w = GetWindow<WorldPainterSurfacePaintWindow>("WP Surface Layers");
            w.minSize = new Vector2(300f, 220f);
        }

        private void OnInspectorUpdate() => this.Repaint();

        private void OnGUI()
        {
            WorldPainter? painter = WorldPainterState.ActivePainter;
            if (painter == null)
            {
                EditorGUILayout.HelpBox("Select a WorldPainter in the scene (it registers as the active painter).",
                    MessageType.Info);
                return;
            }

            WorldMapAsset? map = painter.Map;
            if (map == null)
            {
                EditorGUILayout.HelpBox("The painter's WorldMapAsset is not assigned.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Grass"))   WorldMapAssetLifecycle.AddGrassLayerWithBlades(map, "Grass");
                if (GUILayout.Button("Create Demo")) WorldMapAssetLifecycle.CreateDemoSurfaceLayers(map);
            }
            EditorGUILayout.HelpBox(
                "Splat painting is now driven by the WorldMap TerrainPalette: open the WorldPainter " +
                "inspector and use the TerrainLayer palette strip in the BrushDock.",
                MessageType.None);
            EditorGUILayout.Space();

            this.scroll = EditorGUILayout.BeginScrollView(this.scroll);

            WorldPainterLayer? toRemove = null;
            foreach (var sl in map.SurfaceLayers)
            {
                if (sl == null) continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"[{sl.Kind}] {sl.DisplayName}", EditorStyles.boldLabel);
                    if (GUILayout.Button("Select", GUILayout.Width(58f))) Selection.activeObject = sl;
                    if (GUILayout.Button("Remove", GUILayout.Width(62f))) toRemove = sl;
                }

                if (sl is GrassLayer g)
                {
                    bool active =
                        WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Meadow &&
                        WorldPainterState.ActiveLayerId == g.name;

                    string dot = active ? "● " : "○ ";
                    if (GUILayout.Button($"  {dot}{g.DisplayName} — click to paint"))
                    {
                        WorldPainterState.SetActiveLayer(g.name, WorldPainterState.PaintLayerKind.Meadow);
                        WorldPainterState.SetActiveBrushTool("density.paint");
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            if (toRemove != null)
                WorldMapAssetLifecycle.RemoveSurfaceLayer(map, toRemove);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Grass: click a grass layer row to select it, then paint (Paint adds, Erase removes); " +
                "release rebuilds the scatter. " +
                "Splat: use the BrushDock TerrainLayer palette strip in the WorldPainter inspector.",
                MessageType.None);
        }
    }
}
