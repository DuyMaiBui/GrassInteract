#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Live-wiring tests for <see cref="TerrainColliderStreamer"/> self-wiring:
    ///   - RequireComponent auto-resolves the sibling WorldPainter.
    ///   - ResolveTile tier-1 (runtime override) and tier-3 (legacy Tiles list).
    ///   - generateColliders guard cooks / evicts.
    ///   - colliderLodBand → range derivation via the WorldPainter LOD seam (incl. clamp).
    ///
    /// Tier-2 (WorldMapAsset.Map) resolution is exercised end-to-end by the Phase 4 play-mode smoke
    /// (assigning Map in edit mode triggers a full GPU engine build, out of scope for a unit test).
    /// </summary>
    [TestFixture]
    public sealed class TerrainColliderStreamerWiringTests
    {
        private const float TILE = TerrainWorldGrid.TILE_SIZE_M;

        private GameObject go = null!;
        private WorldPainter wp = null!;
        private TerrainColliderStreamer streamer = null!;

        [SetUp]
        public void SetUp()
        {
            this.go = new GameObject("ColliderWiringTest");
            // RequireComponent(WorldPainter) auto-adds the sibling when the streamer is added.
            this.streamer = this.go.AddComponent<TerrainColliderStreamer>();
            this.wp = this.go.GetComponent<WorldPainter>();
            // Drive Tick deterministically — suppress the background SceneView editor auto-tick.
            this.streamer.SuppressEditorAutoTick = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (this.go != null) Object.DestroyImmediate(this.go);
        }

        private static TerrainTileAsset MakeValidTile(Vector2Int coord, float height = 100f,
            float minH = 0f, float maxH = 512f)
        {
            int res  = 5;
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord = coord;
            tile.heightRes = res;
            tile.minHeight = minH;
            tile.maxHeight = maxH;
            tile.heightData = new byte[res * res * TerrainHeightFormat.BYTES_PER_SAMPLE];
            ushort raw = TerrainHeightFormat.EncodeHeight(height, minH, maxH);
            for (int i = 0; i < res * res; ++i)
                TerrainHeightFormat.WriteRaw(tile.heightData, i, raw);
            return tile;
        }

        // ── Sibling auto-resolution ───────────────────────────────────────────

        [Test]
        public void RequireComponent_AutoAddsWorldPainterSibling()
        {
            Assert.IsNotNull(this.wp, "Adding TerrainColliderStreamer must auto-add a WorldPainter sibling.");
        }

        // ── ResolveTile tiers ─────────────────────────────────────────────────

        [Test]
        public void ResolveTile_RuntimeOverride_TakesPriority()
        {
            var coord = new Vector2Int(2, 3);
            var tile  = MakeValidTile(coord);
            this.streamer.RegisterTile(tile);

            Assert.AreSame(tile, this.streamer.ResolveTileForTest(coord),
                "Tier-1 runtime override should resolve the registered tile.");

            Object.DestroyImmediate(tile);
        }

        [Test]
        public void ResolveTile_LegacyTilesList_LinearScan()
        {
            var coord = new Vector2Int(-1, 4);
            var tile  = MakeValidTile(coord);
            this.wp.Tiles.Add(new TileEntry { coord = coord, tileAsset = tile });

            Assert.AreSame(tile, this.streamer.ResolveTileForTest(coord),
                "Tier-3 legacy Tiles scan should resolve by tileAsset.tileCoord.");

            Object.DestroyImmediate(tile);
        }

        [Test]
        public void ResolveTile_UnknownCoord_ReturnsNull()
        {
            Assert.IsNull(this.streamer.ResolveTileForTest(new Vector2Int(99, 99)));
        }

        // ── generateColliders guard ───────────────────────────────────────────

        [Test]
        public void GenerateColliders_TrueCooks_FalseEvictsAll()
        {
            var coord = new Vector2Int(0, 0);
            this.streamer.RegisterTile(MakeValidTile(coord));
            this.streamer.GenerateCollidersForTest = true;
            this.streamer.ColliderLodBandForTest   = 3; // 256 m → ring includes (0,0)

            var camPos = new Vector3(TILE * 0.5f, 0f, TILE * 0.5f); // inside tile (0,0)
            this.streamer.TickForTest(camPos);
            Assert.AreEqual(1, this.streamer.LiveCount,
                "With colliders enabled, the camera's tile should cook (amortised 1/frame).");

            this.streamer.GenerateCollidersForTest = false;
            this.streamer.TickForTest(camPos);
            Assert.AreEqual(0, this.streamer.LiveCount,
                "Disabling generateColliders should evict all live colliders.");
        }

        [Test]
        public void CookedHost_IsHideAndDontSave()
        {
            this.streamer.RegisterTile(MakeValidTile(new Vector2Int(0, 0)));
            this.streamer.GenerateCollidersForTest = true;
            this.streamer.ColliderLodBandForTest   = 3;
            this.streamer.TickForTest(new Vector3(TILE * 0.5f, 0f, TILE * 0.5f));
            Assert.AreEqual(1, this.streamer.LiveCount);

            var tc = this.go.GetComponentInChildren<TerrainCollider>(true);
            Assert.IsNotNull(tc, "A cooked TerrainCollider host should exist.");
            Assert.AreEqual(HideFlags.HideAndDontSave, tc.gameObject.hideFlags,
                "Cooked host must be HideAndDontSave so it never serializes or clutters the hierarchy.");
        }

        [Test]
        public void DebugShowColliders_MakesHostVisible()
        {
            this.streamer.RegisterTile(MakeValidTile(new Vector2Int(0, 0)));
            this.streamer.GenerateCollidersForTest  = true;
            this.streamer.ColliderLodBandForTest    = 3;
            this.streamer.DebugShowCollidersForTest = true;
            this.streamer.TickForTest(new Vector3(TILE * 0.5f, 0f, TILE * 0.5f));

            var tc = this.go.GetComponentInChildren<TerrainCollider>(true);
            Assert.IsNotNull(tc, "A cooked TerrainCollider host should exist.");
            Assert.AreEqual(HideFlags.DontSave, tc.gameObject.hideFlags,
                "With debugShowColliders on, the host should be visible (DontSave), not hidden.");
        }

        // ── Orphan reconciliation (domain-reload duplicate guard) ─────────────

        [Test]
        public void PurgeOrphanColliders_DestroysUntrackedHosts()
        {
            // Simulate a reload orphan: a cooked-host-named child not tracked in `live`.
            var orphan = new GameObject(TerrainColliderProvider.HOST_PREFIX + "9_9");
            orphan.transform.SetParent(this.go.transform, worldPositionStays: false);

            this.streamer.PurgeOrphansForTest();

            Assert.IsTrue(orphan == null, "Untracked cooked-collider host should be purged.");
        }

        // ── LOD-band range derivation (+ clamp) via the WorldPainter seam ──────

        [Test]
        public void LodSeam_DefaultBands_AndClamp()
        {
            Assert.AreEqual(4, this.wp.LodBandCount, "Default WorldPainter has 4 LOD bands.");
            Assert.AreEqual(256f, this.wp.LodRangeMetres(3), 1e-3f, "Band 3 default = 256 m.");
            Assert.AreEqual(32f,  this.wp.LodRangeMetres(0), 1e-3f, "Band 0 default = 32 m.");
            Assert.AreEqual(256f, this.wp.LodRangeMetres(99), 1e-3f, "Out-of-range band clamps to the coarsest.");
            Assert.AreEqual(32f,  this.wp.LodRangeMetres(-5), 1e-3f, "Negative band clamps to the finest.");
        }
    }
}
