#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Edit-mode render driver. Submits each enabled <see cref="ScatterField"/>'s already-built
    /// scatter draws from URP's <see cref="RenderPipelineManager.beginCameraRendering"/> hook, so the
    /// preview renders in BOTH the Scene view and the Game view — exactly like Play mode — without a
    /// per-frame repaint loop. A light <see cref="EditorApplication.update"/> pump only advances wind
    /// time and nudges the Scene + Game views to repaint so the wind animates; it never rebuilds the
    /// scatter and never repaints unrelated editor windows.
    ///
    /// Rendering is ALWAYS-ON in edit mode — no selection or tool gate. The only way to pause it is the
    /// menu kill-switch <c>Tools/GrassInteract/Disable Edit-Mode Preview</c> (EditorPref; default ON).
    ///
    /// <see cref="PreviewEnabled"/> and <see cref="PreviewColliders"/> are preserved as readable
    /// properties (other code and the inspector may still access them); they no longer gate rendering.
    /// </summary>
    [InitializeOnLoad]
    internal static class ScatterFieldEditorTick
    {
        private const string PREVIEW_PREF_KEY    = "GrassInteract.PreviewEnabled";
        private const string COLLIDERS_PREF_KEY  = "GrassInteract.PreviewColliders";
        private const string KILL_SWITCH_PREF_KEY = "GrassInteract.EditModePreviewDisabled";
        private const float  MAX_DT              = 0.1f;

        private static double lastTime;
        private static bool   hasLast;

        // Fields refreshed once per editor frame in AdvanceAndPump, read per-camera in SubmitForCamera.
        private static ScatterField[] cachedFields = System.Array.Empty<ScatterField>();

        static ScatterFieldEditorTick()
        {
            EditorApplication.update += AdvanceAndPump;
            RenderPipelineManager.beginCameraRendering += SubmitForCamera;
        }

        // ─── Public properties (kept for backward-compat; no longer gate rendering) ──────────

        /// <summary>
        /// Edit-mode render preview toggle (persisted per project).
        /// No longer gates scene rendering — rendering is always-on.
        /// Retained so existing inspector/window bindings compile without change.
        /// </summary>
        public static bool PreviewEnabled
        {
            get => EditorPrefs.GetBool(PREVIEW_PREF_KEY, false);
            set => EditorPrefs.SetBool(PREVIEW_PREF_KEY, value);
        }

        /// <summary>
        /// Whether to spawn per-instance colliders while authoring. Default OFF (no thousands of GOs).
        /// Note: the runtime collider pipeline is Play-mode-only by design, so edit-mode collider
        /// preview is reserved for a future pass; this flag is surfaced now for the inspector.
        /// </summary>
        public static bool PreviewColliders
        {
            get => EditorPrefs.GetBool(COLLIDERS_PREF_KEY, false);
            set => EditorPrefs.SetBool(COLLIDERS_PREF_KEY, value);
        }

        // ─── Kill-switch menu toggle ───────────────────────────────────────────────────────────

        private static bool KillSwitchActive
        {
            get => EditorPrefs.GetBool(KILL_SWITCH_PREF_KEY, false); // default false = rendering ON
            set => EditorPrefs.SetBool(KILL_SWITCH_PREF_KEY, value);
        }

        [MenuItem("Tools/GrassInteract/Disable Edit-Mode Preview", priority = 200)]
        private static void ToggleKillSwitch()
        {
            ScatterFieldEditorTick.KillSwitchActive = !ScatterFieldEditorTick.KillSwitchActive;
        }

        [MenuItem("Tools/GrassInteract/Disable Edit-Mode Preview", validate = true, priority = 200)]
        private static bool ToggleKillSwitchValidate()
        {
            Menu.SetChecked("Tools/GrassInteract/Disable Edit-Mode Preview", ScatterFieldEditorTick.KillSwitchActive);
            return true;
        }

        // ─── Tick ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Once per editor frame: refresh the field cache, advance wind time (cheap — NOT a rebuild),
        /// and nudge only the Scene + Game views to repaint so the wind animates. The actual draw
        /// submission happens per-camera in <see cref="SubmitForCamera"/>.
        /// </summary>
        private static void AdvanceAndPump()
        {
            if (Application.isPlaying || ScatterFieldEditorTick.KillSwitchActive)
            {
                hasLast = false;
                cachedFields = System.Array.Empty<ScatterField>();
                return;
            }

            cachedFields = Object.FindObjectsByType<ScatterField>(FindObjectsSortMode.None);

            double now = EditorApplication.timeSinceStartup;
            if (!hasLast) { lastTime = now; hasLast = true; return; } // skip first frame (huge dt)
            float dt = Mathf.Min((float)(now - lastTime), MAX_DT);
            lastTime = now;

            bool any = false;
            foreach (ScatterField field in cachedFields)
            {
                if (field == null || !field.isActiveAndEnabled) continue;
                field.StepAll(dt); // advances wind time only — no re-scatter
                any = true;
            }
            if (!any) return;

            // Pump ONLY the preview views — never RepaintAllViews (which redraws every editor window).
            SceneView.RepaintAll();                    // Scene view(s)
            EditorApplication.QueuePlayerLoopUpdate();  // Game view(s) in edit mode
        }

        /// <summary>
        /// URP render hook: when a Scene or Game camera renders, submit each field's already-built
        /// draws scoped to THAT camera. This makes the preview show in the Game view just like the
        /// Scene view and Play mode, with no manual repaint loop. Submit is draw-only (no rebuild).
        /// </summary>
        private static void SubmitForCamera(ScriptableRenderContext context, Camera camera)
        {
            if (Application.isPlaying || ScatterFieldEditorTick.KillSwitchActive) return; // play loop submits via ScatterField.LateUpdate
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;

            foreach (ScatterField field in cachedFields)
            {
                if (field == null || !field.isActiveAndEnabled) continue;
                field.SubmitAll(camera);
            }
        }
    }
}
