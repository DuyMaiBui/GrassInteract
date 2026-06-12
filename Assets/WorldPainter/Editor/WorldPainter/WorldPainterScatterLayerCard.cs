#nullable enable
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Detail card shown when a Grass scatter layer row is selected in the layer stack.
    /// Custom-draws the layer's fields with UI Toolkit: a <see cref="SerializedObject"/>-bound
    /// column of <see cref="Foldout"/> cards (Render / Wind / Deform / Bounds / Placement /
    /// Density), each a group of <see cref="PropertyField"/>s. The Render card also hosts the
    /// segmented LOD distance bar (<see cref="WorldPainterLodDistanceBar"/>).
    ///
    /// Modeled on the GrassInteract Scatter-Studio LayerPanelView.
    /// </summary>
    internal sealed class WorldPainterScatterLayerCard
    {
        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds and returns the UIElements card for the scatter layer at
        /// <paramref name="scatterIndex"/> in <paramref name="painter"/>.ScatterLayers.
        /// Returns null when the index is out of range or the layer is null.
        /// </summary>
        public VisualElement? Build(WorldPainter painter, int scatterIndex)
        {
            var scatterLayers = painter.ScatterLayers;
            if (scatterIndex < 0 || scatterIndex >= scatterLayers.Count) return null;

            ScatterLayer? rawLayer = scatterLayers[scatterIndex];
            if (rawLayer == null) return null;

            var card = new VisualElement();
            card.AddToClassList("wp-splat-card");

            var nameBanner = new Label($"Grass: {rawLayer.name}");
            nameBanner.AddToClassList("wp-layer-name");
            card.Add(nameBanner);

            card.Add(BuildLayerFields(rawLayer));

            return card;
        }

        // ── Custom UIToolkit field column ─────────────────────────────────────

        /// <summary>
        /// Builds a SerializedObject-bound column of foldout cards, one per config struct plus
        /// a Density card for density-specific fields. Edits persist via binding; a change
        /// callback flags the layer dirty so the world re-scatters on the next rebuild.
        /// </summary>
        private static VisualElement BuildLayerFields(ScatterLayer layer)
        {
            var so = new SerializedObject(layer);

            var column = new VisualElement();

            column.Add(BuildRenderCard(so));
            column.Add(MakeFoldout(so, "Wind",      "wind"));
            column.Add(MakeFoldout(so, "Deform",    "deform"));
            column.Add(MakeFoldout(so, "Bounds",    "bounds"));
            column.Add(MakeFoldout(so, "Placement", "placement"));

            if (layer is DensityScatterLayer)
            {
                column.Add(MakeFoldout(so, "Density",
                    "densityMap", "targetInstances", "fieldBounds", "scaleRange", "seed",
                    "slopeRange", "splatLayerIndex", "splatThreshold",
                    "rotationOffsetEuler", "randomPitchRange", "randomRollRange", "alignToNormal"));
            }

            column.Bind(so);

            // Persist + flag dirty on any edit (parity with the old per-field controls).
            column.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(layer);
            });

            return column;
        }

        // ── Render card (PropertyField + LOD distance bar) ────────────────────

        /// <summary>
        /// Render foldout: the <c>render</c> struct PropertyField plus an IMGUI LOD distance bar
        /// sharing the same <see cref="SerializedObject"/> so handle drags persist immediately.
        /// </summary>
        private static VisualElement BuildRenderCard(SerializedObject so)
        {
            var foldout = MakeFoldout(so, "Render", "render");

            SerializedProperty? renderProp = so.FindProperty("render");
            if (renderProp != null)
            {
                string renderPath = renderProp.propertyPath;
                var bar = new IMGUIContainer(() =>
                {
                    so.Update();
                    SerializedProperty? rp = so.FindProperty(renderPath);
                    if (rp == null) return;

                    Rect r = GUILayoutUtility.GetRect(
                        0f, WorldPainterLodDistanceBar.BAR_HEIGHT, GUILayout.ExpandWidth(true));
                    WorldPainterLodDistanceBar.Draw(r, rp);
                });
                foldout.Add(bar);
            }

            return foldout;
        }

        // ── Foldout helper ────────────────────────────────────────────────────

        /// <summary>
        /// A titled, expanded <see cref="Foldout"/> holding a <see cref="PropertyField"/> per
        /// named serialized property. Missing properties are skipped.
        /// </summary>
        private static Foldout MakeFoldout(SerializedObject so, string title, params string[] propertyNames)
        {
            var foldout = new Foldout { text = title, value = true };
            foldout.AddToClassList("wp-layer-card");

            foreach (string propName in propertyNames)
            {
                SerializedProperty? prop = so.FindProperty(propName);
                if (prop == null) continue;

                var field = new PropertyField(prop);
                field.AddToClassList("wp-layer-field");
                foldout.Add(field);
            }

            return foldout;
        }
    }
}
