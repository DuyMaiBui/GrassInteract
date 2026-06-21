# WorldPainter Custom CDLOD vs Unity Built-in Terrain — Mobile Decision Report

**Date:** 2026-06-20
**Type:** Research / decision report (no code changes)
**Decision framing:** Genuinely open — keep maintaining the custom WorldPainter terrain engine vs. migrate to Unity built-in Terrain.
**Target profile (user-confirmed):**
- World scale: **large, streamed, km-scale open world**
- Devices: **mid-to-low-end mobile, minimum spec INCLUDES OpenGL ES 3.0 / non-compute Android**
- Concerns in scope: **render cost, memory & streaming, physics/colliders, authoring & tooling** (all four)

---

## TL;DR — Verdict

**Keep the custom WorldPainter CDLOD engine. It is the correct architecture for this target. Built-in Terrain cannot meet a km-scale streamed mid/low-end mobile budget as a runtime renderer.** The evidence is one-directional.

BUT, because the confirmed device floor **includes GLES3.0 / non-compute Android**, there is one **ship-blocking gap in the current code**:

> 🔴 **`GpuTerrainEngine` calls `Graphics.RenderMeshIndirect` unconditionally with no non-compute fallback.** On GLES3.0 devices (and devices that fall back from a broken Vulkan driver to GLES) indirect draw + compute are unsupported → **the terrain does not render at all**. The grass system already tiers down for this case (`GrassTierProbe` → `GrassCpuEngine`); the terrain system does not. This must be fixed before shipping to the stated device floor.

Net recommendation: **Custom engine + two guardrails** (mandatory non-compute terrain tier, author-by-baking-from-Terrain).

---

## Why built-in Terrain loses at this target

Built-in `Terrain` + `TerrainData` is a strong **authoring/editor** tool and a poor **runtime** fit for km-scale streamed mobile. The two structural walls:

1. **Splat shader is fill-rate murder on mobile.** The terrain blend shader does 4 layers per pass; **every additional group of 4 layers is another full-screen render pass**. Measured ≈10 fps lost per blended layer on Mali; mobile TBDR GPUs (Mali/Adreno/Apple/PowerVR) are bandwidth-bound, so multi-pass terrain shading dominates the frame.
2. **No runtime streaming exists.** You must hand-roll GameObject activation / additive async scenes / Addressables. `TerrainData` height upload spikes (~60 ms; worse on mobile), per-tile collider bake hitches, and 10–40 MB resident `TerrainData` per tile give you no hard VRAM ceiling.

**Shipped precedent is unanimous:** large-scale mobile open worlds that start on built-in Terrain end up using **Terrain for authoring only**, then **baking each tile to a low-poly LOD mesh + atlased single-material splat** for runtime (Game Developer "Open World on Mobile with Unity"; MicroSplat Mesh Terrains; FastTerrainToMesh). That baked-mesh + GPU-instanced architecture **is what WorldPainter already is.**

---

## Dimension-by-dimension comparison

| Concern | Unity Built-in Terrain (runtime) | Custom WorldPainter CDLOD | Winner @ target |
|---|---|---|---|
| **Render cost / GPU** | No cross-tile batching — draws scale linearly with active tiles. Multi-pass splat shader (~10 fps/layer on Mali). `drawInstanced` cuts CPU only, not draw count or fill rate. | One/few `RenderMeshIndirect` for whole terrain; per-patch LOD in ComputeBuffer; CPU ~constant in tile count. CDLOD vertex-shader morph (cheap ALU) + skirts for seams. | **Custom** (decisive) — *conditional on compute support; see risk* |
| **Memory & streaming** | No built-in streaming. `TerrainData.SetHeightmap` ≈60 ms spikes. 10–40 MB resident per tile. No memory ceiling. | Residency ring + async R16 tiles → **hard, predictable VRAM ceiling** (`TerrainResidencyRing`, `TerrainTileLoader`, `TerrainStreamingManager`). The AAA streamed-terrain model. | **Custom** (decisive, structural win) |
| **Physics / colliders** | `TerrainCollider` is heightmap-native (cheap) but doesn't stream itself; per-tile bake adds load hitches. | Already streams colliders around the player (`TerrainColliderStreamer` + `TerrainColliderRing`); ground-snap via heightmap sample/raycast. | **Custom** (already built) |
| **Authoring & tooling** | **Built-in's one real win:** mature sculpt/paint/splat/tree/detail toolset + undo + brushes, free. | Must own sculpt/paint/splat/scatter + **bake pipeline** + multi-GPU shader-variant maintenance. Render code is the easy 20%; tooling is the 80% that sinks schedules. | **Built-in** — mitigated by authoring-via-bake (see guardrail #2) |

---

## The two guardrails that decide whether custom *succeeds*

### 🔴 Guardrail #1 (MANDATORY for this device floor) — non-compute terrain fallback

`RenderMeshIndirect` / `DrawProceduralIndirect` / compute shaders are **unsupported on OpenGL ES** (all versions, per Unity's own GPU Resident Drawer constraint — "requires compute shaders, except OpenGL ES"). Because the confirmed minimum spec **includes GLES3.0 / non-compute Android**, the current `GpuTerrainEngine` path renders **nothing** on those devices.

**Code state today:**
- ✅ Grass: `GrassTierProbe` detects `SystemInfo.supportsComputeShaders == false` → routes to `GrassCpuEngine` (CPU `Graphics.RenderMeshInstanced`, 1023-instance slabs). Pattern already proven in-repo.
- ❌ Terrain: `GpuTerrainEngine` (Runtime/Terrain/GpuTerrainEngine.cs:504, :534) calls `Graphics.RenderMeshIndirect` with no guard and no CPU sibling. **No `CpuTerrainEngine` exists.**

**Required fix (one of):**
- **(a) Build a non-compute terrain tier** gated on `SystemInfo.supportsComputeShaders` (and a graphics-API check), mirroring `GrassTierProbe`/`GrassCpuEngine`: CPU-built per-patch `DrawMeshInstanced` (1023 batches) **or** a static pre-baked chunked-mesh terrain with discrete LOD for low-end. The CDLOD quadtree + R16 tiles can feed either path; only the *submission* changes.
- **(b) Raise the minimum spec to Vulkan/Metal-only** and formally drop GLES3.0 devices. *(User confirmed this is NOT acceptable — the floor must include GLES3.0 — so (a) is required.)*

Gate the runtime path: `bool indirect = SystemInfo.supportsComputeShaders && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3 …` Test on **real low-end Adreno/Mali hardware**, not emulators — broken-Vulkan→GLES fallback only shows on-device.

### 🟡 Guardrail #2 (strongly recommended) — author by baking from Terrain, not bespoke authoring

Keep custom code to the **runtime renderer + baker**. Don't rebuild sculpt/paint/splat from scratch — that's the maintenance cost that quietly sinks the schedule. The pragmatic hybrid: author heightmaps in Unity Terrain (or offline World Machine/Gaea), then **bake to WorldPainter's R16 tile + splat/normal format**. This inherits the entire mature authoring toolchain and limits owned code to the runtime path. *(Note: WorldPainter already has substantial in-editor brush/paint tooling — evaluate whether that's cheaper to maintain than a Terrain-bake importer; the principle is "don't grow bespoke authoring further than necessary.")*

---

## Per-option optimization playbook

### If staying custom (recommended) — optimize the WorldPainter engine

- **Non-compute fallback tier** (guardrail #1) — the headline item.
- **R16 heightmap tiles, kept small & locally coherent** — half the VTF bandwidth of float; vertex texture fetch cache misses are far costlier on mobile. Per-tile height scale/offset preserves precision. Never lossy-compress heights (terracing).
- **Lean on CDLOD morph + aggressive LOD distances** — keep patch vertex counts modest; on TBDR the binning/geometry phase pays for heavy VS displacement.
- **Shader-variant discipline** — a custom URP terrain shader can explode into hundreds of variants and compile-on-first-use → in-play hitches on low-end. Strip via `IPreprocessShaders`/URP feature toggles, minimize `multi_compile`, and **pre-warm a ShaderVariantCollection** at load.
- **Colliders:** keep the residency-ring `TerrainCollider` small; ground-snap cheap agents by direct heightmap sampling, not per-instance physics raycasts; reserve real colliders for player + nearby dynamics.
- **Fixed VRAM ceiling** via the residency ring — already in place; keep it tuned per device tier.
- **Shadows:** terrain shadow casting off where the art allows — large mobile saving.

### If migrating to built-in Terrain (NOT recommended at this target) — clamp hard

- ≤4 splat layers per tile (one pass) — never exceed on low-end.
- Very low `basemapDistance` (most of frame uses cheap composited basemap) — biggest fill-rate win.
- Raise `heightmapPixelError` aggressively; enable `drawInstanced`.
- Custom mobile terrain shader: no per-pixel normals, no specular, single dir light, `reflectionProbeUsage = Off`.
- **Bake terrain → static low-poly mesh + atlased splat with custom LOD** (the shipped-game pattern) — at which point you've rebuilt a worse version of WorldPainter.
- Mali: use only odd splatmap indices (documented "chessboard" driver bug workaround).
- Streaming: async additive scene loads, pre-warm `TerrainData`/collider bake off the hot frame, 1024 m tiles, ≤1–2 splat atlases resident.

---

## Risks & limitations of this analysis

- **Device-specific fps numbers must be measured on YOUR target hardware** (real low-end Adreno/Mali + a mid iPhone). The ≈10 fps/layer splat figure is Mali-tablet-derived; the *shape* holds, absolute numbers vary.
- **Guardrail #1 fallback is non-trivial** and must be validated on physical low-end devices — the broken-Vulkan→GLES case does not appear on emulators or high-end phones.
- A hand-rolled GLES3.1 indirect path *might* work where Unity's GPU Resident Drawer refuses, but treat it as unproven until on-device verified — and it does nothing for true GLES3.0 devices, which still need the CPU/static-mesh tier.
- This is generic + Unity-docs research synthesized against the WorldPainter codebase; no on-device profiling pass was run.

---

## Recommended next steps

1. **Decide guardrail #1 implementation** — CPU `DrawMeshInstanced` per-patch tier vs. static pre-baked chunked-mesh LOD terrain for non-compute devices. (Candidate for `/t1k:plan`.)
2. **Add the `supportsComputeShaders` + graphics-API gate** to `GpuTerrainEngine`/`TerrainStreamingManager`, mirroring `GrassTierProbe`.
3. **Profile both tiers on real low-end Adreno/Mali hardware** to set per-tier residency-ring + LOD-distance budgets.
4. **Decide authoring strategy** (guardrail #2) — continue WorldPainter in-editor tooling vs. add a Unity-Terrain-bake importer.
5. (Optional) Pre-warm `ShaderVariantCollection` for the terrain + grass + prop shaders to kill first-use hitches.

---

## Key sources

**Built-in Terrain (mobile):**
- Unity Manual — [Terrain Settings reference](https://docs.unity3d.com/Manual/terrain-OtherSettings.html) · [Heightmaps](https://docs.unity3d.com/Manual/terrain-Heightmaps.html) · [Grass & details](https://docs.unity3d.com/2022.1/Documentation/Manual/terrain-Grass.html) · [GPU instancing constraints](https://docs.unity3d.com/Manual/GPUInstancing.html)
- Unity Discussions — [Terrain shader perf on mobile (≈10 fps/layer)](https://discussions.unity.com/t/terrain-shader-performance-on-mobile/548853) · [Large open-world streaming (no built-in streaming)](https://discussions.unity.com/t/best-approach-for-handling-large-open-world-terrain-streaming/1710438) · [Terrain still poorly optimized 2023](https://discussions.unity.com/t/unity-terrain-is-still-poorly-optimized-in-2023-922092/922092)
- Game Developer — [Open World on Mobile with Unity (bake-to-mesh, 1024m tiles, >3 texture limit, Mali chessboard/odd-index)](https://www.gamedeveloper.com/programming/open-world-on-mobile-with-unity)

**Custom GPU / CDLOD / indirect:**
- Strugar — [CDLOD paper](https://aggrobird.com/files/cdlod_latest.pdf) · [vterrain LOD survey](http://vterrain.org/LOD/Papers/)
- Unity Manual — [GPU Resident Drawer ("requires compute, except OpenGL ES")](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/gpu-resident-drawer.html) · [DrawProceduralIndirect](https://docs.unity3d.com/ScriptReference/Graphics.DrawProceduralIndirect.html) · [GLES3.1 feature details](https://docs.unity3d.com/2021.3/Documentation/Manual/OpenGLCoreDetails.html)
- GDC — [Zen of Streaming: Ghost of Tsushima](https://www.gdcvault.com/play/1027205/Zen-of-Streaming-Building-and) · [Samurai Landscapes (terrain rendering)](https://gdcvault.com/play/1027352/Samurai-Landscapes-Building-and-Rendering)
- [MicroSplat Mesh Terrains](https://assetstore.unity.com/packages/tools/terrain/microsplat-mesh-terrains-157356) · [FastTerrainToMesh](https://github.com/unitycoder/FastTerrainToMesh) · [Mountains Beyond Mountains (virtual streamed terrain)](https://github.com/xshazwar/mountains-beyond-mountains)
- Mobile GPU — [Adreno best practices (bandwidth budgets)](https://docs.qualcomm.com/bundle/publicresource/topics/80-78185-2/mobile_best_practices.html) · [Android texture bandwidth](https://developer.android.com/agi/sys-trace/texture-memory-bw)
- Shader variants — [Reduce URP shader variants](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/shader-stripping.html)

**In-repo evidence:**
- `Runtime/Terrain/GpuTerrainEngine.cs` (RenderMeshIndirect, no fallback) · `Runtime/Scatter/GrassTierProbe.cs` (the compute-support tier pattern to mirror) · `Runtime/Scatter/GrassCpuEngine.cs` · `Runtime/Terrain/TerrainColliderStreamer.cs` / `TerrainResidencyRing.cs` / `TerrainStreamingManager.cs` (streaming already built)
