#nullable enable
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Per-instance authored override flags packed into a uint32.
    /// Bit 0 = collider configuration present (ColliderConfigured).
    /// </summary>
    [Flags]
    public enum InstanceOverrideMask : uint
    {
        None               = 0,
        ColliderConfigured = 1 << 0,
    }

    /// <summary>
    /// V3 per-instance record (strict brainstorm Â§2, Phase A D1 final + Phase 1 PhysicMaterial).
    ///
    /// Fixed header (36 B):
    ///   Vector3 position      12 B
    ///   Quaternion rotation   16 B
    ///   float scale            4 B  (uniform â€” non-uniform V1 records collapse to average XYZ on migration)
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

        // â”€â”€ Optional collider fields (populated when ColliderConfigured bit is set) â”€

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

        // â”€â”€ Byte layout constants (V3) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
    /// <see cref="PropLayer"/>. Created automatically when a layer is first created.
    ///
    /// Storage: a 1-byte version header followed by a flat byte[] blob (binary, fast I/O) plus a
    /// List{Object} for object references (meshes) indexed from the blob.
    ///
    /// V3 blob layout: [VERSION_BYTE=3] + [records...]. A blob whose version byte is not 3 is
    /// rejected with an exception â€” legacy V1/V2 formats are no longer supported.
    ///
    /// â”€â”€ Per-tile bucket keying (Phase 1 extension) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// The <see cref="tileCoordKeys"/> + <see cref="tileBlobValues"/> parallel lists form a
    /// signed <see cref="Vector2Int"/> â†’ blob-segment index map for per-tile streaming.
    /// The V3 blob above remains the canonical global storage; tile buckets are an additive
    /// index layer â€” they do NOT duplicate instance data.
    ///
    /// Asmdef boundary: this file must NOT use UnityEditor. Editor helpers live in
    /// Editor/InstancePickingService.cs and Editor/TerrainScatterConfigEditor.cs.
    /// </summary>
    [CreateAssetMenu(menuName = "WorldPainter/Authored Instances Data", order = 100)]
    public sealed class AuthoredInstancesData : ScriptableObject, ISerializationCallbackReceiver
    {
        // â”€â”€ Blob version â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private const byte VERSION_BYTE = 3;

        // â”€â”€ Per-tile bucket keying (additive index, Phase 1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Tile coordinate keys for per-tile instance lookup.
        /// Parallel list with <see cref="tileBlobValues"/>.
        /// Each entry is a signed <see cref="Vector2Int"/> tile coordinate.
        /// </summary>
        [SerializeField] private List<Vector2Int> tileCoordKeys = new();

        /// <summary>
        /// Per-tile record index ranges (x = startIndex, y = count) into the working list.
        /// Parallel list with <see cref="tileCoordKeys"/>.
        /// Managed by the lifecycle API â€” do NOT hand-edit.
        /// </summary>
        [SerializeField] private List<Vector2Int> tileBlobValues = new();

        /// <summary>
        /// Returns the tile coord keys (read-only view). Use <see cref="RegisterTileBucket"/> /
        /// <see cref="UnregisterTileBucket"/> to mutate.
        /// </summary>
        public IReadOnlyList<Vector2Int> TileCoordKeys => this.tileCoordKeys;

        /// <summary>
        /// Registers a tile bucket index range (startIndex, count) for a given coord.
        /// Overwrites any existing entry for the same coord.
        /// </summary>
        public void RegisterTileBucket(Vector2Int coord, int startIndex, int count)
        {
            for (int i = 0; i < this.tileCoordKeys.Count; i++)
            {
                if (this.tileCoordKeys[i] == coord)
                {
                    this.tileBlobValues[i] = new Vector2Int(startIndex, count);
                    return;
                }
            }
            this.tileCoordKeys.Add(coord);
            this.tileBlobValues.Add(new Vector2Int(startIndex, count));
        }

        /// <summary>
        /// Removes the tile bucket entry for <paramref name="coord"/>. No-op if absent.
        /// </summary>
        public void UnregisterTileBucket(Vector2Int coord)
        {
            for (int i = this.tileCoordKeys.Count - 1; i >= 0; i--)
            {
                if (this.tileCoordKeys[i] == coord)
                {
                    this.tileCoordKeys.RemoveAt(i);
                    this.tileBlobValues.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Returns the (startIndex, count) range for <paramref name="coord"/>, or (-1,-1) if absent.
        /// </summary>
        public (int startIndex, int count) GetTileBucketRange(Vector2Int coord)
        {
            for (int i = 0; i < this.tileCoordKeys.Count; i++)
            {
                if (this.tileCoordKeys[i] == coord)
                {
                    var v = this.tileBlobValues[i];
                    return (v.x, v.y);
                }
            }
            return (-1, -1);
        }

        // â”€â”€ Serialized storage â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Tooltip("Binary blob encoding all InstanceRecord entries (V3 format). " +
                 "Managed by AuthoredInstancesData â€” do NOT hand-edit.")]
        [SerializeField] private byte[] blob = Array.Empty<byte>();

        [Tooltip("Object references (meshes) indexed from the byte blob's collider override blocks.")]
        [SerializeField] private List<UnityEngine.Object> objectRefs = new();

        // â”€â”€ Runtime cache â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private NativeArray<InstanceRecord> runtimeRecords;
        private bool runtimeDirty = true;

        // â”€â”€ Mutable list (working set for editor tools) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [System.NonSerialized]
        private List<InstanceRecord>? workingList;

        // â”€â”€ ISerializationCallbackReceiver â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // â”€â”€ Working list API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        /// Appends a new record to the working list (does NOT pack the blob â€” call
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

        // â”€â”€ P2 public API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // ── P7 per-tile TRS placement accessors ───────────────────────────────

        /// <summary>
        /// Appends a new <see cref="InstanceRecord"/> to the working list and updates the tile
        /// bucket index for <paramref name="tileCoord"/> so the record is included in the tile's
        /// (startIndex, count) range. Callers must call <see cref="PackBlob"/> before saving.
        /// </summary>
        public void AddRecordToTile(Vector2Int tileCoord, InstanceRecord record)
        {
            var list = this.WorkingList;
            (int startIdx, int existingCount) = this.GetTileBucketRange(tileCoord);

            if (startIdx < 0)
            {
                // No bucket yet — record starts a new contiguous run at the tail.
                int newStart = list.Count;
                list.Add(record);
                this.RegisterTileBucket(tileCoord, newStart, 1);
            }
            else
            {
                // Bucket exists — append and expand the count.
                list.Add(record);
                this.RegisterTileBucket(tileCoord, startIdx, existingCount + 1);
            }

            this.runtimeDirty = true;
        }

        /// <summary>
        /// Returns all <see cref="InstanceRecord"/>s for <paramref name="tileCoord"/>.
        /// Returns an empty list when the tile has no bucket.
        /// </summary>
        public List<InstanceRecord> GetTileRecords(Vector2Int tileCoord)
        {
            (int startIdx, int count) = this.GetTileBucketRange(tileCoord);
            var result = new List<InstanceRecord>(count > 0 ? count : 0);
            if (startIdx < 0 || count <= 0) return result;
            var list = this.WorkingList;
            int end = Mathf.Min(startIdx + count, list.Count);
            for (int i = startIdx; i < end; i++)
                result.Add(list[i]);
            return result;
        }

        /// <summary>
        /// Overwrites the record at absolute working-list index <paramref name="idx"/>.
        /// Returns false when the index is out of range.
        /// Used by Transform-edit mode to write back a moved/rotated/scaled instance.
        /// </summary>
        public bool TryUpdateRecord(int idx, InstanceRecord updated)
        {
            if (idx < 0 || idx >= this.WorkingList.Count) return false;
            this.WorkingList[idx] = updated;
            this.runtimeDirty = true;
            return true;
        }

        //â”€â”€ Object-ref helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // â”€â”€ Blob pack / unpack â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        /// <summary>Unpacks the V3 blob into a new List of InstanceRecords.</summary>
        private List<InstanceRecord> UnpackBlob()
        {
            if (this.blob == null || this.blob.Length == 0)
                return new List<InstanceRecord>();

            byte version = this.blob[0];
            if (version != VERSION_BYTE)
                throw new InvalidOperationException(
                    $"[AuthoredInstancesData] Unsupported blob version {version} on layer '{this.name}'. " +
                    $"Only V{VERSION_BYTE} is supported (legacy V1/V2 migration was removed).");

            return this.UnpackBlobV3();
        }

        // â”€â”€ V3 unpack â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // â”€â”€ CountFromBlob â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private int CountFromBlob()
        {
            if (this.blob == null || this.blob.Length == 0) return 0;

            byte version = this.blob[0];
            if (version != VERSION_BYTE)
                throw new InvalidOperationException(
                    $"[AuthoredInstancesData] Unsupported blob version {version} on layer '{this.name}'. " +
                    $"Only V{VERSION_BYTE} is supported (legacy V1/V2 migration was removed).");

            return this.CountFromBlobV3();
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

        // â”€â”€ Runtime NativeArray access â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // â”€â”€ Blob serialization helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
    }
}
