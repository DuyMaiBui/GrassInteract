# Phase 4 - Density-map placement

Cross-ref: plan.md - brainstorm-grass-interact-boingfree-20260601.md (section E). Blocked by Phase 0 (needs GrassFieldSpace). Independent of Phases 1-3 (different files except GrassInteractField - sequence on that file).
Activate first: t1k-unity-base-code-conventions, unity-terrain, t1k-unity-base-mcp-skill.

## Objective

Replace uniform-random placement with deterministic density-map-driven placement. A GrassLayer ScriptableObject holds a readable R8 density Texture2D plus placement params and references a GrassLODConfig for render/LOD. ChunkGrid.Build is rewritten to seeded rejection-sample candidate XZ positions (keep with probability = sampled density), raycast straight down to snap Y onto a ground collider (fallback to field-plane Y), then bucket + pool exactly as before. Preserves the seeded-deterministic, chunk-bucketed, pooled flow.

## Files owned

Created:
- Assets/GrassInteract/Runtime/GrassLayer.cs (NEW) - [CreateAssetMenu] ScriptableObject. Fields: Texture2D densityMap (readable R8), int targetDensity (instances at density=1 across the field, or instances-per-square-metre - pick one and document), Vector2 scaleRange, int seed, LayerMask groundSnapMask, GrassLODConfig renderConfig, Vector2 fieldBounds (the layer owns the field rect now, OR keep on config - decide: put fieldBounds on GrassLayer since placement belongs to the layer). Validate(out string): densityMap != null && isReadable && uncompressed single-channel; renderConfig != null && renderConfig.Validate; scaleRange/seed/bounds sane.

Modified:
- Assets/GrassInteract/Runtime/ChunkGrid.cs - rewrite Build to take (GrassLayer layer, Vector3 origin, InstanceBatchPool pool). Rejection-sampling + raycast ground snap. Keep ReturnSlabs unchanged.
- Assets/GrassInteract/Runtime/GrassInteractField.cs - serialize a GrassLayer instead of (or in addition to) the GrassLODConfig; derive config = layer.RenderConfig; build GrassFieldSpace from layer.FieldBounds; pass the layer to ChunkGrid.Build.
- Assets/GrassInteract/Runtime/GrassLODConfig.cs - if fieldBounds/instanceCount/scaleRange/randomSeed move to GrassLayer, remove them from config (config becomes render/LOD-only). Document the split (GrassLODConfig = render; GrassLayer = placement) per design decision.

## Implementation steps

1. Decide the data split: GrassLODConfig = LOD meshes + distances + shadow mode + maxBladeHeight + bendHeadroom + wind tunables (render-only). GrassLayer = densityMap + targetDensity + scaleRange + seed + groundSnapMask + fieldBounds + chunkSize + renderConfig (placement). Move placement fields out of config into GrassLayer. Update accessors + the demo builder expectation (builder handled in Phase 6).
2. GrassLayer.Validate: hard-fail (return false + message) if densityMap == null, !densityMap.isReadable, or format is compressed/multi-channel. No silent fallback (development-principles).
3. ChunkGrid.Build rewrite: var rng = new System.Random(layer.Seed). Iterate a candidate budget (e.g. targetDensity scaled by field area or a max-candidates count): for each candidate, sample localX/localZ uniformly in bounds; map to UV via the SAME field rect (GrassFieldSpace.WorldToUv) used by trample; sample density = densityMap.GetPixelBilinear(uv.x, uv.y).r; keep candidate iff rng.NextDouble() <= density. For kept candidates: yaw + scale from rng (deterministic); raycast Physics.Raycast(from high above candidate XZ, Vector3.down, out hit, maxDist, groundSnapMask) -> worldY = hit.point.y; fallback worldY = origin.y + Debug.LogWarning once if no hit. Build TRS, bucket into chunk tile (same indexing as current), pool slabs (unchanged).
4. Keep the MAX_CHUNKS guard + AABB sizing (bladeReachY/lateralPad) logic. AABB Y must now span the actual terrain height range of the chunk (track min/max hit Y per bucket) since ground is no longer flat - expand the chunk AABB to cover min..max snapped Y + bladeReachY. Important: flat-plane assumption is gone.
5. GrassInteractField: serialize GrassLayer grassLayer; in Rebuild validate layer, set config = layer.RenderConfig, build GrassFieldSpace from layer.FieldBounds, ChunkGrid.Build(layer, transform.position, pool), new GrassRenderer(config, transform.position). Bind field globals from layer/config.

## Risk table

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Density UV != trample UV (drift) | 3 | 5 | 15 | ChunkGrid samples density via GrassFieldSpace.WorldToUv using the SAME field rect bound for trample. Single owner = GrassInteractField. No inline UV math in ChunkGrid. |
| densityMap compressed/non-readable -> GetPixelBilinear throws / black | 3 | 4 | 12 | GrassLayer.Validate hard-fails before Build; brush (Phase 5) creates the asset with readable+uncompressed import settings. |
| No ground collider at build -> all blades at plane Y (floating/buried) | 3 | 3 | 9 | Fallback plane Y + single Debug.LogWarning. Demo uses Terrain (has TerrainCollider). groundSnapMask documented. |
| Non-flat terrain breaks chunk AABB Y span -> blades frustum-culled on slopes | 3 | 4 | 12 | Track per-bucket min/max snapped Y; size chunk AABB to cover that range + bladeReachY. Verify on the sloped Ezereal terrain. |
| Determinism lost (raycast order / float drift) -> non-reproducible field | 2 | 3 | 6 | Single-threaded seeded rng; candidate order fixed by loop index; raycast is deterministic for a static scene. Re-build twice -> identical instance count. |
| targetDensity semantics ambiguous (total vs per-m2) | 2 | 2 | 4 | Pick total-candidates-across-field; document in tooltip + GrassLayer XML doc. |

## Effort

L

## Scene-window verification gate

1. read_console -> ZERO errors after compile.
2. Create a test GrassLayer with a hand-made density Texture2D (e.g. a white circle on black). Assign to a GrassInteractField over the Ezereal terrain (or demo ground).
3. EDIT mode Scene view: grass appears ONLY where density > 0 (the white circle), none in the black region.
4. Grass FOLLOWS terrain height (sits on the surface, not floating/buried) on the sloped Ezereal terrain.
5. Rebuild twice (context-menu) -> identical placement (deterministic): same instance count, same layout.
6. On a slope, no grass disappears when the Scene camera frames it (chunk AABB Y span correct).

Done only when: grass appears only where density>0, snaps to terrain height, deterministic across rebuilds, no console errors, visible in Scene view.
