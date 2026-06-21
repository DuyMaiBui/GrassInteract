# Phase 2 — Scene Setup Window (UI)

Goal: one Editor window that is the sole GPUGrass entry point — author the shared grass properties
once, see every terrain's status (baked data hidden), bake all terrains in a click, and tune perf in
an Optimize section. Blocked by Phase 1 (`GpuGrassSceneSetup`, injected config, `enableOcclusionCulling`).

## File ownership

- `Assets/GPUGrass/Editor/GpuGrassSceneWindow.cs` — new

## Tasks

1. **Window shell + single menu item.**
   - `public sealed class GpuGrassSceneWindow : EditorWindow`.
   - `[MenuItem("Tools/GPUGrass/Scene Grass Setup", false, 0)]` → `GetWindow<GpuGrassSceneWindow>("GPUGrass")`.
   - This is now the ONLY `Tools/GPUGrass/*` menu in the compiled project (demo builder is in `Samples~`).

2. **Shared-config picker.**
   - `ObjectField` for the shared `GpuGrassConfig` + a "Create / Find" button calling `GpuGrassSceneSetup.EnsureSharedConfig()`.
   - Persist the chosen config across domain reloads via `EditorPrefs` (path string) so the window reopens onto the same config.

3. **Embedded config inspector (edit grass props once).**
   - Cache `UnityEditor.Editor cachedConfigEditor` via `Editor.CreateEditor(sharedConfig)`.
   - Render inside a foldout "Grass Properties (shared)" → `cachedConfigEditor.OnInspectorGUI()` (all `[Header]/[Tooltip]` fields free).
   - **Lifecycle (risk mitigation):** recreate the cached editor when the config object changes; `DestroyImmediate(cachedConfigEditor)` on config swap and in `OnDisable`. Never leak.

4. **Per-terrain status list (baked DATA hidden — Guard / requirement 4).**
   - Enumerate `Terrain.activeTerrains`; for each show a read-only row: name · blade count · resolved tier.
   - Blade count source: the terrain's `GpuGrassController.Bake?.InstanceCount` (0 / "not set up" if no controller/bake yet); tier from `controller.ResolvedTier`.
   - Do NOT render the `GpuGrassBakeData` arrays — only the summary numbers.
   - Header line: "Terrains in scene (N)".

5. **"Setup & Bake All Terrains" button.**
   - Disabled when no shared config selected.
   - On click → `GpuGrassSceneSetup.SetupScene(sharedConfig)`; refresh status rows from the returned result; `Debug.Log` summary.
   - Wrap in `EditorUtility.DisplayProgressBar` if terrain count is large.

6. **Optimize (Performance) section (the "optimize tool").**
   - Foldout "Optimize (Performance)" exposing a curated subset for fast mobile tuning, edited on the SAME shared config (so it stays SSOT — these are just convenient duplicates of inspector fields, bound to the same `SerializedProperty`s, NOT new state):
     - `enableOcclusionCulling` (P3 flag) toggle
     - `enableAdaptiveDensity` + `adaptiveTargetFps` + `minDensity`
     - `lodMaxDistances` + `renderCullDistance`
     - `tierMode` + `enableTerrainFallback` + `lowEndMemoryThresholdMB`
   - Use a `SerializedObject` of the shared config for these so edits are undo-able and write back to the same asset (no derived/duplicated state — satisfies SSOT rule).
   - **"Re-apply & Rebuild"** button → for each controller in scene `controller.Rebuild()` (apply perf changes without a full re-bake).
   - **"Apply Mobile Preset"** button → set conservative values on the shared config (occlusion on, cull ≈ 60–80 m, adaptive density on, tier Auto) then mark dirty. One-click optimize.

7. **Repaint hygiene.**
   - `OnFocus` / scene-change → refresh terrain enumeration. Keep IMGUI; no UIToolkit dependency needed.

## Verification (manual, in live editor)

- Open `Tools ▸ GPUGrass ▸ Scene Grass Setup`; confirm it is the only GPUGrass menu.
- Create/select a shared config; edit a grass property once.
- In a scene with ≥2 terrains, click "Setup & Bake All Terrains": every terrain's controller ends with the SAME config instance (inspect) + its own distinct bake; status rows show per-terrain blade counts; bake arrays never shown.
- Optimize section: toggle occlusion / change cull distance → "Re-apply & Rebuild" updates fields on the one asset (undo works). "Apply Mobile Preset" sets conservative values.
- `read_console` clean.

## Definition of done

- Single-window workflow: edit-once shared config → bake all terrains, hidden bake data, working Optimize section. Old menu gone (from P1). No console errors; no leaked cached editors.
