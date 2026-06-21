# Phase 2 — Build validator (M)

**Priority:** P1 — regression insurance. **Effort:** M. **Independent / parallel-safe after Phase 1.**

## Objective

Add an `IPreprocessBuildWithReport` that fails the Android build if the mobile render config drifts from the verified-safe Medium configuration. Zero direct FPS gain; insures against silent **−15–25 fps** regressions — most importantly the original CPU-tier disaster (a `scatterForceTier == ForceCpu` serialized footgun that bypasses `GrassTierProbe` and ships the 20-fps CPU scatter path).

## Design

A single editor build pre-processor that, for the Android target, asserts the serialized config the brainstorm verified as the safe steady state. On any violation it throws `BuildFailedException` with the offending value + the required value. Reads the serialized assets directly (data-driven; no hardcoded duplicate of the config).

### Assertions (Android target only)

| Assertion | Source | Required |
|---|---|---|
| Default quality level == Medium | `QualitySettings` (Medium is index **2**: VeryLow=0, Low=1, Medium=2) | active/default Android quality resolves to Medium |
| URP active asset HDR off | `Medium.asset` `m_SupportsHDR` | 0 |
| MSAA == 1 | `Medium.asset` `m_MSAA` | 1 |
| Main-light shadowmap ≤ 1024 | `Medium.asset` `m_MainLightShadowmapResolution` | ≤ 1024 |
| Shadow distance ≤ 25 | `Medium.asset` `m_ShadowDistance` | ≤ 25 (currently 20) |
| AlwaysIncludedShaders ⊇ `WorldPainter/IndirectGrass` | `GraphicsSettings` `m_AlwaysIncludedShaders` | contains the indirect grass shader (else the grass shader is stripped from the build → invisible grass) |
| **Block `scatterForceTier == ForceCpu`** | every `WorldPainter` component / prefab serialized `scatterForceTier` (enum `WorldPainter.ScatterTierMode` in `WorldPainter.Scatter.cs`) | NEVER `ForceCpu` — this is the 20-fps CPU-tier footgun that bypasses `GrassTierProbe` |

The ForceCpu check is the load-bearing one: scan scenes/prefabs that carry a `WorldPainter` for a serialized `scatterForceTier` set to `ForceCpu` (index for `ForceCpu` in the `ScatterTierMode` enum) and hard-fail.

## File ownership

- **Create:** `Assets/WorldPainter/Editor/Build/MobileRenderConfigValidator.cs` — new `#nullable enable` class implementing `IPreprocessBuildWithReport` (`callbackOrder` early). `this.` prefix, camelCase fields. Throws `BuildFailedException` on any violation. Only runs assertions when `report.summary.platform == BuildTarget.Android`.
- **Read-only reference (asserted against):** `Assets/URPDefaultResources/Medium.asset`, `ProjectSettings/QualitySettings.asset`, `ProjectSettings/GraphicsSettings.asset`, `Assets/WorldPainter/Runtime/WorldPainter.Scatter.cs` (the `ScatterTierMode` enum SSOT), `Assets/WorldPainter/Runtime/Scatter/GrassTierProbe.cs` (what ForceCpu bypasses).
- **Asmdef:** the validator lives under the existing `WorldPainter.Editor` asmdef.

## Step-by-step tasks

1. Implement `IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport report)`. Early-return if not Android.
2. Resolve the active URP asset for the default Android quality tier and assert HDR/MSAA/shadowmap/shadowDistance.
3. Assert default quality index resolves to Medium (index 2) for Android.
4. Assert `GraphicsSettings.m_AlwaysIncludedShaders` contains `WorldPainter/IndirectGrass`.
5. Scan all `WorldPainter` authoring components (scenes in build + prefabs) for `scatterForceTier == ForceCpu`; collect every offender path.
6. Aggregate all violations into one message and `throw new BuildFailedException(...)` listing each offending value, its location, and the required value (clear, actionable).

## On-device verification gate (PASS criteria)

This phase's gate is a **build-time** gate (it is the test), but the on-device confirmation that it protects the right thing:

- [ ] A deliberately-broken build (set one `scatterForceTier = ForceCpu`, or flip `m_SupportsHDR = 1`) **fails the build** with a clear message naming the offender. (Run each violation once.)
- [ ] The known-good config **builds successfully** and the resulting on-device build shows **"Grass tier: GPU"** (green) in the PerformanceConsole — confirming the validator's guard corresponds to the real on-device tier.
- [ ] AlwaysIncludedShaders assertion proves out: removing the indirect grass shader from the list fails the build (do not ship without grass).

## Risk note

R7 (false-positive blocks legitimate build) — Likelihood 2 / Impact 2 / score 4. Mitigation: assertions read the exact serialized config the brainstorm verified; messages name the value + fix so a legitimate config change is a one-line allow-list edit, not a mystery. This phase adds no runtime code and cannot regress on-device FPS.
