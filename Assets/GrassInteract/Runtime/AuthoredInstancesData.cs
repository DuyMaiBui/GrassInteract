#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Per-instance authored override flags packed into a uint32.
    /// Bit 0 = collider configuration present (ColliderConfigured).
    /// RendererOverride was removed in Phase A (D1 strict-V2).
    /// </summary>
    [Flags]
    public enum InstanceOverrideMask : uint
    {
        None              = 0,
        ColliderConfigured = 1 << 0,

        // Legacy V1 bit — used only during migration to detect and skip renderer blocks.
        // DO NOT use for new records.
        [Obsolete("V1 layout only — present during V1->V2 migration; not written to V2 blobs.")]
        ColliderOverride  = 1 << 0,  // same bit as ColliderConfigured (V1 compat name)
        [Obsolete("V1 layout only — removed in Phase A D1 strict-V2.")]
        RendererOverride  = 1 << 1,
    }

    /// <summary>
    /// V3 per-instance record (strict brainstorm §2, Phase A D1 final + Phase 1 PhysicMaterial).
    ///
    /// Fixed header (36 B):
    ///   Vector3 position      12 B
    ///   Quaternion rotation   16 B
    ///   float scale            4 B  (uniform — non-uniform V1 records collapse to average XYZ on migration)
    ///   uint overrideMask      4 B
    ///
    /// Optional ColliderConfigured block (20 B) appended when the ColliderConfigured bit is set:
    ///   bool generateCollider       1 B (stored as int32: 4 B)
    ///   bool colliderConvex         1 B (stored as int32: 4 B)
    ///   float colliderScale          4 B
    ///   int colliderMeshRefIndex     4 B
    ///   int colliderMaterialRefIndex 4 B
    ///
    /// Total: 36 B (no collider) or 56 B (with collider).
    ///
    /// RendererOverride is GONE per D1 strict-V2.
    /// </summary>
    [System.Serializable]
    public struct InstanceRecord
    {
        /// <summary>World-space position.</summary>
        public Vector3 position;

        /// <summary>World-space rotation.</summary>
        public Quaternion rotation;

        /// <summary>Uniform scale (non-uniform V1 records collapse to average XYZ on migration).</summary>
        public float scale;

        /// <summary>Bitmask of active overrides. See <see cref="InstanceOverrideMask"/>.</summary>
        public InstanceOverrideMask overrideMask;

        // ── Optional collider fields (populated when ColliderConfigured bit is set) ─

        /// <summary>Whether this instance spawns a collider.</summary>
        [System.NonSerialized] public bool generateCollider;

        /// <summary>Whether the collider is convex.</summary>
        [System.NonSerialized] public bool colliderConvex;

        /// <summary>Uniform collider scale multiplier (1 = match instance scale).</summary>
        [System.NonSerialized] public float colliderScale;

        /// <summary>
        /// Object-ref index into AuthoredInstancesData.objectRefs for the override collider mesh.
        /// -1 = use the layer's DefaultColliderMesh.
        /// </summary>
        [System.NonSerialized] public int colliderMeshRefIndex;

        /// <summary>
        /// Object-ref index into AuthoredInstancesData.objectRefs for the override collider PhysicMaterial.
        /// -1 = use the layer's DefaultColliderMaterial.
        /// </summary>
        [System.NonSerialized] public int colliderMaterialRefIndex;

        // ── Byte layout constants (V3) ────────────────────────────────────────

        /// <summary>Byte size of the V3 fixed header.</summary>
        public const int FIXED_BYTES = 36; // 12 + 16 + 4 + 4

        /// <summary>Byte size of the optional collider block (5 ints).</summary>
        public const int COLLIDER_BYTES = 20; // generateCollider(4) + colliderConvex(4) + colliderScale(4) + meshRefIdx(4) + matRefIdx(4)

        /// <summary>Returns the total byte size for this record given its override flags.</summary>
        public int ByteSize()
        {
            int size = FIXED_BYTES;
            if ((this.overrideMask & InstanceOverrideMask.ColliderConfigured) != 0)
                size += COLLIDER_BYTES;
            return size;
        }
    }

    /// <summary>
    /// Sub-asset ScriptableObject that stores all authored per-instance data for one
    /// <see cref="InstanceScatterLayer"/>. Created automatically when a layer is first created.
    ///
    /// Storage: a 1-byte version header followed by a flat byte[] blob (binary, fast I/O) plus a
    /// List{Object} for object references (meshes) indexed from the blob.
    ///
    /// V2 blob layout: [VERSION_BYTE=2] + [records...]. V1 blobs (no header / VERSION_BYTE != 2)
    /// are migrated in-memory on first read; the next SaveAssets writes a V2 blob.
    ///
    /// RendererOverride is REMOVED per Phase A D1 strict-V2. V1 RendererOverride blocks are
    /// silently dropped on migration with a one-shot per-layer Debug.LogWarning.
    ///
    /// Asmdef boundary: this file must NOT use UnityEditor. Editor helpers live in
    /// Editor/InstancePickingService.cs and Editor/TerrainScatterConfigEditor.cs.
    /// </summary>
    [CreateAssetMenu(menuName = "GrassInteract/Authored Instances Data", order = 100)]
    public sealed class AuthoredInstancesData : ScriptableObject, ISerializationCallbackReceiver
    {
        // ── Blob version ───────────────────────────────────────────────────────

        private const byte VERSION_BYTE = 3;

        // ── Serialized storage ─────────────────────────────────────────────────

        [Tooltip("Binary blob encoding all InstanceRecord entries (V2 format). " +
                 "Managed by AuthoredInstancesData — do NOT hand-edit.")]
        [SerializeField] private byte[] blob = Array.Empty<byte>();

        [Tooltip("Object references (meshes) indexed from the byte blob's collider override blocks.")]
        [SerializeField] private List<UnityEngine.Object> objectRefs = new();

        // ── Runtime cache ──────────────────────────────────────────────────────

        private NativeArray<InstanceRecord> runtimeRecords;
        private bool runtimeDirty = true;

        // ── Mutable list (working set for editor tools) ────────────────────────

        [System.NonSerialized]
        private List<InstanceRecord>? workingList;

        // ── ISerializationCallbackReceiver ─────────────────────────────────────

        /// <summary>Called by Unity before serialization. Packs the working list into the blob.</summary>
        public void OnBeforeSerialize()
        {
            if (this.workingList != null)
                this.PackBlob();
        }

        /// <summary>Called by Unity after deserialization. Marks runtime cache dirty.</summary>
        public void OnAfterDeserialize()
        {
            this.runtimeDirty = true;
            if (this.runtimeRecords.IsCreated)
                this.runtimeRecords.Dispose();
        }

        private void OnDestroy()
        {
            if (this.runtimeRecords.IsCreated)
                this.runtimeRecords.Dispose();
        }

        // ── Working list API ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the mutable working list of instance records. Populated lazily from the blob on
        /// first access. Editor tools operate on this list.
        /// </summary>
        public List<InstanceRecord> WorkingList
        {
            get
            {
                if (this.workingList == null)
                    this.workingList = this.UnpackBlob();
                return this.workingList;
            }
        }

        /// <summary>Total authored instance count (reads working list if available, else blob).</summary>
        public int Count => this.workingList != null ? this.workingList.Count : this.CountFromBlob();

        /// <summary>
        /// Appends a new record to the working list (does NOT pack the blob — call
        /// <see cref="PackBlob"/> before saving).
        /// </summary>
        public void AddRecord(InstanceRecord record)
        {
            this.WorkingList.Add(record);
            this.runtimeDirty = true;
        }

        /// <summary>
        /// Removes the record at the given index via swap-pop (O(1), does not preserve order).
        /// </summary>
        public void RemoveRecordSwapPop(int index)
        {
            var list = this.WorkingList;
            if (index < 0 || index >= list.Count) return;
            int last = list.Count - 1;
            if (index != last)
                list[index] = list[last];
            list.RemoveAt(last);
            this.runtimeDirty = true;
        }

        // ── P2 public API ─────────────────────────────────────────────────────

        /// <summary>Tries to read the record at <paramref name="idx"/>. Returns false if out-of-range.</summary>
        public bool TryGetRecord(int idx, out InstanceRecord rec)
        {
            var list = this.WorkingList;
            if (idx < 0 || idx >= list.Count) { rec = default; return false; }
            rec = list[idx];
            return true;
        }

        /// <summary>Overwrites the record at <paramref name="idx"/> and marks the sidecar dirty.</summary>
        public void SetRecord(int idx, InstanceRecord rec)
        {
            var list = this.WorkingList;
            if (idx < 0 || idx >= list.Count) return;
            list[idx] = rec;
            this.runtimeDirty = true;
        }

        /// <summary>
        /// Sets or clears the collider configuration for the record at <paramref name="idx"/>.
        /// Passing a record with generateCollider=false AND colliderMeshRefIndex=-1 effectively clears the block.
        /// </summary>
        public void SetColliderConfig(int idx, bool generateCollider, bool colliderConvex,
            float colliderScale, int colliderMeshRefIndex, int colliderMaterialRefIndex = -1)
        {
            var list = this.WorkingList;
            if (idx < 0 || idx >= list.Count) return;
            var rec = list[idx];
            rec.overrideMask          |= InstanceOverrideMask.ColliderConfigured;
            rec.generateCollider       = generateCollider;
            rec.colliderConvex         = colliderConvex;
            rec.colliderScale          = colliderScale;
            rec.colliderMeshRefIndex   = colliderMeshRefIndex;
            rec.colliderMaterialRefIndex = colliderMaterialRefIndex;
            list[idx] = rec;
            this.runtimeDirty = true;
        }

        /// <summary>Clears the collider configuration block on the record at <paramref name="idx"/>.</summary>
        public void ClearColliderConfig(int idx)
        {
            var list = this.WorkingList;
            if (idx < 0 || idx >= list.Count) return;
            var rec = list[idx];
            rec.overrideMask           &= ~InstanceOverrideMask.ColliderConfigured;
            rec.generateCollider        = false;
            rec.colliderConvex          = false;
            rec.colliderScale           = 1f;
            rec.colliderMeshRefIndex    = -1;
            rec.colliderMaterialRefIndex = -1;
            list[idx] = rec;
            this.runtimeDirty = true;
        }

        /// <summary>Removes the record at <paramref name="idx"/> via swap-pop (O(1), no order preserved).</summary>
        public void RemoveAt(int idx) => this.RemoveRecordSwapPop(idx);

        /// <summary>
        /// Batch-overwrites a list of (index, record) pairs and marks the sidecar dirty once.
        /// </summary>
        public void SetRecords(IList<(int idx, InstanceRecord rec)> edits)
        {
            var list = this.WorkingList;
            int count = list.Count;
            foreach (var (idx, rec) in edits)
            {
                if (idx < 0 || idx >= count) continue;
                list[idx] = rec;
            }
            this.runtimeDirty = true;
        }

        // ── Object-ref helpers ─────────────────────────────────────────────────

        /// <summary>Returns the object at <paramref name="refIndex"/> in objectRefs, or null.</summary>
        public UnityEngine.Object? GetObjectRef(int refIndex)
        {
            if (refIndex < 0 || refIndex >= this.objectRefs.Count) return null;
            return this.objectRefs[refIndex];
        }

        /// <summary>Ensures <paramref name="obj"/> is in objectRefs and returns its index.</summary>
        public int EnsureObjectRef(UnityEngine.Object obj)
        {
            for (int i = 0; i < this.objectRefs.Count; ++i)
                if (this.objectRefs[i] == obj) return i;
            this.objectRefs.Add(obj);
            return this.objectRefs.Count - 1;
        }

        // ── Blob pack / unpack ────────────────────────────────────────────────

        /// <summary>
        /// Packs the working list into the serialized blob (V3 format with version header byte).
        /// Called automatically in <see cref="OnBeforeSerialize"/> and explicitly by editor tools
        /// before <c>SetDirty</c>.
        /// </summary>
        public void PackBlob()
        {
            var list = this.workingList;
            if (list == null)
            {
                this.blob = Array.Empty<byte>();
                return;
            }

            // First pass: calculate total byte count (1 byte version header + record data).
            int totalBytes = 1; // VERSION_BYTE
            foreach (var rec in list)
                totalBytes += rec.ByteSize();

            var buf = new byte[totalBytes];
            int offset = 0;

            // Write V3 version header.
            buf[offset++] = VERSION_BYTE;

            foreach (var rec in list)
            {
                // position (12 B)
                WriteFloat(buf, ref offset, rec.position.x);
                WriteFloat(buf, ref offset, rec.position.y);
                WriteFloat(buf, ref offset, rec.position.z);
                // rotation (16 B)
                WriteFloat(buf, ref offset, rec.rotation.x);
                WriteFloat(buf, ref offset, rec.rotation.y);
                WriteFloat(buf, ref offset, rec.rotation.z);
                WriteFloat(buf, ref offset, rec.rotation.w);
                // scale float (4 B)
                WriteFloat(buf, ref offset, rec.scale);
                // overrideMask (4 B)
                WriteUInt(buf, ref offset, (uint)rec.overrideMask);

                // Optional collider block (20 B) when ColliderConfigured bit is set.
                if ((rec.overrideMask & InstanceOverrideMask.ColliderConfigured) != 0)
                {
                    WriteInt(buf, ref offset, rec.generateCollider ? 1 : 0);
                    WriteInt(buf, ref offset, rec.colliderConvex ? 1 : 0);
                    WriteFloat(buf, ref offset, rec.colliderScale);
                    WriteInt(buf, ref offset, rec.colliderMeshRefIndex);
                    WriteInt(buf, ref offset, rec.colliderMaterialRefIndex);
                }
            }

            this.blob = buf;
        }

        /// <summary>Unpacks the blob into a new List of InstanceRecords. Handles V1→V2→V3 migration.</summary>
        private List<InstanceRecord> UnpackBlob()
        {
            if (this.blob == null || this.blob.Length == 0)
                return new List<InstanceRecord>();

            // Dispatch on version byte: 3=V3, 2=V2→V3 migration, else=V1→V2→V3 chain.
            byte version = this.blob[0];
            if (version == 3)
                return this.UnpackBlobV3();
            else if (version == 2)
                return this.MigrateV2ToV3();
            else
                return this.MigrateV1ToV3();
        }

        // ── V3 unpack ─────────────────────────────────────────────────────────

        private List<InstanceRecord> UnpackBlobV3()
        {
            var list = new List<InstanceRecord>();
            int offset = 1; // skip version byte

            while (offset + InstanceRecord.FIXED_BYTES <= this.blob.Length)
            {
                var rec = new InstanceRecord
                {
                    position = new Vector3(
                        ReadFloat(this.blob, ref offset),
                        ReadFloat(this.blob, ref offset),
                        ReadFloat(this.blob, ref offset)),
                    rotation = new Quaternion(
                        ReadFloat(this.blob, ref offset),
                        ReadFloat(this.blob, ref offset),
                        ReadFloat(this.blob, ref offset),
                        ReadFloat(this.blob, ref offset)),
                    scale       = ReadFloat(this.blob, ref offset),
                    overrideMask = (InstanceOverrideMask)ReadUInt(this.blob, ref offset),
                };

                if ((rec.overrideMask & InstanceOverrideMask.ColliderConfigured) != 0)
                {
                    rec.generateCollider       = ReadInt(this.blob, ref offset) != 0;
                    rec.colliderConvex         = ReadInt(this.blob, ref offset) != 0;
                    rec.colliderScale          = ReadFloat(this.blob, ref offset);
                    rec.colliderMeshRefIndex   = ReadInt(this.blob, ref offset);
                    rec.colliderMaterialRefIndex = ReadInt(this.blob, ref offset);
                }
                else
                {
                    rec.colliderScale          = 1f;
                    rec.colliderMeshRefIndex   = -1;
                    rec.colliderMaterialRefIndex = -1;
                }

                list.Add(rec);
            }
            return list;
        }

        // ── V2 → V3 migration (reads old 16 B collider block, sets matRefIdx=-1) ─

        private List<InstanceRecord> MigrateV2ToV3()
        {
            var list = new List<InstanceRecord>();
            int offset = 1; // skip version byte
            int colliderCount = 0;

            while (offset + InstanceRecord.FIXED_BYTES <= this.blob.Length)
            {
                var rec = new InstanceRecord
                {
                    position = new Vector3(
                        ReadFloat(this.blob, ref offset),
                        ReadFloat(this.blob, ref offset),
                        ReadFloat(this.blob, ref offset)),
                    rotation = new Quaternion(
                        ReadFloat(this.blob, ref offset),
                        ReadFloat(this.blob, ref offset),
                        ReadFloat(this.blob, ref offset),
                        ReadFloat(this.blob, ref offset)),
                    scale       = ReadFloat(this.blob, ref offset),
                    overrideMask = (InstanceOverrideMask)ReadUInt(this.blob, ref offset),
                };

                if ((rec.overrideMask & InstanceOverrideMask.ColliderConfigured) != 0)
                {
                    // V2 collider block: 4 ints (16 B) — no matRefIdx.
                    rec.generateCollider       = ReadInt(this.blob, ref offset) != 0;
                    rec.colliderConvex         = ReadInt(this.blob, ref offset) != 0;
                    rec.colliderScale          = ReadFloat(this.blob, ref offset);
                    rec.colliderMeshRefIndex   = ReadInt(this.blob, ref offset);
                    rec.colliderMaterialRefIndex = -1;
                    colliderCount++;
                }
                else
                {
                    rec.colliderScale          = 1f;
                    rec.colliderMeshRefIndex   = -1;
                    rec.colliderMaterialRefIndex = -1;
                }

                list.Add(rec);
            }

            if (colliderCount > 0)
            {
                Debug.LogWarning(
                    $"[AuthoredInstancesData] Migrated {colliderCount} V2 collider records on layer " +
                    $"'{this.name}' to V3; colliderMaterialRefIndex set to -1 (layer default).");
            }

            // Promote working list to V3 so next OnBeforeSerialize writes V3 blob.
            this.workingList = list;
            this.PackBlob();
            return list;
        }

        // ── V1 → V2 → V3 chain (reuse V1→V2 logic, then promote to V3) ─

        private List<InstanceRecord> MigrateV1ToV3()
        {
            var list = this.MigrateV1ToV2();
            // Promote to V3 in-memory so next save writes V3.
            this.workingList = list;
            this.PackBlob();
            return list;
        }

        // ── V1 → V2 migration ──────────────────────────────────────────────────

        // V1 fixed header: position(12) + rotation(16) + Vector3 scale(12) + overrideMask(4) = 44 B
        private const int V1_FIXED_BYTES    = 44;
        private const int V1_COLLIDER_BYTES = 12; // enabled(4) + convex(4) + meshRefIndex(4)
        private const int V1_RENDERER_BYTES = 12; // materialRefIndex(4) + shadowMode(4) + padding(4)

        // V1 RendererOverride bit (bit 1).
        private const uint V1_RENDERER_BIT = 1u << 1;
        // V1 ColliderOverride bit (bit 0).
        private const uint V1_COLLIDER_BIT = 1u << 0;

        private List<InstanceRecord> MigrateV1ToV2()
        {
            var list          = new List<InstanceRecord>();
            var rendererDropped = new List<int>();
            var nonUniformCollapsed = new List<int>();
            int offset = 0;
            int recordIdx = 0;

            while (offset + V1_FIXED_BYTES <= this.blob.Length)
            {
                // Read V1 fixed header.
                float px = ReadFloat(this.blob, ref offset);
                float py = ReadFloat(this.blob, ref offset);
                float pz = ReadFloat(this.blob, ref offset);
                float rx = ReadFloat(this.blob, ref offset);
                float ry = ReadFloat(this.blob, ref offset);
                float rz = ReadFloat(this.blob, ref offset);
                float rw = ReadFloat(this.blob, ref offset);
                float sx = ReadFloat(this.blob, ref offset);
                float sy = ReadFloat(this.blob, ref offset);
                float sz = ReadFloat(this.blob, ref offset);
                uint  v1Mask = ReadUInt(this.blob, ref offset);

                // Collapse non-uniform scale to uniform (average XYZ).
                float uniformScale = (sx + sy + sz) / 3f;
                if (Mathf.Abs(Mathf.Max(sx, Mathf.Max(sy, sz)) - Mathf.Min(sx, Mathf.Min(sy, sz))) > 0.001f)
                    nonUniformCollapsed.Add(recordIdx);

                var rec = new InstanceRecord
                {
                    position = new Vector3(px, py, pz),
                    rotation = new Quaternion(rx, ry, rz, rw),
                    scale    = uniformScale,
                };

                // V1 ColliderOverride (bit 0) → V2 ColliderConfigured.
                if ((v1Mask & V1_COLLIDER_BIT) != 0 && offset + V1_COLLIDER_BYTES <= this.blob.Length)
                {
                    bool colEnabled   = ReadInt(this.blob, ref offset) != 0;
                    bool colConvex    = ReadInt(this.blob, ref offset) != 0;
                    int  meshRefIndex = ReadInt(this.blob, ref offset);

                    rec.overrideMask       = InstanceOverrideMask.ColliderConfigured;
                    rec.generateCollider   = colEnabled;
                    rec.colliderConvex     = colConvex;
                    rec.colliderScale      = 1f;
                    rec.colliderMeshRefIndex = meshRefIndex;
                    rec.colliderMaterialRefIndex = -1; // V1 had no PhysicMaterial → layer default
                }
                else
                {
                    rec.overrideMask         = InstanceOverrideMask.None;
                    rec.colliderScale        = 1f;
                    rec.colliderMeshRefIndex = -1;
                    rec.colliderMaterialRefIndex = -1;
                }

                // V1 RendererOverride (bit 1) → skip 12 bytes, record the drop.
                if ((v1Mask & V1_RENDERER_BIT) != 0 && offset + V1_RENDERER_BYTES <= this.blob.Length)
                {
                    offset += V1_RENDERER_BYTES; // skip materialRefIndex + shadowMode + padding
                    rendererDropped.Add(recordIdx);
                }

                list.Add(rec);
                recordIdx++;
            }

            // Emit one-shot warnings.
            if (rendererDropped.Count > 0)
            {
                Debug.LogWarning(
                    $"[AuthoredInstancesData] Migrated {rendererDropped.Count} V1 records on layer " +
                    $"'{this.name}'; RendererOverride data dropped on indices " +
                    $"[{JoinInts(rendererDropped)}] per D1 strict-V2.");
            }
            if (nonUniformCollapsed.Count > 0)
            {
                Debug.LogWarning(
                    $"[AuthoredInstancesData] Migrated {nonUniformCollapsed.Count} V1 records on layer " +
                    $"'{this.name}'; non-uniform scale collapsed to uniform (average XYZ) on indices " +
                    $"[{JoinInts(nonUniformCollapsed)}] per D1 strict-V2 §2.");
            }

            return list;
        }

        // ── CountFromBlob ────────────────────────────────────────────────────

        private int CountFromBlob()
        {
            if (this.blob == null || this.blob.Length == 0) return 0;

            byte version = this.blob[0];
            if (version == 3)
                return this.CountFromBlobV3();
            else if (version == 2)
                return this.CountFromBlobV2();
            else
                return this.CountFromBlobV1();
        }

        private int CountFromBlobV3()
        {
            int offset = 1, count = 0; // skip version byte
            while (offset + InstanceRecord.FIXED_BYTES <= this.blob.Length)
            {
                // overrideMask is at offset 32 within each record (12 pos + 16 rot + 4 scale = 32).
                uint mask = ReadUIntAt(this.blob, offset + 32);
                int size  = InstanceRecord.FIXED_BYTES;
                if ((mask & (uint)InstanceOverrideMask.ColliderConfigured) != 0)
                    size += InstanceRecord.COLLIDER_BYTES;
                if (offset + size > this.blob.Length) break;
                offset += size;
                count++;
            }
            return count;
        }

        private int CountFromBlobV2()
        {
            int offset = 1, count = 0; // skip version byte
            while (offset + InstanceRecord.FIXED_BYTES <= this.blob.Length)
            {
                // overrideMask is at offset 32 within each record (12 pos + 16 rot + 4 scale = 32).
                uint mask = ReadUIntAt(this.blob, offset + 32);
                int size  = InstanceRecord.FIXED_BYTES;
                if ((mask & (uint)InstanceOverrideMask.ColliderConfigured) != 0)
                    size += 16; // V2 collider block was 16 B (4 ints, no matRefIdx)
                if (offset + size > this.blob.Length) break;
                offset += size;
                count++;
            }
            return count;
        }

        private int CountFromBlobV1()
        {
            int offset = 0, count = 0;
            while (offset + V1_FIXED_BYTES <= this.blob.Length)
            {
                uint mask = ReadUIntAt(this.blob, offset + 40); // V1: overrideMask at byte 40
                int size  = V1_FIXED_BYTES;
                if ((mask & V1_COLLIDER_BIT)  != 0) size += V1_COLLIDER_BYTES;
                if ((mask & V1_RENDERER_BIT)  != 0) size += V1_RENDERER_BYTES;
                if (offset + size > this.blob.Length) break;
                offset += size;
                count++;
            }
            return count;
        }

        // ── Runtime NativeArray access ────────────────────────────────────────

        /// <summary>
        /// Returns a NativeArray view of all authored instance records (Allocator.Persistent).
        /// Rebuilt when the blob has changed since last access.
        /// </summary>
        public NativeArray<InstanceRecord> GetRuntimeRecords()
        {
            if (!this.runtimeDirty && this.runtimeRecords.IsCreated)
                return this.runtimeRecords;

            if (this.runtimeRecords.IsCreated)
                this.runtimeRecords.Dispose();

            var list = this.workingList ?? this.UnpackBlob();
            this.runtimeRecords = new NativeArray<InstanceRecord>(
                list.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < list.Count; ++i)
                this.runtimeRecords[i] = list[i];
            this.runtimeDirty = false;
            return this.runtimeRecords;
        }

        // ── Blob serialization helpers ─────────────────────────────────────────

        private static void WriteFloat(byte[] buf, ref int offset, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            buf[offset++] = bytes[0];
            buf[offset++] = bytes[1];
            buf[offset++] = bytes[2];
            buf[offset++] = bytes[3];
        }

        private static void WriteInt(byte[] buf, ref int offset, int value) =>
            WriteUInt(buf, ref offset, (uint)value);

        private static void WriteUInt(byte[] buf, ref int offset, uint value)
        {
            buf[offset++] = (byte)(value & 0xFF);
            buf[offset++] = (byte)((value >> 8) & 0xFF);
            buf[offset++] = (byte)((value >> 16) & 0xFF);
            buf[offset++] = (byte)((value >> 24) & 0xFF);
        }

        private static float ReadFloat(byte[] buf, ref int offset)
        {
            float v = BitConverter.ToSingle(buf, offset);
            offset += 4;
            return v;
        }

        private static int ReadInt(byte[] buf, ref int offset) => (int)ReadUInt(buf, ref offset);

        private static uint ReadUInt(byte[] buf, ref int offset)
        {
            uint v = ReadUIntAt(buf, offset);
            offset += 4;
            return v;
        }

        private static uint ReadUIntAt(byte[] buf, int offset) =>
            (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));

        private static string JoinInts(List<int> items)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count; ++i)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(items[i]);
            }
            return sb.ToString();
        }
    }
}
