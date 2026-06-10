#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Edit-mode render driver: steps + submits every enabled <see cref="ScatterField"/> so the
    /// Scene view shows WYSIWYG preview without entering Play mode (requirement #2 full-engine render).
    ///
    /// Rendering is ALWAYS-ON in edit mode — no selection or tool gate. The only way to pause
    /// rendering is via the menu kill-switch: <c>Tools/GrassInteract/Disable Edit-Mode Preview</c>.
    /// The kill-switch is an EditorPref that defaults to rendering ENABLED (kill-switch OFF).
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

        static ScatterFieldEditorTick()
        {
            EditorApplication.update += Tick;
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

        private static void Tick()
        {
            if (Application.isPlaying)          { hasLast = false; return; } // play loop drives itself
            if (ScatterFieldEditorTick.KillSwitchActive) { hasLast = false; return; } // user kill-switch

            double now = EditorApplication.timeSinceStartup;
            if (!hasLast) { lastTime = now; hasLast = true; return; } // skip first frame (huge dt)
            float dt = Mathf.Min((float)(now - lastTime), MAX_DT);
            lastTime = now;

            SceneView sv = SceneView.lastActiveSceneView;
            Camera? cam = sv != null ? sv.camera : null;

            bool drew = false;
            var fields = Object.FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            foreach (var field in fields)
            {
                if (field == null || !field.isActiveAndEnabled) continue;
                field.StepAll(dt);
                field.SubmitAll(cam);
                drew = true;
            }

            if (drew) SceneView.RepaintAll();
        }
    }
}
