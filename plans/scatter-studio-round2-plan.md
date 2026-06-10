# Plan: Scatter Studio Round 2 — config inspector, tabbed layout, always-on preview, brush cursor

**Branch:** `feat/grass-authoring-toolchain` · **Engine:** Unity 6 · UI Toolkit
**Source:** brainstorm 2026-06-10 (approved design). Builds on the shipped Scatter Studio (commit dfef595).
**Execution:** single parallel wave (3 file-disjoint units) → one compile+test gate.

## Approved decisions
| Topic | Choice |
|---|---|
| Edit-mode render | **Fully always-on** (step+submit+repaint every editor frame; remove Preview gate) + a menu kill-switch escape hatch |
| Layout | **Context tab bar** `[Layer][Brushes][Paint/Place]` over the content area; one panel at a time; auto-switch by layer type |
| Brush cursor | **Flat disc oriented to surface normal** at hit point |
| Open-from-config (no field) | **Offer "Create ScatterField"** button that spawns a wired GameObject |

## Ground truth (verified)
- `ScatterStudio.uxml` `content-area` stacks `layer-panel-scroll` + `#brush-library` + `#density-paint-panel` + `#instance-panel` in one column → the overlap.
- `ScatterField` already has `[ExecuteAlways]`; `ScatterFieldEditorTick.Tick()` gates on `PreviewEnabled` (default OFF) + `ShouldTick()` (selection/tool).
- `ScatterStudioWindow` binds config via `field.Config`; opened only from `ScatterField` selection. Views constructed in `CreateGUI`, bound in `SetActiveField`.
- Mount names `#brush-library` / `#density-paint-panel` / `#instance-panel` consumed by `BrushLibraryView` / `DensityPaintPanel` / `InstancePanel` ctors — MUST be preserved (relocate, don't rename).
- Tests: `GrassInteract.EditorTests` 6/6 (compile + regression gate).

## Dependency graph — all three units are FILE-DISJOINT → one parallel wave
```
U1 ScatterFieldEditorTick.cs            (always-on + kill-switch)
U2 DensityPaintTool.cs, InstancePlacementTool.cs   (oriented brush disc)
U3 ScatterStudio.uxml/.uss, ScatterStudioWindow.cs,
   TerrainScatterConfigEditor.cs (new), StudioTabs.cs (new)   (tabs + config-open + inspector)
```
No file is touched by two units → spawn U1+U2+U3 together; gate once.

**Cross-unit note (no file conflict):** U1 makes the tick ignore `PreviewEnabled` (always render). U3 removes the now-obsolete "Preview" header toggle from the uxml/window. They don't share files; U1 leaves the `PreviewEnabled` property in place (harmless) so nothing breaks if U3's toggle removal lands in any order.

---

## U1 — Always-on edit-mode rendering | Effort: S
**Owns:** `Editor/ScatterFieldEditorTick.cs`
- Remove the `!PreviewEnabled` early-out and the `ShouldTick()` selection/tool gate in `Tick()` → every editor frame (when `!Application.isPlaying`) step+submit+repaint all active `ScatterField`s. Keep the `MAX_DT` clamp and the first-frame skip.
- Add a project-persisted kill-switch (default rendering ON): a `[MenuItem("Tools/GrassInteract/Disable Edit-Mode Preview")]` checked toggle backed by an EditorPref; `Tick()` early-outs when disabled. This is the only escape hatch.
- Keep `PreviewEnabled`/`PreviewColliders` properties (other code may still read them); they no longer gate visibility.
- **Success:** with no selection and no tool active, grass/instances render + animate in the Scene in edit mode. Menu toggle stops it. Compiles; tests green.
- **Risks:** editor churn on large fields (accepted; clamp + kill-switch). L2·I3.

## U2 — Brush cursor conforms to surface | Effort: S
**Owns:** `Editor/DensityPaintTool.cs`, `Editor/InstancePlacementTool.cs` (sole owner of both)
- Where the brush/erase radius is drawn in the Scene, draw a flat disc oriented to the **surface normal at the cursor hit** (`Handles.DrawWireDisc(center, hit.normal, radius)` / matching `ScatterGizmos` call) instead of a world-XZ disc. Apply to DensityPaintTool brush ring and InstancePlacementTool place/erase radius.
- Do NOT change paint/placement math — visual cursor only.
- **Success:** brush ring tilts to follow the surface at the cursor on sloped ground. Compiles; tests green.
- **Risks:** none material. L1·I2.

## U3 — Tabbed window + config inspector + open-from-config | Effort: L
**Owns:** `Editor/ScatterStudio/ScatterStudio.uxml`, `Editor/ScatterStudio/ScatterStudio.uss` (append), `Editor/ScatterStudioWindow.cs`, `Editor/TerrainScatterConfigEditor.cs` (new), `Editor/ScatterStudio/StudioTabs.cs` (new, optional — may fold into window)

### B1. Tabbed content area
- UXML: replace the stacked `content-area` with a `tab-bar` (buttons `Layer` / `Brushes` / `Paint` / `Place`) + a `tab-content` host containing four pages: `layer-panel` page, `#brush-library` page, `#density-paint-panel` page, `#instance-panel` page. **Keep those three mount element names** (relocate into pages). Pages toggle via `display:none/flex`.
- Window: a small tab controller selects the active page. Auto-context on layer selection — Density layer → `[Layer][Brushes][Paint]`; Instance layer → `[Layer][Place]`; none → `[Layer]` only. Disable/hide irrelevant tabs.
- USS: append tab-bar + tab-button (active/hover) + page styles (pro/light scoped, matching the existing theme tokens). Append only.

### B2. Remove obsolete Preview toggle
- Remove the `#preview-toggle` from uxml + its wiring in `WireHeader` (always-on now). Keep `#colliders-toggle` and `#rebuild-button`.

### B3. Config asset inspector + open
- New `[CustomEditor(typeof(TerrainScatterConfig))]` `TerrainScatterConfigEditor`: summary (layer count by type, brush count, GPU material/compute assigned?) + a prominent "Open in Scatter Studio" button (→ `ScatterStudioWindow.OpenForConfig(config)`) + default inspector under a foldout.
- `[OnOpenAsset]` static callback: double-click a `TerrainScatterConfig` → `OpenForConfig` and return true; ignore other asset types.

### B4. Config-only window binding + Create ScatterField
- `ScatterStudioWindow.OpenForConfig(TerrainScatterConfig)`: open + bind the config directly. Resolve an owning `ScatterField` in the scene (`FindObjectsByType<ScatterField>()` where `Config == config`).
  - If found → behave as today (full features).
  - If none → bind config for layer/brush editing; show a "Create ScatterField" button that creates a GameObject + `ScatterField`, assigns the config (via `SerializedObject`), Undo-registered, selects it, then rebinds. Paint/Place tabs show a "needs a ScatterField" hint until one exists.
- Refactor `SetActiveField` so binding can be driven by a config (with optional field) — keep selection-following behavior intact.

- **Success:** double-click or button opens the window on the config; with no field, Create ScatterField wires one and enables paint/preview; tabs separate brushes from layer props (no overlap); Preview toggle gone. Compiles; tests green; window still follows ScatterField scene selection.
- **Risks:** tab restructure must preserve mount names (else views fail to attach) — L2·I4, mitigated by keeping names. `OnOpenAsset` over-trigger — L1·I3, type-guarded. Config-only `SetActiveField` refactor regressing selection-follow — L2·I3, keep the Selection path.

---

## Wave gate (single)
Force script refresh → `read_console` (full error set) → `GrassInteract.EditorTests`. Blind-implement all 3 units, verify once, fix errors in a batch, re-verify. Orchestrator handles any view→window wiring left by ownership splits (U3 owns the whole window+uxml, so none expected).

## Definition of done
1. Compile clean; tests 6/6.
2. Grass/instances always visible + animating in edit mode (no toggle); menu kill-switch works.
3. Brush ring follows surface normal at cursor.
4. Brushes live in their own tab — never overlapping layer properties; tabs auto-switch by layer type.
5. Double-click config OR inspector button opens Scatter Studio; Create-ScatterField path works when no field exists.
6. Window still follows `ScatterField` scene selection; live re-scatter via `ScatterRebuildScheduler` unchanged.
