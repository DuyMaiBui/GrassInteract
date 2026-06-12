#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Per-LOD mesh + maximum camera distance pair.
    /// Used by <see cref="ScatterRenderConfig.Lods"/> to unify LOD data for all layer kinds.
    /// The LOD is active when camera distance &lt;= <see cref="maxDistance"/>. LOD0 should have
    /// the smallest maxDistance (highest detail, close range); the last entry covers all remaining
    /// distances.
    /// </summary>
    [System.Serializable]
    public struct ScatterLod
    {
        [Tooltip("Mesh for this LOD level. LOD0 = highest detail.")]
        public Mesh? mesh;

        [Tooltip("Maximum camera distance (metres) at which this LOD is still active. " +
                 "The last LOD covers all remaining distances beyond the previous entry.")]
        [Min(0f)]
        public float maxDistance;
    }

    /// <summary>
    /// Thin serialization marker for scatter layers. ZERO fields, ZERO shared behavior.
    /// Concrete subclasses compose their own config independently.
    ///
    /// Exists solely so <see cref="TerrainScatterConfig"/> can hold a <c>List&lt;ScatterLayer&gt;</c>
    /// and Unity's serializer can persist sub-assets.
    ///
    /// <para>
    /// To add a new layer type (e.g. GridScatterLayer, SplineScatterLayer):
    /// 1. Create a new <c>ScriptableObject</c> inheriting <see cref="ScatterLayer"/>.
    /// 2. Compose whichever config structs you need.
    /// 3. Override <see cref="CreatePlacement"/> to return a custom <see cref="IScatterPlacement"/>.
    /// 4. No changes to any existing layer type or engine are required.
    /// </para>
    /// </summary>
    public abstract class ScatterLayer : ScriptableObject
    {
        /// <summary>Wind model the GPU shader applies. CPU simulator keeps Sine regardless.</summary>
        public enum WindMode
        {
            /// <summary>Directional sin() wave per blade (legacy). Cheapest; matches CPU sim exactly.</summary>
            Sine,

            /// <summary>2-octave Perlin gust + ripple sampled in the vertex shader (GPU-only).</summary>
            Perlin,
        }

        // ── Abstract config accessors ──────────────────────────────────────────
        // Concrete types compose their own config structs and expose them here.
        // Engines read through these accessors — they never cast to concrete types.

        public abstract ScatterRenderConfig   Render   { get; }
        public abstract ScatterWindConfig     Wind     { get; }
        public abstract ScatterDeformConfig   Deform   { get; }
        public abstract ScatterBoundsConfig   Bounds   { get; }
        public abstract ScatterPlacementConfig Placement { get; }

        // ── Abstract placement data (engine bounds / shader uniforms) ──────────

        /// <summary>World-space XZ size of the field. Used by engines for chunking bounds.</summary>
        public abstract Vector2 FieldBounds { get; }

        /// <summary>Instance scale random range [min, max]. Used by engines for cull AABB.</summary>
        public abstract Vector2 ScaleRange { get; }

        /// <summary>Uniform rotation offset applied to every instance. Passed to shader.</summary>
        public abstract Vector3 RotationOffsetEuler { get; }

        /// <summary>True when per-instance oriented packing is needed in GPU buffers.</summary>
        public abstract bool IsOriented { get; }

        // ── Abstract factory ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the placement strategy for this layer.
        /// Concrete subclasses implement this to return their respective strategy.
        /// </summary>
        public abstract IScatterPlacement CreatePlacement();

        // ── Virtual validation ─────────────────────────────────────────────────

        /// <summary>
        /// Validates the layer. Base implementation is a pass-through.
        /// Subclasses override to add type-specific validation.
        /// </summary>
        public virtual bool Validate(out string error)
        {
            error = string.Empty;
            return true;
        }
    }
}
