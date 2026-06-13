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
        /// Legacy index arithmetic is still used for Height (index=0/-1) and Biome rows
        /// (which are not unified surface layers).
        /// </summary>
        /// <param name="painter">The active WorldPainter.</param>
        /// <param name="splatChannel">
        /// Output: 0-based splat channel [0..3] if the active layer is Splat; -1 otherwise.
        /// </param>
        /// <returns>The <see cref="LayerType"/> of the active layer.</returns>
        public static LayerType ActiveLayerType(WorldPainter painter, out int splatChannel)
        {
            splatChannel = -1;

            // Phase 2 — unified surface layer is active: derive kind from ActiveLayerKind.
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

            // Legacy path: derive from the raw display index.
            int idx = ActiveLayerIndex;
            if (idx <= 0) return LayerType.Height; // 0 or -1 = Height base

            // Biome rows. The stack now lists only biome rows after unified surface rows
            // (splatLayers/scatterLayers removed in Phase 5b — no index arithmetic available).
            // Fall through to Height for any index we can't classify.
            int biomeOffset = idx - 1; // legacy index 1+ maps to biome offset in the simple case
            if (biomeOffset >= 0 && biomeOffset < painter.Biomes.Count)
                return LayerType.Biome;

            return LayerType.Height;
        }

        /// <summary>
        /// The effective layer type that drives brush-tool dispatch + the contextual palette.
        ///
        /// Phase 2: combines THREE selection signals in priority order:
        ///   1. <see cref="ActiveLayerKind"/> — unified surface layer (Splat/Meadow/Prop).
        ///   2. <see cref="ActiveBiomeIndex"/> ≥ 0 — biome row selected via direct index.
        ///   3. Legacy <see cref="ActiveLayerType"/> index path (Height fallback).
        /// </summary>
        public static LayerType EffectiveLayerType(WorldPainter painter)
        {
            PaintLayerKind p5 = ActiveLayerKind;

            // 1. Unified surface layer takes priority.
            if (p5 == PaintLayerKind.Splat)  return LayerType.Splat;
            if (p5 == PaintLayerKind.Meadow) return LayerType.Grass;
            if (p5 == PaintLayerKind.Prop)   return LayerType.Props;

            // 2. Biome row selected directly (Phase 2 biome path).
            if (ActiveBiomeIndex >= 0 && ActiveBiomeIndex < painter.Biomes.Count)
                return LayerType.Biome;

            // 3. Legacy index-based routing (Height base row, or legacy splat/scatter/biome).
            LayerType legacy = ActiveLayerType(painter, out _);
            if (legacy == LayerType.Splat)  return LayerType.Splat;
            if (legacy == LayerType.Grass)  return LayerType.Grass;
            if (legacy == LayerType.Props)  return LayerType.Props;
            if (legacy == LayerType.Biome)  return LayerType.Biome;
            return LayerType.Height;
        }

        /// <summary>
        /// Returns the 0-based index into <see cref="WorldPainter.Biomes"/> for the
        /// currently active layer, or -1 when the active layer is not a Biome layer.
        ///
        /// Phase 2: prefers <see cref="ActiveBiomeIndex"/> when it is ≥ 0 (set directly by
        /// the stack's biome row selection). Falls back to the legacy display-index arithmetic
        /// for tests and code that still sets <see cref="ActiveLayerIndex"/> directly.
        /// </summary>
        public static int ActiveBiomeLayerIndex(WorldPainter painter)
        {
            // Direct path: biome was selected by setting ActiveBiomeIndex directly.
            if (ActiveBiomeIndex >= 0 && ActiveBiomeIndex < painter.Biomes.Count)
                return ActiveBiomeIndex;

            // Legacy fallback: derive from raw display index.
            int idx         = ActiveLayerIndex;
            int biomeOffset = idx - 1; // index 1+ used by biome rows when no surface layers
            if (biomeOffset >= 0 && biomeOffset < painter.Biomes.Count)
                return biomeOffset;
            return -1;
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

        // ── Active biome (P5) ─────────────────────────────────────────────────

        /// <summary>
        /// Index of the currently selected biome in <see cref="WorldPainter.Biomes"/>.
        /// -1 = no biome selected.
        /// </summary>
        public static int ActiveBiomeIndex { get; set; } = -1;

        // ── Active splat channel (unified SurfaceLayers paint) ────────────────

        /// <summary>
        /// The RGBA channel index [0..3] the splat brush writes to when a unified
        /// <see cref="SplatLayer"/> is active (Phase 3 paint routing).
        /// -1 = no channel selected / use legacy path.
        ///
        /// Set together with <see cref="SetActiveLayer"/>(splatLayer.name, Splat) when the user
        /// clicks an albedo slot sub-row in the layer stack. The splat brush reads this value
        /// and passes it as <c>_SplatLayer</c> to the GPU compute kernel, replacing the old
        /// index-arithmetic path for the unified case.
        /// </summary>
        public static int ActiveSplatChannel { get; set; } = -1;

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
            ActiveBiomeIndex = -1;
            ActiveSplatChannel = -1;
            ActiveLayerId    = string.Empty;
            ActiveLayerKind  = PaintLayerKind.None;
            ActiveBrushToolId  = string.Empty;
            ActiveLayerChanged = null;
            ActiveLayerIndexChanged = null;
            ActiveBrushToolChanged  = null;
            BrushFalloffDirty = null;
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

        [Tooltip("Brush radius in world-space metres.")]
        [UnityEngine.Range(0.5f, 256f)]
        public float size = DEFAULT_SIZE_M;

        [Tooltip("Brush strength / opacity [0..1].")]
        [UnityEngine.Range(0f, 1f)]
        public float strength = DEFAULT_STRENGTH;

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

        /// <summary>Returns a <see cref="BrushSettings"/> instance initialised to smart defaults.</summary>
        public static BrushSettings Default => new BrushSettings();
    }
}
