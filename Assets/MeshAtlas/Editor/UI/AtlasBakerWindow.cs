using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshAtlas.Editor.UI
{
    /// <summary>
    /// Wizard entry point: select mesh GameObjects, configure atlas size / padding /
    /// channels / output, then Bake. Drives <see cref="AtlasBakePipeline"/> and reports
    /// skipped (UV-out-of-range) meshes + output paths in a log area.
    /// </summary>
    public sealed class AtlasBakerWindow : EditorWindow
    {
        private readonly BakeOptions options = new BakeOptions();
        private string log = string.Empty;
        private Vector2 scroll;

        [MenuItem("Tools/Mesh Atlas/Combine & Bake")]
        private static void Open()
        {
            var window = GetWindow<AtlasBakerWindow>("Mesh Atlas");
            window.minSize = new Vector2(360, 380);
        }

        private void OnGUI()
        {
            var selection = SelectedRoots();
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                selection.Count == 0
                    ? "Select one or more GameObjects with MeshRenderers in the Hierarchy."
                    : $"{selection.Count} selected root object(s).",
                selection.Count == 0 ? MessageType.Info : MessageType.None);

            EditorGUILayout.Space();
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

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            this.options.baseName = EditorGUILayout.TextField("Base Name", this.options.baseName);
            this.options.outputFolder = EditorGUILayout.TextField("Folder", this.options.outputFolder);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(selection.Count == 0))
            {
                if (GUILayout.Button("Bake", GUILayout.Height(32)))
                {
                    this.Bake(selection);
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

        private void Bake(List<GameObject> selection)
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
                result = AtlasBakePipeline.Run(selection, this.options);
            }
            catch (System.Exception e)
            {
                this.log = $"Bake threw: {e.Message}\n{e.StackTrace}";
                Debug.LogException(e);
                return;
            }

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
                this.log = sb.ToString();
                return;
            }

            foreach (var w in result.Warnings)
            {
                sb.AppendLine($"WARN: {w}");
            }
            sb.AppendLine($"Baked {result.CombinedCount} meshes → 1 mesh / 1 material.");
            sb.AppendLine($"Mesh:     {result.Output.MeshPath}");
            sb.AppendLine($"Material: {result.Output.MaterialPath}");
            sb.AppendLine($"Prefab:   {result.Output.PrefabPath}");
            this.log = sb.ToString();

            if (result.Output.Prefab != null)
            {
                EditorGUIUtility.PingObject(result.Output.Prefab);
            }
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
