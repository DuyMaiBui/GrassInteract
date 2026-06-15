#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// IMGUI segmented LOD distance bar (Unity-LODGroup style) for a scatter layer's
    /// <c>render</c> (<see cref="ScatterRenderConfig"/>) struct.
    ///   - Read-only render: LOD0/LOD1/LOD2 colored segments + Culled cap,
    ///     closeness-% labels (1 - dist/renderCullDistance), metres on hover.
    ///   - Draggable handles write back through <c>SerializedProperty.ApplyModifiedProperties</c> (SSOT).
    ///
    /// Switch distances: lods[0..lodCount-2].maxDistance (ascending).
    /// Last LOD band spans [lastSwitch .. renderCullDistance).
    ///
    /// Ported from the GrassInteract Scatter-Studio <c>LodDistanceBar</c>.
    /// </summary>
    internal static class WorldPainterLodDistanceBar
    {
        // ── Constants ─────────────────────────────────────────────────────────

        public const float BAR_HEIGHT    = 26f;
        private const float HANDLE_WIDTH  = 8f;   // hit-test half-width (px)
        /// <summary>Minimum band width (metres) enforced between adjacent handles to prevent inversion.</summary>
        private const float MIN_BAND_GAP  = 0.01f;

        // ── Palette ───────────────────────────────────────────────────────────

        private static readonly Color[] LOD_COLORS =
        {
            new Color(0.36f, 0.62f, 0.30f), // LOD0 green
            new Color(0.85f, 0.70f, 0.25f), // LOD1 amber
            new Color(0.70f, 0.45f, 0.25f), // LOD2 brown
        };
        private static readonly Color CULLED_COLOR  = new Color(0.65f, 0.20f, 0.20f);
        private static readonly Color HANDLE_COLOR  = new Color(1.0f,  1.0f,  1.0f, 0.7f);

        // ── Hot-control state (static — one bar visible at a time) ────────────

        // handleIndex in [0 .. lodCount-2] → switch handle; lodCount-1 → cull handle.
        private static int capturedHandle = -1;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Draws the bar inside <paramref name="rect"/>.
        /// <paramref name="renderProp"/> must be the SerializedProperty for the "render" struct field.
        /// </summary>
        public static void Draw(Rect rect, SerializedProperty renderProp)
        {
            SerializedProperty? lodsProp = renderProp.FindPropertyRelative("lods");
            SerializedProperty? cullProp  = renderProp.FindPropertyRelative("renderCullDistance");

            if (lodsProp == null || cullProp == null) return;

            float cull     = cullProp.floatValue;
            int   lodCount = lodsProp.arraySize;

            if (cull <= 0f)
            {
                EditorGUI.HelpBox(rect, "renderCullDistance is 0 — set it to draw the LOD bar.", MessageType.Info);
                return;
            }

            // ── Step A: draw segments ─────────────────────────────────────────

            float prev = 0f;
            for (int i = 0; i < lodCount; ++i)
            {
                float bandEnd = (i < lodCount - 1)
                    ? lodsProp.GetArrayElementAtIndex(i).FindPropertyRelative("maxDistance")!.floatValue
                    : cull; // last LOD band ends at cull

                Rect seg   = SliceByDistance(rect, prev, bandEnd, cull);
                Color color = LOD_COLORS[Mathf.Min(i, LOD_COLORS.Length - 1)];
                EditorGUI.DrawRect(seg, color);
                DrawSegLabel(seg, $"LOD{i}", prev, bandEnd, cull);
                prev = bandEnd;
            }

            // Thin red Culled cap at right edge
            {
                float capWidth = Mathf.Min(20f, rect.width * 0.05f);
                Rect cap = new Rect(rect.xMax - capWidth, rect.y, capWidth, rect.height);
                EditorGUI.DrawRect(cap, CULLED_COLOR);
                var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(cap, new GUIContent("C", $"{cull:0} m"), style);
            }

            // ── Step B: draggable handles ─────────────────────────────────────

            // Build handle x-positions: [0..lodCount-2] = switch handles, [lodCount-1] = cull handle
            // (only switch handles exist for lodCount>1; the cull handle is always last)
            int handleCount = lodCount; // lodCount-1 switches + 1 cull
            if (handleCount < 1) return;

            float[] handleDists = new float[handleCount];
            for (int i = 0; i < lodCount - 1; ++i)
                handleDists[i] = lodsProp.GetArrayElementAtIndex(i).FindPropertyRelative("maxDistance")!.floatValue;
            handleDists[handleCount - 1] = cull; // last handle = cull

            // Draw handle ticks
            for (int h = 0; h < handleCount; ++h)
            {
                float hx = rect.x + rect.width * Mathf.Clamp01(handleDists[h] / cull);
                Rect tickRect = new Rect(hx - 1f, rect.y, 2f, rect.height);
                EditorGUI.DrawRect(tickRect, HANDLE_COLOR);
            }

            // Process input
            ProcessHandleInput(rect, renderProp, lodsProp, cullProp, handleDists, handleCount, cull);
        }

        // ── Handle interaction (Step B) ───────────────────────────────────────

        private static void ProcessHandleInput(
            Rect rect, SerializedProperty renderProp,
            SerializedProperty lodsProp, SerializedProperty cullProp,
            float[] handleDists, int handleCount, float cull)
        {
            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive, rect);

            switch (e.type)
            {
                case EventType.MouseDown when rect.Contains(e.mousePosition):
                {
                    // Find closest handle within HANDLE_WIDTH px
                    int best  = -1;
                    float bestDx = HANDLE_WIDTH;
                    for (int h = 0; h < handleCount; ++h)
                    {
                        float hx = rect.x + rect.width * Mathf.Clamp01(handleDists[h] / cull);
                        float dx = Mathf.Abs(e.mousePosition.x - hx);
                        if (dx < bestDx) { bestDx = dx; best = h; }
                    }
                    if (best >= 0)
                    {
                        capturedHandle          = best;
                        GUIUtility.hotControl   = controlId;
                        e.Use();
                    }
                    break;
                }

                case EventType.MouseDrag when GUIUtility.hotControl == controlId && capturedHandle >= 0:
                {
                    float newDist = Remap(e.mousePosition.x, rect.x, rect.xMax, 0f, cull);
                    newDist       = ClampHandle(newDist, capturedHandle, handleDists, handleCount);

                    bool isCull = capturedHandle == handleCount - 1;
                    if (isCull)
                    {
                        cullProp.floatValue = newDist;
                    }
                    else
                    {
                        lodsProp.GetArrayElementAtIndex(capturedHandle)
                                .FindPropertyRelative("maxDistance")!.floatValue = newDist;
                    }
                    renderProp.serializedObject.ApplyModifiedProperties();
                    e.Use();
                    break;
                }

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                {
                    capturedHandle        = -1;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static float Remap(float x, float srcMin, float srcMax, float dstMin, float dstMax)
        {
            float t = Mathf.Clamp01((x - srcMin) / (srcMax - srcMin));
            return Mathf.Lerp(dstMin, dstMax, t);
        }

        /// <summary>Clamp handle at <paramref name="idx"/> so bands never invert.</summary>
        private static float ClampHandle(float dist, int idx, float[] dists, int count)
        {
            bool isCull = idx == count - 1;
            if (isCull)
            {
                // Cull handle: must stay >= last switch distance (or 0 if no switches)
                float minCull = count >= 2 ? dists[count - 2] : 0f;
                return Mathf.Max(dist, minCull);
            }
            else
            {
                // Switch handle: must stay > previous switch (or 0) and < next switch (or cull)
                float lo = idx > 0       ? dists[idx - 1] : 0f;
                float hi = idx < count - 1 ? dists[idx + 1] : dists[count - 1]; // neighbor switch or cull
                return Mathf.Clamp(dist, lo + MIN_BAND_GAP, hi - MIN_BAND_GAP);
            }
        }

        private static Rect SliceByDistance(Rect rect, float dStart, float dEnd, float cull)
        {
            float x0 = rect.x + rect.width * Mathf.Clamp01(dStart / cull);
            float x1 = rect.x + rect.width * Mathf.Clamp01(dEnd   / cull);
            return new Rect(x0, rect.y, Mathf.Max(1f, x1 - x0), rect.height);
        }

        private static void DrawSegLabel(Rect seg, string lod, float dStart, float dEnd, float cull)
        {
            float closeNear = 1f - Mathf.Clamp01(dStart / cull);
            var content = new GUIContent($"{lod}\n{closeNear * 100f:0}%", $"{dStart:0}–{dEnd:0} m");
            var style   = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(seg, content, style);
        }
    }
}
