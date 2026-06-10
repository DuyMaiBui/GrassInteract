# Scatter LOD Cull Fix + Editor Bar — Adversarial Code Review

**Scope:** `git diff c1c35b7..HEAD -- Assets/GrassInteract/` (commits 4804f2b, 52eb0f8, 0fc795a)
**Reviewer:** t1k-code-reviewer (adversarial pass)
**Date:** 2026-06-10

## VERDICT: APPROVE-WITH-NITS

The core change is correct, SSOT-clean, and well-tested for the documented happy path. The contract change is fully propagated, all three engines use `RenderCullDistance²` with the `isPlaying` branch removed, the collider `cullDistance` field is untouched, and the editor bar is SSOT-correct via `ApplyModifiedProperties`. One **Important** behavioural inconsistency (CPU vs GPU handling of `cull == 0`) and a few minor nits below.

---

## Findings

| Severity | Location | Issue | Fix |
|---|---|---|---|
| Important | GrassGpuEngine.cs:209 / InstancedPropEngine.cs:191 vs GrassRenderer.cs:104 + GrassCull.compute:115,221 | **CPU/GPU divergence when `cull == 0`.** `GrassRenderer` guards `if (cullSqrDistance > 0f && sqrDist > cullSqrDistance)` → cull=0 means "no cull, render all". The two GPU engines set `maxSqrDistance = 0*0 = 0` and feed it to `GrassCull.compute`, which does `if (sqrDist > maxCullSqrDistance) return;` with **no `> 0` guard** → cull=0 means "cull EVERYTHING". Same field, opposite behaviour across engines. The OnValidate migration normally prevents 0 from persisting, but a `cull==0` config CAN reach the GPU path: (a) asset whose `OnValidate` never fired this session before play (e.g. AssetBundle / Addressables-loaded layer, or a runtime-constructed config), (b) a fresh asset before first Inspector touch. Result: a field silently renders nothing in play, nothing logged. | Either add a `> 0` guard in the compute shader (`maxCullSqrDistance <= 0 || sqrDist <= maxCullSqrDistance`) to match GrassRenderer, OR clamp `maxSqrDistance` to a sentinel (`cull <= 0 ? float.MaxValue : cull*cull`) in both GPU engines. Pick one and make all three engines agree. Document the chosen `cull==0` contract. |
| Minor | InstanceScatterLayer.cs:182 / DensityScatterLayer.cs:122 | **Duplicated migration logic (DRY).** `MigrateRenderCullDistance()` is byte-identical in both layer classes (incl. doc-comment). Both derive from a common base (`ScatterLayer` — both expose `Render`). | Hoist to a shared `protected void MigrateRenderCullDistance(ref ScatterRenderConfig)` on the base, or a static helper on `ScatterRenderConfig`. Avoids drift if the formula changes. |
| Minor | LodDistanceBar.cs:194-195 | Switch-handle clamp uses `lo + 0.01f` / `hi - 0.01f` — undocumented magic epsilon. Not in the brief's allowed-constants list (500/2). Harmless but a literal. | Promote to a named `const float HANDLE_EPSILON = 0.01f;` with a one-line comment ("min band width to prevent inversion"). |
| Minor | LodDistanceBar.cs:101,129 | `handleDists[h] / cull` is unguarded — `cull` is proven `> 0` at line 55 before this runs, so safe today. But `SliceByDistance`/`Remap` are reached only after the same guard. | No action required; noting the dependency on the line-55 early-return. If the guard is ever moved, these divide-by-zero. |
| Nit | ScatterLodCullTests.cs | Tests are **pure-math spec-locks**, not behavioural. They re-implement `cull*cull` and the migration formula inline rather than exercising `GrassGpuEngine`/`InstancedPropEngine`/`MigrateRenderCullDistance()` directly. They would NOT catch the Important finding above (CPU/GPU cull=0 divergence) nor a regression where an engine stops reading `RenderCullDistance`. | Add one test that constructs an engine (or invokes the real `MigrateRenderCullDistance` via reflection on a `ScriptableObject` instance) and asserts the engine's `maxSqrDistance`. At minimum add a test asserting the `cull==0` contract once it is unified. |

---

## Six-point verification (evidence)

### 1. Contract change — all `new ScatterRenderConfig(` call sites pass the 4th arg ✅
`grep -rn "new ScatterRenderConfig(" Assets/` → 6 hits, all 4-arg:
- `DensityScatterLayer.cs:132` — passes `migrated` ✅
- `InstanceScatterLayer.cs:192` — passes `migrated` ✅
- `ScatterLodCullTests.cs:31,48,60,69,74` — pass `500f / 750f / 0f / 0f / 0f` ✅

No 3-arg caller remains → compiles. **PASS.**

### 2. All three engines use `RenderCullDistance²`, `isPlaying` branch removed ✅
- `InstancedPropEngine.cs:190-191`: `float cull = layer.Render.RenderCullDistance; this.maxSqrDistance = cull * cull;` — old `minCullSqr`/`Mathf.Max(...*4f, ...)` deleted (confirmed in diff). ✅
- `GrassGpuEngine.cs:208-209`: `float cull = render.RenderCullDistance; this.maxSqrDistance = cull * cull;` — old formula deleted. ✅
- `GrassRenderer.cs:69`: `float cull = render.RenderCullDistance; this.cullSqrDistance = cull * cull;` plus cull check at `:104`. ✅

`grep "Application.isPlaying"` against these three files → **0 hits** in the cull path. **PASS** (the engines agree on the formula; see Finding #1 for the `cull==0` edge behaviour divergence).

### 3. Migration correctness ✅ (with DRY nit)
- `> 0f` idempotency guard: `InstanceScatterLayer.cs:184` + `DensityScatterLayer.cs:124` `if (this.render.RenderCullDistance > 0f) return;` ✅
- `<2 LODs` path: `LodMaxDistances` (ScatterRenderConfig.cs:66) returns `Mathf.Max(0, src.Length-1)`-sized array → empty when <2 lods → `dists.Length > 0 ? ... : 0f` → `Mathf.Max(2*0, 500) = 500`, **no index exception**. ✅ (Test `Migration_BackfillsRenderCullDistance...` line 73-75 covers this.)
- `SetDirty`: `EditorUtility.SetDirty(this)` present in both. ✅
- `#if UNITY_EDITOR`: both `OnValidate` + migration wrapped. ✅

### 4. Regression on touchpoints — GPU uniform wiring ✅ (1 caveat)
- `GrassGpuEngine.cs:618` and `InstancedPropEngine.cs:538`: `cmd.SetComputeFloatParam(this.computeShader, "maxCullSqrDistance", maxSqrDistance);` — both still feed `maxCullSqrDistance` from the new `maxSqrDistance` (= `cull*cull`). No engine feeds a stale/0 literal. ✅
- `GrassCull.compute:46,115,221` reads the uniform unchanged. ✅
- **Caveat:** the *value* fed can be `0` (Finding #1). The wiring is intact; the semantics of `0` are the issue.
- `LodMaxDistances` / `lod0/lod1MaxSqrDist` math unchanged in all engines — only the far-cull line changed. No other consumer of `maxSqrDistance` silently altered. **PASS (wiring) / see Finding #1 (value semantics).**

### 5. Editor bar ✅
- Reads `renderCullDistance`: `LodDistanceBar.cs:48` `renderProp.FindPropertyRelative("renderCullDistance")` — NOT the collider `cullDistance`. ✅
- Writeback only via SerializedProperty: `:150` `cullProp.floatValue = newDist;` `:154-155` lod `maxDistance`, then `:157` `renderProp.serializedObject.ApplyModifiedProperties()`. No direct struct mutation. ✅
- Drag clamps prevent band inversion: `ClampHandle` (`:181-197`) — cull handle ≥ last switch (`:188`); switch handle clamped `[lo+ε, hi-ε]` (`:195`). ✅
- `LayerPanelView` re-finds the property inside the IMGUI lambda after rebind (`:152-153`) — correct, avoids stale-SerializedProperty crash. ✅

### 6. Collider-field collision ✅
`InstanceScatterLayer.cs:78` `cullDistance = 80f` (collider) and `:141` `CullDistance` accessor are **untouched** by the diff. New field is `renderCullDistance` on `ScatterRenderConfig` (distinct type, distinct name). Migration reads/writes only `RenderCullDistance`. `InstanceFrustumCuller.cs` still uses the collider `cullDistance`. **No collision. PASS.**

### Lint/convention
- `this.` prefix used throughout new code. ✅
- Magic numbers: `500f`/`2f` documented migration constants (allowed); `0.01f` handle epsilon is an undocumented literal (Minor nit #3); bar palette/layout literals are acceptable editor-chrome constants.
- New files: `LodDistanceBar.cs` (214 lines, under 200-line guidance for a single-responsibility editor widget — borderline, acceptable). `#nullable enable` present. ✅

---

## Summary
Solid, SSOT-correct fix that achieves the stated acceptance criteria (explicit cull, edit==play, idempotent migration, no collider collision, SSOT editor bar). The one thing to resolve before merge is the **CPU vs GPU `cull == 0` divergence** — unify the contract so a `0` cull can't silently blank a GPU field that the equivalent CPU path would render. Everything else is non-blocking polish.
