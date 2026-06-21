#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// CPU / low-tier prop engine behind the <see cref="IGrassEngine"/> seam — the GLES3.0 fallback for
    /// <see cref="InstancedPropEngine"/>. The indirect engine draws via <c>Graphics.RenderMeshIndirect</c>
    /// + a compute cull + a <c>StructuredBuffer</c>-driven shader, NONE of which exist on OpenGL ES 3.0
    /// (no compute), so on that floor props render NOTHING (and fail silently — RenderMeshIndirect no-ops).
    /// This engine renders the same authored prop instances with <c>Graphics.RenderMeshInstanced</c>
    /// (1023-matrix slabs) + a URP/Lit material, mirroring <see cref="GrassCpuEngine"/> exactly:
    /// <see cref="InstancePlacement"/> for placement, <see cref="GrassRenderer"/> for the draw.
    ///
    /// <para><b>Scope (v1 — fixes the blank-render ship-blocker):</b> renders props lit + shadowed on
    /// GLES3.0, one global LOD by camera distance (as <see cref="GrassRenderer"/> does). Interactive
    /// rigid TILT (<see cref="InstanceTiltSimulator"/>) and per-instance pooled COLLIDERS are GPU-tier
    /// features not yet mirrored here — documented follow-ups; the low tier prioritizes "renders + lit"
    /// over interaction, the same way other low-tier features degrade.</para>
    ///
    /// Ownership: the facade owns the <see cref="InstanceBatchPool"/>; this engine receives it in
    /// <see cref="Build"/>, returns the scatter slabs in <see cref="Dispose"/>, and owns the cloned
    /// CPU material (destroyed in <see cref="Dispose"/>).
    /// </summary>
    internal sealed class PropCpuEngine : IGrassEngine
    {
        private GrassScatterResult? scatter;
        private GrassRenderer? renderer;
        private InstanceBatchPool? pool;
        private Material? cpuMaterial;       // engine-owned URP/Lit clone; destroyed in Dispose
        private WorldRootBinder? rootBinder;
        private Bounds renderBounds;         // world-space (root applied) — render + LOD-distance reference
        private bool warnedScaleFactor;

        /// <summary>
        /// Bind the WorldPainter root-transform binder. Unlike the GPU tier (whose shader runs
        /// WorldRootTransform.hlsl), the URP/Lit CPU material does not apply the root transform, so this
        /// engine bakes it into the instance matrices at <see cref="Build"/>. Identity root → no-op.
        /// Must be called BEFORE <see cref="Build"/>.
        /// </summary>
        internal void BindRootSpace(WorldRootBinder binder) => this.rootBinder = binder;

        /// <inheritdoc/>
        public void Build(ScatterLayer layer, Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler)
        {
            this.Dispose(); // return any previously held slabs + material before rebuilding
            this.pool = pool;

            if (layer is not IInstancePlacementSource src)
            {
                WpLog.Warning($"[PropCpuEngine] Layer '{layer.name}' is not an IInstancePlacementSource; " +
                              "no props will render.");
                return;
            }

            // Authored prop placement → base TRS matrix slabs (the same result type grass uses).
            this.scatter = new InstancePlacement(src).Build(origin, pool, sampler);

            // Bake the WorldPainter root transform into the matrices (the URP/Lit material can't run
            // WorldRootTransform.hlsl). Identity root → no-op. After this the path is fully world-space.
            this.ApplyRootTransform();

            this.renderBounds = (this.rootBinder != null && !this.rootBinder.IsIdentity)
                ? this.rootBinder.PaintingBoundsToWorld(this.scatter.WorldBounds)
                : this.scatter.WorldBounds;

            // GLES3.0-safe instanced material (URP/Lit) carrying the prop's albedo/normal/emission.
            this.cpuMaterial = BuildCpuMaterial(layer.Render.Material);

            // Reuse GrassRenderer verbatim with the CPU material override (global-LOD RenderMeshInstanced).
            this.renderer = new GrassRenderer(layer, origin, this.cpuMaterial);
        }

        /// <inheritdoc/>
        public void Step(float dt)
        {
            // v1: static. Interactive rigid tilt (InstanceTiltSimulator) is a GPU-tier feature not yet
            // mirrored on the CPU prop tier — follow-up. No per-frame work here.
        }

        /// <inheritdoc/>
        public void Submit(Camera? targetCamera, Vector3 lodReferencePos)
        {
            if (this.scatter == null || this.renderer == null)
                return;

            this.renderer.Render(lodReferencePos, this.scatter.BaseSlabs, this.scatter.SlabCounts,
                this.renderBounds, targetCamera);
        }

        /// <inheritdoc/>
        public Bounds WorldBounds => this.scatter != null ? this.renderBounds : default;

        /// <inheritdoc/>
        public void SetScaleFactor(float factor)
        {
            if (this.warnedScaleFactor) return;
            this.warnedScaleFactor = true;
            WpLog.Warning("[PropCpuEngine] scaleFactor is a GPU render feature; the CPU prop tier ignores it.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (this.scatter != null && this.pool != null)
                GrassScatter.ReturnSlabs(this.scatter, this.pool);
            this.scatter = null;
            this.renderer = null;

            if (this.cpuMaterial != null)
            {
                if (Application.isPlaying) Object.Destroy(this.cpuMaterial);
                else Object.DestroyImmediate(this.cpuMaterial);
                this.cpuMaterial = null;
            }
            // Do NOT null out this.pool — it is facade-owned; we just stop using it.
        }

        /// <summary>
        /// Premultiply every base instance matrix by the root's painting→world transform so the URP/Lit
        /// material (which does not run WorldRootTransform.hlsl) places props in world space. No-op when the
        /// root is identity (the common case) or unbound.
        /// </summary>
        private void ApplyRootTransform()
        {
            if (this.scatter == null) return;
            WorldRootBinder? binder = this.rootBinder;
            if (binder == null || binder.IsIdentity) return;

            Matrix4x4 toWorld = binder.LocalToWorld;
            Matrix4x4[][] slabs = this.scatter.BaseSlabs;
            int[] counts = this.scatter.SlabCounts;
            for (int b = 0; b < slabs.Length; ++b)
            {
                Matrix4x4[] slab = slabs[b];
                int n = counts[b];
                for (int k = 0; k < n; ++k)
                    slab[k] = toWorld * slab[k];
            }
        }

        /// <summary>
        /// Builds a GPU-instancing-enabled URP/Lit material that carries the prop material's albedo
        /// (and normal/emission when enabled). URP/Lit compiles + instances on GLES3.0 and gives lighting
        /// + shadows for free, so the CPU tier needs no custom shader (no pink-shader risk). The prop's
        /// own ScatterInstanced material can't be reused — it reads transforms from a StructuredBuffer,
        /// which RenderMeshInstanced (unity_ObjectToWorld) does not populate.
        /// </summary>
        private static Material BuildCpuMaterial(Material? src)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                WpLog.Error("[PropCpuEngine] 'Universal Render Pipeline/Lit' shader not found; " +
                            "CPU props cannot render. Ensure URP is the active pipeline.");
                // Last resort: an empty material so callers don't NRE; nothing will draw correctly.
                return new Material(Shader.Find("Hidden/InternalErrorShader")) { enableInstancing = true };
            }

            var mat = new Material(lit) { name = "PropCpu_Lit", enableInstancing = true };
            if (src == null)
                return mat;

            if (src.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
            if (src.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", src.GetColor("_BaseColor"));

            // Normal map: prop shader uses _NormalMap + _NORMALMAP keyword → URP/Lit uses _BumpMap.
            if (src.HasProperty("_NormalMap") && src.IsKeywordEnabled("_NORMALMAP") &&
                src.GetTexture("_NormalMap") != null)
            {
                mat.SetTexture("_BumpMap", src.GetTexture("_NormalMap"));
                if (src.HasProperty("_NormalStrength"))
                    mat.SetFloat("_BumpScale", src.GetFloat("_NormalStrength"));
                mat.EnableKeyword("_NORMALMAP");
            }

            // Emission.
            if (src.HasProperty("_EmissionMap") && src.IsKeywordEnabled("_EMISSION"))
            {
                mat.SetTexture("_EmissionMap", src.GetTexture("_EmissionMap"));
                if (src.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            return mat;
        }
    }
}
