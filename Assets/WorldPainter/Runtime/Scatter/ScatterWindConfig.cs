#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Wind configuration shared across all scatter layer types.
    /// Composed by concrete layer types rather than inherited.
    /// </summary>
    [System.Serializable]
    public struct ScatterWindConfig
    {
        [BoxGroup("Wind")]
        [Tooltip("Wind model. Sine = directional sin() wave (legacy, matches CPU sim). Perlin = 2-octave gust+ripple in the GPU shader (CPU sim still uses Sine).")]
        [SerializeField] private ScatterLayer.WindMode windMode;

        [BoxGroup("Wind")]
        [Tooltip("Ambient wind direction in the XZ plane (auto-normalized when bound).")]
        [SerializeField] private Vector2 windDirection;

        [BoxGroup("Wind")]
        [Range(0f, 2f)]
        [Tooltip("Horizontal sway amplitude at the blade tip, in metres.")]
        [SerializeField] private float windStrength;

        [BoxGroup("Wind")]
        [Range(0f, 5f)]
        [Tooltip("Sine mode: sway oscillation speed (cycles scale with _Time).")]
        [SerializeField] private float windFrequency;

        [BoxGroup("Wind")]
        [Range(0.01f, 2f)]
        [Tooltip("Sine mode: spatial noise scale — how quickly the gust phase varies across the field.")]
        [SerializeField] private float windNoiseScale;

        [BoxGroup("Wind")]
        [Range(0.005f, 0.5f)]
        [Tooltip("Perlin mode: spatial frequency of the slow gust octave.")]
        [SerializeField] private float windGustScale;

        [BoxGroup("Wind")]
        [Range(0.05f, 2f)]
        [Tooltip("Perlin mode: spatial frequency of the fast ripple octave.")]
        [SerializeField] private float windRippleScale;

        [BoxGroup("Wind")]
        [Range(0f, 3f)]
        [Tooltip("Perlin mode: scroll speed of the gust octave (metres/sec along _WindDir).")]
        [SerializeField] private float windGustSpeed;

        [BoxGroup("Wind")]
        [Range(0f, 5f)]
        [Tooltip("Perlin mode: scroll speed of the ripple octave (metres/sec along _WindDir).")]
        [SerializeField] private float windRippleSpeed;

        [BoxGroup("Wind")]
        [Range(0f, 1f)]
        [Tooltip("Perlin mode: how much the ripple octave contributes on top of the gust (0 = gust only).")]
        [SerializeField] private float windRippleWeight;

        public ScatterWindConfig(
            ScatterLayer.WindMode windMode,
            Vector2 windDirection,
            float windStrength,
            float windFrequency,
            float windNoiseScale,
            float windGustScale,
            float windRippleScale,
            float windGustSpeed,
            float windRippleSpeed,
            float windRippleWeight)
        {
            this.windMode = windMode;
            this.windDirection = windDirection;
            this.windStrength = windStrength;
            this.windFrequency = windFrequency;
            this.windNoiseScale = windNoiseScale;
            this.windGustScale = windGustScale;
            this.windRippleScale = windRippleScale;
            this.windGustSpeed = windGustSpeed;
            this.windRippleSpeed = windRippleSpeed;
            this.windRippleWeight = windRippleWeight;
        }

        public ScatterLayer.WindMode Mode => this.windMode;
        public Vector2 WindDirection => this.windDirection;
        public float WindStrength => this.windStrength;
        public float WindFrequency => this.windFrequency;
        public float WindNoiseScale => this.windNoiseScale;
        public float WindGustScale => this.windGustScale;
        public float WindRippleScale => this.windRippleScale;
        public float WindGustSpeed => this.windGustSpeed;
        public float WindRippleSpeed => this.windRippleSpeed;
        public float WindRippleWeight => this.windRippleWeight;
    }
}
