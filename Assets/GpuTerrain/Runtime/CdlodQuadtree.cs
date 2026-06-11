#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GpuTerrain
{
    /// <summary>
    /// CPU CDLOD quadtree for one tile.
    /// Builds an N-level tree where level 0 covers the full tile and level (depth-1) is a
    /// single PATCH_RES-sized leaf.  Per-frame Select(cameraPos) traverses the tree and emits
    /// a flat CdlodNode[] that covers the tile with no gaps/overlaps.
    ///
    /// LOD selection rule: a node at level L is SPLIT (recurse into children) when the camera
    /// is within lodRanges[L] of the node's centre.  Otherwise the node is emitted as-is.
    ///
    /// Morph ranges: each node carries [morphStart, morphEnd] in SQUARED-DISTANCE space.
    /// The VS blends XZ grid positions toward the coarser grid when blend = 1 (far end).
    /// </summary>
    public sealed class CdlodQuadtree
    {
        // ── Constants ─────────────────────────────────────────────────────────
        /// <summary>Maximum supported LOD depth (bands).</summary>
        public const int MAX_LOD_BANDS = 8;

        /// <summary>
        /// Fraction of the LOD range over which morphing occurs.
        /// Morph starts at (1 - MORPH_FRACTION) * range end.
        /// </summary>
        private const float MORPH_FRACTION = 0.15f;

        // ── Configuration ─────────────────────────────────────────────────────
        private readonly float tileSize;      // metres (TerrainWorldGrid.TILE_SIZE_M)
        private readonly float tileOriginX;   // world-space X of tile min corner
        private readonly float tileOriginZ;   // world-space Z of tile min corner
        private readonly float tileMinY;      // minHeight of the tile asset
        private readonly float tileMaxY;      // maxHeight of the tile asset
        private readonly int   depth;         // number of LOD levels = log2(tileSize/minNodeSize)
        private readonly float[] lodRanges;   // squared-distance threshold per level [0..depth-1]
        private readonly uint tileIdx;        // tile index for CdlodNode.tileIdx

        // ── Output (reused per frame) ─────────────────────────────────────────
        private readonly List<CdlodNode> selected = new List<CdlodNode>(256);

        // ── Construction ──────────────────────────────────────────────────────

        /// <summary>
        /// Build the quadtree for one tile.
        /// </summary>
        /// <param name="tileOriginX">World X of tile min corner.</param>
        /// <param name="tileOriginZ">World Z of tile min corner.</param>
        /// <param name="tileMinY">minHeight of the tile (for node AABB Y).</param>
        /// <param name="tileMaxY">maxHeight of the tile (for node AABB Y).</param>
        /// <param name="tileSize">World side length of the tile (TILE_SIZE_M).</param>
        /// <param name="lodRangesLinear">
        /// LOD range distances in metres per level, index 0 = finest.
        /// lodRangesLinear[i] = maximum distance (metres) at which level i is rendered.
        /// Stored internally as squared distances.
        /// </param>
        /// <param name="tileIdx">Tile index packed into each emitted CdlodNode.</param>
        public CdlodQuadtree(
            float tileOriginX, float tileOriginZ,
            float tileMinY,    float tileMaxY,
            float tileSize,
            float[] lodRangesLinear,
            uint tileIdx = 0)
        {
            this.tileOriginX = tileOriginX;
            this.tileOriginZ = tileOriginZ;
            this.tileMinY    = tileMinY;
            this.tileMaxY    = tileMaxY;
            this.tileSize    = tileSize;
            this.tileIdx     = tileIdx;

            int bandCount = Mathf.Clamp(lodRangesLinear.Length, 1, MAX_LOD_BANDS);
            this.depth     = bandCount;
            this.lodRanges = new float[bandCount];
            for (int i = 0; i < bandCount; ++i)
                this.lodRanges[i] = lodRangesLinear[i] * lodRangesLinear[i];
        }

        /// <summary>
        /// Per-frame traversal: select nodes that cover the tile for the given camera position.
        /// Returns a view into an internal list — copy if you need persistence across frames.
        /// </summary>
        public IReadOnlyList<CdlodNode> Select(Vector3 cameraPos)
        {
            this.selected.Clear();
            this.SelectNode(
                cameraPos,
                this.tileOriginX, this.tileOriginZ,
                this.tileSize,
                level: 0);
            return this.selected;
        }

        // ── Private recursion ─────────────────────────────────────────────────

        private void SelectNode(
            Vector3 cameraPos,
            float nodeOriginX, float nodeOriginZ,
            float nodeSize,
            int level)
        {
            bool isLeaf = (level >= this.depth - 1);

            if (!isLeaf)
            {
                // Test whether any child centre is within the next-finer LOD range.
                float childSize = nodeSize * 0.5f;
                float cx = nodeOriginX + childSize * 0.5f;
                float cz = nodeOriginZ + childSize * 0.5f;

                float sqrDistToNodeCentre = SqrDistXZ(cameraPos, nodeOriginX + nodeSize * 0.5f,
                                                                   nodeOriginZ + nodeSize * 0.5f);

                if (sqrDistToNodeCentre < this.lodRanges[this.depth - 1 - level])
                {
                    // Split into 4 children
                    this.SelectNode(cameraPos, nodeOriginX,            nodeOriginZ,            childSize, level + 1);
                    this.SelectNode(cameraPos, nodeOriginX + childSize, nodeOriginZ,            childSize, level + 1);
                    this.SelectNode(cameraPos, nodeOriginX,            nodeOriginZ + childSize, childSize, level + 1);
                    this.SelectNode(cameraPos, nodeOriginX + childSize, nodeOriginZ + childSize, childSize, level + 1);
                    return;
                }
            }

            // Emit this node.
            // morphEnd = squared-distance at which a coarser parent node would be selected instead.
            // This equals the parent's split threshold: lodRanges[depth - level] for non-root nodes.
            // lodRanges[i] is stored indexed by public LOD index (0=finest), which is (depth-1-internalLevel).
            // For level=0 (root, coarsest), no parent exists → float.MaxValue (never morphs away).
            float morphEnd   = level > 0 ? this.lodRanges[this.depth - level] : float.MaxValue;
            float morphStart = morphEnd < float.MaxValue ? morphEnd * (1f - MORPH_FRACTION) : morphEnd;

            this.selected.Add(new CdlodNode
            {
                worldOffset = new Vector3(nodeOriginX, this.tileMinY, nodeOriginZ),
                scale       = nodeSize,
                lod         = (uint)(this.depth - 1 - level),
                morphStart  = morphStart,
                morphEnd    = morphEnd,
                tileIdx     = this.tileIdx,
            });
        }

        private static float SqrDistXZ(Vector3 cam, float cx, float cz)
        {
            float dx = cam.x - cx;
            float dz = cam.z - cz;
            return dx * dx + dz * dz;
        }
    }
}
