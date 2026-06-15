#nullable enable
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Binds the active brush's optional mask texture (<see cref="BrushSettings.maskTexture"/> on
    /// <see cref="WorldPainterState.Brush"/>) to a brush compute kernel as <c>_BrushMask</c> +
    /// <c>_UseBrushMask</c> — mirroring <see cref="BrushFalloffLut.BindToCompute"/>.
    ///
    /// Call once per dispatch, immediately after <c>falloffLut.BindToCompute</c>, on the same
    /// kernel. When no mask is set it binds a dummy white texture and <c>_UseBrushMask=0</c>:
    /// the kernel references <c>_BrushMask</c> unconditionally (via <c>BrushMask.hlsl</c>), so
    /// Unity rejects <c>Dispatch</c> if the slot is left unbound even on the disabled branch.
    /// </summary>
    internal static class BrushMaskBinder
    {
        public static void BindToCompute(ComputeShader cs, int kernel)
        {
            var mask = WorldPainterState.Brush.maskTexture;
            if (mask != null)
            {
                cs.SetTexture(kernel, "_BrushMask", mask);
                cs.SetInt("_UseBrushMask", 1);
            }
            else
            {
                cs.SetTexture(kernel, "_BrushMask", Texture2D.whiteTexture);
                cs.SetInt("_UseBrushMask", 0);
            }
        }
    }
}
