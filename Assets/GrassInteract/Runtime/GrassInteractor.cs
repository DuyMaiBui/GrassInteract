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
    /// Bend parameters (radius, strength, maxBendDegrees) live on the sibling
    /// <see cref="GrassInteractorData"/> component — edit them there.
    ///
    /// Cost note: the simulator's per-blade cost scales with the number of interactors WHOSE footprint the
    /// blade falls inside (out-of-range interactors are skipped per blade via a distance early-out).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GrassInteractorData))]
    public sealed class GrassInteractor : MonoBehaviour
    {
        private GrassInteractorData? dataCache;
        private GrassInteractorData Data => this.dataCache ??= this.GetComponent<GrassInteractorData>();

        /// <summary>World-space position of the footprint center (the transform position).</summary>
        public Vector3 WorldPosition => this.transform.position;

        /// <summary>Footprint radius in world metres.</summary>
        public float Radius => this.Data.WorldRadius;

        /// <summary>Lean strength at the footprint center, 0..1.</summary>
        public float Strength => this.Data.Strength;

        /// <summary>Maximum bend angle in degrees.</summary>
        public float MaxBendDegrees => this.Data.MaxBendDegrees;

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

            if (this.Data.WorldRadius <= 0f)
                Debug.LogWarning($"[{nameof(GrassInteractor)}] '{this.name}' has worldRadius=" +
                    $"{this.Data.WorldRadius:0.###} (<= 0) - a zero-radius footprint leans nothing. Set it > 0.", this);

            if (this.Data.Strength <= 0f)
                Debug.LogWarning($"[{nameof(GrassInteractor)}] '{this.name}' has strength=" +
                    $"{this.Data.Strength:0.###} (<= 0) - zero strength leaves no lean. Set it > 0.", this);
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

    }
}
