---
title: Authored Per-Instance Scatter — Editor Layer Design
date: 2026-06-04
status: design-approved
scope: editor-layer (storage + runtime engine handoff covered as out-of-scope follow-ups)
---

# Authored Per-Instance Scatter Editor — Brainstorm Report

## Problem Statement

The current `ScatterLayer` is fully **procedural**: `TargetInstances` candidates are scattered each rebuild via a seed-deterministic RNG, accepted/rejected by density-map sampling, and given random yaw/scale within layer ranges. Per-instance customization is impossible — every instance derives from layer-wide config.

**User goal:**
1. Remove `TargetInstances` field.
2. Place instances **like Unity Terrain Detail tool** (density-paint spawns instances directly).
3. Click any instance in the Scene view to **enter focused edit mode** (Inspector-like panel) with per-instance overrides for transform, collider, and renderer.
4. Multi-instance brush-edit for batch tweaks (rotation, scale, position, normal-align).
5. Keep GPU indirect render path.

## Requirements

| # | Requirement |
|---|---|
| R1 | Removing `TargetInstances` does not break existing scenes — one-shot bake migration available. |
| R2 | Density map persists, but its role changes: from count-multiplier to **placement mask** (where Place brush is allowed). |
| R3 | Click-pick a single instance in Scene view → wireframe outline + transform gizmo + Inspector panel. |
| R4 | Per-instance overrides for Transform (always), Collider (enabled / convex / custom mesh), Renderer (material / shadow mode). Unselected = inherit layer default. |
| R5 | Brush-edit supports randomize-rotation, nudge-scale, nudge-position, align-to-normal toggle, with falloff weighting. |
| R6 | Storage: binary sidecar asset per layer. Target scale 10k–500k instances/layer. |
| R7 | Drop seed-based determinism for authored layers — authoring IS the source of truth. |
| R8 | Keep GPU indirect render (`RenderMeshIndirect`) downstream. |
| R9 | Undo supported per brush stroke + per gizmo drag.

## Approaches Considered

### A. Pure-authored, click-to-place (Terrain Tree tool style)
Each click = one instance. Density map = placement mask only. Brush stamps N instances over an area at configured spacing.

- ✔ Simplest mental model.
- ✖ Slow for dense fields (50k blades by hand is unreasonable).
- ✖ Brush-paint variant is mostly identical to (B); little net difference.

### B. Density-paints-instances (Terrain Detail tool style) — **CHOSEN**
Brush paints density → spawns instances at deterministic spacing within painted area. Density map drives spawn count, NOT a `TargetInstances` field. Per-instance authored overrides are stored as a separate layer.

- ✔ Matches Unity's well-known Detail workflow (familiar to artists).
- ✔ Dense-field-friendly (one stroke = thousands of instances).
- ✔ Erase + brush-edit operations work naturally over the same brush model.
- ✖ Density-map and authored-instance-list become dual SSOT — needs careful documentation.

### C. Hybrid — procedural base + sparse overrides
Procedural scatter still runs each rebuild from a seed; overrides stored as a sparse dict `{instanceIndex → tweaks}`. Re-seeding remaps by closest-position.

- ✔ Preserves seed harness validation.
- ✖ Override identity is fragile across re-scatters (heuristic remap → flickers/drift).
- ✖ Two SSOT systems running in lockstep (seed RNG + override dict).
- ✖ Rejected by user.

**Decision:** Approach **B**. Density-paints-instances with authored overrides as the canonical layer model. Drop seed determinism for authored layers (R7).

## Recommended Solution (Editor Layer)

### Tool model — 4 modes + Off, one toolbar

The ScatterField Inspector hosts a tool toolbar (replaces today's Paint / Erase / Off):

| Tool | Action | Inputs |
|---|---|---|
| **Place** | Brush-paint spawns instances inside radius; density map = placement mask; spacing param controls candidates/m² | density map (mask), spacing |
| **Erase** | Brush-paint removes instances inside radius | — |
| **Edit Single** | Click picks one instance → wireframe overlay + transform gizmo + focused Inspector panel | — |
| **Edit Brush** | Brush re-randomizes rot / scale / position / align-normal across touched instances (falloff-weighted) | per-op param block |
| **Off** | Normal scene selection | — |

Brush parameters (radius, opacity, falloff, stamp, density overlay) shared across Place / Erase / Edit-Brush. Edit-Single uses cursor pick, not brush.

### Picking pipeline (Edit Single)

1. On layer rebuild, build CPU **spatial hash** `cellId → List<instanceIdx>` (cell size = layer.ChunkSize, reuses existing baker grid).
2. OnSceneGUI mouse: cast ray, walk cells along ray, test ray-vs-bounding-sphere (`r = mesh.bounds.extents.magnitude * scale`), return nearest by `t`.
3. Picked index → focused panel + wireframe overlay.

**Cost:** 100k instances at chunk=16m → ~50 inst/cell, ≤10 cells per ray → ~500 sphere tests/frame.

### Selection visualization
- **Wireframe**: `Graphics.DrawMesh` (or `Handles.DrawWireMesh`) of layer LOD0 mesh at picked TRS, in `OnSceneGUI`. No shader change.
- **Transform gizmo**: standard `Handles.PositionHandle / RotationHandle / ScaleHandle` at picked pos. Drag → update sidecar entry + single-instance fast-path rebuild.

### Focused Inspector panel (Edit Single)

Renders below the toolbar:

```
▼ Instance #4732
  Transform
    Position    [x] [y] [z]
    Rotation    [x] [y] [z]   (Euler ⇄ internal Quaternion)
    Scale       [x] [y] [z]
  Collider                    [✓] Override
    Enabled       [✓]
    Convex        [ ]
    Mesh          [ColliderMesh asset slot]
  Renderer                    [✓] Override
    Material      [Material slot]   ⚠ adds a draw call
    Shadow Mode   [Off / On / Two Sided]
  [Delete Instance]
```

Override checkboxes gate each block. Unchecked = layer default (greyed values).

### Brush-edit operations (Edit Brush)

Same spatial-hash walk applied to all instances inside radius, with falloff weight `w ∈ [0,1]`:

| Op | Behavior |
|---|---|
| Randomize rotation | Re-roll yaw (+ pitch/roll if oriented), `lerp(current, new, w)` |
| Nudge / randomize scale | Re-roll within `layer.ScaleRange` OR additive `±delta * w` |
| Nudge position | Random XZ jitter within `nudgeRadius * w`; re-snap to ground via existing `ISurfaceSampler` |
| Align-to-normal toggle | Set per-instance `aligned` flag; rot resampled from surface normal |

Op choice = radio group inside brush block. Stroke = single Undo step.

### Data model (binary sidecar)

`AuthoredInstancesData` sub-asset of the layer:

```csharp
public sealed class AuthoredInstancesData : ScriptableObject, ISerializationCallbackReceiver {
    [SerializeField, HideInInspector] private byte[] blob;
    [SerializeField] private List<Object> refs;   // material / mesh resolve table
    [NonSerialized] public NativeArray<InstanceRecord> Records;
    // ...
}
```

Per-instance schema (variable-size):

```
Vector3   pos              12B
Quaternion rot             16B
Vector3   scale            12B
uint32    overrideMask      4B   (bits: hasCollider, hasRenderer, aligned, ...)
[optional ColliderOverride] 12B  (enabled+convex flags + meshRef idx)
[optional RendererOverride] 12B  (materialRef idx + shadowMode)
─────────────────────────  44–68B/inst
```

100k instances ≈ 5 MB sidecar. Comfortable.

### Undo
- `Undo.RegisterCompleteObjectUndo(sidecar, "Paint Stroke")` once per stroke (mouse-down → mouse-up). 5 MB / stroke at 100k. Desktop-fine.
- Edit-Single gizmo: `Undo.RecordObject` per drag-end.

### Reuses (no rewrite)
- `ScatterBrush` — flush/throttle/cursor/falloff/density overlay reused. Place mode swaps "write density texel" for "append InstanceRecord".
- `TerrainScatterConfigEditor` — tab / PropertyTree / OnSceneGUI plumbing reused.
- Density map — keeps `Texture2D` role as a paintable MASK.
- Existing `ChunkedInstanceBuffer` / `ChunkedBladeBuffer` upload path — engines iterate authored list instead of running `GrassScatter.Build`.

## Out-of-scope (next brainstorm / plan-driven)

These are not editor concerns but must be sequenced after editor work or in parallel:

1. **Engine integration** — `GrassScatter.Build` skip path when `layer.HasAuthoredInstances == true`; feed authored list straight into baker.
2. **`ChunkedInstanceBuffer` schema** — add per-instance override-mask bit (for renderer-override draw split).
3. **Material-override draw split** — group instances by material, one `RenderMeshIndirect` per group.
4. **Migration tool** — "Bake Procedural Layer → Authored" menu (runs current scatter once, writes result to sidecar, flips `HasAuthoredInstances = true`).
5. **`TargetInstances` removal** — `[FormerlySerializedAs]` for one release cycle, then drop.

## Risks & Trade-offs

| Risk | Mitigation |
|---|---|
| Renderer override breaks single-draw indirect | Group-by-material; warn user when >10% of instances override |
| Wireframe selection adds a CPU draw per frame | Only active when an instance is selected — acceptable |
| Whole-blob undo step at 100k+ instances (5 MB) | Acceptable for desktop; defer per-cell delta undo until >500k |
| Density-map vs authored-list dual SSOT | Document the role-change clearly; tooltip in inspector |
| Per-instance LOD override NOT exposed | User explicitly excluded; easy to add later |
| Migration of existing demo layer (procedural) | Bake-to-authored menu = one-shot conversion |

## Success Metrics & Validation

| Metric | Target |
|---|---|
| Place-brush throughput | ≥ 5000 instances/sec stamp (desktop) |
| Single-instance pick latency | < 16 ms (1 frame) at 100k instances |
| Brush-edit (rotate 1000 instances) | < 50 ms / stroke |
| Sidecar file size | ≤ 6 MB at 100k instances |
| Undo step memory | ≤ 6 MB / stroke at 100k instances |
| Existing demo layer migrates cleanly | `Bake to Authored` produces visually-identical result |
| GPU render path unchanged | Existing `ScatterInstanceCullHarness` still PASS for authored layers |

## Phasing (proposed)

1. **P1 Editor scaffolding** — toolbar (5 modes), sidecar SO, spatial hash. Place + Erase functional. (~1–2 days)
2. **P2 Edit Single** — picking, wireframe, transform gizmo, focused Inspector with override checkboxes. (~1–2 days)
3. **P3 Edit Brush** — rotate / scale / position / align ops with falloff. (~1 day)
4. **P4 Engine integration** — bypass `GrassScatter` on authored, group-by-material draw split. (~2 days)
5. **P5 Migration + `TargetInstances` removal** — bake menu, deprecation, asset migrator. (~0.5 day)

## Next Steps

Hand off to `/t1k:plan` to expand the phases into ordered tasks with file-ownership boundaries and approval gates.
