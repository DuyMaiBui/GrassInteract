#nullable enable
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Asset that owns all <see cref="ScatterLayer"/>s, density textures, and
    /// <see cref="BrushStamp"/>s for a terrain-scale scatter field, plus shared GPU resources
    /// (compute shader, indirect material).
    ///
    /// Layers and their density maps are stored as sub-assets. One <c>.asset</c> file = one
    /// complete scatter project. Wind, bend, and render parameters are per-layer (on each
    /// <see cref="ScatterLayer"/> sub-asset directly).
    ///
    /// Create via <c>Assets &gt; Create &gt; GrassInteract &gt; Terrain Scatter Config</c>.
    /// Assign to <see cref="ScatterField.Config"/>; the field drives from <see cref="Layers"/>,
    /// <see cref="CullCompute"/>, and <see cref="IndirectMaterial"/> defined here.
    ///
    /// Note: NO Terrain field here — a Terrain is a scene object and cannot be referenced by a
    /// project asset. The <see cref="ScatterField"/> component keeps the <c>boundTerrain</c>
    /// binding.
    /// </summary>
    [CreateAssetMenu(menuName = "GrassInteract/Terrain Scatter Config", fileName = "TerrainScatterConfig")]
    public sealed class TerrainScatterConfig : ScriptableObject
    {
        // ── GPU Resources ─────────────────────────────────────────────────────

        [TitleGroup("GPU Resources")]
        [Tooltip("The GrassCull compute shader (GrassCull.compute). Required for the GPU indirect tier.")]
        [SerializeField] private ComputeShader? cullCompute;

        [TitleGroup("GPU Resources")]
        [Tooltip("Base material using the GrassInteract/IndirectGrass shader. Required for the GPU tier.")]
        [SerializeField] private Material? indirectMaterial;

        // ── Layers ────────────────────────────────────────────────────────────

        [TabGroup("Main", "Layers")]
        [Tooltip("Ordered list of scatter layers owned by this config. Each layer is built into one " +
                 "engine by the ScatterField that references this config.")]
        [SerializeField] private List<ScatterLayer> layers = new();

        // ── Brushes ───────────────────────────────────────────────────────────

        [TabGroup("Main", "Brushes")]
        [Tooltip("Library of brush stamps available for painting. Stamps are sub-assets of this config.")]
        [SerializeField] private List<BrushStamp> brushStamps = new();

        // ── Public accessors ──────────────────────────────────────────────────

        /// <summary>Read-only view of the scatter layers owned by this config.</summary>
        public IReadOnlyList<ScatterLayer> Layers => this.layers;

        /// <summary>Read-only view of the brush stamps owned by this config.</summary>
        public IReadOnlyList<BrushStamp> BrushStamps => this.brushStamps;

        /// <summary>The GrassCull compute shader. Required for the GPU indirect tier.</summary>
        public ComputeShader? CullCompute => this.cullCompute;

        /// <summary>Base indirect material. Required for the GPU indirect tier.</summary>
        public Material? IndirectMaterial => this.indirectMaterial;

#if UNITY_EDITOR
        // ── Sub-asset CRUD ────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new <see cref="DensityScatterLayer"/> sub-asset plus an owned white-filled
        /// R8 density map sub-asset (512×512), wires them together, appends the layer, and saves.
        ///
        /// <paramref name="defaultMaterial"/> and <paramref name="defaultMesh"/> are optional seeds
        /// provided by the editor (loaded from Editor/Defaults/). Pass null to leave them unset.
        ///
        /// IMPORTANT: this config must already be saved as an asset on disk before calling.
        /// </summary>
        public DensityScatterLayer CreateDensityLayer(string layerName,
            Material? defaultMaterial = null, Mesh? defaultMesh = null)
        {
            this.EnsureSavedAssetPath();

            var layer = ScriptableObject.CreateInstance<DensityScatterLayer>();
            layer.name      = layerName;
            layer.hideFlags = HideFlags.None;
            UnityEditor.AssetDatabase.AddObjectToAsset(layer, this);
            layer.hideFlags = HideFlags.HideInHierarchy;

            // Create white-filled R8 512×512 density texture sub-asset.
            var tex = new Texture2D(
                512, 512,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.None)
            {
                name       = $"Density_{layerName}",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.HideInHierarchy,
            };

            // White-fill (full density everywhere).
            var white = new Color32[512 * 512];
            for (int i = 0; i < white.Length; ++i)
                white[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(white);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            UnityEditor.AssetDatabase.AddObjectToAsset(tex, this);

            this.WireLayerFields(layer, tex, defaultMaterial, defaultMesh);

            this.layers.Add(layer);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
            return layer;
        }

        /// <summary>
        /// Creates a new <see cref="InstanceScatterLayer"/> sub-asset plus an empty
        /// <see cref="AuthoredInstancesData"/> sub-asset, wires them together, appends the layer, and saves.
        ///
        /// <paramref name="defaultMaterial"/> and <paramref name="defaultMesh"/> are optional seeds.
        ///
        /// IMPORTANT: this config must already be saved as an asset on disk before calling.
        /// </summary>
        public InstanceScatterLayer CreateInstanceLayer(string layerName,
            Material? defaultMaterial = null, Mesh? defaultMesh = null)
        {
            this.EnsureSavedAssetPath();

            var layer = ScriptableObject.CreateInstance<InstanceScatterLayer>();
            layer.name      = layerName;
            layer.hideFlags = HideFlags.None;
            UnityEditor.AssetDatabase.AddObjectToAsset(layer, this);
            layer.hideFlags = HideFlags.HideInHierarchy;

            // Create empty AuthoredInstancesData sub-asset.
            var authored = ScriptableObject.CreateInstance<AuthoredInstancesData>();
            authored.name      = $"Authored_{layerName}";
            authored.hideFlags = HideFlags.None;
            UnityEditor.AssetDatabase.AddObjectToAsset(authored, this);
            authored.hideFlags = HideFlags.HideInHierarchy;

            this.WireInstanceLayerFields(layer, authored, defaultMaterial, defaultMesh);

            this.layers.Add(layer);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
            return layer;
        }

        // Legacy single method kept as private delegate for both new methods.
        // Phase C editor calls CreateDensityLayer / CreateInstanceLayer directly.
        private void WireLayerFields(DensityScatterLayer layer, Texture2D tex,
            Material? mat, Mesh? mesh)
        {
            using var so = new UnityEditor.SerializedObject(layer);
            so.FindProperty("densityMap").objectReferenceValue = tex;
            if (mat != null)
                so.FindProperty("material").objectReferenceValue = mat;
            if (mesh != null)
            {
                var lodsProp = so.FindProperty("lods");
                lodsProp.arraySize = 1;
                var lod0 = lodsProp.GetArrayElementAtIndex(0);
                lod0.FindPropertyRelative("mesh").objectReferenceValue = mesh;
                lod0.FindPropertyRelative("maxDistance").floatValue = 30f;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private void WireInstanceLayerFields(InstanceScatterLayer layer,
            AuthoredInstancesData authored, Material? mat, Mesh? mesh)
        {
            using var so = new UnityEditor.SerializedObject(layer);
            so.FindProperty("authoredInstances").objectReferenceValue = authored;
            if (mat != null)
                so.FindProperty("material").objectReferenceValue = mat;
            if (mesh != null)
            {
                var lodsProp = so.FindProperty("lods");
                lodsProp.arraySize = 1;
                var lod0 = lodsProp.GetArrayElementAtIndex(0);
                lod0.FindPropertyRelative("mesh").objectReferenceValue = mesh;
                lod0.FindPropertyRelative("maxDistance").floatValue = 30f;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private void EnsureSavedAssetPath()
        {
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(assetPath)) return;

            throw new System.InvalidOperationException(
                "[TerrainScatterConfig] Save the TerrainScatterConfig asset before creating layer sub-assets.");
        }

        /// <summary>
        /// Removes a <see cref="ScatterLayer"/> and its owned sub-assets (density texture or
        /// AuthoredInstancesData) from the list and from the asset database.
        /// Does nothing if <paramref name="layer"/> is null or not owned by this config.
        /// </summary>
        public void DeleteLayer(ScatterLayer layer)
        {
            if (layer == null || !this.layers.Contains(layer)) return;
            this.layers.Remove(layer);

            // Remove owned density texture (DensityScatterLayer).
            if (layer is DensityScatterLayer densLayer)
            {
                using var dso = new UnityEditor.SerializedObject(densLayer);
                var tex = dso.FindProperty("densityMap").objectReferenceValue as Texture2D;
                if (tex != null &&
                    UnityEditor.AssetDatabase.GetAssetPath(tex) ==
                    UnityEditor.AssetDatabase.GetAssetPath(this))
                {
                    UnityEditor.AssetDatabase.RemoveObjectFromAsset(tex);
                    Object.DestroyImmediate(tex, allowDestroyingAssets: true);
                }
            }
            // Remove owned AuthoredInstancesData (InstanceScatterLayer).
            else if (layer is InstanceScatterLayer instLayer)
            {
                using var iso = new UnityEditor.SerializedObject(instLayer);
                var authored = iso.FindProperty("authoredInstances").objectReferenceValue as AuthoredInstancesData;
                if (authored != null &&
                    UnityEditor.AssetDatabase.GetAssetPath(authored) ==
                    UnityEditor.AssetDatabase.GetAssetPath(this))
                {
                    UnityEditor.AssetDatabase.RemoveObjectFromAsset(authored);
                    Object.DestroyImmediate(authored, allowDestroyingAssets: true);
                }
            }

            UnityEditor.AssetDatabase.RemoveObjectFromAsset(layer);
            Object.DestroyImmediate(layer, allowDestroyingAssets: true);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Creates a new <see cref="BrushStamp"/> sub-asset backed by the provided
        /// <paramref name="shape"/> texture.
        /// </summary>
        public BrushStamp CreateBrushStamp(string stampName, Texture2D shape)
        {
            var stamp = ScriptableObject.CreateInstance<BrushStamp>();
            stamp.name      = stampName;
            stamp.hideFlags = HideFlags.None;
            UnityEditor.AssetDatabase.AddObjectToAsset(stamp, this);
            stamp.hideFlags = HideFlags.HideInHierarchy;
            stamp.SetShape(shape, stampName);
            this.brushStamps.Add(stamp);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
            return stamp;
        }

        /// <summary>
        /// Imports a source PNG/EXR/TGA into a new R8 grayscale <see cref="BrushStamp"/> sub-asset.
        /// </summary>
        public BrushStamp ImportBrushStamp(string sourceAssetPath, string displayName)
        {
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(sourceAssetPath);
            if (source == null)
                throw new System.Exception($"[ImportBrushStamp] Source not found: {sourceAssetPath}");
            if (!source.isReadable)
                throw new System.Exception(
                    $"[ImportBrushStamp] '{source.name}' is not readable. " +
                    "Enable Read/Write in the texture's import settings before importing.");

            int w = source.width, h = source.height;
            var copy = new Texture2D(
                w, h,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.None)
            {
                name       = $"{displayName}_Stamp",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.HideInHierarchy,
            };

            Color[] src = source.GetPixels();
            for (int i = 0; i < src.Length; ++i)
            {
                float g = src[i].grayscale;
                src[i] = new Color(g, g, g, 1f);
            }
            copy.SetPixels(src);
            copy.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            UnityEditor.AssetDatabase.AddObjectToAsset(copy, this);

            var stamp = ScriptableObject.CreateInstance<BrushStamp>();
            stamp.name      = displayName;
            stamp.hideFlags = HideFlags.None;
            UnityEditor.AssetDatabase.AddObjectToAsset(stamp, this);
            stamp.hideFlags = HideFlags.HideInHierarchy;
            stamp.SetShape(copy, displayName);
            this.brushStamps.Add(stamp);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
            return stamp;
        }

        /// <summary>Removes a <see cref="BrushStamp"/> from the library and from the asset database.</summary>
        public void DeleteBrushStamp(BrushStamp stamp)
        {
            if (stamp == null || !this.brushStamps.Contains(stamp)) return;
            this.brushStamps.Remove(stamp);
            UnityEditor.AssetDatabase.RemoveObjectFromAsset(stamp);
            Object.DestroyImmediate(stamp, allowDestroyingAssets: true);
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
        }

        // ── Auto-rebuild on inspector changes ─────────────────────────────────

        private bool rebuildQueued;

        private void OnValidate()
        {
            if (this.rebuildQueued) return;
            this.rebuildQueued = true;
            UnityEditor.EditorApplication.delayCall += this.NotifyDependents;
        }

        private void NotifyDependents()
        {
            this.rebuildQueued = false;
            if (this == null) return;
            ScatterField.RebuildAllReferencingConfig(this);
        }

        [ShowInInspector, TabGroup("Main", "Brushes")]
        [PropertyOrder(1000)]
        private bool BrushesTabImportStub => false;

        [ShowInInspector, TabGroup("Main", "Brushes")]
        [PropertyOrder(1001)]
        [Button("Import Stamp from PNG/EXR...", ButtonSizes.Large)]
        private void ImportStampButton()
        {
            string path = EditorUtility.OpenFilePanel(
                "Import Brush Stamp", Application.dataPath, "png,jpg,jpeg,exr,tga");
            if (string.IsNullOrEmpty(path)) return;
            if (!path.StartsWith(Application.dataPath))
            {
                EditorUtility.DisplayDialog("Import Failed", "Source texture must live under the Assets folder.", "OK");
                return;
            }
            string relative = "Assets" + path.Substring(Application.dataPath.Length).Replace("\\", "/");
            string displayName = System.IO.Path.GetFileNameWithoutExtension(path);
            try { this.ImportBrushStamp(relative, displayName); }
            catch (System.Exception ex) { EditorUtility.DisplayDialog("Import Failed", ex.Message, "OK"); }
        }
#endif
    }
}
