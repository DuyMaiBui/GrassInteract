# GrassInteract UIToolkit Editor — End-to-End Smoke Checklist

Version: 2026-06-05 (Phase I)
Run this checklist against a clean project after each significant change to the editor or runtime.

---

## Prerequisites

- [ ] Unity Editor open with the GrassInteract project.
- [ ] No compilation errors in the Console.
- [ ] A scene is open with a `Terrain` or at least some ground colliders.

---

## 1. Create a fresh config

- [ ] In the Project window, right-click an empty folder and select `Create > GrassInteract > Terrain Scatter Config`.
- [ ] Name it `Smoke_Config`. Confirm it appears as a `.asset` file.
- [ ] Select `Smoke_Config`. Confirm the TerrainScatterConfig inspector renders:
  - Header label showing asset name.
  - Layer count "0 layers".
  - `[ + Density Layer ]` and `[ + Instance Layer ]` buttons.
  - Empty-state placeholder visible in the tile grid area.

---

## 2. Add a Density layer

- [ ] Click `[ + Density Layer ]`.
- [ ] Confirm a new tile appears in the tile grid with a grass icon and the name `Layer_Density_0`.
- [ ] Click the tile to select it.
- [ ] In the layer inspector below the grid, confirm:
  - DensityTextureField shows a white 512×512 preview.
  - ValidationBadge on the texture field is green (OK).
  - Material field shows `Default_Material`.
  - ValidationBadge on the material row is green.
  - LOD section shows at least one LOD card with a mesh assigned.
- [ ] Expand the Project window and confirm sub-assets appear under `Smoke_Config`:
  - `Layer_Density_0` (ScriptableObject).
  - `Density_Layer_Density_0` (Texture2D).

---

## 3. Paint the density map

- [ ] With the Density layer selected, click `Paint…` on the DensityTextureField.
- [ ] Confirm the `DensityPaintWindow` opens.
- [ ] Paint a few strokes on the density canvas.
- [ ] Click `Save` (or close the window — confirm auto-save on close).
- [ ] Re-select the Density layer. Confirm the 64×64 preview reflects the painted strokes.

---

## 4. Add an Instance layer

- [ ] Click `[ + Instance Layer ]`.
- [ ] Confirm a second tile appears with a mesh-prop icon and the name `Layer_Instance_0`.
- [ ] Click the tile. Confirm the instance layer inspector shows:
  - "Instance (authored records)" kind label.
  - Authored Instances section with 0 records and a `[+ Add]` button.
  - Drop zone strip visible.
  - Material field showing `Default_Material` with green badge.
  - Sidecar badge green (or absent if no host slot in UXML).
  - LOD section with default mesh assigned.
- [ ] Confirm sub-assets in the Project window:
  - `Layer_Instance_0` (ScriptableObject).
  - `Authored_Layer_Instance_0` (AuthoredInstancesData).

---

## 5. Drag a prefab onto the Instance layer

- [ ] Create a simple cube prefab (or use any existing prefab).
- [ ] With the Instance layer inspector visible, drag the prefab from the Project window onto the drop-zone strip.
- [ ] Confirm records appear in the record list (one per transform in the prefab hierarchy).
- [ ] Confirm the count label updates ("Authored Instances (N)").

---

## 6. Use Place mode in the scene view

- [ ] In the scene view, look for the "Scatter Placement" overlay panel. If hidden, enable it via the overlay hamburger menu.
- [ ] Click `Place` in the Mode segmented bar.
- [ ] Click on the terrain surface five times. Confirm:
  - A green ghost disc appears at the cursor.
  - Each click adds one record to the list (count increments).
- [ ] Press `Escape` to return to `Select` mode.

---

## 7. Test validation badges

- [ ] Select the Density layer. Clear the `Density Map` field (set to None).
- [ ] Confirm the ValidationBadge turns red and hovering over it shows the error message.
- [ ] Confirm an "Assign default texture" button appears in the popover.
- [ ] Click "Assign default texture". Confirm the badge returns to green.

- [ ] Clear the `Material` field on any layer. Confirm the material badge turns red and "Assign Default_Material" appears.
- [ ] Click the auto-fix. Confirm material is restored.

---

## 8. Switch editor theme

- [ ] Go to `Edit > Preferences > General > Editor Theme` and switch to **Personal** (light).
- [ ] Re-select `Smoke_Config`. Confirm:
  - All text is legible (dark text on light background).
  - Tile borders, badges, and buttons render correctly.
  - No white-on-white or dark-on-dark contrast issues.
- [ ] Switch back to **Pro** (dark) and repeat the legibility check.

---

## 9. Play mode verification

- [ ] Add a `ScatterField` MonoBehaviour to a GameObject in the scene. Assign `Smoke_Config` to its config field.
- [ ] Enter Play mode.
- [ ] Confirm no exceptions in the Console.
- [ ] Confirm the Density layer's procedural grass renders on the terrain surface.
- [ ] Confirm the Instance layer's records render the assigned mesh at the authored positions.
- [ ] Exit Play mode. Confirm no exceptions on exit.

---

## 10. Persistence

- [ ] Save the scene (`Ctrl+S`).
- [ ] Close and reopen Unity.
- [ ] Select `Smoke_Config`. Confirm layer count, record count, and painted texture are all preserved.

---

## Sign-off

All items checked: smoke PASSED. Date: __________ Tester: __________
