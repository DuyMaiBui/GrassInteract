#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// ScriptableSingleton that is the single source of truth for all authoring tool state:
    /// density-paint brush settings, instance-placement settings, and the active stamp reference.
    /// Replaces the scattered <c>EditorPrefs</c> reads in <see cref="DensityPaintTool"/> and
    /// <see cref="InstancePlacementTool"/>; legacy EditorPrefs values are migrated exactly once
    /// at domain load via <see cref="MigrateFromEditorPrefs"/>.
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

        // ── Overlay visibility ─────────────────────────────────────────────────

        [SerializeField] private bool overlayVisible = false;

        /// <summary>Whether the density heatmap overlay is drawn in the Scene view.</summary>
        internal bool OverlayVisible
        {
            get => this.overlayVisible;
            set { this.overlayVisible = value; this.Save(true); }
        }

        // ── Migration flag ─────────────────────────────────────────────────────

        [SerializeField] private bool migratedFromEditorPrefs = false;

        // ── Legacy EditorPrefs keys (read-only constants) ──────────────────────

        private const string K_BRUSH_SIZE    = "GrassInteract.Brush.Size";
        private const string K_BRUSH_OPACITY = "GrassInteract.Brush.Opacity";
        private const string K_BRUSH_FALLOFF = "GrassInteract.Brush.Falloff";
        private const string K_BRUSH_FLOW    = "GrassInteract.Brush.Flow";
        private const string K_BRUSH_MODE    = "GrassInteract.Brush.Mode";
        private const string K_BRUSH_STAMP   = "GrassInteract.Brush.Stamp";

        private const string K_PLACE_MODE    = "GrassInteract.Place.Mode";
        private const string K_PLACE_ALIGN   = "GrassInteract.Place.Align";
        private const string K_PLACE_YAW     = "GrassInteract.Place.RandYaw";
        private const string K_PLACE_SMIN    = "GrassInteract.Place.ScaleMin";
        private const string K_PLACE_SMAX    = "GrassInteract.Place.ScaleMax";
        private const string K_PLACE_ERASE   = "GrassInteract.Place.EraseRadius";

        // ── One-time migration ─────────────────────────────────────────────────

        /// <summary>
        /// Runs once per project, at domain load, to lift legacy EditorPrefs values into this
        /// singleton. Gated on <see cref="migratedFromEditorPrefs"/> — subsequent domain reloads
        /// skip this entirely.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void MigrateFromEditorPrefs()
        {
            if (I.migratedFromEditorPrefs) return;

            // Paint settings — read with the exact same defaults the original tool used.
            I.brushSize    = EditorPrefs.GetFloat(K_BRUSH_SIZE,    3f);
            I.brushOpacity = EditorPrefs.GetFloat(K_BRUSH_OPACITY, 1f);
            I.brushFalloff = EditorPrefs.GetFloat(K_BRUSH_FALLOFF, 0.5f);
            I.brushFlow    = EditorPrefs.GetFloat(K_BRUSH_FLOW,    0.5f);
            I.paintMode    = EditorPrefs.GetInt  (K_BRUSH_MODE,    0);

            // Stamp: old key stored the int index (-1 = none/procedural).
            int legacyStampIdx = EditorPrefs.GetInt(K_BRUSH_STAMP, -1);
            I.activeStamp = legacyStampIdx < 0
                ? new StampRef(StampRef.StampSource.None,   0)
                : new StampRef(StampRef.StampSource.Config, legacyStampIdx);

            // Place settings — read with the exact same defaults the original tool used.
            I.placeMode    = EditorPrefs.GetInt  (K_PLACE_MODE,  0);
            I.alignToNormal= EditorPrefs.GetBool (K_PLACE_ALIGN, false);
            I.randomYaw    = EditorPrefs.GetBool (K_PLACE_YAW,   true);
            I.placeScaleMin= EditorPrefs.GetFloat(K_PLACE_SMIN,  1f);
            I.placeScaleMax= EditorPrefs.GetFloat(K_PLACE_SMAX,  1f);
            I.eraseRadius  = EditorPrefs.GetFloat(K_PLACE_ERASE, 2f);

            I.migratedFromEditorPrefs = true;
            I.Save(true);

            Debug.Log("[ScatterAuthoringState] Migrated tool settings from EditorPrefs to ScatterAuthoringState singleton. This runs exactly once.");
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
