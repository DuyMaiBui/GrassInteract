# Brainstorm — Scatter Brush + Config Refactor

**Date:** 2026-06-04
**Scope:** Brush paint tool overhaul, realtime editor updates, consolidate `TerrainScatterConfig` + `ScatterLayer` + density textures into one sub-asset hierarchy, Odin Inspector adoption.

## Problem

- `TerrainScatterConfig`, `ScatterLayer`, density `Texture2D` are 3 separate top-level assets — fragile to version, share, or rename.
- Brush UI is IMGUI; only re-scatters on mouse-up; preview is a flat wire-disc that ignores opacity/falloff.
- Realtime updates rely on `delayCall + full Rebuild` and effectively need a domain reload for some changes to stick.
- No support for imported brush stamps (Photoshop-style soft/hard/noise brushes).

## Decisions (from clarifying questions)

| Question | Choice |
|---|---|
| Texture storage | Sub-asset of `TerrainScatterConfig` |
| Realtime triggers | In-brush live preview + texture swap + any layer field change + any config field change |
| Inline-layers path | Deprecate, force Config |
| Inspector layout | Odin tabbed groups per layer, brush preview reflects opacity/falloff |
| Brush import | Import brush stamp textures (Photoshop-style grayscale stamps) |
| Migration | Auto-migrate on first inspector open |

## Architecture

```
TerrainScatterConfig.asset (single file)
├── TerrainScatterConfig (main)
│   ├── GPU Resources, Wind Defaults
│   ├── Layers[]          → sub-asset ScatterLayer refs
│   └── BrushStamps[]     → sub-asset BrushStamp refs
├── ScatterLayer "Grass_Tall"          (sub-asset)
│   └── DensityMap Texture2D R8        (sub-asset, owned by layer)
├── ScatterLayer "Flowers"             (sub-asset)
│   └── DensityMap Texture2D R8        (sub-asset)
└── BrushStamp "Soft Round"            (sub-asset)
    BrushStamp "Hard Round"            (sub-asset)
```

`ScatterField` (MonoBehaviour) becomes thin: `[TerrainScatterConfig config, Terrain boundTerrain]` — no inline layers, no shared GPU refs.

## Key Changes

### 1. Sub-asset ownership
- `TerrainScatterConfig` exposes `CreateLayer / DeleteLayer / CreateBrushStamp` editor APIs using `AssetDatabase.AddObjectToAsset`.
- Density `Texture2D` is constructed with `R8 + !mips + readable + linear` at creation — no import-settings dialog ever.
- `ScatterLayer.[CreateAssetMenu]` removed; only the config can create layers.

### 2. ScatterField slim-down
```csharp
public class ScatterField : MonoBehaviour {
    [SerializeField, Required] TerrainScatterConfig config;
    [SerializeField] Terrain boundTerrain;
}
```
Inspector shows: Config ref, Terrain ref, ActiveTier readout, **[Open Config]** button.

### 3. Realtime updates — fast-path per-layer rebuild
| Change source | Trigger | Path |
|---|---|---|
| Brush drag stamp | `ThrottledFlush` (50 ms) | `field.RebuildLayer(idx)` each flush |
| Layer property edit | Odin `[OnValueChanged]` | `field.RebuildLayer(idx)` |
| Density texture swap | Odin `[OnValueChanged]` | `field.RebuildLayer(idx)` |
| Config field edit | Odin `[OnValueChanged]` | `field.Rebuild()` |
| Add/delete layer / tier change | Odin button / inspector | `field.Rebuild()` |
| External AssetPostprocessor on density tex | `OnPostprocessAllAssets` | `field.RebuildLayer(idx)` |

`RebuildLayer(int)` disposes + rebuilds only one engine; full `Rebuild()` reserved for structural changes. Replace `delayCall` chain in `OnValidate` with direct calls + re-entry guard.

### 4. Odin inspector layout (config asset)

```
TerrainScatterConfig
├── [TitleGroup "GPU Resources"]   CullCompute, IndirectMaterial
├── [TitleGroup "Wind Defaults"]   Direction, Strength, Frequency, NoiseScale, Bend, Flatten
├── [TabGroup "Layers"]
│   ├── Tab "Grass_Tall"  → [InlineProperty] ScatterLayer
│   │   ├── [BoxGroup "Density"]      DensityMap preview, TargetInstances
│   │   ├── [BoxGroup "Placement"]    FieldBounds, ScaleRange, SlopeRange, SplatLayer/Threshold
│   │   ├── [BoxGroup "Orientation"]  AlignToNormal, Pitch/Roll, Offset
│   │   ├── [BoxGroup "LOD/Render"]   RenderConfig, Lods[]
│   │   └── [BoxGroup "Brush"]        Tool (Paint/Erase/Off), Stamp dropdown, Radius/Opacity/Falloff sliders, [Save/Revert/Clear/Import PNG]
│   └── Tab "+"  add-layer
└── [TabGroup "Brushes"]
    ├── Tab per imported stamp (preview + replace)
    └── Tab "+ Import"  file picker → PNG/EXR sub-asset
```

### 5. Brush improvements
- `BrushStamp` SO holds grayscale `Texture2D shape` + displayName. Replaces procedural circular falloff when assigned.
- Scene-view preview = `Handles.DrawTexture` of the stamp tinted by `(paintColor, opacity)` on the hit plane (oriented by hit normal). Procedural mode generates radial-gradient texture once and reuses. WYSIWYG with opacity + falloff baked in.
- Brush is tied to the active **layer tab** — switching tabs auto-reloads the brush buffer for that layer.

### 6. Auto-migration
- Triggered when a `ScatterField` whose inline `layers` list is non-empty is selected.
- Dialog: "Migrate legacy assets into a new TerrainScatterConfig?"
- On accept: create `TerrainScatterConfig.asset` next to the scene; for each inline layer, instantiate a copy as sub-asset, copy the density texture pixels into a fresh sub-asset Texture2D, rewire the field's `config` ref, clear inline fields, mark scene dirty.
- Old loose assets stay on disk (manual cleanup), rollback = revert scene + delete new config asset.

## Files touched

| File | Change |
|---|---|
| `Runtime/TerrainScatterConfig.cs` | Sub-asset APIs, brush-stamp list, Odin attrs, `RebuildAllReferencing*` for fast-path |
| `Runtime/ScatterLayer.cs` | Drop `[CreateAssetMenu]`, add Odin attrs + `[OnValueChanged]`, `[InlineProperty]` block |
| `Runtime/ScatterField.cs` | Drop inline `layers`/`cullCompute`/`indirectMaterial`, add `RebuildLayer(int)`, slim inspector |
| `Editor/ScatterFieldEditor.cs` | Trimmed to Config-ref + Open-Config button (Odin `OdinEditor`) |
| `Editor/TerrainScatterConfigEditor.cs` | **NEW** — main Odin editor with tabbed layers + brush + stamps |
| `Editor/ScatterBrush.cs` | Stamp support, textured scene preview, per-layer rebuild calls |
| `Runtime/BrushStamp.cs` | **NEW** — SO {Texture2D shape, string displayName} |
| `Editor/ScatterAssetMigrator.cs` | **NEW** — auto-migration utility + menu item fallback |
| `Editor/ScatterAssetPostprocessor.cs` | **NEW** — watches sub-asset density tex reimports |

## Tradeoffs

| Pros | Costs |
|---|---|
| One `.asset` ships entire scatter setup | Sub-assets less git-diffable |
| Atomic rename/delete, no orphan textures | Migration must be bulletproof (one-shot) |
| Inspector is a single config-shaped doc | Hard dependency on Odin (already installed) |
| WYSIWYG brush preview | Stamp library can bloat asset |
| Per-layer fast rebuild = snappy drag | `RebuildLayer` must stay in sync with full `Rebuild` |

## Risks & Mitigations

1. **Migration data loss** — copy textures via `GetPixels/SetPixels` into fresh sub-asset; old assets untouched; explicit user confirmation; rollback steps logged.
2. **Sub-asset YAML churn** — stable file-IDs via `AssetDatabase.TryGetGUIDAndLocalFileIdentifier` after add.
3. **`OnValidate` ↔ Rebuild recursion** — replace `delayCall` chain with direct call + `bool rebuilding` re-entry guard.
4. **Odin `[InlineProperty]` on sub-asset refs** — verified pattern; works because refs are sub-assets in same file.

## Success Criteria

- Drag any density-map slider → grass updates within one editor frame, no domain reload.
- Paint stroke shows live density change DURING drag, not just on mouse-up.
- Brush preview disc shape matches actual stamp + falloff curve (no more flat wire-disc).
- One `.asset` file holds the entire scatter setup (config + N layers + N density textures + M brush stamps).
- `Tools/GrassInteract/Migrate Legacy ScatterField` converts the demo scene cleanly; demo runs identical.

## Next Step

Invoke `/t1k:plan` to phase this work:
- **Phase 1** sub-asset infra (config APIs, BrushStamp, ScatterLayer changes)
- **Phase 2** ScatterField slim-down + RebuildLayer fast-path
- **Phase 3** Odin TerrainScatterConfigEditor (tabs + InlineProperty)
- **Phase 4** Brush stamps + WYSIWYG preview + ScatterAssetPostprocessor
- **Phase 5** Auto-migration tool
