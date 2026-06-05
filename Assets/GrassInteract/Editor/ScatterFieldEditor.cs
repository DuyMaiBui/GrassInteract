#nullable enable
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    [CustomEditor(typeof(ScatterField), true), CanEditMultipleObjects]
    public sealed class ScatterFieldEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            var field = this.target as ScatterField;
            if (field != null && ScatterAssetMigrator.IsLegacy(field))
            {
                EditorGUILayout.HelpBox(
                    "This ScatterField has legacy inline layers. Click below to migrate to a TerrainScatterConfig.",
                    MessageType.Warning);
                if (GUILayout.Button("Migrate Now", GUILayout.Height(32)))
                    ScatterAssetMigrator.Migrate(field);
                return;
            }

            base.OnInspectorGUI();

            if (field == null || field.Config == null)
                return;

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Author density and instance layers on the TerrainScatterConfig asset inspector.",
                MessageType.Info);

            if (GUILayout.Button("Edit Config", GUILayout.Height(28f)))
            {
                Selection.activeObject = field.Config;
                EditorGUIUtility.PingObject(field.Config);
            }

            EditorGUILayout.LabelField("Active Tier", field.ActiveTierName, EditorStyles.miniLabel);
        }
    }
}
