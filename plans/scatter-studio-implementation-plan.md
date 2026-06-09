# Plan: Scatter Studio — TerrainScatterConfig Authoring Redesign (Implementation)

**Source design (approved):** `plans/reports/scatter-studio-uiux-redesign-design.md`
**Branch:** `feat/grass-authoring-toolchain`
**Engine:** Unity 6 · UI Toolkit (UXML + USS + C# controller) `EditorWindow`
**Execution model:** `/t1k:team` parallel fan-out, strict non-overlapping file ownership.

This plan turns the 5 approved design phases into parallel-executable workstreams with concrete, spawn-ready Team Shape rosters. The design is approved — defaults follow the design doc for anything it resolved.

---

## Ground truth (verified against codebase)

| Fact | Value | Source |
|---|---|---|
| Runtime asmdef | `GrassInteract` (`Assets/GrassInteract/GrassInteract.asmdef`) | verified |
| Editor asmdef | `GrassInteract.Editor` (`Assets/GrassInteract/Editor/GrassInteract.Editor.asmdef`) | verified |
| Tests asmdef | `GrassInteract.EditorTests` (`Assets/GrassInteract/Tests/Editor/`) | verified |
| Config uses Odin | `TerrainScatterConfig` has `[TitleGroup]`/`[TabGroup]` (Sirenix) | verified — window binds `SerializedObject`, NOT Odin drawers |
| Rebuild SSOT | `ScatterRebuildScheduler.MarkDirty(field, layerIdx)` / `MarkAllLayersDirty(field)` — 0.15s debounce | verified — reuse, no parallel path |
| Layer→field lookup | `ScatterFieldLookup.FindOwningField(layer)` / `MarkDirtyForLayer(layer)` | verified — reuse |
| Tool state today | `EditorPrefs` keys `GrassInteract.Brush.*` (DensityPaintTool) + `GrassInteract.Place.*` (InstancePlacementTool) | verified — migrate to `ScatterAuthoringState` |
| Stamp selection today | `EditorPrefs` `GrassInteract.Brush.Stamp` = int index into `config.BrushStamps` | verified — replaced by `StampRef` |
| Density paint persistence | writes R8 `densityMap`, PNG via `File.WriteAllBytes` + `ImportAsset(ForceUpdate)` | verified — `DensityMapFactory` must produce assets compatible with this |
| Instance scale today | `InstanceScatterLayer.ScaleRange` is **auto-computed** from authored records | verified — new override field decouples this |

---

## Feasibility

- **Reuse check:** `ScatterRebuildScheduler`, `ScatterFieldLookup`, `ScatterGizmos`, `GrassFieldSpace` (world↔UV SSOT), existing PNG-persist + R8-import logic in `DensityPaintTool.SaveToAsset` (extract into `DensityMapFactory`). `DensityPaintTool` / `InstancePlacementTool` kept as `EditorTool` scene overlays; only their *state source* changes.
- **NEW:** `ScatterStudioWindow` (+ UXML/USS), `ScatterAuthoringState` (ScriptableSingleton), `ScatterBrushLibrary`, `DensityMapFactory`, density scene-overlay renderer, one runtime field on `InstanceScatterLayer`.
- **Complexity:** moderate–complex (5 phases; the runtime field is trivial, the window + theming are the bulk).
- **Third-party:** none. All new code in existing asmdefs. No vendor coupling (`library-third-party-decoupling` satisfied — UI Toolkit + UnityEditor are platform stdlib).

---

## Dependency graph (critical path)

```
P0 (runtime field + asmdef sanity)        ──┐
P1.A ScatterAuthoringState (SSOT) ─────────┐ │
P2.A ScatterBrushLibrary asset ───────────┐│ │
                                          ▼▼ ▼
P1.B ScatterStudioWindow shell (UXML/USS/controller) ── needs P1.A for header toggles
   │
   ├─► P2.B Brush library UI + DensityMapFactory + tool rewire  (needs P1.A, P1.B, P2.A)
   │
   ├─► P3 Density polish (scene overlay + icon modes)            (needs P1.A, P1.B, P2.B)
   │
   ├─► P4 Instance polish (scatter-brush, multiselect, override) (needs P0, P1.A, P1.B)
   │
   └─► P5 Theming pass (USS theme + animations)                 (needs P1.B; ideally last)
```

**Hard ordering:**
- `ScatterAuthoringState` (P1.A) MUST exist before any tool rewire (P2.B) or window state binding.
- `ScatterStudioWindow` shell (P1.B) MUST exist before any UI workstream that adds panels to it (P2.B, P3, P4 in-window panels, P5 theming).
- Inspector deletion (P1.B) MUST verify no external dependents first (risk row R7).

**Parallel-safe from the start:** P0, P1.A, P2.A have zero file overlap and no inter-dependency → spawn together as wave 1.

---

## Team Layout (upfront roster)

| Unit | Agent identity | model | Owns (globs) | Worktree | Wave |
|---|---|---|---|---|---|
| P0 | `t1k-fullstack-developer` | sonnet | `Runtime/InstanceScatterLayer.cs` | no (shared tree, single file) | 1 |
| P1.A | `t1k-fullstack-developer` | sonnet | `Editor/ScatterAuthoringState.cs` (new) | no | 1 |
| P2.A | `t1k-fullstack-developer` | sonnet | `Editor/ScatterBrushLibrary.cs` (new), `Editor/ScatterBrushLibraryProvider.cs` (new) | no | 1 |
| P1.B | `t1k-fullstack-developer` | sonnet | `Editor/ScatterStudioWindow.cs` (new), `Editor/ScatterStudio/*.uxml/.uss` (new), delete 3 editors | no | 2 |
| P2.B | `t1k-fullstack-developer` | sonnet | `Editor/DensityMapFactory.cs` (new), `Editor/ScatterStudio/BrushLibraryView.cs` (new), `Editor/DensityPaintTool.cs` (rewire) | no | 3 |
| P3 | `t1k-fullstack-developer` | sonnet | `Editor/ScatterDensityOverlay.cs` (new), `Editor/ScatterStudio/DensityPaintPanel.cs` (new) | no | 4 |
| P4 | `t1k-fullstack-developer` | sonnet | `Editor/InstancePlacementTool.cs` (rewire), `Editor/ScatterStudio/InstancePanel.cs` (new) | no | 4 |
| P5 | `t1k-fullstack-developer` | sonnet | `Editor/ScatterStudio/ScatterStudio.uss` (theme), `Editor/ScatterStudio/ScatterStudioLight.uss` (new) | no | 5 |
| Verify | `t1k-tester` | sonnet | read-only + run tests | no | each wave gate |

**Worktree note:** all units share the one Unity working tree (one Library/, one domain). Per `parallelize-batch-work.md` + `ai-velocity-batch-compile-unity.md`, do NOT give units separate git worktrees — Unity serializes on a single Library/domain reload. Instead, enforce **non-overlapping file ownership** and **wave-gated compile** (each wave compiles + tests ONCE before the next wave spawns). Use the pathspec commit form (`git commit -- <files>`) per `parallel-teammate-git-index-race.md`; new files need explicit `git add <path>` first.

**Skills to activate (all UI units):** `t1k-unity-ui-toolkit` (UXML/USS/EditorWindow patterns), `unity-code-conventions`. Editor-automation units may reference `t1k-unity-base-mcp-skill` only if MCP-driven asset creation is needed (not required — `AssetDatabase` calls suffice).

---

## Conventions (binding on every unit)

- `code-conventions-unity.md`: private fields `camelCase` no underscore; `this.` prefix mandatory; `PascalCase` public; `UPPER_SNAKE_CASE` consts; `#nullable enable` in new files; serialized fields `[SerializeField] private`. VContainer N/A (editor-only).
- One responsibility per file, ≤200 lines where reasonable (window controller may exceed — split into partial classes / sub-views under `Editor/ScatterStudio/`).
- All re-scatter goes through `ScatterRebuildScheduler.MarkDirty` — never a direct `Rebuild()`/`RebuildLayer()`.
- All asset mutations wrapped in `Undo.RegisterCompleteObjectUndo` / `Undo.RegisterCreatedObjectUndo` (sub-asset creation) so undo/redo holds.
- No hand-made assets: `DensityMapFactory` and `+ New Stamp` must produce assets with correct R8/readable/uncompressed import settings programmatically.

---

## Phase 0 — Runtime field (scale-range override) | Effort: S

**Scope:** Add the manual scale-range override field to `InstanceScatterLayer` so instance scale is decoupled from the auto-computed min/max (auto remains default). Only runtime file touched in the whole plan.

**Owns:** `Runtime/InstanceScatterLayer.cs`

### Implementation
- Add `[SerializeField] private bool overrideScaleRange = false;` and `[SerializeField] private Vector2 scaleRangeOverride = new Vector2(1f, 1f);`.
- Change `ScaleRange` accessor: `=> this.overrideScaleRange ? this.scaleRangeOverride : ComputeScaleRangeFromAuthored();`.
- Add public read accessors `OverrideScaleRange` / `ScaleRangeOverride` for the window to bind.
- Keep `ComputeScaleRangeFromAuthored()` untouched (still the default path).

### Team Shape
| Field | Value |
|---|---|
| agent | `t1k-fullstack-developer` |
| model | sonnet |
| ownership | `Assets/GrassInteract/Runtime/InstanceScatterLayer.cs` |
| worktree | no |
| spawn order | wave 1 (parallel with P1.A, P2.A) |
| skills | `unity-code-conventions` |

### Success criteria
- Compiles clean. Default (`overrideScaleRange=false`) yields identical `ScaleRange` to current behavior (auto-computed). With override on, `ScaleRange` returns the manual `Vector2`.

### Risk Assessment
| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Existing assets lose scale behavior on deserialize | 2 | 3 | 6 | New bool defaults `false` → existing assets keep auto path; no migration needed |
| `scaleRangeOverride.x <= 0` breaks downstream validation | 2 | 2 | 4 | Window UI clamps min > 0; document in tooltip |

---

## Phase 1.A — ScatterAuthoringState (SSOT for tool state) | Effort: M

**Scope:** `ScriptableSingleton<ScatterAuthoringState>` replacing the scattered `EditorPrefs` reads in both tools. Single source for mode/size/opacity/falloff/flow + active brush + place settings (align/yaw/scale/eraseRadius) + `StampRef`.

**Owns:** `Editor/ScatterAuthoringState.cs` (new)

### Implementation
- `internal sealed class ScatterAuthoringState : ScriptableSingleton<ScatterAuthoringState>`.
- Fields mirroring current EditorPrefs: paint (`brushSize`, `brushOpacity`, `brushFalloff`, `brushFlow`, `paintMode`), place (`alignToNormal`, `randomYaw`, `placeScaleMin`, `placeScaleMax`, `eraseRadius`, `placeMode`).
- `StampRef`: a serializable struct `{ enum Source { None, Config, Global }, int index }` resolving to either `TerrainScatterConfig.BrushStamps[i]` or `ScatterBrushLibrary` (P2.A) — define the struct here as the SSOT, but resolution-to-texture stays in the consumer (P2.B) to avoid a P1.A→P2.A compile dependency. **Sequencing: P1.A defines `StampRef` with `Source`/`index` ints only; it does NOT reference `ScatterBrushLibrary`.**
- One-time migration: `[InitializeOnLoadMethod]` static migrator that, if a `migratedFromEditorPrefs` flag is false, reads the legacy `GrassInteract.Brush.*` + `GrassInteract.Place.*` keys into the singleton, sets the flag, saves. Errors-over-silent: log a one-line `Debug.Log` confirming migration ran.
- `Save(true)` on mutation; expose `static ScatterAuthoringState I => instance`.

### Team Shape
| Field | Value |
|---|---|
| agent | `t1k-fullstack-developer` |
| model | sonnet |
| ownership | `Assets/GrassInteract/Editor/ScatterAuthoringState.cs` |
| worktree | no |
| spawn order | wave 1 (parallel with P0, P2.A) — NO dependency on P2.A (StampRef is int-only) |
| skills | `t1k-unity-ui-toolkit`, `unity-code-conventions` |

### Success criteria
- Singleton persists across domain reload. First load with legacy EditorPrefs present → values migrate exactly once (re-running does not re-migrate). Compiles with zero reference to `ScatterBrushLibrary`.

### Risk Assessment
| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Migration drops current user settings | 3 | 3 | 9 | Read every existing key with its current default; gate on a persisted `migrated` flag; log confirmation |
| `StampRef` couples to `ScatterBrushLibrary` → wave-1 dependency cycle | 3 | 3 | 9 | StampRef stores `Source` enum + int index only; resolution lives in P2.B consumer |
| ScriptableSingleton path collision | 1 | 2 | 2 | Default `ProjectSettings/` storage; no custom `filePath` needed |

---

## Phase 2.A — ScatterBrushLibrary (shared global asset) | Effort: M

**Scope:** Project-wide shared `BrushStamp` library asset, usable by any config; per-config stamps remain on `TerrainScatterConfig.brushStamps`.

**Owns:** `Editor/ScatterBrushLibrary.cs` (new), `Editor/ScatterBrushLibraryProvider.cs` (new)

### Implementation
- `ScatterBrushLibrary : ScriptableObject` holding `List<BrushStamp>` shared stamps (stamps as sub-assets of the library asset, mirroring how config stamps are sub-assets today). Keep editor-only if possible — place under `Editor/` asmdef so `BrushStamp` (runtime type) is referenced, not the reverse. `BrushStamp` is already runtime (`GrassInteract` asmdef) and accessible from Editor.
- `ScatterBrushLibraryProvider`: locates-or-creates the single project library asset under a settings folder (e.g. `Assets/GrassInteract/Editor/Settings/ScatterBrushLibrary.asset`) lazily on first access. `AssetDatabase.CreateAsset` + folder ensure. No hand creation.
- API: `IReadOnlyList<BrushStamp> Stamps`, `BrushStamp AddStamp(Texture2D? shape)`, `void Remove(int)`, `void Rename(int, string)` — all Undo-wrapped.

### Team Shape
| Field | Value |
|---|---|
| agent | `t1k-fullstack-developer` |
| model | sonnet |
| ownership | `Assets/GrassInteract/Editor/ScatterBrushLibrary.cs`, `Assets/GrassInteract/Editor/ScatterBrushLibraryProvider.cs` |
| worktree | no |
| spawn order | wave 1 (parallel with P0, P1.A) |
| skills | `unity-code-conventions` |

### Success criteria
- First access auto-creates the library asset (no manual `Assets > Create`). Adding a stamp from a texture forces R8/readable on the shape and creates a `BrushStamp` sub-asset. Undo removes both.

### Risk Assessment
| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Library asset path duplicates if two callers race first-create | 2 | 3 | 6 | Provider guards with `AssetDatabase.LoadAssetAtPath` check before create; single-threaded editor so no true race |
| Editor-only asset referenced from a built player | 1 | 4 | 4 | Library + provider live in `Editor/` asmdef (excluded from player build); BrushStamp stays runtime |

---

## Phase 1.B — ScatterStudioWindow shell | Effort: L

**Scope:** The window: header field picker + preview/collider toggles + Rebuild; left layer rail (create/remove/reorder/select); center data-bound layer panel via `PropertyField` on `SerializedObject`. Deletes the 3 custom inspectors. Eliminates manual sub-asset wiring.

**Owns:** `Editor/ScatterStudioWindow.cs` (new), `Editor/ScatterStudio/ScatterStudio.uxml` (new), `Editor/ScatterStudio/ScatterStudio.uss` (new, minimal layout — full theme is P5), `Editor/ScatterStudio/LayerRailView.cs` (new), `Editor/ScatterStudio/LayerPanelView.cs` (new). **Deletes:** `Editor/ScatterFieldEditor.cs`, `Editor/DensityScatterLayerEditor.cs`, `Editor/InstanceScatterLayerEditor.cs`.

### Implementation
- `ScatterStudioWindow : EditorWindow`, `[MenuItem("Tools/GrassInteract/Scatter Studio")]`. Load UXML/USS via `AssetDatabase.LoadAssetAtPath` (or `UIElementsEntryPoint`-style relative load).
- Selection-aware: track active `ScatterField` via `Selection` + a header field picker (`ObjectField`/popup). Build `SerializedObject` of the active config; bind layer panel with `rootVisualElement.Bind(serializedObject)`.
- Header toggles wire to `ScatterFieldEditorTick.PreviewEnabled` / `PreviewColliders` (keep that class — verify it survives the editor deletion; it's a separate file `ScatterFieldEditorTick.cs`, NOT deleted). Rebuild button → `field.Rebuild()` (the explicit escape hatch, same as old inspector).
- Layer rail: list of layers, color chips (Density=green, Instance=amber), enable toggle, `+ Density`/`+ Instance` (create sub-asset → `AssetDatabase.AddObjectToAsset` → append `config.layers` via `SerializedProperty`; instance layers auto-spawn `AuthoredInstancesData` sub-asset), remove, drag-reorder (`Undo.RegisterCompleteObjectUndo(config)` + serialized list reorder).
- Layer panel: `PropertyField` foldout cards (Render/Wind/Deform/Bounds/Placement/type-specific). All edits → on `SerializedObject.ApplyModifiedProperties`, call `ScatterRebuildScheduler.MarkDirtyForLayer` equivalent (use `ScatterFieldLookup.MarkDirtyForLayer` per selected layer, or `MarkAllLayersDirty` for field-level edits).
- **Inspector deletion guard (R7):** before deleting, grep the codebase + tests for references to the 3 editor type names; confirm zero external dependents. Unity default inspector remains for raw field access (design decision).

### Team Shape
| Field | Value |
|---|---|
| agent | `t1k-fullstack-developer` |
| model | sonnet |
| ownership | `Editor/ScatterStudioWindow.cs`, `Editor/ScatterStudio/ScatterStudio.uxml`, `Editor/ScatterStudio/ScatterStudio.uss`, `Editor/ScatterStudio/LayerRailView.cs`, `Editor/ScatterStudio/LayerPanelView.cs`; deletes the 3 editor `.cs` |
| worktree | no |
| spawn order | wave 2 (after wave 1 compiles green; needs P1.A for state binding) |
| skills | `t1k-unity-ui-toolkit`, `unity-code-conventions` |

### Success criteria
- Window opens from menu; follows selected `ScatterField`. `+ Density` and `+ Instance` create a fully-wired layer sub-asset with zero manual `Assets > Create`. Edits in the panel re-scatter live via the scheduler. Drag-reorder is undoable. The 3 inspectors are gone and Unity falls back to the default inspector for those types. Compiles clean.

### Risk Assessment
| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| `SerializedObject` binding to Odin-decorated `TerrainScatterConfig` misbehaves | 3 | 3 | 9 | Window binds raw `SerializedProperty` paths (`layers`, `brushStamps`) — Odin attrs are inert for `PropertyField`; verify in smoke test |
| Sub-asset create/append not undoable as one step | 3 | 4 | **12** | Wrap create + list-append in one `Undo.RegisterCreatedObjectUndo` + `RegisterCompleteObjectUndo(config)` group; test undo restores both |
| Deleting inspectors breaks an external dependent (R7) | 2 | 4 | 8 | Grep all `.cs` + tests for the 3 type names BEFORE delete; if any hit, sequence the dependent fix first |
| UXML/USS asset-path load fails at runtime | 2 | 3 | 6 | Use stable relative path under `Editor/ScatterStudio/`; assert non-null on load with a clear error |

---

## Phase 2.B — Brush library UI + DensityMapFactory + tool rewire | Effort: L

**Scope:** Brush library thumbnail grid (global|config tab, None tile, rename, drag-reorder, `+ New Stamp`); `DensityMapFactory.CreateBlank`; rewire `DensityPaintTool` to read `ScatterAuthoringState` + resolve `StampRef`.

**Owns:** `Editor/DensityMapFactory.cs` (new), `Editor/ScatterStudio/BrushLibraryView.cs` (new), `Editor/DensityPaintTool.cs` (rewire — sole owner this wave). Adds a brush panel mount point already present in `ScatterStudio.uxml` (P1.B owns the uxml; P2.B adds the view code that attaches to a named element — **no uxml edit needed if P1.B reserves a `#brush-library` container**; sequencing note below).

### Implementation
- `DensityMapFactory.CreateBlank(int size, Object owner)`: builds a blank `Texture2D` (R8, readable, uncompressed), writes a PNG to the project, sets import settings (`TextureImporter` → `R8`/`isReadable=true`/no compression), re-imports, and returns the asset. Extract the existing PNG-persist logic from `DensityPaintTool.SaveToAsset` into here (SSOT — DRY). `DensityScatterLayer` first-paint with no map → window calls factory, assigns `densityMap` via SerializedProperty.
- `BrushLibraryView`: thumbnail grid bound to either `config.BrushStamps` or `ScatterBrushLibrary.Stamps` per active tab; "None (procedural)" tile; click sets `ScatterAuthoringState.StampRef`; inline rename; drag-reorder; `+ New Stamp` (from dragged texture or blank) → `ScatterBrushLibraryProvider.AddStamp` (global tab) or config stamp append (config tab).
- `DensityPaintTool` rewire: replace `EditorPrefs` static props with reads from `ScatterAuthoringState.I`; replace `StampIndex`/`ResolveStamp` with `StampRef` resolution (Source.Config → `field.Config.BrushStamps`, Source.Global → `ScatterBrushLibraryProvider.Library.Stamps`). Shrink the in-scene `DrawSettingsWindow` to brush cursor + mode label only (settings now live in window). Keep the paint kernel, `GrassFieldSpace` mapping, PNG persist (now calling `DensityMapFactory` helper), and `ScatterRebuildScheduler.MarkDirty`.

### Sequencing
- P2.B depends on: P1.A (`ScatterAuthoringState`), P2.A (`ScatterBrushLibrary`), P1.B (window shell + `#brush-library` mount container in uxml). **P1.B MUST reserve a named `#brush-library` VisualElement in `ScatterStudio.uxml`** so P2.B attaches `BrushLibraryView` without editing the uxml (no shared-file write). This is recorded in P1.B's deliverables.
- `DensityPaintTool.cs` is owned **solely** by P2.B this wave — no other unit touches it.

### Team Shape
| Field | Value |
|---|---|
| agent | `t1k-fullstack-developer` |
| model | sonnet |
| ownership | `Editor/DensityMapFactory.cs`, `Editor/ScatterStudio/BrushLibraryView.cs`, `Editor/DensityPaintTool.cs` |
| worktree | no |
| spawn order | wave 3 (after P1.A, P2.A, P1.B green) |
| skills | `t1k-unity-ui-toolkit`, `unity-code-conventions` |

### Success criteria
- A blank density map is auto-created on first paint with correct R8/readable/uncompressed settings (zero manual import). A brush stamp is created and used without leaving the window. `DensityPaintTool` reads all settings from `ScatterAuthoringState`; the legacy EditorPrefs path is gone. Painting still re-scatters live and persists to PNG. Compiles clean.

### Risk Assessment
| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| `TextureImporter` settings not applied before first GetPixels | 3 | 4 | **12** | Set importer + `ImportAsset(ForceUpdate)` THEN `LoadAssetAtPath` the re-imported texture; assert `isReadable` |
| Extracting `SaveToAsset` into factory regresses live paint persist | 3 | 4 | **12** | Move logic verbatim; keep one call site; smoke-test a paint→reload→pixels-survive cycle |
| `StampRef` resolution diverges from old StampIndex semantics | 2 | 3 | 6 | Map `Source.Config` to the same `field.Config.BrushStamps` list used today |
| Two units edit `DensityPaintTool.cs` | 1 | 4 | 4 | Sole-owner rule (P2.B only this wave); P3 icon-mode toggles touch the *window panel*, not the tool |

---

## Phase 3 — Density polish (scene overlay + icon modes) | Effort: M

**Scope:** Scene density heatmap overlay (projected onto field) with toggle + live thumbnail; icon-based Paint/Erase/Smooth toggle group in the window (replaces the dropdown).

**Owns:** `Editor/ScatterDensityOverlay.cs` (new), `Editor/ScatterStudio/DensityPaintPanel.cs` (new).

### Implementation
- `ScatterDensityOverlay`: render the density map as a heatmap on the field surface (Handles-drawn textured quad / gizmo or `Graphics.DrawMeshNow` in `SceneView.duringSceneGui`). Toggle stored in `ScatterAuthoringState`. Reuse `GrassFieldSpace` for placement extent. Deferrable/isolated — owns its own file.
- `DensityPaintPanel`: in-window paint controls — icon mode toggle group (`Paint`/`Erase`/`Smooth` via `EditorGUIUtility.IconContent`), size/opacity/falloff/flow sliders bound to `ScatterAuthoringState`, live heatmap thumbnail (small `Image` element sampling the density map). Attaches to a `#density-paint-panel` container reserved by P1.B.
- "Paint" entry button → `ToolManager.SetActiveTool<DensityPaintTool>()`.

### Sequencing
- Depends on P1.A (state), P1.B (window + reserved container), P2.B (`DensityPaintTool` rewired + `DensityMapFactory`). Does NOT edit `DensityPaintTool.cs`.

### Team Shape
| Field | Value |
|---|---|
| agent | `t1k-fullstack-developer` |
| model | sonnet |
| ownership | `Editor/ScatterDensityOverlay.cs`, `Editor/ScatterStudio/DensityPaintPanel.cs` |
| worktree | no |
| spawn order | wave 4 (parallel with P4 — disjoint files) |
| skills | `t1k-unity-ui-toolkit`, `unity-code-conventions` |

### Success criteria
- Heatmap overlay toggles on/off and matches painted density. Icon mode group switches Paint/Erase/Smooth and the tool honors it via `ScatterAuthoringState`. Live thumbnail updates after a stroke. Compiles clean; no regression in painting.

### Risk Assessment
| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Overlay rendering wrong projection vs field | 3 | 2 | 6 | Reuse `GrassFieldSpace` UV↔world (same SSOT the painter uses) |
| Overlay tanks Scene view FPS | 2 | 3 | 6 | Draw single textured quad, not per-pixel gizmos; gate behind toggle (default off) |
| Heatmap thumbnail not refreshing after stroke | 2 | 2 | 4 | Subscribe to a post-stroke repaint hook; rebuild `Image` texture on `MarkDirty` flush |

---

## Phase 4 — Instance polish | Effort: M

**Scope:** Scatter-brush place mode (many within radius respecting `placeSpacing`); collider indicators (colored dots); multi-select + batch edit; scale-range override UI (binds P0 field); per-instance panel moved into the window dock.

**Owns:** `Editor/InstancePlacementTool.cs` (rewire — sole owner this wave), `Editor/ScatterStudio/InstancePanel.cs` (new).

### Implementation
- `InstancePlacementTool` rewire: read settings from `ScatterAuthoringState` (replace `GrassInteract.Place.*` EditorPrefs). Add `PlaceMode.Scatter` (place N within brush radius respecting `placeSpacing`). Add multi-select (`HashSet<int>` of indices) with batch transform/collider. Collider indicators: tint `ScatterGizmos.InstanceDot` for records with `InstanceOverrideMask.ColliderConfigured`. Shrink in-scene HUD to cursor + mode label.
- `InstancePanel` (in-window): per-instance editor (moved out of the scene overlay `DrawSelectedInstance`) — collider config, mesh/material override, transform; multi-select batch fields; scale-range override toggle + `Vector2` (binds `InstanceScatterLayer.overrideScaleRange` / `scaleRangeOverride` from P0). Attaches to `#instance-panel` container reserved by P1.B.
- All edits Undo-wrapped + `ScatterRebuildScheduler.MarkDirty`.

### Sequencing
- Depends on P0 (override field), P1.A (state), P1.B (window + reserved container). Does NOT depend on P2.B/P3 (disjoint). Parallel-safe with P3 (no shared files).
- `InstancePlacementTool.cs` sole-owned by P4 this wave.

### Team Shape
| Field | Value |
|---|---|
| agent | `t1k-fullstack-developer` |
| model | sonnet |
| ownership | `Editor/InstancePlacementTool.cs`, `Editor/ScatterStudio/InstancePanel.cs` |
| worktree | no |
| spawn order | wave 4 (parallel with P3 — disjoint files) |
| skills | `t1k-unity-ui-toolkit`, `unity-code-conventions` |

### Success criteria
- Scatter-brush places multiple instances honoring `placeSpacing`. Collider-configured instances show a distinct indicator. Multi-select + batch edit works and is undoable. Scale-range override toggles auto vs manual (matches P0). Per-instance panel lives in the window. Compiles clean; single-click place/select/erase still work.

### Risk Assessment
| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Scatter-brush spacing check O(n²) stalls on dense fields | 3 | 3 | 9 | Reuse existing `RespectsSpacing` pattern; cap per-stroke candidates; spatial-hash if profiling shows stall |
| Multi-select batch edit undo granularity wrong | 3 | 3 | 9 | One `RegisterCompleteObjectUndo(authored)` per batch op, then `Commit` once |
| Moving per-instance panel out of overlay loses Phase-1 collider payload | 2 | 4 | 8 | Port `DrawSelectedInstance` logic verbatim into `InstancePanel`; keep `AuthoredInstancesData` API calls identical |

---

## Phase 5 — Theming pass | Effort: M

**Scope:** Full USS theme — rounded section cards, layer color chips, built-in/editor icons, hover/active transitions, drag-reorder animation, pro/light skin variants via `EditorGUIUtility.isProSkin` class toggle.

**Owns:** `Editor/ScatterStudio/ScatterStudio.uss` (theme expansion — P1.B created a minimal-layout version; P5 owns the theme expansion), `Editor/ScatterStudio/ScatterStudioLight.uss` (new light variant).

### Sequencing
- `ScatterStudio.uss` is created by P1.B (minimal layout) and **theme-expanded by P5**. To avoid a shared-file write conflict: P1.B's uss contains ONLY structural layout (flex, sizes, named selectors). P5 appends theme rules (colors, radius, transitions) and adds the light variant. **Because both touch `ScatterStudio.uss`, P5 runs strictly AFTER P1.B (different waves) — never concurrently.** P5 is the last wave; no other unit edits uss after P1.B.
- The window controller (P1.B) must already add/remove a `pro`/`light` root class based on `isProSkin` so P5 only writes USS, not C#.

### Team Shape
| Field | Value |
|---|---|
| agent | `t1k-fullstack-developer` |
| model | sonnet |
| ownership | `Editor/ScatterStudio/ScatterStudio.uss`, `Editor/ScatterStudio/ScatterStudioLight.uss` |
| worktree | no |
| spawn order | wave 5 (last; after all functional waves green) |
| skills | `t1k-unity-ui-toolkit` |

### Success criteria
- Themed cards/chips/icons render; pro and light skins both legible; transitions/drag animation present; no functional regression (theming is USS-only + the pre-existing skin-class toggle). Compiles clean.

### Risk Assessment
| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| USS theme breaks layout flex from P1.B | 2 | 2 | 4 | P5 appends theme rules; does not alter structural selectors |
| Light/pro skin contrast unreadable | 2 | 2 | 4 | Test both via `isProSkin` toggle; use editor palette tokens |
| Concurrent edit of `ScatterStudio.uss` with P1.B | 1 | 4 | 4 | Wave ordering: P5 strictly after P1.B; P1.B = layout-only, P5 = theme-only |

---

## Timeline

| Phase | Effort | Wave | Notes / dependency |
|---|---|---|---|
| P0 runtime field | S | 1 | independent |
| P1.A ScatterAuthoringState | M | 1 | independent (StampRef int-only) |
| P2.A ScatterBrushLibrary | M | 1 | independent |
| P1.B Window shell | L | 2 | needs P1.A; reserves uxml mount containers; deletes 3 inspectors |
| P2.B Brush UI + factory + tool rewire | L | 3 | needs P1.A, P2.A, P1.B |
| P3 Density polish | M | 4 | needs P1.A, P1.B, P2.B; parallel with P4 |
| P4 Instance polish | M | 4 | needs P0, P1.A, P1.B; parallel with P3 |
| P5 Theming | M | 5 | needs P1.B; strictly after (shared uss) |
| **Total** | **~3L + 4M + 1S** | 5 waves | **Critical path: P1.A → P1.B → P2.B → P3 → (gate) → P5** |

**Wave gate (each wave):** spawn `t1k-tester` to compile (read_console — full error set, not first error) + run `GrassInteract.EditorTests` ONCE. Per `ai-velocity-batch-compile-unity.md`: blind-implement the whole wave's files, verify ONCE, collect all errors, fix in parallel, verify again. Do NOT verify per-file. No wave N+1 spawn until wave N is green.

---

## Overall verification gate (definition of done)

All must hold before the feature is "done":

1. **Compile clean** — `read_console` shows zero errors after the final wave (full domain reload).
2. **Tests pass** — `GrassInteract.EditorTests` green (including `AuthoredInstancesDataBlobTests`); zero failures.
3. **Zero-manual-asset acceptance test** — from an empty `TerrainScatterConfig`: open Scatter Studio → `+ Density` → paint first stroke, with NO `Assets > Create` and NO manual import-settings edit. Density map auto-created R8/readable/uncompressed.
4. **Brush-without-leaving-window** — create + use a brush stamp entirely inside the window.
5. **Undo/redo** — every window edit (layer create/remove/reorder, paint stroke, place, transform, collider config, scale override) is undoable and redoable.
6. **No re-scatter regression** — all edits still route through `ScatterRebuildScheduler` (0.15s debounce); no parallel rebuild path introduced. Verify a slider drag re-scatters once, not per-frame.
7. **Inspector removal clean** — the 3 deleted editors have zero remaining references; Unity default inspector renders for those types.
8. **Migration** — legacy `EditorPrefs` settings imported into `ScatterAuthoringState` exactly once.

---

## Behavioral checklist (pre-handoff verification)

- [x] Data flows — paint/place edits traced: window → `ScatterAuthoringState`/`SerializedObject` → tool/asset → `ScatterRebuildScheduler` → `ScatterField.RebuildLayer`.
- [x] Dependency graph — blockers explicit; waves labeled; critical path identified.
- [x] Risk assessment — every phase scored; R8/R9 high-risk rows (score 12) have mitigations (sub-asset undo, TextureImporter ordering, factory extraction).
- [x] Backwards compatibility — P0 field defaults preserve behavior; EditorPrefs migration preserves user settings; additive elsewhere.
- [x] Test matrix — each phase has a pass/fail criterion; overall gate is reproducible.
- [x] Rollback — each new file is independently revertable; tool rewires are single-file; inspector deletion guarded by pre-delete grep.
- [x] File ownership — no two units in the same wave touch the same file; shared-file cases (`ScatterStudio.uss`, `DensityPaintTool.cs`, `InstancePlacementTool.cs`) are wave-sequenced and sole-owned.
- [x] Success criteria — objective and reproducible.

---

`/t1k:team cook plans/scatter-studio-implementation-plan.md`
