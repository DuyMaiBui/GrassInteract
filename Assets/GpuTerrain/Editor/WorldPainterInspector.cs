#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// UIToolkit custom inspector for <see cref="WorldPainter"/>.
    /// Applies <c>.pro</c> or <c>.light</c> class to the root VisualElement
    /// based on <see cref="EditorGUIUtility.isProSkin"/>, matching the
    /// Scatter Studio theme convention.
    /// </summary>
    [CustomEditor(typeof(WorldPainter))]
    internal sealed class WorldPainterInspector : UnityEditor.Editor
    {
        // ── Stylesheet paths (relative to Assets/) ───────────────────────────

        private const string USS_PRO_PATH =
            "Assets/GpuTerrain/Editor/WorldPainter/WorldPainter.uss";

        private const string USS_LIGHT_PATH =
            "Assets/GpuTerrain/Editor/WorldPainter/WorldPainterLight.uss";

        // ── Sub-views (created once, reused on repaint) ───────────────────────

        private WorldPainterFilterChips? filterChips;
        private WorldPainterLayerStackView? layerStack;
        private WorldPainterBrushDock? brushDock;

        // ── CreateInspectorGUI ────────────────────────────────────────────────

        public override VisualElement CreateInspectorGUI()
        {
            var painter = (WorldPainter)this.target;

            // Bind state
            WorldPainterState.ActivePainter = painter;
            WorldPainterAuthoring.ActivePainter = painter;

            // Root
            var root = new VisualElement();
            root.AddToClassList("world-painter-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "pro" : "light");

            // Load stylesheets
            this.AddStyleSheet(root, USS_PRO_PATH);
            if (!EditorGUIUtility.isProSkin)
            {
                this.AddStyleSheet(root, USS_LIGHT_PATH);
            }

            // Filter chips (All / Height / Splat / Grass / Props)
            this.filterChips = new WorldPainterFilterChips();
            root.Add(this.filterChips.Build());

            // Layer stack
            this.layerStack = new WorldPainterLayerStackView(
                this.serializedObject,
                painter,
                this.filterChips);
            root.Add(this.layerStack.Build());

            // Brush dock (constant — never moves between layer selections)
            this.brushDock = new WorldPainterBrushDock();
            root.Add(this.brushDock.Build());

            return root;
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        private void OnDisable()
        {
            if (WorldPainterState.ActivePainter == (WorldPainter)this.target)
            {
                WorldPainterState.ActivePainter   = null;
                WorldPainterAuthoring.ActivePainter = null;
                WorldPainterState.ResetLastStroked();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AddStyleSheet(VisualElement root, string path)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null)
            {
                root.styleSheets.Add(sheet);
            }
            else
            {
                Debug.LogWarning($"[WorldPainter] USS not found: {path}");
            }
        }
    }
}
