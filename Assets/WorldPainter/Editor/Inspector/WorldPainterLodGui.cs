#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// SSOT for the inline LOD editors drawn directly in the layer inspectors — the
    /// <see cref="GrassLayerEditor"/> / <see cref="PropLayerEditor"/> Render section and the
    /// WorldPainter terrain card. Replaces the former standalone setup popup: selecting a layer
    /// now shows its LOD bar inline.
    ///
    /// All edits mutate the host's live <see cref="SerializedProperty"/> (Undo + the host editor's
    /// own change-check drive the rebuild). Manual bar-drag / add / remove edits — which the host's
    /// <see cref="EditorGUI.EndChangeCheck"/> would otherwise miss — set <c>GUI.changed = true</c>
    /// so the host catches them. The host owns <c>serializedObject.Update()</c> / <c>ApplyModifiedProperties()</c>.
    /// </summary>
    internal static class WorldPainterLodGui
    {
        // ── Palette (mirrors Unity's LODGroupEditor band colours) ────────────────
        private static readonly Color[] LOD_COLORS =
        {
            new Color(0.40f, 0.45f, 0.20f, 1f), // LOD 0 — olive
            new Color(0.25f, 0.32f, 0.45f, 1f), // LOD 1 — dark blue
            new Color(0.32f, 0.32f, 0.32f, 1f), // LOD 2 — gray
            new Color(0.36f, 0.20f, 0.34f, 1f), // LOD 3 — magenta
            new Color(0.20f, 0.36f, 0.34f, 1f), // LOD 4 — teal
        };
        private static readonly Color CULLED_COLOR = new Color(0.45f, 0.10f, 0.10f, 1f);
        private static readonly Color COARSE_COLOR = new Color(0.22f, 0.22f, 0.26f, 1f);
        private static readonly Color HANDLE_COLOR = new Color(0.85f, 0.85f, 0.85f, 0.95f);

        private const float BAR_HEIGHT  = 44f;
        private const float HANDLE_HALF = 4f;

        private const int   TERRAIN_MAX_BANDS = 8;   // CdlodQuadtree.MAX_LOD_BANDS
        private const float TERRAIN_MIN_GAP   = 0.5f;

        // One bar interacted with at a time across the whole inspector (grass/prop XOR terrain).
        private static int draggingHandle = -1;

        // ══════════════════════════════════════════════════════════════════════
        //  Scatter LOD (grass / prop) — operates on the layer's "render" struct
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws material, shadow, the distance bar, per-LOD mesh + switch-distance rows,
        /// add/remove, and the cull distance for <paramref name="renderProp"/> (the layer's
        /// <c>render</c> <see cref="ScatterRenderConfig"/> property).
        /// </summary>
        public static void DrawScatterLodSection(SerializedProperty renderProp)
        {
            SerializedProperty? lodsProp   = renderProp.FindPropertyRelative("lods");
            SerializedProperty? cullProp   = renderProp.FindPropertyRelative("renderCullDistance");
            SerializedProperty? matProp    = renderProp.FindPropertyRelative("material");
            SerializedProperty? shadowProp = renderProp.FindPropertyRelative("shadowCastingMode");

            if (lodsProp == null || cullProp == null)
            {
                EditorGUILayout.HelpBox("render struct is missing 'lods' / 'renderCullDistance'.",
                    MessageType.Error);
                return;
            }

            if (matProp    != null) EditorGUILayout.PropertyField(matProp);
            if (shadowProp != null) EditorGUILayout.PropertyField(shadowProp);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("LOD Meshes", EditorStyles.boldLabel);

            float cull = cullProp.floatValue;
            DrawScatterBar(lodsProp, cull);

            EditorGUILayout.Space(4f);

            int n        = lodsProp.arraySize;
            int removeAt = -1;
            for (int i = 0; i < n; ++i)
            {
                SerializedProperty  lodElem  = lodsProp.GetArrayElementAtIndex(i);
                SerializedProperty? meshProp = lodElem.FindPropertyRelative("mesh");
                SerializedProperty? distProp = lodElem.FindPropertyRelative("maxDistance");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"LOD {i}", GUILayout.Width(45f));
                if (meshProp != null)
                    EditorGUILayout.PropertyField(meshProp, GUIContent.none);

                if (i < n - 1 && distProp != null)
                {
                    EditorGUILayout.LabelField("Max", GUILayout.Width(28f));
                    EditorGUILayout.PropertyField(distProp, GUIContent.none, GUILayout.Width(60f));
                }
                else
                {
                    EditorGUILayout.LabelField("→ cull", GUILayout.Width(92f));
                }

                if (GUILayout.Button("✕", GUILayout.Width(22f)))
                    removeAt = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0)
            {
                lodsProp.DeleteArrayElementAtIndex(removeAt);
                GUI.changed = true;
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add LOD", GUILayout.Width(90f)))
            {
                AddScatterLod(lodsProp, cull);
                GUI.changed = true;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Cull (m)", GUILayout.Width(54f));
            EditorGUILayout.PropertyField(cullProp, GUIContent.none, GUILayout.Width(70f));
            EditorGUILayout.EndHorizontal();
        }

        private static void AddScatterLod(SerializedProperty lodsProp, float cull)
        {
            int last = lodsProp.arraySize;          // becomes the index of the new (coarsest) element
            lodsProp.arraySize = last + 1;

            // arraySize-grow copies the previous last element — clear the new slot's mesh so it's a
            // distinct empty coarse LOD (→ cull), and seed the now-inner element's switch distance.
            SerializedProperty  newElem = lodsProp.GetArrayElementAtIndex(last);
            SerializedProperty? newMesh = newElem.FindPropertyRelative("mesh");
            if (newMesh != null) newMesh.objectReferenceValue = null;

            if (last - 1 >= 0)
            {
                float prevSwitch = last - 2 >= 0
                    ? lodsProp.GetArrayElementAtIndex(last - 2).FindPropertyRelative("maxDistance")!.floatValue
                    : 0f;
                float seed = cull > 0f ? Mathf.Lerp(prevSwitch, cull, 0.5f) : prevSwitch + 10f;
                lodsProp.GetArrayElementAtIndex(last - 1).FindPropertyRelative("maxDistance")!.floatValue =
                    Mathf.Max(seed, prevSwitch + 0.5f);
            }
        }

        private static void DrawScatterBar(SerializedProperty lodsProp, float cull)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, BAR_HEIGHT, GUILayout.ExpandWidth(true));
            rect.x     += 4f;
            rect.width -= 8f;

            if (cull <= 0f)
            {
                EditorGUI.DrawRect(rect, CULLED_COLOR);
                GUI.Label(new Rect(rect.x + 6f, rect.y + 12f, rect.width - 8f, 18f),
                    "Set Cull (m) > 0 to show LOD bands", EditorStyles.miniLabel);
                return;
            }

            int   n     = lodsProp.arraySize;
            float prevX = rect.x;
            for (int i = 0; i < n; ++i)
            {
                float dist = i < n - 1
                    ? lodsProp.GetArrayElementAtIndex(i).FindPropertyRelative("maxDistance")!.floatValue
                    : cull;
                float frac      = Mathf.Clamp01(dist / cull);
                float bandRight = rect.x + frac * rect.width;
                var   bandRect  = new Rect(prevX, rect.y, Mathf.Max(0f, bandRight - prevX), rect.height);

                EditorGUI.DrawRect(bandRect, LOD_COLORS[i % LOD_COLORS.Length]);
                GUI.contentColor = Color.white;
                GUI.Label(new Rect(bandRect.x + 5f, bandRect.y + 4f,  Mathf.Max(12f, bandRect.width - 8f), 16f),
                    $"LOD {i}", EditorStyles.boldLabel);
                GUI.Label(new Rect(bandRect.x + 5f, bandRect.y + 22f, Mathf.Max(12f, bandRect.width - 8f), 16f),
                    $"{dist:0}m", EditorStyles.miniLabel);

                // Inner boundaries are draggable; the last boundary IS cull (edited via the Cull field).
                if (i < n - 1)
                    DragHandleScatter(rect, cull, bandRight, lodsProp, i);

                prevX = bandRight;
            }

            ReleaseGuard();
        }

        private static void DragHandleScatter(Rect rect, float cull, float bandRight,
            SerializedProperty lodsProp, int i)
        {
            var handleRect = new Rect(bandRight - HANDLE_HALF, rect.y, HANDLE_HALF * 2f, rect.height);
            EditorGUI.DrawRect(handleRect, HANDLE_COLOR);
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && handleRect.Contains(e.mousePosition))
            {
                draggingHandle = i;
                e.Use();
            }
            if (draggingHandle == i && e.type == EventType.MouseDrag)
            {
                float xLocal  = Mathf.Clamp(e.mousePosition.x - rect.x, 0f, rect.width);
                float newDist = (xLocal / rect.width) * cull;
                lodsProp.GetArrayElementAtIndex(i).FindPropertyRelative("maxDistance")!.floatValue =
                    ClampScatter(lodsProp, i, cull, newDist);
                GUI.changed = true;
                e.Use();
            }
        }

        private static float ClampScatter(SerializedProperty lodsProp, int i, float cull, float value)
        {
            int   n  = lodsProp.arraySize;
            float lo = i > 0     ? lodsProp.GetArrayElementAtIndex(i - 1).FindPropertyRelative("maxDistance")!.floatValue + 0.01f : 0f;
            float hi = i < n - 2 ? lodsProp.GetArrayElementAtIndex(i + 1).FindPropertyRelative("maxDistance")!.floatValue - 0.01f : cull;
            if (hi < lo) hi = lo;
            return Mathf.Clamp(value, lo, hi);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Terrain LOD (CDLOD distance bands) — operates on WorldPainter.lodRangesM
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draws the terrain LOD distance-band editor for <paramref name="ranges"/> (the
        /// <c>lodRangesM</c> float[] property). Bands are ascending max-distance thresholds — no
        /// meshes (continuous tessellation). Past the last band the terrain stays at the coarsest LOD.
        /// </summary>
        public static void DrawTerrainLodSection(SerializedProperty ranges)
        {
            if (!ranges.isArray)
            {
                EditorGUILayout.HelpBox("lodRangesM is not an array.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("LOD Distance Bands", EditorStyles.boldLabel);
            DrawTerrainBar(ranges);

            EditorGUILayout.Space(4f);

            int count    = ranges.arraySize;
            int removeAt = -1;
            for (int i = 0; i < count; ++i)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"LOD {i}", GUILayout.Width(45f));
                EditorGUILayout.LabelField("Max dist (m)", GUILayout.Width(80f));

                SerializedProperty elem = ranges.GetArrayElementAtIndex(i);
                EditorGUI.BeginChangeCheck();
                float d = EditorGUILayout.FloatField(elem.floatValue, GUILayout.Width(70f));
                if (EditorGUI.EndChangeCheck())
                    elem.floatValue = ClampTerrain(ranges, i, d);

                using (new EditorGUI.DisabledScope(count <= 1))
                {
                    if (GUILayout.Button("✕", GUILayout.Width(22f)))
                        removeAt = i;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0 && ranges.arraySize > 1)
            {
                ranges.DeleteArrayElementAtIndex(removeAt);
                GUI.changed = true;
            }

            EditorGUILayout.Space(2f);
            using (new EditorGUI.DisabledScope(ranges.arraySize >= TERRAIN_MAX_BANDS))
            {
                if (GUILayout.Button("+ Add LOD band", GUILayout.Width(120f)))
                {
                    int   last    = ranges.arraySize;
                    float prevVal = last > 0 ? ranges.GetArrayElementAtIndex(last - 1).floatValue : 32f;
                    ranges.arraySize = last + 1;
                    ranges.GetArrayElementAtIndex(last).floatValue = Mathf.Max(prevVal * 2f, prevVal + 1f);
                    GUI.changed = true;
                }
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "Each band = the max camera distance (m) at which that terrain LOD level renders. " +
                "Values ascend (LOD 0 = finest, nearest). Beyond the last band the terrain stays at the " +
                "coarsest level (no cull). Drag the light handles on the bar or edit the fields.",
                MessageType.None);
        }

        private static void DrawTerrainBar(SerializedProperty ranges)
        {
            int  n    = ranges.arraySize;
            Rect rect = GUILayoutUtility.GetRect(0f, BAR_HEIGHT, GUILayout.ExpandWidth(true));
            rect.x     += 4f;
            rect.width -= 8f;

            if (n == 0) { EditorGUI.DrawRect(rect, CULLED_COLOR); return; }

            float maxDist = ranges.GetArrayElementAtIndex(n - 1).floatValue;
            if (maxDist <= 0f)
            {
                EditorGUI.DrawRect(rect, CULLED_COLOR);
                GUI.Label(new Rect(rect.x + 6f, rect.y + 12f, rect.width - 8f, 18f),
                    "Set a positive LOD distance", EditorStyles.miniLabel);
                return;
            }

            float capWidth = Mathf.Min(30f, rect.width * 0.1f);
            float barWidth = Mathf.Max(1f, rect.width - capWidth);

            float prevX = rect.x;
            for (int i = 0; i < n; ++i)
            {
                float dist      = ranges.GetArrayElementAtIndex(i).floatValue;
                float frac      = Mathf.Clamp01(dist / maxDist);
                float bandRight = rect.x + frac * barWidth;
                var   bandRect  = new Rect(prevX, rect.y, Mathf.Max(0f, bandRight - prevX), rect.height);

                EditorGUI.DrawRect(bandRect, LOD_COLORS[i % LOD_COLORS.Length]);
                GUI.contentColor = Color.white;
                GUI.Label(new Rect(bandRect.x + 5f, bandRect.y + 4f,  Mathf.Max(12f, bandRect.width - 8f), 16f),
                    $"LOD {i}", EditorStyles.boldLabel);
                GUI.Label(new Rect(bandRect.x + 5f, bandRect.y + 22f, Mathf.Max(12f, bandRect.width - 8f), 16f),
                    $"{dist:0}m", EditorStyles.miniLabel);

                if (i < n - 1)
                    DragHandleTerrain(rect, barWidth, maxDist, bandRight, ranges, i);

                prevX = bandRight;
            }

            // Coarsest-beyond cap — terrain never culls; past the last band it stays at the coarsest LOD.
            var capRect = new Rect(rect.x + barWidth, rect.y, capWidth, rect.height);
            EditorGUI.DrawRect(capRect, COARSE_COLOR);
            GUI.contentColor = Color.white;
            GUI.Label(new Rect(capRect.x + 3f, capRect.y + 4f,  capWidth - 4f, 16f), "∞",      EditorStyles.boldLabel);
            GUI.Label(new Rect(capRect.x + 3f, capRect.y + 22f, capWidth - 4f, 16f), "coarse", EditorStyles.miniLabel);

            ReleaseGuard();
        }

        private static void DragHandleTerrain(Rect rect, float barWidth, float maxDist, float bandRight,
            SerializedProperty ranges, int i)
        {
            var handleRect = new Rect(bandRight - HANDLE_HALF, rect.y, HANDLE_HALF * 2f, rect.height);
            EditorGUI.DrawRect(handleRect, HANDLE_COLOR);
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && handleRect.Contains(e.mousePosition))
            {
                draggingHandle = i;
                e.Use();
            }
            if (draggingHandle == i && e.type == EventType.MouseDrag)
            {
                float xLocal  = Mathf.Clamp(e.mousePosition.x - rect.x, 0f, barWidth);
                float newDist = (xLocal / barWidth) * maxDist;
                ranges.GetArrayElementAtIndex(i).floatValue = ClampTerrain(ranges, i, newDist);
                GUI.changed = true;
                e.Use();
            }
        }

        private static float ClampTerrain(SerializedProperty ranges, int i, float value)
        {
            int   n  = ranges.arraySize;
            float lo = i > 0     ? ranges.GetArrayElementAtIndex(i - 1).floatValue + TERRAIN_MIN_GAP : 0.5f;
            float hi = i < n - 1 ? ranges.GetArrayElementAtIndex(i + 1).floatValue - TERRAIN_MIN_GAP : float.MaxValue;
            if (hi < lo) hi = lo;
            return Mathf.Clamp(value, lo, hi);
        }

        // ── Shared release guard ──────────────────────────────────────────────
        private static void ReleaseGuard()
        {
            if (Event.current.type == EventType.MouseUp && draggingHandle >= 0)
            {
                draggingHandle = -1;
                GUI.changed = true;
                Event.current.Use();
            }
        }
    }
}
