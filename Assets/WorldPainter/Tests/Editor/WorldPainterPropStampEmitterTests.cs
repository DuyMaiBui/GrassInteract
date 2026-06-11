#nullable enable
using NUnit.Framework;
using UnityEngine;
using GpuTerrain.Editor;
using GrassInteract;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for <see cref="WorldPainterPropStampEmitter"/> — spacing-to-record-count math
    /// and delete-mode behaviour (Phase 4 task 2).
    ///
    /// Uses a real <see cref="InstanceScatterLayer"/> + <see cref="AuthoredInstancesData"/>
    /// ScriptableObject pair created in-memory. No GPU / MCP / scene dependencies.
    /// Runs as EditMode tests.
    /// </summary>
    [TestFixture]
    public class WorldPainterPropStampEmitterTests
    {
        private WorldPainterPropStampEmitter emitter = null!;
        private InstanceScatterLayer         layer   = null!;
        private AuthoredInstancesData        authored = null!;

        [SetUp]
        public void SetUp()
        {
            this.emitter  = new WorldPainterPropStampEmitter();
            this.layer    = ScriptableObject.CreateInstance<InstanceScatterLayer>();
            this.authored = ScriptableObject.CreateInstance<AuthoredInstancesData>();

            // Wire authored instances into the layer via the public property.
            // InstanceScatterLayer.AuthoredInstances is a serialized field we access
            // via the public getter — set it via SerializedObject to avoid touching frozen SSOT.
            var so   = new UnityEditor.SerializedObject(this.layer);
            var prop = so.FindProperty("authoredInstances");
            if (prop != null)
            {
                prop.objectReferenceValue = this.authored;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(this.layer);
            Object.DestroyImmediate(this.authored);
        }

        // ── Density per stamp ─────────────────────────────────────────────────

        [Test]
        public void Emit_OneStamp_AddsExactlyDefaultDensityRecords()
        {
            int before = this.authored.WorkingList.Count;

            // One stamp at origin with radius 5m, no surface sampler.
            this.emitter.Emit(this.layer, Vector3.zero, brushRadius: 5f,
                deleteMode: false, surfaceSampler: null);

            int added = this.authored.WorkingList.Count - before;

            // DEFAULT_DENSITY_PER_STAMP = 3 (private const — slope check always passes with
            // null sampler so all 3 are added).
            Assert.AreEqual(3, added,
                "One stamp with no slope rejection should add DEFAULT_DENSITY_PER_STAMP (3) records");
        }

        [Test]
        public void Emit_TwoStamps_RecordCountDoubles()
        {
            this.emitter.Emit(this.layer, Vector3.zero, 5f, false, null);
            int afterFirst = this.authored.WorkingList.Count;

            this.emitter.Emit(this.layer, Vector3.zero, 5f, false, null);
            int afterSecond = this.authored.WorkingList.Count;

            Assert.AreEqual(afterFirst * 2, afterSecond,
                "Second stamp should add the same number of records as the first");
        }

        [Test]
        public void Emit_RecordsAreWithinBrushRadius()
        {
            const float RADIUS = 5f;
            this.emitter.Emit(this.layer, Vector3.zero, RADIUS, false, null);

            foreach (var rec in this.authored.WorkingList)
            {
                float dx = rec.position.x;
                float dz = rec.position.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                Assert.LessOrEqual(dist, RADIUS + 0.001f,
                    $"Record at ({rec.position.x:F2},{rec.position.z:F2}) is outside brush radius {RADIUS}m");
            }
        }

        [Test]
        public void Emit_ScaleIsWithinExpectedRange()
        {
            // Default ScaleRange on a fresh InstanceScatterLayer is 0…0 or 1…1;
            // the emitter uses a midScale = (min+max)/2, clamped to 1 when zero.
            this.emitter.Emit(this.layer, Vector3.zero, 5f, false, null);

            foreach (var rec in this.authored.WorkingList)
                Assert.Greater(rec.scale, 0f, "Scale must be positive");
        }

        // ── Delete mode ───────────────────────────────────────────────────────

        [Test]
        public void Emit_DeleteMode_RemovesRecordsUnderBrush()
        {
            // Add 6 records.
            this.emitter.Emit(this.layer, Vector3.zero, 5f, false, null);
            this.emitter.Emit(this.layer, Vector3.zero, 5f, false, null);
            int before = this.authored.WorkingList.Count;
            Assert.AreEqual(6, before, "Expected 6 records before delete");

            // Delete with same brush — should remove all since they are within radius 5m.
            this.emitter.Emit(this.layer, Vector3.zero, 5f, deleteMode: true, null);

            Assert.AreEqual(0, this.authored.WorkingList.Count,
                "Delete mode should remove all records within brush radius");
        }

        [Test]
        public void Emit_DeleteMode_LeavesRecordsOutsideBrush()
        {
            // Place records far away from (0,0,0).
            var farPos = new Vector3(100f, 0f, 100f);
            this.emitter.Emit(this.layer, farPos, 5f, false, null);
            int before = this.authored.WorkingList.Count;

            // Delete brush at origin — should not touch far records.
            this.emitter.Emit(this.layer, Vector3.zero, 5f, deleteMode: true, null);

            Assert.AreEqual(before, this.authored.WorkingList.Count,
                "Records outside delete brush should be untouched");
        }

        // ── GetDensityLabel ───────────────────────────────────────────────────

        [Test]
        public void GetDensityLabel_ReturnsNonEmptyString()
        {
            string label = WorldPainterPropStampEmitter.GetDensityLabel(this.layer);
            Assert.IsFalse(string.IsNullOrWhiteSpace(label),
                "Density label should not be empty");
        }
    }
}
