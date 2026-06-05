#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract
{
    /// <summary>
    /// Render driver: pick ONE LOD mesh for the WHOLE field by distance to a reference viewpoint and submit
    /// every instancing slab via <see cref="Graphics.RenderMeshInstanced"/> under one field-wide bounds.
    ///
    /// CRITICAL — call site: this MUST be driven from the player loop (<c>MonoBehaviour.LateUpdate</c> in play,
    /// <c>EditorApplication.update</c> in edit), NOT from <see cref="RenderPipelineManager.beginCameraRendering"/>.
    /// Under URP's RenderGraph (Unity 6 default, <c>enableRenderCompatibilityMode = false</c>) immediate-mode
    /// instanced draws issued from the <c>beginCameraRendering</c> callback are silently dropped — the callback
    /// fires and the draw call runs, but nothing reaches the screen. Issued from the player loop, the same call
    /// renders correctly. Each draw uses <c>rp.camera = null</c> so the grass appears in every camera (Game view
    /// AND Scene view), and a field-wide <see cref="RenderParams.worldBounds"/> (MANDATORY for RenderMeshInstanced
    /// — its default zero-extent box culls everything) keeps the whole field visible.
    ///
    /// Do NOT route any per-draw data through a <see cref="MaterialPropertyBlock"/> (<c>rp.matProps</c>): setting
    /// matProps on RenderMeshInstanced here makes the whole draw render NOTHING under RenderGraph (geometry count
    /// drops to zero). One field-wide bounds means no off-screen frustum culling — accepted for a contained field.
    /// </summary>
    public sealed class GrassRenderer
    {
        private readonly float[] lodMaxSqrDistances;
        private readonly Mesh[] lodMeshes; // snapshot — never read live config in the hot loop
        private readonly Material? grassMaterial;
        private RenderParams renderParams;
        private readonly bool hasParams;

        public GrassRenderer(GrassLODConfig config, Vector3 origin)
        {
            // Precompute squared LOD thresholds so field-LOD selection avoids a sqrt.
            float[] dists = config.LodMaxDistances;
            this.lodMaxSqrDistances = new float[dists.Length];
            for (int i = 0; i < dists.Length; ++i)
                this.lodMaxSqrDistances[i] = dists[i] * dists[i];

            // Snapshot the LOD meshes so a live inspector edit can't desync index vs threshold mid-frame.
            this.lodMeshes = (Mesh[])config.LodMeshes.Clone();
            this.grassMaterial = config.GrassMaterial;

            if (this.grassMaterial != null)
            {
                this.renderParams = new RenderParams(this.grassMaterial)
                {
                    shadowCastingMode = config.ShadowCastingMode,
                    receiveShadows = false,
                    layer = 0,
                    camera = null, // render in ALL cameras (game + scene view) — see class summary
                };
                this.hasParams = true;
            }
        }

        /// <summary>
        /// Submit every slab for instanced rendering in all cameras. ONE LOD mesh is chosen for the WHOLE
        /// field by distance from <paramref name="lodReferencePos"/> (the main camera position) to the field
        /// center, under one field-wide <paramref name="worldBounds"/>. In Phase 2 the
        /// <paramref name="renderSlabs"/> ARE the static base slabs; Phase 3 swaps in the simulator output.
        /// </summary>
        public void Render(Vector3 lodReferencePos, Matrix4x4[][] renderSlabs, int[] slabCounts,
            Bounds worldBounds)
        {
            if (!this.hasParams)
                return;

            // ONE LOD for the whole field: distance from the camera to the field center.
            float sqrDist = (worldBounds.center - lodReferencePos).sqrMagnitude;
            Mesh mesh = this.lodMeshes[this.SelectLod(sqrDist)];

            RenderParams rp = this.renderParams;
            // worldBounds is MANDATORY for RenderMeshInstanced — the default (a zero-extent box at the origin)
            // culls EVERY instance. Set the field-wide AABB ONCE (already includes blade height + bend headroom).
            rp.worldBounds = worldBounds;

            for (int b = 0; b < renderSlabs.Length; ++b)
                Graphics.RenderMeshInstanced(rp, mesh, 0, renderSlabs[b], slabCounts[b]);
        }

        private int SelectLod(float sqrDist)
        {
            for (int i = 0; i < this.lodMaxSqrDistances.Length; ++i)
            {
                if (sqrDist <= this.lodMaxSqrDistances[i])
                    return i;
            }

            return this.lodMaxSqrDistances.Length; // beyond the last threshold → farthest LOD
        }
    }
}
