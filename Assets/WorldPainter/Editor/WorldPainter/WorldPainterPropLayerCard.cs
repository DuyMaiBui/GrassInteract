#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Detail card shown when a Prop scatter layer row is selected in the layer stack.
    /// Displays: mesh reference, scale/rotation jitter, slope mask, density-per-stamp.
    ///
    /// Reuses <see cref="InstanceScatterLayer"/> + <see cref="AuthoredInstancesData"/> as
    /// the data model (frozen SSOT — accessed via the layer reference, never directly edited).
    ///
    /// Design §4.1 Phase 4 task 1.
    /// </summary>
    internal sealed class WorldPainterPropLayerCard
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float CONTROLS_HEIGHT = 130f;

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds and returns the UIElements card for the prop scatter layer at
        /// <paramref name="scatterIndex"/> in <paramref name="painter"/>.ScatterLayers.
        /// Returns null when the index is out of range or the layer is not an
        /// <see cref="InstanceScatterLayer"/>.
        /// </summary>
        public VisualElement? Build(WorldPainter painter, int scatterIndex)
        {
            var scatterLayers = painter.ScatterLayers;
            if (scatterIndex < 0 || scatterIndex >= scatterLayers.Count) return null;

            var rawLayer = scatterLayers[scatterIndex] as InstanceScatterLayer;
            if (rawLayer == null) return null;

            var card = new VisualElement();
            card.AddToClassList("wp-splat-card");

            // ── Layer name banner ──────────────────────────────────────────────

            var nameBanner = new Label($"Prop: {rawLayer.name}");
            nameBanner.AddToClassList("wp-layer-name");
            card.Add(nameBanner);

            // ── LOD0 mesh info (read-only display) ────────────────────────────

            var meshLabel = new Label(BuildMeshInfo(rawLayer));
            meshLabel.AddToClassList("wp-layer-name");
            card.Add(meshLabel);

            // ── Controls (scale/rotation jitter, slope mask, density) ─────────

            var so = new SerializedObject(rawLayer);
            var controls = new IMGUIContainer(() => this.DrawPropControls(so, rawLayer));
            controls.style.height = CONTROLS_HEIGHT;
            card.Add(controls);

            // ── Instance count ────────────────────────────────────────────────

            var authored = rawLayer.AuthoredInstances;
            int count = authored != null ? authored.Count : 0;
            var countLabel = new Label($"Authored instances: {count:N0}");
            countLabel.AddToClassList("wp-layer-name");
            card.Add(countLabel);

            return card;
        }

        // ── Prop controls (IMGUI) ─────────────────────────────────────────────

        private void DrawPropControls(SerializedObject so, InstanceScatterLayer layer)
        {
            so.Update();

            // Scale range from ScatterRenderConfig embedded struct.
            var renderProp = so.FindProperty("render");
            var scaleRange = renderProp?.FindPropertyRelative("scaleRange");
            if (scaleRange != null)
                EditorGUILayout.PropertyField(scaleRange, new GUIContent("Scale Range"));

            // Slope mask: from ScatterPlacementConfig embedded struct.
            var placementProp = so.FindProperty("placement");
            var slopeRange = placementProp?.FindPropertyRelative("slopeRange");
            if (slopeRange != null)
                EditorGUILayout.PropertyField(slopeRange, new GUIContent("Slope Mask"));

            // Density per stamp (custom prop on this tool — shown as a read-only proxy).
            EditorGUILayout.LabelField("Density/stamp", WorldPainterPropStampEmitter.GetDensityLabel(layer),
                EditorStyles.miniLabel);

            // Generate colliders toggle.
            var genColliders = so.FindProperty("generateColliders");
            if (genColliders != null)
                EditorGUILayout.PropertyField(genColliders, new GUIContent("Generate Colliders"));

            if (so.ApplyModifiedProperties())
                EditorUtility.SetDirty(layer);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string BuildMeshInfo(InstanceScatterLayer layer)
        {
            var lods = layer.Render.Lods;
            if (lods.Length == 0 || lods[0].mesh == null)
                return "Mesh: (none — assign via InstanceScatterLayer asset)";
            return $"Mesh: {lods[0].mesh!.name}  ({lods.Length} LOD(s))";
        }
    }
}
