# Plan: GPUGrass Mobile Optimization (6 improvements)

Date: 2026-07-06 · Mode: --auto (single-agent) · Source assessment: `docs/GPUGrass.md` §8.2

Apply the 6 ranked mobile-optimization improvements from the GPUGrass mobile assessment. Target: Unity 6 /
URP 17.3, mobile down to GLES 3.0 on tile-based (Adreno/Mali) GPUs. The module already does most things right
(GLES3.0→CPU tier probe, `Cull Back`, opaque default, 20 B blade stride, GPU-side density thinning) — these
changes remove per-frame overhead and buffer bloat that is net-negative on flat mobile fields, without
regressing the parts that are already correct.

## Scope summary (engineering decisions locked)

- **Occlusion default flips to OFF** (`enableOcclusionCulling = false`) and the Mobile Preset turns it OFF, not
  ON. Hi-Z stays fully functional and opt-in for hilly/occluded terrain — no code removed, only the default.
- **#5 + #6 are ONE refactor.** Merge the three per-LOD visible-index `Append` buffers
  (`visibleLod0/1/2Buf`, each sized to `bladeCap`) into a **single packed `Append` buffer** with a 2-bit LOD
  tag in the high bits of each `uint` index. This cuts scratch from 12 B/blade → 4 B/blade (#5) AND drops the
  BladeCull SSBO binding count by 2 (#6). One change, both wins. *(Alternative — right-sizing three separate
  buffers to per-LOD fractions — is rejected: it risks overflow-dropped blades and still leaves 3 bindings.)*
- **#2 splits:** the preset-side guardrail (Mobile Preset must never switch the material off the opaque
  `GPUGrass/IndirectGrass` shader) lands in Phase 1; the shader-pass concern (keep `_ALPHACLIP` opt-in, don't
  regress `Cull Back`) is verified alongside Phase 3's shader edits.
- **Mobile Preset values (locked):** LOD `{8, 20}`, `targetDensityPerSqM = 0.5` (from 0.76),
  `minDensity = 0.4` (from 0.6), `shadowCastingMode = Off`, `receiveShadows = false`, `enableOcclusionCulling
  = false`, `renderCullDistance = 60` (from 70), `enableAdaptiveDensity = true`, `tierMode = Auto`. Preset stays
  play-tunable — these are aggressive defaults, not hard caps.
- **`_WIND_PERLIN` off on mobile** is enforced via the material/preset (keyword disabled), not by deleting the
  Perlin path (it stays available for high-end).
- **Struct-stride contract stays green.** `Tests/GpuStructStrideTests.cs` (20/24/8/32/48 B) must pass
  unchanged after every phase. The packed-index buffer (Phase 2) is a `uint` stream — it does not touch any of
  the five pinned structs, but Phase 2 adds its own EditMode test for the LOD-tag pack/unpack round-trip.

## Components → phase + file ownership

| Component | Type | Phase | Owns |
|---|---|---|---|
| `enableOcclusionCulling` default `true → false` | config | P1 | `Runtime/GpuGrassConfig.cs` (modify, line 96) |
| `ApplyMobilePreset` — expand to locked values + occlusion OFF | editor | P1 | `Editor/GpuGrassSceneWindow.cs` (modify, lines 368-383) |
| Preset opaque-shader guardrail (assert material on `GPUGrass/IndirectGrass`) | editor | P1 | `Editor/GpuGrassSceneWindow.cs` (modify) |
| EditMode test: preset applies locked values incl. occlusion=false | test | P1 | `Tests/MobilePresetTests.cs` (new) |
| `docs/GPUGrass.md` §8 status update (mark #1/#4/#2-preset done) | doc | P1 | `docs/GPUGrass.md` (modify) |
| Merge `visibleLod0/1/2Buf` → single packed `visibleBladesBuf` (2-bit LOD tag) | runtime | P2 | `Runtime/Render/GpuGrassRenderer.cs` (modify, lines 200-207, 387-470) |
| BladeCull: append to one buffer with packed LOD tag; per-LOD draw-arg counts derived | shader | P2 | `Shaders/GrassCull.compute` (modify) |
| Indirect shader VS: unpack LOD tag from `_VisibleIndices` | shader | P2 | `Shaders/GpuGrassIndirect.shader` (modify) |
| EditMode test: LOD-tag pack/unpack round-trip (pure C# mirror) | test | P2 | `Tests/LodTagPackTests.cs` (new) |
| Skip trail-bend loop in DepthOnly + ShadowCaster passes | shader | P3 | `Shaders/GpuGrassIndirect.shader` (modify) |
| LOD1/LOD2 skip interactor + trail touch-bend (keyword/branch on LOD) | shader | P3 | `Shaders/GpuGrassIndirect.shader` (modify) |
| Confirm `_ALPHACLIP` stays opt-in + `Cull Back` intact (guardrail #2) | shader | P3 | `Shaders/GpuGrassIndirect.shader` (verify) |
| `docs/GPUGrass.md` §8 status update (mark #5/#6/#3/#2 done) | doc | P2,P3 | `docs/GPUGrass.md` (modify) |

## Phases

- **Phase 1 — Config defaults + aggressive Mobile Preset + opaque guardrail (#1, #4, #2-preset)** — flip
  `enableOcclusionCulling` default to false; expand `ApplyMobilePreset` to the locked values with occlusion
  OFF; add a guardrail that the preset never moves the material off `GPUGrass/IndirectGrass`; EditMode-test the
  preset. Pure C#/config — fully batch-verifiable, no GPU. | Effort: S
- **Phase 2 — Merge per-LOD visible-index append buffers (#5 + #6)** — replace the three `bladeCap`-sized
  `Append` buffers with ONE packed `Append` buffer (2-bit LOD tag in the index uint); update `BladeCull` to
  append once with the tag and derive per-LOD draw counts; update the indirect shader VS to unpack the tag.
  Cuts ~8 B/blade and 2 SSBO bindings. Pure-C# pack/unpack helper EditMode-tested; live Play-mode GPU smoke
  (draw-call count + visual parity + blade count unchanged). | Effort: M
- **Phase 3 — Depth/Shadow deform lightening + LOD touch-bend skip (#3, #2-shader)** — skip the ≤128-iteration
  trail-segment loop (and ≤16 interactor loop) in the DepthOnly + ShadowCaster passes; make LOD1/LOD2 skip
  touch-bend; verify `_ALPHACLIP` stays opt-in and `Cull Back` is intact. Silhouette-error check + on-device
  GLES3 profiling before sign-off. | Effort: M

## Feasibility

- **Reuse check:** every edit site is confirmed at exact line numbers (config line 96, preset lines 368-383,
  buffer alloc lines 200-207, cull record 387-470). No new subsystem — this is surgical modification of
  existing seams.
- **#5+#6 unification is the load-bearing insight:** the three `visibleLodN` buffers already share a stride
  (`sizeof(uint)`) and are consumed the same way (indirect draw per LOD). Packing a 2-bit LOD tag into the top
  of each index is a well-trodden GPU pattern and is the single change that satisfies both improvements.
- **Batch-verify discipline (`ai-velocity-batch-compile-unity.md`):** P1 is C#-only and verifies in one
  compile. P2/P3 touch compute + shader — edit the full batch, then verify ONCE (read_console for all errors +
  run EditMode tests), collect the whole error set, fix in one pass. Compute/shader correctness needs a live
  editor Play-mode smoke; that is the verification gate, not a blocker to batch-editing.

## Dependencies

- **P1 is independent** and lands first (config + editor only; nothing depends on it).
- **P2 blocks P3 at the shader-source level** — both edit `GpuGrassIndirect.shader`. P2 changes the VS index
  read (unpack LOD tag); P3 changes the VS deform branches. Sequencing P2→P3 avoids a merge on the same VS
  function. (They *could* be split by owner with a worktree, but single-agent sequential is simpler and the
  shader is one file — no parallel-teammate git-index race to manage.)
- **Critical path:** P1 (fast) ∥ start; P2 → P3 on the shader. On-device profiling is the gate for P2 and P3
  sign-off (P1 needs no device — it's defaults + preset).

## Guards (mandatory)

1. **Occlusion stays functional, only the default flips.** No Hi-Z code deleted. `enableOcclusionCulling` still
   turns the whole pipeline on for hilly terrain; fail-open behavior unchanged. A scene that had it on keeps it
   on (existing serialized configs are not rewritten by a default change).
2. **Struct-stride contract green after every phase.** Run `Tests/GpuStructStrideTests.cs` — the 5 pinned
   structs (20/24/8/32/48 B) must be byte-identical. Phase 2's packed buffer is a `uint` stream and must not
   alter `GpuGrassBladeInstance` or any GPU struct.
3. **Blade count invariant (Phase 2).** After the append-buffer merge, the total rendered blade count for a
   given camera/frame must equal the pre-refactor count (the LOD tag redistributes indices; it must not drop
   or duplicate any). Assert via draw-arg sums in the Play-mode smoke.
4. **Depth/shadow silhouette parity (Phase 3).** Skipping trail-bend in Depth/Shadow passes must not visibly
   change the shadow/depth silhouette (trail bend is a small per-blade lean). Eyeball a moving trail
   interactor's shadow before/after; error must be imperceptible. If a case shows visible shadow popping, gate
   the skip behind a keyword instead of removing it outright.
5. **No regression of the already-correct mobile wins.** `Cull Back`, opaque default, 20 B blade stride,
   stripped fragment interpolators, GPU-side density thinning — none may be touched. Phase 3 explicitly
   verifies `Cull Back` + `_ALPHACLIP`-opt-in survive.
6. **Preset never breaks rendering.** The opaque-shader guardrail (P1) must ensure `ApplyMobilePreset` cannot
   leave the material on a non-indirect shader (URP/Lit) — that silently renders nothing (a known GPUGrass
   failure mode noted in the README's auto-wire/self-heal).

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|---|---|---|---|---|
| P2 packed-index LOD-tag pack/unpack mismatch C#↔HLSL → wrong-mesh blades or dropped indices | 3 | 5 | 15 | Guard 3 (blade-count invariant) + a pure-C# pack/unpack EditMode test mirroring the HLSL (Phase 2, like `LodThresholds`). Play-mode smoke asserts per-LOD draw counts sum to total. HIGH — gates P2. |
| Compute/shader edits unverifiable in batch (need live editor) | 4 | 2 | 8 | Extract the LOD-tag pack/unpack as pure C# (EditMode-tested); the compute/shader side is an in-editor Play-mode smoke (draw-call count + visual). Standard GPUGrass verification pattern (mirrors HiZMathTests). |
| P3 depth/shadow deform skip causes visible shadow popping on trail bend | 2 | 3 | 6 | Guard 4 — eyeball moving-trail shadow before/after; if visible, keyword-gate the skip instead of removing. Low likelihood (trail lean is small). |
| Occlusion-default flip surprises a scene that relied on the default-on | 2 | 2 | 4 | Guard 1 — existing serialized configs keep their stored value; only NEW configs default off. Documented in `docs/GPUGrass.md` §8 + preset log line. |
| Aggressive preset values too aggressive → sparse/ugly grass on a target device | 2 | 2 | 4 | Preset is play-tunable; values are defaults not caps. Document them; adjust after on-device look-dev. |
| `_WIND_PERLIN`/shadow keyword left on by a stale material | 2 | 2 | 4 | Preset disables the keyword + sets shadow flags via SerializedObject; guardrail (P1) re-asserts material shader. |

### Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: Config + Preset + guardrail (#1,#4,#2-preset) | S (~1d) | C#-only, batch-verifiable, no device needed. Land first. |
| Phase 2: Merge visible-index append buffers (#5+#6) | M (~2-3d) | Compute + shader + renderer; pack/unpack test + Play-mode smoke + on-device profile. |
| Phase 3: Depth/Shadow deform lightening (#3,#2-shader) | M (~2-3d) | Shader-only; silhouette check + GLES3 on-device profile. Depends on P2 (same VS file). |
| Total | ~5-7d | Critical path: P1 ∥ (P2 → P3). Device profiling gates P2/P3. |

## Verification per phase

- **P1:** compile clean; `MobilePresetTests` asserts every locked value incl. `enableOcclusionCulling == false`
  and material stays on `GPUGrass/IndirectGrass`; `GpuStructStrideTests` still green.
- **P2:** `LodTagPackTests` round-trips the 2-bit tag; Play-mode smoke — draw-call count still 3/field (or
  fewer if a LOD is empty), per-LOD draw-arg counts sum to the pre-refactor total (Guard 3), visual parity,
  buffer allocation reports 4 B/blade not 12 B/blade; `GpuStructStrideTests` green. On-device: memory delta
  confirmed on a ~1M-blade field.
- **P3:** Play-mode smoke — moving trail interactor, shadow/depth silhouette parity (Guard 4); `Cull Back` +
  `_ALPHACLIP`-opt-in confirmed in shader source (Guard 5). On-device GLES3: frame-time delta on a near-field
  grass-heavy view (the ALU win scales with near-blade coverage).

## Cook handoff

`/t1k:cook plans/260706-gpugrass-mobile-optimization/plan.md`

Recommended order: land **Phase 1** and profile the occlusion-off default on target hardware first (highest
leverage, lowest risk, no shader work), then proceed to **Phase 2 → Phase 3**.
