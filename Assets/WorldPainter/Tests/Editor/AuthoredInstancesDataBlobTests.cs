#nullable enable
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// EditMode coverage for the <see cref="AuthoredInstancesData"/> V3 blob codec
    /// (Phase 1 — PhysicMaterial per instance).
    /// </summary>
    public sealed class AuthoredInstancesDataBlobTests
    {
        // ── Reflection helpers (the blob codec is private by design) ───────────

        private static readonly FieldInfo BlobField =
            typeof(AuthoredInstancesData).GetField("blob", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly FieldInfo WorkingListField =
            typeof(AuthoredInstancesData).GetField("workingList", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static byte[] GetBlob(AuthoredInstancesData data) => (byte[])BlobField.GetValue(data)!;

        /// <summary>Nulls the cached working list so the next read re-unpacks from the blob.</summary>
        private static void ForceReloadFromBlob(AuthoredInstancesData data) => WorkingListField.SetValue(data, null);

        private static void AssertRecordsEqual(InstanceRecord expected, InstanceRecord actual, string ctx)
        {
            Assert.AreEqual(expected.position.x, actual.position.x, 1e-5f, $"{ctx} position.x");
            Assert.AreEqual(expected.position.y, actual.position.y, 1e-5f, $"{ctx} position.y");
            Assert.AreEqual(expected.position.z, actual.position.z, 1e-5f, $"{ctx} position.z");
            Assert.AreEqual(expected.rotation.x, actual.rotation.x, 1e-5f, $"{ctx} rotation.x");
            Assert.AreEqual(expected.rotation.y, actual.rotation.y, 1e-5f, $"{ctx} rotation.y");
            Assert.AreEqual(expected.rotation.z, actual.rotation.z, 1e-5f, $"{ctx} rotation.z");
            Assert.AreEqual(expected.rotation.w, actual.rotation.w, 1e-5f, $"{ctx} rotation.w");
            Assert.AreEqual(expected.scale, actual.scale, 1e-5f, $"{ctx} scale");
            Assert.AreEqual(expected.overrideMask, actual.overrideMask, $"{ctx} overrideMask");
            Assert.AreEqual(expected.generateCollider, actual.generateCollider, $"{ctx} generateCollider");
            Assert.AreEqual(expected.colliderConvex, actual.colliderConvex, $"{ctx} colliderConvex");
            Assert.AreEqual(expected.colliderScale, actual.colliderScale, 1e-5f, $"{ctx} colliderScale");
            Assert.AreEqual(expected.colliderMeshRefIndex, actual.colliderMeshRefIndex, $"{ctx} meshRefIndex");
            Assert.AreEqual(expected.colliderMaterialRefIndex, actual.colliderMaterialRefIndex, $"{ctx} matRefIndex");
        }

        // ── Test 4: byte-layout constants ──────────────────────────────────────

        [Test]
        public void ColliderBytes_Is20_AndByteSizeBoundaries()
        {
            Assert.AreEqual(20, InstanceRecord.COLLIDER_BYTES, "COLLIDER_BYTES");
            Assert.AreEqual(36, InstanceRecord.FIXED_BYTES, "FIXED_BYTES");

            var noCollider = new InstanceRecord { overrideMask = InstanceOverrideMask.None };
            Assert.AreEqual(36, noCollider.ByteSize(), "no-collider record ByteSize");

            var withCollider = new InstanceRecord { overrideMask = InstanceOverrideMask.ColliderConfigured };
            Assert.AreEqual(56, withCollider.ByteSize(), "collider record ByteSize");
        }

        // ── Test 1: V3 pack/unpack round-trip ──────────────────────────────────

        [Test]
        public void V3_RoundTrip_PreservesAllFieldsIncludingMaterialRef()
        {
            var data = ScriptableObject.CreateInstance<AuthoredInstancesData>();
            try
            {
                var r0 = new InstanceRecord
                {
                    position = new Vector3(1f, 2f, 3f), rotation = Quaternion.Euler(10f, 20f, 30f),
                    scale = 1.5f, overrideMask = InstanceOverrideMask.None,
                    colliderScale = 1f, colliderMeshRefIndex = -1, colliderMaterialRefIndex = -1,
                };
                var r1 = new InstanceRecord
                {
                    position = new Vector3(4f, 5f, 6f), rotation = Quaternion.identity,
                    scale = 2f, overrideMask = InstanceOverrideMask.ColliderConfigured,
                    generateCollider = true, colliderConvex = true, colliderScale = 0.5f,
                    colliderMeshRefIndex = 3, colliderMaterialRefIndex = -1,
                };
                var r2 = new InstanceRecord
                {
                    position = new Vector3(7f, 8f, 9f), rotation = Quaternion.Euler(5f, 0f, 0f),
                    scale = 0.8f, overrideMask = InstanceOverrideMask.ColliderConfigured,
                    generateCollider = true, colliderConvex = false, colliderScale = 1f,
                    colliderMeshRefIndex = 2, colliderMaterialRefIndex = 4,
                };
                data.AddRecord(r0);
                data.AddRecord(r1);
                data.AddRecord(r2);

                data.PackBlob();
                Assert.AreEqual(3, GetBlob(data)[0], "version byte must be 3");

                ForceReloadFromBlob(data);
                var list = data.WorkingList;

                Assert.AreEqual(3, list.Count, "record count after re-read");
                AssertRecordsEqual(r0, list[0], "r0");
                AssertRecordsEqual(r1, list[1], "r1");
                AssertRecordsEqual(r2, list[2], "r2");
            }
            finally { UnityEngine.Object.DestroyImmediate(data); }
        }

        // ── Test 5: CountFromBlobV3 parity (no full unpack) ────────────────────

        [Test]
        public void CountFromBlobV3_MatchesWorkingListCount()
        {
            var data = ScriptableObject.CreateInstance<AuthoredInstancesData>();
            try
            {
                data.AddRecord(new InstanceRecord { overrideMask = InstanceOverrideMask.None, colliderMaterialRefIndex = -1 });
                data.AddRecord(new InstanceRecord
                {
                    overrideMask = InstanceOverrideMask.ColliderConfigured,
                    generateCollider = true, colliderScale = 1f, colliderMeshRefIndex = 0, colliderMaterialRefIndex = 1,
                });
                data.AddRecord(new InstanceRecord { overrideMask = InstanceOverrideMask.None, colliderMaterialRefIndex = -1 });
                data.PackBlob();

                // Null the working list so Count routes through CountFromBlob (→ CountFromBlobV3).
                ForceReloadFromBlob(data);
                Assert.AreEqual(3, data.Count, "CountFromBlobV3 parity with record count");
            }
            finally { UnityEngine.Object.DestroyImmediate(data); }
        }

        // ── Test 6: pool assigns PhysicMaterial (override + layer default) ──────

        [Test]
        public void Pool_AssignsSharedMaterial_OverrideThenDefault()
        {
            var go = new GameObject("PoolTest");
            var defaultMat = new PhysicsMaterial("default");
            var overrideMat = new PhysicsMaterial("override");
            try
            {
                var pool = go.AddComponent<InstanceColliderPool>();
                pool.Init(4, null, false, defaultMat);

                MeshCollider? mcDefault = pool.Acquire(0, Vector3.zero, Quaternion.identity, 1f, null, false, null);
                Assert.IsNotNull(mcDefault, "acquire (default material)");
                Assert.AreSame(defaultMat, mcDefault!.sharedMaterial, "null override → layer default material");

                MeshCollider? mcOverride = pool.Acquire(1, Vector3.one, Quaternion.identity, 1f, null, false, overrideMat);
                Assert.IsNotNull(mcOverride, "acquire (override material)");
                Assert.AreSame(overrideMat, mcOverride!.sharedMaterial, "override material wins");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(defaultMat);
                UnityEngine.Object.DestroyImmediate(overrideMat);
            }
        }

    }
}
