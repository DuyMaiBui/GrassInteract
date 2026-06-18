using UnityEngine;

namespace MeshAtlas.Editor.Packing
{
    /// <summary>
    /// Computes normalized (0-1, bottom-left origin) grid cells for the import flow's
    /// "auto grid" quick-fill: <paramref name="count"/> materials laid out row-major into a
    /// <c>cols × rows</c> grid. Cell 0 is the TOP-left and the index advances left-to-right
    /// then top-to-bottom, matching how a user reads an atlas image. Pure —
    /// EditMode-unit-testable.
    /// </summary>
    public static class GridRectLayout
    {
        public static Rect[] Compute(int count, int cols, int rows)
        {
            if (count <= 0)
            {
                return System.Array.Empty<Rect>();
            }
            cols = Mathf.Max(1, cols);
            rows = Mathf.Max(1, rows);
            var w = 1f / cols;
            var h = 1f / rows;
            var rects = new Rect[count];
            for (var i = 0; i < count; i++)
            {
                var col = i % cols;
                var rowFromTop = (i / cols) % rows;
                // Flip to UV's bottom-left origin: the top row (rowFromTop 0) gets the highest y.
                var y = 1f - (rowFromTop + 1) * h;
                rects[i] = new Rect(col * w, y, w, h);
            }
            return rects;
        }

        /// <summary>Smallest square-ish grid that holds <paramref name="count"/> cells.</summary>
        public static Vector2Int SuggestGrid(int count)
        {
            if (count <= 1)
            {
                return new Vector2Int(1, 1);
            }
            var cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            var rows = Mathf.CeilToInt(count / (float)cols);
            return new Vector2Int(cols, rows);
        }
    }
}
