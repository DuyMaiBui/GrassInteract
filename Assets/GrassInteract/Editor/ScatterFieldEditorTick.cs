#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Edit-mode render driver: steps + submits every enabled <see cref="ScatterField"/> so the
    /// Scene view shows WYSIWYG preview without entering Play mode (requirement #2 full-engine render).
    ///
    /// HIGH-risk mitigation against a busy repaint loop: the tick runs ONLY when preview is enabled
    /// AND (a Scatter <see cref="EditorTool"/> is active OR a ScatterField/layer/config is selected),
    /// and it repaints only on frames where a draw actually happened. Preview defaults OFF.
    /// </summary>
    [InitializeOnLoad]
    internal static class ScatterFieldEditorTick
    {
        private const string PREVIEW_PREF_KEY   = "GrassInteract.PreviewEnabled";
        private const string COLLIDERS_PREF_KEY = "GrassInteract.PreviewColliders";
        private const float  MAX_DT             = 0.1f;

        private static double lastTime;
        private static bool   hasLast;

        static ScatterFieldEditorTick()
        {
            EditorApplication.update += Tick;
        }

        /// <summary>Edit-mode render preview toggle (persisted per project). Default OFF.</summary>
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

        private static void Tick()
        {
            if (Application.isPlaying) { hasLast = false; return; } // play loop drives itself
            if (!PreviewEnabled)       { hasLast = false; return; }
            if (!ShouldTick())         { hasLast = false; return; }

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

        // Gate: tick only when one of our tools is active OR a scatter object is selected.
        private static bool ShouldTick()
        {
            System.Type? active = ToolManager.activeToolType;
            if (active == typeof(DensityPaintTool) || active == typeof(InstancePlacementTool))
                return true;

            foreach (Object obj in Selection.objects)
            {
                switch (obj)
                {
                    case GameObject go when go.GetComponent<ScatterField>() != null: return true;
                    case ScatterField: return true;
                    case ScatterLayer: return true;
                    case TerrainScatterConfig: return true;
                }
            }
            return false;
        }
    }
}
