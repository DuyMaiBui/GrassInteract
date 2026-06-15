#nullable enable
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    [CustomEditor(typeof(GrassLayer))]
    public sealed class GrassLayerEditor : UnityEditor.Editor
    {
        // ── Box section name constants ───────────────────────────────────────

        const string SECTION_RENDER    = "Render";
        const string SECTION_WIND      = "Wind";
        const string SECTION_DEFORM    = "Deform";
        const string SECTION_BOUNDS    = "Bounds";
        const string SECTION_PLACEMENT = "Placement";
        const string SECTION_DENSITY   = "Density";

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
        SerializedProperty? propTargetDensityPerSqM;
        SerializedProperty? propRotationOffsetEuler;
        SerializedProperty? propRandomPitchRange;
        SerializedProperty? propRandomRollRange;
        SerializedProperty? propAlignToNormal;
        SerializedProperty? propDensityTiles;

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
            this.propTargetDensityPerSqM    = this.serializedObject.FindProperty("targetDensityPerSqM");
            this.propRotationOffsetEuler= this.serializedObject.FindProperty("rotationOffsetEuler");
            this.propRandomPitchRange   = this.serializedObject.FindProperty("randomPitchRange");
            this.propRandomRollRange    = this.serializedObject.FindProperty("randomRollRange");
            this.propAlignToNormal      = this.serializedObject.FindProperty("alignToNormal");
            this.propDensityTiles       = this.serializedObject.FindProperty("densityTiles");
        }

        public override void OnInspectorGUI()
        {
            this.serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            // Grass layer fields are editable only inside the WorldPainter detail card; the
            // standalone sub-asset inspector renders them read-only.
            using (new EditorGUI.DisabledScope(!WorldPainterLayerEditContext.EditingInWorldPainter))
            {
                DrawBox(SECTION_RENDER,    this.DrawRender);
                DrawBox(SECTION_WIND,      this.DrawWind);
                DrawBox(SECTION_DEFORM,    this.DrawDeform);
                DrawBox(SECTION_BOUNDS,    this.DrawBounds);
                DrawBox(SECTION_PLACEMENT, this.DrawPlacement);
                DrawBox(SECTION_DENSITY,   this.DrawDensity);
            }

            bool changed = EditorGUI.EndChangeCheck();
            this.serializedObject.ApplyModifiedProperties();

            // Live preview: rebuild ONLY this grass layer's engines on every painter that
            // references the owning map. Other layers stay live → no full-map flicker.
            if (changed && this.target is GrassLayer grass)
                RebuildOnAllPainters(grass);
        }

        static void RebuildOnAllPainters(GrassLayer layer)
        {
            // Coalesced — see WorldPainterRebuildScheduler. Fast typing in numeric fields can
            // fire EndChangeCheck multiple times per frame; the scheduler collapses them so the
            // game-view render doesn't catch a partially-disposed GPU buffer.
            WorldPainterRebuildScheduler.MarkGrassDirty(layer);
        }

        static void DrawBox(string label, System.Action body)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            body();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        /// <summary>
        /// Wrap a nested-struct property inside its own sub-box. Iterates visible children
        /// manually so Unity's built-in foldout for expandable structs is skipped — the
        /// outer helpBox is the only container the user sees.
        /// </summary>
        static void DrawNestedBox(string label, SerializedProperty prop)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            DrawChildrenFlat(prop);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(1f);
        }

        /// <summary>
        /// Draws every visible direct child of <paramref name="prop"/> without a parent foldout.
        /// Each child still uses its own default drawer, so primitives, ranges, and arrays look
        /// exactly like they do when shown elsewhere — only the wrapper foldout is suppressed.
        /// </summary>
        static void DrawChildrenFlat(SerializedProperty prop)
        {
            var iter = prop.Copy();
            var end  = prop.GetEndProperty();
            if (!iter.NextVisible(enterChildren: true)) return;
            while (!SerializedProperty.EqualContents(iter, end))
            {
                EditorGUILayout.PropertyField(iter, includeChildren: true);
                if (!iter.NextVisible(enterChildren: false)) break;
            }
        }

        void DrawRender()    => WorldPainterLodGui.DrawScatterLodSection(this.propRender!);
        void DrawWind()      => DrawChildrenFlat(this.propWind!);
        void DrawDeform()    => DrawChildrenFlat(this.propDeform!);
        void DrawBounds()    => DrawChildrenFlat(this.propBounds!);

        void DrawPlacement()
        {
            DrawNestedBox("Placement Config", this.propPlacement!);
            EditorGUILayout.PropertyField(this.propFieldBounds!);
            EditorGUILayout.PropertyField(this.propScaleRange!);
            EditorGUILayout.PropertyField(this.propSeed!);
            EditorGUILayout.PropertyField(this.propSlopeRange!);
            EditorGUILayout.PropertyField(this.propTargetDensityPerSqM!);
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
