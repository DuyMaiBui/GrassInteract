# Brainstorm — CPU-baked bend, dumb-instance grass shader (2026-06-02)

/ t1k-brainstorm · GrassInteract · Unity 6 / URP 17.3 / Mono (no DOTS/Burst)

## Problem statement

The interactive-grass deform has lived **entirely in the shader** (top-down trample
RenderTexture → `GrassInteractDeform.hlsl` per-vertex lean + `_Time` ambient wind).
That GPU path has cost ~6 sessions to bugs that are *intrinsic to it* and invisible to
`Debug.Log`:

- locked `Library/ShaderCache.db` serving stale compiles
- `RenderTextureFormat.R8` sampling as 0 despite `SupportsRenderTextureFormat`=true
- `SetGlobalTexture` needing a standalone inline sampler, not `sampler_<TexName>`
- `.hlsl` include-fold not applying while identical inline code works
- one bad shader variant → magenta error-shader poisoning ALL variants → no grass
- URP RenderGraph silently dropping `RenderMeshInstanced` draws that set `rp.matProps`

**Goal:** move ALL motion (bend + wind) into C# so it is debuggable, deterministic, and
shader-cache-proof. The shader becomes a *dumb* instanced renderer.

## Locked decisions (user, this session)

| Fork | Decision |
|---|---|
| Bend fidelity | **Rigid lean about base** — baked into the per-instance matrix; zero shader deform |
| Wind | **Moved to C#** — fully dumb shader (escape hatch: wind-only back in shader if mobile-tight) |
| Update strategy | **Only blades near interactors** for the bend math (wind still touches all — see Perf) |
| LOD | **Global LOD by camera distance** — whole field swaps mesh; one field-wide bounds |
| Placement | **Unchanged** — density-map scatter (`GrassLayer` + `GrassPainterWindow`) kept; scatter into a flat list instead of spatial chunks |

## Architecture flip

| | Before | After |
|---|---|---|
| Bend | vertex shader samples trample RT | C# bakes lean into the instance matrix |
| Wind | vertex shader `_Time` sway | C# per-blade sway in the same matrix |
| Shader | custom deform + wind + trample sampling | dumb instanced URP shader: mesh at matrix + height-gradient color |
| Spatial chunks | `ChunkGrid` buckets + per-chunk bounds/LOD | flat instance list, one field-wide bounds |
| Interaction data | RT splat + global texture binding | plain C# list of interactor pos/radius/strength |

## Components

**Deleted / retired**
- `Runtime/GrassTrampleMap.cs`
- `Shaders/GrassInteractDeform.hlsl`, `Shaders/TrampleUpdate.shader`
- `Runtime/GrassChunk.cs`
- all trample/wind/bend shader globals (`_GrassTrampleMap`, `_GrassWind*`, `_GrassBendStrength`, `_GrassFlatten`, `_GrassTrampleTexelDensity`)
- the trample half of `GrassFieldSpace` (keep only world rect if still needed for placement)

**Kept**
- `GrassLayer`, `Editor/GrassPainterWindow.cs` — density-map placement unaffected
- `GrassInteractor.cs` — now a plain pos/radius/strength source; drop `GrassTrampleMap.Register/Unregister`, replace with a static registry the simulator reads
- `GrassLODConfig.cs` — wind + bend + recovery become plain float tunables here

**New / reworked**
- `Runtime/GrassScatter.cs` (replaces `ChunkGrid`): build-time. Scatter blades into a flat
  `Matrix4x4[] baseMatrices` (pivot at blade base, y=0) + parallel `Vector3[] basePositions`,
  partitioned only into the API-mandated ≤1023 render slabs (NOT spatial chunks).
- `Runtime/GrassBendSimulator.cs` (new — the heart): owns `baseMatrices`, `basePositions`,
  per-blade `bendState` (current lean `Vector2` + amount), per-blade wind `phase`, and the
  output `renderMatrices` slabs. One pass per frame (below).
- `Runtime/GrassRenderer.cs`: render all slabs with a single global-LOD mesh + one field-wide
  `worldBounds`. Keep the `rp.camera = null` + player-loop call-site discipline (the ONE GPU
  lesson worth keeping — immediate-mode instanced draws must come from the player loop, not
  `beginCameraRendering`).
- `Runtime/GrassInteractField.cs`: orchestrate build + drive simulator/renderer from
  `LateUpdate` (play) / `EditorApplication.update` (edit), as today. Drops all global binding.

## Per-frame loop (single pass over N blades)

```
for each blade i:
    windTilt   = sin(time*windFreq + phase[i]) * windStrength        // always
    bendTarget = Vector2.zero
    for each interactor:                                             // early-out if none in range
        d = distance(basePositions[i].xz, interactor.posXZ)
        if d < interactor.radius:
            falloff = 1 - d / interactor.radius
            bendTarget += awayDir(i, interactor) * falloff * interactor.strength
    bendState[i] = MoveTowards(bendState[i], bendTarget, recoveryRate * dt)  // recover when target=0
    renderMatrices[i] = T(basePos) * Rot(windTilt + bendState[i], about base) * baseYawScale
```

- Rigid tilt about the base pivot = ONE rotation baked into the matrix. No per-vertex data,
  no shader logic.
- Recovery is a per-blade `MoveTowards` lerp — replaces the RT fade/recover.
- "Only near interactors" = early-out of the bend accumulation + skip recovery when already
  upright; the wind term still runs for all (see Perf).

## Performance reality (the honest tradeoff)

Wind-in-C# means **all N matrices rebuild every frame** on the Mono main thread.
Cost ≈ N × (1 sin + M radius checks + 1 TRS). At 20k blades / few interactors ≈ sub-ms;
at 50k ≈ ~1–2 ms. Matrix→GPU upload per `RenderMeshInstanced` is unchanged from today.

Mitigations in the design: early-out bend math for out-of-range blades; skip recovery for
upright blades; precomputed per-blade wind phase; reuse persistent slab arrays (zero per-frame
GC, same discipline as today).

**Escape hatch** if mobile frame budget gets tight: move *only wind* back into the dumb shader
(one-line `_Time` sway); bend stays in C#. Keeps the all-blades-per-frame cost off the CPU
while preserving the debuggable interaction path. Documented as a config toggle candidate.

## LOD

Single global LOD: `GrassRenderer` picks one mesh for the whole field by camera distance to the
field center, draws every slab with it under one field-wide `worldBounds`. No per-blade LOD cost.

## Risks

- **Blade mesh pivot must be at y=0 (base)** or the rigid lean pivots from the wrong point —
  verify `Editor/GrassBladeMeshBuilder.cs` output; bake a base offset if not.
- **Rigid lean reads stiffer** than a per-vertex curve (chosen knowingly) — tune with a slight
  scale-squash on heavy bend to fake compression.
- **One field-wide bounds = no GPU frustum culling of off-screen blades** — acceptable for a
  contained field; note it. If a huge field is needed later, re-introduce coarse culling cells.
- **Wind-in-C# is the perf ceiling** — escape hatch above.
- **Determinism** — keep the seeded scatter draw-order from `ChunkGrid` so placement is
  byte-stable across the refactor.

## Success criteria

1. Zero shader-deform code — grass shader compiles to a plain instanced URP material; no
   trample globals; immune to the `ShaderCache.db` / sampler / magenta bug class.
2. Grass renders in Game AND Scene view, edit + play (current working state preserved).
3. A moving `GrassInteractor` visibly leans blades away and they recover after it passes —
   verifiable by reading `bendState` / `renderMatrices` in C# (no GPU readback needed).
4. ~20k blades at the demo's frame budget; 50k documented as the soft ceiling with the wind
   escape hatch.
5. `unity-code-reviewer` pass: 0 Critical/High before "done".

## Next step

Hand to `/t1k:plan` for a phased implementation plan (scatter rework → simulator → dumb shader
→ renderer/LOD → delete trample path → demo verify → review).
