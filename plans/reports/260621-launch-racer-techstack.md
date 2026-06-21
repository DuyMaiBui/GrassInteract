# Tech-Stack Decision Report — 3D Casual Launch-Upgrade Distance Racer (Broad Mobile)

**Date:** 2026-06-21
**Type:** Research / decision report (no code changes)
**Method:** 17-agent multi-perspective workflow — 6 web-research + 4 codebase-fit (Explore) + 6 adversarial verifications + synthesis. All load-bearing facts re-verified against the repo.

## Game definition (user-confirmed this session)
- **Genre:** 3D casual "launch-and-upgrade distance racer" (Hill Climb Racing / Earn to Die / Learn to Fly / Burrito Bison lineage, but 3D).
- **Core loop:** push once to launch → steer the kart behind-view down a long linear track → momentum runs out / crash → reward → upgrade kart stats → relaunch from start → **reach end of map = win**. Failed run resets to start.
- **Camera/control:** 3D, camera behind kart, steer left/right during the run.
- **Map:** large discrete tracks (one long linear **corridor** loaded at a time) — NOT free-roam open world.
- **Agents:** **1** (player kart). No AI, no traffic.
- **Multiplayer:** none. Single-player. No netcode.
- **Target:** casual, **broad multi-device incl. low-end / OpenGL ES 3.0 (no-compute) Android**. Unity 6 / URP.
- **Existing:** WorldPainter custom CDLOD terrain + interactive grass + car-as-grass-interactor.

---

## 1. Executive verdict

Build gameplay on the studio's existing lightweight **Mono stack**, model the kart as a **single rolling-sphere Rigidbody**, render the track as **1-D streamed baked segments**, and **keep the interactive grass** as the signature feature. **Do not adopt DOTS/ECS. Do not ship the CDLOD terrain runtime.**

Every adversarial verdict came back `confirmed` (grass was `nuanced` — the nuance is a shader-target gap, not an architecture flaw). The three facts that anchor everything, re-verified in-repo:

- `com.unity.entities` in `packages-lock.json`: **0** (Entities not installed).
- `Graphics.RenderMeshIndirect` called **unconditionally** in `GpuTerrainEngine.cs` (504/534) **and** `InstancedPropEngine.cs` (508/510/512); **gated** only in `GrassGpuEngine.cs`.
- `ProjectSettings.asset`: `openGLRequireES31: 0` → **GLES3.0 (no-compute) is a real shipping target.**

Headline consequence: the open-world terrain engine is both **wrong-shaped** for a 1-D corridor AND a **silent ship-blocker** on the stated low-end floor. Repurpose WorldPainter's *editor tooling* as a bake pipeline; ship **standard URP meshes**.

---

## 2. Recommended stack (layer by layer)

| Layer | Choice | Why |
|---|---|---|
| **Engine / RP** | Unity 6 + URP 17.3 (unchanged) | Already in project; correct mobile RP. |
| **Gameplay arch** | MonoBehaviour + VContainer (DI) + SignalBus (events) + UniTask (async) + R3 (reactive) | House standard, already wired. 1 agent = zero ECS payoff. |
| **DOTS/ECS** | **Do NOT adopt.** Keep Burst+Collections only where grass/prop jobs already use them. | Installing Entities for one kart = baking/SubScene/ECS-physics cost for no throughput gain; wrecks iteration. Installed Burst/Collections are pure non-ECS `IJob`/`NativeArray`. |
| **Kart physics** | **Rolling-sphere Rigidbody** + visual kart aligned to raycast `hit.normal` | Lowest, most deterministic FixedUpdate cost; one-shot `AddForce` launch, velocity/torque steer (works airborne+grounded), damping = momentum decay, `OnCollisionEnter` = crash. ~3 knobs. |
| **Track repr** | **1-D baked-SEGMENT streaming** — spline centerline (`com.unity.splines`), baked into N fixed-length Addressables segments (ribbon mesh + collider + props/grass-density + baked GI). Sliding window ~3-5 segments keyed off distance. | O(1) residency vs CDLOD ring's O(radius²). Standard meshes render on GLES3.0 (no compute). Subway-Surfers/Temple-Run precedent. |
| **Track renderer** | **Standard SRP-batched URP MeshRenderers.** CUT the CDLOD render path from the shipping build. | `GpuTerrainEngine` RenderMeshIndirect is unconditional + silent-fail on GLES3.0. Standard meshes sidestep it; get shadows/LOD/baked-GI free. |
| **Signature grass** | **KEEP GrassInteract as-is**, routed through `GrassTierProbe` → GPU/CPU engine. One `GrassInteractor` on the kart; scope blades to active window. | Single kart = cheapest interactor case; CPU tier is compute-free `RenderMeshInstanced`. **Caveat:** shaders are `#pragma target 4.5`/`Fallback Off` — validate/lower target on a real ES3.0 device. |
| **WorldPainter tooling** | **REPURPOSE editor-only** as the bake pipeline that emits per-segment baked assets. | Tooling is reusable; only the *runtime* 2D streaming/render path is wrong. Keep editor assemblies out of the runtime build. |
| **Save / economy** | Versioned JSON to `persistentDataPath` (atomic) + ScriptableObject upgrade defs; one soft currency, geometric cost curve; rewarded ads behind an interface. | Casual-mobile standard; simple, debuggable. VContainer-injected services. |
| **Core loop** | Explicit FSM (`AimLaunch→Launching→Running→Settling→Reward→Upgrade→Win`) as a VContainer-resolved `RunStateMachine`; R3 `ReactiveProperty` for HUD; SignalBus for `RunEnded`/`RewardGranted`/`UpgradePurchased`/`MapCompleted`. | Centralizes transitions, decouples UI. Named-method subscribe + dispose in `OnDestroy` or relaunch leaks. |
| **Device scaling** | 3 SystemInfo tiers (High: compute/Vulkan-Metal · Mid: GLES3.1 · Low: GLES3.0 no-compute) over URP assets + grass density + draw distance. | GLES3.0 is the real floor. Replicate `GrassTierProbe` for any retained GPU path. |

**Kart physics — close alternative & rejects:** raycast-suspension Rigidbody (4 corner raycasts + spring) is the canonical "kart-feel-with-body-lean" option — pick it *only if body pitch/lean over jumps is a declared feel pillar* (more FixedUpdate cost, fiddlier tuning; decide up front, sphere→suspension migration is non-trivial). **WheelCollider rejected** — per-wheel solve too heavy for mobile, no airborne contact (so air-steer is bolted on anyway), confirmed Unity 6.x bugs; vendor EVP/Edy is removing the WheelCollider dependency in 2026.

---

## 3. WorldPainter keep / trim / cut / repurpose

| Component | Verdict | Note |
|---|---|---|
| Interactive grass (GrassBendSimulator, GrassRenderer, GrassTierProbe, Gpu/CpuEngine, GrassInteractor, GrassScatter) | **KEEP (signature)** — low | The differentiator. Caveats: (1) shader `target 4.5`/`Fallback Off` → verify on ES3.0 or grass goes pink; (2) **re-bind GrassInteractor every relaunch** (self-registers `OnEnable` into static `Active`) or bend dies after first reset; (3) `GrassCpuEngine` ignores `SetScaleFactor`/`SetDensity` → add per-tier blade-count presets; (4) scope from single-field to active track window. |
| `GpuTerrainEngine` (627 lines) runtime CDLOD render | **CUT from shipping build** | RenderMeshIndirect unconditional (504/534), no fallback → **blank terrain on GLES3.0, fails SILENTLY** (SelfTest only catches throws; RenderMeshIndirect no-ops). |
| `TerrainResidencyRing` / `TerrainStreamingManager` (2D 5×5 ring) | **CUT → 1-D window** | 2D O(radius²) wastes ~80% on cross-track tiles a 1-D kart never reaches. Repurpose the *concept* (hysteresis, per-frame upload budget), not the 2D code. |
| `InstancedPropEngine` (props) | **CUT or RE-TIER** | **Also** RenderMeshIndirect unconditional (508/510/512) → props blank on ES3.0 too. Cutting terrain alone does NOT clear the ES3.0 build. |
| `CdlodQuadtree` / `TerrainTileLoader` / `TerrainPatchMesh` (skirts) | **CUT (runtime)** | Open-world LOD machinery, no payoff for a bounded corridor at one trailing camera. Solve segment seams by snapping/overlapping shared edge verts at BAKE time. |
| Collider streaming (`TerrainColliderStreamer/Ring/Provider`) + `ISurfaceSampler` family | **KEEP-TRIM** — medium | Ring overkill, but cook-amortization (FIFO, `MAX_COOKS_PER_FRAME=1`) + metric-distance logic reusable → narrow to forward-only lookahead. **KEEP** `ISurfaceSampler`/`RaycastSurfaceSampler`/`TerrainSurfaceSampler` for kart ground-snap (avoid streaming-hitch fall-through) + grass placement. |
| WorldPainter editor pipeline (sculpt/paint/scatter/bake, `WorldMapAsset`, `ISurfaceSampler`) | **REPURPOSE (editor-only)** | Becomes the tool that emits per-segment baked assets (mesh + collider + props + grass-density + GI). Keep editor assemblies out of the runtime build. |
| Burst 1.8.x + Collections 2.4.3 | **KEEP (scoped)** | Pure non-ECS `IJob`/`NativeArray` grass/prop math. Don't expand into a gameplay ECS. |
| DOTS/Entities for gameplay | **DO NOT ADOPT** | Not installed. One kart = no batch workload. Re-evaluate only if entities later grow by orders of magnitude — and even then Jobs+Burst is the lighter first step. |
| Kart / vehicle / RunState gameplay | **BUILD NEW (greenfield)** | No existing kart/Rigidbody/loop code → clean Mono+VContainer build, zero migration debt. |

**The only WorldPainter pieces that survive into the shipped runtime: the interactive grass (tier-gated) + the 1-D-reduced streaming pattern.** Everything else becomes editor-only bake tooling or is replaced by standard URP meshes.

---

## 4. Reconciliation with the prior terrain report

The prior report (`260620-worldpainter-custom-vs-builtin-terrain.md`) concluded **"keep the custom CDLOD engine"** — correct for *its* scope (header: "large, streamed, **km-scale open world**"). That conclusion does **not** carry to this game, and it's a scope guard firing, not a contradiction:

- The prior report's decisive custom-engine wins — *cross-tile batching at km scale* and *a hard VRAM ceiling via the 2D residency ring* — are **open-world-specific**. A 1-D corridor with one trailing camera has a handful of segments (standard SRP batching covers it) and travels along one axis (a 1-D window gives a tighter, simpler memory bound than the O(radius²) ring).
- **Remove the scale and the second dimension and those wins evaporate, while the engine's costs (compute dependency, ring overhead, two-renderer maintenance) remain.** Same evidence, different problem shape, opposite conclusion.
- The prior report *already* flagged the RenderMeshIndirect/GLES3.0 blocker as ship-critical and noted the shipped-precedent endgame is "Terrain for authoring only, bake to mesh for runtime" — which is exactly the **repurpose-to-bake-pipeline** recommendation here, applied to a corridor instead of a world.

Carries forward unchanged: (a) the `GrassTierProbe`/`GrassCpuEngine` tier pattern is the model every retained GPU path must copy; (b) built-in Terrain's runtime weaknesses (multi-pass splat fill-rate, no streaming, ~60ms heightmap-upload spikes) are real — which is *also* why a baked **ribbon mesh** beats built-in Terrain even for a single bounded segment when the corridor is wide and steerable.

**This report's own scope guard:** the verdict assumes "one long linear track loaded one-at-a-time." If a single track grows large enough to need mid-track multi-terrain streaming, the open-world failure mode partially returns — re-run the reconciliation then.

---

## 5. Device-scaling — 3-tier plan

GLES3.0 is the verified floor → a probe-driven tier system is mandatory, modeled on `GrassTierProbe` (`supportsComputeShaders` / `supportsIndirectArgumentsBuffer` / `graphicsDeviceType`).

| Tier | Devices | Track render | Grass | URP / upscale |
|---|---|---|---|---|
| **High** | Vulkan/Metal, compute | Standard meshes (GPU-instanced behind probe if ever wanted) | GPU tier, full blades, shader wind | STP (high only), full post |
| **Mid** | GLES3.1 | Standard meshes | GPU tier, reduced density | FSR/bilinear, no STP |
| **Low** | **GLES3.0 (no compute)** | Standard URP meshes only | **CPU tier (RenderMeshInstanced), low blade count, or grass off** | Bilinear, **no HDR** (fix: low URP asset currently has HDR on), minimal post |

Non-optional because: (1) terrain AND props currently render nothing on Low; (2) the grass CPU tier degrades correctly in C# but its shaders need SM4.5 → lower target (e.g. 3.5) and **validate on a physical ES3.0 device, not an emulator**. `GrassCpuEngine` also ignores density setters, so per-tier blade-count presets must be authored (10k–50k is a desktop number).

---

## 6. Gap analysis — what to build (greenfield)

Grass + (repurposed) bake tooling is the foundation; the **entire game layer is absent**:
- **Kart:** rolling-sphere Rigidbody, one-shot launch impulse, velocity/torque steer, momentum decay, crash + low-speed-stop detection, ground-snap via raycast/heightmap sampler.
- **Loop FSM:** transitions, launch input (tap + charge meter), win trigger, reset-to-start.
- **Camera:** behind-kart smooth damping, look-ahead, collision avoidance, optional speed-FOV.
- **Economy + upgrades:** currency, stat schema, exponential pricing, purchase feedback.
- **Persistence:** versioned JSON save on reward/pause.
- **UI:** HUD (distance/speed/track-%/launch meter, R3-bound), upgrade shop, reward screen.
- **Track authoring:** spline-centerline tool + segment baker (built on repurposed WorldPainter pipeline) + start/end markers + physics layers.
- **Audio:** launch/loop/crash/coin/upgrade/UI SFX, 3D engine audio, music.
- **Device tiers:** startup probe + per-tier presets.
- **ES3.0 fix:** non-compute render path for track + props; grass shader target lowered/validated.

---

## 7. Phased build order

1. **Unblock ES3.0 + prove the render floor.** Replace track render with standard URP meshes (or probe-gate any GPU path); re-tier/cut `InstancedPropEngine`; lower/validate grass shader target on a real ES3.0 device. *Highest priority — fails silently otherwise.*
2. **Kart vertical slice.** Rolling-sphere Rigidbody + launch + steer + ground-snap on one hand-built segment; attach + re-bind GrassInteractor; tune to forgiving casual feel.
3. **Loop + economy.** RunStateMachine FSM, R3 HUD, reward→upgrade→relaunch→reset, JSON save, ScriptableObject upgrades.
4. **1-D segment streaming.** Spline authoring, segment baker, sliding-window loader (hysteresis + per-frame instantiate budget); seam-snap at bake.
5. **Device tiers + polish.** 3-tier presets, grass density per tier, camera/audio/feel juice, win/end-of-track, optional leaderboards.

---

## 8. Top risks

1. **GLES3.0 SILENT blank-render is broader than terrain** — `GpuTerrainEngine` (504/534) AND `InstancedPropEngine` (508/510/512) both call RenderMeshIndirect unconditionally. Cutting terrain alone leaves props invisible. Cannot be caught by SelfTest (RenderMeshIndirect no-ops, never throws) — ships green from a compute-capable dev device, blank to ES3.0 users. **Highest priority.**
2. **Grass "GLES3.0 tier" unproven at the shader level** — `target 4.5` / `Fallback Off`. C# CPU path is compute-free, but SM4.5 may not compile on a true ES3.0 GPU → missing/pink grass. Validate on real hardware.
3. **Relaunch lifecycle** — R3/UniTask subscriptions need named methods + `OnDestroy` dispose or every relaunch leaks; GrassInteractor must be re-bound each reset or the bend dies after run 1.
4. **Real engineering cost** — cutting the CDLOD runtime + building 1-D segment streaming is work, not free. Budget it. The two-renderer alternative (CDLOD + non-compute fallback) is strictly more ongoing complexity for a corridor that never uses 2D streaming.
5. **Sphere-driver instability + tunneling** — steer via velocity (not raw torque), clamp/freeze angular velocity, decouple visual rotation from physics spin, `collisionDetectionMode = Continuous`, sample heightmap rather than relying solely on streamed colliders.
6. **Segment-boundary seams** — baked ribbon must snap/overlap shared edge verts (normals+UVs) at bake (CDLOD skirt tool is gone). Keep 1–2 segments behind (rear-view camera + backward-rolling kart); gate eviction on visibility + hysteresis.
7. **Grass budget** — 10k–50k blades is a desktop number; add tier presets, validate active-window count on ES3.0. Economy curve can drift into a grind — keep tiers small, tune the geometric slope.
8. **Genre precedent is inferred** — HCR2/Earn-to-Die internals aren't vendor-confirmed; the single-body + 1-D-corridor pattern is strong-but-inferred. The Mono-over-DOTS and segment-over-CDLOD verdicts rest on the **verified codebase reality**, not the genre inference.

---

## 9. Sources

**In-repo (verified):** `Runtime/Terrain/GpuTerrainEngine.cs` (RenderMeshIndirect 504/534, no fallback) · `Runtime/Scatter/InstancedPropEngine.cs` (508/510/512, no fallback) · `Runtime/Scatter/GrassGpuEngine.cs` (gated) · `GrassTierProbe.cs`/`GrassCpuEngine.cs`/`GrassRenderer.cs`/`GrassInteractor.cs` · `Runtime/Terrain/TerrainResidencyRing.cs` (5×5, RING_RADIUS=2) / `TerrainStreamingManager.cs` / `TerrainColliderStreamer.cs` / `TerrainColliderRing.cs` / `TerrainColliderProvider.cs` · `ISurfaceSampler.cs`/`RaycastSurfaceSampler.cs`/`TerrainSurfaceSampler.cs` · `Packages/packages-lock.json` (entities=0; burst/collections present) · `ProjectSettings.asset` (openGLRequireES31: 0, AndroidMinSdk 25) · manifest (vcontainer, r3, UniTask, Input System 1.19, URP 17.3).

**Prior report:** `plans/reports/260620-worldpainter-custom-vs-builtin-terrain.md` (km-scale open-world scope; reconciled §4).

**External:** Hill Climb Racing 1/2, Earn to Die, Learn to Fly, Burrito Bison (single launched Rigidbody + 1-D corridor — strong-but-inferred). Subway Surfers / Temple Run / Crash: On the Run / Lara Croft: Relic Run (pooled chunk/segment streaming). Unity 6000 docs: `Graphics.RenderMeshIndirect` requires compute; OpenGL ES 3.0 has no compute (3.1+ required); GPU Resident Drawer "requires compute, except OpenGL ES"; Terrain GPU instancing optional. Unity Discussions 577142 + Unity 6.x WheelCollider bugs; EVP/Edy removing WheelCollider dependency (2026). `com.unity.splines` 2.8.x runtime extrusion.
