using UnityEngine;

namespace MeshAtlas.Editor.Packing
{
    /// <summary>
    /// Remaps mesh UV0 from its native 0-1 space into an atlas sub-rect.
    /// Pure function — EditMode-unit-testable.
    /// </summary>
    public static class UVRemapper
    {
        /// <summary><c>uvNew = subRect.position + uvOld * subRect.size</c>.</summary>
        public static Vector2 Remap(Vector2 uvOld, Rect subRect)
            => subRect.position + Vector2.Scale(uvOld, subRect.size);

        /// <summary>Batch remap. Returns a new array; the input is not mutated.</summary>
        public static Vector2[] Remap(Vector2[] uvsOld, Rect subRect)
        {
            if (uvsOld == null)
            {
                return System.Array.Empty<Vector2>();
            }
            var result = new Vector2[uvsOld.Length];
            for (var i = 0; i < uvsOld.Length; i++)
            {
                result[i] = Remap(uvsOld[i], subRect);
            }
            return result;
        }
    }
}
