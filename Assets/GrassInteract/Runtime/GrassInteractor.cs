#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// A moving disturbance that leans grass AWAY from it — attach to a car, wheel, player, or any mover.
    /// On enable it adds itself to a static registry (<see cref="Active"/>); <see cref="GrassBendSimulator"/>
    /// reads that registry each frame, leans every blade inside this interactor's circular footprint away
    /// from the footprint center, and recovers the blades toward upright after the interactor leaves.
    ///
    /// Cost note: the simulator's per-blade cost scales with the number of interactors WHOSE footprint the
    /// blade falls inside (out-of-range interactors are skipped per blade via a distance early-out).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GrassInteractor : MonoBehaviour
    {
        [Min(0f)]
        [Tooltip("Footprint radius in world metres — how wide a patch this interactor leans.")]
        [SerializeField] private float worldRadius = 2f;

        [Range(0f, 1f)]
        [Tooltip("Lean strength at the footprint center (1 = full lean).")]
        [SerializeField] private float strength = 1f;

        /// <summary>World-space position of the footprint center (the transform position).</summary>
        public Vector3 WorldPosition => this.transform.position;

        /// <summary>Footprint radius in world metres.</summary>
        public float Radius => this.worldRadius;

        /// <summary>Lean strength at the footprint center, 0..1.</summary>
        public float Strength => this.strength;

        // Static registry of enabled interactors. GrassBendSimulator iterates this every frame instead of
        // each interactor pushing into a per-field map — the simulator is the single consumer.
        private static readonly List<GrassInteractor> active = new();

        /// <summary>All currently-enabled interactors. Read by <see cref="GrassBendSimulator"/> each frame.</summary>
        public static IReadOnlyList<GrassInteractor> Active => active;

        private void OnEnable()
        {
            if (!active.Contains(this)) // idempotent — guard double-add across edit-mode domain reloads
                active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool hasValidated;

        // One-time self-diagnosis of the common "I added a GrassInteractor but grass won't lean" causes.
        // Runs on the first Update (after every component's OnEnable has run, so the field is settled).
        // Editor/development builds only — stripped from release players. Cost after the first call is one
        // bool check; the field is harmless dead weight in release.
        private void Update()
        {
            if (this.hasValidated)
                return;
            this.hasValidated = true;

            if (!HasEnabledConsumer())
                Debug.LogWarning($"[{nameof(GrassInteractor)}] '{this.name}' is active but no enabled " +
                    $"{nameof(ScatterField)}/{nameof(GrassBendSimulator)} exists in the scene - nothing " +
                    "reads this interactor, so no grass leans. Add a ScatterField " +
                    "with at least one Grass-kind layer.", this);

            if (this.worldRadius <= 0f)
                Debug.LogWarning($"[{nameof(GrassInteractor)}] '{this.name}' has worldRadius=" +
                    $"{this.worldRadius:0.###} (<= 0) - a zero-radius footprint leans nothing. Set it > 0.", this);

            if (this.strength <= 0f)
                Debug.LogWarning($"[{nameof(GrassInteractor)}] '{this.name}' has strength=" +
                    $"{this.strength:0.###} (<= 0) - zero strength leaves no lean. Set it > 0.", this);
        }

        // True when at least one enabled ScatterField
        // exists — the field owns the GrassBendSimulator that is the sole consumer of the interactor registry.
        private static bool HasEnabledConsumer()
        {
            ScatterField[] fields = FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            foreach (ScatterField f in fields)
                if (f.isActiveAndEnabled)
                    return true;
            return false;
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
