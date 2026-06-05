#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// Scene-view authoring mode for instance scatter layers.
    /// Stores lightweight session state for the inline inspector-driven authoring UI.
    /// </summary>
    public enum InstanceEditMode
    {
        Select = 0,
        Place = 1,
        Erase = 2,
    }

    internal static class InstancePlacementOverlay
    {
        private const string KEY_MODE = "GrassInteract_InstanceEditMode";
        private const string KEY_SNAP_ALIGN = "GrassInteract_SnapAlignNormal";
        private const string KEY_ERASE_RADIUS = "GrassInteract_EraseBrushRadius";

        internal static event System.Action? SessionChanged;

        internal static InstanceScatterLayer? ActiveLayer => InstanceSelectionService.CurrentLayer;
        internal static bool IsSceneModeActive => ActiveLayer != null;

        internal static InstanceEditMode Mode
        {
            get => (InstanceEditMode)EditorPrefs.GetInt(KEY_MODE, 0);
            set => EditorPrefs.SetInt(KEY_MODE, (int)value);
        }

        internal static bool SnapAlignNormal
        {
            get => EditorPrefs.GetBool(KEY_SNAP_ALIGN, false);
            set
            {
                EditorPrefs.SetBool(KEY_SNAP_ALIGN, value);
                NotifySessionChanged();
            }
        }

        internal static float EraseBrushRadius
        {
            get => EditorPrefs.GetFloat(KEY_ERASE_RADIUS, 2f);
            set
            {
                EditorPrefs.SetFloat(KEY_ERASE_RADIUS, value);
                NotifySessionChanged();
            }
        }

        internal static bool IsActiveFor(InstanceScatterLayer? layer) =>
            layer != null && ActiveLayer == layer;

        internal static void EnterMode(InstanceScatterLayer? layer, InstanceEditMode mode)
        {
            if (layer == null)
                return;

            Mode = mode;
            InstanceSelectionService.SetActiveLayer(layer);
            NotifySessionChanged();
            RepaintScene();
        }

        internal static void ExitSceneMode()
        {
            InstanceSelectionService.Clear();
            NotifySessionChanged();
            RepaintScene();
        }

        internal static void RefreshToolbarStatus()
        {
            NotifySessionChanged();
        }

        internal static void RepaintScene()
        {
            SceneView.RepaintAll();
        }

        private static void NotifySessionChanged()
        {
            SessionChanged?.Invoke();
        }
    }
}
