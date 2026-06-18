using System.Collections.Generic;
using MeshAtlas.Editor.Combine;
using MeshAtlas.Editor.Packing;
using UnityEditor;
using UnityEngine;

namespace MeshAtlas.Editor.UI
{
    /// <summary>
    /// Wizard entry point. Two modes:
    /// <list type="bullet">
    /// <item><b>Bake</b> — select mesh GameObjects, pack their per-material maps into a
    /// generated atlas, remap UVs, and combine into 1 mesh / 1 material.</item>
    /// <item><b>Import</b> — supply a pre-made atlas texture plus a manual sub-rect per
    /// material; UVs are re-aligned into those rects without baking.</item>
    /// </list>
    /// A single mesh with 2+ submesh materials is a valid selection in both modes.
    /// Drives <see cref="AtlasBakePipeline"/> / <see cref="AtlasImportPipeline"/> and reports
    /// skipped (UV-out-of-range) meshes + output paths in a log area.
    /// </summary>
    public sealed class AtlasBakerWindow : EditorWindow
    {
        private readonly BakeOptions options = new BakeOptions();
        private readonly Dictionary<Material, Rect> importRects = new Dictionary<Material, Rect>();
        private int gridCols = 2;
        private int gridRows = 1;
        private string log = string.Empty;
        private Vector2 scroll;

        [MenuItem("Tools/Mesh Atlas/Combine & Bake")]
        private static void Open()
        {
            var window = GetWindow<AtlasBakerWindow>("Mesh Atlas");
            window.minSize = new Vector2(360, 420);
        }

        private void OnGUI()
        {
            var selection = SelectedRoots();
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                selection.Count == 0
                    ? "Select one or more GameObjects with MeshRenderers in the Hierarchy. "
                      + "A single mesh that uses 2+ materials is valid."
                    : $"{selection.Count} selected root object(s).",
                selection.Count == 0 ? MessageType.Info : MessageType.None);

            EditorGUILayout.Space();
            this.options.mode = (AtlasMode)GUILayout.Toolbar(
                (int)this.options.mode, new[] { "Bake Atlas", "Import Atlas" });

            EditorGUILayout.Space();
            if (this.options.mode == AtlasMode.Bake)
            {
                this.DrawBakeOptions();
            }
            else
            {
                this.DrawImportOptions(selection);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            this.options.baseName = EditorGUILayout.TextField("Base Name", this.options.baseName);
            this.options.outputFolder = EditorGUILayout.TextField("Folder", this.options.outputFolder);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(selection.Count == 0))
            {
                var verb = this.options.mode == AtlasMode.Bake ? "Bake" : "Re-align UVs";
                if (GUILayout.Button(verb, GUILayout.Height(32)))
                {
                    this.Run(selection);
                }
            }

            if (!string.IsNullOrEmpty(this.log))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
                this.scroll = EditorGUILayout.BeginScrollView(this.scroll, GUILayout.MinHeight(80));
                EditorGUILayout.TextArea(this.log, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawBakeOptions()
        {
            EditorGUILayout.LabelField("Atlas", EditorStyles.boldLabel);
            this.options.maxAtlasSize = EditorGUILayout.IntPopup("Max Atlas Size",
                this.options.maxAtlasSize,
                new[] { "512", "1024", "2048", "4096", "8192" },
                new[] { 512, 1024, 2048, 4096, 8192 });
            this.options.padding = EditorGUILayout.IntSlider("Padding (px)", this.options.padding, 0, 32);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Channels", EditorStyles.boldLabel);
            this.options.bakeAlbedo = EditorGUILayout.Toggle("Albedo", this.options.bakeAlbedo);
            this.options.bakeNormal = EditorGUILayout.Toggle("Normal", this.options.bakeNormal);
            this.options.bakeMask = EditorGUILayout.Toggle("Metallic/Smoothness", this.options.bakeMask);
            this.options.bakeEmission = EditorGUILayout.Toggle("Emission", this.options.bakeEmission);
        }

        private void DrawImportOptions(List<GameObject> selection)
        {
            EditorGUILayout.LabelField("Imported Atlas Textures", EditorStyles.boldLabel);
            this.options.importedAlbedo = TextureField("Albedo (required)", this.options.importedAlbedo);
            this.options.importedNormal = TextureField("Normal", this.options.importedNormal);
            this.options.importedMask = TextureField("Metallic/Smoothness", this.options.importedMask);
            this.options.importedEmission = TextureField("Emission", this.options.importedEmission);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Per-Material UV Rects (0-1, bottom-left origin)", EditorStyles.boldLabel);
            var materials = SelectionMaterials(selection);
            this.SyncImportRects(materials);

            if (materials.Count == 0)
            {
                EditorGUILayout.HelpBox("No materials found on the selection.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                this.gridCols = Mathf.Max(1, EditorGUILayout.IntField("Cols", this.gridCols));
                this.gridRows = Mathf.Max(1, EditorGUILayout.IntField("Rows", this.gridRows));
                if (GUILayout.Button("Auto-Grid Fill"))
                {
                    this.AutoGridFill(materials);
                }
                if (GUILayout.Button("Suggest"))
                {
                    var g = GridRectLayout.SuggestGrid(materials.Count);
                    this.gridCols = g.x;
                    this.gridRows = g.y;
                    this.AutoGridFill(materials);
                }
            }

            foreach (var mat in materials)
            {
                this.importRects[mat] = EditorGUILayout.RectField(mat.name, this.importRects[mat]);
            }
        }

        private void Run(List<GameObject> selection)
        {
            if (string.IsNullOrWhiteSpace(this.options.baseName)
                || !this.options.outputFolder.StartsWith("Assets"))
            {
                this.log = "Base name must be set and the output folder must be under 'Assets/'.";
                return;
            }

            PipelineResult result;
            try
            {
                if (this.options.mode == AtlasMode.Bake)
                {
                    result = AtlasBakePipeline.Run(selection, this.options);
                }
                else
                {
                    var imported = new[]
                    {
                        this.options.importedAlbedo,
                        this.options.importedNormal,
                        this.options.importedMask,
                        this.options.importedEmission,
                    };
                    // Pass a snapshot keyed only by the selection's materials.
                    var rects = new Dictionary<Material, Rect>();
                    foreach (var mat in SelectionMaterials(selection))
                    {
                        if (this.importRects.TryGetValue(mat, out var r))
                        {
                            rects[mat] = r;
                        }
                    }
                    result = AtlasImportPipeline.Run(selection, rects, imported, this.options);
                }
            }
            catch (System.Exception e)
            {
                this.log = $"Run threw: {e.Message}\n{e.StackTrace}";
                Debug.LogException(e);
                return;
            }

            this.log = FormatResult(result);
            if (result.Success && result.Output != null && result.Output.Prefab != null)
            {
                EditorGUIUtility.PingObject(result.Output.Prefab);
            }
        }

        private static string FormatResult(PipelineResult result)
        {
            var sb = new System.Text.StringBuilder();
            if (result.SkippedMeshes.Count > 0)
            {
                sb.AppendLine($"Skipped {result.SkippedMeshes.Count} mesh(es) with UV0 outside [0,1]:");
                foreach (var name in result.SkippedMeshes)
                {
                    sb.AppendLine($"  • {name}");
                }
            }
            if (!result.Success)
            {
                sb.AppendLine($"FAILED: {result.Error}");
                return sb.ToString();
            }

            foreach (var w in result.Warnings)
            {
                sb.AppendLine($"WARN: {w}");
            }
            sb.AppendLine($"Combined {result.CombinedCount} mesh(es) → 1 mesh / 1 material.");
            sb.AppendLine($"Mesh:     {result.Output.MeshPath}");
            sb.AppendLine($"Material: {result.Output.MaterialPath}");
            sb.AppendLine($"Prefab:   {result.Output.PrefabPath}");
            return sb.ToString();
        }

        /// <summary>Add a default full-texture rect for any new material; drop stale entries so
        /// the dict tracks the current selection.</summary>
        private void SyncImportRects(List<Material> materials)
        {
            foreach (var mat in materials)
            {
                if (!this.importRects.ContainsKey(mat))
                {
                    this.importRects[mat] = new Rect(0f, 0f, 1f, 1f);
                }
            }

            var stale = new List<Material>();
            foreach (var key in this.importRects.Keys)
            {
                if (!materials.Contains(key))
                {
                    stale.Add(key);
                }
            }
            foreach (var key in stale)
            {
                this.importRects.Remove(key);
            }
        }

        private void AutoGridFill(List<Material> materials)
        {
            var rects = GridRectLayout.Compute(materials.Count, this.gridCols, this.gridRows);
            for (var i = 0; i < materials.Count && i < rects.Length; i++)
            {
                this.importRects[materials[i]] = rects[i];
            }
        }

        private static Texture2D TextureField(string label, Texture2D value)
            => (Texture2D)EditorGUILayout.ObjectField(label, value, typeof(Texture2D), false);

        /// <summary>
        /// Unique materials across the selection, derived from the SAME collector the import
        /// pipeline uses — so the rect editor never shows a material on a UV-out-of-range mesh
        /// that the pipeline would silently drop. One source of truth for "which materials".
        /// </summary>
        private static List<Material> SelectionMaterials(List<GameObject> roots)
        {
            var mats = new List<Material>();
            foreach (var clip in RendererCollector.Collect(roots, null))
            {
                if (clip.Materials == null)
                {
                    continue;
                }
                foreach (var m in clip.Materials)
                {
                    if (m != null && !mats.Contains(m))
                    {
                        mats.Add(m);
                    }
                }
            }
            return mats;
        }

        private static List<GameObject> SelectedRoots()
        {
            var roots = new List<GameObject>();
            foreach (var go in Selection.gameObjects)
            {
                if (go != null)
                {
                    roots.Add(go);
                }
            }
            return roots;
        }
    }
}
