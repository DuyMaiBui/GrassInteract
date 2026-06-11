#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Runtime pool of per-instance <see cref="MeshCollider"/> GameObjects for a single
    /// <see cref="InstanceScatterLayer"/>. One pool instance lives as a child component on the
    /// ScatterField's collider root during Play mode; it is destroyed via <see cref="Dispose"/>
    /// when the engine disposes or the scene exits.
    ///
    /// TheOne.Pooling is not present in this project — this is a focused single-purpose pool.
    /// TODO[lib]: migrate to TheOne.Pooling.IObjectPoolManager when the shared pooling package
    ///            is available in this project.
    /// </summary>
    internal sealed class InstanceColliderPool : MonoBehaviour
    {
        // ── Config (set via Init) ─────────────────────────────────────────────

        private int hardCap;
        private Mesh? defaultMesh;
        private bool defaultConvex;
        private PhysicsMaterial? defaultMaterial;

        // ── Pool state ────────────────────────────────────────────────────────

        private readonly Stack<MeshCollider> idle   = new();
        private readonly Dictionary<int, MeshCollider> active = new();

        private bool initialized;
        private bool warnedAtCap;

        // ── Initialisation ────────────────────────────────────────────────────

        /// <summary>
        /// Must be called once after the component is added. Replaces constructor parameters.
        /// </summary>
        public void Init(int capacity, Mesh? defaultColliderMesh, bool convexDefault,
            PhysicsMaterial? defaultColliderMaterial = null)
        {
            this.hardCap      = Mathf.Max(1, capacity);
            this.defaultMesh  = defaultColliderMesh;
            this.defaultConvex = convexDefault;
            this.defaultMaterial = defaultColliderMaterial;
            this.initialized  = true;
        }

        /// <summary>Pre-creates <paramref name="count"/> idle pooled GameObjects.</summary>
        public void Prewarm(int count)
        {
            if (!this.initialized) return;
            int toCreate = Mathf.Min(count, this.hardCap) - this.idle.Count;
            for (int i = 0; i < toCreate; ++i)
                this.idle.Push(this.CreatePooledCollider());
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Read-only enumerable of the authored-record indices that currently have an active collider.
        /// Used by <see cref="InstanceVisibilityColliderDriver"/> to diff the desired set.
        /// </summary>
        public Dictionary<int, MeshCollider>.KeyCollection ActiveKeys => this.active.Keys;

        /// <summary>
        /// Acquires a pooled <see cref="MeshCollider"/> and positions it for record
        /// <paramref name="recordIdx"/>. Returns null if the pool is at capacity (one warning logged
        /// per pool lifetime).
        /// </summary>
        public MeshCollider? Acquire(
            int recordIdx,
            Vector3 pos,
            Quaternion rot,
            float scale,
            Mesh? meshOverride,
            bool convex,
            PhysicsMaterial? materialOverride = null)
        {
            if (!this.initialized) return null;

            // Already active for this record — update in place.
            if (this.active.TryGetValue(recordIdx, out MeshCollider? existing))
            {
                this.ApplyTransformAndMesh(existing, pos, rot, scale, meshOverride, convex, materialOverride);
                return existing;
            }

            // At hard cap — reject with one-shot warning.
            if (this.active.Count >= this.hardCap)
            {
                if (!this.warnedAtCap)
                {
                    Debug.LogWarning(
                        $"[InstanceColliderPool] Pool at capacity ({this.hardCap}). " +
                        "Some instances will not have colliders. Raise PoolCap on the layer.");
                    this.warnedAtCap = true;
                }
                return null;
            }

            // Pop idle or create a new GO.
            MeshCollider mc = this.idle.Count > 0
                ? this.idle.Pop()
                : this.CreatePooledCollider();

            this.ApplyTransformAndMesh(mc, pos, rot, scale, meshOverride, convex, materialOverride);
            mc.gameObject.SetActive(true);

            this.active[recordIdx] = mc;
            return mc;
        }

        /// <summary>Deactivates the collider bound to <paramref name="recordIdx"/> and returns it to idle.</summary>
        public void Release(int recordIdx)
        {
            if (!this.active.TryGetValue(recordIdx, out MeshCollider? mc)) return;
            this.active.Remove(recordIdx);
            mc.gameObject.SetActive(false);
            this.idle.Push(mc);
        }

        /// <summary>Releases all active colliders back to idle.</summary>
        public void ReleaseAll()
        {
            foreach (var pair in this.active)
            {
                pair.Value.gameObject.SetActive(false);
                this.idle.Push(pair.Value);
            }
            this.active.Clear();
        }

        /// <summary>Destroys all pooled GameObjects and clears state.</summary>
        public void Dispose()
        {
            foreach (var pair in this.active)
                SafeDestroy(pair.Value.gameObject);
            this.active.Clear();

            while (this.idle.Count > 0)
                SafeDestroy(this.idle.Pop().gameObject);

            this.idle.Clear();
        }

        // ── MonoBehaviour ─────────────────────────────────────────────────────

        private void OnDestroy() => this.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private MeshCollider CreatePooledCollider()
        {
            var go = new GameObject("InstanceCollider");
            go.transform.SetParent(this.transform, worldPositionStays: false);
            go.SetActive(false);
            var mc = go.AddComponent<MeshCollider>();
            return mc;
        }

        private void ApplyTransformAndMesh(
            MeshCollider mc,
            Vector3 pos,
            Quaternion rot,
            float scale,
            Mesh? meshOverride,
            bool convex,
            PhysicsMaterial? materialOverride)
        {
            Transform t = mc.transform;
            t.position   = pos;
            t.rotation   = rot;
            t.localScale = new Vector3(scale, scale, scale);

            Mesh? mesh = meshOverride ?? this.defaultMesh;
            if (mc.sharedMesh != mesh)
                mc.sharedMesh = mesh;

            bool wantConvex = convex || this.defaultConvex;
            if (mc.convex != wantConvex)
                mc.convex = wantConvex;

            PhysicsMaterial? material = materialOverride ?? this.defaultMaterial;
            if (mc.sharedMaterial != material)
                mc.sharedMaterial = material;
        }

        private static void SafeDestroy(GameObject? go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
