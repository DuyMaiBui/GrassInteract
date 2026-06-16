using UnityEngine;

namespace MeshAtlas.Editor.Packing
{
    /// <summary>
    /// Result of an <see cref="AtlasPacker"/> pack. On success carries the chosen
    /// square atlas pixel size and the normalized (0-1, bottom-left origin) sub-rects
    /// parallel to the input size list. On failure carries a human-readable reason.
    /// </summary>
    public readonly struct AtlasPackResult
    {
        public bool Success { get; }
        public int AtlasSize { get; }
        public Rect[] Rects { get; }
        public string Error { get; }

        private AtlasPackResult(bool success, int atlasSize, Rect[] rects, string error)
        {
            this.Success = success;
            this.AtlasSize = atlasSize;
            this.Rects = rects;
            this.Error = error;
        }

        public static AtlasPackResult Ok(int atlasSize, Rect[] rects)
            => new AtlasPackResult(true, atlasSize, rects, null);

        public static AtlasPackResult Failure(string error)
            => new AtlasPackResult(false, 0, null, error);
    }
}
