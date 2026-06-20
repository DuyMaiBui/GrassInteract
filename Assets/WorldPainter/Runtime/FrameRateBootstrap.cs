#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Forces the application target frame rate at startup.
    ///
    /// Mobile platforms default <see cref="Application.targetFrameRate"/> to 30 fps (to save
    /// battery), so the game is capped at 30 even on a 60 Hz+ display. This requests 60.
    ///
    /// IMPORTANT: <see cref="Application.targetFrameRate"/> is ignored while vSync is on
    /// (<c>QualitySettings.vSyncCount &gt; 0</c> syncs to the display instead), so vSync is
    /// disabled here first — otherwise the target has no effect.
    ///
    /// Runs automatically before the first scene loads; no wiring required. Re-applied on quality
    /// changes via the active-quality reassert is not needed for a single-tier build, but if you
    /// switch quality levels at runtime, call <see cref="Apply"/> again afterwards.
    /// </summary>
    public static class FrameRateBootstrap
    {
        /// <summary>Desired frame cap. 0 = uncapped (let the platform decide).</summary>
        public const int TARGET_FPS = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Apply()
        {
            // vSync overrides targetFrameRate — must be 0 for the target to take effect.
            QualitySettings.vSyncCount  = 0;
            Application.targetFrameRate = TARGET_FPS;
        }
    }
}
