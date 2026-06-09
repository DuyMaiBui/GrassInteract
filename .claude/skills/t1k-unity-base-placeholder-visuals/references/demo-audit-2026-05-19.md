---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Demo Audit — 2026-05-19

Source-of-truth audit of 7 RPG demos in `Assets/Demos/RPG/`. Captures consensus + divergence patterns to ground the placeholder-visuals skill in real code, not theory.

Auditor: Explore agent (background, very-thorough breadth). Date: 2026-05-19.

---

## Demos audited

| # | Demo | Perspective | Primitives | UI source |
|---|---|---|---|---|
| 1 | RushTank | 2D side-view ortho | Quad XY (multi-part) | Code (3-canvas split, partial files) |
| 2 | BackpackCrawler | Mobile portrait, mixed | Cube (grid 0.3) | Pre-built scene + procedural panels |
| 3 | InventoryDemo | Drag-drop grid + 3D items | Cube (grid-scaled) | Pre-built scene |
| 4 | BattleDemo | 3D battlefield | Capsule/Cylinder/Sphere + impostor LOD | Code (world-space HP bar) |
| 5 | BattleDemo2D | 2D top-down ortho | Quad XZ + alpha cutout | Code (heavy authoring stack) |
| 6 | BattleDemoIso | Isometric billboard | Quad + billboard shader | Code |
| 7 | BattleDemoSideView | 2D side-view ortho | Quad XY + alpha cutout | Code |

---

## Consensus patterns (3+ demos)

1. **Primitives only.** Every demo uses Unity primitives. No Synty, no 3D model imports. Pure procedural.
2. **URP shaders.** `Universal Render Pipeline/Unlit` for 2D/side/iso. `Universal Render Pipeline/Lit` for 3D. No custom shaders.
3. **`GetOrCreateMaterial(name, color)` saved to `.mat`.** All demos share this pattern.
4. **Tool menu pair.** Every demo has `Tools/{DemoName}/Create All Prefabs` + `Tools/{DemoName}/Setup Scene`.
5. **Idempotent destroy-by-name scene setup.** RushTank + BackpackCrawler explicit. Others assume fresh scene.
6. **Partial-class organization.** RushTank (12 partial files), BackpackCrawler (multiple setup files), BattleDemo2D (4 unit creator files).
7. **Color-as-identity.** Rarity tints (Common=grey, Rare=blue, Epic=purple, Legendary=gold), team tints (red/blue), archetype tints.
8. **Authoring at prefab-creation time.** Every demo attaches `XxxAuthoring` MonoBehaviour during prefab construction so bakers see correct SerializeField values.

## Divergence (where demos disagree)

| Topic | Approaches | Recommendation |
|---|---|---|
| **Mesh orientation** | Quad XY (-Z), Quad XZ (top), Cube grid, Capsule pivot-child | Pick based on perspective (see playbooks). |
| **Scene-setup cohesion** | RushTank: prefabs FIRST, then wire scene. Others: assume prefabs exist. | RushTank pattern is safest. |
| **Texture generation** | BackpackCrawler ALONE generates procedural pixel-art textures (4 PNG layers). Others: solid-color swatches. | Optional — only worth the effort for mobile games where backdrop matters. |
| **Material storage** | Flat `Materials/` vs per-unit `{Name}Mat.mat` | Flat folder with category prefix (`Item_*`, `Enemy_*`). |
| **UI canvas creation** | RushTank: full 3-canvas hierarchy in code. BackpackCrawler: pre-built scene + augmented. | Code-first wins — survives scene-asset deletion + git merge conflicts. |
| **Authoring complexity** | Min: BattleDemoSideView (stats + combat). Max: BattleDemo2D (boss phases + inventory + loot + progression + summons + stealth). | Whatever the gameplay needs. Don't over-engineer. |

## Notable innovations to borrow

1. **RushTank multi-part archetype builder** — `ApplyArchetypeBodyShape` + `AddArchetypeParts` per archetype gives each enemy a distinct silhouette without art assets. Pattern: body scale + N child quads at known offsets per archetype.

2. **BackpackCrawler procedural pixel-art landscape** — `Tools/BackpackCrawler/Generate Corridor Textures` builds 4 PNG layers (256×128) with:
   - Sky (X-tiled gradient)
   - CorridorScene (perspective path with quadratic narrowing for depth)
   - NearTreeEdges (vertical strip wraps for edge occlusion)
   - GroundPath (X-tiled dirt path)
   Y-wrap tiling for vertical scroll. Worth it for mobile games where backdrop is on-screen 95% of the time.

3. **BattleDemo Impostor LOD fallback** — `LODGroup` with LOD0 (primitive) + LOD1 (Amplify Impostors) — falls back gracefully when impostor asset missing. Linux/Vulkan: LOD0 transition at 1%.

4. **BattleDemoIso billboard shader** — Quad + per-material `_Billboard` flag rotates to camera. Enables iso perspective without 3D rotation cost.

5. **RushTank `RushTankPhaseBanner`** — transient HUDCanvas overlay watching ECS `RunPhase` singleton. Fades in title + instruction line on phase change. Solves P0-C2 ("phase-intro feedback") universally.

## Anti-patterns observed

| Demo | Anti-pattern | Impact |
|---|---|---|
| RushTank (pre-fix) | "user wires refs in Inspector" comment + null SerializeFields | Demo appeared completely broken — only a grey quad on brown background. Fixed 2026-05-19. |
| RushTank (pre-fix) | `Image` + `Text` on same GameObject via `typeof()` | Unity NullReferenceException at scene setup. Fixed 2026-05-19. |
| BattleDemo2D | Extremely heavy authoring (boss phases + inventory + loot + progression + summons + stealth) all in one prefab | Hard to maintain — easy to miss a config field. Split into focused authoring components per concern. |

## Materials inventory (count)

| Demo | Material count | Folder structure |
|---|---|---|
| RushTank | ~40 (items + enemies + projectiles + env + tank parts + UI icons) | Flat `Materials/` with category prefix |
| BackpackCrawler | ~14 (item prefabs + corridor textures) | Flat `Materials/` |
| InventoryDemo | ~12 (one per item) | Flat `Materials/` |
| BattleDemo | ~16 (per-unit + AOE) | Inline `{Name}Mat.mat` |
| BattleDemo2D | ~20+ (unit + projectile + structure + boss) | Flat |
| BattleDemoIso | ~12 (billboard + projectile + AOE) | Flat |
| BattleDemoSideView | ~12 (unit + projectile + AOE) | Flat |

---

## Closing observations

The placeholder pattern is *load-bearing infrastructure* for this project's library-first goal. Every demo proves the library's gameplay loop using primitives so the library code stays game-agnostic. When art lands, the demo's prefab creator is updated to wire real meshes/materials — gameplay code (DOTS systems, authoring) NEVER changes.

This decoupling is the whole point. The skill exists to keep it consistent across future demos.
