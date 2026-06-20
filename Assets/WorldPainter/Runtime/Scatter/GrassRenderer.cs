#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter
{
    /// <summary>
    /// Render driver: pick ONE LOD mesh for the WHOLE field by distance to a reference viewpoint and submit
    /// every instancing slab via <see cref="Graphics.RenderMeshInstanced"/> under one field-wide bounds.
    ///
    /// LOD meshes/distances, material, shadow mode, and wind parameters all live on the
    /// <see cref="ScatterLayer"/> config structs (SSOT, post-refactor).
    ///
    /// CRITICAL — call site differs by mode (<see cref="ScatterField"/> owns this):
    /// • PLAY: drive from the player loop (<c>MonoBehaviour.LateUpdate</c>) with <paramref name="targetCamera"/>
    ///   = null, so the draw renders in EVERY camera (Game + Scene view).
    /// • EDIT: drive from <see cref="RenderPipelineManager.beginCameraRendering"/> with
    ///   <paramref name="targetCamera"/> = the camera being rendered. Submitting an instanced draw from
    ///   <c>EditorApplication.update</c> (outside any camera-render) leaves the material's UnityPerMaterial
    ///   constant buffer UNBOUND in the Scene view — the geometry draws but <c>_BaseColor</c>/<c>_TipColor</c>
    ///   read 0, so the blades render solid BLACK. Issuing the draw from inside the render loop
    ///   (beginCameraRendering) binds the cbuffer, so the blades render with their real colors. Targeting the
    ///   specific camera per callback avoids N× duplicate submissions across the editor's cameras.
    ///
    /// A field-wide <see cref="RenderParams.worldBounds"/> is MANDATORY for RenderMeshInstanced — its default
    /// zero-extent box culls everything. Do NOT route per-draw data through a <see cref="MaterialPropertyBlock"/>
    /// (<c>rp.matProps</c>): it makes the whole draw render NOTHING under RenderGraph. One field-wide bounds
    /// means no off-screen frustum culling — accepted for a contained field.
    ///
    /// 0-LOD graceful handling: when the layer has no LOD meshes the renderer logs a warning at build time
    /// and skips the draw each frame without throwing. The main loop must populate layer.Render.Lods before testing.
    /// </summary>
    public sealed class GrassRenderer
    {
        private readonly float[] lodMaxSqrDistances;
        private readonly float cullSqrDistance;
        private readonly Mesh[] lodMeshes; // snapshot — never read live config in the hot loop
        private readonly Material? grassMaterial;
        private RenderParams renderParams;
        private readonly bool hasParams;
        private readonly bool hasLods;

        /// <summary>
        /// Builds the renderer from a <paramref name="layer"/> (LOD meshes + distances, material, shadow mode).
        /// Layer data is snapshotted at construction so a live inspector edit cannot desync index vs threshold mid-frame.
        /// </summary>
        public GrassRenderer(ScatterLayer layer, Vector3 origin)
        {
            // Snapshot LOD data from the layer (SSOT: all render params live on the layer).
            var render = layer.Render;
            Mesh[] meshes = render.LodMeshes;
            float[] dists = render.LodMaxDistances;

            this.hasLods = meshes.Length > 0;

            if (!this.hasLods)
            {
                // 0-LOD graceful: log once at build, skip draws until migrated.
                WpLog.Warning(
                    $"[GrassRenderer] Layer '{layer.name}' has no LOD meshes in its lods array." +
                    " Assign blade meshes to the layer's lods field. No grass will render until lods is populated.");
            }

            // Precompute squared LOD thresholds.
            this.lodMaxSqrDistances = new float[dists.Length];
            for (int i = 0; i < dists.Length; ++i)
                this.lodMaxSqrDistances[i] = dists[i] * dists[i];

            float cull = render.RenderCullDistance;
            this.cullSqrDistance = cull * cull;

            this.lodMeshes = (Mesh[])meshes.Clone();
            this.grassMaterial = render.Material;

            if (this.grassMaterial != null)
            {
                // Graphics.RenderMeshInstanced hard-requires GPU instancing on the material.
                // The CPU grass tier draws through it, so ensure the flag is set (a non-instanced
                // grass material would otherwise throw InvalidOperationException and draw nothing).
                if (!this.grassMaterial.enableInstancing)
                    this.grassMaterial.enableInstancing = true;

                this.renderParams = new RenderParams(this.grassMaterial)
                {
                    shadowCastingMode = render.ShadowCastingMode,
                    // Phase 3: driven from config (no hardcoded false) so CPU + GPU grass tiers agree.
                    // Default OFF preserves Phase 2 baseline (grass was always receive=false before).
                    receiveShadows = render.ReceiveShadows,
                    layer = 0,
                    camera = null, // render in ALL cameras (game + scene view) — see class summary
                };
                this.hasParams = true;
            }
        }

        /// <summary>
        /// Submit every slab for instanced rendering. ONE LOD mesh is chosen for the WHOLE field by distance
        /// from <paramref name="lodReferencePos"/> to the field center, under one field-wide
        /// <paramref name="worldBounds"/>. <paramref name="targetCamera"/> = null renders in ALL cameras
        /// (player-loop / play path); a specific camera renders ONLY in that one (edit-mode per-camera submit
        /// from beginCameraRendering — see class summary).
        /// Silently skips the draw when no LOD meshes were configured (0-LOD graceful path).
        /// </summary>
        public void Render(Vector3 lodReferencePos, Matrix4x4[][] renderSlabs, int[] slabCounts,
            Bounds worldBounds, Camera? targetCamera = null)
        {
            if (!this.hasParams || !this.hasLods)
                return;

            // ONE LOD for the whole field: distance from the reference viewpoint to the field center.
            float sqrDist = (worldBounds.center - lodReferencePos).sqrMagnitude;
            if (this.cullSqrDistance > 0f && sqrDist > this.cullSqrDistance)
                return; // whole field beyond explicit cull distance
            // Clamp into the mesh array: SelectLod can return lodMaxSqrDistances.Length, which
            // exceeds lodMeshes if distances outnumber meshes (mis-authoring) — guard the index.
            int lod = this.SelectLod(sqrDist);
            if (lod >= this.lodMeshes.Length) lod = this.lodMeshes.Length - 1;
            Mesh mesh = this.lodMeshes[lod];

            RenderParams rp = this.renderParams;
            // worldBounds is MANDATORY for RenderMeshInstanced — the default (a zero-extent box at the origin)
            // culls EVERY instance. Set the field-wide AABB ONCE (already includes blade height + bend headroom).
            rp.worldBounds = worldBounds;
            rp.camera = targetCamera; // null = all cameras (play); a camera = that one only (edit per-camera)

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
