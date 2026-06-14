#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Setup panel for a <see cref="GrassLayer"/> or <see cref="PropLayer"/>. Lets the artist
    /// assign LOD meshes (drag &amp; drop), tune per-LOD switch distances via a horizontal LOD
    /// bar (mirrors Unity's <c>LODGroupEditor</c> UX, distance-mapped instead of screen-height),
    /// and — for prop layers — preview the anchor offset in a small 3-D viewport.
    ///
    /// Opens via the gear button on each surface-layer row in the layer-stack view AND
    /// automatically the first time a new layer is created (so the artist configures meshes
    /// before painting).
    /// </summary>
    internal sealed class WorldPainterLayerSetupWindow : EditorWindow
    {
        // ── LOD bar palette (mirrors Unity's LODGroupEditor band colours) ────────
        private static readonly Color[] LOD_COLORS =
        {
            new Color(0.40f, 0.45f, 0.20f, 1f), // LOD 0 — olive
            new Color(0.25f, 0.32f, 0.45f, 1f), // LOD 1 — dark blue
            new Color(0.32f, 0.32f, 0.32f, 1f), // LOD 2 — gray
            new Color(0.36f, 0.20f, 0.34f, 1f), // LOD 3 — magenta
            new Color(0.20f, 0.36f, 0.34f, 1f), // LOD 4 — teal
        };
        private static readonly Color CULLED_COLOR  = new Color(0.45f, 0.10f, 0.10f, 1f);
        private static readonly Color HANDLE_COLOR  = new Color(0.85f, 0.85f, 0.85f, 0.95f);

        private const float BAR_HEIGHT       = 44f;
        private const float HANDLE_HALF      = 4f;
        private const float PREVIEW_WIDTH    = 220f;
        private const float PREVIEW_HEIGHT   = 180f;
        private const float PREVIEW_FOV      = 35f;
        private const float AXIS_LENGTH      = 0.1f;

        // ── State ─────────────────────────────────────────────────────────────
        private WorldPainterLayer? layer;
        private WorldPainter?      painter;
        private Vector2            scroll;
        private int                draggingHandle = -1;

        // Prop anchor preview — only created on demand.
        private PreviewRenderUtility? previewUtil;
        private Vector2               previewDrag;
        private float                 previewDistance = 3f;
        private static Mesh?          lineAxisMesh;
        private Material?             lineMatRed;
        private Material?             lineMatGreen;
        private Material?             lineMatBlue;

        // ── Open ──────────────────────────────────────────────────────────────
        public static void Open(WorldPainterLayer layer, WorldPainter? painter)
        {
            var w = GetWindow<WorldPainterLayerSetupWindow>(utility: true,
                title: $"Setup: {layer.name}", focus: true);
            w.layer    = layer;
            w.painter  = painter;
            w.minSize  = new Vector2(440f, 360f);
            w.Show();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void OnDisable()
        {
            this.previewUtil?.Cleanup();
            this.previewUtil = null;
            if (this.lineMatRed   != null) { DestroyImmediate(this.lineMatRed);   this.lineMatRed   = null; }
            if (this.lineMatGreen != null) { DestroyImmediate(this.lineMatGreen); this.lineMatGreen = null; }
            if (this.lineMatBlue  != null) { DestroyImmediate(this.lineMatBlue);  this.lineMatBlue  = null; }
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (this.layer == null) { this.Close(); return; }

            EditorGUILayout.LabelField(this.layer.name, EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            this.scroll = EditorGUILayout.BeginScrollView(this.scroll);
            EditorGUI.BeginChangeCheck();

            this.DrawLodSection();

            if (this.layer is PropLayer prop)
            {
                EditorGUILayout.Space(8f);
                this.DrawAnchorSection(prop);
            }

            bool changed = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndScrollView();

            // Live preview: coalesced rebuild via the shared scheduler so the GPU buffer churn
            // doesn't bleed into Game/Inspector views as flicker (see WorldPainterRebuildScheduler).
            if (changed && this.painter != null)
            {
                if (this.layer is GrassLayer g) WorldPainterRebuildScheduler.MarkGrassDirty(g);
                if (this.layer is PropLayer  p) WorldPainterRebuildScheduler.MarkPropDirty(p);
            }
        }

        // ── LOD section ───────────────────────────────────────────────────────
        private void DrawLodSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("LOD Meshes", EditorStyles.boldLabel);

            ScatterLod[] lods = this.GetLods();
            float cull       = this.GetCullDistance();

            // ── Bar visualiser ──────────────────────────────────────────────
            this.DrawLodBar(lods, cull);

            EditorGUILayout.Space(4f);

            // ── Per-LOD mesh + max-distance fields ─────────────────────────
            for (int i = 0; i < lods.Length; ++i)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"LOD {i}", GUILayout.Width(45f));
                lods[i].mesh = (Mesh?)EditorGUILayout.ObjectField(
                    lods[i].mesh, typeof(Mesh), allowSceneObjects: false);

                if (i < lods.Length - 1)
                {
                    float d = lods[i].maxDistance;
                    EditorGUILayout.LabelField("Max", GUILayout.Width(28f));
                    d = EditorGUILayout.FloatField(d, GUILayout.Width(60f));
                    lods[i].maxDistance = Mathf.Max(0f, d);
                }
                else
                {
                    EditorGUILayout.LabelField("→ cull", GUILayout.Width(92f));
                }

                if (GUILayout.Button("✕", GUILayout.Width(22f)))
                {
                    var trimmed = new ScatterLod[lods.Length - 1];
                    for (int k = 0, w = 0; k < lods.Length; ++k)
                        if (k != i) trimmed[w++] = lods[k];
                    this.SetLods(trimmed);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add LOD", GUILayout.Width(90f)))
            {
                var grown = new ScatterLod[lods.Length + 1];
                System.Array.Copy(lods, grown, lods.Length);
                // Seed the new LOD halfway between the previous last switch and cull.
                float prev = lods.Length > 1 ? lods[lods.Length - 2].maxDistance : 0f;
                grown[lods.Length].maxDistance = Mathf.Max(prev, lods.Length > 0 ? lods[lods.Length - 1].maxDistance : cull * 0.5f);
                if (grown.Length >= 2)
                    grown[grown.Length - 2].maxDistance = Mathf.Lerp(prev, cull, 0.5f);
                this.SetLods(grown);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Cull distance (m)", GUILayout.Width(120f));
            float newCull = EditorGUILayout.FloatField(cull, GUILayout.Width(80f));
            if (!Mathf.Approximately(newCull, cull) && newCull >= 0f)
                this.SetCullDistance(newCull);
            EditorGUILayout.EndHorizontal();

            // Commit edits to mesh / maxDistance back to the SerializedObject.
            this.SetLods(lods);

            EditorGUILayout.EndVertical();
        }

        // ── LOD horizontal bar ────────────────────────────────────────────────
        private void DrawLodBar(ScatterLod[] lods, float cull)
        {
            var rect = GUILayoutUtility.GetRect(0f, BAR_HEIGHT, GUILayout.ExpandWidth(true));
            rect.x      += 4f;
            rect.width  -= 8f;

            if (cull <= 0f) { EditorGUI.DrawRect(rect, CULLED_COLOR); return; }

            float prevX = rect.x;
            for (int i = 0; i < lods.Length; ++i)
            {
                float frac = i < lods.Length - 1
                    ? Mathf.Clamp01(lods[i].maxDistance / cull)
                    : 1f;
                float bandRight = rect.x + frac * rect.width;
                var bandRect    = new Rect(prevX, rect.y, Mathf.Max(0f, bandRight - prevX), rect.height);

                var color = LOD_COLORS[i % LOD_COLORS.Length];
                EditorGUI.DrawRect(bandRect, color);

                // Band label
                float pct = i == 0
                    ? Mathf.RoundToInt(frac * 100f)
                    : Mathf.RoundToInt((frac - Mathf.Clamp01(lods[i - 1].maxDistance / cull)) * 100f);
                GUI.contentColor = Color.white;
                GUI.Label(new Rect(bandRect.x + 6f, bandRect.y + 4f, bandRect.width - 8f, 18f),
                    $"LOD {i}", EditorStyles.boldLabel);
                GUI.Label(new Rect(bandRect.x + 6f, bandRect.y + 20f, bandRect.width - 8f, 18f),
                    $"{pct}%", EditorStyles.miniLabel);

                // Drag handle on the right edge (except the last LOD — that boundary IS cull).
                if (i < lods.Length - 1)
                {
                    var handleRect = new Rect(bandRight - HANDLE_HALF, rect.y, HANDLE_HALF * 2f, rect.height);
                    EditorGUI.DrawRect(handleRect, HANDLE_COLOR);
                    EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

                    Event e = Event.current;
                    if (e.type == EventType.MouseDown && handleRect.Contains(e.mousePosition))
                    {
                        this.draggingHandle = i;
                        e.Use();
                    }
                    if (this.draggingHandle == i && e.type == EventType.MouseDrag)
                    {
                        float xLocal     = Mathf.Clamp(e.mousePosition.x - rect.x, 0f, rect.width);
                        float newFrac    = xLocal / rect.width;
                        float newDist    = newFrac * cull;
                        float minDist    = i > 0 ? lods[i - 1].maxDistance + 0.01f : 0f;
                        float maxDist    = i < lods.Length - 2
                            ? lods[i + 1].maxDistance - 0.01f
                            : cull;
                        lods[i].maxDistance = Mathf.Clamp(newDist, minDist, maxDist);
                        this.SetLods(lods);
                        e.Use();
                        this.Repaint();
                    }
                    if (e.type == EventType.MouseUp && this.draggingHandle == i)
                    {
                        this.draggingHandle = -1;
                        e.Use();
                    }
                }

                prevX = bandRight;
            }

            // Culled band
            if (prevX < rect.xMax)
            {
                var culledRect = new Rect(prevX, rect.y, rect.xMax - prevX, rect.height);
                EditorGUI.DrawRect(culledRect, CULLED_COLOR);
                GUI.Label(new Rect(culledRect.x + 6f, culledRect.y + 4f, culledRect.width - 8f, 18f),
                    "Culled", EditorStyles.boldLabel);
            }
        }

        // ── Anchor section (Prop only) ────────────────────────────────────────
        private void DrawAnchorSection(PropLayer prop)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Anchor", EditorStyles.boldLabel);

            var so = new SerializedObject(prop);
            so.Update();
            var anchorOffset = so.FindProperty("anchorOffsetLocal");
            if (anchorOffset != null) EditorGUILayout.PropertyField(anchorOffset);
            so.ApplyModifiedProperties();

            // Auto-anchor helper. The placement formula is `pivot = cursor − rot·(anchor·scale)`
            // (commit 35e2def). For the mesh BASE to land on the cursor, the anchor in local
            // space must equal the mesh's bounds.min — that's the LOWEST local-Y point on the
            // mesh. Then `cursor − rot·(bounds.min·scale)` lifts the pivot so the lowest vertex
            // lands exactly on the cursor.
            EditorGUILayout.Space(2f);
            using (new EditorGUI.DisabledScope(GetLod0(prop) == null))
            {
                if (GUILayout.Button("Auto-anchor: mesh base on cursor", GUILayout.Height(22f)))
                {
                    var lod0 = GetLod0(prop);
                    if (lod0 != null)
                    {
                        Vector3 anchor = new Vector3(0f, lod0.bounds.min.y, 0f);
                        prop.EditorSetAnchorOffset(anchor);
                        EditorUtility.SetDirty(prop);
                        WorldPainterRebuildScheduler.MarkPropDirty(prop);
                    }
                }
            }

            EditorGUILayout.Space(4f);
            this.DrawAnchorPreview(prop);
            EditorGUILayout.EndVertical();
        }

        private static Mesh? GetLod0(PropLayer prop)
        {
            var lods = prop.Render.Lods;
            return (lods != null && lods.Length > 0) ? lods[0].mesh : null;
        }

        private void DrawAnchorPreview(PropLayer prop)
        {
            Mesh? mesh = null;
            var lods = prop.Render.Lods;
            if (lods != null && lods.Length > 0) mesh = lods[0].mesh;

            // No LOD0 mesh → no meaningful anchor preview. Surface a hint instead of an empty
            // viewport so the user knows to assign a mesh in the LOD list above.
            if (mesh == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a LOD 0 mesh above to preview the anchor.",
                    MessageType.Info);
                return;
            }

            var rect = GUILayoutUtility.GetRect(PREVIEW_WIDTH, PREVIEW_HEIGHT, GUILayout.ExpandWidth(false));

            Event evt = Event.current;
            if (evt.type == EventType.MouseDrag && rect.Contains(evt.mousePosition))
            {
                this.previewDrag += evt.delta * 0.5f;
                evt.Use(); this.Repaint();
            }
            if (evt.type == EventType.ScrollWheel && rect.Contains(evt.mousePosition))
            {
                this.previewDistance = Mathf.Clamp(this.previewDistance + evt.delta.y * 0.1f, 0.5f, 20f);
                evt.Use(); this.Repaint();
            }
            if (evt.type != EventType.Repaint) return;

            this.previewUtil ??= new PreviewRenderUtility();
            this.previewUtil.BeginPreview(rect, GUIStyle.none);

            var cam = this.previewUtil.camera;
            cam.fieldOfView        = PREVIEW_FOV;
            cam.nearClipPlane      = 0.01f;
            cam.farClipPlane       = 100f;
            cam.backgroundColor    = new Color(0.15f, 0.15f, 0.15f, 1f);
            cam.clearFlags         = CameraClearFlags.SolidColor;

            var rotation = Quaternion.Euler(this.previewDrag.y, this.previewDrag.x, 0f);
            cam.transform.position = rotation * new Vector3(0f, 0f, -this.previewDistance);
            cam.transform.rotation = rotation;

            if (mesh != null && prop.Render.Material != null)
                this.previewUtil.DrawMesh(mesh, Matrix4x4.identity, prop.Render.Material, 0);

            Vector3 marker = prop.AnchorOffsetLocal;
            this.DrawAxisLine(marker, marker + new Vector3(AXIS_LENGTH, 0f, 0f), Color.red);
            this.DrawAxisLine(marker, marker + new Vector3(0f, AXIS_LENGTH, 0f), Color.green);
            this.DrawAxisLine(marker, marker + new Vector3(0f, 0f, AXIS_LENGTH), Color.blue);

            this.previewUtil.camera.Render();
            var tex = this.previewUtil.EndPreview();
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);

            EditorGUI.LabelField(
                new Rect(rect.x, rect.yMax - 16f, rect.width, 16f),
                $"Anchor: {marker:F2}",
                EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawAxisLine(Vector3 from, Vector3 to, Color color)
        {
            if (this.previewUtil == null) return;
            lineAxisMesh ??= new Mesh { hideFlags = HideFlags.HideAndDontSave };
            lineAxisMesh.SetVertices(new[] { from, to });
            lineAxisMesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);

            Material? mat = color == Color.red   ? (this.lineMatRed   ??= CreateLineMaterial(color))
                          : color == Color.green ? (this.lineMatGreen ??= CreateLineMaterial(color))
                          :                        (this.lineMatBlue  ??= CreateLineMaterial(color));
            if (mat != null)
                this.previewUtil.DrawMesh(lineAxisMesh, Matrix4x4.identity, mat, 0);
        }

        private static Material? CreateLineMaterial(Color color)
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return null;
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_Cull",     (int)CullMode.Off);
            mat.SetInt("_ZWrite",   0);
            mat.color = color;
            return mat;
        }

        // ── Accessors: route through editor-only setters so the SerializedObject stays dirty ──
        private ScatterLod[] GetLods()
        {
            if (this.layer is GrassLayer g) return Clone(g.Render.Lods);
            if (this.layer is PropLayer  p) return Clone(p.Render.Lods);
            return System.Array.Empty<ScatterLod>();
        }

        private void SetLods(ScatterLod[] lods)
        {
            if (this.layer is GrassLayer g) { g.EditorSetLods(lods); EditorUtility.SetDirty(g); }
            if (this.layer is PropLayer  p) { p.EditorSetLods(lods); EditorUtility.SetDirty(p); }
        }

        private float GetCullDistance()
        {
            if (this.layer is GrassLayer g) return g.Render.RenderCullDistance;
            if (this.layer is PropLayer  p) return p.Render.RenderCullDistance;
            return 500f;
        }

        private void SetCullDistance(float cull)
        {
            // Cull distance lives inside ScatterRenderConfig — rebuild the struct via an editor setter
            // that preserves material/shadow/lods.
            if (this.layer is GrassLayer g)
            {
                var cur = g.Render;
                var rebuilt = new ScatterRenderConfig(cur.Material, cur.ShadowCastingMode, cur.Lods, cull);
                g.EditorSetRender(rebuilt);
                EditorUtility.SetDirty(g);
            }
            else if (this.layer is PropLayer p)
            {
                var cur = p.Render;
                var rebuilt = new ScatterRenderConfig(cur.Material, cur.ShadowCastingMode, cur.Lods, cull);
                p.EditorSetRender(rebuilt);
                EditorUtility.SetDirty(p);
            }
        }

        private static ScatterLod[] Clone(ScatterLod[] src)
        {
            if (src == null || src.Length == 0) return System.Array.Empty<ScatterLod>();
            var copy = new ScatterLod[src.Length];
            System.Array.Copy(src, copy, src.Length);
            return copy;
        }
    }
}
