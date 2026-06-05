# Phase 1 — Sub-asset Infra + BrushStamp + ScatterLayer Odin Attrs

**Plan:** `plan.md` · **Brainstorm:** `plans/reports/brainstorm-scatter-brush-config-refactor-20260604.md`
**Effort:** M (3d) · **Depends on:** —

## Goal

Lay the data foundation: `TerrainScatterConfig` becomes the sub-asset host, `BrushStamp` SO is new, `ScatterLayer` learns Odin attributes and loses its `[CreateAssetMenu]`. No editor UX changes yet — Phase 3 wires that up. Demo must still render byte-stable using its **existing** loose assets (we don't run migration here).

## Deliverables

1. `Runtime/BrushStamp.cs` — NEW SO with `string displayName, Texture2D shape, [TextureSize/SaturatedColor previews]`.
2. `Runtime/TerrainScatterConfig.cs` — add sub-asset CRUD APIs (`CreateLayer`, `DeleteLayer`, `CreateBrushStamp`, `DeleteBrushStamp`); add `[SerializeField] List<BrushStamp> brushStamps`; add Odin grouping attributes.
3. `Runtime/ScatterLayer.cs` — remove `[CreateAssetMenu]`, regroup fields with Odin `[BoxGroup]` / `[TitleGroup]` to match the future tab layout; add `[OnValueChanged]` callbacks that will be wired in Phase 2 (stub method `internal void NotifyChanged()`).
4. Compiles clean. Demo opens with no console errors, renders identical to baseline.

## File ownership

| Path | Owner | Action |
|---|---|---|
| `Assets/GrassInteract/Runtime/BrushStamp.cs` | NEW | Write |
| `Assets/GrassInteract/Runtime/BrushStamp.cs.meta` | Unity | auto |
| `Assets/GrassInteract/Runtime/TerrainScatterConfig.cs` | EDIT | Add sub-asset APIs + brush stamps list + Odin attrs |
| `Assets/GrassInteract/Runtime/ScatterLayer.cs` | EDIT | Drop CreateAssetMenu, add Odin groups, add NotifyChanged stub |

**Out of scope for this phase:**
- `ScatterField.cs` (Phase 2)
- Any `Editor/` folder file (Phase 3+)
- AssetPostprocessor (Phase 4)
- Migration utility (Phase 5)
- Wiring up `RebuildLayer` (Phase 2 owns the field-side API)

## Task breakdown

### T1.1 — Add `BrushStamp` ScriptableObject (~30 min)

```csharp
namespace GrassInteract
{
    public sealed class BrushStamp : ScriptableObject
    {
        [SerializeField] private string displayName = "Stamp";
        [SerializeField, Required] private Texture2D? shape;

        public string DisplayName => string.IsNullOrEmpty(this.displayName) ? this.name : this.displayName;
        public Texture2D? Shape => this.shape;

#if UNITY_EDITOR
        internal void SetShape(Texture2D tex, string name)
        {
            this.shape = tex;
            this.displayName = name;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
```

Notes:
- No `[CreateAssetMenu]` — only created through `TerrainScatterConfig.CreateBrushStamp`.
- Odin's `[PreviewField]` will be added in Phase 3 alongside the editor (keeps Runtime asmdef Odin-clean? — verify Odin attribute assemblies; Odin attrs are in `Sirenix.OdinInspector.Attributes` and are runtime-safe).

### T1.2 — Extend `TerrainScatterConfig` (~2 hr)

Add private serialized list:

```csharp
[SerializeField] private List<BrushStamp> brushStamps = new();
public IReadOnlyList<BrushStamp> BrushStamps => this.brushStamps;
```

Add editor-only sub-asset CRUD inside `#if UNITY_EDITOR`:

```csharp
internal ScatterLayer CreateLayer(string name)
{
    var layer = ScriptableObject.CreateInstance<ScatterLayer>();
    layer.name = name;
    layer.hideFlags = HideFlags.None;                 // gotcha: not DontSave
    UnityEditor.AssetDatabase.AddObjectToAsset(layer, this);
    layer.hideFlags = HideFlags.HideInHierarchy;      // hide from project tree

    // Auto-create + own a fresh R8 density map sub-asset.
    var tex = new Texture2D(512, 512,
        UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
        UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
    tex.name = $"{name}_DensityMap";
    tex.wrapMode = TextureWrapMode.Clamp;
    tex.filterMode = FilterMode.Bilinear;
    Color32[] clear = new Color32[512 * 512];         // zero alpha = empty
    tex.SetPixels32(clear);
    tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    tex.hideFlags = HideFlags.HideInHierarchy;
    UnityEditor.AssetDatabase.AddObjectToAsset(tex, this);

    // Wire the texture into the layer via SerializedObject (densityMap is private).
    using var so = new UnityEditor.SerializedObject(layer);
    so.FindProperty("densityMap").objectReferenceValue = tex;
    so.ApplyModifiedPropertiesWithoutUndo();

    this.layers.Add(layer);
    UnityEditor.EditorUtility.SetDirty(this);
    UnityEditor.AssetDatabase.SaveAssets();
    return layer;
}

internal void DeleteLayer(ScatterLayer layer)
{
    if (layer == null || !this.layers.Contains(layer)) return;
    this.layers.Remove(layer);

    // Density texture: only delete if it's a sub-asset of THIS config.
    using var so = new UnityEditor.SerializedObject(layer);
    var tex = so.FindProperty("densityMap").objectReferenceValue as Texture2D;
    if (tex != null && UnityEditor.AssetDatabase.GetAssetPath(tex)
            == UnityEditor.AssetDatabase.GetAssetPath(this))
    {
        UnityEditor.AssetDatabase.RemoveObjectFromAsset(tex);
        UnityEngine.Object.DestroyImmediate(tex, allowDestroyingAssets: true);
    }

    UnityEditor.AssetDatabase.RemoveObjectFromAsset(layer);
    UnityEngine.Object.DestroyImmediate(layer, allowDestroyingAssets: true);
    UnityEditor.EditorUtility.SetDirty(this);
    UnityEditor.AssetDatabase.SaveAssets();
}

internal BrushStamp CreateBrushStamp(string name, Texture2D shape) { /* analogous */ }
internal void DeleteBrushStamp(BrushStamp stamp) { /* analogous */ }
```

Gotcha checks (must validate manually before T1.3):
- `AssetDatabase.AddObjectToAsset` requires the parent asset already saved (one-time). When called from `[Tools/.../Migrate]`, ensure config has been `CreateAsset`'d first.
- Removing the texture sub-asset uses `RemoveObjectFromAsset` then `DestroyImmediate` — NOT `DeleteAsset` (silent no-op on sub-assets).

Add Odin grouping (visible only after Phase 3 wires the editor, but attrs are harmless now):

```csharp
[TitleGroup("GPU Resources")]
[SerializeField] private ComputeShader? cullCompute;
[TitleGroup("GPU Resources")]
[SerializeField] private Material? indirectMaterial;

[TitleGroup("Wind Defaults")]
// ... existing wind fields with [TitleGroup("Wind Defaults")]

[TabGroup("layout", "Layers")]
[SerializeField] private List<ScatterLayer> layers = new();

[TabGroup("layout", "Brushes")]
[SerializeField] private List<BrushStamp> brushStamps = new();
```

(Tab labels are placeholders; Phase 3 finalizes the structure.)

### T1.3 — Strip ScatterLayer `[CreateAssetMenu]` + add Odin groups (~1 hr)

- Delete the `[CreateAssetMenu(...)]` line above the class.
- Group the existing serialized fields with `[BoxGroup]` annotations matching the planned tab layout (`Density`, `Placement`, `Orientation`, `LOD/Render`, `Mesh`, `Colliders`).
- Add `internal void NotifyChanged() { #if UNITY_EDITOR ScatterField.RebuildAllReferencingLayer(this); #endif }` — Phase 2 will replace the body with `RebuildLayer(idx)` once it exists.

Side note: the existing `OnValidate` + `delayCall` chain on `ScatterLayer` STAYS for this phase (Phase 2 replaces it once `RebuildLayer` lands). No functional regression risk.

### T1.4 — Smoke test (~30 min)

1. Open `GrassInteractDemo` scene. Console clean? Demo renders identical to git-baseline.
2. Open the demo scene's `TerrainScatterConfig.asset` — verify Odin shows the title groups (no error spam, no missing-namespace).
3. Right-click `Assets/GrassInteract` in Project — confirm "Scatter Layer" menu item is GONE (was under `Assets > Create > GrassInteract > Scatter Layer`).
4. Try to call `CreateLayer("Test")` from a one-off `[MenuItem]` quick test (delete after) — verify the new sub-asset appears nested inside the config.

## Success criteria

- ✅ Compile clean (no errors, no new warnings).
- ✅ `GrassInteractDemo` renders byte-identical to baseline (Unity MCP screenshot vs main branch).
- ✅ `Console clean` after demo scene load.
- ✅ `Assets > Create > GrassInteract > Scatter Layer` menu entry is GONE.
- ✅ Manual `CreateLayer` from a test menu produces a sub-asset under the config (visible in Project view when expanded).

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Odin Runtime asmdef can't see attribute assemblies | 2 | 4 | 8 | Verify `GrassInteract.Runtime.asmdef` references `Sirenix.OdinInspector.Attributes` (runtime-safe); Odin attrs live in a non-editor assembly |
| `AddObjectToAsset` before parent `CreateAsset` throws | 2 | 3 | 6 | Document in CreateLayer comment: caller must save parent first. T1.4 covers the manual test |
| Existing demo config breaks (`brushStamps` is a new serialized list) | 1 | 3 | 3 | New empty list serializes as empty array in YAML; no migration needed for existing configs |
| Hiding sub-assets via HideFlags hides them too aggressively | 2 | 2 | 4 | Use `HideInHierarchy` only (visible in inspector slot picker); avoid `DontSave` (would block serialization) |

## Verification commands (Unity MCP)

```
mcp__UnityMCP__set_active_instance(unity_instance="GrassInteract@<hash>")
mcp__UnityMCP__refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)
mcp__UnityMCP__read_console(types=["Error", "Warning"], count=50)
mcp__UnityMCP__manage_scene(action="load", path="Assets/GrassInteract/Demo/GrassInteractDemo.unity")
mcp__UnityMCP__rendering_stats()   # capture batch count for baseline
```
