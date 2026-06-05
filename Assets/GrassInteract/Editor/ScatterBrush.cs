#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    // ── Brush edit operation type ──────────────────────────────────────────────

    /// <summary>
    /// Operations available in EditBrush mode. Each op modifies authored instance records
    /// within the brush radius, weighted by falloff and opacity.
    /// </summary>
    internal enum BrushEditOp
    {
        /// <summary>Randomises the yaw rotation of instances inside the brush radius.</summary>
        RandomizeRotation,

        /// <summary>Nudges the uniform scale of instances by a random amount.</summary>
        NudgeScale,

        /// <summary>Nudges XZ position of instances by a random offset; Y is re-snapped to surface.</summary>
        NudgePosition,

        /// <summary>Flips the align-to-normal bit per instance; re-samples normal when newly aligned.</summary>
        ToggleAlignNormal,
    }

    /// <summary>
    /// Per-op parameters for <see cref="ScatterBrush.EditBrushStamp"/>.
    /// Only the field relevant to the active <see cref="BrushEditOp"/> is consumed.
    /// </summary>
    internal struct BrushEditOpParams
    {
        /// <summary>Used by <see cref="BrushEditOp.NudgeScale"/>: max fractional change per stamp.</summary>
        public float scaleDelta;

        /// <summary>Used by <see cref="BrushEditOp.NudgePosition"/>: max XZ displacement in metres.</summary>
        public float nudgeRadius;
    }
    // ── Static cursor-preview cache ────────────────────────────────────────────
    // Stored as static so it survives partial editor reloads without re-allocating.
    // The falloff texture is small (128×128 R8) and can be leaked safely if the
    // editor is closed — no explicit disposal is required, but OnDisable in the
    // host editor can call ClearCursorCache() for cleanliness.

    /// <summary>
    /// Reusable brush core for painting scatter density. Contains all stamp/flush/save/load/clear
    /// logic. Stateless with respect to the host editor — the host owns a single instance and
    /// calls into this.
    ///
    /// Field-origin resolution is SSOT with the runtime scatter:
    ///   - boundTerrain bound → terrain center + <see cref="TerrainSurfaceSampler.TerrainSizeXZ"/>
    ///   - No terrain → field transform.position + active layer <see cref="ScatterLayer.FieldBounds"/>
    ///
    /// As of Phase 3, the brush is layer-targeted: call
    /// <see cref="SetActiveLayer(ScatterField, ScatterLayer, int)"/> to bind a field + layer + index.
    /// The throttled-flush callback then calls <see cref="ScatterField.RebuildLayer"/> on that index
    /// instead of the full <see cref="ScatterField.Rebuild"/>.
    /// </summary>
    internal sealed class ScatterBrush
    {
        // ── Authored-instance stamp constants ─────────────────────────────────
        /// <summary>
        /// Hard cap on the number of authored instances generated per brush stamp. Prevents
        /// OOM when PlaceSpacing is very small or the brush radius is very large.
        /// </summary>
        internal const int MAX_INSTANCES_PER_STAMP = 10000;

        // ── Procedural-falloff texture cache (static — one per editor session) ─
        // Rebuilt when falloff parameter changes by > 0.01. Leaked intentionally:
        // it is 128×128 R8 (~16 KB) and any GC/domain reload cleans it up anyway.

        private static Texture2D? s_FalloffTex;
        private static float s_CachedFalloff = -1f;
        private static Material? s_CursorMat;
        private static Mesh? s_CursorQuad;

        // ── CPU buffer ────────────────────────────────────────────────────────

        private Texture2D? densityMap;
        private float[]? buffer;
        private int texWidth;
        private int texHeight;
        private bool bufferDirty;

        // ── Layer-targeted rebuild state (Phase 3) ────────────────────────────

        private ScatterField? targetField;
        private int targetLayerIdx = -1;

        // ── Throttled-flush state ─────────────────────────────────────────────

        private double lastApplyTime;
        private const double APPLY_INTERVAL = 0.05;

        // ── Public state (read by the host inspector / editor) ────────────────

        /// <summary>True when a density map is loaded and is readable.</summary>
        internal bool IsReady => this.densityMap != null && this.densityMap.isReadable && this.buffer != null;

        /// <summary>The currently loaded density map (in-memory, may differ from disk until Save).</summary>
        internal Texture2D? DensityMap => this.densityMap;

        // ── SetActiveLayer (Phase 3) ──────────────────────────────────────────

        /// <summary>
        /// Binds this brush to a specific field + layer + index so that throttled flushes
        /// can call <see cref="ScatterField.RebuildLayer"/> on just the painted slot.
        ///
        /// Call FlushBufferToTexture BEFORE switching to commit any pending paint.
        /// </summary>
        internal void SetActiveLayer(ScatterField field, ScatterLayer layer, int idx)
        {
            this.targetField = field;
            this.targetLayerIdx = idx;
            this.Load(layer);
        }

        // ── Load ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads the CPU buffer from <paramref name="layer"/>'s DensityMap. Safe to call repeatedly;
        /// any unsaved changes are discarded (call <see cref="Save"/> first if needed).
        /// </summary>
        internal void Load(ScatterLayer? layer)
        {
            this.densityMap = layer != null ? layer.DensityMap : null;
            this.buffer = null;
            this.bufferDirty = false;

            if (this.densityMap == null || !this.densityMap.isReadable)
                return;

            this.texWidth = this.densityMap.width;
            this.texHeight = this.densityMap.height;
            Color[] pixels = this.densityMap.GetPixels();
            this.buffer = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; ++i)
                this.buffer[i] = pixels[i].r;
        }

        // ── Stamp ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Stamps a kernel centred at <paramref name="worldHit"/> into the CPU buffer.
        /// When <paramref name="stamp"/> is non-null the stamp's grayscale shape is sampled;
        /// the procedural falloff weight is MULTIPLIED on top so the Falloff slider still applies.
        ///
        /// Perf note: when a stamp is active, <c>stamp.Shape.GetPixels()</c> is cached into a flat
        /// Color[] array before the loop (rather than calling GetPixelBilinear per pixel). For a
        /// 256×256 stamp this saves ~65 000 managed-to-native round-trips per brush stroke tick.
        /// </summary>
        /// <param name="stamp">Optional brush stamp (null = procedural circle).</param>
        internal void Stamp(
            Vector3 worldHit,
            ScatterField field,
            ScatterLayer layer,
            bool paint,
            float brushRadius,
            float brushStrength,
            float brushFalloff,
            BrushStamp? stamp = null)
        {
            if (this.buffer == null)
                return;

            Vector3 origin = ResolveFieldOrigin(field);
            Vector2 effectiveBounds = ResolveEffectiveBounds(field, layer);
            var space = new GrassFieldSpace(origin, effectiveBounds);
            Vector2 uv = space.WorldToUv(worldHit);

            float cx = uv.x * this.texWidth;
            float cy = uv.y * this.texHeight;
            float prx = Mathf.Max(brushRadius / Mathf.Max(effectiveBounds.x, 1e-4f) * this.texWidth, 1f);
            float pry = Mathf.Max(brushRadius / Mathf.Max(effectiveBounds.y, 1e-4f) * this.texHeight, 1f);

            int minX = Mathf.Clamp(Mathf.FloorToInt(cx - prx), 0, this.texWidth - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(cx + prx), 0, this.texWidth - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(cy - pry), 0, this.texHeight - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(cy + pry), 0, this.texHeight - 1);

            float sign = paint ? 1f : -1f;

            // Stamp prepass — cache stamp pixels once per Stamp() call.
            // GetPixels() on R8 sub-assets returns Color where r=g=b=value, a=1; sample .r.
            Texture2D? shape = stamp?.Shape;
            Color[]? stampPixels = null;
            int stampW = 0, stampH = 0;
            if (shape != null)
            {
                stampPixels = shape.GetPixels();
                stampW = shape.width;
                stampH = shape.height;
            }

            for (int y = minY; y <= maxY; ++y)
            {
                for (int x = minX; x <= maxX; ++x)
                {
                    float ndx = (x - cx) / prx;
                    float ndy = (y - cy) / pry;
                    float nd = Mathf.Sqrt(ndx * ndx + ndy * ndy);
                    if (nd > 1f)
                        continue;

                    // Procedural falloff — used alone (no stamp) or multiplied on top of stamp.
                    float falloffWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, brushFalloff, nd));

                    float weight;
                    if (stampPixels != null)
                    {
                        // Map (ndx, ndy) ∈ [-1,1]² → stamp UV [0,1]² → integer index.
                        float u = (ndx + 1f) * 0.5f;
                        float v = (ndy + 1f) * 0.5f;
                        int si = Mathf.Clamp((int)(u * stampW), 0, stampW - 1);
                        int sj = Mathf.Clamp((int)(v * stampH), 0, stampH - 1);
                        float stampVal = stampPixels[sj * stampW + si].r;
                        weight = stampVal * falloffWeight;
                    }
                    else
                    {
                        weight = falloffWeight;
                    }

                    int pixIdx = y * this.texWidth + x;
                    this.buffer[pixIdx] = Mathf.Clamp01(this.buffer[pixIdx] + sign * brushStrength * weight);
                }
            }

            this.bufferDirty = true;
        }

        // ── Buffer → Texture flush ────────────────────────────────────────────

        /// <summary>Unconditionally flushes the CPU buffer to the Texture2D (in-memory only).</summary>
        internal void FlushBufferToTexture()
        {
            if (this.buffer == null || this.densityMap == null || !this.bufferDirty || !this.densityMap.isReadable)
                return;

            var colors = new Color[this.buffer.Length];
            for (int i = 0; i < this.buffer.Length; ++i)
            {
                float v = this.buffer[i];
                colors[i] = new Color(v, v, v, 1f);
            }
            this.densityMap.SetPixels(colors);
            this.densityMap.Apply(false);
            this.bufferDirty = false;
        }

        /// <summary>
        /// Throttled flush — only writes to the texture when at least <c>APPLY_INTERVAL</c> seconds
        /// have elapsed since the last flush. Call during mouse-drag.
        ///
        /// The density overlay (see <see cref="DrawOverlay"/>) reads the flushed texture directly,
        /// giving real-time visual feedback. Engine rebuild is deferred to <see cref="Save"/> on
        /// mouse-up to avoid expensive per-tick instance regeneration.
        /// </summary>
        internal void ThrottledFlush()
        {
            if (EditorApplication.timeSinceStartup - this.lastApplyTime < APPLY_INTERVAL)
                return;
            this.FlushBufferToTexture();
            this.lastApplyTime = EditorApplication.timeSinceStartup;

            // Rebuild is deferred to Save() on mouse-up; real-time feedback comes from the
            // density overlay (brush.DrawOverlay) which reads the flushed texture directly.
        }

        // ── Save ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Flushes the CPU buffer, marks the density map and layer dirty, and saves all modified assets.
        /// Also calls <see cref="ScatterField.RebuildLayer"/> on the active layer index.
        /// </summary>
        internal void Save(ScatterLayer? layer)
        {
            if (this.densityMap == null)
                return;
            this.FlushBufferToTexture();
            EditorUtility.SetDirty(this.densityMap);
            if (layer != null)
                EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssets();

            if (this.targetField != null && this.targetLayerIdx >= 0)
                this.targetField.RebuildLayer(this.targetLayerIdx);

            Debug.Log($"[ScatterBrush] Saved density map '{this.densityMap.name}'.");
        }

        // ── Clear ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills the CPU buffer with zeros and flushes to the texture.
        /// Also calls <see cref="ScatterField.RebuildLayer"/> on the active layer index.
        /// </summary>
        internal void Clear()
        {
            if (this.buffer == null)
                return;
            for (int i = 0; i < this.buffer.Length; ++i)
                this.buffer[i] = 0f;
            this.bufferDirty = true;
            this.FlushBufferToTexture();

            if (this.targetField != null && this.targetLayerIdx >= 0)
                this.targetField.RebuildLayer(this.targetLayerIdx);
        }

        // ── Import settings helper ────────────────────────────────────────────

        /// <summary>
        /// Fixes the density map's import settings: readable, uncompressed, no mipmaps, linear.
        /// Reimports the asset and reloads the CPU buffer from the given <paramref name="layer"/>.
        /// </summary>
        internal void FixImportSettings(ScatterLayer? layer)
        {
            if (this.densityMap == null)
                return;
            string path = AssetDatabase.GetAssetPath(this.densityMap);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.Log("[ScatterBrush] Density map is a native asset (already readable).");
                return;
            }
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
            this.Load(layer);
        }

        // ── Field-origin resolution (SSOT with runtime scatter) ───────────────

        /// <summary>
        /// Resolves the field origin using the same logic as <see cref="ScatterField.Rebuild"/>:
        ///   - boundTerrain set → terrain position + half terrain XZ size (terrain center)
        ///   - No terrain → ScatterField transform.position
        ///
        /// The boundTerrain is read via SerializedObject because it is a private serialized field
        /// on <see cref="ScatterField"/> (no public accessor). This is editor-only code.
        /// </summary>
        internal static Vector3 ResolveFieldOrigin(ScatterField field)
        {
            using var so = new SerializedObject(field);
            SerializedProperty terrainProp = so.FindProperty("boundTerrain");
            if (terrainProp != null && terrainProp.objectReferenceValue is Terrain terrain
                && terrain.terrainData != null)
            {
                Vector3 size = terrain.terrainData.size;
                Vector3 pos = terrain.transform.position;
                return pos + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            }
            return field.transform.position;
        }

        /// <summary>
        /// Resolves the effective field XZ bounds using the same logic as the runtime:
        ///   - boundTerrain set → TerrainSizeXZ (covers the entire terrain tile)
        ///   - No terrain → layer.FieldBounds
        /// </summary>
        internal static Vector2 ResolveEffectiveBounds(ScatterField field, ScatterLayer layer)
        {
            using var so = new SerializedObject(field);
            SerializedProperty terrainProp = so.FindProperty("boundTerrain");
            if (terrainProp != null && terrainProp.objectReferenceValue is Terrain terrain
                && terrain.terrainData != null)
            {
                Vector3 size = terrain.terrainData.size;
                return new Vector2(size.x, size.z);
            }
            return layer.FieldBounds;
        }

        /// <summary>
        /// Returns terrain splat-layer names from the bound terrain of <paramref name="field"/>,
        /// or an empty array when no terrain is bound.
        /// </summary>
        internal static string[] GetSplatLayerNames(ScatterField field)
        {
            using var so = new SerializedObject(field);
            SerializedProperty terrainProp = so.FindProperty("boundTerrain");
            if (terrainProp == null || terrainProp.objectReferenceValue == null)
                return System.Array.Empty<string>();

            var terrain = terrainProp.objectReferenceValue as Terrain;
            if (terrain == null || terrain.terrainData == null)
                return System.Array.Empty<string>();

            TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
            var names = new string[terrainLayers.Length];
            for (int i = 0; i < terrainLayers.Length; ++i)
                names[i] = terrainLayers[i] != null ? $"[{i}] {terrainLayers[i].name}" : $"[{i}] (unnamed)";
            return names;
        }

        // ── Scene-view textured cursor ─────────────────────────────────────────

        /// <summary>
        /// Draws a WYSIWYG stamp cursor centred at <paramref name="hitPoint"/> and oriented to
        /// <paramref name="hitNormal"/> — the textured quad lies FLAT on the surface (not screen-space
        /// billboarded) so the preview tracks slopes correctly. Alpha-blended via Sprites/Default so
        /// the underlying mesh shows through transparent regions of <paramref name="preview"/>.
        ///
        /// <paramref name="tint"/> RGB tints the texture (paint=green, erase=red); <paramref name="tint"/>.a
        /// is IGNORED — overlay alpha is derived from <paramref name="strength"/> so the brush opacity
        /// slider drives visible transparency.
        ///
        /// Must be called inside OnSceneGUI. Cached mesh + material are reused across frames.
        /// </summary>
        internal static void DrawTexturedCursor(
            SceneView sceneView,
            Vector3 hitPoint,
            Vector3 hitNormal,
            Color tint,
            Texture preview,
            float radius,
            float strength)
        {
            Vector3 up = hitNormal.sqrMagnitude > 1e-6f ? hitNormal.normalized : Vector3.up;

            // Always draw wire-disc edges for crisp radius feedback (cheap, never fails).
            Handles.color = new Color(tint.r, tint.g, tint.b, 1f);
            Handles.DrawWireDisc(hitPoint, up, radius);
            Handles.DrawWireDisc(hitPoint, up, radius * 0.5f);

            if (preview == null || radius <= 0f) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            try
            {
                // Cache an XZ unit quad — vertices at (±0.5, 0, ±0.5), Y+ is the quad's local up axis.
                if (s_CursorQuad == null)
                {
                    s_CursorQuad = new Mesh { name = "_BrushCursorQuad", hideFlags = HideFlags.HideAndDontSave };
                    s_CursorQuad.SetVertices(new[] {
                        new Vector3(-0.5f, 0f, -0.5f),
                        new Vector3( 0.5f, 0f, -0.5f),
                        new Vector3( 0.5f, 0f,  0.5f),
                        new Vector3(-0.5f, 0f,  0.5f),
                    });
                    s_CursorQuad.SetUVs(0, new[] {
                        new Vector2(0f, 0f),
                        new Vector2(1f, 0f),
                        new Vector2(1f, 1f),
                        new Vector2(0f, 1f),
                    });
                    s_CursorQuad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                    s_CursorQuad.RecalculateBounds();
                }

                // Editor-only overlay shader: ZTest Always (overlays everything) + luminance-as-alpha
                // (treats black PNG pixels as transparent so B&W stamps act as a mask).
                if (s_CursorMat == null)
                {
                    Shader sh = Shader.Find("Hidden/GrassInteract/BrushCursor");
                    if (sh == null) return; // fall back to wire-disc only
                    s_CursorMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                }

                // Compose the final overlay color: RGB from tint, A from strength (clamped to a visible floor
                // so a tiny opacity slider value still shows the stamp shape).
                Color overlay = new(tint.r, tint.g, tint.b, Mathf.Clamp(strength, 0.15f, 1f));

                s_CursorMat.mainTexture = preview;
                s_CursorMat.color       = overlay;

                // World-space TRS: tiny +normal offset to avoid z-fight, rotation that maps local Y+ → hitNormal.
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, up);
                Vector3    pos = hitPoint + up * 0.01f;
                Vector3    scl = new(radius * 2f, 1f, radius * 2f);
                Matrix4x4  trs = Matrix4x4.TRS(pos, rot, scl);

                if (s_CursorMat.SetPass(0))
                    Graphics.DrawMeshNow(s_CursorQuad, trs);
            }
            catch (System.Exception)
            {
                // Wire-disc already drawn above; swallow render-path errors silently.
            }
        }

        // ── Procedural falloff texture cache ──────────────────────────────────

        /// <summary>
        /// Returns a 128×128 R8 radial gradient texture baking the current <paramref name="falloff"/>
        /// value. Regenerated only when <paramref name="falloff"/> changes by more than 0.01 since
        /// the last call, limiting to one regeneration per editor frame delta.
        ///
        /// The cached texture is stored as a static field and leaked intentionally (≤16 KB).
        /// Call <see cref="ClearCursorCache"/> from OnDisable if a clean teardown is preferred.
        /// </summary>
        internal static Texture2D GetProceduralFalloffTexture(float falloff)
        {
            const int SIZE = 128;
            if (s_FalloffTex != null && Mathf.Abs(falloff - s_CachedFalloff) <= 0.01f)
                return s_FalloffTex;

            // Rebuild.
            if (s_FalloffTex == null)
            {
                s_FalloffTex = new Texture2D(
                    SIZE, SIZE,
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                    UnityEngine.Experimental.Rendering.TextureCreationFlags.None)
                {
                    name = "_ProceduralFalloff",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            s_CachedFalloff = falloff;
            var pixels = new Color[SIZE * SIZE];
            float half = SIZE * 0.5f;
            for (int y = 0; y < SIZE; ++y)
            {
                for (int x = 0; x < SIZE; ++x)
                {
                    float ndx = (x - half) / half;
                    float ndy = (y - half) / half;
                    float nd = Mathf.Sqrt(ndx * ndx + ndy * ndy);
                    float v = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, falloff, nd));
                    pixels[y * SIZE + x] = new Color(v, v, v, v);
                }
            }
            s_FalloffTex.SetPixels(pixels);
            s_FalloffTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return s_FalloffTex;
        }

        /// <summary>
        /// Releases the cached falloff texture and cursor material. Call from host editor's OnDisable.
        /// </summary>
        internal static void ClearCursorCache()
        {
            if (s_FalloffTex != null)
            {
                Object.DestroyImmediate(s_FalloffTex);
                s_FalloffTex = null;
                s_CachedFalloff = -1f;
            }
            if (s_CursorMat != null)
            {
                Object.DestroyImmediate(s_CursorMat);
                s_CursorMat = null;
            }
            if (s_CursorQuad != null)
            {
                Object.DestroyImmediate(s_CursorQuad);
                s_CursorQuad = null;
            }
        }

        // ── Authored-instance Place stroke ────────────────────────────────────

        /// <summary>
        /// Place stroke for authored-instance layers. Generates Poisson-disk candidates within
        /// <paramref name="brushRadius"/> centred at <paramref name="worldHit"/>, then appends
        /// each accepted candidate as a new <see cref="InstanceRecord"/> to
        /// <paramref name="sidecar"/>.
        ///
        /// Dual-write: also paints the density mask texel at each accepted position so the
        /// density overlay stays in visual parity with the authored list.
        ///
        /// Call <see cref="Undo.RegisterCompleteObjectUndo"/> on the sidecar BEFORE calling this
        /// (done by the host inspector on MouseDown).
        ///
        /// Hard-capped at <see cref="MAX_INSTANCES_PER_STAMP"/> per stamp; excess candidates are
        /// dropped with a warning.
        /// </summary>
        internal void StampAuthored(
            Vector3 worldHit,
            ScatterField field,
            ScatterLayer layer,
            AuthoredInstancesData sidecar,
            float brushRadius,
            float brushStrength)
        {
            if (layer.PlaceSpacing <= 0f || brushRadius <= 0f)
                return;

            // Sample density mask — 0 = placement blocked, >0 = allowed (role-flip when authored).
            Texture2D? densityTex = layer.DensityMap;
            Vector3 origin = ResolveFieldOrigin(field);
            Vector2 effectiveBounds = ResolveEffectiveBounds(field, layer);
            var space = new GrassFieldSpace(origin, effectiveBounds);

            // Poisson-disk via simple random throw + min-distance rejection.
            // We generate up to MAX_INSTANCES_PER_STAMP candidates inside the brush disc,
            // spaced at least PlaceSpacing apart. For P1 we use a rejection-sample approach
            // rather than a proper Bridson algorithm — fast enough for typical brush sizes.
            float spacing = layer.PlaceSpacing;
            float r2 = brushRadius * brushRadius;
            float spacingSq = spacing * spacing;

            // Collect positions already in-sidecar that are inside the brush radius for spacing check.
            var nearbyPositions = new System.Collections.Generic.List<Vector3>(32);
            var workList = sidecar.WorkingList;
            foreach (var existing in workList)
            {
                float dx = existing.position.x - worldHit.x;
                float dz = existing.position.z - worldHit.z;
                if (dx * dx + dz * dz <= r2 * 4f) // 2× radius neighbourhood for spacing
                    nearbyPositions.Add(existing.position);
            }

            // New positions placed this stamp.
            var newPositions = new System.Collections.Generic.List<Vector3>(64);

            // Max attempts scales with disc area / cell area, capped generously.
            float discArea = Mathf.PI * r2;
            float cellArea = spacing * spacing;
            int maxCandidates = Mathf.Clamp((int)(discArea / cellArea) * 4, 8, MAX_INSTANCES_PER_STAMP);

            int placed = 0;
            int attempt = 0;
            // Deterministic seed based on world position for stable results on re-stroke.
            var rng = new System.Random(Mathf.FloorToInt(worldHit.x * 73856093f) ^ Mathf.FloorToInt(worldHit.z * 19349663f));

            while (attempt < maxCandidates && placed < MAX_INSTANCES_PER_STAMP)
            {
                attempt++;
                // Random point in disc (rejection-sample disc from square).
                float rx = (float)(rng.NextDouble() * 2.0 - 1.0) * brushRadius;
                float rz = (float)(rng.NextDouble() * 2.0 - 1.0) * brushRadius;
                if (rx * rx + rz * rz > r2) continue;

                Vector3 candidate = new Vector3(worldHit.x + rx, worldHit.y, worldHit.z + rz);

                // Check density mask — placement allowed when mask > 0.
                if (densityTex != null && densityTex.isReadable)
                {
                    Vector2 uv = space.WorldToUv(candidate);
                    float density = densityTex.GetPixelBilinear(uv.x, uv.y).r;
                    if (density < 0.01f) continue;
                }

                // Check minimum spacing against existing nearby + already placed this stamp.
                bool tooClose = false;
                foreach (var np in nearbyPositions)
                {
                    float ddx = np.x - candidate.x;
                    float ddz = np.z - candidate.z;
                    if (ddx * ddx + ddz * ddz < spacingSq) { tooClose = true; break; }
                }
                if (!tooClose)
                {
                    foreach (var np in newPositions)
                    {
                        float ddx = np.x - candidate.x;
                        float ddz = np.z - candidate.z;
                        if (ddx * ddx + ddz * ddz < spacingSq) { tooClose = true; break; }
                    }
                }
                if (tooClose) continue;

                // Ground-snap: raycast down from a height offset to find terrain/mesh surface.
                float snapY = worldHit.y;
                const float SNAP_HEIGHT = 100f;
                if (Physics.Raycast(
                    new Vector3(candidate.x, worldHit.y + SNAP_HEIGHT, candidate.z),
                    Vector3.down, out RaycastHit snapHit, SNAP_HEIGHT * 2f,
                    layer.GroundSnapMask, QueryTriggerInteraction.Ignore))
                {
                    snapY = snapHit.point.y;
                }
                candidate.y = snapY;

                // Random yaw in [0, 360), scale within layer's ScaleRange.
                float yaw = (float)(rng.NextDouble() * 360.0);
                float scaleMin = layer.ScaleRange.x;
                float scaleMax = layer.ScaleRange.y;
                float scale = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());

                // V2: scale is float (uniform).
                var record = new InstanceRecord
                {
                    position     = candidate,
                    rotation     = Quaternion.Euler(0f, yaw, 0f),
                    scale        = scale,
                    overrideMask = InstanceOverrideMask.None,
                };

                sidecar.AddRecord(record);
                newPositions.Add(candidate);
                nearbyPositions.Add(candidate); // prevent new placements from clumping with this one
                placed++;

                // Transitional dual-write: paint density mask texel at this world position.
                this.PaintDensityTexelAtWorld(candidate, field, layer, 1f);
            }

            if (placed >= MAX_INSTANCES_PER_STAMP)
                Debug.LogWarning(
                    $"[ScatterBrush] Stamp hit MAX_INSTANCES_PER_STAMP ({MAX_INSTANCES_PER_STAMP}). " +
                    "Increase PlaceSpacing or reduce brush radius.");

            if (placed > 0)
                this.bufferDirty = true;
        }

        // ── Authored-instance Erase stroke ────────────────────────────────────

        /// <summary>
        /// Erase stroke for authored-instance layers. Queries the <paramref name="pickingService"/>
        /// spatial hash for instance indices inside <paramref name="brushRadius"/>, removes them
        /// via swap-pop, and clears the density mask texel at each removed position.
        ///
        /// Call <see cref="Undo.RegisterCompleteObjectUndo"/> on the sidecar BEFORE calling this.
        /// </summary>
        internal void EraseAuthored(
            Vector3 worldHit,
            ScatterField field,
            ScatterLayer layer,
            AuthoredInstancesData sidecar,
            InstancePickingService pickingService,
            float brushRadius)
        {
            // Collect indices first (QueryRadius is lazy — do NOT modify the list mid-enumeration).
            var toRemove = new System.Collections.Generic.List<int>(
                pickingService.QueryRadius(worldHit, brushRadius));

            if (toRemove.Count == 0) return;

            // Sort descending so swap-pop indices stay valid.
            toRemove.Sort((a, b) => b.CompareTo(a));

            // Save positions before removal for density clear.
            var workList = sidecar.WorkingList;
            var removedPositions = new System.Collections.Generic.List<Vector3>(toRemove.Count);
            foreach (int idx in toRemove)
            {
                if (idx < workList.Count)
                    removedPositions.Add(workList[idx].position);
            }

            // Remove (descending order to preserve index validity).
            foreach (int idx in toRemove)
                sidecar.RemoveRecordSwapPop(idx);

            // Transitional dual-write: clear density mask texels at removed positions.
            foreach (var pos in removedPositions)
                this.PaintDensityTexelAtWorld(pos, field, layer, -1f);

            this.bufferDirty = true;
        }

        // ── Edit-brush stamp ──────────────────────────────────────────────────

        /// <summary>
        /// Edit-brush stamp: queries the <paramref name="pickingService"/> for all instances within
        /// <paramref name="radius"/> of <paramref name="cursor"/>, computes a falloff weight for each,
        /// and applies the selected <paramref name="op"/> using <paramref name="opParams"/>.
        ///
        /// Undo is registered by the <em>caller</em> on mouse-down (RegisterCompleteObjectUndo on
        /// <paramref name="sidecar"/>). This method accumulates edits in a local list and flushes
        /// them to <see cref="AuthoredInstancesData.SetRecords"/> in a single call for efficiency.
        /// </summary>
        internal void EditBrushStamp(
            Vector3 cursor,
            ScatterField field,
            ScatterLayer layer,
            AuthoredInstancesData sidecar,
            InstancePickingService pickingService,
            float radius,
            float opacity,
            BrushEditOp op,
            BrushEditOpParams opParams)
        {
            if (radius <= 0f || opacity <= 0f) return;

            // Collect candidate indices via spatial hash (XZ radius query).
            var candidates = new List<int>(pickingService.QueryRadius(cursor, radius));
            if (candidates.Count == 0) return;

            var workList = sidecar.WorkingList;
            var edits = new List<(int idx, InstanceRecord rec)>(candidates.Count);

            foreach (int idx in candidates)
            {
                if (idx < 0 || idx >= workList.Count) continue;
                InstanceRecord rec = workList[idx];

                // Compute normalised distance and falloff weight.
                float dx = rec.position.x - cursor.x;
                float dz = rec.position.z - cursor.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float nd = Mathf.Clamp01(dist / radius);
                float w = SampleFalloff(nd);
                float blendWeight = w * opacity;
                if (blendWeight <= 0f) continue;

                switch (op)
                {
                    case BrushEditOp.RandomizeRotation:
                    {
                        float currentYaw = rec.rotation.eulerAngles.y;
                        float targetYaw  = Random.Range(0f, 360f);
                        float newYaw     = Mathf.LerpAngle(currentYaw, targetYaw, blendWeight);
                        // Preserve pitch/roll from current rotation; replace only yaw.
                        Vector3 euler = rec.rotation.eulerAngles;
                        rec.rotation  = Quaternion.Euler(euler.x, newYaw, euler.z);
                        break;
                    }

                    case BrushEditOp.NudgeScale:
                    {
                        // V2: rec.scale is float (uniform). Phase A collapsed non-uniform to uniform.
                        float delta    = Random.Range(-opParams.scaleDelta, opParams.scaleDelta);
                        float newScale = Mathf.Lerp(
                            rec.scale,
                            rec.scale * (1f + delta),
                            blendWeight);
                        newScale = Mathf.Clamp(newScale, layer.ScaleRange.x, layer.ScaleRange.y);
                        rec.scale = newScale;
                        break;
                    }

                    case BrushEditOp.NudgePosition:
                    {
                        Vector2 offset2d = Random.insideUnitCircle * opParams.nudgeRadius * blendWeight;
                        float newX = rec.position.x + offset2d.x;
                        float newZ = rec.position.z + offset2d.y;
                        // Re-snap Y via physics downcast (same approach as StampAuthored).
                        float newY = rec.position.y;
                        const float SNAP_HEIGHT = 100f;
                        if (Physics.Raycast(
                            new Vector3(newX, rec.position.y + SNAP_HEIGHT, newZ),
                            Vector3.down, out RaycastHit snapHit, SNAP_HEIGHT * 2f,
                            layer.GroundSnapMask, QueryTriggerInteraction.Ignore))
                        {
                            newY = snapHit.point.y;
                        }
                        rec.position = new Vector3(newX, newY, newZ);
                        break;
                    }

                    case BrushEditOp.ToggleAlignNormal:
                    {
                        // Per the spec: flip the aligned bit (overrideMask reuse).
                        // We repurpose the RendererOverride bit as an "align-normal" override indicator.
                        // However, since InstanceOverrideMask has no dedicated AlignNormal bit,
                        // we instead directly flip the rotation: if already world-up, snap to surface;
                        // if surface-aligned, reset to world-up yaw-only.
                        float existingYaw = rec.rotation.eulerAngles.y;
                        float lenFromUp = Quaternion.Angle(rec.rotation, Quaternion.Euler(0f, existingYaw, 0f));
                        if (lenFromUp < 5f)
                        {
                            // Currently world-up → align to surface normal.
                            if (Physics.Raycast(
                                new Vector3(rec.position.x, rec.position.y + 100f, rec.position.z),
                                Vector3.down, out RaycastHit normalHit, 200f,
                                layer.GroundSnapMask, QueryTriggerInteraction.Ignore))
                            {
                                rec.rotation = Quaternion.FromToRotation(Vector3.up, normalHit.normal) *
                                               Quaternion.Euler(0f, existingYaw, 0f);
                            }
                        }
                        else
                        {
                            // Currently surface-aligned → reset to world-up yaw-only.
                            rec.rotation = Quaternion.Euler(0f, existingYaw, 0f);
                        }
                        break;
                    }
                }

                edits.Add((idx, rec));
            }

            if (edits.Count > 0)
            {
                sidecar.SetRecords(edits);
                sidecar.PackBlob();
                EditorUtility.SetDirty(sidecar);
            }
        }

        // ── Falloff math helper ────────────────────────────────────────────────

        /// <summary>
        /// Returns the falloff weight for a normalised distance <paramref name="nd"/> ∈ [0,1].
        /// Uses the same SmoothStep kernel as the density-brush stamp path so visual results
        /// are consistent between density-paint and edit-brush modes.
        ///
        /// <paramref name="nd"/> = 0 at the cursor centre (weight = 1); 1 at the brush edge (weight = 0).
        /// </summary>
        internal static float SampleFalloff(float nd)
        {
            // InverseLerp(1, 0, nd) maps nd=0→1, nd=1→0 — centre gets full weight, edge gets 0.
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0f, nd));
        }

        // ── Density texel paint helper ─────────────────────────────────────────

        /// <summary>
        /// Paints a single density-map texel at <paramref name="worldPos"/> with
        /// <paramref name="signedStrength"/> (positive = paint, negative = erase).
        /// Used for transitional dual-write in Place / Erase authored strokes.
        /// </summary>
        private void PaintDensityTexelAtWorld(Vector3 worldPos, ScatterField field, ScatterLayer layer, float signedStrength)
        {
            if (this.buffer == null || this.texWidth == 0 || this.texHeight == 0) return;

            Vector3 origin = ResolveFieldOrigin(field);
            Vector2 effectiveBounds = ResolveEffectiveBounds(field, layer);
            var space = new GrassFieldSpace(origin, effectiveBounds);
            Vector2 uv = space.WorldToUv(worldPos);

            int tx = Mathf.Clamp((int)(uv.x * this.texWidth), 0, this.texWidth - 1);
            int ty = Mathf.Clamp((int)(uv.y * this.texHeight), 0, this.texHeight - 1);
            int idx = ty * this.texWidth + tx;
            this.buffer[idx] = Mathf.Clamp01(this.buffer[idx] + signedStrength);
            this.bufferDirty = true;
        }

        /// <summary>Draws the density overlay thumbnail in the bottom-left of the scene view.</summary>
        internal void DrawOverlay(SceneView sceneView)
        {
            if (this.densityMap == null)
                return;

            Handles.BeginGUI();
            const float size = 128f;
            var rect = new Rect(10f, sceneView.position.height - size - 30f, size, size);
            GUI.Box(rect, GUIContent.none);
            GUI.DrawTexture(rect, this.densityMap, ScaleMode.ScaleToFit, false);
            GUI.Label(new Rect(rect.x, rect.y - 16f, size, 16f), "Density");
            Handles.EndGUI();
        }
    }
}
