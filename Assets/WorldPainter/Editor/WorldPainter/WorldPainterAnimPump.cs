#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Edit-mode animation pump: advances the surface-layer scatter simulation (grass + prop
    /// wind sway and interactor lean) once per editor frame, then calls
    /// <see cref="SceneView.RepaintAll"/> so the Scene view animates continuously without manual
    /// interaction.
    ///
    /// WHY STEP THE ENGINES instead of pushing a global uniform: each <see cref="WorldPainter"/>
    /// surface engine writes <c>_GrassTime</c> PER-MATERIAL in its <c>Submit</c> (so sibling
    /// scatter layers don't overwrite each other's wind via global state — see
    /// <c>GrassGpuEngine.Submit</c> / <c>InstancedPropEngine.Submit</c>). A per-material value
    /// shadows <see cref="Shader.SetGlobalFloat"/>, so writing a global <c>_GrassTime</c> here
    /// would be silently overridden on every camera render and nothing would animate. Instead we
    /// advance each engine's own time accumulator via <c>StepSurfaceLayers(dt)</c>; the engine's
    /// <c>Submit</c> (driven by the edit-mode <c>beginCameraRendering</c> callback after the
    /// repaint) then carries that advancing time into its per-material uniform — the exact path
    /// play mode uses (LateUpdate → StepSurfaceLayers → SubmitSurfaceLayers).
    ///
    /// Throttled to ~30 fps. No-ops in play mode (the player loop already steps the engines, so
    /// stepping here too would advance wind at 2× speed). Idles (no repaint) when no
    /// <see cref="WorldPainter"/> is present in the scene.
    ///
    /// Enable/disable via <see cref="Enabled"/> (wired to the "Animate in Edit Mode" toggle in
    /// <c>WorldPainterInspector</c>); persisted in <see cref="EditorPrefs"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class WorldPainterAnimPump
    {
        // ── Config ────────────────────────────────────────────────────────────

        private const double TICK_INTERVAL_S = 1.0 / 30.0; // ~33 ms
        private const float  MAX_DT_S        = 0.1f;        // clamp dt after an idle/paused editor

        // ── State ─────────────────────────────────────────────────────────────

        private const string PREFS_KEY = "WorldPainter.AnimateInEditMode";

        // In-memory cache so the getter never hits disk every frame.
        private static bool   enabled;
        private static double lastTickTime;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Whether the pump is running. Persisted via EditorPrefs so the setting survives domain
        /// reloads and editor restarts. Default is <c>true</c> on first run. Toggled by the
        /// "Animate in Edit Mode" inspector control.
        /// </summary>
        public static bool Enabled
        {
            get => enabled;
            set
            {
                enabled = value;
                EditorPrefs.SetBool(PREFS_KEY, value);
            }
        }

        // ── InitializeOnLoad ──────────────────────────────────────────────────

        static WorldPainterAnimPump()
        {
            // Restore persisted state on every domain reload. Default true on first run.
            enabled = EditorPrefs.GetBool(PREFS_KEY, defaultValue: true);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        private static void Tick()
        {
            // Never run in play mode — the player loop (LateUpdate → StepSurfaceLayers) owns the
            // scatter time there; double-stepping would advance wind at 2× speed.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!enabled)
                return;

            double now     = EditorApplication.timeSinceStartup;
            double elapsed = now - lastTickTime;
            if (elapsed < TICK_INTERVAL_S)
                return;

            lastTickTime = now;

            // Only animate when there is actually a WorldPainter to drive — otherwise idle so we
            // never trigger a SceneView repaint storm in scenes with no scatter.
            var painters = Object.FindObjectsByType<WorldPainter>(FindObjectsSortMode.None);
            if (painters.Length == 0)
                return;

            // Advance each painter's grass + prop scatter time by the real elapsed delta (clamped
            // so an idle/paused editor doesn't pop a large jump). The engines' Submit — fired by
            // the edit-mode beginCameraRendering callback after RepaintAll — reads this advancing
            // time into the per-material _GrassTime, so wind sway / interactor lean animate live.
            float dt = Mathf.Min((float)elapsed, MAX_DT_S);
            bool stepped = false;
            for (int i = 0; i < painters.Length; ++i)
            {
                WorldPainter? p = painters[i];
                if (p == null)
                    continue;
                p.StepSurfaceLayers(dt);
                stepped = true;
            }

            if (stepped)
                SceneView.RepaintAll();
        }
    }
}
