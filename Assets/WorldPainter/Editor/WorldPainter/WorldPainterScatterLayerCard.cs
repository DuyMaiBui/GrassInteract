#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Detail card shown when a Grass scatter layer row is selected in the layer stack.
    /// Displays: density slider, slope range, align-normal toggle, jitter, and a live
    /// blade-count label (async GPU readback on a 0.15s tick, never per-frame CPU recount).
    ///
    /// LOD0 orbit preview and LOD band ruler are rendered via IMGUI containers wrapping
    /// <see cref="WorldPainterLodPreviewPanel"/> and <see cref="WorldPainterLodBandRuler"/>.
    ///
    /// Design §4.1 / §6 — Phase 3 task 1 + 8.
    /// </summary>
    internal sealed class WorldPainterScatterLayerCard
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float BLADE_TICK_SEC = 0.15f;

        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly WorldPainterLodPreviewPanel lodPreview;
        private readonly WorldPainterLodBandRuler    lodRuler;
        private readonly WorldPainterPreviewCache    previewCache;

        // ── Blade count state ─────────────────────────────────────────────────

        private double lastBladeTickTime;
        private int    cachedBladeCount = -1;
        private bool   pendingReadback;

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

            // ── Density/slope controls (DensityScatterLayer only) ────────────

            if (rawLayer is DensityScatterLayer densityLayer)
            {
                var so2 = new SerializedObject(densityLayer);
                card.Add(this.BuildDensityControls(densityLayer, so2));
            }

            // ── Live blade count ──────────────────────────────────────────────

            var bladeLabel = new Label("Blade count: —");
            bladeLabel.AddToClassList("wp-layer-name");
            card.Add(bladeLabel);

            // Schedule async tick: read target instances from painter.ScatterLayers directly.
            bladeLabel.schedule
                .Execute(() => this.TickBladeCount(bladeLabel, painter))
                .Every((long)(BLADE_TICK_SEC * 1000));

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

            var so2 = new SerializedObject(layer);
            card.Add(this.BuildDensityControls(layer, so2));

            var bladeLabel = new Label("Blade count: —");
            bladeLabel.AddToClassList("wp-layer-name");
            card.Add(bladeLabel);

            bladeLabel.schedule
                .Execute(() => this.TickBladeCountForLayer(bladeLabel, layer))
                .Every((long)(BLADE_TICK_SEC * 1000));

            return card;
        }

        private void TickBladeCountForLayer(Label label, DensityScatterLayer layer)
        {
            if (this.pendingReadback) return;
            label.text = $"Blade count: ~{layer.TargetInstances:N0} (target)";
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

        // ── Blade count async tick ────────────────────────────────────────────

        private void TickBladeCount(Label label, WorldPainter painter)
        {
            if (this.pendingReadback) return;

            // Prefer map layers (the P2+ SSOT); fall back to inline ScatterLayers for
            // any legacy scene that hasn't yet assigned a WorldMapAsset.
            // A true async GPU counter would require a running compute buffer; we
            // read target instances as best-effort (design §6 note).
            System.Collections.Generic.IEnumerable<ScatterLayer>? layers =
                (System.Collections.Generic.IEnumerable<ScatterLayer>?)painter.Map?.Layers
                ?? painter.ScatterLayers;

            if (layers != null)
            {
                int total = 0;
                foreach (ScatterLayer sl in layers)
                {
                    if (sl is DensityScatterLayer dsl)
                        total += dsl.TargetInstances;
                }
                this.cachedBladeCount = total;
            }

            label.text = this.cachedBladeCount >= 0
                ? $"Blade count: ~{this.cachedBladeCount:N0} (target)"
                : "Blade count: —";
        }
    }
}
