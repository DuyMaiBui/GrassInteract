#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    internal enum DensityScenePaintMode
    {
        Paint = 0,
        Erase = 1,
    }

    /// <summary>
    /// SceneView density-paint session for <see cref="DensityScatterLayer"/> authoring.
    /// Reuses <see cref="ScatterBrush"/> for all brush math and incremental rebuilds.
    /// </summary>
    [InitializeOnLoad]
    internal static class DensityScenePaintSession
    {
        private const float DEFAULT_BRUSH_RADIUS = 2f;
        private const float DEFAULT_BRUSH_STRENGTH = 0.25f;
        private const float DEFAULT_BRUSH_FALLOFF = 0.5f;

        private static readonly ScatterBrush brush = new();

        private static DensityScatterLayer? currentLayer;
        private static TerrainScatterConfig? currentConfig;
        private static ScatterField? currentField;
        private static int currentLayerIndex = -1;
        private static bool isStrokeActive;
        private static bool showActivationBanner;
        private static DensityScenePaintMode currentMode = DensityScenePaintMode.Paint;

        private static float brushRadius = DEFAULT_BRUSH_RADIUS;
        private static float brushStrength = DEFAULT_BRUSH_STRENGTH;
        private static float brushFalloff = DEFAULT_BRUSH_FALLOFF;

        internal static event System.Action? SessionChanged;

        internal static bool IsActive => currentLayer != null && currentField != null && currentLayerIndex >= 0;
        internal static DensityScatterLayer? ActiveLayer => currentLayer;
        internal static DensityScenePaintMode Mode => currentMode;
        internal static float BrushRadius => brushRadius;
        internal static float BrushStrength => brushStrength;
        internal static float BrushFalloff => brushFalloff;

        static DensityScenePaintSession()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        internal static bool IsActiveFor(DensityScatterLayer? layer) =>
            layer != null && currentLayer == layer && IsActive;

        internal static void Enter(DensityScatterLayer? layer, DensityScenePaintMode mode)
        {
            if (layer == null)
            {
                EditorUtility.DisplayDialog(
                    "GrassInteract — Scene Paint",
                    "Select a DensityScatterLayer before activating SceneView paint.",
                    "OK");
                return;
            }

            if (!IsActiveFor(layer))
            {
                if (!TryActivate(layer, out string error))
                {
                    EditorUtility.DisplayDialog("GrassInteract — Scene Paint", error, "OK");
                    return;
                }
            }

            currentMode = mode;
            showActivationBanner = true;
            NotifySessionChanged();
            SceneView.RepaintAll();
        }

        internal static void Exit()
        {
            Deactivate();
        }

        internal static void SetMode(DensityScenePaintMode mode)
        {
            if (!IsActive)
                return;

            currentMode = mode;
            showActivationBanner = true;
            NotifySessionChanged();
            SceneView.RepaintAll();
        }

        internal static void Toggle(DensityScatterLayer? layer)
        {
            if (IsActiveFor(layer))
            {
                Exit();
                return;
            }

            Enter(layer, DensityScenePaintMode.Paint);
        }

        internal static void SetBrushRadius(float value)
        {
            brushRadius = Mathf.Max(0.25f, value);
            NotifySessionChanged();
            SceneView.RepaintAll();
        }

        internal static void SetBrushStrength(float value)
        {
            brushStrength = Mathf.Clamp01(value);
            NotifySessionChanged();
            SceneView.RepaintAll();
        }

        internal static void SetBrushFalloff(float value)
        {
            brushFalloff = Mathf.Clamp01(value);
            NotifySessionChanged();
            SceneView.RepaintAll();
        }

        private static bool TryActivate(DensityScatterLayer layer, out string error)
        {
            if (layer.DensityMap == null)
            {
                error = "This density layer has no density map assigned.";
                return false;
            }

            if (!layer.DensityMap.isReadable)
            {
                error = "The density map is not readable. Use the density-map validation fixes before painting in SceneView.";
                return false;
            }

            if (!ScatterFieldLookup.TryFindSingleActiveFieldForLayer(
                    layer,
                    out TerrainScatterConfig? config,
                    out ScatterField? field,
                    out int layerIndex,
                    out error))
            {
                return false;
            }

            currentLayer = layer;
            currentConfig = config;
            currentField = field;
            currentLayerIndex = layerIndex;
            currentMode = DensityScenePaintMode.Paint;
            isStrokeActive = false;
            showActivationBanner = true;
            brush.SetActiveLayer(field!, layer, layerIndex);
            NotifySessionChanged();
            return true;
        }

        private static void Deactivate()
        {
            if (isStrokeActive && currentLayer != null)
                brush.Save(currentLayer);

            currentLayer = null;
            currentConfig = null;
            currentField = null;
            currentLayerIndex = -1;
            isStrokeActive = false;
            showActivationBanner = false;
            currentMode = DensityScenePaintMode.Paint;
            NotifySessionChanged();
            SceneView.RepaintAll();
        }

        private static void NotifySessionChanged() => SessionChanged?.Invoke();

        private static void OnBeforeAssemblyReload() => Deactivate();

        private static void OnSceneGui(SceneView sceneView)
        {
            if (!IsActive || currentLayer == null || currentField == null || currentConfig == null)
                return;

            if (!IsSessionStillValid())
            {
                Deactivate();
                return;
            }

            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Deactivate();
                e.Use();
                return;
            }

            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hit = TryRaycastPaintSurface(ray, currentLayer, currentField, out RaycastHit raycastHit, out Vector3 planeHit);
            Vector3 hitPoint = hit ? raycastHit.point : planeHit;
            Vector3 hitNormal = hit ? raycastHit.normal : Vector3.up;
            bool isErase = currentMode == DensityScenePaintMode.Erase;
            if (e.shift)
                isErase = !isErase;

            if (e.type == EventType.Repaint && hit)
            {
                Texture2D preview = ScatterBrush.GetProceduralFalloffTexture(brushFalloff);
                Color tint = isErase ? new Color(1f, 0.35f, 0.35f, 1f) : new Color(0.35f, 1f, 0.35f, 1f);
                ScatterBrush.DrawTexturedCursor(sceneView, hitPoint, hitNormal, tint, preview, brushRadius, brushStrength);
                brush.DrawOverlay(sceneView);
                DrawStatusBanner(sceneView, isErase);
                showActivationBanner = false;
            }

            bool canPaintEvent = !e.alt && (e.button == 0 || e.button == -1);
            bool isPaintDown = e.type == EventType.MouseDown && e.button == 0 && !e.alt;
            bool isPaintDrag = e.type == EventType.MouseDrag && e.button == 0 && !e.alt;
            bool isPaintUp = e.type == EventType.MouseUp && e.button == 0;

            if (hit && canPaintEvent && (isPaintDown || isPaintDrag))
            {
                brush.Stamp(hitPoint, currentField, currentLayer, !isErase, brushRadius, brushStrength, brushFalloff);
                brush.ThrottledFlush();
                isStrokeActive = true;
                sceneView.Repaint();
                e.Use();
                return;
            }

            if (isPaintUp && isStrokeActive)
            {
                brush.Save(currentLayer);
                isStrokeActive = false;
                sceneView.Repaint();
                e.Use();
            }
        }

        private static bool IsSessionStillValid()
        {
            if (currentLayer == null || currentConfig == null || currentField == null)
                return false;
            if (currentField.Config != currentConfig)
                return false;
            if (currentLayerIndex < 0 || currentLayerIndex >= currentConfig.Layers.Count)
                return false;
            return currentConfig.Layers[currentLayerIndex] == currentLayer;
        }

        private static bool TryRaycastPaintSurface(
            Ray ray,
            DensityScatterLayer layer,
            ScatterField field,
            out RaycastHit raycastHit,
            out Vector3 planeHit)
        {
            int mask = layer.GroundSnapMask;
            if (mask == 0)
                mask = ~0;

            if (Physics.Raycast(ray, out raycastHit, 5000f, mask, QueryTriggerInteraction.Ignore))
            {
                planeHit = raycastHit.point;
                return true;
            }

            Plane plane = new Plane(Vector3.up, ScatterBrush.ResolveFieldOrigin(field));
            if (plane.Raycast(ray, out float enter))
            {
                planeHit = ray.GetPoint(enter);
                raycastHit = default;
                return true;
            }

            planeHit = ScatterBrush.ResolveFieldOrigin(field);
            raycastHit = default;
            return false;
        }

        private static void DrawStatusBanner(SceneView sceneView, bool isErase)
        {
            Handles.BeginGUI();
            string modeText = isErase ? "Erase" : "Paint";
            string bannerText = $"Density Scene Paint — {modeText}  |  Radius {brushRadius:F1}m  Strength {brushStrength:F2}  Falloff {brushFalloff:F2}  |  Esc to exit";
            if (showActivationBanner)
                bannerText = "Density Scene Paint activated. " + bannerText;

            Rect rect = new Rect(10f, 10f, Mathf.Min(sceneView.position.width - 20f, 720f), 22f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 3f, rect.width - 12f, rect.height - 6f), bannerText);
            Handles.EndGUI();
        }
    }
}
