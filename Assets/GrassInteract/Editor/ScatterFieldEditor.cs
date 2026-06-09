#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="ScatterField"/>: edit-mode preview toggles, a manual
    /// "Rebuild Now" escape hatch, and routing of inspector edits through
    /// <see cref="ScatterRebuildScheduler"/> (never a direct Rebuild). Also draws the field-bounds
    /// gizmo via the shared <see cref="ScatterGizmos"/>.
    /// </summary>
    [CustomEditor(typeof(ScatterField))]
    internal sealed class ScatterFieldEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var field = (ScatterField)this.target;

            EditorGUILayout.LabelField("Edit-Mode Preview", EditorStyles.boldLabel);

            bool preview = ScatterFieldEditorTick.PreviewEnabled;
            bool newPreview = EditorGUILayout.Toggle(
                new GUIContent("Preview In Scene",
                    "Render the scatter engines in the Scene view without entering Play mode."),
                preview);
            if (newPreview != preview)
            {
                ScatterFieldEditorTick.PreviewEnabled = newPreview;
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(!newPreview))
            {
                bool colliders = ScatterFieldEditorTick.PreviewColliders;
                bool newColliders = EditorGUILayout.Toggle(
                    new GUIContent("Preview Colliders",
                        "Spawn per-instance colliders while authoring (OFF avoids thousands of GameObjects)."),
                    colliders);
                if (newColliders != colliders)
                    ScatterFieldEditorTick.PreviewColliders = newColliders;
            }

            if (GUILayout.Button("Rebuild Now"))
                field.Rebuild();

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            this.DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
                ScatterRebuildScheduler.MarkAllLayersDirty(field);
        }

        private void OnSceneGUI()
        {
            var field = (ScatterField)this.target;
            var bounds = field.EngineWorldBounds;
            for (int i = 0; i < bounds.Count; ++i)
                ScatterGizmos.Aabb(bounds[i], ScatterGizmos.FieldBoundsColor);
        }
    }
}
