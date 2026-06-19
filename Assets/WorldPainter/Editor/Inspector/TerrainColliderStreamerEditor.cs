#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Custom Inspector for <see cref="TerrainColliderStreamer"/>. Greys out the band + resolution
    /// fields when collider generation is off, and shows the sibling WorldPainter's live LOD-band
    /// count + the resolved collider range for the chosen band.
    /// </summary>
    [CustomEditor(typeof(TerrainColliderStreamer))]
    public sealed class TerrainColliderStreamerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            this.serializedObject.Update();

            SerializedProperty gen   = this.serializedObject.FindProperty("generateColliders");
            SerializedProperty band  = this.serializedObject.FindProperty("colliderLodBand");
            SerializedProperty res   = this.serializedObject.FindProperty("heightfieldRes");
            SerializedProperty debug = this.serializedObject.FindProperty("debugShowColliders");

            EditorGUILayout.LabelField("Collision", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(gen);

            using (new EditorGUI.DisabledScope(!gen.boolValue))
            {
                EditorGUILayout.PropertyField(band);
                EditorGUILayout.PropertyField(res);
            }

            var streamer = (TerrainColliderStreamer)this.target;
            var sibling  = streamer.GetComponent<WorldPainter>();
            if (sibling != null)
            {
                int count   = sibling.LodBandCount;
                int rawBand = band.intValue;
                int effBand = count > 0 ? Mathf.Clamp(rawBand, 0, count - 1) : rawBand;
                string bandText = rawBand == effBand ? $"{rawBand}" : $"{rawBand}→{effBand} (clamped)";
                string msg = count > 0
                    ? $"Collider range = LOD band {bandText} of {count} " +
                      $"({sibling.LodRangeMetres(rawBand):0} m). Band clamps into [0, {count - 1}]."
                    : "Sibling WorldPainter has no LOD bands configured — no colliders will cook.";
                EditorGUILayout.HelpBox(msg, MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Manual Generation", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Edit mode does not stream colliders automatically. Use these buttons to cook them " +
                "manually. At runtime the camera ring still generates missing colliders on demand.",
                MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Colliders (All Tiles)"))
                {
                    int n = streamer.GenerateAllColliders();
                    WpLog.Log($"[TerrainColliderStreamer] Cooked {n} tile collider(s); " +
                              $"{streamer.LiveCount} live.");
                }
                if (GUILayout.Button("Clear Colliders"))
                    streamer.ClearColliders();
            }
            EditorGUILayout.LabelField("Live colliders", streamer.LiveCount.ToString());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(debug);

            this.serializedObject.ApplyModifiedProperties();
        }
    }
}
