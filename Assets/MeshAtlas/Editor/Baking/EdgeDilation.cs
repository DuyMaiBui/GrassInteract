namespace MeshAtlas.Editor.Baking
{
    /// <summary>
    /// Bleeds covered-region edge colors outward into the padding gutter so bilinear /
    /// mip sampling never reads a neighbour region (the classic atlas-bleed artefact).
    /// Pure C# over a flat pixel buffer + coverage mask — EditMode-unit-testable.
    /// </summary>
    public static class EdgeDilation
    {
        private static readonly int[] OffsetX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] OffsetY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>
        /// Iteratively copy the nearest covered neighbour color into each uncovered pixel.
        /// Mutates <paramref name="pixels"/> and <paramref name="covered"/> in place.
        /// </summary>
        public static void Dilate(UnityEngine.Color[] pixels, bool[] covered, int width, int height, int iterations)
        {
            if (pixels == null || covered == null || width <= 0 || height <= 0)
            {
                return;
            }
            for (var pass = 0; pass < iterations; pass++)
            {
                var newlyCovered = new System.Collections.Generic.List<int>();
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var idx = (y * width) + x;
                        if (covered[idx])
                        {
                            continue;
                        }
                        if (TrySampleNeighbour(pixels, covered, width, height, x, y, out var color))
                        {
                            pixels[idx] = color;
                            newlyCovered.Add(idx);
                        }
                    }
                }
                if (newlyCovered.Count == 0)
                {
                    break;
                }
                foreach (var idx in newlyCovered)
                {
                    covered[idx] = true;
                }
            }
        }

        private static bool TrySampleNeighbour(UnityEngine.Color[] pixels, bool[] covered, int width, int height,
            int x, int y, out UnityEngine.Color color)
        {
            for (var k = 0; k < OffsetX.Length; k++)
            {
                var nx = x + OffsetX[k];
                var ny = y + OffsetY[k];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                {
                    continue;
                }
                var nIdx = (ny * width) + nx;
                if (covered[nIdx])
                {
                    color = pixels[nIdx];
                    return true;
                }
            }
            color = default;
            return false;
        }
    }
}
