#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract
{
    /// <summary>
    /// Render driver: pick a LOD mesh per chunk by distance to a reference viewpoint and submit GPU-instanced
    /// draws via <see cref="Graphics.RenderMeshInstanced"/>.
    ///
    /// CRITICAL — call site: this MUST be driven from the player loop (<c>MonoBehaviour.LateUpdate</c> in play,
    /// <c>EditorApplication.update</c> in edit), NOT from <see cref="RenderPipelineManager.beginCameraRendering"/>.
    /// Under URP's RenderGraph (Unity 6 default, <c>enableRenderCompatibilityMode = false</c>) immediate-mode
    /// instanced draws issued from the <c>beginCameraRendering</c> callback are silently dropped — the callback
    /// fires and the draw call runs, but nothing reaches the screen. Issued from the player loop, the same call
    /// renders correctly. Each draw uses <c>rp.camera = null</c> so the grass appears in every camera (Game view
    /// AND Scene view), and a per-chunk <see cref="RenderParams.worldBounds"/> (MANDATORY for RenderMeshInstanced
    /// — its default zero-extent box culls everything) lets the GPU cull off-screen chunks.
    ///
    /// Trample/deform inputs reach the shader as plain SHADER GLOBALS (<c>Shader.SetGlobalTexture</c> /
    /// <c>SetGlobalFloat</c>) — verified to bind correctly for these draws. Do NOT route the trample map through
    /// a per-draw <see cref="MaterialPropertyBlock"/> (<c>rp.matProps</c>): setting matProps on RenderMeshInstanced
    /// here makes the whole draw render NOTHING under RenderGraph (geometry count drops to zero).
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
            // Precompute squared LOD thresholds so per-chunk selection avoids a sqrt.
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
        /// Submit every chunk for instanced rendering in all cameras. LOD is chosen per chunk by distance to
        /// <paramref name="lodReferencePos"/> (the main camera position). GPU-side per-chunk culling is handled
        /// by each draw's <see cref="RenderParams.worldBounds"/>, so off-screen chunks cost almost nothing.
        /// </summary>
        public void Render(Vector3 lodReferencePos, GrassChunk[] chunks)
        {
            if (!this.hasParams)
                return;

            RenderParams rp = this.renderParams;

            for (int i = 0; i < chunks.Length; ++i)
            {
                GrassChunk chunk = chunks[i];

                float sqrDist = (chunk.Center - lodReferencePos).sqrMagnitude;
                Mesh mesh = this.lodMeshes[this.SelectLod(sqrDist)];

                // worldBounds is MANDATORY for RenderMeshInstanced — the default (a zero-extent box at the
                // origin) culls EVERY instance. Use the chunk AABB (already includes blade height + bend
                // headroom) so bent blades are never wrongly culled.
                rp.worldBounds = chunk.Bounds;

                Matrix4x4[][] batches = chunk.Batches;
                int[] counts = chunk.BatchCounts;
                for (int b = 0; b < batches.Length; ++b)
                    Graphics.RenderMeshInstanced(rp, mesh, 0, batches[b], counts[b]);
            }
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
