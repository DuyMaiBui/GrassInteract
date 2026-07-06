# GPUGrass — Technical Documentation

A **standalone** interactive grass plugin for Unity built-in **Terrain**, independent of WorldPainter.
It reads a Terrain's painted detail (grass) layer, bakes placement to an asset, and renders it as
GPU-instanced interactive grass with wind + bend (point & trail interactors). It scales across devices
(down to OpenGL ES 3.0 via a device-tier probe) and sets up in one click.

- **Engine:** Unity 6 (6000.3.x), URP 17.3.
- **Location:** `Assets/GPUGrass/` — namespaces `GPUGrass` (runtime) and `GPUGrass.Editor`.
- **Source of truth:** the Terrain's detail/grass layer (paint with **Terrain ▸ Paint Details**), or the
  whole terrain surface (`PlacementSource = TerrainSurface`).
- The Terrain renders the ground; its own detail rendering is disabled; GPUGrass renders the grass.

> This document is the architecture + usage reference. For the pass-by-pass build history and the design
> rationale, see `Assets/GPUGrass/README.md`.

---

## 1. Quick start

1. Add one or more Unity Terrains. Optionally paint grass with **Terrain ▸ Paint Details** (or set the
   config `PlacementSource = TerrainSurface` to scatter over the whole surface — no painting needed).
2. **Tools ▸ GPUGrass ▸ Scene Grass Setup** → pick/create the shared config → assign a blade **LOD mesh** on
   the config → **Setup & Bake All Terrains**.
3. *(Optional, occlusion)* Add **GpuGrassHiZFeature** to your URP Renderer asset, assign
   `Shaders/HiZBuild.compute`, and enable **Depth Texture** on the URP Renderer. See §7. *(On mobile,
   consider leaving occlusion off — see §8.)*
4. Attach a `GrassInteractor` (and/or `GrassTrailInteractor`) to your car/player. Done.

Or copy `Samples~/InteractiveGrassDemo/` into `Assets/` and run **Tools ▸ GPUGrass ▸ Build Demo Scene** for a
ready-made procedural example.

---

## 2. Architecture at a glance

```
Terrain (detail layer or surface)
        │  editor bake — deterministic
        ▼
GpuGrassBakeData  (positions[], yaws[], scales[], worldBounds, instanceCount)
        │  Build (counting-sort into 16 m XZ chunks)
        ▼
GpuGrassChunkedBuffer  →  _Blades (20 B) + _ChunkAabb (24 B) + _ChunkRange (8 B)
        │  per frame, GPU-driven, no CPU readback
        ▼
GrassCull.compute:  ChunkCull → WriteArgsB → BladeCull  (frustum + distance + Hi-Z + density)
        │  buckets visible blade indices into per-LOD append buffers
        ▼
Graphics.RenderMeshIndirect ×3  (one indirect draw per LOD)
        │
        ▼
GpuGrassIndirect.shader  (VS reconstructs TRS, applies wind + interactor/trail bend GPU-side)
```

Deformation is **100% GPU-side**. The CPU only advances a time accumulator and uploads the small interactor
/ trail registries each frame. The large blade buffer is uploaded once at build and never re-sent.

---

## 3. Runtime data flow (stage by stage)

| Stage | Class / Method | What happens |
|---|---|---|
| Setup | `GpuGrassSceneSetup.SetupScene` → `GpuGrassAutoSetup.SetupOnTerrain` (Editor) | Adds the controller, creates a per-terrain bake asset, wires render assets, disables terrain detail, bakes, rebuilds. |
| Bake | `GpuGrassBaker.Bake(Terrain, GpuGrassConfig, GpuGrassBakeData)` | Two placement sources: **`ScatterDetailLayers`** (painted density map, rejection-sampled) or **`ScatterSurface`** (uniform over the surface, altitude + slope masked). Ground-snaps via `SampleHeight`, slope-masks via `GetInterpolatedNormal`. Deterministic RNG seeded from `config.Seed`. Writes parallel arrays via `GpuGrassBakeData.SetData`. |
| Tier resolve | `GpuGrassController.Rebuild` → `ResolveTier` | Resolves the device tier (§5). Only `GrassDeviceTier.Gpu` constructs a renderer. |
| Renderer build | `GpuGrassRenderBootstrap.Create` (via static `GpuGrassController.RendererFactory`) | Installed with `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` + `[InitializeOnLoadMethod]`; builds `GpuGrassRenderer` from the config's serialized compute/shader/material refs. |
| Chunk partition | `GpuGrassChunkedBuffer.Build` → `Partition` | Counting-sort partitions blades into a **16 m XZ grid** (`CHUNK_SIZE = 16`); builds per-cell AABB + contiguous range; packs yaw/scale to 16 bits each; uploads three structured buffers. |
| Per-frame drive | `GpuGrassController.LateUpdate` (play) / `EditorStep` + `OnBeginCameraRendering` (edit) | `engine.Step(dt)` (advances time) then `engine.Submit(camera, lodRef)`. Play passes `null` camera (all cameras); edit passes the specific camera. |
| Compute cull | `GpuGrassRenderer.RecordFrameCommands` | One `CommandBuffer`: **ChunkCull** (kernel 0 — frustum + distance + Hi-Z, appends visible chunks) → `CopyCounterValue` → **WriteArgsB** (kernel 1 — builds indirect dispatch args) → **BladeCull** (kernel 2, indirect dispatch — per-blade frustum + distance + density skip, buckets into `visibleLod0/1/2`) → 3× `CopyCounterValue` into per-LOD indirect **draw** args. No CPU readback. |
| Draw | `GpuGrassRenderer.Submit` → `Graphics.RenderMeshIndirect` ×3 | One indirect draw per LOD mesh/material; absent LODs skipped. A whole-field frustum gate (`TestPlanesAABB`) skips the entire chain when the field is off-screen. |
| Shader | `GpuGrassIndirect.shader` (`GPUGrass/IndirectGrass`) | VS reads `_Blades` + `_VisibleIndices`, reconstructs TRS, applies wind + interactor/trail bend fully GPU-side. |

---

## 4. Configuration — `GpuGrassConfig`

One `ScriptableObject` (CreateAssetMenu **GPUGrass/Grass Config**) holds every tunable. The Scene Grass
Setup window edits it via an embedded inspector, and setup applies it to every active Terrain (each terrain
keeps its own hidden `GpuGrassBakeData`).

| Group | Field | Default | Notes |
|---|---|---|---|
| **Placement** | `placementSource` | `DetailLayer` | `DetailLayer` (painted) or `TerrainSurface` (whole surface). |
| | `targetDensityPerSqM` | 0.76 | Blades per m² at full coverage. |
| | `scaleRange` | (0.8, 1.2) | Per-blade uniform scale. |
| | `bladeHeightRange` | (0.3, 0.6) | Height variation. |
| | `slopeRange` (deg) | (0, 60) | Slopes outside this are masked out. |
| | `heightRange01` | (0, 1) | Altitude band (normalized). |
| | `seed` | 0 | Deterministic placement seed. |
| **LOD / Render** | `lodMeshes` | — | Mesh[] (LOD0…). **Must be assigned** or nothing renders. |
| | `lodMaxDistances` | {15, 40} | LOD switch distances (m). |
| | `renderCullDistance` | 80 | Hard cull radius (m); 0 = infinite. |
| | `grassMaterial` | auto | Must be on `GPUGrass/IndirectGrass`. |
| | `shadowCastingMode` | Off | |
| | `receiveShadows` | false | |
| **Render assets** | `cullCompute` | auto | `GrassCull.compute`, auto-wired by setup. |
| | `indirectShader` | auto | `GPUGrass/IndirectGrass`, auto-wired. |
| **Wind** | `windDirection` | (1, 0.3) | |
| | `windStrength` | 0.15 | |
| | `windFrequency` | 1.2 | |
| | `windNoiseScale` | 0.1 | |
| **Bend** | `bendStrength` | 1 | |
| | `flatten` | 0.3 | 0…1. |
| | `recoveryRate` | 2 | *(serialized; recovery is GPU/trail-age driven — see note.)* |
| | `enableTrailInteractors` | true | |
| **Adaptive density** | `enableAdaptiveDensity` | true | CPU governor thins blades to hold FPS (§6). |
| | `adaptiveTargetFps` | 60 | |
| | `minDensity` | 0.6 | 0…1 floor. |
| **Occlusion** | `enableOcclusionCulling` | true | Hi-Z chunk occlusion (§7). Fail-open. |
| **Device tier** | `tierMode` | Auto | `Auto` / `ForceGpu` / `ForceTerrainFallback` / `ForceDisabled`. |
| | `enableTerrainFallback` | true | Allow built-in terrain detail on non-compute devices. |
| | `lowEndMemoryThresholdMB` | 2048 | Below this system RAM → no grass. |
| | `terrainFallbackDetailDistance` | 80 | `Terrain.detailObjectDistance` in fallback tier. |

> **Note on `recoveryRate`:** it is exposed on the config but not consumed by the renderer snapshot; blade
> recovery is handled GPU-side via trail-sample aging.

---

## 5. Device-tier policy — one probe, three outcomes

`GpuGrassController.ResolveTier` reads `config.TierMode`. `Auto` calls
`GpuGrassTierProbe.ClassifyAuto(enableTerrainFallback, lowEndMemoryThresholdMB)`, evaluated in order:

1. **Disabled** — if `lowEndMemoryThresholdMB > 0` and `SystemInfo.systemMemorySize` is in `(0, threshold)`.
   Very low-RAM devices get *no* grass.
2. **Gpu** — if `TryGpu` passes: requires **all** of `supportsComputeShaders`,
   `supportsIndirectArgumentsBuffer`, and `maxComputeBufferInputsVertex > 0`.
   *(OpenGL ES 3.0 fails the first check — no compute — and never reaches the GPU tier. GLES 3.1 devices that
   can run compute but can't read StructuredBuffers in the vertex stage fail the third check.)*
3. **TerrainFallback** — else, if `enableTerrainFallback`; otherwise **Disabled**.

Only the **Gpu** tier runs the GPUGrass renderer. `ApplyTerrainDetailForTier` (runtime) sets
`terrain.detailObjectDistance` to `terrainFallbackDetailDistance` for TerrainFallback, else 0 (so the built-in
terrain grass is hidden when GPUGrass is drawing). `GrassDeviceTier` = `{ Gpu, TerrainFallback, Disabled }`.

---

## 6. Interactors (touch bend)

Two independent **static registries**, both read by the renderer every `Submit`. Drop the component on a
mover; no wiring needed.

**Point — `GrassInteractor`** (`[ExecuteAlways]` MonoBehaviour)
- Fields: `worldRadius` (2), `strength` (1), `maxBendDegrees` (0…90, 70).
- `OnEnable`/`OnDisable` add/remove from a static `List<GrassInteractor>` (idempotent, domain-reload safe).
- Uploaded via `GpuGrassInteractorBuffer.Upload` (max **16**; overflow dropped with a one-time warning; idle-skip
  when zero-now and zero-last-frame). Bound globally as `_Interactors`, count as `_InteractorCount`.

**Trail — `GrassTrailInteractor`**
- Fields: point-interactor fields + `trailDuration` (5 s), `minVertexDistance` (0.25),
  `centerZonePercent` (0…1, 0.4). Runtime `Emitting` toggle.
- `LateUpdate` maintains a FIFO of `TrailSample{ PosWS, Age, StrokeStart }` (max **256**): ages/evicts by
  duration, records on movement, flags `StrokeStart` on emit-resume (pen-lift).
- Uploaded via `GpuGrassTrailBuffer.Upload` only when `enableTrailInteractors`: flattens samples into segments
  (max **128** cross-interactor), skips pen-lift gaps, computes per-segment alpha from age/duration. Bound
  globally as trail segments with `_GrassTrailSegmentCount`.

---

## 7. Hi-Z occlusion culling

`enableOcclusionCulling` (default on, **fail-open**). `GpuGrassHiZFeature` (URP RenderGraph
`ScriptableRendererFeature`, `AfterRenderingOpaques`) builds a per-camera depth pyramid via `HiZBuild.compute`
(half-screen R32F base, ≤12 mips, conservative max-Z). `GrassCull.compute` `ChunkCull` reprojects each chunk
AABB through the previous frame's view-projection and tests it against the pyramid; occluded chunks are skipped
before the per-blade cull. When no depth texture is available it fails open to frustum + distance culling only.

**To enable:** add **GpuGrassHiZFeature** to your URP Renderer asset (Renderer Features), assign
`Shaders/HiZBuild.compute`, and enable **Depth Texture** on the URP Renderer. Without the feature,
`enableOcclusionCulling` no-ops.

---

## 8. Mobile optimization — assessment & recommendations

Target: mobile down to GLES 3.0, URP 17.3, tile-based (TBDR) GPUs (Adreno / Mali). The system is **already
well-built for mobile in most respects**; the items below separate what's correct from what can improve.

### 8.1 Already correct — do not regress

- **GLES3.0 → CPU tier probe.** `TryGpu` correctly gates on `supportsComputeShaders` +
  `supportsIndirectArgumentsBuffer` + `maxComputeBufferInputsVertex > 0`. GLES3.0 (no compute) never reaches
  the GPU tier — this is the key mobile-safety fact.
- **`Cull Back` on all passes.** Halves grass forward-fragment count on TBDR — the single most important
  fillrate decision, already made correctly.
- **Opaque default path (no alpha blend).** `RenderType=Opaque`, `Queue=Geometry`, early-Z intact. Alpha is
  handled by opt-in alpha-clip, not transparency.
- **20 B blade stride, uploaded once.** Tight per-blade data (pos + packed yaw/scale + hash); the big buffer is
  never re-sent per frame. Per-frame CPU→GPU traffic is a few KB (interactor 512 B, trail ≤6 KB).
- **Stripped fragment interpolators** on the default path (no PBR/normal/shadow) — saves ~36 B/fragment
  interpolator bandwidth on TBDR.
- **GPU-side density thinning** (stable per-blade hash → no shimmer, skipped blades cost nothing) +
  deterministic LOD2 far-field thinning (~40% kept at cull radius).
- **3 indirect draws per field** + whole-field frustum gate. About as low as GPU-driven grass gets.

### 8.2 Ranked improvement opportunities

| # | Impact | Improvement | Evidence / rationale |
|---|---|---|---|
| 1 | **High** | **Default Hi-Z occlusion OFF on mobile.** Flip `enableOcclusionCulling` default to false and have "Apply Mobile Preset" turn it **off** (currently it turns it **on**). | The Hi-Z pass forces a `_CameraDepthTexture` resolve (bandwidth-heavy on tilers) + a half-res R32F pyramid (~1.5 MB/camera at 1080p) + a log2(n) reduce-dispatch chain **every frame**, whether or not any grass is occluded. On mostly-flat fields it is pure overhead; frustum + distance + LOD2 thinning already remove most off-screen/far grass far cheaper. Keep it opt-in for hilly / heavily-occluded terrain, and profile on device before shipping enabled. |
| 2 | **High** | **Keep the opaque blade path; avoid alpha-clip cutout grass on mobile.** | `clip()`/discard disables early-Z on Mali/Adreno and forces late-Z. Grass overdraw is the #1 fillrate cost; alpha-clip cards multiply it. If cutout is required, keep the silhouette tight. The `Cull Back` default must not be undone by cutout cards. |
| 3 | **Med-High** | **Lighten the vertex deform in Depth/Shadow passes; keep `_WIND_PERLIN` off.** | The interactor loop (≤16) and **trail-segment loop (≤128 iterations)** run per-vertex in **all three passes** (Forward + DepthOnly + ShadowCaster). Trail-bend error in the depth/shadow silhouette is imperceptible — skip it there. `_WIND_PERLIN` is ~16 `sin`/hash ops vs ~1 for the default `sin` wind; keep it off on mobile. LOD1/LOD2 could also skip touch-bend entirely. |
| 4 | **Med** | **Make "Apply Mobile Preset" actually aggressive.** | Today it flips only 4 flags: occlusion on, adaptive on, `renderCullDistance` 80→70, tier Auto. It does **not** pull in LOD distances, lower `targetDensityPerSqM`, lower the `minDensity` floor, force shadows off, or ensure `_WIND_PERLIN` off. Recommended preset: LOD `{8, 20}`, lower target density, `minDensity ≈ 0.4`, `shadowCastingMode = Off` + `receiveShadows = false`, `_WIND_PERLIN` off, **occlusion off** (per #1). |
| 5 | **Med** | **Right-size / merge the 3 per-LOD visible-index buffers.** | Each of `visibleLod0/1/2` is sized to the **whole** blade count (`bladeCap`), so scratch = 3 × 4 B × TotalBlades = **12 B/blade** on top of the 20 B blade data — even though their sum can never exceed TotalBlades. Sizing each to a realistic per-LOD fraction, or using one shared append buffer partitioned by LOD, saves ~8 B/blade (≈8 MB on a 1M-blade field). Matters on 2–3 GB devices, which still hit the GPU tier (`lowEndMemoryThresholdMB` default 2048). |
| 6 | **Low** | **Reduce BladeCull SSBO binding count for the weakest GLES3.1 tier.** | BladeCull binds ~8 UAV/SRV (blades, ranges, visible-chunks, count, 3× append, dispatch args). GLES3.1 guarantees only 4 SSBOs per compute stage on the floor (Mali-T7xx era); most real devices expose 8+, but merging the 3 LOD append buffers (packed 2-bit LOD tag) or dropping occlusion bindings when disabled shaves headroom. |

> **Net for a flat mobile field:** items #1 + #5 together remove a per-frame depth-resolve + pyramid + ~8–10 MB
> of buffers for little to no visual change — the highest-leverage, lowest-risk wins. Items #2–#4 are ALU /
> fillrate wins that scale with near-field grass coverage.

---

## 9. Public API surface

**Components (drop in scene):**
- `GpuGrassController` — properties `Terrain`, `Config`, `Bake`; `Rebuild()`; read-only `ResolvedTier`,
  `CurrentDensity`; static `RendererFactory` (tier-seam extension point).
- `GrassInteractor` — serialized `worldRadius` / `strength` / `maxBendDegrees`.
- `GrassTrailInteractor` — same + `trailDuration` / `minVertexDistance` / `centerZonePercent`; runtime
  `Emitting` bool.

**Assets:**
- `GpuGrassConfig` (ScriptableObject, **GPUGrass/Grass Config**) — all tunables; `SetRenderAssets`,
  `SetLodMeshes`, `SetLodMaxDistances`.
- `GpuGrassBakeData` (ScriptableObject, **GPUGrass/Grass Bake Data**) — read-only inspector; re-bake to change.

**Interfaces / URP:**
- `IGpuGrassRenderer` (`Build` / `Step` / `SetDensity` / `Submit` / `WorldBounds` / `Dispose`) — the tier seam;
  `GpuGrassRenderer` itself is `internal`.
- `GpuGrassHiZFeature` (public `ScriptableRendererFeature`) — assign `hiZBuildCompute`, needs Depth Texture.

**Editor entry points:**
- **Tools ▸ GPUGrass ▸ Scene Grass Setup** window (`GpuGrassSceneWindow`).
- Scripted: `GpuGrassSceneSetup.SetupScene` / `EnsureSharedConfig`, `GpuGrassAutoSetup.SetupOnTerrain`.
- **Tools ▸ GPUGrass ▸ Build Demo Scene** (from the copied sample).

---

## 10. GPU struct contract (C# ↔ HLSL)

Strides are explicit constants (not `Marshal.SizeOf`) to avoid padding surprises, and are pinned by
`Tests/GpuStructStrideTests.cs`.

| C# struct | Stride | Layout | HLSL match |
|---|---|---|---|
| `GpuGrassBladeInstance` | **20 B** | float3 posWS + uint packedYawScale + uint hash | `BladeInstance` |
| `GpuGrassChunkAabb` | **24 B** | float3 min + float3 max (empty sentinel: min > max) | `ChunkAabb` |
| `GpuGrassChunkRange` | **8 B** | uint start + uint count | `ChunkRange` |
| `InteractorGpu` | **32 B** | float3 pos + radius + strength + 3× pad | `GrassInteractorGpu` |
| `TrailSegmentGpu` | **48 B** | PosA + Radius + PosB + Alpha + MaxBendRad + CenterPct + Strength + Pad | `GrassTrailSegmentGpu` |

**Encoding:** `packedYawScale` hi16 = yaw over `[0, 360°)` (`YAW_ENCODE_SCALE = 65535/360`), lo16 = scale over
`[0, ScaleMax]` (decoded with the `_ScaleMax2` uniform). `hash` = deterministic Xorshift32, used for density
skip and LOD2 jitter.

---

## 11. Tests

EditMode tests under `Tests/` (all green) cover: chunked-buffer partition correctness (contiguous ranges, AABB
coverage, yaw/scale pack round-trip, empty-cell sentinels), LOD-threshold math, the C#↔HLSL struct-stride
contract, the placeholder blade-mesh builder, the terrain baker on a synthetic Terrain (placement, slope mask,
determinism, empty-detail), and the Hi-Z projection/mip-select math. Run from
**Window ▸ General ▸ Test Runner ▸ EditMode**.
