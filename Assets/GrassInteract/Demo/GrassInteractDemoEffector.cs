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

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Vector3 ringCenter = Application.isPlaying ? this.center : this.transform.position;

            // Orbit ring at the configured height
            UnityEditor.Handles.color = new Color(0f, 0.9f, 1f, 0.85f); // cyan
            UnityEditor.Handles.DrawWireDisc(new Vector3(ringCenter.x, this.height, ringCenter.z), Vector3.up, this.radius);

            // Center cross marker at ground-level ring center
            Vector3 centMark = new Vector3(ringCenter.x, this.height, ringCenter.z);
            float cross = 0.3f;
            UnityEditor.Handles.DrawLine(centMark - Vector3.right * cross, centMark + Vector3.right * cross);
            UnityEditor.Handles.DrawLine(centMark - Vector3.forward * cross, centMark + Vector3.forward * cross);
        }
#endif
    }
}
