---
phase: 4
name: engine-integration
effort: L
agent: t1k-unity-developer
blocked-by: P1, P2, P3
blocks: P5
risk: HIGH (byte-layout change, score 20)
---

# Phase 4 - Engine Integration

## Goal

Replace procedural scatter for authored layers: GrassScatter.Build skips when HasAuthoredInstances==true and feeds AuthoredInstancesData straight into the baker. Add per-instance override-mask bit to ChunkedInstanceBuffer schema. Group-by-material draw split in MeshScatterEngine for renderer-override support.

**Highest-risk phase (score 20).** First task is the byte-stability harness. No engine edits ship until harness passes against the procedural baseline.

## Scope

IN: ChunkInstanceLayoutVerify harness (NEW); GrassScatter authored skip-path; ChunkedInstanceBuffer override-mask bit; MeshScatterEngine group-by-material draw split; 10-percent renderer-override warning in Inspector.

OUT: migration menu (P5); targetInstances deprecation (P5).

## File Ownership

| File | Action |
|---|---|
| Editor/ChunkInstanceLayoutVerify.cs | CREATE - harness asserts byte-stable output between procedural and authored-with-overrideMask=0 |
| Runtime/ChunkedInstanceBuffer.cs | EDIT - reserve override-mask bit in per-instance schema; preserve byte offset for procedural path |
| Runtime/GrassScatter.cs | EDIT - if layer.HasAuthoredInstances: skip RNG scatter; pump AuthoredInstancesData.Records straight into baker |
| Runtime/MeshScatterEngine.cs | EDIT - group instances by effective material (layer default + per-instance overrides); one RenderMeshIndirect per group; fast-path single group when zero overrides |
| Editor/TerrainScatterConfigEditor.cs | EDIT - compute override percentage; show HelpBox warning when >10 percent renderer-overridden |

## Step-by-Step Tasks (sequenced - harness FIRST)

1. **Capture procedural baseline** (FIRST, before any engine edits):
   - On demo layer with HasAuthoredInstances=false, run current GrassScatter.Build, dump ChunkedInstanceBuffer bytes to baseline-procedural.bin under plans/authored-instance-scatter-editor/baselines/.
   - Hash via SHA256 + record count + chunk count; stash in baselines/manifest.json.
2. **Author ChunkInstanceLayoutVerify**: editor harness that (a) runs GrassScatter.Build on a procedural layer, hashes output; (b) bakes an equivalent authored layer with same instance positions/rotations/scales and overrideMask=0 for all, hashes output; (c) asserts hashes equal. Failure prints byte-level diff (first 64 bytes of divergence).
3. **Run harness against baseline** before any edits - expect FAIL (no authored path exists yet). This proves the harness detects the change.
4. **Edit ChunkedInstanceBuffer schema**:
   - Reserve a bit-slot for overrideMask. Choose layout that preserves byte offset for procedural path (e.g., append at end; or pack into existing padding if one exists). MUST document chosen layout in a header comment with offset table.
   - When overrideMask=0, the byte output MUST equal pre-change procedural output. Verify via harness.
5. **Edit GrassScatter.Build**: at entry, if (layer.HasAuthoredInstances && layer.AuthoredInstances != null && layer.AuthoredInstances.Records.Length > 0): iterate Records and call the existing chunk-emit path with the authored TRS + overrideMask, then return. Skip the RNG-candidate loop + density-sample loop entirely.
6. **Re-run harness**: now expect PASS (authored bake with overrideMask=0 matches procedural baseline byte-for-byte for the same instances).
7. **MeshScatterEngine group-by-material draw split**:
   - Group authored records by effective material: layer default OR per-instance RendererOverride.Material.
   - Fast-path: if all records share layer default material (group count == 1), emit one RenderMeshIndirect (unchanged from procedural).
   - Slow-path: emit one RenderMeshIndirect per group; sub-range argsBuffer per group; same shadow + LOD pipeline.
   - Per-instance ShadowCastingMode override flows through standard Renderer API; group additionally split by shadow mode if needed (small constant 1-3 sub-groups).
8. **Render output validation**: ScatterInstanceCullHarness MUST PASS. Visual inspection in demo scene: a procedural-look authored layer renders identically to procedural; an authored layer with 1 renderer-overridden instance shows the override material correctly.
9. **10-percent warning UI**: in TerrainScatterConfigEditor.OnInspectorGUI, when HasAuthoredInstances, compute float overridePct = (records with RendererOverride bit set) / records.Length. If >0.10: EditorGUILayout.HelpBox(Renderer overrides on {pct:P} of instances - adds {n} draw calls per layer, MessageType.Warning).
10. **Cost sanity**: count RenderMeshIndirect calls before + after on demo with 0 percent overrides (expect SAME count) and with 15 percent overrides + 2 distinct override materials (expect +2 calls).

## Verification Gate (stricter than other phases)

1. refresh_unity + read_console: clean compile.
2. **ChunkInstanceLayoutVerify** (NEW): PASS - authored overrideMask=0 byte-equal to procedural baseline. **HARD GATE - phase does not ship without this.**
3. ScatterInstanceCullHarness: PASS unchanged.
4. Renderer-override visual: paint 1000 instances, set RendererOverride.Material on 5 of them with a distinct material; verify those 5 render with override material in demo scene.
5. Single-material fast-path: confirm draw-call count unchanged from procedural via Frame Debugger or RenderDoc capture.
6. Multi-material slow-path: 2 distinct override materials -> +2 RenderMeshIndirect calls, no more.
7. 10-percent warning UI fires at 11 percent, does not fire at 9 percent (manual scrub).
8. Screenshot: scene with mixed default + overridden materials, save screenshots/phase-4.png.
9. Asmdef boundary unchanged.

## Exit Criteria

- ChunkInstanceLayoutVerify PASS.
- ScatterInstanceCullHarness PASS.
- Procedural layers untouched (HasAuthoredInstances=false) render byte-identical to pre-P4 baseline.
- Authored layers with overrideMask=0 render byte-identical to equivalent procedural output.
- Authored layers with renderer overrides render correctly via group-by-material split.
- 10-percent warning fires.
- phase-4-report.md written including baseline SHA + post-P4 SHA + diff summary.

## Rollback Plan

- Revert ChunkedInstanceBuffer.cs to P3-end byte layout.
- Revert GrassScatter.cs to P3-end (drop authored skip-path; procedural always runs).
- Revert MeshScatterEngine.cs to P3-end (single RenderMeshIndirect path).
- Delete ChunkInstanceLayoutVerify.cs + meta.
- Sidecar data preserved (data model unchanged in P4); the engine simply does not consume it anymore.
- Rollback risk: MEDIUM. The Schema bit in ChunkedInstanceBuffer is the load-bearing change. If the bit was packed into existing padding rather than appended, reverting requires careful offset audit. **Mitigation: choose append-at-end layout for the override-mask bit to make rollback trivially safe.**

