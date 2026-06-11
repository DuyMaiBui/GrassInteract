#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Mutation half of <see cref="WorldPainterLayerStackView"/> (partial).
    ///
    /// Contains: add-menu, drag-reorder implementation, add/remove/reorder mutations
    /// (all wrapped in <c>Undo.RecordObject</c>).  The add-layer path enforces the
    /// 4-layer RGBA32 hard cap with a surfaced <see cref="Debug.LogError"/> message
    /// (errors-over-fallbacks rule).
    /// </summary>
    internal sealed partial class WorldPainterLayerStackView
    {
        // ── Add menu ──────────────────────────────────────────────────────────

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Splat layer"), false, () => this.AddSplatLayer());
            menu.AddDisabledItem(new GUIContent("Grass layer (P3)"));
            menu.AddDisabledItem(new GUIContent("Props layer (P4)"));
            menu.AddDisabledItem(new GUIContent("Biome (P5)"));
            menu.ShowAsContext();
        }

        // ── Drag-reorder implementation ───────────────────────────────────────

        private void RegisterDragReorder(VisualElement row, int splatIndex)
        {
            row.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button == 0 && e.ctrlKey)
                {
                    this.dragFromIndex = splatIndex;
                    e.StopPropagation();
                }
            });

            row.RegisterCallback<MouseUpEvent>(e =>
            {
                if (e.button == 0 && this.dragFromIndex >= 0 && this.dragFromIndex != splatIndex)
                    this.ReorderSplatLayer(this.dragFromIndex, splatIndex);
                this.dragFromIndex = -1;
            });
        }

        private void ReorderSplatLayer(int fromIdx, int toIdx)
        {
            if (fromIdx < 0 || toIdx < 0) return;
            if (fromIdx >= this.splatLayersProp.arraySize) return;
            if (toIdx >= this.splatLayersProp.arraySize) return;

            Undo.RecordObject(this.painter, "Reorder Splat Layer");
            this.splatLayersProp.MoveArrayElement(fromIdx, toIdx);
            this.serializedObject.ApplyModifiedProperties();
            this.RefreshStack();
        }

        // ── Mutations (persisted via SerializedObject) ────────────────────────

        private void AddSplatLayer()
        {
            // Hard cap: 4 layers max — surfaced error, not silent drop.
            if (this.splatLayersProp.arraySize >= WorldPainter.MAX_SPLAT_LAYERS)
            {
                Debug.LogError(
                    $"[WorldPainter] Cannot add splat layer: RGBA32 splat RT supports " +
                    $"at most {WorldPainter.MAX_SPLAT_LAYERS} layers. Remove one first.");
                return;
            }

            Undo.RecordObject(this.painter, "Add Splat Layer");

            int newIdx = this.splatLayersProp.arraySize;
            this.splatLayersProp.InsertArrayElementAtIndex(newIdx);

            var elem     = this.splatLayersProp.GetArrayElementAtIndex(newIdx);
            var nameProp = elem.FindPropertyRelative("name");
            if (nameProp != null)
                nameProp.stringValue = $"Splat {newIdx}";

            this.serializedObject.ApplyModifiedProperties();
            WorldPainterState.ActiveLayerIndex = 0;
            this.RefreshStack();
        }

        private void RemoveSplatLayer(int index)
        {
            if (index < 0 || index >= this.splatLayersProp.arraySize) return;

            Undo.RecordObject(this.painter, "Remove Splat Layer");
            this.splatLayersProp.DeleteArrayElementAtIndex(index);
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveLayerIndex =
                Mathf.Max(0, WorldPainterState.ActiveLayerIndex - 1);
            this.RefreshStack();
        }

        private void RemoveScatterLayer(int index)
        {
            if (index < 0 || index >= this.scatterLayersProp.arraySize) return;

            Undo.RecordObject(this.painter, "Remove Scatter Layer");
            this.scatterLayersProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
            this.scatterLayersProp.DeleteArrayElementAtIndex(index);
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveLayerIndex =
                Mathf.Max(0, WorldPainterState.ActiveLayerIndex - 1);
            this.RefreshStack();
        }

        // ── Row helpers (called from WorldPainterLayerStackView.cs) ───────────

        private void SelectLayer(int index)
        {
            WorldPainterState.ActiveLayerIndex = index;
            this.RefreshStack();
        }

        private VisualElement CreateBaseRow(int displayIdx)
        {
            var row = new VisualElement();
            row.AddToClassList("wp-layer-row");
            if (displayIdx == WorldPainterState.ActiveLayerIndex)
                row.AddToClassList("wp-layer-row--selected");
            return row;
        }

        private Toggle MakeEyeToggle(System.Action<bool>? onChange)
        {
            var eye = new Toggle { value = true };
            eye.AddToClassList("wp-eye-toggle");
            if (onChange != null)
                eye.RegisterValueChangedCallback(e => onChange(e.newValue));
            return eye;
        }

        private Toggle MakeLockToggle(bool locked, System.Action<bool>? onChange)
        {
            var lockBtn = new Toggle { value = !locked, tooltip = "Lock layer" };
            lockBtn.AddToClassList("wp-lock-toggle");
            if (onChange != null)
                lockBtn.RegisterValueChangedCallback(e => onChange(!e.newValue));
            return lockBtn;
        }

        private static string LayerIcon(LayerType t) => t switch
        {
            LayerType.Height => "⛰",
            LayerType.Splat  => "🎨",
            LayerType.Grass  => "🌿",
            LayerType.Props  => "🌳",
            _                => "?",
        };
    }
}
