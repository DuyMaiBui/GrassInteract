#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// A grayscale brush stamp used by the scatter painter. White pixels = full opacity,
    /// black pixels = no paint. Instances are stored as sub-assets inside
    /// <see cref="TerrainScatterConfig"/>; do NOT use <c>Assets &gt; Create</c> directly.
    /// </summary>
    public sealed class BrushStamp : ScriptableObject
    {
        [Tooltip("Human-readable label shown in the brush library. Falls back to the asset name if empty.")]
        [SerializeField] private string displayName = "Stamp";

        [Tooltip("Grayscale R8 texture: white = full brush opacity, black = transparent edge. " +
                 "Must be readable. Provided as a sub-asset of TerrainScatterConfig.")]
        [PreviewField(64)]
        [SerializeField] private Texture2D? shape;

        // ── Public accessors ──────────────────────────────────────────────────

        /// <summary>
        /// Display label for the brush library. Returns <see cref="ScriptableObject.name"/>
        /// when <see cref="displayName"/> is empty.
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(this.displayName) ? this.name : this.displayName;

        /// <summary>Grayscale stamp texture. May be null before a shape is assigned.</summary>
        public Texture2D? Shape => this.shape;

#if UNITY_EDITOR
        /// <summary>
        /// Sets the shape texture and display name. Called by <see cref="TerrainScatterConfig"/>
        /// during stamp creation; not intended for runtime use.
        /// </summary>
        internal void SetShape(Texture2D tex, string stampName)
        {
            this.shape = tex;
            this.displayName = stampName;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
