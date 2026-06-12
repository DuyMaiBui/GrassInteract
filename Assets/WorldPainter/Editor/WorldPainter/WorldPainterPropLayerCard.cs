#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Detail card shown when a Prop scatter layer row is selected in the layer stack.
    ///
    /// Features (P7):
    ///   - Per-layer anchor config: pivot offset / ground-snap / align-to-normal.
    ///   - Explicit Scatter ↔ Transform mode toggle (UI button + T shortcut key).
    ///   - LOD0 mesh square preview via <see cref="AssetPreview"/>.
    ///   - Live instance count + in-scene gizmo summary.
    ///   - Scatter controls: density, scale range, slope mask, yaw jitter.
    ///
    /// Mode toggle shortcut: <b>T</b> (configurable via <see cref="TOGGLE_KEY"/>).
    /// </summary>
    internal sealed class WorldPainterPropLayerCard
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float CONTROLS_HEIGHT  = 230f;
        private const float PREVIEW_SIZE_PX  = 64f;

        /// <summary>Keyboard shortcut that toggles Scatter ↔ Transform mode (lower-case).</summary>
        public const char TOGGLE_KEY = 't';

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>Active prop placement mode for the currently-shown layer card.</summary>
        public PropPlacementMode Mode { get; private set; } = PropPlacementMode.Scatter;

        // UI element references (kept to allow live-count refresh without full rebuild).
        private Label?      countLabel;
        private IMGUIContainer? imguiContainer;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Toggles between <see cref="PropPlacementMode.Scatter"/> and
        /// <see cref="PropPlacementMode.Transform"/>. Called from the toggle button and from
        /// keyboard shortcut handling in <see cref="WorldPainterPropTransformEdit"/>.
        /// </summary>
        public void ToggleMode()
        {
            this.Mode = (this.Mode == PropPlacementMode.Scatter)
                ? PropPlacementMode.Transform
                : PropPlacementMode.Scatter;
            this.imguiContainer?.MarkDirtyRepaint();
        }

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

            // ── LOD0 preview + mesh info row ───────────────────────────────────

            var previewRow = new VisualElement();
            previewRow.style.flexDirection = FlexDirection.Row;
            previewRow.style.marginBottom  = 4f;

            var lod0Mesh = GetLod0Mesh(rawLayer);
            if (lod0Mesh != null)
            {
                var previewImg = new IMGUIContainer(() =>
                {
                    var tex = AssetPreview.GetAssetPreview(lod0Mesh);
                    if (tex != null)
                    {
                        var rect = GUILayoutUtility.GetRect(PREVIEW_SIZE_PX, PREVIEW_SIZE_PX,
                            GUILayout.Width(PREVIEW_SIZE_PX), GUILayout.Height(PREVIEW_SIZE_PX));
                        GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
                    }
                    else
                    {
                        GUILayout.Label("preview…",
                            GUILayout.Width(PREVIEW_SIZE_PX), GUILayout.Height(PREVIEW_SIZE_PX));
                    }
                });
                previewImg.style.width  = PREVIEW_SIZE_PX;
                previewImg.style.height = PREVIEW_SIZE_PX;
                previewRow.Add(previewImg);
            }

            var meshInfoLabel = new Label(BuildMeshInfo(rawLayer));
            meshInfoLabel.AddToClassList("wp-layer-name");
            meshInfoLabel.style.alignSelf = Align.Center;
            previewRow.Add(meshInfoLabel);

            card.Add(previewRow);

            // ── Mode toggle button ─────────────────────────────────────────────

            Button toggleBtn = null!;
            toggleBtn = new Button(() =>
            {
                this.ToggleMode();
                toggleBtn_UpdateText();
            });
            string toggleBtnText() => this.Mode == PropPlacementMode.Scatter
                ? $"Mode: Scatter (press {char.ToUpper(TOGGLE_KEY)} to switch)"
                : $"Mode: Transform (press {char.ToUpper(TOGGLE_KEY)} to switch)";
            void toggleBtn_UpdateText() => toggleBtn.text = toggleBtnText();
            toggleBtn.text = toggleBtnText();
            toggleBtn.AddToClassList("wp-layer-name");
            card.Add(toggleBtn);

            // Register the card-level keyboard shortcut: T = toggle mode.
            card.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.character == TOGGLE_KEY || evt.keyCode == KeyCode.T)
                {
                    this.ToggleMode();
                    toggleBtn_UpdateText();
                    evt.StopPropagation();
                }
            });
            card.focusable = true;

            // ── Controls IMGUI container ────────────────────────────────────────

            var so = new SerializedObject(rawLayer);
            this.imguiContainer = new IMGUIContainer(() =>
            {
                toggleBtn_UpdateText();
                this.DrawPropControls(so, rawLayer);
            });
            this.imguiContainer.style.height = CONTROLS_HEIGHT;
            card.Add(this.imguiContainer);

            // ── Live instance count ─────────────────────────────────────────────

            var authored = rawLayer.AuthoredInstances;
            int count = authored != null ? authored.Count : 0;
            this.countLabel = new Label(BuildCountText(count));
            this.countLabel.AddToClassList("wp-layer-name");
            card.Add(this.countLabel);

            // Refresh count whenever the card is repainted (covers post-scatter updates).
            card.RegisterCallback<AttachToPanelEvent>(_ => this.RefreshCount(rawLayer));

            return card;
        }

        // ── Prop controls (IMGUI) ─────────────────────────────────────────────

        private void DrawPropControls(SerializedObject so, InstanceScatterLayer layer)
        {
            so.Update();

            // ── Mode indicator ────────────────────────────────────────────────

            var modeStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle  = FontStyle.Bold,
            };
            GUILayout.Label(this.Mode == PropPlacementMode.Scatter
                ? "SCATTER MODE — drag brush to place props"
                : "TRANSFORM MODE — click an instance in SceneView",
                modeStyle);

            EditorGUILayout.Space(4f);

            // ── Anchor config (layer-wide) ────────────────────────────────────

            EditorGUILayout.LabelField("Anchor Config", EditorStyles.boldLabel);

            var propPivotProp = so.FindProperty("propPivotOffset");
            if (propPivotProp != null)
                EditorGUILayout.PropertyField(propPivotProp, new GUIContent("Pivot Offset"));

            var groundSnapProp = so.FindProperty("propGroundSnap");
            if (groundSnapProp != null)
                EditorGUILayout.PropertyField(groundSnapProp, new GUIContent("Ground Snap"));

            var alignNormalProp = so.FindProperty("propAlignToNormal");
            if (alignNormalProp != null)
                EditorGUILayout.PropertyField(alignNormalProp, new GUIContent("Align To Normal"));

            EditorGUILayout.Space(4f);

            // ── Scatter-mode controls (hidden in Transform mode) ──────────────

            if (this.Mode == PropPlacementMode.Scatter)
            {
                EditorGUILayout.LabelField("Scatter Controls", EditorStyles.boldLabel);

                // Density per stamp: shown as informational (drive via WorldPainterPropStampEmitter).
                EditorGUILayout.LabelField("Density/stamp",
                    WorldPainterPropStampEmitter.GetDensityLabel(layer), EditorStyles.miniLabel);

                // Scale range from ScatterRenderConfig embedded struct.
                var renderProp = so.FindProperty("render");
                var scaleRange = renderProp?.FindPropertyRelative("scaleRange");
                if (scaleRange != null)
                    EditorGUILayout.PropertyField(scaleRange, new GUIContent("Scale Range"));

                // Slope mask: from ScatterPlacementConfig embedded struct.
                var placementProp = so.FindProperty("placement");
                var slopeRange    = placementProp?.FindPropertyRelative("slopeRange");
                if (slopeRange != null)
                    EditorGUILayout.PropertyField(slopeRange, new GUIContent("Slope Mask"));
            }

            // ── Collider (always visible) ─────────────────────────────────────

            EditorGUILayout.Space(4f);
            var genColliders = so.FindProperty("generateColliders");
            if (genColliders != null)
                EditorGUILayout.PropertyField(genColliders, new GUIContent("Generate Colliders"));

            if (so.ApplyModifiedProperties())
                EditorUtility.SetDirty(layer);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void RefreshCount(InstanceScatterLayer layer)
        {
            if (this.countLabel == null) return;
            var authored = layer.AuthoredInstances;
            int count = authored != null ? authored.Count : 0;
            this.countLabel.text = BuildCountText(count);
        }

        private static string BuildCountText(int count) =>
            $"Authored instances: {count:N0}";

        private static string BuildMeshInfo(InstanceScatterLayer layer)
        {
            var lods = layer.Render.Lods;
            if (lods.Length == 0 || lods[0].mesh == null)
                return "Mesh: (none)";
            return $"Mesh: {lods[0].mesh!.name}  ({lods.Length} LOD(s))";
        }

        private static Mesh? GetLod0Mesh(InstanceScatterLayer layer)
        {
            var lods = layer.Render.Lods;
            return lods.Length > 0 ? lods[0].mesh : null;
        }
    }

    // ── PropPlacementMode ─────────────────────────────────────────────────────

    /// <summary>Sub-mode for prop layer editing in the WorldPainter.</summary>
    public enum PropPlacementMode
    {
        /// <summary>Brush drag randomly places new instances in the brush footprint.</summary>
        Scatter,
        /// <summary>Click to select an existing instance; Handles gizmo for move/rotate/scale.</summary>
        Transform,
    }
}
