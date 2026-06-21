# Phase 1 — Shared-config multi-terrain setup core (no UI)

Goal: make GPUGrass set up **all** terrains in a scene against **one shared** `GpuGrassConfig`, each
terrain keeping its own bake. Pure editor logic, EditMode-tested. Strip the old menu item. Land the
`enableOcclusionCulling` config field so P2 and P3 both compile against it.

## File ownership

- `Assets/GPUGrass/Editor/GpuGrassAutoSetup.cs` — modify
- `Assets/GPUGrass/Editor/GpuGrassSceneSetup.cs` — new
- `Assets/GPUGrass/Runtime/GpuGrassConfig.cs` — modify (one field)
- `Assets/GPUGrass/Tests/SceneSetupTests.cs` — new

## Tasks

1. **Refactor `SetupOnTerrain` to inject the config.**
   - Change signature to `public static int SetupOnTerrain(Terrain terrain, GpuGrassConfig sharedConfig)`.
   - Remove the internal per-terrain config creation block (the `if (controller.Config == null) { CreateInstance<GpuGrassConfig> … }`). Assign `controller.Config = sharedConfig` instead.
   - Keep: per-terrain bake-asset creation (rename to make terrain-keyed path explicit), render-asset wiring (`WireRenderAssets` — operates on the shared config, idempotent), blade-mesh ensure, detail-distance disable, `GpuGrassBaker.Bake`, dirty/save, `controller.Rebuild()`.
   - **Guard 1/2:** config is the SAME instance for all terrains; bake is per-terrain.

2. **Strip the old `[MenuItem]`.**
   - Remove `[MenuItem("Tools/GPUGrass/Auto-Setup Grass On Terrain", false, 0)]` and its `AutoSetup()` wrapper.
   - Keep `ResolveTerrain`, `EnsureGeneratedFolder`, `WireRenderAssets`, `EnsureBladeMesh` as helpers (used by `SetupOnTerrain`).
   - **Pre-strip grep:** `grep -rn "Auto-Setup Grass On Terrain\|GpuGrassAutoSetup" Assets/GPUGrass` — confirm no other caller (esp. `Samples~/…/GpuGrassDemoBuilder.cs`). If the demo builder calls the old `SetupOnTerrain(terrain)`, update it to create a config then pass it.

3. **New `GpuGrassSceneSetup` static class.**
   - `public static SceneSetupResult SetupScene(GpuGrassConfig sharedConfig)`:
     - Resolve all terrains: `Terrain.activeTerrains` (fallback `Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)`), skip null `terrainData`.
     - For each terrain → call `GpuGrassAutoSetup.SetupOnTerrain(terrain, sharedConfig)`, collect `(terrainName, bladeCount, resolvedTier)`.
     - `AssetDatabase.SaveAssets()` once at the end (not per terrain — batch).
     - Return a result struct/list the window renders as status rows.
   - `public static GpuGrassConfig EnsureSharedConfig()` — create-if-missing helper: load/create `Assets/GPUGrass/Generated/SceneGrassConfig.asset` (or return existing). Window may override the path via its picker.
   - **Guard 5:** does not touch existing per-terrain configs.

4. **Add `enableOcclusionCulling` to `GpuGrassConfig`.**
   - `[Header("Occlusion (Hi-Z)")] [Tooltip("GPU Hi-Z occlusion cull: skips grass chunks hidden behind terrain/geometry. Auto-disabled when no camera depth texture is available.")] [SerializeField] private bool enableOcclusionCulling = true;`
   - `public bool EnableOcclusionCulling => this.enableOcclusionCulling;`
   - This is the only Phase-1 runtime change; it lets P2's Optimize toggle + P3's renderer both compile.

5. **EditMode tests `SceneSetupTests` (synthetic terrains, mirror `BakerTests`).**
   - `SetupScene_AssignsSameConfigInstanceToEveryController` — N synthetic terrains → all controllers `.Config` reference-equal to the passed shared config.
   - `SetupScene_GivesEachTerrainDistinctBake` — N terrains → N distinct `GpuGrassBakeData` assets (Guard 2); positions disjoint per terrain bounds.
   - `SetupScene_SkipsTerrainsWithoutTerrainData` — null `terrainData` terrain is skipped, no exception.
   - `EnsureSharedConfig_IsIdempotent` — second call returns the same asset, no duplicate.
   - Clean up created assets/gameobjects in `[TearDown]`.

## Verification

- `run_tests` (EditMode) → all `SceneSetupTests` + existing 23 green.
- Confirm `Tools ▸ GPUGrass` no longer shows "Auto-Setup Grass On Terrain" (only the P2 window will appear there).
- `read_console` clean after recompile.

## Definition of done

- One shared config assigned to every terrain's controller; each terrain has its own bake; old menu item gone; `enableOcclusionCulling` field present; all EditMode tests green.
