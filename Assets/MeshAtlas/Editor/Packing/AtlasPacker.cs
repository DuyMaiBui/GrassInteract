using System.Collections.Generic;
using UnityEngine;

namespace MeshAtlas.Editor.Packing
{
    /// <summary>
    /// MaxRects (best short-side fit) bin packer. Packs source-texture sizes into a
    /// single power-of-two square atlas and returns normalized (0-1, bottom-left
    /// origin) sub-rects parallel to the input. Each source reserves a `padding`
    /// gutter on its right/top so neighbour regions never touch (mip/bilinear safety).
    ///
    /// Pure C# (UnityEngine math types only) — EditMode-unit-testable, no live render.
    /// </summary>
    public sealed class AtlasPacker
    {
        public const int DEFAULT_PADDING = 4;
        public const int DEFAULT_MAX_ATLAS = 4096;
        private const int MIN_ATLAS = 4;

        private readonly int padding;
        private readonly int maxAtlasSize;

        public AtlasPacker(int padding = DEFAULT_PADDING, int maxAtlasSize = DEFAULT_MAX_ATLAS)
        {
            this.padding = Mathf.Max(0, padding);
            this.maxAtlasSize = Mathf.Max(MIN_ATLAS, Mathf.NextPowerOfTwo(maxAtlasSize));
        }

        /// <summary>Pack the given pixel sizes. Order of <paramref name="sizes"/> is preserved in the result rects.</summary>
        public AtlasPackResult Pack(IReadOnlyList<Vector2Int> sizes)
        {
            if (sizes == null || sizes.Count == 0)
            {
                return AtlasPackResult.Failure("No sizes to pack.");
            }

            var order = new List<int>(sizes.Count);
            for (var i = 0; i < sizes.Count; i++)
            {
                if (sizes[i].x <= 0 || sizes[i].y <= 0)
                {
                    return AtlasPackResult.Failure($"Source #{i} has non-positive size {sizes[i]}.");
                }
                order.Add(i);
            }
            // Largest area first — improves MaxRects fill quality.
            order.Sort((a, b) => ((long)sizes[b].x * sizes[b].y).CompareTo((long)sizes[a].x * sizes[a].y));

            for (var atlas = this.StartSize(sizes); atlas <= this.maxAtlasSize; atlas *= 2)
            {
                if (this.TryPack(sizes, order, atlas, out var pixelRects))
                {
                    return AtlasPackResult.Ok(atlas, Normalize(pixelRects, atlas));
                }
            }
            return AtlasPackResult.Failure(
                $"Cannot fit {sizes.Count} textures within a {this.maxAtlasSize}px atlas (padding {this.padding}).");
        }

        private int StartSize(IReadOnlyList<Vector2Int> sizes)
        {
            long area = 0;
            var maxDim = MIN_ATLAS;
            foreach (var s in sizes)
            {
                area += (long)(s.x + this.padding) * (s.y + this.padding);
                maxDim = Mathf.Max(maxDim, Mathf.Max(s.x, s.y) + this.padding);
            }
            var side = Mathf.Max(Mathf.CeilToInt(Mathf.Sqrt(area)), maxDim);
            return Mathf.NextPowerOfTwo(side);
        }

        private bool TryPack(IReadOnlyList<Vector2Int> sizes, List<int> order, int atlas, out Rect[] pixelRects)
        {
            pixelRects = new Rect[sizes.Count];
            var free = new List<RectInt> { new RectInt(0, 0, atlas, atlas) };
            foreach (var idx in order)
            {
                var w = sizes[idx].x + this.padding;
                var h = sizes[idx].y + this.padding;
                if (!TryFindBest(free, w, h, out var spot))
                {
                    return false;
                }
                // Content rect excludes the reserved gutter (gutter sits to right/top).
                pixelRects[idx] = new Rect(spot.x, spot.y, sizes[idx].x, sizes[idx].y);
                SplitFree(free, new RectInt(spot.x, spot.y, w, h));
                Prune(free);
            }
            return true;
        }

        private static bool TryFindBest(List<RectInt> free, int w, int h, out RectInt best)
        {
            best = default;
            var bestShort = int.MaxValue;
            var bestLong = int.MaxValue;
            var found = false;
            foreach (var f in free)
            {
                if (f.width < w || f.height < h)
                {
                    continue;
                }
                var leftoverH = f.width - w;
                var leftoverV = f.height - h;
                var shortFit = Mathf.Min(leftoverH, leftoverV);
                var longFit = Mathf.Max(leftoverH, leftoverV);
                if (shortFit < bestShort || (shortFit == bestShort && longFit < bestLong))
                {
                    best = new RectInt(f.x, f.y, w, h);
                    bestShort = shortFit;
                    bestLong = longFit;
                    found = true;
                }
            }
            return found;
        }

        private static void SplitFree(List<RectInt> free, RectInt used)
        {
            for (var i = free.Count - 1; i >= 0; i--)
            {
                var f = free[i];
                if (!Intersects(f, used))
                {
                    continue;
                }
                free.RemoveAt(i);
                if (used.xMin > f.xMin)
                {
                    free.Add(new RectInt(f.xMin, f.yMin, used.xMin - f.xMin, f.height));
                }
                if (used.xMax < f.xMax)
                {
                    free.Add(new RectInt(used.xMax, f.yMin, f.xMax - used.xMax, f.height));
                }
                if (used.yMin > f.yMin)
                {
                    free.Add(new RectInt(f.xMin, f.yMin, f.width, used.yMin - f.yMin));
                }
                if (used.yMax < f.yMax)
                {
                    free.Add(new RectInt(f.xMin, used.yMax, f.width, f.yMax - used.yMax));
                }
            }
        }

        private static void Prune(List<RectInt> free)
        {
            for (var i = free.Count - 1; i >= 0; i--)
            {
                for (var j = 0; j < free.Count; j++)
                {
                    if (i != j && Contains(free[j], free[i]))
                    {
                        free.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static bool Intersects(RectInt a, RectInt b)
            => b.xMin < a.xMax && b.xMax > a.xMin && b.yMin < a.yMax && b.yMax > a.yMin;

        private static bool Contains(RectInt outer, RectInt inner)
            => inner.xMin >= outer.xMin && inner.yMin >= outer.yMin
               && inner.xMax <= outer.xMax && inner.yMax <= outer.yMax;

        private static Rect[] Normalize(Rect[] pixelRects, int atlas)
        {
            var inv = 1f / atlas;
            var result = new Rect[pixelRects.Length];
            for (var i = 0; i < pixelRects.Length; i++)
            {
                var p = pixelRects[i];
                result[i] = new Rect(p.x * inv, p.y * inv, p.width * inv, p.height * inv);
            }
            return result;
        }
    }
}
