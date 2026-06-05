#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// A moving disturbance that flattens grass — attach to a car, wheel, player, or any mover. Registers
    /// with <see cref="GrassTrampleMap"/>, which splats this interactor's circular footprint into the
    /// top-down trample RenderTexture each frame. The grass shader reads that map and folds blades toward
    /// the ground, leaving a trail that recovers over time.
    ///
    /// Cost note: the per-blade shader cost is O(1) in interactor count — blades sample the trample map
    /// once regardless of how many interactors exist.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GrassInteractor : MonoBehaviour
    {
        [Min(0f)]
        [Tooltip("Footprint radius in world metres — how wide a patch this interactor flattens.")]
        [SerializeField] private float worldRadius = 2f;

        [Range(0f, 1f)]
        [Tooltip("Flatten strength at the footprint center (1 = fully flattened).")]
        [SerializeField] private float strength = 1f;

        /// <summary>World-space position of the footprint center (the transform position).</summary>
        public Vector3 WorldPosition => this.transform.position;

        /// <summary>Footprint radius in world metres.</summary>
        public float Radius => this.worldRadius;

        /// <summary>Flatten strength at the footprint center, 0..1.</summary>
        public float Strength => this.strength;

        private void OnEnable()
        {
            GrassTrampleMap.Register(this);
        }

        private void OnDisable()
        {
            GrassTrampleMap.Unregister(this);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool hasValidated;

        // One-time self-diagnosis of the common "I added a GrassInteractor but grass won't flatten" causes.
        // Runs on the first Update (after every component's OnEnable has run, so HasActiveInstance is settled).
        // Editor/development builds only — stripped from release players. Cost after the first call is one
        // bool check; the field is harmless dead weight in release.
        private void Update()
        {
            if (this.hasValidated)
                return;
            this.hasValidated = true;

            if (!GrassTrampleMap.HasActiveInstance)
                Debug.LogWarning($"[{nameof(GrassInteractor)}] '{this.name}' is active but there is no enabled " +
                    $"{nameof(GrassTrampleMap)} in the scene - nothing consumes this interactor, so grass cannot " +
                    "flatten. Add a GrassTrampleMap component (one per grass field).", this);

            if (this.worldRadius <= 0f)
                Debug.LogWarning($"[{nameof(GrassInteractor)}] '{this.name}' has worldRadius=" +
                    $"{this.worldRadius:0.###} (<= 0) - a zero-radius footprint flattens nothing. Set it > 0.", this);

            if (this.strength <= 0f)
                Debug.LogWarning($"[{nameof(GrassInteractor)}] '{this.name}' has strength=" +
                    $"{this.strength:0.###} (<= 0) - zero strength leaves no footprint. Set it > 0.", this);
        }
#endif

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            UnityEditor.Handles.color = Color.Lerp(Color.green, Color.red, this.Strength);
            UnityEditor.Handles.DrawWireDisc(this.WorldPosition, Vector3.up, this.Radius);
            // Small center cross marker
            float cross = 0.15f;
            UnityEditor.Handles.DrawLine(this.WorldPosition - Vector3.right * cross, this.WorldPosition + Vector3.right * cross);
            UnityEditor.Handles.DrawLine(this.WorldPosition - Vector3.forward * cross, this.WorldPosition + Vector3.forward * cross);
        }

        private void OnDrawGizmosSelected()
        {
            // Translucent filled disc
            Color fill = Color.Lerp(Color.green, Color.red, this.Strength);
            fill.a = 0.15f;
            UnityEditor.Handles.color = fill;
            UnityEditor.Handles.DrawSolidDisc(this.WorldPosition, Vector3.up, this.Radius);

            // Vertical stalk ~1m tall
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.DrawLine(this.WorldPosition, this.WorldPosition + Vector3.up * 1f);

            // Label
            UnityEditor.Handles.Label(
                this.WorldPosition + Vector3.up * 1.15f,
                $"r={this.Radius:0.##}  strength={this.Strength:0.##}");
        }
#endif
    }
}
