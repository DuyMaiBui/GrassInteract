# GrassInteract → Scatter API Migration

The grass-specific types were generalized into a genre-neutral **Scatter** system that supports
multiple paintable layers (grass + mesh props) and Unity-Terrain binding. The old grass-specific
types provided `[Obsolete]` shims for one release cycle; **those shims have now been removed**. The
canonical types are `ScatterField`, `ScatterLayer`, and the in-Inspector `ScatterFieldEditor` paint
tools.

## What changed

| Removed | Replacement | Notes |
|---|---|---|
| `GrassLayer` (ScriptableObject) | `ScatterLayer` | `ScatterLayer` is the concrete base layer type — **SSOT** for every per-layer setting (density map, placement, LOD meshes, material, shadow mode, wind, bend, blade-height bounds, GPU chunk size). Created as a sub-asset of `TerrainScatterConfig`. |
| `GrassLODConfig` (ScriptableObject) | _(folded into `ScatterLayer`)_ | All render/wind/bend/bounds tunables now live directly on `ScatterLayer` per SSOT. The standalone render-config asset is gone. |
| `GrassInteractField` (MonoBehaviour) | `ScatterField` | `ScatterField` is the `[ExecuteAlways]` field orchestrator. It owns a multi-layer `layers` list and builds the scatter + simulator + renderer. |
| `GrassPainterWindow` (Editor window) | `ScatterFieldEditor` paint tools | Painting is now an in-Inspector "Paint" section on `ScatterField` (no separate window). |
| `GrassInteractField.GrassTierMode` | `ScatterField.GrassTierMode` | Enum lives on the field component. |

## Current state

- Only `ScatterField` and `ScatterLayer` ship as concrete types. There is no longer an obsolete
  subclass of either.
- `ScatterField.Rebuild()` calls the virtual `SeedLayers()` hook (base implementation is a no-op) so
  subclasses *could* inject layers before the `layers` list is iterated — but no built-in subclass uses
  it today.
- Density is painted per-layer via the `ScatterFieldEditor` "Paint" section directly in the Inspector.

## Recommended setup (new work)

1. Create `ScatterLayer` assets: `Assets → Create → GrassInteract → Scatter Layer`. Set `kind` =
   `Grass` (blades) or `Mesh` (static instanced props).
2. Add a **`ScatterField`** component to your field GameObject.
3. Populate its **`layers`** list (ordered: e.g. grass, flowers, rocks). Each layer has its own density
   map painted via the `ScatterFieldEditor` Paint section's layer dropdown.
4. (Optional) Assign **`boundTerrain`** to a Unity Terrain to sample height/holes/slope from
   `TerrainData` and center the field on the terrain. Set the layer's `fieldBounds` to the terrain size
   for full coverage.
