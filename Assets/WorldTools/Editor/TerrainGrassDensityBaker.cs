#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WorldTools.Editor
{
    /// <summary>
    /// Bakes a Unity built-in <see cref="Terrain"/> DETAIL layer (what you paint with the Terrain
    /// "Paint Details" brush) into a readable density <see cref="Texture2D"/> that the existing
    /// GrassInteract scatter pipeline consumes as its Density Map.
    ///
    /// This is the HYBRID bridge: the Terrain detail brush becomes the placement source of truth,
    /// while the existing GPU-instanced interactive grass renderer is reused unchanged. (Unity's
    /// built-in Grass/Billboard detail render modes can't take a custom interactive shader, so we
    /// drive placement from the Terrain and render with the project's own grass system.)
    ///
    /// Workflow:
    ///   1. Paint grass with Terrain ▸ Paint Details (a detail texture/mesh layer).
    ///   2. Tools ▸ World ▸ Grass ▸ Terrain Detail → Density Map… → pick the terrain + layer → Bake.
    ///   3. Assign the baked texture to your ScatterLayer's Density Map, bind the ScatterField to the
    ///      same Terrain (TerrainSurfaceSampler) so the field rect == terrain size and UVs align.
    ///
    /// Output orientation matches <see cref="TerrainSurfaceSampler"/>'s UV convention
    /// (u → terrain X, v → terrain Z), so the baked map lines up with ground-snap sampling.
    /// </summary>
    public sealed class TerrainGrassDensityBaker : EditorWindow
    {
        private enum Normalization
        {
            AutoMaxInLayer, // divide by the largest cell value present (default; robust)
            FixedDivisor,   // divide by an explicit value (e.g. detail prototype max density)
        }

        private const int MAX_RECOMMENDED_RESOLUTION = 2048;

        private Terrain? terrain;
        private int detailLayerIndex;
        private Normalization normalization = Normalization.AutoMaxInLayer;
        private float fixedDivisor = 16f;
        private bool flipV;
        private bool disableTerrainDetailAfterBake = true;
        private string outputFolder = "Assets";

        [MenuItem("Tools/World/Grass/Terrain Detail → Density Map…", false, 0)]
        private static void Open()
        {
            var window = GetWindow<TerrainGrassDensityBaker>(true, "Grass Density Baker");
            window.minSize = new Vector2(420f, 260f);
            window.TryDefaultTerrainFromSelection();
            window.Show();
        }

        private void OnEnable()
        {
            this.TryDefaultTerrainFromSelection();
        }

        private void TryDefaultTerrainFromSelection()
        {
            if (this.terrain != null)
            {
                return;
            }

            if (Selection.activeGameObject != null)
            {
                this.terrain = Selection.activeGameObject.GetComponent<Terrain>();
            }

            if (this.terrain == null)
            {
                this.terrain = Object.FindFirstObjectByType<Terrain>();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Converts a Terrain detail (grass) layer into a density Texture2D for the GrassInteract " +
                "scatter pipeline. Paint grass with Terrain ▸ Paint Details first.",
                MessageType.Info);

            this.terrain = (Terrain)EditorGUILayout.ObjectField(
                "Terrain", this.terrain, typeof(Terrain), allowSceneObjects: true);

            TerrainData? data = this.terrain != null ? this.terrain.terrainData : null;
            if (this.terrain == null || data == null)
            {
                EditorGUILayout.HelpBox("Assign a Terrain with a TerrainData asset.", MessageType.Warning);
                return;
            }

            var prototypes = data.detailPrototypes;
            if (prototypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "This Terrain has no detail layers. Add one in Terrain ▸ Paint Details ▸ Edit Details, " +
                    "then paint grass before baking.",
                    MessageType.Warning);
                return;
            }

            var labels = BuildPrototypeLabels(prototypes);
            this.detailLayerIndex = EditorGUILayout.Popup("Detail Layer", this.detailLayerIndex, labels);
            this.detailLayerIndex = Mathf.Clamp(this.detailLayerIndex, 0, prototypes.Length - 1);

            EditorGUILayout.LabelField("Detail Resolution", $"{data.detailWidth} × {data.detailHeight}");

            this.normalization = (Normalization)EditorGUILayout.EnumPopup("Normalization", this.normalization);
            if (this.normalization == Normalization.FixedDivisor)
            {
                this.fixedDivisor = Mathf.Max(1f, EditorGUILayout.FloatField("Divisor", this.fixedDivisor));
            }

            this.flipV = EditorGUILayout.Toggle(
                new GUIContent("Flip V", "Enable only if the baked grass appears mirrored along Z."),
                this.flipV);

            this.disableTerrainDetailAfterBake = EditorGUILayout.Toggle(
                new GUIContent("Disable Terrain Grass Render After Bake",
                    "Sets Terrain Detail Distance = 0 so the Terrain stops drawing its built-in detail " +
                    "grass (the external GrassInteract system renders it instead). Painted detail data " +
                    "is kept. Recommended for the hybrid setup."),
                this.disableTerrainDetailAfterBake);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Output Folder", this.outputFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(70f)))
            {
                string picked = EditorUtility.OpenFolderPanel("Output Folder", this.outputFolder, string.Empty);
                if (!string.IsNullOrEmpty(picked))
                {
                    this.outputFolder = ToProjectRelative(picked) ?? this.outputFolder;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (data.detailWidth > MAX_RECOMMENDED_RESOLUTION || data.detailHeight > MAX_RECOMMENDED_RESOLUTION)
            {
                EditorGUILayout.HelpBox(
                    $"Detail resolution exceeds {MAX_RECOMMENDED_RESOLUTION}. The density map can be large; " +
                    "consider a lower Detail Resolution on the Terrain.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(this.terrain == null))
            {
                if (GUILayout.Button("Bake Density Map", GUILayout.Height(32f)))
                {
                    this.Bake();
                }
            }
        }

        private void Bake()
        {
            Terrain boundTerrain = this.terrain!;
            TerrainData data = boundTerrain.terrainData;

            int width = data.detailWidth;
            int height = data.detailHeight;
            if (width <= 0 || height <= 0)
            {
                EditorUtility.DisplayDialog("Grass Density Baker", "Terrain has zero detail resolution.", "OK");
                return;
            }

            int[,] layer = data.GetDetailLayer(0, 0, width, height, this.detailLayerIndex);

            // The array dims are authoritative: rows = GetLength(0), cols = GetLength(1).
            int rows = layer.GetLength(0);
            int cols = layer.GetLength(1);

            int maxValue = 0;
            for (int r = 0; r < rows; ++r)
            {
                for (int c = 0; c < cols; ++c)
                {
                    if (layer[r, c] > maxValue)
                    {
                        maxValue = layer[r, c];
                    }
                }
            }

            if (maxValue == 0)
            {
                EditorUtility.DisplayDialog(
                    "Grass Density Baker",
                    "The selected detail layer is empty (no painted grass). Paint grass with " +
                    "Terrain ▸ Paint Details before baking.",
                    "OK");
                return;
            }

            float divisor = this.normalization == Normalization.FixedDivisor
                ? this.fixedDivisor
                : maxValue;
            divisor = Mathf.Max(1f, divisor);

            // Texture sized to the detail grid. u → terrain X (column), v → terrain Z (row), matching
            // TerrainSurfaceSampler. Linear (not sRGB) — density is data, not colour.
            var texture = new Texture2D(cols, rows, TextureFormat.RGBA32, mipChain: false, linear: true);
            var pixels = new Color[cols * rows];

            for (int r = 0; r < rows; ++r)
            {
                int destRow = this.flipV ? (rows - 1 - r) : r;
                for (int c = 0; c < cols; ++c)
                {
                    float d = Mathf.Clamp01(layer[r, c] / divisor);
                    pixels[destRow * cols + c] = new Color(d, d, d, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            string folder = string.IsNullOrEmpty(this.outputFolder) ? "Assets" : this.outputFolder;
            string fileName = $"{boundTerrain.name}_GrassDensity_L{this.detailLayerIndex}.png";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}");

            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            ConfigureImporter(assetPath, Mathf.Max(cols, rows));

            // Hybrid setup: stop the Terrain drawing its own detail grass (the external GrassInteract
            // system renders it instead). Done AFTER GetDetailLayer above, so the data was already read.
            // Detail DATA is preserved — only rendering is disabled.
            string detailNote;
            if (this.disableTerrainDetailAfterBake)
            {
                TerrainDetailRenderToggle.SetDetailDistance(boundTerrain, 0f);
                detailNote = " Terrain detail (grass) rendering DISABLED (detailObjectDistance = 0) — " +
                             "data kept; the external grass renders it.";
            }
            else
            {
                detailNote = " Terrain detail rendering left ON — use " +
                             "Tools ▸ World ▸ Grass ▸ Disable Terrain Detail Rendering to turn it off.";
            }

            var baked = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            EditorGUIUtility.PingObject(baked);
            Selection.activeObject = baked;

            Debug.Log(
                $"[GrassDensityBaker] Baked '{assetPath}' ({cols}×{rows}, max cell={maxValue}, " +
                $"divisor={divisor:F1}). Assign it to your ScatterLayer's Density Map, and bind the " +
                "ScatterField to this Terrain so the field rect matches terrain size (UVs align)." +
                detailNote +
                " Reminder: re-bind the kart's GrassInteractor on each relaunch — it self-registers OnEnable.");
        }

        private static void ConfigureImporter(string assetPath, int resolution)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.isReadable = true;            // DensityPlacement reads GetPixelBilinear at build time
            importer.sRGBTexture = false;          // density is linear data
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Mathf.Max(2048, Mathf.NextPowerOfTwo(resolution));
            importer.SaveAndReimport();
        }

        private static string[] BuildPrototypeLabels(DetailPrototype[] prototypes)
        {
            var labels = new string[prototypes.Length];
            for (int i = 0; i < prototypes.Length; ++i)
            {
                var p = prototypes[i];
                string name;
                if (p.usePrototypeMesh && p.prototype != null)
                {
                    name = p.prototype.name;
                }
                else if (p.prototypeTexture != null)
                {
                    name = p.prototypeTexture.name;
                }
                else
                {
                    name = "(empty)";
                }

                labels[i] = $"{i}: {name}";
            }

            return labels;
        }

        private static string? ToProjectRelative(string absolutePath)
        {
            string dataPath = Application.dataPath; // …/<Project>/Assets
            if (absolutePath == dataPath)
            {
                return "Assets";
            }

            string prefix = dataPath + "/";
            if (absolutePath.StartsWith(prefix))
            {
                return "Assets/" + absolutePath.Substring(prefix.Length);
            }

            Debug.LogWarning("[GrassDensityBaker] Output folder must be inside the project's Assets/ folder.");
            return null;
        }
    }
}
