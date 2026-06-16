#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Shared static authoring state for WorldPainter — the Inspector
    /// (<see cref="WorldPainterInspector"/>) and the sculpt EditorTool read from one SSOT.
    ///
    /// Not persisted — defaults reset on domain reload (acceptable for editor tools).
    /// </summary>
    public static class WorldPainterState
    {
        // ── Active painter ────────────────────────────────────────────────────

        /// <summary>WorldPainter component currently bound for authoring. Set by the Inspector.</summary>
        public static WorldPainter? ActivePainter { get; set; }

        // ── Tile edit mode (toggle Add / Remove via inspector TileStrip) ──────

        /// <summary>
        /// Inspector-driven tile-topology mode. Off = brush is the only scene interaction.
        /// Add = scene shows green ghost quads at every open neighbour edge; click adds a tile.
        /// Remove = scene shows red ghost quads on every existing tile; click removes it.
        /// Drives <see cref="WorldPainterTileGhostHandler.OnSceneGui"/>.
        /// </summary>
        public enum TileEditModeKind
        {
            Off,
            Add,
            Remove,
        }

        /// <summary>Current tile-edit mode. Setter fires <see cref="TileEditModeChanged"/>.</summary>
        public static TileEditModeKind TileEditMode
        {
            get => tileEditMode;
            set
            {
                if (tileEditMode == value) return;
                tileEditMode = value;
                TileEditModeChanged?.Invoke(value);
            }
        }
        private static TileEditModeKind tileEditMode = TileEditModeKind.Off;

        /// <summary>Fired after <see cref="TileEditMode"/> changes.</summary>
        public static event System.Action<TileEditModeKind>? TileEditModeChanged;

        // ── Paint mode (Phase 1 — replaces the EditorTool active-state) ───────

        /// <summary>
        /// When true, the inspector-driven <c>WorldPainterSculptTool</c> processes Scene-view
        /// input. When false the driver is short-circuited — handy as the "off" position of
        /// the BrushDock Paint Mode toggle. Auto-enabled when the user selects a paintable
        /// layer; users can flip it off to interact with the scene normally without restoring
        /// the previous EditorTool (since there is no longer one).
        /// </summary>
        public static bool PaintModeActive
        {
            get => paintModeActive;
            set
            {
                if (paintModeActive == value) return;
                paintModeActive = value;
                PaintModeChanged?.Invoke(value);
            }
        }
        private static bool paintModeActive;

        /// <summary>Fired after <see cref="PaintModeActive"/> flips. Argument = new value.</summary>
        public static event System.Action<bool>? PaintModeChanged;

        // ── Brush-dirty notification ──────────────────────────────────────────

        /// <summary>
        /// Raised by the brush dock when the falloff curve changes so the active
        /// <see cref="WorldPainterSculptTool"/> can re-upload the 256×1 LUT to GPU
        /// without requiring a tool re-activation.
        /// </summary>
        public static event System.Action? BrushFalloffDirty;

        /// <summary>Invoke to notify the active sculpt tool that the falloff curve changed.</summary>
        public static void RaiseBrushFalloffDirty() => BrushFalloffDirty?.Invoke();

        // ── Active layer ──────────────────────────────────────────────────────

        /// <summary>
        /// Index into the layer stack of the currently selected (active) layer.
        /// -1 = no layer selected. The layer stack view reads/writes this.
        /// Stack order (display): 0 = Height (synthetic), 1..N = splat rows.
        /// Setting a new value fires <see cref="ActiveLayerIndexChanged"/> so the contextual
        /// brush-tool palette can refresh (the stack selects via this index, not SetActiveLayer).
        /// </summary>
        public static int ActiveLayerIndex
        {
            get => activeLayerIndex;
            set
            {
                if (activeLayerIndex == value) return;
                activeLayerIndex = value;
                ActiveLayerIndexChanged?.Invoke();
            }
        }
        private static int activeLayerIndex = -1;

        /// <summary>Fired after <see cref="ActiveLayerIndex"/> changes (stack-driven selection).</summary>
        public static event System.Action? ActiveLayerIndexChanged;

        /// <summary>
        /// Returns the <see cref="LayerType"/> and splat channel index for the currently
        /// active layer.
        ///
        /// Phase 2: when a unified surface layer is active (ActiveLayerKind != None and
        /// ActiveLayerId is set), the kind is derived directly from <see cref="ActiveLayerKind"/>
        /// without relying on index arithmetic into the legacy lists.
        ///
        /// Otherwise the index 0/-1 fallback resolves to Height (the synthetic base row).
        /// </summary>
        /// <param name="painter">The active WorldPainter (unused; kept for call-site symmetry).</param>
        /// <param name="splatChannel">
        /// Output: 0-based splat channel [0..3] if the active layer is Splat; -1 otherwise.
        /// </param>
        /// <returns>The <see cref="LayerType"/> of the active layer.</returns>
        public static LayerType ActiveLayerType(WorldPainter painter, out int splatChannel)
        {
            splatChannel = -1;

            // Unified surface layer is active: derive kind from ActiveLayerKind.
            if (ActiveLayerKind != PaintLayerKind.None && !string.IsNullOrEmpty(ActiveLayerId))
            {
                return ActiveLayerKind switch
                {
                    PaintLayerKind.Splat  => LayerType.Splat,
                    PaintLayerKind.Meadow => LayerType.Grass,
                    PaintLayerKind.Prop   => LayerType.Props,
                    _ => LayerType.Height,
                };
            }

            // Legacy path: index 0/-1 = Height base.
            return LayerType.Height;
        }

        /// <summary>
        /// The effective layer type that drives brush-tool dispatch + the contextual palette.
        /// Prefers the unified <see cref="ActiveLayerKind"/>; falls back to the legacy
        /// <see cref="ActiveLayerType"/> index path (Height base row).
        /// </summary>
        public static LayerType EffectiveLayerType(WorldPainter painter)
        {
            PaintLayerKind p5 = ActiveLayerKind;

            // Unified surface layer takes priority.
            if (p5 == PaintLayerKind.Splat)  return LayerType.Splat;
            if (p5 == PaintLayerKind.Meadow) return LayerType.Grass;
            if (p5 == PaintLayerKind.Prop)   return LayerType.Props;

            // Fall back to the legacy index path (Height base row).
            return ActiveLayerType(painter, out _);
        }

        // ── Brush settings (SSOT) ─────────────────────────────────────────────

        /// <summary>
        /// Shared brush settings for all layer types (size/strength/falloff/spacing/flow).
        /// The brush dock binds to this instance; stroke dispatch reads from it.
        /// </summary>
        public static BrushSettings Brush { get; } = BrushSettings.Default;

        // ── Active paint-layer (P5 SSOT — consumed by P6 brush dispatch + P7 prop placement) ──

        /// <summary>
        /// Discriminates the section a selected paint layer belongs to.
        /// P6 (brush dispatch) and P7 (prop placement) both key off this.
        /// </summary>
        public enum PaintLayerKind
        {
            /// <summary>No layer is selected (default).</summary>
            None,
            /// <summary>A splat (terrain-texture) layer is active.</summary>
            Splat,
            /// <summary>A meadow/density scatter layer is active.</summary>
            Meadow,
            /// <summary>A prop/instance scatter layer is active.</summary>
            Prop,
        }

        /// <summary>
        /// The string ID of the currently selected paint layer.
        /// For Splat: the splat layer's name. For Meadow/Prop: the <see cref="ScatterLayer.name"/>
        /// (i.e. the sub-asset name including the "Layer_" prefix produced by
        /// <see cref="WorldMapAssetLifecycle.LayerSubAssetName"/>).
        /// Empty string when <see cref="ActiveLayerKind"/> is <see cref="PaintLayerKind.None"/>.
        /// </summary>
        public static string ActiveLayerId { get; private set; } = string.Empty;

        /// <summary>
        /// The kind of the currently selected paint layer.
        /// <see cref="PaintLayerKind.None"/> when no layer is selected.
        /// </summary>
        public static PaintLayerKind ActiveLayerKind { get; private set; } = PaintLayerKind.None;

        /// <summary>
        /// Fired after <see cref="SetActiveLayer"/> changes the active paint layer.
        /// Subscribers receive the new (id, kind) pair.
        ///
        /// Canonical consumers:
        ///   P6 — brush dispatch reads this to know which density channel to stamp.
        ///   P7 — prop placement reads this to know which instance layer to stamp.
        ///
        /// Contract: NEVER changes <c>Selection.activeObject</c> or <c>Selection.activeGameObject</c>.
        /// Clicking a palette square sets this state only; it never selects a tile or any Unity object.
        /// </summary>
        public static event System.Action<string, PaintLayerKind>? ActiveLayerChanged;

        /// <summary>
        /// Sets the active paint layer to (<paramref name="layerId"/>, <paramref name="kind"/>)
        /// and fires <see cref="ActiveLayerChanged"/> if the value changed.
        ///
        /// Pass <c>id = ""</c> and <c>kind = PaintLayerKind.None</c> to deselect.
        ///
        /// This method NEVER modifies <c>UnityEditor.Selection</c>.
        /// </summary>
        /// <param name="layerId">
        /// The layer's string ID. For scatter layers this is the sub-asset name
        /// (e.g. <c>"Layer_Meadow"</c>). For splat layers this is the splat entry's name field.
        /// </param>
        /// <param name="kind">The palette section this layer belongs to.</param>
        public static void SetActiveLayer(string layerId, PaintLayerKind kind)
        {
            bool changed = ActiveLayerId != layerId || ActiveLayerKind != kind;
            ActiveLayerId   = layerId;
            ActiveLayerKind = kind;
            if (changed)
                ActiveLayerChanged?.Invoke(layerId, kind);
        }

        // ── Active brush tool (P8 — generic draw interface) ───────────────────

        /// <summary>
        /// Id of the currently selected brush tool (e.g. <c>"height.raise"</c>). Mapped onto the
        /// active layer's tool set by <see cref="BrushToolRegistry.ResolveActiveTool"/>; an id
        /// that doesn't belong to the active kind falls back to that kind's default tool.
        /// Empty = use each kind's default. Drives <c>BindAndDispatch</c> + the dock tool palette.
        /// </summary>
        public static string ActiveBrushToolId { get; private set; } = string.Empty;

        /// <summary>Fired after <see cref="SetActiveBrushTool"/> changes the active tool.</summary>
        public static event System.Action<string>? ActiveBrushToolChanged;

        /// <summary>Sets the active brush tool id and fires <see cref="ActiveBrushToolChanged"/> when changed.</summary>
        public static void SetActiveBrushTool(string toolId)
        {
            if (ActiveBrushToolId == toolId) return;
            ActiveBrushToolId = toolId;
            ActiveBrushToolChanged?.Invoke(toolId);
        }

        /// <summary>
        /// True when the tool is a single-click action (no continuous brush stroke):
        /// <c>instance.place</c>, <c>instance.single</c>, <c>instance.select</c>.
        /// These tools bypass the <see cref="PaintModeActive"/> gate and do NOT auto-enable
        /// Paint Mode when selected — "Paint Mode" is reserved for continuous paint/erase
        /// strokes (density paint, terrain layer paint, height sculpt, etc.).
        /// </summary>
        public static bool IsClickOnlyTool(string toolId) =>
            toolId == "instance.place" || toolId == "instance.single" || toolId == "instance.select";

        // ── Active TerrainLayer palette index (Phase 2a/2b — new splat path) ──

        /// <summary>
        /// Index into <see cref="WorldMapAsset.TerrainPalette"/> for the active paint ink.
        /// -1 = no palette layer selected (brush dispatch returns early).
        /// </summary>
        public static int ActivePaletteIndex
        {
            get => activePaletteIndex;
            set
            {
                if (activePaletteIndex == value) return;
                activePaletteIndex = value;
                ActivePaletteIndexChanged?.Invoke(value);
            }
        }
        private static int activePaletteIndex = -1;

        /// <summary>Fired after <see cref="ActivePaletteIndex"/> changes.</summary>
        public static event System.Action<int>? ActivePaletteIndexChanged;

        // ── Active splat channel (unified SurfaceLayers paint) ────────────────

        // (Phase 3 cleanup — ActiveSplatChannel removed. ActivePaletteIndex above is the SSOT.)

        // ── Stroke tracking ───────────────────────────────────────────────────

        /// <summary>
        /// Full set of tile coords touched by the last completed stroke.
        /// Replaced at the start of each new stroke; never null but may be empty.
        /// </summary>
        public static readonly HashSet<Vector2Int> LastStrokedTileSet = new();

        /// <summary>Coord of the last tile that received a stroke this session.</summary>
        public static Vector2Int? LastStrokedCoord { get; set; }

        // ── Reset helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Clear stroke tracking state atomically.
        /// Call whenever <see cref="ActivePainter"/> changes.
        /// </summary>
        public static void ResetLastStroked()
        {
            LastStrokedCoord = null;
            LastStrokedTileSet.Clear();
        }

        /// <summary>Reset everything to defaults (domain-reload equivalent).</summary>
        public static void Reset()
        {
            ActivePainter    = null;
            ActiveLayerIndex = -1;
            ActiveLayerId    = string.Empty;
            ActiveLayerKind  = PaintLayerKind.None;
            ActiveBrushToolId  = string.Empty;
            ActiveLayerChanged = null;
            ActiveLayerIndexChanged = null;
            ActiveBrushToolChanged  = null;
            BrushFalloffDirty = null;
            paintModeActive   = false;
            PaintModeChanged  = null;
            tileEditMode      = TileEditModeKind.Off;
            TileEditModeChanged = null;
            activePaletteIndex = -1;
            ActivePaletteIndexChanged = null;
            ResetLastStroked();
        }
    }

    // ── BrushSettings ─────────────────────────────────────────────────────────

    /// <summary>Brush footprint shape. Drives both the editor preview AND the GPU stamp mask.</summary>
    public enum BrushShape
    {
        Circle = 0,
        Square = 1,
    }

    /// <summary>
    /// Unified brush parameters shared across all layer types.
    /// Design §5.1 SSOT — one vocabulary, one set of controls.
    /// </summary>
    [System.Serializable]
    public sealed class BrushSettings
    {
        private const float DEFAULT_SIZE_M    = 12f;
        private const float DEFAULT_STRENGTH  = 0.4f;
        private const float DEFAULT_SPACING_M = 2f;
        private const float DEFAULT_FLOW      = 0.8f;
        private const float DEFAULT_SET_HEIGHT = 0f;

        [Tooltip("Brush radius in world-space metres.")]
        [UnityEngine.Range(0.5f, 256f)]
        public float size = DEFAULT_SIZE_M;

        [Tooltip("Brush strength / opacity [0..1].")]
        [UnityEngine.Range(0f, 1f)]
        public float strength = DEFAULT_STRENGTH;

        [Tooltip("Raise/Lower target height in world metres. Raise rises toward this height " +
                 "(acts as a ceiling); Lower drops toward it (acts as a floor). Strength + Falloff " +
                 "still control how fast/where; Set Height is the limit the stroke converges to.")]
        [UnityEngine.Range(-20f, 20f)]
        public float setHeight = DEFAULT_SET_HEIGHT;

        [Tooltip("Falloff curve: X=normalized distance from centre [0..1], Y=weight [0..1].")]
        public AnimationCurve falloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Tooltip("Spacing between stamps along the drag path, in metres.")]
        [UnityEngine.Range(0.1f, 64f)]
        public float spacing = DEFAULT_SPACING_M;

        [Tooltip("Flow: accumulated deposit per stamp [0..1].")]
        [UnityEngine.Range(0f, 1f)]
        public float flow = DEFAULT_FLOW;

        [Tooltip("Brush footprint shape — circle (Euclidean falloff) or square (Chebyshev falloff).")]
        public BrushShape shape = BrushShape.Circle;

        [Tooltip("Optional imported grayscale brush mask. When set, the brush footprint is shaped " +
                 "by this texture (multiplied with the falloff) instead of a plain circle.")]
        public Texture2D? maskTexture = null;

        /// <summary>Returns a <see cref="BrushSettings"/> instance initialised to smart defaults.</summary>
        public static BrushSettings Default => new BrushSettings();
    }
}
