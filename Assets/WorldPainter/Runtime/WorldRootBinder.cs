#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// SSOT for the WorldPainter root-transform feature. All WorldPainter content
    /// (terrain/grass/props) is authored in PAINTING space (= the WorldPainter root
    /// GameObject's LOCAL space). This binder maps painting space → world space using
    /// the root's full non-uniform TRS and keeps render/cull/colliders/editor in lockstep:
    ///
    ///   - <see cref="PushGlobals"/> pushes the 3 global shader matrices + toggles the
    ///     <c>_WP_ROOT_TRANSFORM</c> keyword, once per LateUpdate / per edit-mode camera.
    ///   - CPU helpers (<see cref="WorldToPainting"/>, <see cref="PaintingToWorld"/>,
    ///     <see cref="WorldPlanesToPainting"/>) let culling/colliders/editor convert into
    ///     painting space so the existing painting-space math runs unchanged.
    ///
    /// When the root is identity the keyword is OFF → byte-identical to the pre-feature build.
    /// </summary>
    public sealed class WorldRootBinder
    {
        /// <summary>Global multi_compile keyword toggled when the root is non-identity.</summary>
        public const string KEYWORD_ROOT_TRANSFORM = "_WP_ROOT_TRANSFORM";

        private static readonly int ID_LocalToWorld = Shader.PropertyToID("_WPLocalToWorld");
        private static readonly int ID_WorldToLocal = Shader.PropertyToID("_WPWorldToLocal");
        private static readonly int ID_NormalMatrix = Shader.PropertyToID("_WPNormalMatrix");

        // Below this max-abs element delta from identity, the root is treated as identity
        // (keyword OFF → byte-identical render path). Generous enough to ignore FP noise.
        private const float IDENTITY_EPSILON = 1e-6f;

        private readonly Transform root;

        public WorldRootBinder(Transform root)
        {
            this.root = root;
        }

        /// <summary>The root Transform that defines painting space (= the WorldPainter GameObject).</summary>
        internal Transform Root => this.root;

        /// <summary>painting → world point transform (root.localToWorldMatrix). Valid after PushGlobals.</summary>
        public Matrix4x4 LocalToWorld { get; private set; } = Matrix4x4.identity;

        /// <summary>world → painting point transform (root.worldToLocalMatrix). Valid after PushGlobals.</summary>
        public Matrix4x4 WorldToLocal { get; private set; } = Matrix4x4.identity;

        /// <summary>True when the root is (approximately) identity → render path is byte-identical.</summary>
        public bool IsIdentity { get; private set; } = true;

        /// <summary>
        /// Recompute the matrices from the root transform and push them as global shader state.
        /// Call ONCE per frame BEFORE any terrain/grass/prop submit (covers all three because the
        /// matrices are global). Toggles the <c>_WP_ROOT_TRANSFORM</c> keyword by identity state.
        /// </summary>
        public void PushGlobals()
        {
            Matrix4x4 m = this.root.localToWorldMatrix;
            this.LocalToWorld = m;
            this.WorldToLocal = this.root.worldToLocalMatrix;
            this.IsIdentity   = IsApproximatelyIdentity(m);

            if (this.IsIdentity)
            {
                // Identity → keep the cheap OFF variant; do not touch the matrices.
                Shader.DisableKeyword(KEYWORD_ROOT_TRANSFORM);
                return;
            }

            Shader.EnableKeyword(KEYWORD_ROOT_TRANSFORM);
            Shader.SetGlobalMatrix(ID_LocalToWorld, m);
            Shader.SetGlobalMatrix(ID_WorldToLocal, this.WorldToLocal);
            Shader.SetGlobalMatrix(ID_NormalMatrix, NormalMatrix(m));
        }

        /// <summary>
        /// Normal matrix = inverse-transpose of the upper-left 3x3 of <paramref name="m"/>,
        /// packed into a 4x4 (row/col 3 = identity) for Shader.SetGlobalMatrix. Correct normals
        /// under non-uniform scale; equals the rotation for pure rotation + uniform scale.
        /// </summary>
        public static Matrix4x4 NormalMatrix(Matrix4x4 m)
        {
            // Strip translation → pure linear 3x3 (in a 4x4 with identity 4th row/col).
            Matrix4x4 linear = m;
            linear.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            linear.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            return linear.inverse.transpose;
        }

        /// <summary>World-space point → painting space.</summary>
        public Vector3 WorldToPainting(Vector3 worldPos) => this.WorldToLocal.MultiplyPoint3x4(worldPos);

        /// <summary>Painting-space point → world space.</summary>
        public Vector3 PaintingToWorld(Vector3 paintingPos) => this.LocalToWorld.MultiplyPoint3x4(paintingPos);

        /// <summary>
        /// Transform world-space frustum planes (from GeometryUtility.CalculateFrustumPlanes) into
        /// painting space so a painting-space culler (CdlodQuadtree / GrassCull.compute) stays
        /// metric-correct under the root's non-uniform TRS.
        ///
        /// A plane (n, d) [dot(n,p)+d=0] expressed in WORLD space maps to PAINTING space by
        /// transpose(LocalToWorld) applied to the 4-vector (n, d) — the inverse-transpose identity
        /// for the world→painting point map. We renormalise so |n|=1 and d keeps metric meaning.
        /// Output is written as Vector4 (n.xyz, d) into <paramref name="outPainting"/>.
        /// </summary>
        public void WorldPlanesToPainting(Plane[] worldPlanes, Vector4[] outPainting)
        {
            Matrix4x4 mt = this.LocalToWorld.transpose;
            int n = Mathf.Min(worldPlanes.Length, outPainting.Length);
            for (int i = 0; i < n; i++)
            {
                Plane p = worldPlanes[i];
                Vector4 pw = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
                Vector4 pp = mt * pw;
                float len = new Vector3(pp.x, pp.y, pp.z).magnitude;
                float inv = len > 1e-12f ? 1f / len : 0f;
                outPainting[i] = pp * inv;
            }
        }

        private static bool IsApproximatelyIdentity(Matrix4x4 m)
        {
            Matrix4x4 id = Matrix4x4.identity;
            for (int i = 0; i < 16; i++)
            {
                if (Mathf.Abs(m[i] - id[i]) > IDENTITY_EPSILON) return false;
            }
            return true;
        }
    }
}
