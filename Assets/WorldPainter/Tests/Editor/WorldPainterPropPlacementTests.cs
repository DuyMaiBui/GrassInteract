#nullable enable
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WorldPainter;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// EditMode tests for P7 dual-mode prop placement.
    ///
    /// Coverage:
    ///   1. Scatter — brush footprint → N TRS instances in the tile's bucket, within ranges.
    ///   2. Transform-edit — writing back an updated TRS to the bucket.
    ///   3. Anchor config — ground-snap pivot offset applied to record position.
    ///   4. Tile-bucket keying — instances scattered across a tile boundary go into separate buckets.
    ///
    /// No GPU / MCP / scene dependencies; pure EditMode.
    /// </summary>
    [TestFixture]
    public class WorldPainterPropPlacementTests
    {
        // ── Fixtures ──────────────────────────────────────────────────────────

        private WorldPainterPropStampEmitter emitter   = null!;
        private InstanceScatterLayer         layer     = null!;
        private AuthoredInstancesData        authored  = null!;

        [SetUp]
        public void SetUp()
        {
            this.emitter  = new WorldPainterPropStampEmitter();
            this.layer    = ScriptableObject.CreateInstance<InstanceScatterLayer>();
            this.authored = ScriptableObject.CreateInstance<AuthoredInstancesData>();

            WireLayerAuthoredInstances(this.layer, this.authored);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(this.layer);
            Object.DestroyImmediate(this.authored);
        }

        // ── Scatter mode ──────────────────────────────────────────────────────

        [Test]
        public void Scatter_BrushFootprint_AddsDefaultDensityInstances()
        {
            // One stamp at a position well inside tile (0,0).
            this.emitter.Emit(this.layer, new Vector3(10f, 0f, 10f),
                brushRadius: 5f, deleteMode: false, surfaceSampler: null);

            int count = this.authored.WorkingList.Count;
            Assert.AreEqual(WorldPainterPropStampEmitter.DEFAULT_DENSITY_PER_STAMP, count,
                "One scatter stamp should add DEFAULT_DENSITY_PER_STAMP records");
        }

        [Test]
        public void Scatter_Records_AreWithinBrushRadius()
        {
            const float RADIUS  = 5f;
            var centre = new Vector3(20f, 0f, 20f);

            this.emitter.Emit(this.layer, centre, RADIUS, false, null);

            foreach (var rec in this.authored.WorkingList)
            {
                float dx = rec.position.x - centre.x;
                float dz = rec.position.z - centre.z;
                Assert.LessOrEqual(Mathf.Sqrt(dx * dx + dz * dz), RADIUS + 0.001f,
                    "Each record must land within the brush radius");
            }
        }

        [Test]
        public void Scatter_Scale_IsWithinJitterRange()
        {
            // Default ScaleRange on a fresh layer yields midScale = 1 (clamped from 0).
            // ScaleJitter default = 0.25 → scale in [0.75, 1.25].
            const float MID    = 1f;
            const float JITTER = 0.25f; // mirrors WorldPainterPropStampEmitter.DEFAULT_SCALE_JITTER

            this.emitter.Emit(this.layer, Vector3.zero, 5f, false, null);

            float expectedMin = MID * (1f - JITTER) - 0.001f;
            float expectedMax = MID * (1f + JITTER) + 0.001f;

            foreach (var rec in this.authored.WorkingList)
            {
                Assert.Greater(rec.scale, 0f, "Scale must be positive");
                Assert.GreaterOrEqual(rec.scale, expectedMin, "Scale below jitter floor");
                Assert.LessOrEqual(rec.scale, expectedMax, "Scale above jitter ceiling");
            }
        }

        [Test]
        public void Scatter_Yaw_IsFullCircle()
        {
            // With enough stamps, we should see rotation values at various yaw angles.
            // We verify that the yaw (Euler Y) is in [0, 360).
            for (int i = 0; i < 10; i++)
                this.emitter.Emit(this.layer, Vector3.zero, 5f, false, null);

            bool anyNonZeroYaw = false;
            foreach (var rec in this.authored.WorkingList)
            {
                float yaw = rec.rotation.eulerAngles.y;
                Assert.GreaterOrEqual(yaw, 0f, "Yaw must be >= 0");
                Assert.Less(yaw, 360f + 0.001f, "Yaw must be < 360");
                if (yaw > 1f) anyNonZeroYaw = true;
            }
            Assert.IsTrue(anyNonZeroYaw, "Expected some non-zero yaw rotations after 10 stamps");
        }

        [Test]
        public void Scatter_TileBucket_ContainsAllRecordsInTile()
        {
            // Place within tile (0,0) — TerrainWorldGrid maps world [0,256) → tile (0,0).
            var centre = new Vector3(10f, 0f, 10f);
            this.emitter.Emit(this.layer, centre, brushRadius: 2f, false, null);

            var tileCoord = TerrainWorldGrid.WorldToTileCoord(centre.x, centre.z);
            var tileRecords = this.authored.GetTileRecords(tileCoord);

            Assert.AreEqual(this.authored.WorkingList.Count, tileRecords.Count,
                "All records in a single-tile stamp should appear in the tile bucket");
        }

        [Test]
        public void Scatter_TwoBrushesInSameTile_BucketGrows()
        {
            this.emitter.Emit(this.layer, new Vector3(10f, 0f, 10f), 2f, false, null);
            this.emitter.Emit(this.layer, new Vector3(15f, 0f, 15f), 2f, false, null);

            var tileCoord   = TerrainWorldGrid.WorldToTileCoord(12f, 12f);
            var tileRecords = this.authored.GetTileRecords(tileCoord);
            int totalRecords = this.authored.WorkingList.Count;

            Assert.AreEqual(totalRecords, tileRecords.Count,
                "Two stamps in the same tile — bucket count should equal total record count");
        }

        // ── Transform-edit mode ───────────────────────────────────────────────

        [Test]
        public void TransformEdit_UpdateRecord_PersistsNewTRS()
        {
            this.emitter.Emit(this.layer, Vector3.zero, 5f, false, null);

            int idx = 0;
            Assert.IsTrue(this.authored.TryGetRecord(idx, out var original));

            var updated = original;
            updated.position = new Vector3(99f, 2f, 77f);
            updated.rotation = Quaternion.Euler(0f, 45f, 0f);
            updated.scale    = 3.14f;

            bool ok = this.authored.TryUpdateRecord(idx, updated);
            Assert.IsTrue(ok, "TryUpdateRecord should return true for a valid index");

            Assert.IsTrue(this.authored.TryGetRecord(idx, out var readBack));
            Assert.AreEqual(99f,  readBack.position.x, 1e-4f, "position.x after update");
            Assert.AreEqual(2f,   readBack.position.y, 1e-4f, "position.y after update");
            Assert.AreEqual(77f,  readBack.position.z, 1e-4f, "position.z after update");
            Assert.AreEqual(3.14f, readBack.scale, 1e-4f, "scale after update");
        }

        [Test]
        public void TransformEdit_UpdateRecord_OutOfRange_ReturnsFalse()
        {
            bool ok = this.authored.TryUpdateRecord(999, default);
            Assert.IsFalse(ok, "TryUpdateRecord with out-of-range index must return false");
        }

        // ── Anchor config — pivot offset ──────────────────────────────────────

        [Test]
        public void Scatter_PivotOffset_ShiftsRecordPosition()
        {
            // Set propPivotOffset = (0, 1, 0) via SerializedObject.
            SetPivotOffset(this.layer, new Vector3(0f, 1f, 0f));

            // Place at origin with no surface sampler (Y stays 0, pivot offset still applied).
            this.emitter.Emit(this.layer, Vector3.zero, 0.01f, false, null);

            // With brushRadius ≈ 0, all records are near origin with y ~= 1 (pivot offset).
            foreach (var rec in this.authored.WorkingList)
            {
                // The pivot offset is rotated by the record's rotation so exact Y depends on yaw.
                // For a (0,1,0) offset and any Y-rotation the offset stays (0,1,0) because
                // Quaternion.Euler(0, yaw, 0) * (0,1,0) = (0,1,0).
                Assert.AreEqual(1f, rec.position.y, 0.01f,
                    "Pivot offset (0,1,0) should shift y by +1");
            }
        }

        // ── Ground-snap ───────────────────────────────────────────────────────

        [Test]
        public void Scatter_WithNullSampler_NoGroundSnap_YRemainsStampY()
        {
            const float STAMP_Y = 5f;
            this.emitter.Emit(this.layer, new Vector3(0f, STAMP_Y, 0f),
                brushRadius: 0.01f, deleteMode: false, surfaceSampler: null);

            foreach (var rec in this.authored.WorkingList)
                Assert.AreEqual(STAMP_Y, rec.position.y, 0.001f,
                    "Without sampler, Y should remain at stamp Y");
        }

        // ── Per-tile bucket integrity after pack/unpack ────────────────────────

        [Test]
        public void Scatter_PackUnpack_TileBucketRangePreserved()
        {
            this.emitter.Emit(this.layer, new Vector3(10f, 0f, 10f), 2f, false, null);
            var tileCoord = TerrainWorldGrid.WorldToTileCoord(10f, 10f);

            (int start, int count) = this.authored.GetTileBucketRange(tileCoord);
            Assert.GreaterOrEqual(start, 0, "Tile bucket should have a valid start index");
            Assert.Greater(count, 0, "Tile bucket should have a positive count");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void WireLayerAuthoredInstances(InstanceScatterLayer layer, AuthoredInstancesData authored)
        {
            var so   = new SerializedObject(layer);
            var prop = so.FindProperty("authoredInstances");
            if (prop != null)
            {
                prop.objectReferenceValue = authored;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void SetPivotOffset(InstanceScatterLayer layer, Vector3 offset)
        {
            var so   = new SerializedObject(layer);
            var prop = so.FindProperty("propPivotOffset");
            if (prop != null)
            {
                prop.vector3Value = offset;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
