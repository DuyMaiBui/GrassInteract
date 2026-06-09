#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GrassInteract.Tests
{
    /// <summary>
    /// EditMode coverage for the <see cref="AuthoredInstancesData"/> V3 blob codec and the
    /// V1→V2→V3 migration chain (Phase 1 — PhysicMaterial per instance).
    /// </summary>
    public sealed class AuthoredInstancesDataBlobTests
    {
        // ── Reflection helpers (the blob codec is private by design) ───────────

        private static readonly FieldInfo BlobField =
            typeof(AuthoredInstancesData).GetField("blob", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly FieldInfo WorkingListField =
            typeof(AuthoredInstancesData).GetField("workingList", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static byte[] GetBlob(AuthoredInstancesData data) => (byte[])BlobField.GetValue(data)!;
        private static void SetBlob(AuthoredInstancesData data, byte[] bytes) => BlobField.SetValue(data, bytes);

        /// <summary>Nulls the cached working list so the next read re-unpacks from the blob.</summary>
        private static void ForceReloadFromBlob(AuthoredInstancesData data) => WorkingListField.SetValue(data, null);

        // ── Little-endian byte writer mirroring AuthoredInstancesData's codec ───

        private sealed class ByteBuf
        {
            private readonly List<byte> bytes = new();
            public void WriteByte(byte b) => this.bytes.Add(b);
            public void WriteFloat(float v) => this.bytes.AddRange(BitConverter.GetBytes(v));
            public void WriteInt(int v) => this.bytes.AddRange(BitConverter.GetBytes((uint)v));
            public void WriteUInt(uint v) => this.bytes.AddRange(BitConverter.GetBytes(v));
            public byte[] ToArray() => this.bytes.ToArray();
        }

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

        // ── Test 2: V2 → V3 migration ──────────────────────────────────────────

        [Test]
        public void V2ToV3_Migration_SetsMaterialRefToMinusOne_AndRepacksAsV3()
        {
            var data = ScriptableObject.CreateInstance<AuthoredInstancesData>();
            try
            {
                var buf = new ByteBuf();
                buf.WriteByte(2); // V2 version header

                // Record 0 — no collider (36 B header).
                WriteHeader(buf, new Vector3(1f, 0f, 0f), Quaternion.identity, 1f, InstanceOverrideMask.None);

                // Record 1 — collider with the OLD 16 B V2 block (no matRefIdx).
                WriteHeader(buf, new Vector3(2f, 0f, 0f), Quaternion.identity, 1f, InstanceOverrideMask.ColliderConfigured);
                buf.WriteInt(1);     // generateCollider
                buf.WriteInt(0);     // convex
                buf.WriteFloat(0.7f); // colliderScale
                buf.WriteInt(9);     // meshRefIndex

                SetBlob(data, buf.ToArray());
                ForceReloadFromBlob(data);
                var list = data.WorkingList;

                Assert.AreEqual(2, list.Count, "record count");
                Assert.AreEqual(-1, list[0].colliderMaterialRefIndex, "no-collider matRef defaults to -1");

                var col = list[1];
                Assert.IsTrue(col.generateCollider, "generateCollider preserved");
                Assert.IsFalse(col.colliderConvex, "convex preserved");
                Assert.AreEqual(0.7f, col.colliderScale, 1e-5f, "colliderScale preserved");
                Assert.AreEqual(9, col.colliderMeshRefIndex, "meshRefIndex preserved");
                Assert.AreEqual(-1, col.colliderMaterialRefIndex, "V2→V3 sets matRefIndex to -1");

                // Migration auto-promotes the blob to V3 (MigrateV2ToV3 calls PackBlob).
                byte[] promoted = GetBlob(data);
                Assert.AreEqual(3, promoted[0], "blob promoted to V3");
                Assert.AreEqual(1 + 36 + 56, promoted.Length, "V3 blob length: header + 36B + 56B");
            }
            finally { UnityEngine.Object.DestroyImmediate(data); }
        }

        // ── Test 3: V1 → V2 → V3 chain ─────────────────────────────────────────

        [Test]
        public void V1ToV3_Chain_DropsRenderer_SetsMaterialRefMinusOne()
        {
            var data = ScriptableObject.CreateInstance<AuthoredInstancesData>();
            try
            {
                const uint V1_COLLIDER_BIT = 1u << 0;
                const uint V1_RENDERER_BIT = 1u << 1;

                var buf = new ByteBuf();
                // V1 has NO version header. pos=(1,2,3) → first byte 0x00, so dispatch routes to V1.
                buf.WriteFloat(1f); buf.WriteFloat(2f); buf.WriteFloat(3f);          // position 12 B
                buf.WriteFloat(0f); buf.WriteFloat(0f); buf.WriteFloat(0f); buf.WriteFloat(1f); // rotation 16 B
                buf.WriteFloat(2f); buf.WriteFloat(2f); buf.WriteFloat(2f);          // V1 Vector3 scale 12 B (uniform → no collapse warning)
                buf.WriteUInt(V1_COLLIDER_BIT | V1_RENDERER_BIT);                     // mask 4 B
                // V1 collider block 12 B
                buf.WriteInt(1); // enabled
                buf.WriteInt(1); // convex
                buf.WriteInt(5); // meshRefIndex
                // V1 renderer block 12 B (dropped on migration)
                buf.WriteInt(2); buf.WriteInt(0); buf.WriteInt(0);

                SetBlob(data, buf.ToArray());
                ForceReloadFromBlob(data);

                LogAssert.Expect(LogType.Warning, new Regex("RendererOverride data dropped"));

                var list = data.WorkingList;
                Assert.AreEqual(1, list.Count, "record count");

                var r = list[0];
                Assert.AreEqual(InstanceOverrideMask.ColliderConfigured, r.overrideMask, "collider bit preserved");
                Assert.IsTrue(r.generateCollider, "generateCollider preserved");
                Assert.IsTrue(r.colliderConvex, "convex preserved");
                Assert.AreEqual(5, r.colliderMeshRefIndex, "meshRefIndex preserved");
                Assert.AreEqual(-1, r.colliderMaterialRefIndex, "V1→V3 sets matRefIndex to -1");
                Assert.AreEqual(2f, r.scale, 1e-5f, "uniform scale preserved");

                Assert.AreEqual(3, GetBlob(data)[0], "blob promoted to V3");
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

        // ── V2/V3 fixed-header writer (12+16+4+4 = 36 B) ────────────────────────

        private static void WriteHeader(ByteBuf buf, Vector3 pos, Quaternion rot, float scale, InstanceOverrideMask mask)
        {
            buf.WriteFloat(pos.x); buf.WriteFloat(pos.y); buf.WriteFloat(pos.z);
            buf.WriteFloat(rot.x); buf.WriteFloat(rot.y); buf.WriteFloat(rot.z); buf.WriteFloat(rot.w);
            buf.WriteFloat(scale);
            buf.WriteUInt((uint)mask);
        }
    }
}
