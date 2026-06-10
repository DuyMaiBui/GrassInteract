# Brainstorm — Scatter LOD: explicit cull distance + draggable distance bar

Date: 2026-06-10 · Status: design approved → plan next

## Problem statement
Two linked requests on the scatter LOD system:
1. **Bug** — `InstanceScatterLayer` LOD distance set to 500 still culls at a distance ≠ 500.
2. **UX** — add a Unity-LODGroup-style segmented distance bar (reference image: LOD0/LOD1/LOD2/Culled with % labels), with draggable boundaries.

Both share one root cause: the data model has **no explicit cull boundary**.

## Root cause (confirmed in code)
For `InstanceScatterLayer` → `InstancedPropEngine`:
- `ScatterRenderConfig.LodMaxDistances` (ScatterRenderConfig.cs:56) returns `lods.Length - 1` distances — the **last LOD's `maxDistance` is silently dropped**.
- Actual cull is a hidden derived formula (InstancedPropEngine.cs:189-190):
  ```csharp
  float minCullSqr = Application.isPlaying ? 250000f : 1e8f;   // 500m play / 10000m editor floor
  this.maxSqrDistance = Mathf.Max(this.lod1MaxSqrDist * 4f, minCullSqr);
  ```
  → cull = `max(2 × secondLastLODdistance, 500m play / 10000m editor)`. Typing 500 on the LOD1→2 boundary culls at **1000m**; edit vs play differ.
- Same hidden formula in `GrassGpuEngine.cs:204-210`. CPU `GrassRenderer` uses field-center `SelectLod` with squared thresholds.

## Decisions (from user)
- **Cull model:** explicit `cullDistance` field (not derived).
- **Bar semantics:** pure **distance** visualization — **no** per-LOD density/thinning feature.
- **% label convention:** **closeness** — `closeness = 1 − distance/cullDistance` (100% = at camera, 0% = at cull).
- **Interactivity:** draggable handles, synced with numeric fields (SSOT via `SerializedProperty`).
- **Scope:** all three engines — CPU `GrassRenderer`, GPU `GrassGpuEngine`, GPU `InstancedPropEngine`.

## Approved design (v2)
### Data model
- `ScatterRenderConfig`: add `[SerializeField] float cullDistance;` + `public float CullDistance`.
- `ScatterLod` unchanged (`mesh`, `maxDistance`); last LOD now bounded by `cullDistance`.
- Bands: `[0..d0) LOD0  [d0..d1) LOD1  [d1..cull) LOD2  [cull..∞) CULLED`.

### Bug fix (all engines)
Replace hidden formula with `maxSqrDistance = CullDistance * CullDistance` in `InstancedPropEngine` and `GrassGpuEngine`; give CPU `GrassRenderer` the same explicit cull boundary. Edit == play.

### Distance bar (editor, draggable)
- Drawer in `TerrainScatterConfigEditor` / Scatter Studio (Odin `OdinValueDrawer<ScatterRenderConfig>` or IMGUI Rect).
- Segment width = distance span ÷ cullDistance; colored per LOD; final red Culled segment.
- Label = closeness % (`1 − dist/cull`), absolute metres on hover.
- Draggable handles edit transition distances; last handle edits `cullDistance`; write back via `SerializedProperty`, mark dirty, repaint.

## Phasing
1. Data model + `OnValidate` migration — existing assets default `cullDistance = max(lod1*2, 500)` to preserve current look.
2. Cull fix across 3 engines + shared `GrassCull.compute` cull uniform — verify 500 → culls at 500, edit == play.
3. Bar drawer: read-only render first, then draggable handles.
4. Validation: distance-boundary tests + migrate one existing layer asset.

## Risks
- Asset migration: missing `cullDistance` → default in `OnValidate`/serialization callback or everything culls at 0.
- `GrassCull.compute` shared by grass + props — cull uniform must be set by both engines.
- Draggable IMGUI hit-testing inside Scatter Studio's existing handle code — read-only-first de-risks it.

## Files in scope
- `Assets/GrassInteract/Runtime/ScatterRenderConfig.cs`
- `Assets/GrassInteract/Runtime/ScatterLayer.cs` (ScatterLod)
- `Assets/GrassInteract/Runtime/InstancedPropEngine.cs`
- `Assets/GrassInteract/Runtime/GrassGpuEngine.cs`
- `Assets/GrassInteract/Runtime/GrassRenderer.cs`
- `Assets/GrassInteract/Shaders/GrassCull.compute`
- `Assets/GrassInteract/Editor/TerrainScatterConfigEditor.cs` (+ Scatter Studio bar drawer)
