# Phase 3 — Hi-Z GPU occlusion (feature-flagged)

Goal: skip grass chunks hidden behind terrain/geometry via a shared per-camera Hi-Z depth pyramid,
tested per chunk AABB inside the existing ChunkCull compute pass. Behind `enableOcclusionCulling`
(landed in P1), fail-open to today's frustum+distance cull. Highest-risk phase; needs live editor +
GLES3 smoke.

## File ownership

- `Assets/GPUGrass/Runtime/Render/GpuGrassHiZ.cs` — new
- `Assets/GPUGrass/Runtime/Render/GpuGrassHiZFeature.cs` — new
- `Assets/GPUGrass/Shaders/HiZBuild.compute` — new
- `Assets/GPUGrass/Shaders/GrassCull.compute` — modify (ChunkCull occlusion test + binds)
- `Assets/GPUGrass/Runtime/Render/GpuGrassRenderer.cs` — modify (bind Hi-Z + prev-VP)
- `Assets/GPUGrass/Tests/HiZMathTests.cs` — new

## Design recap (from brainstorm)

- **Shared per-camera Hi-Z** — built ONCE per camera, consumed by ALL grass fields (not per field).
- **Source:** previous-frame `_CameraDepthTexture`, reprojected by prev→cur view-proj.
- **Reduction:** max-Z (farthest) → conservative; cull a chunk only when its NEAR depth is strictly beyond the Hi-Z FAR sample (Guard 4).
- **Fail-open:** missing Hi-Z / no depth texture / GLES gap → behave exactly like today (Guard 3).

## Tasks

1. **`HiZBuild.compute` — mip-chain generation.**
   - Kernel `CopyDepth`: linearize `_CameraDepthTexture` → mip 0 of an R32F (or RHalf) pyramid RT.
   - Kernel `ReduceMip`: each output texel = `max` of the 2×2 parent (conservative farthest). Loop over mips on the CPU side issuing one dispatch per level.
   - Cap base resolution to half-screen (perf); document the cap.

2. **`GpuGrassHiZ.cs` — per-camera Hi-Z buffer + reprojection state.**
   - Holds the pyramid `RenderTexture` (mip chain), mip count, base size.
   - Stores previous-frame `viewProjMatrix` for reprojection; exposes current + prev VP to the cull bind.
   - `bool IsReady` — false until a depth texture has been captured at least once (first-frame fail-open).
   - Keyed by `Camera` (static dictionary) so multiple cameras each get their own pyramid; cleaned on camera destroy / disable.
   - **Pure math (EditMode-tested):** `static bool TryProjectAabbToScreenRect(Bounds aabb, Matrix4x4 vp, out Rect screenRect, out float nearDepth)` and `static int SelectMip(Rect screenRect, int baseSize, int mipCount)`. Mirror `GpuGrassRenderer.LodThresholds` testability pattern (`internal` + `InternalsVisibleTo`).

3. **`GpuGrassHiZFeature.cs` — URP ScriptableRendererFeature.**
   - Enqueue a pass after `RenderPassEvent.AfterRenderingOpaques` that: ensures `_CameraDepthTexture` is requested (`ConfigureInput(ScriptableRenderPassInput.Depth)`), runs `HiZBuild.compute` to (re)build the camera's pyramid via `GpuGrassHiZ`.
   - **Depth-availability detection:** if the platform/URP gives no depth texture, mark the camera's Hi-Z `IsReady = false` and log once → renderer falls back (Guard 3).
   - Document that the user adds this feature to their URP Renderer asset (or auto-add via the P2 window's "Apply Mobile Preset"/setup if feasible — optional nicety, not required).

4. **`GrassCull.compute` ChunkCull — add occlusion test.**
   - New binds: `Texture2D hiZ`, `SamplerState`, `float4x4 prevViewProj`, `float2 hiZSize`, `int hiZMipCount`, `int occlusionEnabled`.
   - After the existing frustum + `maxCullSqrDistance` tests pass: if `occlusionEnabled != 0`:
     - Project the chunk AABB by `prevViewProj` → screen rect + chunk near depth.
     - Pick the mip whose texel covers the rect; sample Hi-Z far depth.
     - If `chunkNearDepth > hiZfar` (with a small epsilon) → mark occluded, do NOT append to `visibleChunks`.
   - `occlusionEnabled == 0` OR degenerate projection → keep the chunk (fail-open, Guard 3/4).

5. **`GpuGrassRenderer.cs` — bind Hi-Z into the cull dispatch.**
   - In `RecordFrameCommands`, fetch the cull camera's `GpuGrassHiZ` (shared). If `config.EnableOcclusionCulling && hiZ.IsReady` → set the Hi-Z binds + `occlusionEnabled=1`; else `occlusionEnabled=0`.
   - Snapshot `config.EnableOcclusionCulling` in `Build` like other config fields; respect the existing `#if UNITY_EDITOR` re-push pattern.
   - No new dispatch — occlusion folds into the existing ChunkCull.

6. **EditMode tests `HiZMathTests`.**
   - `TryProjectAabbToScreenRect` — known AABB + VP → expected screen rect + near depth.
   - `SelectMip` — rect size → expected coarsest covering mip; clamps to `[0, mipCount-1]`.
   - Off-screen / behind-camera AABB → returns false (caller keeps the chunk = fail-open).

## Verification

- **EditMode:** `run_tests` → `HiZMathTests` + all prior green.
- **Play-mode smoke (live editor, GLES3 tier if possible):** build a hilly multi-terrain scene (P2 window), enter Play:
  - With occlusion ON: orbit camera so a hill hides a far grass field → confirm the hidden field's instance/draw load drops (profiler / `rendering_stats`), and crucially **no visible grass pops in/out** at frustum-visible chunks (Risk-20 check).
  - Toggle occlusion OFF (Optimize section) → rendering identical to pre-Phase-3 (frustum+distance only).
- **MCP discipline:** `set_active_instance("GrassInteract@…")` first; compute/shader edits → `read_console`; `refresh_unity` may no-op on shader-only edits (touch a `.cs` or re-import the `.compute`); MCP timeout = busy compiling, never kill the editor.

## Definition of done

- Hi-Z occlusion working behind `enableOcclusionCulling`; conservative (no false occlusion of visible grass); fail-open when depth unavailable; shared per camera across fields; Hi-Z math EditMode-green; Play-mode smoke confirms load drop on hidden fields with no popping; flag-off path byte-identical to Phase-2 behavior.

## Post-completion

- Update `Assets/GPUGrass/README.md` with the Hi-Z occlusion pattern + the "add the renderer feature to your URP asset" step (library-feature-discovery skill-update mandate).
- Update the `gpugrass-module` memory: Pass 3 occlusion done + window entry point.
