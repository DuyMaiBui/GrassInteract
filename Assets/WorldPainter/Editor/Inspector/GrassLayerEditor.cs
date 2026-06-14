#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    [CustomEditor(typeof(GrassLayer))]
    public sealed class GrassLayerEditor : UnityEditor.Editor
    {
        // ── Foldout section name constants ───────────────────────────────────

        const string SECTION_RENDER    = "Render";
        const string SECTION_WIND      = "Wind";
        const string SECTION_DEFORM    = "Deform";
        const string SECTION_BOUNDS    = "Bounds";
        const string SECTION_PLACEMENT = "Placement";
        const string SECTION_DENSITY   = "Density";

        static string PrefKey(string section) =>
            $"WorldPainter.GrassLayerEditor.{section}";

        // ── Serialized properties ─────────────────────────────────────────────

        SerializedProperty? propRender;
        SerializedProperty? propWind;
        SerializedProperty? propDeform;
        SerializedProperty? propBounds;
        SerializedProperty? propPlacement;
        SerializedProperty? propFieldBounds;
        SerializedProperty? propScaleRange;
        SerializedProperty? propSeed;
        SerializedProperty? propSlopeRange;
        SerializedProperty? propTargetInstances;
        SerializedProperty? propRotationOffsetEuler;
        SerializedProperty? propRandomPitchRange;
        SerializedProperty? propRandomRollRange;
        SerializedProperty? propAlignToNormal;
        SerializedProperty? propDensityTiles;

        // ── Foldout states ────────────────────────────────────────────────────

        bool foldRender;
        bool foldWind;
        bool foldDeform;
        bool foldBounds;
        bool foldPlacement;
        bool foldDensity;

        void OnEnable()
        {
            this.propRender             = this.serializedObject.FindProperty("render");
            this.propWind               = this.serializedObject.FindProperty("wind");
            this.propDeform             = this.serializedObject.FindProperty("deform");
            this.propBounds             = this.serializedObject.FindProperty("bounds");
            this.propPlacement          = this.serializedObject.FindProperty("placement");
            this.propFieldBounds        = this.serializedObject.FindProperty("fieldBounds");
            this.propScaleRange         = this.serializedObject.FindProperty("scaleRange");
            this.propSeed               = this.serializedObject.FindProperty("seed");
            this.propSlopeRange         = this.serializedObject.FindProperty("slopeRange");
            this.propTargetInstances    = this.serializedObject.FindProperty("targetInstances");
            this.propRotationOffsetEuler= this.serializedObject.FindProperty("rotationOffsetEuler");
            this.propRandomPitchRange   = this.serializedObject.FindProperty("randomPitchRange");
            this.propRandomRollRange    = this.serializedObject.FindProperty("randomRollRange");
            this.propAlignToNormal      = this.serializedObject.FindProperty("alignToNormal");
            this.propDensityTiles       = this.serializedObject.FindProperty("densityTiles");

            this.foldRender    = EditorPrefs.GetBool(PrefKey(SECTION_RENDER),    true);
            this.foldWind      = EditorPrefs.GetBool(PrefKey(SECTION_WIND),      true);
            this.foldDeform    = EditorPrefs.GetBool(PrefKey(SECTION_DEFORM),    false);
            this.foldBounds    = EditorPrefs.GetBool(PrefKey(SECTION_BOUNDS),    false);
            this.foldPlacement = EditorPrefs.GetBool(PrefKey(SECTION_PLACEMENT), true);
            this.foldDensity   = EditorPrefs.GetBool(PrefKey(SECTION_DENSITY),   false);
        }

        public override void OnInspectorGUI()
        {
            this.serializedObject.Update();

            this.DrawFoldout(SECTION_RENDER, ref this.foldRender,    this.DrawRender);
            this.DrawFoldout(SECTION_WIND,   ref this.foldWind,      this.DrawWind);
            this.DrawFoldout(SECTION_DEFORM, ref this.foldDeform,    this.DrawDeform);
            this.DrawFoldout(SECTION_BOUNDS, ref this.foldBounds,    this.DrawBounds);
            this.DrawFoldout(SECTION_PLACEMENT, ref this.foldPlacement, this.DrawPlacement);
            this.DrawFoldout(SECTION_DENSITY,   ref this.foldDensity,   this.DrawDensity);

            this.serializedObject.ApplyModifiedProperties();
        }

        void DrawFoldout(string label, ref bool state, System.Action body)
        {
            bool next = EditorGUILayout.Foldout(state, label, true, EditorStyles.foldoutHeader);
            if (next != state)
            {
                state = next;
                EditorPrefs.SetBool(PrefKey(label), state);
            }
            if (!state) return;
            EditorGUI.indentLevel++;
            body();
            EditorGUI.indentLevel--;
        }

        void DrawRender()    => EditorGUILayout.PropertyField(this.propRender!,    GUIContent.none, true);
        void DrawWind()      => EditorGUILayout.PropertyField(this.propWind!,      GUIContent.none, true);
        void DrawDeform()    => EditorGUILayout.PropertyField(this.propDeform!,    GUIContent.none, true);
        void DrawBounds()    => EditorGUILayout.PropertyField(this.propBounds!,    GUIContent.none, true);

        void DrawPlacement()
        {
            EditorGUILayout.PropertyField(this.propPlacement!,           GUIContent.none, true);
            EditorGUILayout.PropertyField(this.propFieldBounds!);
            EditorGUILayout.PropertyField(this.propScaleRange!);
            EditorGUILayout.PropertyField(this.propSeed!);
            EditorGUILayout.PropertyField(this.propSlopeRange!);
            EditorGUILayout.PropertyField(this.propTargetInstances!);
            EditorGUILayout.PropertyField(this.propRotationOffsetEuler!);
            EditorGUILayout.PropertyField(this.propRandomPitchRange!);
            EditorGUILayout.PropertyField(this.propRandomRollRange!);
            EditorGUILayout.PropertyField(this.propAlignToNormal!);
        }

        void DrawDensity()
        {
            var tiles = this.propDensityTiles!;
            int count = tiles.arraySize;
            EditorGUILayout.LabelField("Tile count", count.ToString());
            if (count > 0)
            {
                var first = tiles.GetArrayElementAtIndex(0).FindPropertyRelative("coord");
                var last  = tiles.GetArrayElementAtIndex(count - 1).FindPropertyRelative("coord");
                EditorGUILayout.LabelField("First coord", first!.vector2IntValue.ToString());
                EditorGUILayout.LabelField("Last coord",  last!.vector2IntValue.ToString());
            }
        }
    }
}
