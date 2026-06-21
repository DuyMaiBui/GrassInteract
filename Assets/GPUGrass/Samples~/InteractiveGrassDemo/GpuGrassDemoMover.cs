#nullable enable
using UnityEngine;

namespace GPUGrass.Demo
{
    /// <summary>
    /// Demo driver: orbits this GameObject in a horizontal circle over the grass field so the attached
    /// <see cref="GrassInteractor"/> (and/or <see cref="GrassTrailInteractor"/>) sweeps through the blades —
    /// you see the lean-away bend follow the mover and, with a trail interactor, a fading matted track.
    /// Runs in edit + play ([ExecuteAlways]) so the effect is visible in the Scene view without entering
    /// Play mode. Height is sampled from the terrain below so the mover hugs the surface.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("GPUGrass/Demo/Grass Demo Mover")]
    public sealed class GpuGrassDemoMover : MonoBehaviour
    {
        [Tooltip("World-space centre of the orbit. Defaults to this object's start position projected to the field.")]
        [SerializeField] private Vector3 center = Vector3.zero;

        [Tooltip("Orbit radius in metres.")]
        [Min(0f)]
        [SerializeField] private float radius = 12f;

        [Tooltip("Orbit angular speed in degrees/second.")]
        [SerializeField] private float degreesPerSecond = 60f;

        [Tooltip("Height above the sampled ground (metres).")]
        [SerializeField] private float heightOffset = 0.5f;

        [Tooltip("Optional terrain for ground-snapping. Auto-found if left null.")]
        [SerializeField] private Terrain? terrain;

        private float angleDeg;

        private void OnEnable()
        {
            if (this.terrain == null)
                this.terrain = Object.FindFirstObjectByType<Terrain>();
        }

        private void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : Time.unscaledDeltaTime;
            this.angleDeg += this.degreesPerSecond * dt;

            float rad = this.angleDeg * Mathf.Deg2Rad;
            float x = this.center.x + Mathf.Cos(rad) * this.radius;
            float z = this.center.z + Mathf.Sin(rad) * this.radius;

            float y = this.center.y;
            if (this.terrain != null)
                y = this.terrain.SampleHeight(new Vector3(x, 0f, z)) + this.terrain.transform.position.y;

            this.transform.position = new Vector3(x, y + this.heightOffset, z);
        }

        /// <summary>Configures the orbit (used by the demo builder so the mover circles the field centre).</summary>
        public void Configure(Vector3 orbitCenter, float orbitRadius, Terrain? groundTerrain)
        {
            this.center = orbitCenter;
            this.radius = orbitRadius;
            this.terrain = groundTerrain;
        }
    }
}
