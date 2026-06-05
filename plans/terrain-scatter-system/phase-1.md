# Phase 1 — Surface-Sampler Seam + Terrain Bind

**Delivers:** "Integrate grass to terrain." Grass placement follows Unity Terrain height, skips holes, respects max slope — with ZERO render-path changes and byte-stable behavior on non-terrain colliders.

## Scope

Extract the hardcoded downward `Physics.Raycast` in `GrassScatter.Build` behind an `ISurfaceSampler` seam; add a Terrain-aware sampler and a raycast fallback; let the field bind to a Unity Terrain to auto-derive origin/bounds and select the terrain sampler.

## Files owned (this phase)

| File | Change |
|---|---|
| `Assets/GrassInteract/Runtime/ISurfaceSampler.cs` | NEW — `interface ISurfaceSampler { bool TrySample(float wx, float wz, out SurfaceHit hit); }` + `struct SurfaceHit { float Y; Vector3 Normal; float SlopeDeg; float[]? SplatWeights; }` |
| `Assets/GrassInteract/Runtime/RaycastSurfaceSampler.cs` | NEW — wraps today's `Physics.Raycast(down, RAY_MAX_DISTANCE, groundMask)`; `SlopeDeg` from `hit.normal`; `SplatWeights=null`. Verbatim behavior of current snap. |
| `Assets/GrassInteract/Runtime/TerrainSurfaceSampler.cs` | NEW — `TerrainData.GetInterpolatedHeight(uv)` (+ terrain.transform.y), `GetInterpolatedNormal` → `SlopeDeg`, `GetHoles`/`IsHole` → `TrySample` returns false on hole, alphamaps cached for `SplatWeights`. |
| `Assets/GrassInteract/Runtime/GrassScatter.cs` | MODIFY — `Build(layer, origin, pool, ISurfaceSampler sampler)`; replace inline raycast with `sampler.TrySample`; after sample, skip candidate on `!hit` (hole/no-ground) or `hit.SlopeDeg > layer.MaxSlopeDeg`. Preserve EXACT rng draw order. |
| `Assets/GrassInteract/Runtime/GrassLayer.cs` | MODIFY (additive only this phase) — add `[SerializeField] private float maxSlopeDeg = 90f;` + `public float MaxSlopeDeg`. (Full ScatterLayer generalization is Phase 2.) |
| `Assets/GrassInteract/Runtime/GrassInteractField.cs` | MODIFY (additive) — add `[SerializeField] private Terrain? boundTerrain;`; in the build path, construct the sampler: `boundTerrain != null ? new TerrainSurfaceSampler(boundTerrain) : new RaycastSurfaceSampler(layer.GroundSnapMask)`; when bound, override origin = `boundTerrain.transform.position` and field bounds = `terrainData.size.xz`. Pass the sampler into `GrassScatter.Build` (via the engine Build chain). |
| `Assets/GrassInteract/Runtime/GrassCpuEngine.cs`, `GrassGpuEngine.cs` | MODIFY (minimal) — thread the `ISurfaceSampler` from the field into their `GrassScatter.Build` call (engines already call Build internally). |
| `Assets/GrassInteract/Editor/GrassScatterSamplerVerify.cs` | NEW (editor harness) — builds a scatter with each sampler; asserts (a) raycast path produces IDENTICAL instance count + first-N positions vs the pre-refactor baseline (byte-stability), (b) terrain path snaps Y to `GetInterpolatedHeight`, skips holes, drops candidates above `maxSlopeDeg`. |

## Out of scope (later phases)

- `ScatterLayer`/`ScatterField` rename + layer list (Phase 2).
- Mesh props (Phase 3).
- Splat-mask PAINTING and align-to-normal (Phase 4). (Splat WEIGHTS are read into `SurfaceHit` now, but not yet used as a placement mask — that's Phase 4.)

## Approach notes

- **Byte-stability is the long pole.** The rng draw order in `GrassScatter` (localX, localZ, accept[, yaw, scale]) MUST stay identical; the sampler ONLY replaces the Y-resolution step. Capture a baseline (instance count + first 64 positions) on the current demo BEFORE refactor, compare AFTER.
- `TerrainSurfaceSampler` converts world XZ → terrain-local normalized UV: `u = (wx - terrainPos.x)/size.x`, `v = (wz - terrainPos.z)/size.z`; out-of-[0,1] → `TrySample` false.
- Terrain holes: `TerrainData.IsHole(x, z)` on the hole-texture resolution; map UV → hole indices.
- Slope: `acos(dot(normal, up)) * Rad2Deg`.

## Success criteria

1. Existing `GrassInteractDemo` (raycast path, no terrain bound) renders **identical** instance count + positions vs baseline (harness diff PASS).
2. Binding a Terrain to the field: grass auto-fills the terrain bounds, every blade Y matches `GetInterpolatedHeight`, blades over holes are absent, blades on slopes > `maxSlopeDeg` are absent. **Screenshot-verified** on a terrain in-editor.
3. `GrassScatterSamplerVerify.Run()` PASS (both samplers).
4. Clean compile (0 C#/shader errors); all 3 existing grass harnesses still PASS; grass GPU+CPU tiers unchanged.

## Verification (live, main-loop-driven MCP)

`set_active_instance GrassInteract@de203215` → refresh/compile → `read_console` (0 errors) → run `GrassScatterSamplerVerify.Run()` + the 3 existing harnesses → create/locate a Terrain, bind it, Rebuild, screenshot grass following terrain + holes/slope.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|---|---|
| Refactor breaks placement byte-stability | 3 | 4 | 12 | Baseline diff harness; sampler replaces only Y-snap; preserve rng order |
| Terrain UV/hole index mapping off-by-one | 3 | 3 | 9 | Unit-style harness asserts known height at known UV on a ramp terrain |
| Engines thread sampler incorrectly (null in some path) | 2 | 3 | 6 | Default to RaycastSurfaceSampler when boundTerrain null; never null sampler |

## Timeline: M (~3 days). Long pole = byte-stability diff + terrain UV/hole mapping.
