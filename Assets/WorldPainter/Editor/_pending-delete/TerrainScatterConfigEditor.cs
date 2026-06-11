#nullable enable
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="TerrainScatterConfig"/> assets.
    ///
    /// Displays a compact summary of the config (layer count by type, brush-stamp count, GPU
    /// resource assignment status) followed by a prominent "Open in Scatter Studio" button, then
    /// the full default inspector under a collapsible foldout.
    ///
    /// Double-clicking a <see cref="TerrainScatterConfig"/> asset in the Project window opens
    /// Scatter Studio directly via the <see cref="OnOpenAsset"/> callback.
    /// </summary>
    [CustomEditor(typeof(TerrainScatterConfig))]
    internal sealed class TerrainScatterConfigEditor : UnityEditor.Editor
    {
        // ── Foldout state ─────────────────────────────────────────────────────

        private bool showDefaultInspector = false;

        // ── Inspector GUI ─────────────────────────────────────────────────────

        public override void OnInspectorGUI()
        {
            var config = (TerrainScatterConfig)this.target;

            // ── Summary section ───────────────────────────────────────────────

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Scatter Config Summary", EditorStyles.boldLabel);

            // Layer counts
            int densityCount  = 0;
            int instanceCount = 0;
            foreach (var layer in config.Layers)
            {
                if (layer is DensityScatterLayer)
                    ++densityCount;
                else if (layer is InstanceScatterLayer)
                    ++instanceCount;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Density layers",  densityCount.ToString());
            EditorGUILayout.LabelField("Instance layers", instanceCount.ToString());
            EditorGUILayout.LabelField("Brush stamps",    config.BrushStamps.Count.ToString());

            // GPU resource assignment
            bool gpuReady = config.CullCompute != null && config.IndirectMaterial != null;
            using (new EditorGUI.DisabledScope(disabled: false))
            {
                EditorGUILayout.LabelField(
                    "GPU resources",
                    gpuReady
                        ? "Assigned (compute + material)"
                        : (config.CullCompute == null && config.IndirectMaterial == null)
                            ? "Not assigned"
                            : config.CullCompute == null
                                ? "Missing: CullCompute"
                                : "Missing: IndirectMaterial");
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(8f);

            // ── Open in Scatter Studio button ─────────────────────────────────

            if (GUILayout.Button("Open in Scatter Studio", GUILayout.Height(28f)))
                ScatterStudioWindow.OpenForConfig(config);

            EditorGUILayout.Space(6f);

            // ── Default inspector foldout ─────────────────────────────────────

            this.showDefaultInspector = EditorGUILayout.Foldout(
                this.showDefaultInspector, "Raw Inspector", toggleOnLabelClick: true);

            if (this.showDefaultInspector)
            {
                EditorGUI.indentLevel++;
                this.DrawDefaultInspector();
                EditorGUI.indentLevel--;
            }
        }

        // ── Double-click asset handler ────────────────────────────────────────

        /// <summary>
        /// Called by Unity when the user double-clicks an asset in the Project window.
        /// Returns <c>true</c> (consumed) if the asset is a <see cref="TerrainScatterConfig"/>.
        /// </summary>
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceID, int line)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceID);
            if (obj is TerrainScatterConfig config)
            {
                ScatterStudioWindow.OpenForConfig(config);
                return true;
            }
            return false;
        }
    }
}
