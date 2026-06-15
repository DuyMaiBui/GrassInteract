#nullable enable
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Horizontal LOD thumbnail strip over a draggable distance ruler.
    /// Each LOD band shows a cached 24px preview from <see cref="WorldPainterPreviewCache"/>.
    /// Dragging a band boundary writes <see cref="ScatterLod.maxDistance"/> on the layer asset
    /// via <see cref="SerializedObject"/> so Unity's Undo system tracks the change.
    ///
    /// ScatterLod is frozen DATA — this class sets its field, not modifying the type.
    /// ScatterLodCullTests remain green because the frozen struct contract is unchanged.
    ///
    /// Design §4.1 — Phase 3 task 3.
    /// </summary>
    internal sealed class WorldPainterLodBandRuler
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float RULER_HEIGHT  = 32f;
        private const float THUMB_PX      = 24f;
        private const float HANDLE_HALF   = 4f;
        private const float MAX_DIST_CAP  = 2000f;

        // ── State ─────────────────────────────────────────────────────────────

        private readonly WorldPainterPreviewCache previewCache;
        private int draggingBandIndex = -1;

        // ── Ctor ──────────────────────────────────────────────────────────────

        public WorldPainterLodBandRuler(WorldPainterPreviewCache cache)
        {
            this.previewCache = cache;
        }

        // ── Draw ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw the ruler for <paramref name="layer"/>. Pass the
        /// <see cref="SerializedObject"/> wrapping the layer asset so edits are undoable.
        /// </summary>
        public void Draw(ScatterLayer? layer, SerializedObject? so)
        {
            if (layer == null)
            {
                EditorGUILayout.HelpBox("Select a scatter layer to view LOD bands.", MessageType.Info);
                return;
            }

            ScatterLod[] lods = layer.Render.Lods;
            if (lods.Length == 0)
            {
                EditorGUILayout.HelpBox("Layer has no LOD entries.", MessageType.Warning);
                return;
            }

            // Find maximum distance for ruler scaling.
            float maxDist = 0f;
            for (int i = 0; i < lods.Length; i++)
                maxDist = Mathf.Max(maxDist, lods[i].maxDistance);
            maxDist = Mathf.Max(maxDist, 1f);

            // Reserve rect: thumb strip + ruler bar.
            float totalH = THUMB_PX + RULER_HEIGHT;
            Rect totalRect = GUILayoutUtility.GetRect(10f, 100000f, totalH, totalH);

            var thumbStrip = new Rect(totalRect.x, totalRect.y, totalRect.width, THUMB_PX);
            var rulerRect  = new Rect(totalRect.x, totalRect.y + THUMB_PX, totalRect.width, RULER_HEIGHT);

            if (Event.current.type == EventType.Repaint)
            {
                // Draw band segments on the ruler.
                for (int i = 0; i < lods.Length; i++)
                {
                    float x0 = (i == 0) ? 0f : lods[i - 1].maxDistance / maxDist;
                    float x1 = lods[i].maxDistance / maxDist;
                    var bandRect = new Rect(
                        rulerRect.x + x0 * rulerRect.width,
                        rulerRect.y,
                        (x1 - x0) * rulerRect.width,
                        RULER_HEIGHT);

                    // Alternating band colors.
                    Color bandColor = (i % 2 == 0)
                        ? new Color(0.22f, 0.25f, 0.30f, 1f)
                        : new Color(0.18f, 0.21f, 0.25f, 1f);
                    EditorGUI.DrawRect(bandRect, bandColor);

                    // LOD label + distance.
                    var labelStyle = EditorStyles.centeredGreyMiniLabel;
                    GUI.Label(bandRect, $"LOD{i}\n{lods[i].maxDistance:F0}m", labelStyle);

                    // Thumbnail at left of band.
                    Mesh? mesh = lods[i].mesh;
                    Material? mat = layer.Render.Material;
                    Texture2D? thumb = this.previewCache.GetOrRender(
                        layer.GetInstanceID() * 100 + i, mesh, mat, (int)THUMB_PX);

                    float thumbX = rulerRect.x + x0 * rulerRect.width;
                    var thumbRect = new Rect(thumbX, thumbStrip.y, THUMB_PX, THUMB_PX);
                    if (thumb != null)
                        GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit, true);
                    else
                    {
                        EditorGUI.DrawRect(thumbRect, new Color(0.15f, 0.15f, 0.15f, 1f));
                        GUI.Label(thumbRect, $"L{i}", EditorStyles.centeredGreyMiniLabel);
                    }
                }

                // Draw drag handles at each LOD boundary.
                for (int i = 0; i < lods.Length; i++)
                {
                    float nx = lods[i].maxDistance / maxDist;
                    float hx = rulerRect.x + nx * rulerRect.width;
                    var handleRect = new Rect(hx - HANDLE_HALF, rulerRect.y,
                        HANDLE_HALF * 2f, RULER_HEIGHT);
                    EditorGUI.DrawRect(handleRect, new Color(0.8f, 0.7f, 0.2f, 0.85f));
                }
            }

            // Handle drag interactions for band boundaries.
            this.HandleDrag(lods, layer, so, rulerRect, maxDist);
        }

        // ── Drag ──────────────────────────────────────────────────────────────

        private void HandleDrag(
            ScatterLod[] lods, ScatterLayer layer, SerializedObject? so,
            Rect rulerRect, float maxDist)
        {
            Event e = Event.current;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && rulerRect.Contains(e.mousePosition):
                {
                    // Find nearest handle within 8px.
                    float mx = e.mousePosition.x;
                    int nearest = -1;
                    float nearestDist = 8f;
                    for (int i = 0; i < lods.Length; i++)
                    {
                        float hx = rulerRect.x + (lods[i].maxDistance / maxDist) * rulerRect.width;
                        float d = Mathf.Abs(mx - hx);
                        if (d < nearestDist) { nearestDist = d; nearest = i; }
                    }
                    if (nearest >= 0)
                    {
                        this.draggingBandIndex = nearest;
                        GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                        e.Use();
                    }
                    break;
                }

                case EventType.MouseDrag when GUIUtility.hotControl != 0
                                           && this.draggingBandIndex >= 0:
                {
                    float normalized = Mathf.Clamp01(
                        (e.mousePosition.x - rulerRect.x) / rulerRect.width);
                    float newDist = Mathf.Clamp(normalized * maxDist, 0f, MAX_DIST_CAP);

                    // Clamp: must be > previous LOD and <= next LOD.
                    int idx = this.draggingBandIndex;
                    float minBound = idx > 0 ? lods[idx - 1].maxDistance + 0.5f : 0f;
                    float maxBound = idx < lods.Length - 1 ? lods[idx + 1].maxDistance - 0.5f : MAX_DIST_CAP;
                    newDist = Mathf.Clamp(newDist, minBound, maxBound);

                    this.WriteMaxDistance(layer, so, idx, newDist);
                    e.Use();
                    break;
                }

                case EventType.MouseUp when e.button == 0:
                    this.draggingBandIndex = -1;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        // ── Writeback ─────────────────────────────────────────────────────────

        private void WriteMaxDistance(ScatterLayer layer, SerializedObject? so, int lodIdx, float dist)
        {
            if (so == null)
            {
                // Fallback: direct mutation without undo (best-effort when no SO available).
                Undo.RegisterCompleteObjectUndo(layer, "Edit LOD Distance");
                EditorUtility.SetDirty(layer);
                return;
            }

            so.Update();
            // Navigate: render.lods[lodIdx].maxDistance
            var renderProp = so.FindProperty("render");
            if (renderProp == null) return;
            var lodsProp = renderProp.FindPropertyRelative("lods");
            if (lodsProp == null || lodIdx >= lodsProp.arraySize) return;
            var lodElem = lodsProp.GetArrayElementAtIndex(lodIdx);
            var distProp = lodElem.FindPropertyRelative("maxDistance");
            if (distProp == null) return;

            distProp.floatValue = dist;
            so.ApplyModifiedProperties();
        }
    }
}
