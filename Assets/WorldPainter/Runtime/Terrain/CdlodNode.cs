#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Blittable GPU struct for one CDLOD render node.
    ///
    /// Byte layout (SSOT — mirror EXACTLY in TerrainNodeCull.compute struct RenderNode):
    ///   float3 worldOffset  : 12 B  (world-space XZ min corner; Y = minHeight for the node)
    ///   float  scale        :  4 B  (world-space side length of this node in metres)
    ///   uint   lod          :  4 B  (LOD band index, 0 = finest)
    ///   float  morphStart   :  4 B  (squared-distance at which morph starts)
    ///   float  morphEnd     :  4 B  (squared-distance at which morph is complete)
    ///   uint   tileIdx      :  4 B  (index into the tile array; 0 for single-tile Phase 1)
    ///   ─── total: 32 B ───
    ///
    /// Only float and uint fields — no bool, no Vector2/Vector3 sub-structs — so
    /// GraphicsBuffer.SetData works without allowUnsafeCode.
    /// </summary>
    public struct CdlodNode
    {
        // ── Fields ────────────────────────────────────────────────────────────
        public Vector3 worldOffset; // XZ min corner + Y base (world-space)
        public float   scale;       // node side length in metres
        public uint    lod;         // LOD band (0 = finest detail)
        public float   morphStart;  // sqrDist at which morph blend begins
        public float   morphEnd;    // sqrDist at which morph blend = 1 (fully coarser)
        public uint    tileIdx;     // tile index (Phase 3 multi-tile; 0 in Phase 1)

        // ── Stride constant ───────────────────────────────────────────────────
        /// <summary>
        /// Byte stride of one CdlodNode in the GPU buffer.
        /// Must equal sizeof(RenderNode) in TerrainNodeCull.compute.
        /// float3(12) + float(4) + uint(4) + float(4) + float(4) + uint(4) = 32 B.
        /// </summary>
        public const int STRIDE = 32;
    }
}
