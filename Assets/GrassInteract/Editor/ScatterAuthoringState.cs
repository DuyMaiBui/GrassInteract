#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// ScriptableSingleton that is the single source of truth for all authoring tool state:
    /// density-paint brush settings, instance-placement settings, and the active stamp reference.
    /// Replaces the scattered <c>EditorPrefs</c> reads in <see cref="DensityPaintTool"/> and
    /// <see cref="InstancePlacementTool"/>.
    /// </summary>
    internal sealed class ScatterAuthoringState : ScriptableSingleton<ScatterAuthoringState>
    {
        // ── Internal accessor ──────────────────────────────────────────────────

        /// <summary>Shorthand for the singleton instance.</summary>
        internal static ScatterAuthoringState I => instance;

        // ── Paint settings ─────────────────────────────────────────────────────

        [SerializeField] private float brushSize    = 3f;
        [SerializeField] private float brushOpacity = 1f;
        [SerializeField] private float brushFalloff = 0.5f;
        [SerializeField] private float brushFlow    = 0.5f;
        [SerializeField] private int   paintMode    = 0; // maps to DensityPaintTool.PaintMode enum

        internal float BrushSize
        {
            get => this.brushSize;
            set { this.brushSize = value; this.Save(true); }
        }

        internal float BrushOpacity
        {
            get => this.brushOpacity;
            set { this.brushOpacity = value; this.Save(true); }
        }

        internal float BrushFalloff
        {
            get => this.brushFalloff;
            set { this.brushFalloff = value; this.Save(true); }
        }

        internal float BrushFlow
        {
            get => this.brushFlow;
            set { this.brushFlow = value; this.Save(true); }
        }

        internal int PaintMode
        {
            get => this.paintMode;
            set { this.paintMode = value; this.Save(true); }
        }

        // ── Stamp reference ────────────────────────────────────────────────────

        [SerializeField] private StampRef activeStamp = default;

        internal StampRef ActiveStamp
        {
            get => this.activeStamp;
            set { this.activeStamp = value; this.Save(true); }
        }

        // ── Place settings ─────────────────────────────────────────────────────

        [SerializeField] private int   placeMode     = 0; // maps to InstancePlacementTool.PlaceMode enum
        [SerializeField] private bool  alignToNormal = false;
        [SerializeField] private bool  randomYaw     = true;
        [SerializeField] private float placeScaleMin = 1f;
        [SerializeField] private float placeScaleMax = 1f;
        [SerializeField] private float eraseRadius   = 2f;

        internal int PlaceMode
        {
            get => this.placeMode;
            set { this.placeMode = value; this.Save(true); }
        }

        internal bool AlignToNormal
        {
            get => this.alignToNormal;
            set { this.alignToNormal = value; this.Save(true); }
        }

        internal bool RandomYaw
        {
            get => this.randomYaw;
            set { this.randomYaw = value; this.Save(true); }
        }

        internal float PlaceScaleMin
        {
            get => this.placeScaleMin;
            set { this.placeScaleMin = value; this.Save(true); }
        }

        internal float PlaceScaleMax
        {
            get => this.placeScaleMax;
            set { this.placeScaleMax = value; this.Save(true); }
        }

        internal float EraseRadius
        {
            get => this.eraseRadius;
            set { this.eraseRadius = value; this.Save(true); }
        }

        // ── Active layer (transient — not persisted across domain reload) ─────────
        //
        // NOT a [SerializeField]: DensityScatterLayer is a sub-asset whose identity must be
        // re-established each editor session. DensityPaintPanel.BindLayer() sets this when the
        // user selects a layer in Scatter Studio; DensityPaintTool (global, unscoped EditorTool)
        // reads it instead of this.target (which is always null for a global EditorTool).

        [System.NonSerialized]
        private DensityScatterLayer? activeLayer;

        internal DensityScatterLayer? ActiveLayer
        {
            get => this.activeLayer;
            set => this.activeLayer = value;
        }

        // ── Active instance layer (transient — not persisted across domain reload) ──
        //
        // NOT a [SerializeField]: InstanceScatterLayer is a sub-asset. InstancePanel.BindLayer()
        // sets this when the user selects a layer; InstancePlacementTool (global, unscoped
        // EditorTool) reads it instead of this.target (always null for a global EditorTool).

        [System.NonSerialized]
        private InstanceScatterLayer? activeInstanceLayer;

        internal InstanceScatterLayer? ActiveInstanceLayer
        {
            get => this.activeInstanceLayer;
            set => this.activeInstanceLayer = value;
        }

        // ── Overlay visibility ─────────────────────────────────────────────────

        [SerializeField] private bool overlayVisible = false;

        /// <summary>Whether the density heatmap overlay is drawn in the Scene view.</summary>
        internal bool OverlayVisible
        {
            get => this.overlayVisible;
            set { this.overlayVisible = value; this.Save(true); }
        }
    }

    // ── StampRef ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializable reference to the active brush stamp. Stores only a <see cref="StampSource"/>
    /// discriminator and an <see cref="int"/> index — no reference to <c>ScatterBrushLibrary</c>
    /// or any config type. Resolution to a concrete <see cref="UnityEngine.Texture2D"/> is the
    /// responsibility of the consuming tool (Phase 2.B).
    /// </summary>
    [System.Serializable]
    internal struct StampRef
    {
        /// <summary>Discriminates which collection the <see cref="index"/> addresses.</summary>
        internal enum StampSource
        {
            /// <summary>No stamp selected; use the procedural falloff kernel.</summary>
            None = 0,
            /// <summary>Index into <c>TerrainScatterConfig.BrushStamps</c> (the active field's config).</summary>
            Config = 1,
            /// <summary>Index into the project-wide <c>ScatterBrushLibrary</c> (created in Phase 2.A).</summary>
            Global = 2,
        }

        [SerializeField] private StampSource source;
        [SerializeField] private int         index;

        internal StampRef(StampSource source, int index)
        {
            this.source = source;
            this.index  = index;
        }

        internal StampSource Source => this.source;
        internal int         Index  => this.index;

        /// <summary>Returns true when no stamp is selected (procedural falloff).</summary>
        internal bool IsNone => this.source == StampSource.None;
    }
}
