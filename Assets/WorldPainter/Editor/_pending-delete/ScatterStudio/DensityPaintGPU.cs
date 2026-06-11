#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GrassInteract.Editor
{
    /// <summary>
    /// GPU brush-splat pipeline for density painting. Manages a canonical linear
    /// <see cref="RenderTexture"/> (<c>rt</c>) plus a same-size scratch copy (<c>scratch</c>)
    /// that is Blit'd from <c>rt</c> before each stamp render.
    ///
    /// <para>
    /// <b>SUPERSEDED (P3b):</b> The density paint math is now folded into
    /// <c>TerrainBrush.compute</c> (<c>PaintDensity</c> kernel) and
    /// <c>GpuTerrain.Editor.WorldPainterDensityEncoder</c>. This class is kept alive
    /// because <c>DensityPaintTool</c> and <c>DensityPaintPanel</c> (Scatter Studio window)
    /// still reference it. Remove when Scatter Studio retires. Do NOT add new callers.
    /// </para>
    ///
    /// <para>
    /// <b>BLOCKER-1 fix — no same-RT read-modify-write:</b><br/>
    /// D3D11 (and Metal/Vulkan under some drivers) produces undefined results when the same
    /// RenderTexture is bound as both render target and shader input in the same draw call.
    /// Fix: before every <see cref="PaintAt"/> stamp, <c>Graphics.Blit(rt, scratch)</c> makes a
    /// stable read-only copy. The shader samples <c>_DensityTex = scratch</c> and writes into
    /// <c>rt</c>. Source and target are always distinct objects.
    /// </para>
    ///
    /// <para>
    /// <b>Load-bearing gating contract (score-20 risk):</b><br/>
    /// <see cref="DensityPlacement"/> reads CPU pixels via <c>GetPixelBilinear</c>. The GPU RT
    /// alone will NOT re-scatter. <see cref="RequestLiveReadback"/> is called by
    /// <see cref="DensityPaintTool.OnEditorTick"/> at most once per 0.15s — never per drag event.
    /// </para>
    ///
    /// <para>
    /// RT format: linear R8_UNorm (sRGB=false). Falls back to R16_SFloat then R32_SFloat.
    /// Format is asserted + logged once at <see cref="BeginStroke"/>.
    /// </para>
    /// </summary>
    internal sealed class DensityPaintGPU
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const string SHADER_NAME = "Hidden/GrassInteract/DensityPaintBrush";

        // ── Shader property IDs ───────────────────────────────────────────────

        private static readonly int PropBrushTex  = Shader.PropertyToID("_BrushTex");
        private static readonly int PropStrength  = Shader.PropertyToID("_Strength");
        private static readonly int PropMode      = Shader.PropertyToID("_Mode");
        private static readonly int PropInner     = Shader.PropertyToID("_Inner");
        private static readonly int PropHasBrush  = Shader.PropertyToID("_HasBrush");
        private static readonly int PropDensityTex = Shader.PropertyToID("_DensityTex");
        private static readonly int PropTexelSize  = Shader.PropertyToID("_TexelSize");

        // ── Cached static GPU resources ───────────────────────────────────────

        private static Material? splatMaterial;
        private static bool      shaderErrorLogged;
        private static bool      formatWarningLogged;

        // ── Per-stroke state ──────────────────────────────────────────────────

        /// <summary>Canonical paint surface — always the write target; never bound as _DensityTex.</summary>
        private RenderTexture? rt;

        /// <summary>
        /// Scratch copy — updated from <c>rt</c> via Blit before each stamp.
        /// Bound as <c>_DensityTex</c> so the shader reads a stable snapshot (BLOCKER-1 fix).
        /// </summary>
        private RenderTexture? scratch;

        private Texture2D? densityMap;       // CPU density map being painted into

        // ── Stroke last-UV tracking ───────────────────────────────────────────

        private Vector2 lastPaintUv;
        private bool    hasLastPaint;

        // ── Static initialisation ─────────────────────────────────────────────

        static DensityPaintGPU()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        }

        private static void OnBeforeReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            DestroyStaticResources();
        }

        // ── Stroke lifecycle ──────────────────────────────────────────────────

        /// <summary>
        /// Begins a new paint stroke. Allocates the canonical RT and the scratch RT, seeds both
        /// from the CPU density map, and hands the canonical RT to
        /// <see cref="ScatterDensityOverlay"/> for live display.
        /// </summary>
        internal void BeginStroke(Texture2D map)
        {
            this.densityMap   = map;
            this.hasLastPaint = false;

            if (!EnsureSplatMaterial()) return;

            this.rt      = AllocateRenderTexture(map.width, map.height);
            this.scratch = AllocateRenderTexture(map.width, map.height);
            if (this.rt == null || this.scratch == null)
            {
                this.ReleaseRTs();
                return;
            }

            // Seed canonical RT from the CPU map; scratch starts as a copy of that.
            Graphics.Blit(map, this.rt);
            Graphics.Blit(this.rt, this.scratch);

            ScatterDensityOverlay.SetActiveRenderTexture(this.rt);
        }

        /// <summary>
        /// Splats the brush from <paramref name="fromUv"/> to <paramref name="toUv"/> using
        /// separate X and Y UV radii to honour non-square field bounds (NTH-2).
        /// Stamps at intervals of <c>min(radUvX,radUvY) * spacingFactor</c>; always stamps at
        /// <paramref name="toUv"/>; updates <see cref="lastPaintUv"/>.
        /// No CPU readback here — see class summary for the gating contract.
        /// </summary>
        internal void StrokeTo(
            Vector2    fromUv,
            Vector2    toUv,
            float      radUvX,
            float      radUvY,
            float      spacingFactor,
            float      strength,
            Texture2D? brushTex,
            int        mode,
            float      inner)
        {
            if (this.rt == null || splatMaterial == null) return;

            float radiusForSpacing = Mathf.Min(radUvX, radUvY);
            Vector2[] positions = ComputeStampPositions(fromUv, toUv, radiusForSpacing, spacingFactor);

            foreach (Vector2 pos in positions)
                this.PaintAt(pos, radUvX, radUvY, strength, brushTex, mode, inner);

            this.lastPaintUv  = toUv;
            this.hasLastPaint = true;
        }

        /// <summary>
        /// Computes the stamp positions along a stroke segment from <paramref name="fromUv"/> to
        /// <paramref name="toUv"/> at <c>spacing = radiusUv * spacingFactor</c> intervals.
        /// Always includes <paramref name="toUv"/>.
        ///
        /// Exposed as <c>internal static</c> so P6 EditMode tests exercise it without GPU.
        /// </summary>
        internal static Vector2[] ComputeStampPositions(
            Vector2 fromUv,
            Vector2 toUv,
            float   radiusUv,
            float   spacingFactor)
        {
            float spacing = radiusUv * Mathf.Max(0.001f, spacingFactor);
            float dist    = Vector2.Distance(fromUv, toUv);

            if (dist < 0.0001f)
                return new[] { toUv };

            int count = Mathf.Max(1, Mathf.CeilToInt(dist / spacing));
            var result = new List<Vector2>(count);

            for (int i = 1; i <= count; ++i)
                result.Add(Vector2.Lerp(fromUv, toUv, i / (float)count));

            return result.ToArray();
        }

        /// <summary>
        /// Synchronously reads the canonical RT back into the CPU density map. Runs immediately on the
        /// calling thread — unlike an async readback whose completion callback is pumped on
        /// <see cref="EditorApplication.update"/>, which Unity starves during an active Scene-view drag
        /// (so the grass would only re-scatter on mouse-up). Throttle at the call site (0.15s); a 512²
        /// <c>ReadPixels</c> per call is acceptable at ~6-7/s but must not run per-frame.
        /// </summary>
        internal void ReadbackNow()
        {
            if (this.rt == null || this.densityMap == null) return;

            // TODO: pool the scratch Texture2D to avoid the per-readback alloc.
            Color[] pixels = DensityMapFactory.ReadbackToPixels(this.rt);
            this.densityMap.SetPixels(pixels);
            this.densityMap.Apply(false);
        }

        /// <summary>
        /// Ends the stroke: synchronous readback → persist to PNG, clear overlay seam, release RTs.
        /// </summary>
        internal void EndStroke()
        {
            if (this.rt != null && this.densityMap != null)
            {
                Color[] pixels = DensityMapFactory.ReadbackToPixels(this.rt);
                int w = this.rt.width;
                int h = this.rt.height;

                this.densityMap.SetPixels(pixels);
                this.densityMap.Apply(false);
                EditorUtility.SetDirty(this.densityMap);

                DensityMapFactory.PersistPixels(this.densityMap, pixels, w, h);
            }

            ScatterDensityOverlay.SetActiveRenderTexture(null);
            this.ReleaseRTs();

            this.densityMap   = null;
            this.hasLastPaint = false;
        }

        // ── Internal state accessors ──────────────────────────────────────────

        internal bool TryGetLastUv(out Vector2 uv)
        {
            uv = this.lastPaintUv;
            return this.hasLastPaint;
        }

        // ── Private GPU helpers ───────────────────────────────────────────────

        /// <summary>
        /// Renders one brush stamp into <c>rt</c>, reading density from the <c>scratch</c> copy.
        ///
        /// BLOCKER-1 fix: before rendering, <c>Graphics.Blit(rt, scratch)</c> captures the current
        /// canonical state into the scratch RT. The shader samples <c>_DensityTex = scratch</c>
        /// and writes into <c>rt</c>. Source and target are always distinct — no D3D11 aliasing UB.
        /// </summary>
        private void PaintAt(
            Vector2    centerUv,
            float      radUvX,
            float      radUvY,
            float      strength,
            Texture2D? brushTex,
            int        mode,
            float      inner)
        {
            if (this.rt == null || this.scratch == null || splatMaterial == null) return;

            // ── BLOCKER-1 fix: snapshot rt → scratch before rendering this stamp ──
            Graphics.Blit(this.rt, this.scratch);

            // ── SF-4: _HasBrush controls whether the shader samples _BrushTex.r ──
            // BrushStamp.Shape is a Grayscale R8 texture; mask lives in the R channel.
            // When no stamp is bound (brushTex == null), _HasBrush = 0 and the shader
            // uses only the procedural radial falloff — _BrushTex is ignored.
            bool hasBrush = brushTex != null;
            splatMaterial.SetTexture(PropBrushTex, hasBrush ? (Texture)brushTex! : Texture2D.whiteTexture);
            splatMaterial.SetFloat(PropHasBrush, hasBrush ? 1f : 0f);

            // ── Bind scratch as the read source (BLOCKER-1 contract) ──────────
            splatMaterial.SetTexture(PropDensityTex, this.scratch);

            splatMaterial.SetFloat(PropStrength, Mathf.Clamp01(strength));
            splatMaterial.SetFloat(PropMode,     (float)mode);
            splatMaterial.SetFloat(PropInner,    Mathf.Clamp01(inner));

            // ── SF-3: pass actual texel size so smooth kernel adapts to map resolution ──
            splatMaterial.SetVector(PropTexelSize,
                new Vector4(1f / this.rt.width, 1f / this.rt.height, 0f, 0f));

            RenderTexture? prev = RenderTexture.active;
            try
            {
                RenderTexture.active = this.rt; // write target
                GL.PushMatrix();
                GL.LoadOrtho();

                // NTH-2: honour separate X and Y radii for non-square field bounds.
                float x0 = centerUv.x - radUvX;
                float x1 = centerUv.x + radUvX;
                float y0 = centerUv.y - radUvY;
                float y1 = centerUv.y + radUvY;

                splatMaterial.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.TexCoord2(0, 0); GL.Vertex3(x0, y0, 0);
                GL.TexCoord2(1, 0); GL.Vertex3(x1, y0, 0);
                GL.TexCoord2(1, 1); GL.Vertex3(x1, y1, 0);
                GL.TexCoord2(0, 1); GL.Vertex3(x0, y1, 0);
                GL.End();

                GL.PopMatrix();
            }
            finally
            {
                RenderTexture.active = prev;
            }
        }

        // ── RT allocation ─────────────────────────────────────────────────────

        private static RenderTexture? AllocateRenderTexture(int w, int h)
        {
            GraphicsFormat fmt;
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render))
            {
                fmt = GraphicsFormat.R8_UNorm;
            }
            else if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.Render))
            {
                if (!formatWarningLogged)
                {
                    formatWarningLogged = true;
                    Debug.LogWarning("[DensityPaintGPU] R8_UNorm RT unsupported — falling back to R16_SFloat.");
                }
                fmt = GraphicsFormat.R16_SFloat;
            }
            else
            {
                if (!formatWarningLogged)
                {
                    formatWarningLogged = true;
                    Debug.LogWarning("[DensityPaintGPU] R8_UNorm and R16_SFloat unsupported — falling back to R32_SFloat.");
                }
                fmt = GraphicsFormat.R32_SFloat;
            }

            Debug.Log($"[DensityPaintGPU] Allocating {w}x{h} RT with format {fmt} (sRGB=false, linear density).");

            var rt = new RenderTexture(w, h, 0, fmt)
            {
                name       = "DensityPaintRT",
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
            };

            if (rt.sRGB)
            {
                Debug.LogError("[DensityPaintGPU] RT.sRGB is true despite non-sRGB GraphicsFormat — " +
                               "density values may be gamma-shifted. Check Unity version.");
            }

            rt.Create();
            return rt;
        }

        private void ReleaseRTs()
        {
            if (this.rt != null)
            {
                // Clear RenderTexture.active before releasing to avoid Unity's
                // "Releasing render texture that is set to be RenderTexture.active" warning.
                if (RenderTexture.active == this.rt) RenderTexture.active = null;
                this.rt.Release();
                UnityEngine.Object.DestroyImmediate(this.rt);
                this.rt = null;
            }
            if (this.scratch != null)
            {
                if (RenderTexture.active == this.scratch) RenderTexture.active = null;
                this.scratch.Release();
                UnityEngine.Object.DestroyImmediate(this.scratch);
                this.scratch = null;
            }
        }

        // ── Shader / material helpers ─────────────────────────────────────────

        private static bool EnsureSplatMaterial()
        {
            if (splatMaterial != null) return true;

            Shader? shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                if (!shaderErrorLogged)
                {
                    shaderErrorLogged = true;
                    Debug.LogError($"[DensityPaintGPU] Shader '{SHADER_NAME}' not found — " +
                                   "GPU density paint disabled. Ensure the shader asset exists " +
                                   "under Editor/Resources/.");
                }
                return false;
            }

            splatMaterial = new Material(shader)
            {
                name      = "DensitySplatMat",
                hideFlags = HideFlags.HideAndDontSave,
            };
            return true;
        }

        private static void DestroyStaticResources()
        {
            if (splatMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(splatMaterial);
                splatMaterial = null;
            }
            // NTH-1: unitQuad field removed — PaintAt uses GL.QUADS; no dead field to destroy.
            shaderErrorLogged   = false;
            formatWarningLogged = false;
        }
    }
}
