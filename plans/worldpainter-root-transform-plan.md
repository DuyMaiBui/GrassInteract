# Plan: WorldPainter Root-Local Space (full non-uniform TRS, live-follow)

**Created:** 2026-06-17 08:57 · **Engine:** Unity 6 URP, GPU-driven · **Repo:** C:\Works\Unity\The1\GrassInteract
**Branch suggestion:** `feat/worldpainter-root-transform`

## Goal (one sentence)

Reinterpret every existing "world XZ" coordinate in WorldPainter (terrain + grass + props) as **painting space** (= WorldPainter root GameObject LOCAL space), and apply the root's **full non-uniform TRS** (position + rotation + per-axis scale) to map painting space → world space LIVE every LateUpdate, in lockstep across render, culling, colliders, and editor brush/sculpt/raycast — with an **identity transform producing byte-identical output** to today.

## Locked requirements (do NOT re-litigate)

1. Full non-uniform TRS (T + R + per-axis S).
2. Lockstep across render, culling (CDLOD + grass/prop compute), colliders (terrain + instance), AND editor brush/sculpt/raycast.
3. Live-follow: root moved at runtime/editor → everything updates same frame.
4. Identity transform ⇒ no visual regression vs current behavior.
5. Terrain stochastic anti-tiling (`_TERRAIN_STOCHASTIC` in `TerrainPalette.hlsl`) keeps working.

---

## Core architectural decision (the SSOT seam)

**Two coordinate spaces, one matrix pair, pushed once per LateUpdate:**

- **Painting space (P):** what ALL existing code already computes (`TerrainWorldGrid` "world XZ", blade `posWS`, instance `posWS`, CDLOD node offsets, collider bake coords). **No existing CPU math that operates in painting space changes.** It is simply renamed in intent: "world" → "painting".
- **World space (W):** P transformed by the root's TRS.
- **`M = root.localToWorldMatrix`** (`float4x4`), **`Minv = root.worldToLocalMatrix`**, **`Nmat = transpose(inverse((float3x3)M))`** (normal matrix; correct under non-uniform scale).

Three global uniforms set once per LateUpdate by a NEW SSOT helper:
`_WPLocalToWorld` (`float4x4`), `_WPWorldToLocal` (`float4x4`), `_WPNormalMatrix` (`float3x3` packed as `float4x4` for SetGlobalMatrix; shader reads upper-left 3×3).

**Render seam** (GPU, all 3 paths + shadow passes): the LAST line that produces a painting-space `posWS` is followed by `posW = mul(_WPLocalToWorld, float4(posWS,1)).xyz` before `TransformWorldToHClip`. Terrain normal: `normalW = normalize(mul((float3x3)_WPNormalMatrix, normalP))`.

**Cull seam** (CPU, all paths): transform the CAMERA into painting space (`camP = Minv.MultiplyPoint3x4(camW)`) and the FRUSTUM PLANES into painting space (plane transform under non-uniform scale = inverse-transpose, derived below). Then `CdlodQuadtree.Select`, `GrassCull.compute`, and the terrain frustum test run UNCHANGED in painting space.

**Collider seam:** parent the collider-host GameObjects under the root transform → Unity applies the TRS for free; bake coords stay painting-space. (Chosen over transforming bake coords; justification in P4.)

**Editor seam:** raycast hit (world) → `root.InverseTransformPoint(hit)` → painting space → existing tile/UV resolve unchanged; brush-preview gizmo drawn with `Gizmos.matrix = root.localToWorldMatrix`.

**Identity gate:** keyword-gated `multi_compile _ _WP_ROOT_TRANSFORM` on every affected pass. OFF variant is byte-identical to today (zero added ALU, satisfies req 4 and the stochastic-coexistence constraint). Recommended over always-on identity multiply — see § "Design decision: identity gating".

---

## Frustum-plane transform under non-uniform scale (the load-bearing math)

A plane `(n, d)` in world space (`dot(n, p) + d = 0`) maps to painting space by the inverse-transpose of the **point** transform that takes painting→world. Painting→world point transform is `M`. So a plane expressed in world transforms to painting space by `M^T` applied as a 4-vector:

```
planeP = transpose(M) * planeW4      // planeW4 = (n.x, n.y, n.z, d)
```

then renormalize `planeP.xyz` (and scale `planeP.w` by the same factor) so the positive-vertex test in `GrassCull.compute` / terrain cull stays metric-correct. This is the standard "planes transform by inverse-transpose of the inverse" = `transpose(M)` identity. **This CPU math MUST get an EditMode test** (see P3 / P6).

> Note: `GeometryUtility.CalculateFrustumPlanes(cullCam, ...)` yields WORLD planes. We then push them through `transpose(M)` + renormalize to get painting-space planes for the compute/cull buffers. CDLOD `Select` only needs `camP` (it is XZ-distance based, no planes).

---

## Phases (each independently compilable)

### Phase 1 — Shared binder + uniforms + terrain render & shadow — Effort: M

**Files owned (new):**
- `Runtime/WorldRootBinder.cs` (NEW) — SSOT. Computes `M`, `Minv`, `Nmat` from `transform`; exposes `PushGlobals()` (sets the 3 global matrices + enables/disables `_WP_ROOT_TRANSFORM` global keyword), and helper `WorldToPainting(Vector3)` / `PaintingToWorld`, `TransformFrustumPlanesToPainting(Plane[] worldPlanes, Vector4[] outPainting)`. Property IDs live here as named `static readonly int` (mirror `TerrainShadingConfig` pattern).

**Files owned (edit):**
- `Runtime/WorldPainter.cs` — in `LateUpdate()`, BEFORE `SubmitTerrain()`, call `binder.PushGlobals()`. Instantiate binder in `OnEnable`/`Awake`.
- `Shaders/TerrainPatch.shader` — forward `vert` (~line 157) and `vertShadow` (~line 268): after `posWS` computed, gate `posWS = mul(_WPLocalToWorld, float4(posWS,1)).xyz` under `#if _WP_ROOT_TRANSFORM`. Add `#pragma multi_compile _ _WP_ROOT_TRANSFORM` to BOTH passes. Declare the 3 matrices in `TerrainVtf.hlsl` (shared include, SSOT — both passes already include it).
- `Shaders/TerrainNormals.hlsl` (`DeriveNormalWS`) — transform the derived normal by `_WPNormalMatrix` under the same gate. The height-derivative is computed in painting space (correct); only the final normal needs the normal-matrix to be world-correct for lighting.
- `Runtime/Terrain/GpuTerrainEngine.cs` — material-clone keyword propagation (~line 227) ALREADY copies `shaderKeywords`; since `_WP_ROOT_TRANSFORM` is a GLOBAL keyword (`Shader.EnableKeyword`), not material-local, the clone path is unaffected — **verify** the global keyword reaches the indirect draw (global keywords do; documented caveat resolved).

**Verify:** terrain renders identical with identity root; non-identity root translates/rotates/scales the lit surface AND its shadow together.

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Shadow pass misses the transform → shadows desync from lit surface | 3 | 4 | 12 | Same gated multiply in `vertShadow`; A/B screenshot with R=45° check |
| Normal-matrix wrong → terrain lighting wrong under non-uniform scale | 3 | 4 | 12 | EditMode test for `Nmat = transpose(inverse(M3x3))`; visual A/B under scale=(2,1,0.5) |
| Global keyword not reaching indirect draw clone | 2 | 4 | 8 | Global (not material) keyword; confirm via frame-debugger variant in P6 |

### Phase 2 — Grass + prop render & shadow — Effort: M

**Files owned (edit):**
- `Shaders/ScatterInstanced.shader` — `TransformInstance` (~line 354 static path AND ~line 363 deform path) produce painting-space `posWS`; gate-multiply the FINAL `posWS` by `_WPLocalToWorld` and `normalWS` by `_WPNormalMatrix` after both branches converge (single seam at function exit, not per-branch). `#pragma multi_compile _ _WP_ROOT_TRANSFORM`. Include the matrix declarations (shared `.hlsl` snippet or local cbuffer matching P1).
- `Shaders/GrassInteractIndirect.shader` + `Shaders/GrassInteractInstanced.shader` — same final-`posWS` gate-multiply + normal-matrix; add the pragma. Apply to their ShadowCaster passes too.

**Interactor conversion (REQUIRED — user confirmed interactors are WORLD-authored):** grass/prop wind & bend math evaluates in PAINTING space (reads `posWS.xz` + interactor positions). Grass interactors (`GrassInteractor.Active`, `GrassTrailInteractor.Active`) are authored in WORLD space, so before they are uploaded/consumed, convert each interactor position via `Minv` (`WorldRootBinder.WorldToPainting`) → painting space. Add this conversion at the interactor-gather/upload seam in `GrassGpuEngine`/the grass layer (find where `GrassInteractor.Active` feeds the bend uniforms/buffer). Keep wind/bend evaluation in painting space; only the final vertex maps back to world via `_WPLocalToWorld`.

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Interactor world→painting conversion missed → grass bends at wrong spot under non-identity root | 3 | 3 | 9 | Convert `GrassInteractor.Active`/`GrassTrailInteractor.Active` via `Minv` at the upload seam (confirmed world-authored); EditMode round-trip on the conversion |
| Per-branch double-multiply (static vs deform) | 2 | 3 | 6 | Single seam at `TransformInstance` exit, after branch merge |
| Shadow pass omitted for grass/prop | 3 | 3 | 9 | Grep every Pass for `TransformWorldToHClip`; gate each |

### Phase 3 — Cull seams (camera + frustum into painting space) + LOD-under-scale — Effort: M

**Files owned (edit):**
- `Runtime/Terrain/GpuTerrainEngine.cs` (~line 300, `cameraPos`; ~line 315 frustum planes) — feed `Select(camP)` and push `transpose(M)`-transformed + renormalized planes to the cull buffer.
- `Runtime/Scatter/GrassGpuEngine.cs` (~line 437/455 `_CamPosWS`) — set `_CamPosWS` to `camP` (painting space); push painting-space frustum planes to `GrassCull.compute`.
- `Runtime/Scatter/InstancedPropEngine.cs` — same (reuses `GrassCull.compute`).
- `Runtime/WorldRootBinder.cs` — add `WorldToPainting` for camera + the plane transform helper (used by all 3).

**LOD-under-scale decision (REQUIRED call — stated, not open):** camera + nodes both live in painting space, so distance-LOD is self-consistent in painting (local) units. Under non-uniform world scale, screen-projected texel size diverges from painting-space distance. **Decision: (a) accept painting-space LOD** for v1 — it is correct in local units, deterministic, and matches the "identity ⇒ no regression" gate exactly (identity scale = no divergence). Rationale: a representative-scale LOD-range multiply (option b) is ill-defined under NON-uniform scale (which axis?), adds a tuning knob, and risks CDLOD crack-invariant drift. If shipping at extreme scale reveals LOD popping, P-future can multiply `lodRanges` by `cbrt(det(M3x3))` (uniform-equivalent scale) — noted as a follow-up, NOT in scope.

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Plane transform wrong → grass/terrain culled incorrectly under rotation/scale | 4 | 4 | **16** | **EditMode test** for `transpose(M)` plane transform + renormalize vs brute-force point-in-frustum; mitigate BEFORE P3 ships |
| CDLOD XZ-only metric invariant broken (crack regression) | 3 | 5 | **15** | Select runs UNCHANGED in painting space (camP.xz vs node.xz); morph metric in VS still painting-XZ — invariant preserved by construction; **verify** crack-free under R=0 then R≠0 |
| LOD popping at extreme world scale | 2 | 2 | 4 | Documented decision (a); follow-up multiplier noted |

### Phase 4 — Colliders (terrain + instance) — Effort: M

**Files owned (edit):**
- `Runtime/Terrain/TerrainColliderProvider.cs` / `TerrainColliderStreamer.cs` / `TerrainColliderRing.cs` — parent the collider-host GameObjects under the WorldPainter root transform; keep bake heights/coords in painting space (host localPosition = painting coord). Unity applies root TRS → world colliders track render. The collider streamer's ring center must use the CAMERA IN PAINTING SPACE (`camP`) to pick which tiles to host (same `Minv` transform as P3).
- `Runtime/Scatter/InstanceVisibilityColliderDriver.cs` / `InstanceColliderPool.cs` — pooled instance colliders parented under root; instance `posWS` (painting) becomes host localPosition.

**Decision (stated):** parent-under-root over transform-bake-coords because (1) Unity's transform hierarchy is the live-follow mechanism for free — no per-frame collider re-bake when root moves; (2) non-uniform scale on a MeshCollider is handled by Unity's transform (with the known caveat that MeshCollider under non-uniform scale is supported but skewed-correctly by Unity); (3) zero added math in the hot path. Caveat to verify in P6: PhysX MeshCollider cooking under non-uniform parent scale.

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| MeshCollider under non-uniform parent scale mis-cooks / perf cost | 3 | 4 | 12 | Verify in P6 with scale=(2,1,0.5); fall back to box/convex approximations if cooking stalls |
| Collider ring picks wrong tiles (camera still world-space) | 3 | 3 | 9 | Route ring center through `camP` (Minv) — same helper as P3 |
| Re-parenting breaks existing pooling lifecycle | 2 | 3 | 6 | Parent at pool-spawn; keep Recycle path; no structural pool change |

### Phase 5 — Editor brush / sculpt / raycast — Effort: M

**Files owned (edit):**
- `Editor/Brush/TerrainPaintTargetResolver.cs` — the `Vector2 worldCenter` it receives must be PAINTING-space. Convert the scene-raycast hit (world) via `root.InverseTransformPoint(hit)` at the call site, then resolve tiles via `TerrainWorldGrid` unchanged.
- `Editor/Brush/TerrainSculptRtWriteback.cs` — writeback already operates in painting/tile space → unchanged, but confirm its input center is the painting-space center from the resolver.
- `Editor/Brush/TerrainBrushPreview.cs` — draw the preview gizmo/mesh with `Handles.matrix`/`Gizmos.matrix = root.localToWorldMatrix` so the brush ring sits on the transformed surface. Raycast still hits the rendered (world) terrain; the gizmo is drawn in painting space under the root matrix.

**Minimal-edit principle:** only the world↔painting boundary moves; tile/UV math stays. The boundary is exactly: (a) raycast hit world→painting on input, (b) gizmo painting→world on output.

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Raycast hits world terrain but resolver expects painting → brush paints wrong tile under non-identity root | 4 | 4 | **16** | **EditMode test** for `InverseTransformPoint` round-trip mapping; mitigate before P5 ships |
| Brush preview desync (ring floats off surface) under rotation/scale | 3 | 2 | 6 | `Handles.matrix` = root TRS; visual check |
| Sculpt writeback double-transformed | 2 | 4 | 8 | Writeback stays painting-space; only resolver input is converted (single boundary) |

### Phase 6 — Validation — Effort: M

- **EditMode tests (CPU math — `execute_code` is BROKEN in this env, use `run_tests`):**
  - `Nmat = transpose(inverse((float3x3)M))` for non-uniform scale + rotation (P1).
  - Frustum-plane painting-space transform `transpose(M)` + renormalize vs brute-force point-in-frustum oracle (P3) — table of points inside/outside.
  - `InverseTransformPoint` mapping round-trip: world→painting→world identity, and painting-space tile resolve matches pre-feature for identity root (P5).
  - Identity-root: `M == Minv == I` ⇒ all transforms are no-ops (regression guard for req 4).
- **Play-mode visual A/B (no runtime shader unit test possible):**
  - Identity root: capture terrain+grass+prop+shadow screenshots; compare to pre-feature baseline → byte-identical (req 4).
  - Non-identity root (T=(10,0,5), R=45°, S=(2,1,0.5)): terrain, grass, props, shadows, colliders all move in lockstep; CDLOD crack-free; stochastic anti-tiling still visible; move root at runtime → live-follow.
- **Frame-debugger:** confirm `_WP_ROOT_TRANSFORM` ON variant is the one drawn when root active; OFF variant byte-identical.

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| `execute_code` broken hides a CPU math bug | 3 | 4 | 12 | Route ALL CPU matrix/plane math through EditMode `run_tests`, not execute_code |
| Visual A/B subjective → misses subtle regression | 2 | 3 | 6 | Identity-root path asserts byte-identical via screenshot diff, not eyeball |

---

## Design decision: identity gating (recommended)

**Keyword-gated `multi_compile _ _WP_ROOT_TRANSFORM`** (recommended) over always-on identity-matrix multiply:
- OFF variant = zero added ALU, byte-identical to today → satisfies req 4 at compile level, not just numerically.
- Matches the existing `_TERRAIN_STOCHASTIC` pattern (team precedent, req 5 coexistence proven).
- Global keyword (`Shader.EnableKeyword`/`DisableKeyword`) — set by `WorldRootBinder.PushGlobals()` when root ≠ identity, cleared when identity. The terrain material clone copies `shaderKeywords` but global keywords are not material-local, so the recent clone-keyword bug does NOT affect this feature.

Trade-off: +1 variant per pass (×~6 passes). Acceptable; all passes are project-owned.

---

## Feasibility

- **Reuse check:** NEW `WorldRootBinder.cs` (no existing origin/offset/`SetGlobalMatrix` system — scout-confirmed). Everything else EDITS existing seams. `GrassCull.compute` reused by both grass and props (one shader edit covers two paths). Shared matrix-declaration `.hlsl` snippet avoids DRY violation across 4 shaders.
- **Complexity:** moderate. The math is standard (TRS + normal matrix + plane inverse-transpose); the risk is breadth (6 GPU passes + 3 cull paths + colliders + editor) and the CDLOD crack invariant. No novel algorithms.

## Dependencies

- **Blocked by:** nothing external.
- **Critical path:** P1 (binder + uniforms are the SSOT every later phase consumes) → P3 (cull math, highest-risk EditMode test) → P6.
- **Parallel-safe:** P2 (grass/prop render) and P4 (colliders) and P5 (editor) are independent of each other once P1 lands; they share only `WorldRootBinder` (read-only consumers). P2/P4/P5 can fan out after P1.
- **File-ownership conflicts:** none cross-phase except `WorldRootBinder.cs` (P1 creates; P3 extends with plane/camera helpers — sequence P1→P3 for that file).

## Risk register (scores ≥15 — mitigation mandated BEFORE phase starts)

| Risk | Phase | Score | Mandated mitigation |
|---|---|---|---|
| Frustum-plane transform wrong | P3 | 16 | EditMode test (`transpose(M)` + renormalize vs point oracle) MUST pass before P3 cull edits |
| Editor raycast world/painting mismatch | P5 | 16 | EditMode round-trip test MUST pass before P5 resolver edit |
| CDLOD XZ-metric crack regression | P3 | 15 | Preserved by construction (Select unchanged in painting space); crack-free visual A/B at R≠0 gates P3 |

## Backwards compatibility

Additive + identity-gated. Identity root ⇒ byte-identical (req 4). No migration needed for existing scenes (default root transform is identity). Breaking only if a scene already placed the WorldPainter root at a non-identity transform AND relied on the old behavior (content moved) — flag for the user (see Unresolved).

## Timeline

| Phase | Effort | Notes |
|---|---|---|
| P1 binder + uniforms + terrain render/shadow | M | Critical path head; SSOT for all |
| P2 grass + prop render/shadow | M | After P1; parallel with P4/P5 |
| P3 cull seams + LOD decision | M | After P1; score-16 + score-15 gates |
| P4 colliders | M | After P1; parallel with P2/P5 |
| P5 editor brush/sculpt/raycast | M | After P1; score-16 gate |
| P6 validation | M | After all; EditMode + Play A/B |
| **Total** | **~6×M** | Critical path: P1 → P3 → P6 |

## Success criteria (objective, reproducible)

1. Identity root: terrain+grass+prop+shadow screenshots byte-identical to pre-feature baseline (frame-debugger confirms `_WP_ROOT_TRANSFORM` OFF variant).
2. Non-identity root T=(10,0,5) R=45° S=(2,1,0.5): all of {terrain lit, terrain shadow, grass, props, terrain collider, instance collider, brush paint location} move in lockstep with the rendered surface.
3. Moving the root at runtime (Play) and in Editor updates all of the above same-frame (live-follow).
4. CDLOD crack-free at R≠0 (XZ invariant preserved).
5. `_TERRAIN_STOCHASTIC` anti-tiling still visible under non-identity root (req 5).
6. All EditMode CPU-math tests pass via `run_tests` (normal matrix, plane transform, InverseTransformPoint round-trip, identity no-op).

## Cook handoff

`/t1k:cook plans/worldpainter-root-transform-plan.md` — start P1 (critical-path head, SSOT binder). Gate P3 and P5 on their EditMode tests (scores 16/16/15) BEFORE the corresponding render/resolver edits. Validate via `run_tests` (EditMode) + Play-mode A/B; `execute_code` is BROKEN in this env — do NOT use it for the matrix/plane verification.

## Resolved design decisions (user-confirmed 2026-06-17)

1. **Existing non-identity content → RESOLVED: all roots identity.** Every current scene has the WorldPainter root at identity, so the feature changes nothing for existing content. **No migration phase.** (Confirms the "Backwards compatibility" section.)
2. **GrassInteractor / trail authoring space → RESOLVED: WORLD space.** Runtime grass interactors are authored in world space, so the binder MUST convert their positions to painting space via `Minv` before upload — see P2 (now a concrete required seam, not a "verify").
