---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Perspective Playbooks — Per-Camera Placeholder Recipes

4 playbooks for the 4 perspectives DOTS-AI uses. Pick the one matching your demo's camera setup.

---

## 1. 3D third-person (BattleDemo)

**Camera:** Perspective, slight downward angle (~35°), positioned 8–12 units away from action.

**Primitives:**
- Melee → `Capsule` (vertical, 1×2×1)
- Ranger → `Cylinder` (vertical, 0.8×2×0.8)
- Mage → `Sphere` on top of a thin capsule body (mesh-child pivot pattern)
- Boss → `Capsule` (1.6×3×1.6) with crown spike children

**Material:** `Universal Render Pipeline/Lit`, `smoothness = 0.6`, per-unit colors.

**Mesh-child pivot pattern:** root GameObject (empty pivot at feet) + child mesh offset upward by half-height. Lets the unit "stand on" the ground at y=0 instead of clipping through it. See `BattleDemoUnitPrefabCreator.cs`.

**Impostor LOD:** wire `LODGroup` with LOD0 (primitive mesh) + LOD1 (Amplify Impostors billboard) — falls back to LOD0 if impostor asset missing.

**Health bar:** world-space Canvas above unit head, offset = mesh height + 0.3 unit gap. Looks at camera each frame.

**Quirks:**
- Linux/Vulkan: Set LODGroup transition to 1% (not the default 15%) — distance LOD goes invisible on Linux Vulkan.

---

## 2. 2D top-down ortho (BattleDemo2D)

**Camera:** Orthographic, looking straight DOWN -Y. Ortho size sized to fit arena (e.g. 12 for a 24×24 arena).

**Primitives:**
- Unit → `Quad` on the XZ-plane (rotate `(90, 0, 0)` so it lies flat).
- Projectile → small `Quad` XZ-plane.
- Structure → `Cube` (for solid walls / pillars) or `Quad` XZ.

**Material:** `Universal Render Pipeline/Unlit` with **alpha-cutout** (`_Cutoff = 0.5`, RenderType = TransparentCutout). Or pure solid color for simplest case.

**Sprite (placeholder):** use procedural PNG color swatches as `_BaseMap`. For pure-color placeholder, just set `_BaseColor`.

**Foreshortening trick:** for "perspective" feel without 3D, render a faint shadow `Quad` slightly below each unit's Y position.

**Quirks:**
- Quad's pivot is at its center — for top-down, that's correct (unit "stands" centered on the cell).
- Don't enable Grounding on top-down: characters don't fall.

---

## 3. 2D side-view ortho (RushTank, BattleDemoSideView)

**Camera:** Orthographic, looking down -Z (default Quad facing). Ortho size 6, offset (3, 1.5, -10).

**Primitives:**
- Unit / Tank → `Quad` on XY-plane (default orientation).
- Multi-part silhouettes: body + tracks + turret + barrel (see RushTank `RushTankSceneSetup.SubScene.TankVisual.cs`).
- Projectile → small `Quad`.
- Environment → wide `Quad` strips (ground/horizon/mountains).

**Material:** `Universal Render Pipeline/Unlit`, optionally with alpha-cutout for sprite illusion.

**Vertical layering via Z:** sky (z=+3), mountains (z=+2), ground (z=+0.5), tank (z=0), projectiles (z=-0.5). Orthographic camera at z=-10 looking +Z sees back-to-front correctly.

**Scrolling:** tank stays at origin, world is reset around it. OR camera follows tank rightward. Pick one — RushTank uses "tank moves, world stays" for simplicity.

**Quirks:**
- Quad's pivot at center — Y=0 means feet at -0.5 (half-height below). Use `Env_Ground` quad at y=-2 to anchor visually.
- Side-view requires `SideViewTransformSystem` if using DOTS to Y-mirror world-Z positions to visual-Y.

---

## 4. Isometric billboard (BattleDemoIso)

**Camera:** Perspective with `30°` tilt (rotation `(30, -45, 0)`), positioned to look diagonally down at the arena.

**Primitives:**
- Unit → `Quad` on XY-plane + **billboard shader** that auto-rotates to face the camera.
- Projectiles use arc height (set `ArcHeight = 2f`) for visual lob.

**Material:** Billboard shader (`Universal Render Pipeline/Unlit` with `_Billboard = 1` custom property) + alpha-cutout.

**Billboard shader gotcha:** every renderer needs its own MaterialPropertyBlock or per-instance material to set the billboard center — without it all quads bill-board to the same point and look wrong.

**Quirks:**
- Lighter than pure 3D (no expensive Lit shader, no shadows).
- Linux/Vulkan: `BatchRendererGroup` + billboard shader needs explicit `instancing_options` per material.

---

## Decision tree — picking a perspective

```
Is gameplay grid-based (chess / inventory / city builder)?
  YES → 2D top-down ortho
Is gameplay a scrolling shooter / runner / brawler?
  YES → 2D side-view ortho
Is gameplay tactical / strategy / RTS with terrain?
  YES → Isometric billboard
Is gameplay a 3D action / RPG / adventure?
  YES → 3D third-person
```

If undecided, default to **2D side-view ortho** — simplest tooling, smallest surface area, fastest to iterate.

---

## Universal across all 4 perspectives

- `Universal Render Pipeline/Unlit` (2D) or `Universal Render Pipeline/Lit` (3D)
- `GetOrCreateMaterial(name, color)` saved to `Assets/Demos/{Demo}/Materials/{Category}_{Subject}.mat`
- `Tools/{Demo}/Create All Prefabs` + `Tools/{Demo}/Setup Scene` menu items
- Idempotent destroy-by-name scene setup
- Authoring components attached at prefab-creation time (not at scene-setup time)
- Partial-class file split, every file under 200 lines

## Shader-by-Perspective Enforcement

Using the wrong shader family is a **P0-A1 violation** — lit lighting on 2D quads creates inconsistent tonal rendering depending on light direction and can make units invisible in scenes without dynamic lights.

| Perspective | Required shader | Why |
|---|---|---|
| 3D (BattleDemo, RogueliteDemo, etc.) | `URP/Lit` | Benefits from normal maps and dynamic lighting |
| 2D top-down (BattleDemo2D, puzzle demos) | `URP/Unlit` cutout | No meaningful normals on XZ quads; lit adds noise |
| 2D side-view (RushTankDemo, BattleDemoSideView) | `URP/Unlit` | Same — XY quads get random shading from scene lights |
| Isometric (BattleDemoIso) | Billboard shader (custom) | Must rotate to camera; standard lit/unlit doesn't billboard |
| Puzzle tile/grid demos | `URP/Unlit` | Flat board; lit adds distracting directional variance |

Gate: if a 2D or puzzle demo uses `URP/Lit` materials (non-billboard), it fails P0-A1 at the Beta review. Audit (260520-R4) found this violation in 3 puzzle demos using Lit materials for grid tiles. Source: review-260520-round4-design-placeholder.md §D4
