#nullable enable
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Detail card shown when a Splat layer row is selected in the layer stack.
    /// Displays a cached <see cref="AssetPreview"/> albedo thumbnail, a tiling
    /// Vector2 field, and reports the blend channel index.
    ///
    /// Bound to the active splat layer's <see cref="SerializedProperty"/> so all
    /// edits flow through the Unity Undo system via <see cref="SerializedObject.ApplyModifiedProperties"/>.
    ///
    /// Design §4.1 — Phase 2 per-layer card.
    /// </summary>
    internal sealed class WorldPainterSplatLayerCard
    {
        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly SerializedObject serializedObject;

        // ── Ctor ──────────────────────────────────────────────────────────────

        public WorldPainterSplatLayerCard(SerializedObject so)
        {
            this.serializedObject = so;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds and returns the detail card for the splat layer at
        /// <paramref name="splatIndex"/> (0-based channel index).
        /// Returns null when the index is out of range.
        /// </summary>
        public VisualElement? Build(int splatIndex)
        {
            this.serializedObject.Update();

            var splatLayersProp = this.serializedObject.FindProperty("splatLayers");
            if (splatLayersProp == null || splatIndex < 0 ||
                splatIndex >= splatLayersProp.arraySize)
                return null;

            var layerProp  = splatLayersProp.GetArrayElementAtIndex(splatIndex);
            var nameProp   = layerProp.FindPropertyRelative("name");
            var albedoProp = layerProp.FindPropertyRelative("albedo");
            var tilingProp = layerProp.FindPropertyRelative("tiling");

            var card = new VisualElement();
            card.AddToClassList("wp-splat-card");

            // ── Albedo thumbnail ──────────────────────────────────────────────

            Texture2D? albedo = albedoProp?.objectReferenceValue as Texture2D;
            Texture2D? thumb  = (albedo != null)
                ? (AssetPreview.GetAssetPreview(albedo) ?? albedo)
                : null;

            var thumbEl = new VisualElement();
            thumbEl.AddToClassList("wp-splat-card-thumb");
            if (thumb != null)
                thumbEl.style.backgroundImage = new StyleBackground(thumb);
            card.Add(thumbEl);

            // ── Channel badge ─────────────────────────────────────────────────

            string[] channelNames = { "R (ch 0)", "G (ch 1)", "B (ch 2)", "A (ch 3)" };
            string channelLabel = (splatIndex < channelNames.Length)
                ? channelNames[splatIndex] : $"ch {splatIndex}";
            var channelBadge = new Label($"Blend channel: {channelLabel}");
            channelBadge.AddToClassList("wp-layer-name");
            card.Add(channelBadge);

            // ── Name field ────────────────────────────────────────────────────

            if (nameProp != null)
            {
                var nameField = new TextField("Name") { value = nameProp.stringValue };
                nameField.RegisterValueChangedCallback(e =>
                {
                    nameProp.stringValue = e.newValue;
                    this.serializedObject.ApplyModifiedProperties();
                });
                card.Add(nameField);
            }

            // ── Albedo object field ───────────────────────────────────────────

            if (albedoProp != null)
            {
                var albedoField = new ObjectField("Albedo")
                {
                    objectType = typeof(Texture2D),
                    value      = albedoProp.objectReferenceValue,
                };
                albedoField.RegisterValueChangedCallback(e =>
                {
                    albedoProp.objectReferenceValue = e.newValue;
                    this.serializedObject.ApplyModifiedProperties();
                    // Refresh thumbnail when albedo changes.
                    Texture2D? newTex = e.newValue as Texture2D;
                    Texture2D? newThumb = (newTex != null)
                        ? (AssetPreview.GetAssetPreview(newTex) ?? newTex) : null;
                    thumbEl.style.backgroundImage = newThumb != null
                        ? new StyleBackground(newThumb) : new StyleBackground();
                });
                card.Add(albedoField);
            }

            // ── Tiling field ──────────────────────────────────────────────────

            if (tilingProp != null)
            {
                var tilingField = new PropertyField(tilingProp, "Tiling");
                tilingField.Bind(this.serializedObject);
                card.Add(tilingField);
            }

            return card;
        }
    }
}
