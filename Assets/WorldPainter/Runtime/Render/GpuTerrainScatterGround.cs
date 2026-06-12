#nullable enable
using UnityEngine;
using WorldPainter;

namespace WorldPainter
{
    /// <summary>
    /// Bridge MonoBehaviour: wires <see cref="HeightmapSurfaceSampler"/> into a
    /// <see cref="ScatterField"/> so scatter placement grounds on the custom heightmap
    /// terrain rather than the legacy <c>TerrainData</c> or Physics.Raycast path.
    ///
    /// ── Decoupling boundary ──────────────────────────────────────────────────
    /// This is the ONLY place WorldPainter and WorldPainter meet at runtime.
    /// <c>WorldPainter</c> contains zero WorldPainter references (library-decoupling
    /// compliant). WorldPainter references WorldPainter solely to implement
    /// <see cref="ISurfaceSampler"/> (one-way: WorldPainter → WorldPainter).
    ///
    /// Usage:
    ///   1. Place this component on the same GameObject as (or a parent of) ScatterField.
    ///   2. Assign <c>tileAsset</c> (single-tile / demo) or connect the streaming path.
    ///   3. ScatterField will use <see cref="HeightmapSurfaceSampler"/> on next Rebuild.
    /// </summary>
    [AddComponentMenu("WorldPainter/Gpu Terrain Scatter Ground")]
    [DisallowMultipleComponent]
    public sealed class GpuTerrainScatterGround : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Tooltip("Single tile asset (demo / single-tile mode). " +
                 "Assign a TerrainTileAsset to ground scatter on that tile.")]
        [SerializeField] private TerrainTileAsset? tileAsset;

        [Tooltip("The ScatterField to inject the sampler into. " +
                 "If null, searches children automatically on Enable.")]
        [SerializeField] private ScatterField? scatterField;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            this.Wire();
        }

        private void OnDisable()
        {
            this.Unwire();
        }

        // ── Wiring ────────────────────────────────────────────────────────────

        private void Wire()
        {
            if (this.tileAsset == null)
            {
                Debug.LogWarning(
                    $"[{nameof(GpuTerrainScatterGround)}] tileAsset is null — " +
                    "assign a TerrainTileAsset to ground scatter.", this);
                return;
            }

            ScatterField? field = this.ResolveField();
            if (field == null)
            {
                Debug.LogWarning(
                    $"[{nameof(GpuTerrainScatterGround)}] No ScatterField found. " +
                    "Assign one or place this component on the same GameObject.", this);
                return;
            }

            field.ExternalSampler = new HeightmapSurfaceSampler(this.tileAsset);
            field.Rebuild();
        }

        private void Unwire()
        {
            ScatterField? field = this.ResolveField();
            if (field != null && field.ExternalSampler is HeightmapSurfaceSampler)
            {
                field.ExternalSampler = null;
                // Do NOT call Rebuild on Disable — that would trigger a rebuild during teardown.
            }
        }

        private ScatterField? ResolveField()
        {
            if (this.scatterField != null) return this.scatterField;
            return this.GetComponentInChildren<ScatterField>();
        }
    }
}
