#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Thin abstract base for unified WorldPainter authoring layers (splat + grass).
    /// Stored as sub-assets in <see cref="WorldMapAsset.SurfaceLayers"/>. Concrete types own
    /// their config + palette independently; this base provides only the shared identity needed
    /// for uniform storage and inspector routing.
    ///
    /// Mirrors the thin-marker philosophy of <see cref="ScatterLayer"/> — minimal shared state;
    /// behaviour lives in the concrete type and its consumer (terrain shader for splat,
    /// frozen scatter engines for grass).
    /// </summary>
    public abstract class WorldPainterLayer : ScriptableObject
    {
        /// <summary>
        /// Authoring kind — drives inspector routing and the painted-control semantics
        /// (RGBA blend weights for splat; per-variant R8 density channels for grass).
        /// </summary>
        public enum LayerKind
        {
            /// <summary>Terrain blend: palette albedos blended by a per-tile RGBA weight map.</summary>
            Splat,

            /// <summary>Density scatter: palette variants scattered by per-variant R8 density channels.</summary>
            Grass,

            /// <summary>Authored instances: props placed explicitly via <see cref="AuthoredInstancesData"/>.</summary>
            Prop,
        }

        [Tooltip("Display name shown in the surface-layer stack. Falls back to the asset name when empty.")]
        [SerializeField] private string displayName = string.Empty;

        /// <summary>Display name for the surface-layer stack UI (falls back to the asset name).</summary>
        public string DisplayName =>
            string.IsNullOrEmpty(this.displayName) ? this.name : this.displayName;

        [Tooltip("When off, this layer is skipped by scatter/render builds (the eye toggle in the WorldPainter layer stack).")]
        [SerializeField] private bool enabled = true;

        /// <summary>
        /// When false the layer is excluded from scatter/render builds — the per-layer visibility
        /// toggle in the WorldPainter layer stack. Defaults to true.
        /// </summary>
        public bool Enabled
        {
            get => this.enabled;
            set => this.enabled = value;
        }

        /// <summary>Concrete authoring kind.</summary>
        public abstract LayerKind Kind { get; }

        /// <summary>Number of palette entries (albedos for splat, variants for grass).</summary>
        public abstract int PaletteCount { get; }
    }
}
