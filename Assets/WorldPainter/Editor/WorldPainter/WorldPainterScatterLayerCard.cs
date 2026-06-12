#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Detail card shown when a Grass scatter layer row is selected in the layer stack.
    /// Displays: density slider, slope range, align-normal toggle, and jitter.
    ///
    /// LOD0 orbit preview and LOD band ruler are rendered via IMGUI containers wrapping
    /// <see cref="WorldPainterLodPreviewPanel"/> and <see cref="WorldPainterLodBandRuler"/>.
    ///
    /// Design §4.1 / §6 — Phase 3 task 1 + 8.
    /// </summary>
    internal sealed class WorldPainterScatterLayerCard
    {
        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly WorldPainterLodPreviewPanel lodPreview;
        private readonly WorldPainterLodBandRuler    lodRuler;
        private readonly WorldPainterPreviewCache    previewCache;

        // ── Ctor ──────────────────────────────────────────────────────────────

        public WorldPainterScatterLayerCard(
            WorldPainterLodPreviewPanel lodPreview,
            WorldPainterLodBandRuler    lodRuler,
            WorldPainterPreviewCache    previewCache)
        {
            this.lodPreview   = lodPreview;
            this.lodRuler     = lodRuler;
            this.previewCache = previewCache;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds and returns the UIElements card for the scatter layer at
        /// <paramref name="scatterIndex"/> in <paramref name="painter"/>.ScatterLayers.
        /// Returns null when the index is out of range or the layer is not a
        /// <see cref="DensityScatterLayer"/>.
        /// </summary>
        public VisualElement? Build(WorldPainter painter, int scatterIndex)
        {
            var scatterLayers = painter.ScatterLayers;
            if (scatterIndex < 0 || scatterIndex >= scatterLayers.Count) return null;

            ScatterLayer? rawLayer = scatterLayers[scatterIndex];
            if (rawLayer == null) return null;

            // Also accept layers sourced from WorldMapAsset (P5 palette path).
            // Falls through to the same card construction below.


            var card = new VisualElement();
            card.AddToClassList("wp-splat-card");

            // ── Layer name banner ─────────────────────────────────────────────

            var nameBanner = new Label($"Grass: {rawLayer.name}");
            nameBanner.AddToClassList("wp-layer-name");
            card.Add(nameBanner);

            // ── LOD0 orbit preview (IMGUI container) ─────────────────────────

            ScatterLayer capturedLayer = rawLayer;
            var previewContainer = new IMGUIContainer(() =>
            {
                this.lodPreview.Draw(capturedLayer);
            });
            previewContainer.style.height = 240f; // 220px panel + controls row
            card.Add(previewContainer);

            // ── LOD band ruler (IMGUI container) ─────────────────────────────

            SerializedObject? so = rawLayer != null ? new SerializedObject(rawLayer) : null;
            var rulerContainer = new IMGUIContainer(() =>
            {
                EditorGUILayout.LabelField("LOD Bands", EditorStyles.boldLabel);
                this.lodRuler.Draw(capturedLayer, so);
            });
            rulerContainer.style.height = 80f;
            card.Add(rulerContainer);

            // ── Manual LOD assignment (shown only when LOD0 is unassigned) ───
            card.Add(BuildLodAssignmentField(capturedLayer));

            // ── Density/slope controls (DensityScatterLayer only) ────────────

            if (rawLayer is DensityScatterLayer densityLayer)
            {
                var so2 = new SerializedObject(densityLayer);
                card.Add(this.BuildDensityControls(densityLayer, so2));
            }

            return card;
        }

        // ── Build overload for map-sourced layers (P5 palette) ────────────────

        /// <summary>
        /// Builds and returns the UIElements card directly from a <see cref="DensityScatterLayer"/>
        /// sourced from <see cref="WorldMapAsset.Layers"/> (not the legacy ScatterLayers list).
        /// Returns null when <paramref name="layer"/> is null.
        /// </summary>
        public VisualElement? BuildForMapLayer(DensityScatterLayer? layer)
        {
            if (layer == null) return null;

            var card = new VisualElement();
            card.AddToClassList("wp-splat-card");

            var nameBanner = new Label($"Meadow: {layer.name}");
            nameBanner.AddToClassList("wp-layer-name");
            card.Add(nameBanner);

            ScatterLayer captured = layer;
            var previewContainer = new IMGUIContainer(() => this.lodPreview.Draw(captured));
            previewContainer.style.height = 240f;
            card.Add(previewContainer);

            SerializedObject so = new SerializedObject(layer);
            var rulerContainer = new IMGUIContainer(() =>
            {
                EditorGUILayout.LabelField("LOD Bands", EditorStyles.boldLabel);
                this.lodRuler.Draw(captured, so);
            });
            rulerContainer.style.height = 80f;
            card.Add(rulerContainer);

            // ── Manual LOD assignment (shown only when LOD0 is unassigned) ───
            card.Add(BuildLodAssignmentField(layer));

            var so2 = new SerializedObject(layer);
            card.Add(this.BuildDensityControls(layer, so2));

            return card;
        }

        // ── Manual LOD assignment ─────────────────────────────────────────────

        /// <summary>
        /// IMGUI field that lets the user manually assign LOD meshes from inside the
        /// card. Renders ONLY while the layer's LOD0 mesh is unassigned — once a LOD0
        /// mesh is present the read-only preview/ruler take over and this collapses to
        /// zero height. New layers are created with an empty <c>render.lods</c> array,
        /// so this is what the user sees immediately after adding a scatter layer.
        /// </summary>
        private static VisualElement BuildLodAssignmentField(ScatterLayer layer)
        {
            var so = new SerializedObject(layer);
            return new IMGUIContainer(() =>
            {
                Mesh[] lodMeshes = layer.Render.LodMeshes;
                bool hasLod0 = lodMeshes.Length > 0 && lodMeshes[0] != null;
                if (hasLod0) return; // LOD0 assigned — nothing to surface.

                so.Update();
                var lodsProp = so.FindProperty("render")?.FindPropertyRelative("lods");
                if (lodsProp == null) return;

                EditorGUILayout.HelpBox(
                    "This layer has no LOD0 mesh and will not render. " +
                    "Assign at least one LOD mesh below.", MessageType.Warning);

                // Empty array → offer a one-click slot so the mesh field appears at once.
                if (lodsProp.arraySize == 0)
                {
                    if (GUILayout.Button("Add LOD0 Slot"))
                        lodsProp.InsertArrayElementAtIndex(0);
                }

                EditorGUILayout.PropertyField(lodsProp, new GUIContent("LOD Meshes"), true);

                if (so.ApplyModifiedProperties())
                    EditorUtility.SetDirty(layer);
            });
        }

        // ── Density controls ──────────────────────────────────────────────────

        private VisualElement BuildDensityControls(DensityScatterLayer layer, SerializedObject so)
        {
            var controls = new IMGUIContainer(() =>
            {
                so.Update();

                var targetInstProp = so.FindProperty("targetInstances");
                var slopeProp      = so.FindProperty("slopeRange");
                var alignProp      = so.FindProperty("alignToNormal");
                var pitchProp      = so.FindProperty("randomPitchRange");
                var rollProp       = so.FindProperty("randomRollRange");

                if (targetInstProp != null)
                    EditorGUILayout.IntSlider(targetInstProp,
                        1, 500000, new GUIContent("Density (instances)"));

                if (slopeProp != null)
                    EditorGUILayout.PropertyField(slopeProp, new GUIContent("Slope Range"));

                if (alignProp != null)
                    EditorGUILayout.PropertyField(alignProp, new GUIContent("Align to Normal"));

                if (pitchProp != null)
                    EditorGUILayout.PropertyField(pitchProp, new GUIContent("Jitter Pitch"));

                if (rollProp != null)
                    EditorGUILayout.PropertyField(rollProp, new GUIContent("Jitter Roll"));

                if (so.ApplyModifiedProperties())
                    EditorUtility.SetDirty(layer);
            });
            controls.style.height = 110f;
            return controls;
        }
    }
}
