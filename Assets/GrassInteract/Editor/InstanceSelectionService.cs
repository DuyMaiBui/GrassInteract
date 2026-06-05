#nullable enable
using System;
using UnityEditor;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// Session-scoped selection state for authored instance records.
    /// Tracks the currently inspected <see cref="InstanceScatterLayer"/> and the selected
    /// record index within its sidecar. Scene authoring tools also reuse the active layer state.
    ///
    /// State is cleared on assembly reload — not persisted to EditorPrefs.
    /// </summary>
    internal static class InstanceSelectionService
    {
        // ── State ─────────────────────────────────────────────────────────────

        internal static InstanceScatterLayer? CurrentLayer { get; private set; }
        internal static int SelectedRecordIndex { get; private set; } = -1;

        // ── Event ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Raised whenever the selection changes. Arguments are (layer, recordIndex).
        /// recordIndex == -1 means no record selected.
        /// </summary>
        internal static event Action<InstanceScatterLayer?, int>? OnSelectionChanged;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Sets the active layer without selecting a record.</summary>
        internal static void SetActiveLayer(InstanceScatterLayer? layer)
        {
            Select(layer, -1);
        }

        /// <summary>Sets the selection and fires <see cref="OnSelectionChanged"/>.</summary>
        internal static void Select(InstanceScatterLayer? layer, int idx)
        {
            CurrentLayer = layer;
            SelectedRecordIndex = idx;
            OnSelectionChanged?.Invoke(layer, idx);
        }

        /// <summary>Clears the record selection but keeps the active layer.</summary>
        internal static void ClearRecordSelection()
        {
            if (CurrentLayer == null)
            {
                Select(null, -1);
                return;
            }

            Select(CurrentLayer, -1);
        }

        /// <summary>Clears the selection (layer = null, index = -1).</summary>
        internal static void Clear() => Select(null, -1);

        // ── Domain-reload safety ──────────────────────────────────────────────

        [InitializeOnLoadMethod]
        private static void RegisterReloadCallback()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnBeforeAssemblyReload()
        {
            // Suppress the event during reload — subscribers are being torn down anyway.
            OnSelectionChanged = null;
            CurrentLayer = null;
            SelectedRecordIndex = -1;
        }
    }
}
