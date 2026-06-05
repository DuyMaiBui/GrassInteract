# GrassInteract — Interactive Instanced Grass

Renders 10k–50k+ interactive grass blades on mobile via GPU instancing. Blades **sway with ambient
wind** and **lean away from moving interactors** (a car, the player) — recovering to upright after the
interactor passes. **All motion is computed in C#** and baked into each blade's per-instance transform;
the shader is a *dumb* instanced renderer that just draws the mesh at the matrix it's given. Placement
is **painted** as a density map and snapped onto any ground collider. Self-contained, no third-party
dependencies.

## Why motion is in C# (not the shader)

Earlier versions drove the bend in the vertex shader off a top-down "trample" `RenderTexture`. That GPU
path was a recurring source of bugs that are invisible to `Debug.Log` and intrinsic to shaders — a
locked `ShaderCache.db` serving stale compiles, `R8` sampling as 0, standalone-sampler binding quirks,
`.hlsl` include-fold not applying, one bad variant magenta-poisoning all variants, and URP RenderGraph
silently dropping draws that set `rp.matProps`. The current design moves **wind + bend into a plain C#
per-frame pass** (`GrassBendSimulator`). Motion is now deterministic, debuggable, shader-cache-proof,
and inspectable from C# with no GPU readback.

## Architecture

| File | Role |
|---|---|
| `Runtime/GrassFieldSpace.cs` | SSOT world-XZ ↔ UV mapping over the field rect. Used at build time to sample the density map. |
| `Runtime/ScatterLayer.cs` | ScriptableObject (sub-asset of `TerrainScatterConfig`) — **SSOT** for every per-layer concern: density map, placement (bounds, density, scatter count, seed, masks), LOD meshes + distances, grass/mesh material, shadow mode, **wind**, **bend strength/flatten**, **recovery rate**, blade-height bounds, GPU chunk size. The painted "layer". |
| `Runtime/GrassScatter.cs` | Build-time: seeded **rejection sampling** against the density map → raycast ground-snap → a **flat** instance list partitioned into pooled `≤1023` matrix slabs (no spatial chunks) + parallel base-position slabs + one field-wide AABB. Deterministic. |
| `Runtime/InstanceBatchPool.cs` | Recycles `Matrix4x4[1023]` slabs. Zero per-frame alloc. |
| `Runtime/GrassBendSimulator.cs` | **The motion heart.** Owns the base matrices + per-blade lean state + precomputed wind phase + the reused output slabs. `Step(dt)` rebuilds every blade's matrix each frame: wind sway (all blades) + lean away from in-range interactors + `MoveTowards` recovery. Rigid lean about the blade base (pivot y=0). Allocation-free. |
| `Runtime/GrassInteractor.cs` | Attach to a car/player. On enable it joins a static registry (`GrassInteractor.Active`) that the simulator reads each frame; exposes world position + radius + strength. |
| `Runtime/GrassRenderer.cs` | Picks ONE global LOD mesh by camera distance to the field center and submits the simulator's output slabs via `Graphics.RenderMeshInstanced` under one field-wide bounds, `rp.camera = null` (Game **and** Scene view). |
| `Runtime/ScatterField.cs` | `[ExecuteAlways]` orchestrator. Builds the scatter + owns the simulator + renderer; drives `simulator.Step` then the render from the **player loop** (`LateUpdate` in play, `EditorApplication.update` in edit), so grass shows in Game **and** Scene views, edit **and** play. |
| `Shaders/GrassInteractInstanced.shader` | Dumb instanced URP shader: `multi_compile_instancing`, 3 passes (forward/shadow/depth), each a plain object→world→clip transform + a height-gradient color (`uv.y` lerp Base→Tip). NO deform, NO wind, NO globals. |
| `Editor/GrassBladeMeshBuilder.cs` | `Tools ▸ GrassInteract ▸ Build Blade Meshes` → LOD0/1/2 cross-quad blade meshes (pivot at the base, y=0). |
| `Editor/ScatterFieldEditor.cs` | Custom inspector for `ScatterField` with an in-Inspector "Paint" section → paint/erase density onto any collider. |
| `Editor/GrassInteractDemoBuilder.cs` | `Tools ▸ GrassInteract ▸ Build Demo Scene` → a complete wired demo. |

## How a blade moves (the per-frame pass)

`GrassBendSimulator.Step(dt)`, once per blade:

1. **Wind** — `windTilt = windDir · sin(time·windFreq + phase[i]) · windStrength`. A small XZ lean vector;
   a precomputed per-blade `phase` keeps blades out of lockstep. Runs for **every** blade.
2. **Bend** — for each interactor whose circular footprint covers the blade, accumulate a lean **away**
   from the footprint center scaled by `(1 − d/radius) · strength · bendStrength`. Blades outside all
   footprints early-out (no work).
3. **Recovery** — `bendState = Vector2.MoveTowards(bendState, bendTarget, recoveryRate · dt)`. When no
   interactor is in range the target is zero, so the blade eases back to upright at `recoveryRate`.
4. **Compose** — `totalLean = windTilt + bendState`, mapped to a **rigid rotation about the blade base**
   (clamped to a max angle), baked as `Matrix4x4.TRS(basePos, lean · baseYaw, scale)`. The base pivot is
   at `y = 0`, so a leaning blade keeps its roots planted.

The output matrices feed straight to `GrassRenderer` — there is no GPU readback and no shader-side motion.

**Tuning** (all on `ScatterLayer`, no magic numbers in code):
`Wind` direction/strength/frequency/noise-scale · `Bend Strength` (max tip-lean in metres at full
footprint) · `Flatten` (extra core mat-down) · `Recovery Rate` (how fast a leaned blade stands back up).

## Performance & the wind escape hatch

Because **wind runs for every blade every frame**, the per-frame `Step` cost scales with total blade
count (the bend term early-outs for out-of-range blades). At the demo scale this is comfortably within
budget; **~50k blades is the documented soft ceiling** on a single Mono main thread (no Burst/Jobs).

**Escape hatch — if a mobile frame budget gets tight:** move **only the wind sway** back into the dumb
shader as a one-line `_Time`-based offset in the vertex stage (the shader header marks where it would
go), so the all-blades-per-frame cost leaves the CPU. **Bend stays in C#** regardless — the interaction
path is the part worth keeping debuggable. This is a documented option, not currently implemented.

## Quick start — build the demo

`Tools ▸ GrassInteract ▸ Build Demo Scene` builds a self-contained scene: blade meshes → instanced
material → render config → a density map → a `ScatterLayer` → ground, camera, light, a circling
`GrassInteractor` (the orange effector), and a wired `ScatterField`. Open
`Assets/GrassInteract/Demo/GrassInteractDemo.unity`:

- **Edit mode:** grass is visible in the Scene view and sways with wind (the field is `[ExecuteAlways]`).
- **Play mode:** the orange effector orbits, leaning the swaying grass aside as it passes; blades recover
  to upright over a couple of seconds behind it. Zero per-frame GC.

## Setup from scratch

1. **Blade meshes:** `Tools ▸ GrassInteract ▸ Build Blade Meshes` → `Meshes/GrassBlade_LOD0|1|2.asset`
   (pivot at the base — required for the rigid base-lean).
2. **Material:** new Material with shader `GrassInteract/InstancedGrass`; **enable GPU Instancing**; set
   Base/Tip colors.
3. **Render config:** *Create ▸ GrassInteract ▸ Grass LOD Config*. Assign `Grass Material`, `Lod Meshes`
   (size 3: LOD0/1/2), `Lod Max Distances` (size 2, ascending e.g. `[18, 38]`), `Max Blade Height`,
   `Bend Headroom`, the **Wind** tunables, **Bend Strength** / **Flatten**, and **Recovery Rate**.
4. **Density map:** a readable, uncompressed `Texture2D` (the demo builder makes one; the Grass Painter
   can also fix import settings of an imported texture).
5. **Grass layer:** *Create ▸ GrassInteract ▸ Grass Layer*. Assign the density map, `Render Config`,
   `Field Bounds`, `Target Instances` (candidate scatter count — actual blades ≈ this × average density),
   `Scale Range`, `Seed`, and `Ground Snap Mask`.
6. **Interactors:** add `GrassInteractor` to the car/player (set `World Radius`, `Strength`). They
   self-register; no other wiring needed.
7. **Orchestrator:** add `ScatterField` to an empty GameObject at the field center; assign the
   `Scatter Layer`. One enabled field per scene.

## Painting density

Select the `ScatterField`, open its Inspector "Paint" section, and pick the `ScatterLayer` to paint.
Left-drag over any collider in the Scene view to paint (Paint adds, Erase subtracts); tune
Radius/Strength/Falloff. The brush converts world→UV with the same `GrassFieldSpace` the scatter uses
(origin read from the scene's `ScatterField`), so painted spots match grass spots. **Save** writes the
density texture to disk;
the field rebuilds deterministically (same seed + density + colliders → identical field).

## Render call-site discipline (durable gotchas — keep these)

- **Submit from the player loop, not `beginCameraRendering`.** Under URP RenderGraph (Unity 6 default),
  immediate-mode `RenderMeshInstanced` draws issued from `RenderPipelineManager.beginCameraRendering`
  are silently dropped. `ScatterField` drives draws from `LateUpdate` (play) /
  `EditorApplication.update` (edit) instead.
- **`worldBounds` is mandatory and must be non-zero.** `RenderMeshInstanced`'s default zero-extent box
  culls every instance. `GrassScatter` computes one field-wide AABB (spans the snapped terrain Y range +
  blade reach + bend headroom).
- **Never set `rp.matProps`** on these draws — a per-draw `MaterialPropertyBlock` makes the whole draw
  render nothing under RenderGraph. The dumb shader needs no per-draw properties anyway.

## Known constraints & follow-ups

- **No Burst/Jobs** — the per-frame `Step` is plain C# on the main thread (the 50k soft ceiling above).
- **Rigid lean** — a blade tilts as a stiff body about its base (a single matrix rotation), not a
  per-vertex curve. Tune `Bend Strength` / `Flatten` / `Recovery Rate` for feel.
- **Unlit forward pass** — grass does not receive lighting/shadows by design (cheapest path); it *casts*
  matching shadows when `Shadow Casting Mode` is On.
- **One field-wide bounds** — there is no per-region GPU frustum culling of off-screen blades; fine for a
  contained field. For very large fields, reintroduce coarse culling cells.
- **GPU-driven upgrade (future):** for >100k blades or off-CPU work, move the per-frame matrix build to a
  compute shader + `RenderMeshIndirect` behind the same `ScatterField` interface.
