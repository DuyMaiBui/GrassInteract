#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Project-wide shared library of <see cref="BrushStamp"/>s, usable by any
    /// <see cref="TerrainScatterConfig"/> regardless of which config is active.
    /// Stamps are stored as sub-assets of this library asset — exactly mirroring
    /// how per-config stamps are sub-assets of <see cref="TerrainScatterConfig"/>.
    ///
    /// Do NOT create this asset manually via <c>Assets &gt; Create</c>.
    /// Access the single project instance through <see cref="ScatterBrushLibraryProvider.Library"/>.
    /// All mutating operations are wrapped in Undo so they are reversible.
    /// </summary>
    public sealed class ScatterBrushLibrary : ScriptableObject
    {
        [Tooltip("Shared brush stamps stored as sub-assets of this library. " +
                 "Use the Scatter Studio brush panel to add, rename, or remove stamps.")]
        [SerializeField] private List<BrushStamp> stamps = new();

        // ── Public read-only view ─────────────────────────────────────────────

        /// <summary>Read-only view of all stamps in this library.</summary>
        public IReadOnlyList<BrushStamp> Stamps => this.stamps;

        // ── Mutating API (all Undo-wrapped) ───────────────────────────────────

        /// <summary>
        /// Creates a new <see cref="BrushStamp"/> sub-asset and appends it to the library.
        /// If <paramref name="shape"/> is provided its import settings are forced to R8 /
        /// readable / uncompressed before the stamp references it.
        /// </summary>
        /// <param name="shape">Optional grayscale stamp texture. May be null for a blank stamp.</param>
        /// <returns>The newly created <see cref="BrushStamp"/> sub-asset.</returns>
        public BrushStamp AddStamp(Texture2D? shape)
        {
            if (shape != null)
                ForceR8ReadableUncompressed(shape);

            Undo.RegisterCompleteObjectUndo(this, "Add Brush Stamp");

            var stamp = ScriptableObject.CreateInstance<BrushStamp>();
            stamp.name = shape != null ? shape.name : "Stamp";

            Undo.RegisterCreatedObjectUndo(stamp, "Add Brush Stamp");

            AssetDatabase.AddObjectToAsset(stamp, this);

            // Assign the shape field via SerializedObject so the change is tracked by the
            // serialisation system and shows up in the Inspector immediately.
            var so = new SerializedObject(stamp);
            SerializedProperty shapeProp = so.FindProperty("shape");
            if (shapeProp != null)
            {
                shapeProp.objectReferenceValue = shape;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            this.stamps.Add(stamp);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();

            return stamp;
        }

        /// <summary>Removes the stamp at <paramref name="index"/> and destroys its sub-asset.</summary>
        /// <param name="index">Zero-based index into <see cref="Stamps"/>.</param>
        public void Remove(int index)
        {
            if (index < 0 || index >= this.stamps.Count)
            {
                Debug.LogWarning($"[ScatterBrushLibrary] Remove: index {index} is out of range (count={this.stamps.Count}).");
                return;
            }

            Undo.RegisterCompleteObjectUndo(this, "Remove Brush Stamp");

            BrushStamp stamp = this.stamps[index];
            this.stamps.RemoveAt(index);

            // Destroy the sub-asset. Undo.DestroyObjectImmediate records the destroy so
            // Ctrl+Z restores both the sub-asset and the list entry.
            Undo.DestroyObjectImmediate(stamp);

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Renames the stamp at <paramref name="index"/> to <paramref name="newName"/>.</summary>
        /// <param name="index">Zero-based index into <see cref="Stamps"/>.</param>
        /// <param name="newName">New display name. Must not be null or empty.</param>
        public void Rename(int index, string newName)
        {
            if (index < 0 || index >= this.stamps.Count)
            {
                Debug.LogWarning($"[ScatterBrushLibrary] Rename: index {index} is out of range (count={this.stamps.Count}).");
                return;
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                Debug.LogWarning("[ScatterBrushLibrary] Rename: newName must not be null or empty.");
                return;
            }

            BrushStamp stamp = this.stamps[index];

            Undo.RegisterCompleteObjectUndo(stamp, "Rename Brush Stamp");

            // BrushStamp.DisplayName falls back to ScriptableObject.name when the
            // serialised displayName field is empty — set both so they stay in sync.
            var so = new SerializedObject(stamp);
            SerializedProperty nameProp = so.FindProperty("displayName");
            if (nameProp != null)
            {
                nameProp.stringValue = newName;
                so.ApplyModifiedProperties();
            }

            stamp.name = newName;
            EditorUtility.SetDirty(stamp);
            AssetDatabase.SaveAssets();
        }

        // ── Texture import helpers ────────────────────────────────────────────

        /// <summary>
        /// Forces a texture asset's import settings to: TextureType=Default, format=R8,
        /// isReadable=true, compression=None, then re-imports it.
        /// Logs a clear error and returns without throwing if the importer cannot be found.
        /// </summary>
        private static void ForceR8ReadableUncompressed(Texture2D texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"[ScatterBrushLibrary] ForceR8ReadableUncompressed: texture '{texture.name}' " +
                               "has no asset path — it may be a runtime-created texture. " +
                               "Import settings cannot be applied.");
                return;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[ScatterBrushLibrary] ForceR8ReadableUncompressed: no TextureImporter " +
                               $"found at path '{path}'. Import settings not applied to '{texture.name}'.");
                return;
            }

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                dirty = true;
            }

            if (!importer.isReadable)
            {
                importer.isReadable = true;
                dirty = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            // Force single-channel R8 via the platform-default texture format override.
            TextureImporterPlatformSettings platformSettings = importer.GetDefaultPlatformTextureSettings();
            if (platformSettings.format != TextureImporterFormat.R8)
            {
                platformSettings.overridden = true;
                platformSettings.format = TextureImporterFormat.R8;
                importer.SetPlatformTextureSettings(platformSettings);
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(importer);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }
    }
}
