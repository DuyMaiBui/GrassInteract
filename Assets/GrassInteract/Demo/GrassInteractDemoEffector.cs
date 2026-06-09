#nullable enable
using UnityEngine;

namespace GrassInteract.Demo
{
    /// <summary>
    /// Demo helper: drives a transform in a horizontal circle so the attached <see cref="GrassInteractor"/>
    /// continuously sweeps the grass field — making the trample trail visible in Play mode without any
    /// input. Swap this out for your car/player once gameplay code is in place.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GrassInteractor))]
    public sealed class GrassInteractDemoEffector : MonoBehaviour
    {
        [SerializeField] private float radius = 12f;
        [SerializeField] private float angularSpeedDeg = 60f;
        [SerializeField] private float height = 0.3f;

        private Vector3 center;
        private float angleDeg;

        private void Start()
        {
            this.center = this.transform.position;
        }

        private void Update()
        {
            this.angleDeg += this.angularSpeedDeg * Time.deltaTime;
            float rad = this.angleDeg * Mathf.Deg2Rad;
            this.transform.position = this.center + new Vector3(
                Mathf.Cos(rad) * this.radius,
                this.height,
                Mathf.Sin(rad) * this.radius);
        }

    }
}
