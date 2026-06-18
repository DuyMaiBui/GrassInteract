using System.IO;
using MeshAtlas.Editor.Baking;
using UnityEditor;
using UnityEngine;

namespace MeshAtlas.Editor.Output
{
    /// <summary>Paths + objects produced by a write. Null entries = channel not baked.</summary>
    public sealed class AtlasWriteResult
    {
        public Mesh Mesh { get; set; }
        public Material Material { get; set; }
        public GameObject Prefab { get; set; }
        public string MeshPath { get; set; }
        public string MaterialPath { get; set; }
        public string PrefabPath { get; set; }
    }

    /// <summary>
    /// Persists a bake: writes the combined mesh, the four atlas PNGs (with correct
    /// per-channel importer settings), the URP-Lit material, and a drop-in prefab.
    /// </summary>
    public static class AtlasAssetWriter
    {
        /// <summary>Bake-mode write: encode the freshly baked channel atlases to PNGs, then
        /// create the combined mesh, URP-Lit material, and prefab.</summary>
        public static AtlasWriteResult Write(Texture2D[] atlasesByChannel, Mesh combinedMesh, string folder, string baseName)
        {
            EnsureFolder(folder);
            var imported = new Texture2D[BakeChannelInfo.COUNT];
            for (var c = 0; c < BakeChannelInfo.COUNT; c++)
            {
                if (atlasesByChannel != null && atlasesByChannel[c] != null)
                {
                    imported[c] = WriteTexture(atlasesByChannel[c], (BakeChannel)c, folder, baseName);
                }
            }
            return WriteMeshMaterialPrefab(imported, combinedMesh, folder, baseName);
        }

        /// <summary>Import-mode write: the channel textures already exist as project assets, so
        /// they are referenced directly — no PNG encode, no importer reconfiguration. Only the
        /// combined mesh, material, and prefab are created.</summary>
        public static AtlasWriteResult WriteExisting(Texture2D[] existingByChannel, Mesh combinedMesh, string folder, string baseName)
        {
            EnsureFolder(folder);
            var imported = new Texture2D[BakeChannelInfo.COUNT];
            if (existingByChannel != null)
            {
                for (var c = 0; c < BakeChannelInfo.COUNT && c < existingByChannel.Length; c++)
                {
                    imported[c] = existingByChannel[c];
                }
            }
            return WriteMeshMaterialPrefab(imported, combinedMesh, folder, baseName);
        }

        private static AtlasWriteResult WriteMeshMaterialPrefab(Texture2D[] imported, Mesh combinedMesh, string folder, string baseName)
        {
            var meshPath = $"{folder}/{baseName}_Mesh.asset";
            AssetDatabase.CreateAsset(combinedMesh, meshPath);

            var material = UrpLitMaterialFactory.Create(imported, $"{baseName}_Material");
            var materialPath = $"{folder}/{baseName}_Material.mat";
            if (material != null)
            {
                AssetDatabase.CreateAsset(material, materialPath);
            }

            var prefabPath = $"{folder}/{baseName}.prefab";
            var go = new GameObject(baseName);
            go.AddComponent<MeshFilter>().sharedMesh = combinedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new AtlasWriteResult
            {
                Mesh = combinedMesh,
                Material = material,
                Prefab = prefab,
                MeshPath = meshPath,
                MaterialPath = materialPath,
                PrefabPath = prefabPath,
            };
        }

        private static Texture2D WriteTexture(Texture2D tex, BakeChannel channel, string folder, string baseName)
        {
            var path = $"{folder}/{baseName}_{channel}.png";
            var png = tex.EncodeToPNG();
            if (png == null || png.Length == 0)
            {
                Debug.LogError($"[MeshAtlas] Failed to encode {channel} atlas to PNG (texture not readable?).");
                return null;
            }
            File.WriteAllBytes(Path.GetFullPath(path), png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                if (channel == BakeChannel.Normal)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                }
                else
                {
                    importer.textureType = TextureImporterType.Default;
                    // Albedo + Emission are color (sRGB); Mask is data (linear).
                    importer.sRGBTexture = !BakeChannelInfo.IsLinear(channel);
                }
                importer.mipmapEnabled = true; // dilation makes mips bleed-safe
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            var parts = folder.Split('/');
            var current = parts[0]; // "Assets"
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
