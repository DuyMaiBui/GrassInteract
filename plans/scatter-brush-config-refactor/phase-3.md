# Phase 3 — Odin `TerrainScatterConfigEditor` (Tabs + InlineProperty)

**Plan:** `plan.md` · **Brainstorm:** `plans/reports/brainstorm-scatter-brush-config-refactor-20260604.md`
**Effort:** L (1wk) · **Depends on:** Phase 2 (`RebuildLayer` exists; ScatterField slim; ScatterLayer Odin-grouped)

## Goal

Replace the IMGUI `ScatterFieldEditor` paint UI with an Odin-driven config-centric editor: tabbed layers, each tab showing the full `ScatterLayer` inline via `[InlineProperty]`, with the paint brush controls scoped to the active layer. `ScatterField`'s inspector becomes a thin "Open Config" launcher.

## Deliverables

1. `Editor/TerrainScatterConfigEditor.cs` — NEW `OdinEditor` for `TerrainScatterConfig`. TabGroup per layer; each tab inlines the layer + houses the per-layer brush. Separate `Brushes` tab for the stamp library (UI only; stamp import is Phase 4).
2. `Editor/ScatterFieldEditor.cs` — REPLACED with a minimal `OdinEditor` that draws default inspector + an "Open Config" button.
3. `Editor/ScatterBrush.cs` — refactored to be **layer-targeted** by callers instead of editor-state-owning. Add `SetActiveLayer(ScatterField, ScatterLayer, int idx)` so the brush knows which `RebuildLayer(idx)` to call.
4. Painting in the new UI rebuilds the **just-painted layer only** (fast path).
5. Demo renders byte-identical; manual paint test produces expected density changes within drag.

## File ownership

| Path | Owner | Action |
|---|---|---|
| `Assets/GrassInteract/Editor/TerrainScatterConfigEditor.cs` | NEW | Write |
| `Assets/GrassInteract/Editor/ScatterFieldEditor.cs` | REWRITE | Slim to OdinEditor + Open Config button |
| `Assets/GrassInteract/Editor/ScatterBrush.cs` | EDIT | Add layer-index awareness, switch flush callbacks |

**Out of scope:**
- Brush stamp file import dialog (Phase 4)
- Textured WYSIWYG scene preview — keep current wire-disc this phase; Phase 4 ships the textured preview
- AssetPostprocessor (Phase 4)
- Migration (Phase 5)

## Task breakdown

### T3.1 — Minimal `ScatterFieldEditor` rewrite (~1 hr)

```csharp
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    [CustomEditor(typeof(ScatterField), true), CanEditMultipleObjects]
    public sealed class ScatterFieldEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var field = this.target as ScatterField;
            if (field == null || field.Config == null) return;

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Open Config", GUILayout.Height(28)))
            {
                Selection.activeObject = field.Config;
                EditorGUIUtility.PingObject(field.Config);
            }
            EditorGUILayout.LabelField("Active Tier", field.ActiveTierName, EditorStyles.miniLabel);
        }
    }
}
```

Note: drops the current scene-view paint hook from ScatterField's editor. Painting now happens from the config editor. The scene-view paint logic moves to `TerrainScatterConfigEditor.OnSceneGUI` (T3.4).

### T3.2 — `TerrainScatterConfigEditor` scaffold + tabs (~4 hr)

```csharp
[CustomEditor(typeof(TerrainScatterConfig))]
public sealed class TerrainScatterConfigEditor : OdinEditor
{
    private ScatterField? activeField;
    private int activeLayerIdx;
    private readonly ScatterBrush brush = new();

    protected override void OnEnable()
    {
        base.OnEnable();
        this.activeField = FindActiveFieldForConfig((TerrainScatterConfig)this.target);
        this.activeLayerIdx = 0;
        this.LoadBrushForActiveLayer();
        SceneView.duringSceneGui += this.OnSceneGUI;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        this.brush.FlushBufferToTexture();
        SceneView.duringSceneGui -= this.OnSceneGUI;
    }

    public override void OnInspectorGUI()
    {
        // Odin draws the config (TitleGroups, TabGroup with Layers/Brushes) automatically.
        base.OnInspectorGUI();
    }
}
```

Drive layout from the config asset itself via Odin attributes (Phase 1 added basic ones; refine here):

In `TerrainScatterConfig`:
```csharp
[TabGroup("Main", "Layers"), ListDrawerSettings(/* ... */)]
[SerializeField] private List<ScatterLayer> layers = new();

[TabGroup("Main", "Brushes")]
[SerializeField] private List<BrushStamp> brushStamps = new();
```

But `[InlineProperty]` on the list items requires a custom approach — see T3.3.

### T3.3 — Inline layers in tabs via `[ShowInInspector]` virtual property (~5 hr)

Odin's `[TabGroup]` per dynamic list item works best via a synthesized layer-array surfaced through `[ShowInInspector]`:

In `TerrainScatterConfig`:
```csharp
#if UNITY_EDITOR
[ShowInInspector, HideLabel]
[TabGroup("LayerTabs", AnimateGroup = false, UseFixedHeight = false)]
[ListDrawerSettings(HideAddButton = true, HideRemoveButton = true)]
private LayerTab[] LayerTabs => this.BuildLayerTabs();

[System.Serializable]
private struct LayerTab
{
    [InlineProperty, HideLabel] public ScatterLayer Layer;
}

private LayerTab[] BuildLayerTabs()
{
    var arr = new LayerTab[this.layers.Count];
    for (int i = 0; i < this.layers.Count; ++i) arr[i] = new LayerTab { Layer = this.layers[i] };
    return arr;
}
#endif
```

Risk: `[TabGroup]` on an array surfaces ONE tab per element only when the items have a `[TabGroup]` that names per-instance — Odin doesn't auto-tab arrays. Two viable fallbacks if this doesn't work as designed:

**Fallback A — Hand-rolled per-tab block (preferred):**
Override `OnInspectorGUI` partially. Draw TitleGroups first via `base`, then manually emit:
```csharp
string[] tabNames = config.Layers.Select(l => l?.name ?? "(null)").ToArray();
this.activeLayerIdx = GUILayout.Toolbar(this.activeLayerIdx, tabNames);
ScatterLayer active = config.Layers[this.activeLayerIdx];
if (active != null)
{
    // Odin draws the inlined layer:
    Sirenix.OdinInspector.Editor.PropertyTree.Create(active).Draw();
}
```

This is what we'll ship. The synthesized `LayerTab[]` approach is documented as nice-to-have but `PropertyTree.Draw()` is the proven path.

**Fallback B — Two-pane Odin ListDrawer:** if tab UI feels cramped, switch to `[HorizontalGroup]` with layer-list on left + property tree on right. Decide via T3.7 visual test.

### T3.4 — Per-layer brush block + scene-view painting (~4 hr)

Inside the active layer's drawn area, append the brush controls (radius, opacity, falloff, tool toggle, save/revert/clear/import — import is Phase 4):

```csharp
EditorGUILayout.Space(6);
EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
this.brushRadius   = EditorGUILayout.Slider("Brush Size (m)", this.brushRadius, 0.25f, 50f);
this.brushStrength = EditorGUILayout.Slider("Opacity",        this.brushStrength, 0f, 1f);
this.brushFalloff  = EditorGUILayout.Slider("Falloff",        this.brushFalloff,  0f, 1f);
// Tool toggle, save/revert/clear buttons (lifted from old ScatterFieldEditor)
```

Move `OnSceneGUI` painting logic from old `ScatterFieldEditor` into `TerrainScatterConfigEditor`:
- Resolve `activeField` (first enabled `ScatterField` referencing this config).
- Resolve `activeLayer = config.Layers[activeLayerIdx]`.
- Cast ray, paint, flush — same as before.
- **Change:** every flush (throttled) calls `activeField.RebuildLayer(activeLayerIdx)`, NOT `field.Rebuild()`. Mouse-up still calls full `Rebuild` once for safety (cheap, ~one frame).

### T3.5 — Selecting a tab switches brush buffer (~1 hr)

When `activeLayerIdx` changes:
```csharp
private void SetActiveLayerIdx(int newIdx)
{
    if (newIdx == this.activeLayerIdx) return;
    this.brush.FlushBufferToTexture();    // commit anything pending
    this.activeLayerIdx = newIdx;
    this.LoadBrushForActiveLayer();
}
```

### T3.6 — Brushes tab (placeholder UI) (~1 hr)

For Phase 3, the Brushes tab just lists existing `BrushStamp[]` from the config and shows each stamp's `shape` via `[PreviewField]`. Adding/removing stamps comes in Phase 4 via the file-import dialog.

```csharp
[TabGroup("Main", "Brushes")]
[ListDrawerSettings(Expanded = true)]
[SerializeField] private List<BrushStamp> brushStamps = new();
```

(Odin auto-renders each `BrushStamp` element with its `[Required] Texture2D shape`.)

### T3.7 — Visual smoke (~30 min)

- Open `TerrainScatterConfig` in inspector — verify tabs render (Layers / Brushes).
- Click a layer tab — its full properties (Density, Placement, Orientation, LOD/Render, Mesh, Colliders) render inline.
- Brush controls appear under each layer's inline view.
- Paint into scene → field re-scatters DURING drag (every ~50 ms flush), not just mouse-up.
- Switch layer tab → brush buffer reloads, painting now targets the new layer.

## Success criteria

- ✅ Compile clean.
- ✅ Config inspector renders tabs (Layers, Brushes) with no Odin errors in console.
- ✅ Clicking a layer tab inlines the full ScatterLayer; no separate-asset double-headers.
- ✅ Painting a stroke updates the visible density at 20 fps (50 ms throttle).
- ✅ Demo scene renders byte-identical to Phase-2 baseline.
- ✅ `ScatterField` inspector is minimal: Config ref, Terrain ref, ForceTier, ExtraCullMargin, Prewarm, ActiveTier readout, [Open Config] button.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Odin `[InlineProperty]` on sub-asset refs renders the asset header anyway | 3 | 4 | 12 | Fallback A: hand-rolled `GUILayout.Toolbar` + `PropertyTree.Create(layer).Draw()` — proven path |
| `SceneView.duringSceneGui` event leak when editor disabled mid-paint | 2 | 3 | 6 | `OnDisable` unsubscribes; `OnEnable` ensures single-subscribe by always `-= +=` |
| Painting during drag triggers `RebuildLayer` storm if 50 ms throttle isn't applied at the brush-flush level | 3 | 3 | 9 | `ScatterBrush.ThrottledFlush` already enforces 50 ms; verify `RebuildLayer` is INSIDE that path, not at every Stamp |
| `Sirenix.OdinInspector.Editor.PropertyTree` is editor-only — accidentally referencing from runtime asmdef breaks build | 2 | 4 | 8 | All Phase-3 code lives in `Editor/`; verify GrassInteract.Editor.asmdef references Sirenix.OdinInspector.Editor |
| Two enabled ScatterFields referencing same config → which one does the brush rebuild? | 2 | 2 | 4 | Brush picks the FIRST active field referencing the config; existing `WarnIfMultipleEnabledFields` already warns. Document in HANDOFF |
| Old `ScatterFieldEditor`'s `OnInspectorGUI` painting features have non-obvious behaviors (multi-edit guard, dropdown-collapsed-after-fix-import-settings, etc.) lost in rewrite | 3 | 3 | 9 | Phase-3 review: line-by-line diff old editor against new editor checklist; deliver a `phase-3-rewrite-checklist.md` adjunct doc |

## Verification commands (Unity MCP)

```
mcp__UnityMCP__set_active_instance(unity_instance="GrassInteract@<hash>")
mcp__UnityMCP__refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
mcp__UnityMCP__manage_scene(action="load", path="Assets/GrassInteract/Demo/GrassInteractDemo.unity")
# Manually: select TerrainScatterConfig asset → screenshot inspector tabs
mcp__UnityMCP__read_console(types=["Error", "Warning"], count=50)
mcp__UnityMCP__rendering_stats()
```
