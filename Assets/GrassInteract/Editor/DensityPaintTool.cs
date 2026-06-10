#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Terrain-tool-style density brush for a <see cref="DensityScatterLayer"/>. Raycasts the ground,
    /// splats the brush via <see cref="DensityPaintGPU"/> into a GPU <c>RenderTexture</c>, and
    /// re-scatters live through <see cref="ScatterRebuildScheduler"/> on the 0.15s tick. Persists
    /// the painted pixels back to the texture asset (PNG) on stroke end via
    /// <see cref="DensityMapFactory.PersistPixels"/> (SSOT — no duplicate persist path here).
    ///
    /// <para>
    /// <b>Gating contract (score-20 risk):</b> <see cref="DensityPlacement"/> reads CPU pixels
    /// via <c>GetPixelBilinear</c> — the GPU RT alone will NOT re-scatter. CPU pixels are updated
    /// only on the 0.15s scheduler tick via <see cref="DensityPaintGPU.RequestLiveReadback"/>.
    /// <see cref="MouseDrag"/> does NOT call any readback — only <see cref="ScatterRebuildScheduler.MarkDirty"/>
    /// is called per-drag, which coalesces and fires the tick after the debounce delay.
    /// </para>
    ///
    /// All tool state (brush size, opacity, falloff, flow, paint mode, active stamp) is read from
    /// <see cref="ScatterAuthoringState.I"/> — this tool no longer writes or reads EditorPrefs.
    ///
    /// Stamp resolution follows <see cref="StampRef"/>:
    ///   <see cref="StampRef.StampSource.None"/>   → procedural falloff kernel (same as old StampIndex == -1)
    ///   <see cref="StampRef.StampSource.Config"/>  → <c>field.Config.BrushStamps[index]</c>
    ///   <see cref="StampRef.StampSource.Global"/>  → <see cref="ScatterBrushLibraryProvider.Library"/>.Stamps[index]
    /// </summary>
    // Global (unscoped) EditorTool — no typeof() target.
    // DensityScatterLayer is a ScriptableObject sub-asset; Unity's tool context does NOT
    // track sub-asset selections as typed EditorTool targets, so specifying
    // typeof(DensityScatterLayer) silently prevents activation.
    // The active layer is read from ScatterAuthoringState.I.ActiveLayer instead of this.target.
    [EditorTool("Density Paint")]
    internal sealed class DensityPaintTool : EditorTool
    {
        internal enum PaintMode { Paint, Erase, Smooth }

        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>
        /// Stamp spacing as a fraction of brush radius — controls how densely stamps tile
        /// along a drag stroke. 0.25 gives terrain-like continuous fill without overlap gaps.
        /// </summary>
        private const float SPACING_FACTOR = 0.25f;

        /// <summary>Minimum seconds between live readback requests (SF-1 gate).</summary>
        private const double READBACK_INTERVAL_SECONDS = 0.15;

        // ── Active stroke state ────────────────────────────────────────────────

        private bool            painting;
        private Texture2D?      activeMap;

        /// <summary>
        /// Resolved at stroke start AND on every hover event (SF-5).
        /// null = procedural falloff.
        /// </summary>
        private Texture2D?      activeStamp;
        private readonly DensityPaintGPU gpu = new DensityPaintGPU();

        // ── Cached stroke context (used for per-drag MarkDirty + readback) ─────

        private ScatterField? activeField;
        private int           activeLayerIdx;

        // ── Live-update throttle ───────────────────────────────────────────────

        /// <summary>
        /// EditorApplication.timeSinceStartup when the last live readback ran.
        /// Prevents readbacks at ~60Hz — gated to at most once per
        /// <see cref="READBACK_INTERVAL_SECONDS"/> (SF-1 fix).
        /// </summary>
        private double lastReadbackTime;

        public override GUIContent toolbarIcon => EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSplat");

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;

            // Global EditorTool — read the active layer from ScatterAuthoringState rather than
            // this.target (which is always null for an unscoped tool). The layer is set by
            // DensityPaintPanel.BindLayer() when the user selects a layer in Scatter Studio.
            DensityScatterLayer? layer = ScatterAuthoringState.I.ActiveLayer;
            if (layer == null) return;

            (ScatterField? field, int layerIdx) = ScatterFieldLookup.FindOwningField(layer);

            // Auto-create density map on first paint if none exists.
            if (field != null && layer.DensityMap == null)
                TryAutoCreateDensityMap(layer, field);

            bool valid = layer.Validate(out string error);

            this.DrawSettingsWindow(valid, valid ? null : error);

            if (!valid || field == null)
                return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            // Claim the default scene control so mouse clicks reach this tool instead of being
            // consumed by the Scene view's object-picking (otherwise the brush draws but never paints).
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            Vector3 origin = field.ResolveFieldOrigin();
            Vector2 bounds = field.ResolveFieldBoundsXZ(layer);
            LayerMask mask = field.ResolveGroundMask(layer);

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity,
                mask.value == 0 ? ~0 : mask.value);

            if (hasHit)
            {
                float size    = ScatterAuthoringState.I.BrushSize;
                float falloff = ScatterAuthoringState.I.BrushFalloff;

                // SF-5: resolve the active stamp on every hover so the decal preview shows the
                // current stamp shape before the first click — not only at BeginStroke.
                this.activeStamp = ResolveStamp(field);

                // Push the brush state; ScatterBrushPreview draws it in world space from its
                // SceneView.duringSceneGui callback (DrawMeshNow does not render in world space from
                // OnToolGUI — it would draw in the Scene-view corner instead).
                ScatterBrushPreview.Set(
                    hit.point,
                    hit.normal,
                    size,
                    this.activeStamp,
                    this.BrushColorForMode(),
                    falloff);

                HandleUtility.Repaint();
            }

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && hasHit:
                    GUIUtility.hotControl = controlId;
                    this.BeginStroke(layer, field, layerIdx);
                    {
                        var space = new GrassFieldSpace(origin, bounds);
                        Vector2 centerUv = space.WorldToUv(hit.point);

                        // NTH-2: compute separate X and Y UV radii to honour non-square fields.
                        float radUvX = ScatterAuthoringState.I.BrushSize / Mathf.Max(0.0001f, bounds.x);
                        float radUvY = ScatterAuthoringState.I.BrushSize / Mathf.Max(0.0001f, bounds.y);

                        float strength = GetStrength();
                        float inner    = Mathf.Clamp01(ScatterAuthoringState.I.BrushFalloff);
                        int   mode     = ScatterAuthoringState.I.PaintMode;
                        this.gpu.StrokeTo(centerUv, centerUv, radUvX, radUvY, SPACING_FACTOR, strength, this.activeStamp, mode, inner);
                        // Live re-scatter immediately for the first dab — synchronous throttled
                        // readback + immediate rebuild (EditorApplication.update is starved mid-drag).
                        this.LiveUpdate(field, layerIdx);
                    }
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && this.painting:
                    if (hasHit)
                    {
                        var space = new GrassFieldSpace(origin, bounds);
                        Vector2 toUv = space.WorldToUv(hit.point);

                        // NTH-2: separate X and Y radii.
                        float radUvX = ScatterAuthoringState.I.BrushSize / Mathf.Max(0.0001f, bounds.x);
                        float radUvY = ScatterAuthoringState.I.BrushSize / Mathf.Max(0.0001f, bounds.y);

                        float strength = GetStrength();
                        float inner    = Mathf.Clamp01(ScatterAuthoringState.I.BrushFalloff);
                        int   mode     = ScatterAuthoringState.I.PaintMode;

                        Vector2 fromUv = this.gpu.TryGetLastUv(out Vector2 last) ? last : toUv;
                        this.gpu.StrokeTo(fromUv, toUv, radUvX, radUvY, SPACING_FACTOR, strength, this.activeStamp, mode, inner);
                        // Live re-scatter at the 0.15s throttle while dragging (see LiveUpdate).
                        this.LiveUpdate(field, layerIdx);
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && this.painting:
                    this.EndStroke(field, layerIdx);
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        // ── Auto-create density map ────────────────────────────────────────────

        /// <summary>
        /// On the very first paint stroke for a layer with no <c>densityMap</c> assigned,
        /// creates a blank density map via <see cref="DensityMapFactory.CreateBlank"/> and
        /// assigns it to the layer via <see cref="SerializedObject"/> (Undo-tracked, asset-dirty).
        /// </summary>
        private static void TryAutoCreateDensityMap(DensityScatterLayer layer, ScatterField field)
        {
            int size = 512; // default resolution

            Texture2D? map = DensityMapFactory.CreateBlank(size, field.Config);
            if (map == null)
            {
                Debug.LogError("[DensityPaintTool] Auto-create density map failed — see previous errors.");
                return;
            }

            Undo.RegisterCompleteObjectUndo(layer, "Auto-Create Density Map");

            var so = new SerializedObject(layer);
            var prop = so.FindProperty("densityMap");
            if (prop != null)
            {
                prop.objectReferenceValue = map;
                so.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();

            Debug.Log($"[DensityPaintTool] Auto-created density map '{map.name}' for layer '{layer.name}'.");
        }

        // ── Stroke lifecycle ───────────────────────────────────────────────────

        private void BeginStroke(DensityScatterLayer layer, ScatterField field, int layerIdx)
        {
            Texture2D map = layer.DensityMap!;
            // Snapshot the CPU texture for Undo — EndStroke writes pixels back so undo restores pre-stroke.
            Undo.RegisterCompleteObjectUndo(map, "Paint Density");

            this.activeMap       = map;
            // activeStamp is already current from the hover-resolve in OnToolGUI (SF-5).
            // Re-resolve here as a safety net in case BeginStroke is called without a prior hover.
            this.activeStamp     = ResolveStamp(field);
            this.activeField     = field;
            this.activeLayerIdx  = layerIdx;
            this.painting        = true;
            this.lastReadbackTime = 0;  // 0 → the first LiveUpdate (MouseDown) fires immediately

            this.gpu.BeginStroke(map);
        }

        private void EndStroke(ScatterField field, int layerIdx)
        {
            this.painting = false;

            // Final readback → persist to PNG (all inside gpu.EndStroke).
            this.gpu.EndStroke();

            // Rebuild immediately so the final stroke state shows without waiting for the
            // drag-starved scheduler tick.
            if (layerIdx >= 0) ScatterRebuildScheduler.RebuildImmediate(field, layerIdx);

            this.activeMap      = null;
            this.activeField    = null;
            this.activeLayerIdx = 0;
        }

        // ── Live update during a stroke ────────────────────────────────────────

        /// <summary>
        /// Synchronously reads the painted RT back to the CPU density map and rebuilds the layer
        /// immediately, throttled to once per <see cref="READBACK_INTERVAL_SECONDS"/> (SF-1: not
        /// per-frame). Driven from the <c>MouseDown</c>/<c>MouseDrag</c> handlers — which DO fire
        /// during an active drag — rather than <see cref="EditorApplication.update"/>, which Unity
        /// starves while the user drags in the Scene view (that was why grass only updated on
        /// mouse-up). Synchronous readback + immediate rebuild bypass the starved update loop.
        /// </summary>
        private void LiveUpdate(ScatterField field, int layerIdx)
        {
            if (field == null) return;

            // SF-1: throttle to at most once per 0.15s regardless of mouse-event frequency.
            double now = EditorApplication.timeSinceStartup;
            if (now - this.lastReadbackTime < READBACK_INTERVAL_SECONDS) return;
            this.lastReadbackTime = now;

            this.gpu.ReadbackNow();
            ScatterRebuildScheduler.RebuildImmediate(field, layerIdx);
        }

        // ── Stamp resolution ───────────────────────────────────────────────────

        /// <summary>
        /// Resolves the active <see cref="StampRef"/> from <see cref="ScatterAuthoringState"/> to a
        /// concrete readable <see cref="Texture2D"/>, or <c>null</c> for procedural falloff.
        ///
        /// SSOT: this is the ONLY stamp resolver in the codebase.
        /// <see cref="ScatterBrushPreview"/> and <see cref="DensityPaintGPU"/> consume the
        /// already-resolved texture; they do NOT re-resolve <see cref="StampRef"/>.
        ///
        /// Resolution table (matches old StampIndex semantics):
        ///   StampSource.None   → null (procedural; was StampIndex == -1)
        ///   StampSource.Config → field.Config.BrushStamps[index] (was StampIndex >= 0 into Config list)
        ///   StampSource.Global → ScatterBrushLibraryProvider.Library.Stamps[index]
        /// </summary>
        private static Texture2D? ResolveStamp(ScatterField field)
        {
            StampRef stampRef = ScatterAuthoringState.I.ActiveStamp;

            if (stampRef.IsNone) return null;

            BrushStamp? stamp = null;

            switch (stampRef.Source)
            {
                case StampRef.StampSource.Config:
                    if (field.Config == null) return null;
                    var configStamps = field.Config.BrushStamps;
                    if (stampRef.Index < 0 || stampRef.Index >= configStamps.Count) return null;
                    stamp = configStamps[stampRef.Index];
                    break;

                case StampRef.StampSource.Global:
                    var globalStamps = ScatterBrushLibraryProvider.Library.Stamps;
                    if (stampRef.Index < 0 || stampRef.Index >= globalStamps.Count) return null;
                    stamp = globalStamps[stampRef.Index];
                    break;

                default:
                    return null;
            }

            if (stamp == null) return null;
            Texture2D? shape = stamp.Shape;
            return shape != null && shape.isReadable ? shape : null;
        }

        // ── Minimal in-scene HUD ───────────────────────────────────────────────

        /// <summary>
        /// Minimal in-scene overlay: shows the tool title, a mode label, and any validation error.
        /// All brush settings now live in the Scatter Studio window.
        /// </summary>
        private void DrawSettingsWindow(bool valid, string? error)
        {
            float h = valid ? 52f : 88f;
            Handles.BeginGUI();
            var area = new Rect(8, 8, 200, h);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Density Paint", EditorStyles.boldLabel);

            if (!valid)
                EditorGUILayout.HelpBox(error ?? "Density map invalid.", MessageType.Error);
            else
            {
                var mode = (PaintMode)ScatterAuthoringState.I.PaintMode;
                GUILayout.Label($"Mode: {mode}", EditorStyles.miniLabel);
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private Color BrushColorForMode()
        {
            var mode = (PaintMode)ScatterAuthoringState.I.PaintMode;
            return mode == PaintMode.Erase ? ScatterGizmos.EraseColor : ScatterGizmos.BrushColor;
        }

        private static float GetStrength()
        {
            return Mathf.Clamp01(ScatterAuthoringState.I.BrushOpacity)
                 * Mathf.Clamp01(ScatterAuthoringState.I.BrushFlow);
        }
    }
}
