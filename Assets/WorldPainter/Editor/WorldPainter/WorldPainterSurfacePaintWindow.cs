#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Control panel for unified <c>SurfaceLayers</c> authoring + paint-target selection. Add/remove
    /// splat &amp; grass layers, add grass variants, and click a variant to make it the density brush's
    /// target (sets <see cref="WorldPainterState"/> active layer = Meadow + variant index + Paint tool).
    /// Then activate the WorldPainter sculpt tool and paint. Editing per-layer fields (variant textures,
    /// blade mesh, splat albedos) is done on each sub-asset's default inspector via the Select buttons.
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
                if (GUILayout.Button("Add Splat"))   WorldMapAssetLifecycle.AddSplatLayer(map, "Ground");
                if (GUILayout.Button("Add Grass"))   WorldMapAssetLifecycle.AddGrassLayerWithBlades(map, "Grass", 2);
                if (GUILayout.Button("Create Demo")) WorldMapAssetLifecycle.CreateDemoSurfaceLayers(map);
            }
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

                if (sl is SplatLayer splat)
                {
                    if (GUILayout.Button("Edit TerrainLayerSet (assign albedos)"))
                        Selection.activeObject = splat.LayerSet;
                }
                else if (sl is GrassLayer g)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"Variants ({g.Palette.Count}) — click to paint:");
                        if (GUILayout.Button("Add Variant", GUILayout.Width(90f)))
                            WorldMapAssetLifecycle.AddGrassVariant(map, g, $"Variant{(char)('A' + g.PaletteCount)}", true);
                    }

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

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            if (toRemove != null)
                WorldMapAssetLifecycle.RemoveSurfaceLayer(map, toRemove);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Grass: select a variant, activate the WorldPainter sculpt tool, then paint (Paint adds, " +
                "Erase removes); release rebuilds the scatter. Splat: assign albedos to a Splat layer's " +
                "TerrainLayerSet, then paint with the splat tool.", MessageType.None);
        }
    }
}
