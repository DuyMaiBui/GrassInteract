#nullable enable
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Bakes an <see cref="AnimationCurve"/> into a 256×1 RFloat
    /// <see cref="RenderTexture"/> used as a falloff LUT by <c>TerrainBrush.compute</c>.
    ///
    /// Upload is triggered when the CurveField changes (design §5.2).
    /// The RT is released by the caller on tool deactivate.
    /// </summary>
    internal sealed class BrushFalloffLut : System.IDisposable
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int LUT_WIDTH = 256;

        // ── State ─────────────────────────────────────────────────────────────

        private RenderTexture? lut;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the current LUT RT (256×1 RFloat).
        /// Null before the first <see cref="Upload"/> call.
        /// </summary>
        public RenderTexture? LutRT => this.lut;

        /// <summary>
        /// Bakes <paramref name="curve"/> into the LUT RT, creating or resizing it as needed.
        /// Call whenever the CurveField changes.
        /// </summary>
        public void Upload(AnimationCurve curve)
        {
            this.EnsureRT();
            this.WriteCurveToRT(curve);
        }

        // ── Bind helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Sets <c>_FalloffLUT</c> and <c>_UseFalloffLUT=1</c> on the compute shader kernel.
        /// If the LUT RT is null (not yet uploaded), sets <c>_UseFalloffLUT=0</c> (inline fallback).
        /// </summary>
        public void BindToCompute(ComputeShader cs, int kernel)
        {
            if (this.lut != null)
            {
                cs.SetTexture(kernel, "_FalloffLUT", this.lut);
                cs.SetInt("_UseFalloffLUT", 1);
            }
            else
            {
                cs.SetInt("_UseFalloffLUT", 0);
            }
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (this.lut != null)
            {
                this.lut.Release();
                UnityEngine.Object.DestroyImmediate(this.lut);
                this.lut = null;
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void EnsureRT()
        {
            if (this.lut != null && this.lut.IsCreated()) return;

            // Release stale RT if it exists but isn't created.
            if (this.lut != null)
            {
                this.lut.Release();
                UnityEngine.Object.DestroyImmediate(this.lut);
            }

            this.lut = new RenderTexture(LUT_WIDTH, 1, 0, RenderTextureFormat.RFloat)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
                name       = "BrushFalloffLUT",
                enableRandomWrite = false,
            };
            this.lut.Create();
        }

        private void WriteCurveToRT(AnimationCurve curve)
        {
            if (this.lut == null) return;

            var pixels = new float[LUT_WIDTH];
            for (int i = 0; i < LUT_WIDTH; ++i)
            {
                float t = i / (float)(LUT_WIDTH - 1);
                pixels[i] = Mathf.Clamp01(curve.Evaluate(t));
            }

            // Write via a temporary Texture2D → Graphics.Blit is not available for
            // RFloat without a shader; instead write directly to a Texture2D and copy.
            var tmp = new Texture2D(LUT_WIDTH, 1, TextureFormat.RFloat, mipChain: false, linear: true);
            var colors = new Color[LUT_WIDTH];
            for (int i = 0; i < LUT_WIDTH; ++i)
                colors[i] = new Color(pixels[i], 0f, 0f, 1f);
            tmp.SetPixels(colors);
            tmp.Apply();

            var prevActive = RenderTexture.active;
            Graphics.Blit(tmp, this.lut);
            RenderTexture.active = prevActive;

            UnityEngine.Object.DestroyImmediate(tmp);
        }
    }
}
